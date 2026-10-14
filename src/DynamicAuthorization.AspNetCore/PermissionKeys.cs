namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Builds the permission keys that identify endpoints.
/// </summary>
/// <remarks>
///   <list type="bullet">
///     <item>MVC actions: <c>mvc:{area}:{controller}:{action}</c>, for example <c>mvc::Orders:Edit</c>.</item>
///     <item>Razor Pages: <c>page:{area}:{page}</c>, for example <c>page::/Orders/Edit</c>.</item>
///     <item>Named endpoints: <c>endpoint:{name}</c>.</item>
///     <item>Other endpoints: <c>endpoint:{methods} {route}</c>, for example <c>endpoint:GET /api/orders</c>.</item>
///   </list>
///   Endpoints decorated with <see cref="PermissionAttribute"/> use the key given to the attribute instead.
///   Keys are compared case-insensitively.
/// </remarks>
public static class PermissionKeys
{
    /// <summary>
    ///   The prefix of MVC action permission keys.
    /// </summary>
    public const string ActionPrefix = "mvc:";

    /// <summary>
    ///   The prefix of Razor Page permission keys.
    /// </summary>
    public const string PagePrefix = "page:";

    /// <summary>
    ///   The prefix of permission keys for endpoints that are neither MVC actions nor Razor Pages.
    /// </summary>
    public const string EndpointPrefix = "endpoint:";

    /// <summary>
    ///   Gets the permission key of an MVC action.
    /// </summary>
    /// <param name="area">The area name, or <see langword="null"/> when the controller is not in an area.</param>
    /// <param name="controller">The controller name, without the <c>Controller</c> suffix.</param>
    /// <param name="action">The action name.</param>
    /// <returns>The permission key.</returns>
    public static string ForAction(string? area, string controller, string action)
    {
        ArgumentException.ThrowIfNullOrEmpty(controller);
        ArgumentException.ThrowIfNullOrEmpty(action);

        return $"{ActionPrefix}{area}:{controller}:{action}";
    }

    /// <summary>
    ///   Gets the permission key of a Razor Page.
    /// </summary>
    /// <param name="area">The area name, or <see langword="null"/> when the page is not in an area.</param>
    /// <param name="page">The page path relative to the pages root, for example <c>/Orders/Edit</c>.</param>
    /// <returns>The permission key.</returns>
    public static string ForPage(string? area, string page)
    {
        ArgumentException.ThrowIfNullOrEmpty(page);

        return $"{PagePrefix}{area}:{page}";
    }

    /// <summary>
    ///   Gets the permission key of a named endpoint, for example a Minimal API endpoint named with <c>WithName</c>.
    /// </summary>
    /// <param name="endpointName">The endpoint name.</param>
    /// <returns>The permission key.</returns>
    public static string ForEndpointName(string endpointName)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpointName);

        return $"{EndpointPrefix}{endpointName}";
    }

    /// <summary>
    ///   Gets the permission key of an unnamed endpoint from its HTTP methods and route pattern.
    /// </summary>
    /// <param name="httpMethods">The HTTP methods the endpoint accepts. Empty or <see langword="null"/> means any method.</param>
    /// <param name="routePattern">The route pattern, for example <c>/api/orders/{id}</c>.</param>
    /// <returns>The permission key.</returns>
    public static string ForRoute(IEnumerable<string>? httpMethods, string routePattern)
    {
        ArgumentNullException.ThrowIfNull(routePattern);

        string methods = httpMethods is null
            ? string.Empty
            : string.Join(',', httpMethods.Select(m => m.ToUpperInvariant()).Order(StringComparer.Ordinal));

        return $"{EndpointPrefix}{(methods.Length == 0 ? "*" : methods)} {NormalizeRoute(routePattern)}";
    }

    /// <summary>
    ///   Normalizes a route to start with, and not end with, a slash. <c>MapGet("")</c> in a route group produces a
    ///   trailing slash, which would otherwise end up in the permission key.
    /// </summary>
    internal static string NormalizeRoute(string? routePattern) => "/" + (routePattern ?? string.Empty).Trim('/');
}
