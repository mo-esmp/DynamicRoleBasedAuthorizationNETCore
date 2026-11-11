namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Default values used by dynamic authorization.
/// </summary>
public static class DynamicAuthorizationDefaults
{
    /// <summary>
    ///   The name of the authorization policy that requires an authenticated user who has been granted the
    ///   permission of the endpoint being accessed.
    /// </summary>
    public const string PolicyName = "DynamicAuthorization";
}
