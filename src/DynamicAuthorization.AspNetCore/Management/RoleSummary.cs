namespace DynamicAuthorization.AspNetCore.Management;

/// <summary>
///   A role with its member and permission counts.
/// </summary>
/// <param name="Id">The role identifier.</param>
/// <param name="Name">The role name.</param>
/// <param name="UserCount">The number of users in the role.</param>
/// <param name="PermissionCount">The number of permissions granted to the role.</param>
public sealed record RoleSummary(string Id, string Name, int UserCount, int PermissionCount);
