namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Keeps the roles of users and the permissions of roles between permission checks. The default keeps them in the
///   memory of the instance for <see cref="DynamicAuthorizationOptions.CacheDuration"/>; register another
///   implementation, for example the one on <c>HybridCache</c>, so that several instances share one cache and an
///   invalidation on one of them reaches the others.
/// </summary>
/// <remarks>
///   <para>
///     Permission checks rely on this contract, which the default implementation provides: concurrent reads of a key
///     that is not cached share one call of the load; invalidating a key while its load runs discards the result of
///     that load, so data read before a change is never kept after it; a failed load is not kept, so the next read
///     retries it; and a load runs to completion when the caller that started it is cancelled, so that the other
///     callers waiting for it are served.
///   </para>
///   <para>
///     Loads run in a service scope of their own, outside any ambient transaction, before they reach the store, so an
///     implementation only caches. Every read receives a load it can call; it is never called when caching is disabled.
///   </para>
/// </remarks>
public interface IDynamicAuthorizationCacheStore
{
    /// <summary>
    ///   Gets the roles of a user, loading them with <paramref name="load"/> when they are not cached.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="load">Loads the roles of the user.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The roles of the user and when the cached entry expires.</returns>
    ValueTask<CachedValue<IReadOnlyList<RoleReference>>> GetUserRolesAsync(
        string userId,
        Func<CancellationToken, Task<IReadOnlyList<RoleReference>>> load,
        CancellationToken cancellationToken);

    /// <summary>
    ///   Gets the permissions of roles, loading those that are not cached with <paramref name="load"/>, which loads
    ///   the roles it is given with one store call.
    /// </summary>
    /// <param name="roleIds">The role identifiers.</param>
    /// <param name="load">
    ///   Loads the permissions of the given roles, keyed by role identifier; a role without permissions maps to an
    ///   empty set.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    ///   The permissions of every role in <paramref name="roleIds"/>, keyed by role identifier, and when the first of
    ///   the cached entries expires.
    /// </returns>
    ValueTask<CachedValue<IReadOnlyDictionary<string, IReadOnlySet<string>>>> GetRolePermissionsAsync(
        IReadOnlyList<string> roleIds,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, IReadOnlySet<string>>>> load,
        CancellationToken cancellationToken);

    /// <summary>
    ///   Removes the cached roles of a user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask InvalidateUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    ///   Removes the cached permissions of a role.
    /// </summary>
    /// <param name="roleId">The role identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask InvalidateRoleAsync(string roleId, CancellationToken cancellationToken);

    /// <summary>
    ///   Removes every cached entry.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask InvalidateAllAsync(CancellationToken cancellationToken);
}

/// <summary>
///   A value read from the cache and when the entry it was read from expires, so that a caller keeping the value does
///   not keep it longer than the cache would.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <param name="Value">The value.</param>
/// <param name="ExpiresAt">When the entry the value was read from expires.</param>
public readonly record struct CachedValue<T>(T Value, DateTimeOffset ExpiresAt);
