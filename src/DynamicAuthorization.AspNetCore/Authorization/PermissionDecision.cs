namespace DynamicAuthorization.AspNetCore;

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
    SuperAdminUser
}
