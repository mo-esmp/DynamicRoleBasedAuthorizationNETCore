using System.Security.Claims;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Resolves the roles of a user.
/// </summary>
public interface IUserRoleProvider
{
    /// <summary>
    ///   Gets the identifier of the user represented by a principal.
    /// </summary>
    /// <param name="principal">The principal.</param>
    /// <returns>The user identifier, or <see langword="null"/> when the principal has none.</returns>
    string? GetUserId(ClaimsPrincipal principal);

    /// <summary>
    ///   Gets the roles a user is a member of.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The roles of the user.</returns>
    Task<IReadOnlyList<RoleReference>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>
///   Identifies a role.
/// </summary>
/// <param name="Id">The role identifier.</param>
/// <param name="Name">The role name.</param>
public sealed record RoleReference(string Id, string Name);
