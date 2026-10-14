using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Describes endpoints from their metadata. Results are cached per endpoint instance.
/// </summary>
internal sealed class EndpointAuthorizationResolver
{
    private const string RootPagesGroup = "Pages";
    private const string EndpointsGroup = "Endpoints";

    private readonly ConditionalWeakTable<Endpoint, EndpointAuthorizationInfo> _cache = new();
    private readonly ConditionalWeakTable<Endpoint, EndpointAuthorizationInfo>.CreateValueCallback _factory;
    private readonly IOptionsMonitor<DynamicAuthorizationOptions> _options;

    public EndpointAuthorizationResolver(IOptionsMonitor<DynamicAuthorizationOptions> options)
    {
        _options = options;
        _factory = Create;
    }

    public EndpointAuthorizationInfo Describe(Endpoint endpoint) => _cache.GetValue(endpoint, _factory);

    private EndpointAuthorizationInfo Create(Endpoint endpoint)
    {
        EndpointMetadataCollection metadata = endpoint.Metadata;
        string[] httpMethods = metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.ToArray() ?? Array.Empty<string>();
        PermissionSource source;
        string? area;
        string targetKey;
        string group;
        string displayName;
        string route;

        if (metadata.GetMetadata<ControllerActionDescriptor>() is { } action)
        {
            source = PermissionSource.Controller;
            area = GetArea(action.RouteValues);
            targetKey = PermissionKeys.ForAction(area, action.ControllerName, action.ActionName);
            group = action.ControllerTypeInfo.GetCustomAttribute<PermissionGroupAttribute>()?.Name
                ?? WithArea(area, action.ControllerTypeInfo.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? action.ControllerName);
            displayName = action.MethodInfo.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? action.ActionName;
            route = action.AttributeRouteInfo?.Template is { } template
                ? PermissionKeys.NormalizeRoute(template)
                : "/" + string.Join('/', new[] { area, action.ControllerName, action.ActionName }.Where(s => !string.IsNullOrEmpty(s)));
        }
        else if (metadata.GetMetadata<PageActionDescriptor>() is { } page)
        {
            source = PermissionSource.Page;
            area = string.IsNullOrEmpty(page.AreaName) ? null : page.AreaName;
            targetKey = PermissionKeys.ForPage(area, page.ViewEnginePath);
            TypeInfo? modelType = (page as CompiledPageActionDescriptor)?.ModelTypeInfo;
            group = (modelType?.GetCustomAttribute<PermissionGroupAttribute>() ?? metadata.GetMetadata<PermissionGroupAttribute>())?.Name
                ?? WithArea(area, GetPageFolder(page.ViewEnginePath));
            displayName = (modelType?.GetCustomAttribute<DisplayNameAttribute>() ?? metadata.GetMetadata<DisplayNameAttribute>())?.DisplayName
                ?? GetPageName(page.ViewEnginePath);
            route = PermissionKeys.NormalizeRoute(page.AttributeRouteInfo?.Template ?? (endpoint as RouteEndpoint)?.RoutePattern.RawText);
        }
        else
        {
            source = PermissionSource.Endpoint;
            area = null;
            route = PermissionKeys.NormalizeRoute((endpoint as RouteEndpoint)?.RoutePattern.RawText ?? endpoint.DisplayName);
            string? endpointName = metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
            targetKey = endpointName is null ? PermissionKeys.ForRoute(httpMethods, route) : PermissionKeys.ForEndpointName(endpointName);
            group = metadata.GetMetadata<ITagsMetadata>()?.Tags.FirstOrDefault() ?? EndpointsGroup;
            displayName = metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary
                ?? endpointName
                ?? $"{(httpMethods.Length == 0 ? "ANY" : string.Join(", ", httpMethods))} {route}";
        }

        IPermissionMetadata? explicitPermission = metadata.GetMetadata<IPermissionMetadata>();
        EndpointProtection protection = GetProtection(metadata, explicitPermission, area, _options.CurrentValue);
        PermissionDescriptor? permission = protection != EndpointProtection.DynamicPermission
            ? null
            : explicitPermission is null
                ? new PermissionDescriptor(targetKey, displayName, group, source)
                : new PermissionDescriptor(
                    explicitPermission.Permission,
                    explicitPermission.DisplayName ?? displayName,
                    explicitPermission.Group ?? group,
                    source);

        return new EndpointAuthorizationInfo(endpoint, displayName, group, route, httpMethods, source, area, protection, targetKey, permission);
    }

    private static EndpointProtection GetProtection(
        EndpointMetadataCollection metadata,
        IPermissionMetadata? explicitPermission,
        string? area,
        DynamicAuthorizationOptions options)
    {
        // Mirrors the authorization middleware: [AllowAnonymous] anywhere wins.
        if (metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return EndpointProtection.AllowAnonymous;
        }

        IReadOnlyList<IAuthorizeData> authorizeData = metadata.GetOrderedMetadata<IAuthorizeData>();
        bool hasPolicies = metadata.GetMetadata<AuthorizationPolicy>() is not null
            || metadata.GetMetadata<IAuthorizationRequirementData>() is not null;
        if (authorizeData.Count == 0 && !hasPolicies)
        {
            return EndpointProtection.None;
        }

        if (explicitPermission is not null)
        {
            return EndpointProtection.DynamicPermission;
        }

        // [Authorize] without a policy or roles (schemes only is fine) is evaluated with the default policy.
        bool usesDynamicPolicy = authorizeData.Any(d => string.Equals(d.Policy, DynamicAuthorizationDefaults.PolicyName, StringComparison.Ordinal))
            || (options.ApplyToDefaultPolicy && authorizeData.Any(d => string.IsNullOrWhiteSpace(d.Policy) && string.IsNullOrWhiteSpace(d.Roles)));
        if (!usesDynamicPolicy)
        {
            return EndpointProtection.PolicyOnly;
        }

        if (metadata.GetMetadata<IIgnoreDynamicAuthorizationMetadata>() is not null
            || (area is not null && options.IgnoredAreas.Contains(area)))
        {
            return EndpointProtection.Excluded;
        }

        return EndpointProtection.DynamicPermission;
    }

    private static string? GetArea(IDictionary<string, string?> routeValues) =>
        routeValues.TryGetValue("area", out string? area) && !string.IsNullOrEmpty(area) ? area : null;

    private static string WithArea(string? area, string name) => area is null ? name : $"{area} / {name}";

    private static string GetPageFolder(string viewEnginePath)
    {
        string[] segments = viewEnginePath.Trim('/').Split('/');

        return segments.Length <= 1 ? RootPagesGroup : string.Join(" / ", segments[..^1]);
    }

    private static string GetPageName(string viewEnginePath) => viewEnginePath[(viewEnginePath.LastIndexOf('/') + 1)..];
}
