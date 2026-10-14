using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Registers the dynamic authorization policy and, optionally, adds the dynamic permission requirement to the
///   default policy. Runs as a post-configuration so it builds on the application's own default policy.
/// </summary>
internal sealed class ConfigureAuthorizationOptions(IOptions<DynamicAuthorizationOptions> options) : IPostConfigureOptions<AuthorizationOptions>
{
    public void PostConfigure(string? name, AuthorizationOptions authorizationOptions)
    {
        if (authorizationOptions.GetPolicy(DynamicAuthorizationDefaults.PolicyName) is null)
        {
            authorizationOptions.AddPolicy(
                DynamicAuthorizationDefaults.PolicyName,
                policy => policy.RequireAuthenticatedUser().AddRequirements(new DynamicPermissionRequirement()));
        }

        if (options.Value.ApplyToDefaultPolicy)
        {
            authorizationOptions.DefaultPolicy = new AuthorizationPolicyBuilder()
                .Combine(authorizationOptions.DefaultPolicy)
                .AddRequirements(new DynamicPermissionRequirement())
                .Build();
        }
    }
}
