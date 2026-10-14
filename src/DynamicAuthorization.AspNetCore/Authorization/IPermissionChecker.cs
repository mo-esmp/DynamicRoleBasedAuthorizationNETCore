using System.Security.Claims;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Checks whether users have been granted permissions.
/// </summary>
public interface IPermissionChecker
{
    /// <summary>
    ///   Determines whether a user has been granted a permission.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="permission">The permission key.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the permission is granted; otherwise, <see langword="false"/>.</returns>
    Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default);

    /// <summary>
    ///   Evaluates whether a user has been granted a permission, and why.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="permission">The permission key.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The evaluation.</returns>
    Task<PermissionEvaluation> EvaluateAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default);

    /// <summary>
    ///   Evaluates whether a stored user has been granted a permission, and why, without a signed-in principal.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="userName">The user name, used to recognize <see cref="DynamicAuthorizationOptions.SuperAdminUsers"/>.</param>
    /// <param name="permission">The permission key.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The evaluation.</returns>
    Task<PermissionEvaluation> EvaluateUserAsync(string userId, string? userName, string permission, CancellationToken cancellationToken = default);
}

/// <summary>
///   The outcome of a permission check.
/// </summary>
public enum PermissionDecision
{
    /// <summary>The user is not authenticated.</summary>
    NotAuthenticated,

    /// <summary>None of the user's roles grants the permission.</summary>
    NotGranted,

    /// <summary>One or more of the user's roles grant the permission.</summary>
    GrantedByRole,

    /// <summary>The user is a member of a role listed in <see cref="DynamicAuthorizationOptions.SuperAdminRoles"/>.</summary>
    SuperAdminRole,

    /// <summary>The user is listed in <see cref="DynamicAuthorizationOptions.SuperAdminUsers"/>.</summary>
    SuperAdminUser,
}

/// <summary>
///   The result of evaluating a permission for a user.
/// </summary>
/// <param name="Permission">The permission key.</param>
/// <param name="Decision">The outcome.</param>
/// <param name="GrantingRoles">The roles that grant the permission, or the super admin role.</param>
public sealed record PermissionEvaluation(string Permission, PermissionDecision Decision, IReadOnlyList<RoleReference> GrantingRoles)
{
    /// <summary>
    ///   Gets the granted permission that implies a <c>{resource}.read</c> permission, when the read permission itself
    ///   is not granted, for example <c>orders.update</c> for <c>orders.read</c>.
    /// </summary>
    /// <seealso cref="DynamicAuthorizationOptions.OperationsImplyRead"/>
    public string? ImpliedBy { get; init; }

    /// <summary>
    ///   Gets a value indicating whether the permission is granted.
    /// </summary>
    public bool IsGranted => Decision is PermissionDecision.GrantedByRole or PermissionDecision.SuperAdminRole or PermissionDecision.SuperAdminUser;
}
