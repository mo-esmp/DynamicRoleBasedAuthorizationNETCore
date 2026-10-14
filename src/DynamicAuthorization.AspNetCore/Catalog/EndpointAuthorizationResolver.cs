using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Describes endpoints from their metadata and derives the permissions they require. Results are cached per
///   endpoint instance.
/// </summary>
internal sealed partial class EndpointAuthorizationResolver
{
    private const string HomeResource = "Home";
    private const string EndpointsResource = "endpoints";
    private const string AnyOperation = "access";

    // How authoritative a resource's display name is when endpoints of the same resource disagree.
    private const int ExplicitGroupPriority = 3;
    private const int NaturalGroupPriority = 2;
    private const int TagGroupPriority = 1;
    private const int BorrowedGroupPriority = 0;

    private readonly ConditionalWeakTable<Endpoint, EndpointDescription> _cache = new();
    private readonly ConditionalWeakTable<Endpoint, EndpointDescription>.CreateValueCallback _factory;
    private readonly IOptionsMonitor<DynamicAuthorizationOptions> _options;

    public EndpointAuthorizationResolver(IOptionsMonitor<DynamicAuthorizationOptions> options)
    {
        _options = options;
        _factory = Create;
    }

    /// <summary>
    ///   Gets the entry that applies when navigating to an endpoint: a GET request without a page handler.
    /// </summary>
    public EndpointAuthorizationInfo Describe(Endpoint endpoint) => _cache.GetValue(endpoint, _factory).Navigation;

    /// <summary>
    ///   Gets every entry of an endpoint: one per Razor Page handler, otherwise the endpoint itself.
    /// </summary>
    public IReadOnlyList<EndpointAuthorizationInfo> DescribeAll(Endpoint endpoint) => _cache.GetValue(endpoint, _factory).All;

    /// <summary>
    ///   Gets the entry that applies to a request, selecting the Razor Page handler that will run.
    /// </summary>
    public EndpointAuthorizationInfo Describe(Endpoint endpoint, HttpContext httpContext)
    {
        EndpointDescription description = _cache.GetValue(endpoint, _factory);
        if (description.All.Count == 1)
        {
            return description.Navigation;
        }

        // Razor Pages selects a handler by HTTP method and the "handler" route value or query string parameter,
        // after authorization has run; select it the same way here.
        string method = httpContext.Request.Method;
        string? handler = httpContext.GetRouteValue("handler") as string;
        if (string.IsNullOrEmpty(handler))
        {
            handler = httpContext.Request.Query["handler"].ToString();
        }

        return description.Find(method, handler)
            ?? (HttpMethods.IsHead(method) ? description.Find(HttpMethods.Get, handler) : null)
            ?? description.Navigation;
    }

    private EndpointDescription Create(Endpoint endpoint)
    {
        EndpointMetadataCollection metadata = endpoint.Metadata;
        DynamicAuthorizationOptions options = _options.CurrentValue;
        string[] httpMethods = metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Select(m => m.ToUpperInvariant()).ToArray() ?? [];

        if (metadata.GetMetadata<ControllerActionDescriptor>() is { } action)
        {
            return DescribeAction(endpoint, action, httpMethods, options);
        }

        if (metadata.GetMetadata<PageActionDescriptor>() is { } page)
        {
            return DescribePage(endpoint, page, options);
        }

        return DescribeRoute(endpoint, httpMethods, options);
    }

    private static EndpointDescription DescribeAction(
        Endpoint endpoint,
        ControllerActionDescriptor action,
        string[] httpMethods,
        DynamicAuthorizationOptions options)
    {
        EndpointMetadataCollection metadata = endpoint.Metadata;
        string? area = GetArea(action.RouteValues);
        IPermissionResourceMetadata? resource = metadata.GetMetadata<IPermissionResourceMetadata>();
        IPermissionMetadata? overrides = metadata.GetMetadata<IPermissionMetadata>();
        string? controllerDisplayName = action.ControllerTypeInfo.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
        string? actionDisplayName = action.MethodInfo.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
        string route = action.AttributeRouteInfo?.Template is { } template
            ? PermissionKeys.NormalizeRoute(template)
            : "/" + string.Join('/', new[] { area, action.ControllerName, action.ActionName }.Where(s => !string.IsNullOrEmpty(s)));

        Target target = new(
            PermissionKeys.ForAction(area, action.ControllerName, action.ActionName),
            resource?.Resource ?? JoinResource(area, [action.ControllerName]),
            resource?.DisplayName ?? WithArea(area, controllerDisplayName ?? action.ControllerName),
            resource?.DisplayName is null ? NaturalGroupPriority : ExplicitGroupPriority,
            actionDisplayName,
            action.ActionName,
            PermissionSource.Controller);

        EndpointProtection protection = GetProtection(metadata, area, options, overrides is not null);
        PermissionDescriptor? permission = protection == EndpointProtection.DynamicPermission
            ? CreatePermission(target, overrides, options, () =>
                OperationConventions.FromName(action.ActionName)
                ?? OperationConventions.FromHttpMethods(httpMethods)
                ?? PermissionKeys.ToSegment(action.ActionName))
            : null;

        EndpointAuthorizationInfo entry = new(
            endpoint, actionDisplayName ?? action.ActionName, target.Group, route, httpMethods, PermissionSource.Controller, area,
            protection, target.TargetKey, permission);

        return new EndpointDescription(entry, [entry]);
    }

    private static EndpointDescription DescribePage(Endpoint endpoint, PageActionDescriptor page, DynamicAuthorizationOptions options)
    {
        EndpointMetadataCollection metadata = endpoint.Metadata;
        CompiledPageActionDescriptor? compiled = page as CompiledPageActionDescriptor;
        TypeInfo? modelType = compiled?.ModelTypeInfo;
        string? area = string.IsNullOrEmpty(page.AreaName) ? null : page.AreaName;
        string[] segments = page.ViewEnginePath.Trim('/').Split('/');
        string pageName = segments[^1];
        IPermissionResourceMetadata? resource = modelType?.GetCustomAttribute<PermissionResourceAttribute>()
            ?? metadata.GetMetadata<IPermissionResourceMetadata>();
        IPermissionMetadata? pageOverrides = modelType?.GetCustomAttribute<PermissionAttribute>()
            ?? metadata.GetMetadata<IPermissionMetadata>();
        string? pageDisplayName = (modelType?.GetCustomAttribute<DisplayNameAttribute>() ?? metadata.GetMetadata<DisplayNameAttribute>())?.DisplayName;
        string route = PermissionKeys.NormalizeRoute(page.AttributeRouteInfo?.Template ?? (endpoint as RouteEndpoint)?.RoutePattern.RawText);

        // Pages in a folder belong to the folder's resource (/Orders/Edit: orders). Pages in the root folder are
        // their own resource (/Privacy: privacy), except index-like pages, which belong to "home".
        string[] resourceNames = segments.Length > 1
            ? segments[..^1]
            : [OperationConventions.FromName(pageName) is null ? pageName : HomeResource];

        Target target = new(
            PermissionKeys.ForPage(area, page.ViewEnginePath),
            resource?.Resource ?? JoinResource(area, resourceNames),
            resource?.DisplayName ?? WithArea(area, string.Join(" / ", resourceNames)),
            resource?.DisplayName is null ? NaturalGroupPriority : ExplicitGroupPriority,
            pageDisplayName,
            pageName,
            PermissionSource.Page);

        EndpointProtection protection = GetProtection(metadata, area, options, pageOverrides is not null);
        string pageOperation = ToOperation(pageOverrides?.Operation) ?? OperationConventions.FromName(pageName) ?? PermissionOperation.Read;
        string displayName = pageDisplayName ?? pageName;

        bool describeHandlers = protection == EndpointProtection.DynamicPermission
            && options.Convention == PermissionConvention.Crud
            && compiled is { HandlerMethods.Count: > 0 };
        if (!describeHandlers)
        {
            PermissionDescriptor? permission = protection == EndpointProtection.DynamicPermission
                ? CreatePermission(target, pageOverrides, options, () => pageOperation)
                : null;
            EndpointAuthorizationInfo entry = new(
                endpoint, displayName, target.Group, route, [], PermissionSource.Page, area, protection, target.TargetKey, permission);

            return new EndpointDescription(entry, [entry]);
        }

        // A page-level explicit key applies to every handler; a page-level operation only to its unnamed handlers.
        IPermissionMetadata? pageKey = pageOverrides?.Permission is null ? null : pageOverrides;
        List<EndpointAuthorizationInfo> entries = [];
        // Razor Pages also lists page model overrides such as OnPageHandlerExecutionAsync as handlers of a "Page" HTTP
        // method, which no request can reach; skip them.
        foreach (HandlerMethodDescriptor handler in compiled!.HandlerMethods.Where(h => IsRequestMethod(h.HttpMethod)))
        {
            string method = handler.HttpMethod.ToUpperInvariant();
            string? handlerName = string.IsNullOrEmpty(handler.Name) ? null : handler.Name;
            string? handlerDisplayName = handler.MethodInfo.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
            Target handlerTarget = target with
            {
                DisplayName = handlerDisplayName ?? (handlerName is null ? pageDisplayName : null),
                Name = handlerName ?? pageName,
            };
            PermissionDescriptor permission = CreatePermission(
                handlerTarget,
                handler.MethodInfo.GetCustomAttribute<PermissionAttribute>() ?? pageKey,
                options,
                () => GetHandlerOperation(method, handlerName, pageOperation));

            entries.Add(new EndpointAuthorizationInfo(
                endpoint, displayName, target.Group, route, [method], PermissionSource.Page, area, protection, target.TargetKey, permission, handlerName));
        }

        // Without an unnamed GET handler, a GET request renders the page without running a handler.
        EndpointAuthorizationInfo? navigation = entries.Find(e => e.Handler is null && e.HttpMethods[0] == HttpMethods.Get);
        if (navigation is null)
        {
            navigation = new EndpointAuthorizationInfo(
                endpoint, displayName, target.Group, route, [HttpMethods.Get], PermissionSource.Page, area, protection, target.TargetKey,
                CreatePermission(target, pageKey, options, () => pageOperation));
            entries.Insert(0, navigation);
        }

        return new EndpointDescription(navigation, entries);
    }

    private static EndpointDescription DescribeRoute(Endpoint endpoint, string[] httpMethods, DynamicAuthorizationOptions options)
    {
        EndpointMetadataCollection metadata = endpoint.Metadata;
        string route = PermissionKeys.NormalizeRoute((endpoint as RouteEndpoint)?.RoutePattern.RawText ?? endpoint.DisplayName);
        string? endpointName = metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName;
        string? tag = metadata.GetMetadata<ITagsMetadata>()?.Tags.FirstOrDefault();
        IPermissionResourceMetadata? resource = metadata.GetMetadata<IPermissionResourceMetadata>();
        IPermissionMetadata? overrides = metadata.GetMetadata<IPermissionMetadata>();
        string displayName = metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary
            ?? endpointName
            ?? $"{(httpMethods.Length == 0 ? "ANY" : string.Join(", ", httpMethods))} {route}";

        // The resource is the first literal segment of the route, after "api" and a version: /api/v1/orders/{id}: orders.
        string[] segments = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] literals = [.. segments.Where(s => IsLiteral(s) && !IsApiOrVersion(s))];
        string resourceKey = resource?.Resource
            ?? (literals.Length > 0 ? PermissionKeys.ToSegment(literals[0]) : tag is null ? EndpointsResource : PermissionKeys.ToSegment(tag));

        Target target = new(
            endpointName is null ? PermissionKeys.ForRoute(httpMethods, route) : PermissionKeys.ForEndpointName(endpointName),
            resourceKey,
            resource?.DisplayName ?? tag ?? PermissionKeys.GetResourceDisplayName(resourceKey),
            resource?.DisplayName is null ? TagGroupPriority : ExplicitGroupPriority,
            null,
            displayName,
            PermissionSource.Endpoint);

        EndpointProtection protection = GetProtection(metadata, null, options, overrides is not null);
        PermissionDescriptor? permission = protection == EndpointProtection.DynamicPermission
            ? CreatePermission(target, overrides, options, () =>
                OperationConventions.FromName(endpointName)
                ?? OperationConventions.FromHttpMethods(httpMethods)
                ?? GetRouteOperation(segments, literals, httpMethods))
            : null;

        EndpointAuthorizationInfo entry = new(
            endpoint, displayName, target.Group, route, httpMethods, PermissionSource.Endpoint, null, protection, target.TargetKey, permission);

        return new EndpointDescription(entry, [entry]);
    }

    private static PermissionDescriptor CreatePermission(
        Target target,
        IPermissionMetadata? overrides,
        DynamicAuthorizationOptions options,
        Func<string> inferOperation)
    {
        if (overrides?.Permission is { } explicitKey)
        {
            (string? resource, string? operation) = PermissionKeys.Split(explicitKey);
            bool ownResource = resource is null || string.Equals(resource, target.Resource, StringComparison.OrdinalIgnoreCase);

            return new PermissionDescriptor(
                explicitKey,
                overrides.DisplayName ?? target.DisplayName ?? (operation is null ? explicitKey : PermissionOperation.GetDisplayName(operation)),
                ownResource ? target.Group : PermissionKeys.GetResourceDisplayName(resource!),
                target.Source)
            {
                Resource = resource,
                Operation = operation,
                GroupPriority = ownResource ? target.GroupPriority : BorrowedGroupPriority,
            };
        }

        if (options.Convention == PermissionConvention.Endpoint)
        {
            return new PermissionDescriptor(
                target.TargetKey, overrides?.DisplayName ?? target.DisplayName ?? target.Name, target.Group, target.Source)
            {
                GroupPriority = target.GroupPriority,
            };
        }

        string resolvedOperation = ToOperation(overrides?.Operation) ?? inferOperation();
        string displayName = overrides?.DisplayName
            ?? (PermissionOperation.IsStandard(resolvedOperation)
                ? PermissionOperation.GetDisplayName(resolvedOperation)
                : target.DisplayName ?? PermissionOperation.GetDisplayName(resolvedOperation));

        return new PermissionDescriptor(PermissionKeys.ForOperation(target.Resource, resolvedOperation), displayName, target.Group, target.Source)
        {
            Resource = target.Resource,
            Operation = resolvedOperation,
            GroupPriority = target.GroupPriority,
        };
    }

    private static string GetHandlerOperation(string httpMethod, string? handlerName, string pageOperation)
    {
        bool safe = OperationConventions.IsSafeMethod(httpMethod);
        if (handlerName is not null)
        {
            // OnPostArchive: archive; OnPostDelete: delete; OnGetExport: read.
            return OperationConventions.FromName(handlerName) ?? (safe ? PermissionOperation.Read : PermissionKeys.ToSegment(handlerName));
        }

        if (safe || pageOperation != PermissionOperation.Read)
        {
            return pageOperation;
        }

        // An unnamed POST, PUT or DELETE handler on a page that displays a resource changes it.
        return string.Equals(httpMethod, HttpMethods.Delete, StringComparison.OrdinalIgnoreCase)
            ? PermissionOperation.Delete
            : PermissionOperation.Update;
    }

    private static string GetRouteOperation(string[] segments, string[] literals, string[] httpMethods)
    {
        string? last = segments.Length > 0 ? segments[^1] : null;
        bool endsWithLiteral = last is not null && IsLiteral(last) && !IsApiOrVersion(last);

        if (httpMethods is [var method] && string.Equals(method, HttpMethods.Post, StringComparison.OrdinalIgnoreCase))
        {
            // POST /orders: create; POST /orders/{id}/ship: ship; POST /orders/{id}: update.
            if (!endsWithLiteral)
            {
                return PermissionOperation.Update;
            }

            return literals.Length <= 1 ? PermissionOperation.Create : PermissionKeys.ToSegment(last!);
        }

        return endsWithLiteral && literals.Length > 1 ? PermissionKeys.ToSegment(last!) : AnyOperation;
    }

    private static EndpointProtection GetProtection(
        EndpointMetadataCollection metadata,
        string? area,
        DynamicAuthorizationOptions options,
        bool hasPermissionMetadata)
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

        if (hasPermissionMetadata)
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

    private static string? ToOperation(string? operation) =>
        string.IsNullOrWhiteSpace(operation) ? null : PermissionKeys.ToSegment(operation);

    private static string JoinResource(string? area, IEnumerable<string> names) =>
        string.Join('.', (area is null ? names : names.Prepend(area)).Select(PermissionKeys.ToSegment));

    private static string? GetArea(IDictionary<string, string?> routeValues) =>
        routeValues.TryGetValue("area", out string? area) && !string.IsNullOrEmpty(area) ? area : null;

    private static string WithArea(string? area, string name) => area is null ? name : $"{area} / {name}";

    private static bool IsRequestMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method) || HttpMethods.IsOptions(method);

    private static bool IsLiteral(string segment) => !segment.Contains('{', StringComparison.Ordinal);

    private static bool IsApiOrVersion(string segment) =>
        string.Equals(segment, "api", StringComparison.OrdinalIgnoreCase) || VersionSegmentRegex().IsMatch(segment);

    [GeneratedRegex(@"^v\d+(\.\d+)*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionSegmentRegex();

    /// <summary>
    ///   What a permission is derived for: an action, a page or one of its handlers, or a route.
    /// </summary>
    private sealed record Target(
        string TargetKey,
        string Resource,
        string Group,
        int GroupPriority,
        string? DisplayName,
        string Name,
        PermissionSource Source);

    private sealed class EndpointDescription(EndpointAuthorizationInfo navigation, IReadOnlyList<EndpointAuthorizationInfo> all)
    {
        public EndpointAuthorizationInfo Navigation { get; } = navigation;

        public IReadOnlyList<EndpointAuthorizationInfo> All { get; } = all;

        public EndpointAuthorizationInfo? Find(string httpMethod, string? handler)
        {
            foreach (EndpointAuthorizationInfo entry in All)
            {
                if (entry.HttpMethods.Count == 1
                    && string.Equals(entry.HttpMethods[0], httpMethod, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.Handler ?? string.Empty, handler ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
