using System.Runtime.CompilerServices;
using System.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Reads user roles and role permissions for permission checks through the registered
///   <see cref="IDynamicAuthorizationCacheStore"/>, and invalidates them. Owns what the store does not: the loads,
///   which run in a service scope of their own, outside any ambient transaction, so that they never depend on the
///   request that started them and read committed data only; the index of each role's permissions by resource, built
///   once per cached set; and the version that lets a scope know whether anything was invalidated since it last read.
///   When caching is disabled, every read goes to the store of record.
/// </summary>
internal sealed class DynamicAuthorizationCache(
    IDynamicAuthorizationCacheStore store,
    IServiceScopeFactory scopeFactory,
    IOptions<DynamicAuthorizationOptions> options) : IDynamicAuthorizationCache
{
    // The sets a cache keeps are the same instances across reads, so their index is built once per cached set and
    // collected with it. The sets are copies made by the load, so a store that returns a set it keeps and changes
    // cannot make an index outdated.
    private readonly ConditionalWeakTable<IReadOnlySet<string>, PermissionSet> _indexes = [];
    private long _version;

    /// <summary>
    ///   Gets a number that changes whenever an entry is invalidated through this instance. Callers that keep results
    ///   for the duration of a request compare it to know whether a change has been made since.
    /// </summary>
    public long Version => Volatile.Read(ref _version);

    private bool Enabled => options.Value.CacheDuration > TimeSpan.Zero;

    /// <summary>
    ///   Gets the roles of a user.
    /// </summary>
    public async ValueTask<CachedValue<IReadOnlyList<RoleReference>>> GetUserRolesAsync(string userId, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return new CachedValue<IReadOnlyList<RoleReference>>(await LoadRolesAsync(userId, cancellationToken).ConfigureAwait(false), DateTimeOffset.UtcNow);
        }

        return await store.GetUserRolesAsync(userId, ct => LoadRolesAsync(userId, ct), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///   Gets the permissions of roles, in the order of <paramref name="roleIds"/>, indexed by resource. Roles that
    ///   are not cached are loaded together. The result expires when the first of its entries does.
    /// </summary>
    public async ValueTask<CachedValue<IReadOnlyList<PermissionSet>>> GetRolePermissionsAsync(IReadOnlyList<string> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
        {
            return new CachedValue<IReadOnlyList<PermissionSet>>([], DateTimeOffset.MaxValue);
        }

        CachedValue<IReadOnlyDictionary<string, IReadOnlySet<string>>> cached = Enabled
            ? await store.GetRolePermissionsAsync(roleIds, LoadPermissionsAsync, cancellationToken).ConfigureAwait(false)
            : new CachedValue<IReadOnlyDictionary<string, IReadOnlySet<string>>>(await LoadPermissionsAsync(roleIds, cancellationToken).ConfigureAwait(false), DateTimeOffset.UtcNow);

        PermissionSet[] permissions = new PermissionSet[roleIds.Count];
        for (int i = 0; i < roleIds.Count; i++)
        {
            permissions[i] = cached.Value.TryGetValue(roleIds[i], out IReadOnlySet<string>? granted) && granted.Count > 0
                ? _indexes.GetValue(granted, static g => new PermissionSet(g))
                : PermissionSet.Empty;
        }

        return new CachedValue<IReadOnlyList<PermissionSet>>(permissions, cached.ExpiresAt);
    }

    public async ValueTask InvalidateUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        await store.InvalidateUserAsync(userId, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _version);
    }

    public async ValueTask InvalidateRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        await store.InvalidateRoleAsync(roleId, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _version);
    }

    public async ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        await store.InvalidateAllAsync(cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _version);
    }

    private Task<IReadOnlyList<RoleReference>> LoadRolesAsync(string userId, CancellationToken cancellationToken) =>
        ExecuteAsync(services => services.GetRequiredService<IUserRoleProvider>().GetUserRolesAsync(userId, cancellationToken), cancellationToken);

    /// <summary>
    ///   Loads the permissions of roles from the store of record with one call. Roles the store leaves out have no
    ///   permissions. The sets are copied, so that what is cached and indexed belongs to this load even when the
    ///   store returns a set it keeps and changes, and compares case-insensitively as the contract says.
    /// </summary>
    private Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> LoadPermissionsAsync(IReadOnlyList<string> roleIds, CancellationToken cancellationToken) =>
        ExecuteAsync<IReadOnlyDictionary<string, IReadOnlySet<string>>>(async services =>
        {
            IReadOnlyDictionary<string, IReadOnlySet<string>> loaded = await services.GetRequiredService<IPermissionStore>()
                .GetPermissionsAsync(roleIds, cancellationToken)
                .ConfigureAwait(false);

            Dictionary<string, IReadOnlySet<string>> permissions = new(roleIds.Count, StringComparer.Ordinal);
            foreach (string roleId in roleIds)
            {
                permissions[roleId] = loaded.TryGetValue(roleId, out IReadOnlySet<string>? granted) && granted.Count > 0
                    ? new HashSet<string>(granted, StringComparer.OrdinalIgnoreCase)
                    : s_empty;
            }

            return permissions;
        }, cancellationToken);

    /// <summary>
    ///   Runs a load with services of its own, so that it does not depend on the scope of the request that started it,
    ///   and outside any ambient transaction, so that it reads committed data only.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(Func<IServiceProvider, Task<T>> load, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using TransactionScope suppress = new(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        return await load(scope.ServiceProvider).ConfigureAwait(false);
    }

    private static readonly IReadOnlySet<string> s_empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
