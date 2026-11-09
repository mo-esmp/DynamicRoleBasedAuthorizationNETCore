namespace DynamicAuthorization.AspNetCore;

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

    /// <summary>
    ///   Creates an evaluation.
    /// </summary>
    /// <param name="permission">The permission key.</param>
    /// <param name="decision">The outcome.</param>
    /// <param name="grantingRoles">The roles that grant the permission, or the super admin role.</param>
    /// <returns>The evaluation.</returns>
    public static PermissionEvaluation Create(string permission, PermissionDecision decision, IReadOnlyList<RoleReference> grantingRoles) =>
        new(permission, decision, grantingRoles);
}
