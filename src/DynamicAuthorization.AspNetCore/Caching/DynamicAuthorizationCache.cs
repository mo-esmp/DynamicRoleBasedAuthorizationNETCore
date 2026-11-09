using System.Transactions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Invalidates cached role memberships and role permissions.
/// </summary>
/// <remarks>
///   The management services invalidate the cache automatically. Call these methods when roles or permissions are
///   changed by other means, for example with <c>UserManager.AddToRoleAsync</c>. Invalidating an entry that is being
///   loaded discards the result of that load, so data read before a change is never cached after it.
/// </remarks>
public interface IDynamicAuthorizationCache
{
    /// <summary>
    ///   Removes the cached roles of a user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    void InvalidateUser(string userId);

    /// <summary>
    ///   Removes the cached permissions of a role.
    /// </summary>
    /// <param name="roleId">The role identifier.</param>
    void InvalidateRole(string roleId);

    /// <summary>
    ///   Removes every cached entry.
    /// </summary>
    void InvalidateAll();
}

/// <summary>
///   Caches user roles and role permissions in memory. Pending loads are cached too, so that concurrent requests for
///   the same key share one load, and an invalidation during a load removes the entry before the result is published.
///   Loads run in a service scope of their own, outside any ambient transaction: they never depend on the request
///   that started them, which may end first, and they read committed data only.
/// </summary>
internal sealed class DynamicAuthorizationCache : IDynamicAuthorizationCache, IDisposable
{
    // Entry creation is serialized per key (striped) so that concurrent misses share a single load.
    private const int LockCount = 32;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly Lock[] _locks = [.. Enumerable.Range(0, LockCount).Select(_ => new Lock())];
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<DynamicAuthorizationOptions> _options;
    private CancellationTokenSource _generation = new();

    public DynamicAuthorizationCache(IServiceScopeFactory scopeFactory, IOptionsMonitor<DynamicAuthorizationOptions> options)
    {
        _scopeFactory = scopeFactory;
        _options = options;
    }

    /// <summary>
    ///   Gets the roles of a user, loading them with <paramref name="load"/> when they are not cached. The load
    ///   receives a service provider of its own.
    /// </summary>
    public Task<IReadOnlyList<RoleReference>> GetUserRolesAsync(
        string userId,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<RoleReference>>> load,
        CancellationToken cancellationToken) =>
        GetOrCreateAsync(UserKey(userId), load, cancellationToken);

    /// <summary>
    ///   Gets the permissions of a role, loading them with <paramref name="load"/> when they are not cached. The load
    ///   receives a service provider of its own.
    /// </summary>
    public Task<IReadOnlySet<string>> GetRolePermissionsAsync(
        string roleId,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlySet<string>>> load,
        CancellationToken cancellationToken) =>
        GetOrCreateAsync(RoleKey(roleId), load, cancellationToken);

    public void InvalidateUser(string userId) => _cache.Remove(UserKey(userId));

    public void InvalidateRole(string roleId) => _cache.Remove(RoleKey(roleId));

    public void InvalidateAll()
    {
        // Entries are bound to the current generation; cancelling it expires all of them at once.
        CancellationTokenSource previous = Interlocked.Exchange(ref _generation, new CancellationTokenSource());
        previous.Cancel();
    }

    public void Dispose()
    {
        _cache.Dispose();
        _generation.Dispose();
    }

    private async Task<T> GetOrCreateAsync<T>(string key, Func<IServiceProvider, CancellationToken, Task<T>> load, CancellationToken cancellationToken)
        where T : class
    {
        TimeSpan duration = _options.CurrentValue.CacheDuration;
        if (duration <= TimeSpan.Zero)
        {
            return await ExecuteAsync(load, cancellationToken).ConfigureAwait(false);
        }

        Lazy<Task<T>> pending = GetOrAddLoad(key, load, duration);

        // The load is shared with other callers and runs to completion even when this caller is cancelled.
        return await pending.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private Lazy<Task<T>> GetOrAddLoad<T>(string key, Func<IServiceProvider, CancellationToken, Task<T>> load, TimeSpan duration)
        where T : class
    {
        lock (_locks[(key.GetHashCode(StringComparison.Ordinal) & int.MaxValue) % LockCount])
        {
            if (_cache.TryGetValue(key, out Lazy<Task<T>>? cached) && cached is not null)
            {
                return cached;
            }

            // The pending load is cached before it starts: invalidating the key while it runs removes this entry, so
            // the result reaches the callers waiting for it but is not kept.
            Lazy<Task<T>> pending = null!;
            pending = new Lazy<Task<T>>(() => LoadAsync(key, pending, load), LazyThreadSafetyMode.ExecutionAndPublication);

            MemoryCacheEntryOptions entryOptions = new() { AbsoluteExpirationRelativeToNow = duration };
            entryOptions.ExpirationTokens.Add(new CancellationChangeToken(Volatile.Read(ref _generation).Token));
            _cache.Set(key, pending, entryOptions);

            return pending;
        }
    }

    private async Task<T> LoadAsync<T>(string key, Lazy<Task<T>> pending, Func<IServiceProvider, CancellationToken, Task<T>> load)
        where T : class
    {
        try
        {
            return await ExecuteAsync(load, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Failures are not cached: the next request retries the load.
            if (_cache.TryGetValue(key, out Lazy<Task<T>>? current) && ReferenceEquals(current, pending))
            {
                _cache.Remove(key);
            }

            throw;
        }
    }

    /// <summary>
    ///   Runs a load with services of its own, so that it does not depend on the scope of the request that started it,
    ///   and outside any ambient transaction, so that it reads committed data only.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(Func<IServiceProvider, CancellationToken, Task<T>> load, CancellationToken cancellationToken)
    {
        using TransactionScope suppress = new(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

        return await load(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
    }

    private static string UserKey(string userId) => "user:" + userId;

    private static string RoleKey(string roleId) => "role:" + roleId;
}
