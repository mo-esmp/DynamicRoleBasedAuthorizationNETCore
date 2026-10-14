using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Invalidates cached role memberships and role permissions.
/// </summary>
/// <remarks>
///   The management services invalidate the cache automatically. Call these methods when roles or permissions are
///   changed by other means, for example with <c>UserManager.AddToRoleAsync</c>.
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

internal sealed class DynamicAuthorizationCache : IDynamicAuthorizationCache, IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly IOptionsMonitor<DynamicAuthorizationOptions> _options;
    private CancellationTokenSource _generation = new();

    public DynamicAuthorizationCache(IOptionsMonitor<DynamicAuthorizationOptions> options) => _options = options;

    public Task<IReadOnlyList<RoleReference>> GetUserRolesAsync(
        string userId,
        Func<CancellationToken, Task<IReadOnlyList<RoleReference>>> factory,
        CancellationToken cancellationToken) =>
        GetOrCreateAsync(UserKey(userId), factory, cancellationToken);

    public Task<IReadOnlySet<string>> GetRolePermissionsAsync(
        string roleId,
        Func<CancellationToken, Task<IReadOnlySet<string>>> factory,
        CancellationToken cancellationToken) =>
        GetOrCreateAsync(RoleKey(roleId), factory, cancellationToken);

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

    private async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
        where T : class
    {
        TimeSpan duration = _options.CurrentValue.CacheDuration;
        if (duration <= TimeSpan.Zero)
        {
            return await factory(cancellationToken).ConfigureAwait(false);
        }

        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        // Capture the generation before loading so that an invalidation during the load expires the new entry.
        CancellationToken generation = Volatile.Read(ref _generation).Token;
        T value = await factory(cancellationToken).ConfigureAwait(false);

        MemoryCacheEntryOptions entryOptions = new() { AbsoluteExpirationRelativeToNow = duration };
        entryOptions.ExpirationTokens.Add(new CancellationChangeToken(generation));
        _cache.Set(key, value, entryOptions);

        return value;
    }

    private static string UserKey(string userId) => "user:" + userId;

    private static string RoleKey(string roleId) => "role:" + roleId;
}
