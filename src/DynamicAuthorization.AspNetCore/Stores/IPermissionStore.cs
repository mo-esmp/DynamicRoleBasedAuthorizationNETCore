namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Stores the permissions granted to roles.
/// </summary>
public interface IPermissionStore
{
    /// <summary>
    ///   Gets the permissions granted to a role.
    /// </summary>
    /// <param name="roleId">The role identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The granted permission keys, compared case-insensitively.</returns>
    Task<IReadOnlySet<string>> GetPermissionsAsync(string roleId, CancellationToken cancellationToken = default);

    /// <summary>
    ///   Replaces the permissions granted to a role.
    /// </summary>
    /// <param name="roleId">The role identifier.</param>
    /// <param name="permissions">The permission keys to grant. Permissions not in this list are revoked.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The permissions that were granted and revoked.</returns>
    Task<PermissionChangeSet> SetPermissionsAsync(string roleId, IEnumerable<string> permissions, CancellationToken cancellationToken = default);
}

/// <summary>
///   The permissions granted and revoked by a change.
/// </summary>
/// <param name="Added">The permissions that were granted.</param>
/// <param name="Removed">The permissions that were revoked.</param>
public sealed record PermissionChangeSet(IReadOnlyList<string> Added, IReadOnlyList<string> Removed)
{
    /// <summary>
    ///   Gets a change set without changes.
    /// </summary>
    public static PermissionChangeSet Empty { get; } = new([], []);

    /// <summary>
    ///   Gets a value indicating whether any permission was granted or revoked.
    /// </summary>
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
}
