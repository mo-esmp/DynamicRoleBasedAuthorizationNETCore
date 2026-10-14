using Microsoft.AspNetCore.Http;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   The kind of endpoint a permission was discovered from.
/// </summary>
public enum PermissionSource
{
    /// <summary>An MVC controller action.</summary>
    Controller,

    /// <summary>A Razor Page.</summary>
    Page,

    /// <summary>Any other endpoint, for example a Minimal API endpoint.</summary>
    Endpoint,

    /// <summary>A permission added with <see cref="DynamicAuthorizationOptions.AddPermission"/>.</summary>
    Custom,
}

/// <summary>
///   How an endpoint is protected.
/// </summary>
public enum EndpointProtection
{
    /// <summary>The endpoint has no authorization metadata.</summary>
    None,

    /// <summary>The endpoint allows anonymous access.</summary>
    AllowAnonymous,

    /// <summary>The endpoint requires an authenticated user but is excluded from the dynamic permission check.</summary>
    Excluded,

    /// <summary>The endpoint is protected by roles or policies only, without a dynamic permission.</summary>
    PolicyOnly,

    /// <summary>The endpoint requires a dynamic permission.</summary>
    DynamicPermission,
}

/// <summary>
///   Describes a permission that can be granted to roles.
/// </summary>
/// <param name="Permission">The permission key.</param>
/// <param name="DisplayName">The display name, for example <c>Read</c> or <c>Approve</c>.</param>
/// <param name="Group">The group the permission is listed under: the resource name, for example <c>Orders</c>.</param>
/// <param name="Source">The kind of endpoint the permission was discovered from.</param>
public sealed record PermissionDescriptor(string Permission, string DisplayName, string Group, PermissionSource Source)
{
    /// <summary>
    ///   Gets the resource key of a <c>{resource}.{operation}</c> permission, for example <c>orders</c>.
    /// </summary>
    public string? Resource { get; init; }

    /// <summary>
    ///   Gets the operation of a <c>{resource}.{operation}</c> permission, for example <c>read</c>.
    /// </summary>
    public string? Operation { get; init; }

    /// <summary>
    ///   Gets how authoritative <see cref="Group"/> is when endpoints of the same resource name it differently.
    /// </summary>
    internal int GroupPriority { get; init; }
}

/// <summary>
///   Describes how an endpoint is authorized.
/// </summary>
/// <param name="Endpoint">The endpoint.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Group">The group the endpoint belongs to.</param>
/// <param name="Route">The route of the endpoint.</param>
/// <param name="HttpMethods">The HTTP methods the endpoint accepts; empty when it accepts any method.</param>
/// <param name="Source">The kind of endpoint.</param>
/// <param name="Area">The area of the endpoint, if any.</param>
/// <param name="Protection">How the endpoint is protected.</param>
/// <param name="TargetKey">
///   The key derived from the endpoint's controller and action, page, or route. It equals the permission key unless
///   the endpoint declares an explicit <see cref="PermissionAttribute"/>.
/// </param>
/// <param name="Permission">The permission required by the endpoint when <see cref="Protection"/> is <see cref="EndpointProtection.DynamicPermission"/>.</param>
/// <param name="Handler">
///   The name of the Razor Page handler this entry describes, for example <c>Archive</c> for <c>OnPostArchive</c>;
///   <see langword="null"/> for unnamed handlers and other endpoints. With the <see cref="PermissionConvention.Crud"/>
///   convention, each handler of a page is listed separately because it can require a different permission.
/// </param>
public sealed record EndpointAuthorizationInfo(
    Endpoint Endpoint,
    string DisplayName,
    string Group,
    string Route,
    IReadOnlyList<string> HttpMethods,
    PermissionSource Source,
    string? Area,
    EndpointProtection Protection,
    string TargetKey,
    PermissionDescriptor? Permission,
    string? Handler = null);

/// <summary>
///   The permissions and endpoints discovered in the application.
/// </summary>
public sealed class PermissionCatalog
{
    private readonly Dictionary<string, PermissionDescriptor> _permissions;
    private readonly ILookup<string, EndpointAuthorizationInfo> _endpointsByTarget;
    private readonly ILookup<string, EndpointAuthorizationInfo> _endpointsByPermission;

    /// <summary>
    ///   Initializes a new instance of the <see cref="PermissionCatalog"/> class.
    /// </summary>
    /// <param name="permissions">The permissions that can be granted.</param>
    /// <param name="endpoints">The endpoints of the application.</param>
    public PermissionCatalog(IEnumerable<PermissionDescriptor> permissions, IEnumerable<EndpointAuthorizationInfo> endpoints)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(endpoints);

        Permissions = [.. permissions];
        Endpoints = [.. endpoints];
        _permissions = Permissions.ToDictionary(p => p.Permission, StringComparer.OrdinalIgnoreCase);
        _endpointsByTarget = Endpoints.ToLookup(e => e.TargetKey, StringComparer.OrdinalIgnoreCase);
        _endpointsByPermission = Endpoints
            .Where(e => e.Permission is not null)
            .ToLookup(e => e.Permission!.Permission, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///   Gets the permissions that can be granted, ordered by group and display name.
    /// </summary>
    public IReadOnlyList<PermissionDescriptor> Permissions { get; }

    /// <summary>
    ///   Gets the endpoints of the application.
    /// </summary>
    public IReadOnlyList<EndpointAuthorizationInfo> Endpoints { get; }

    /// <summary>
    ///   Finds a permission by key.
    /// </summary>
    /// <param name="permission">The permission key.</param>
    /// <returns>The permission, or <see langword="null"/> when no endpoint or definition declares it.</returns>
    public PermissionDescriptor? FindPermission(string permission) =>
        _permissions.GetValueOrDefault(permission);

    /// <summary>
    ///   Gets the endpoints that require a permission.
    /// </summary>
    /// <param name="permission">The permission key.</param>
    /// <returns>The endpoints.</returns>
    public IEnumerable<EndpointAuthorizationInfo> GetEndpoints(string permission) => _endpointsByPermission[permission];

    /// <summary>
    ///   Finds the endpoint of an MVC action, Razor Page or route by its target key. When several entries share the
    ///   key, the one that handles navigating to it (a GET request without a page handler) is returned.
    /// </summary>
    /// <param name="targetKey">The key built with <see cref="PermissionKeys"/>.</param>
    /// <returns>The endpoint, or <see langword="null"/> when no endpoint matches.</returns>
    public EndpointAuthorizationInfo? FindEndpoint(string targetKey)
    {
        EndpointAuthorizationInfo? match = null;
        foreach (EndpointAuthorizationInfo endpoint in _endpointsByTarget[targetKey])
        {
            if (match is null || GetRank(endpoint) > GetRank(match))
            {
                match = endpoint;
            }
        }

        return match;
    }

    /// <summary>
    ///   Finds the entry of a Razor Page handler by the page's target key and the handler name. Falls back to
    ///   <see cref="FindEndpoint(string)"/> when the page's handlers are not listed separately.
    /// </summary>
    /// <param name="targetKey">The key built with <see cref="PermissionKeys"/>.</param>
    /// <param name="handler">The handler name, for example <c>Delete</c>, or <see langword="null"/> for the page itself.</param>
    /// <returns>The endpoint, or <see langword="null"/> when no endpoint matches.</returns>
    public EndpointAuthorizationInfo? FindEndpoint(string targetKey, string? handler)
    {
        if (!string.IsNullOrEmpty(handler))
        {
            foreach (EndpointAuthorizationInfo endpoint in _endpointsByTarget[targetKey])
            {
                if (string.Equals(endpoint.Handler, handler, StringComparison.OrdinalIgnoreCase))
                {
                    return endpoint;
                }
            }
        }

        return FindEndpoint(targetKey);
    }

    private static int GetRank(EndpointAuthorizationInfo endpoint)
    {
        bool navigates = endpoint.Handler is null
            && (endpoint.HttpMethods.Count == 0 || endpoint.HttpMethods.Contains("GET", StringComparer.OrdinalIgnoreCase));

        // Prefer navigation, then the most restrictive protection.
        return (navigates ? 10 : 0) + (int)endpoint.Protection;
    }
}
