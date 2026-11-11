namespace DynamicAuthorization.AspNetCore.Management;

/// <summary>
///   Manages roles and the permissions granted to them.
/// </summary>
public interface IRoleManagementService
{
    /// <summary>
    ///   Gets all roles, ordered by name.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The roles.</returns>
    Task<IReadOnlyList<RoleSummary>> GetRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///   Finds a role and its permissions.
    /// </summary>
    /// <param name="roleId">The role identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The role, or <see langword="null"/> when it does not exist.</returns>
    Task<RoleDetails?> FindRoleAsync(string roleId, CancellationToken cancellationToken = default);

    /// <summary>
    ///   Gets every permission granted to every role.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The grants.</returns>
    Task<IReadOnlyList<RoleGrant>> GetGrantsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///   Creates a role and grants it permissions.
    /// </summary>
    /// <param name="name">The role name.</param>
    /// <param name="permissions">The permissions to grant.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result, with the identifier of the new role when it succeeded.</returns>
    Task<ManagementResult<string>> CreateRoleAsync(string name, IEnumerable<string> permissions, CancellationToken cancellationToken = default);

    /// <summary>
    ///   Renames a role and replaces its permissions.
    /// </summary>
    /// <param name="roleId">The role identifier.</param>
    /// <param name="name">The role name.</param>
    /// <param name="permissions">The permissions to grant. Permissions not in this list are revoked.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result.</returns>
    Task<ManagementResult> UpdateRoleAsync(string roleId, string name, IEnumerable<string> permissions, CancellationToken cancellationToken = default);

    /// <summary>
    ///   Deletes a role and its permissions.
    /// </summary>
    /// <param name="roleId">The role identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result.</returns>
    Task<ManagementResult> DeleteRoleAsync(string roleId, CancellationToken cancellationToken = default);
}

/// <summary>
///   A role with its member and permission counts.
/// </summary>
/// <param name="Id">The role identifier.</param>
/// <param name="Name">The role name.</param>
/// <param name="UserCount">The number of users in the role.</param>
/// <param name="PermissionCount">The number of permissions granted to the role.</param>
public sealed record RoleSummary(string Id, string Name, int UserCount, int PermissionCount);

/// <summary>
///   A role and its permissions.
/// </summary>
/// <param name="Id">The role identifier.</param>
/// <param name="Name">The role name.</param>
/// <param name="Permissions">The permissions granted to the role.</param>
public sealed record RoleDetails(string Id, string Name, IReadOnlySet<string> Permissions);

/// <summary>
///   A permission granted to a role.
/// </summary>
/// <param name="RoleId">The role identifier.</param>
/// <param name="RoleName">The role name.</param>
/// <param name="Permission">The permission key.</param>
public sealed record RoleGrant(string RoleId, string RoleName, string Permission);
