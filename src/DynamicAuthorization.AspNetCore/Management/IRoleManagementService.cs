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
    ///   Gets every permission granted to the given roles. The default implementation filters
    ///   <see cref="GetGrantsAsync(CancellationToken)"/>; implement it to filter in the store.
    /// </summary>
    /// <param name="roleIds">The role identifiers. Identifiers that belong to no role are ignored.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The grants, ordered by role name and permission.</returns>
    async Task<IReadOnlyList<RoleGrant>> GetGrantsAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleIds);

        HashSet<string> ids = new(roleIds, StringComparer.Ordinal);
        if (ids.Count == 0)
        {
            return [];
        }

        IReadOnlyList<RoleGrant> grants = await GetGrantsAsync(cancellationToken).ConfigureAwait(false);

        return [.. grants.Where(g => ids.Contains(g.RoleId))];
    }

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
