using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DynamicAuthorization.AspNetCore;

internal sealed partial class DynamicPermissionHandler(
    IPermissionChecker permissionChecker,
    EndpointAuthorizationResolver resolver,
    ILogger<DynamicPermissionHandler> logger) : AuthorizationHandler<DynamicPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, DynamicPermissionRequirement requirement)
    {
        (bool applicable, string? permission) = requirement.Permission is { } required
            ? (true, required)
            : ResolvePermission(context.Resource);

        if (!applicable)
        {
            context.Succeed(requirement);
            return;
        }

        if (permission is null)
        {
            LogUnresolvedPermission(logger, context.Resource?.GetType().Name ?? "null");
            return;
        }

        CancellationToken cancellationToken = context.Resource is HttpContext httpContext ? httpContext.RequestAborted : default;
        PermissionEvaluation evaluation = await permissionChecker.EvaluateAsync(context.User, permission, cancellationToken).ConfigureAwait(false);
        if (evaluation.IsGranted)
        {
            LogPermissionGranted(logger, permission, context.User.Identity?.Name, evaluation.Decision);
            context.Succeed(requirement);
        }
        else
        {
            LogPermissionDenied(logger, permission, context.User.Identity?.Name);
        }
    }

    /// <summary>
    ///   Resolves the permission to check for an authorization resource.
    /// </summary>
    /// <returns>
    ///   Whether the dynamic check applies to the resource and, if so, the permission to check. A <see langword="null"/>
    ///   permission for an applicable resource denies access.
    /// </returns>
    private (bool Applicable, string? Permission) ResolvePermission(object? resource)
    {
        Endpoint? endpoint = resource switch
        {
            HttpContext httpContext => httpContext.GetEndpoint(),
            FilterContext filterContext => filterContext.HttpContext.GetEndpoint(),
            Endpoint e => e,
            _ => null,
        };

        if (endpoint is not null)
        {
            EndpointAuthorizationInfo info = resolver.Describe(endpoint);
            return info.Protection == EndpointProtection.Excluded
                ? (false, null)
                : (true, info.Permission?.Permission ?? info.TargetKey);
        }

        return resource switch
        {
            string permission => (true, permission),

            // An HTTP request without an endpoint cannot be mapped to a permission: deny.
            HttpContext or FilterContext => (true, null),

            // Hub methods are authorized per connection; only an explicit permission on the method is checked.
            HubInvocationContext invocation => invocation.HubMethod.GetCustomAttribute<PermissionAttribute>() is { } attribute
                ? (true, attribute.Permission)
                : (false, null),

            // Authorization without a resource (for example <AuthorizeView> or IAuthorizationService calls) has no
            // endpoint to check; the rest of the policy, such as requiring an authenticated user, still applies.
            _ => (false, null),
        };
    }

    [LoggerMessage(1, LogLevel.Debug, "Permission '{Permission}' was granted to '{UserName}' ({Decision}).")]
    private static partial void LogPermissionGranted(ILogger logger, string permission, string? userName, PermissionDecision decision);

    [LoggerMessage(2, LogLevel.Information, "Permission '{Permission}' was denied for '{UserName}'.")]
    private static partial void LogPermissionDenied(ILogger logger, string permission, string? userName);

    [LoggerMessage(3, LogLevel.Warning, "No permission could be resolved for the authorization resource '{ResourceType}'. Access is denied.")]
    private static partial void LogUnresolvedPermission(ILogger logger, string resourceType);
}
