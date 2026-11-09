using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace DynamicAuthorization.AspNetCore.TagHelpers;

/// <summary>
///   Combines the authorization policy of an endpoint the way the authorization middleware does, and caches it per
///   endpoint when the policy provider allows caching policies, as the middleware does too. Without it, every
///   <c>secure-content</c> tag that targets a page or action would rebuild the policy of its target.
/// </summary>
internal sealed class EndpointPolicyCache(IAuthorizationPolicyProvider policyProvider)
{
    private readonly ConditionalWeakTable<Endpoint, CachedPolicy> _policies = [];

    /// <summary>
    ///   Gets the complete policy of an endpoint: its authorize data and policy instances, its requirement data and,
    ///   without any of those, the fallback policy.
    /// </summary>
    /// <returns>The policy, or <see langword="null"/> when the endpoint has none.</returns>
    public async ValueTask<AuthorizationPolicy?> GetPolicyAsync(Endpoint endpoint)
    {
        bool cacheable = policyProvider.AllowsCachingPolicies;
        if (cacheable && _policies.TryGetValue(endpoint, out CachedPolicy? cached))
        {
            return cached.Policy;
        }

        AuthorizationPolicy? policy = await CombineAsync(endpoint.Metadata).ConfigureAwait(false);
        if (cacheable)
        {
            _policies.AddOrUpdate(endpoint, new CachedPolicy(policy));
        }

        return policy;
    }

    private async Task<AuthorizationPolicy?> CombineAsync(EndpointMetadataCollection metadata)
    {
        // CombineAsync applies the default policy to authorize data without a policy or roles, and the fallback policy
        // when there is no authorize data and no policy instance.
        AuthorizationPolicy? policy = await AuthorizationPolicy.CombineAsync(
            policyProvider,
            metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata.GetOrderedMetadata<AuthorizationPolicy>()).ConfigureAwait(false);

        IReadOnlyList<IAuthorizationRequirementData> requirementData = metadata.GetOrderedMetadata<IAuthorizationRequirementData>();
        if (requirementData.Count > 0)
        {
            AuthorizationPolicyBuilder builder = new();
            foreach (IAuthorizationRequirementData data in requirementData)
            {
                builder.AddRequirements([.. data.GetRequirements()]);
            }

            policy = policy is null ? builder.Build() : AuthorizationPolicy.Combine(policy, builder.Build());
        }

        return policy;
    }

    // Wrapped so that endpoints without a policy are cached as well.
    private sealed record CachedPolicy(AuthorizationPolicy? Policy);
}
