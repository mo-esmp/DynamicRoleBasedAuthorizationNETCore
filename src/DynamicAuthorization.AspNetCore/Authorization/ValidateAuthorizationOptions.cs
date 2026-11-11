using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Rejects a fallback policy that requires the permission of the endpoint. The fallback policy applies to endpoints
///   without authorization metadata, which have no permission in the catalog: the requirement would deny everyone but
///   super admins, and the dashboard could not grant access. Validated at startup.
/// </summary>
internal sealed class ValidateAuthorizationOptions : IValidateOptions<AuthorizationOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.FallbackPolicy?.Requirements.Any(r => r is DynamicPermissionRequirement { Permission: null }) == true)
        {
            return ValidateOptionsResult.Fail(
                "The fallback authorization policy requires the permission of the endpoint (a DynamicPermissionRequirement without a " +
                "permission), but endpoints without authorization metadata have no permission to grant. Protect them with [Authorize] " +
                "or RequireAuthorization() so that the default policy applies, or require an explicit permission in the fallback " +
                "policy with new DynamicPermissionRequirement(\"...\").");
        }

        return ValidateOptionsResult.Success;
    }
}
