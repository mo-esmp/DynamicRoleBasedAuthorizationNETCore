namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Invalidates cached role memberships and role permissions.
/// </summary>
/// <remarks>
///   The management services invalidate the cache automatically. Call these methods when roles or permissions are
///   changed by other means, for example with <c>UserManager.AddToRoleAsync</c>. Invalidating an entry that is being
///   loaded discards the result of that load, so data read before a change is never cached after it. With a shared
///   cache, see <see cref="IDynamicAuthorizationCacheStore"/>, the invalidation reaches every instance.
/// </remarks>
public interface IDynamicAuthorizationCache
{
    /// <summary>
    ///   Removes the cached roles of a user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask InvalidateUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    ///   Removes the cached permissions of a role.
    /// </summary>
    /// <param name="roleId">The role identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask InvalidateRoleAsync(string roleId, CancellationToken cancellationToken = default);

    /// <summary>
    ///   Removes every cached entry.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default);
}
