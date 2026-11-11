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
    /// <param name="userName">The username, used to recognize <see cref="DynamicAuthorizationOptions.SuperAdminUsers"/>.</param>
    /// <param name="permission">The permission key.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The evaluation.</returns>
    Task<PermissionEvaluation> EvaluateUserAsync(string userId, string? userName, string permission, CancellationToken cancellationToken = default);
}
