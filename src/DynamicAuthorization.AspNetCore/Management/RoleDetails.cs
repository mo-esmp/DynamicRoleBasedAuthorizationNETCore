namespace DynamicAuthorization.AspNetCore.Management;

/// <summary>
///   A role and its permissions.
/// </summary>
/// <param name="Id">The role identifier.</param>
/// <param name="Name">The role name.</param>
/// <param name="Permissions">The permissions granted to the role.</param>
public sealed record RoleDetails(string Id, string Name, IReadOnlySet<string> Permissions);
