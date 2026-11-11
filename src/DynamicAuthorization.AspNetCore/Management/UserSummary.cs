namespace DynamicAuthorization.AspNetCore.Management;

/// <summary>
///   A user and the roles they are a member of.
/// </summary>
/// <param name="Id">The user identifier.</param>
/// <param name="UserName">The username.</param>
/// <param name="Email">The email address.</param>
/// <param name="Roles">The roles of the user.</param>
public sealed record UserSummary(string Id, string UserName, string? Email, IReadOnlyList<RoleReference> Roles);
