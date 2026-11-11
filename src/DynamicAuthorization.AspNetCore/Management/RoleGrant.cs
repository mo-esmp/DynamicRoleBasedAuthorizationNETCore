namespace DynamicAuthorization.AspNetCore.Management;

/// <summary>
///   A permission granted to a role.
/// </summary>
/// <param name="RoleId">The role identifier.</param>
/// <param name="RoleName">The role name.</param>
/// <param name="Permission">The permission key.</param>
public sealed record RoleGrant(string RoleId, string RoleName, string Permission);
