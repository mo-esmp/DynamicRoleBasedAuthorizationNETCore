using System.Text;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Builds the keys that identify permissions and endpoints.
/// </summary>
/// <remarks>
///   With the default <see cref="PermissionConvention.Crud"/> convention, permissions are named
///   <c>{resource}.{operation}</c>, for example <c>orders.read</c> or <c>admin.reports.update</c>.
///   <para>
///     Endpoints are also identified by a target key, which is the permission key of the
///     <see cref="PermissionConvention.Endpoint"/> convention:
///   </para>
///   <list type="bullet">
///     <item>MVC actions: <c>mvc:{area}:{controller}:{action}</c>, for example <c>mvc::Orders:Edit</c>.</item>
///     <item>Razor Pages: <c>page:{area}:{page}</c>, for example <c>page::/Orders/Edit</c>.</item>
///     <item>Named endpoints: <c>endpoint:{name}</c>.</item>
///     <item>Other endpoints: <c>endpoint:{methods} {route}</c>, for example <c>endpoint:GET /api/orders</c>.</item>
///   </list>
///   Keys are compared case-insensitively.
/// </remarks>
public static class PermissionKeys
{
    /// <summary>
    ///   The prefix of MVC action keys.
    /// </summary>
    public const string ActionPrefix = "mvc:";

    /// <summary>
    ///   The prefix of Razor Page keys.
    /// </summary>
    public const string PagePrefix = "page:";

    /// <summary>
    ///   The prefix of keys for endpoints that are neither MVC actions nor Razor Pages.
    /// </summary>
    public const string EndpointPrefix = "endpoint:";

    /// <summary>
    ///   Gets the permission key of an operation on a resource, for example <c>orders.read</c>.
    /// </summary>
    /// <param name="resource">The resource key, for example <c>orders</c> or <c>admin.reports</c>.</param>
    /// <param name="operation">The operation, for example <see cref="PermissionOperation.Read"/>.</param>
    /// <returns>The permission key.</returns>
    public static string ForOperation(string resource, string operation)
    {
        ArgumentException.ThrowIfNullOrEmpty(resource);
        ArgumentException.ThrowIfNullOrEmpty(operation);

        return $"{resource}.{operation}";
    }

    /// <summary>
    ///   Converts a name to a key segment: lowercase words separated by hyphens, for example <c>order-items</c> for
    ///   <c>OrderItems</c>.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The key segment.</returns>
    public static string ToSegment(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        StringBuilder builder = new(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (!char.IsLetterOrDigit(c))
            {
                if (builder.Length > 0 && builder[^1] != '-')
                {
                    builder.Append('-');
                }

                continue;
            }

            // A new word starts at an uppercase letter after a lowercase letter or digit ("orderItems"), or at the
            // last uppercase letter of an acronym followed by lowercase ("APIKeys").
            bool startsWord = char.IsUpper(c) && i > 0 && builder.Length > 0 && builder[^1] != '-'
                && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])
                    || (char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1])));
            if (startsWord)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString().TrimEnd('-');
    }

    /// <summary>
    ///   Gets the key of an MVC action.
    /// </summary>
    /// <param name="area">The area name, or <see langword="null"/> when the controller is not in an area.</param>
    /// <param name="controller">The controller name, without the <c>Controller</c> suffix.</param>
    /// <param name="action">The action name.</param>
    /// <returns>The key.</returns>
    public static string ForAction(string? area, string controller, string action)
    {
        ArgumentException.ThrowIfNullOrEmpty(controller);
        ArgumentException.ThrowIfNullOrEmpty(action);

        return $"{ActionPrefix}{area}:{controller}:{action}";
    }

    /// <summary>
    ///   Gets the key of a Razor Page.
    /// </summary>
    /// <param name="area">The area name, or <see langword="null"/> when the page is not in an area.</param>
    /// <param name="page">The page path relative to the pages root, for example <c>/Orders/Edit</c>.</param>
    /// <returns>The key.</returns>
    public static string ForPage(string? area, string page)
    {
        ArgumentException.ThrowIfNullOrEmpty(page);

        return $"{PagePrefix}{area}:{page}";
    }

    /// <summary>
    ///   Gets the key of a named endpoint, for example a Minimal API endpoint named with <c>WithName</c>.
    /// </summary>
    /// <param name="endpointName">The endpoint name.</param>
    /// <returns>The key.</returns>
    public static string ForEndpointName(string endpointName)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpointName);

        return $"{EndpointPrefix}{endpointName}";
    }

    /// <summary>
    ///   Gets the key of an unnamed endpoint from its HTTP methods and route pattern.
    /// </summary>
    /// <param name="httpMethods">The HTTP methods the endpoint accepts. Empty or <see langword="null"/> means any method.</param>
    /// <param name="routePattern">The route pattern, for example <c>/api/orders/{id}</c>.</param>
    /// <returns>The key.</returns>
    public static string ForRoute(IEnumerable<string>? httpMethods, string routePattern)
    {
        ArgumentNullException.ThrowIfNull(routePattern);

        string methods = httpMethods is null
            ? string.Empty
            : string.Join(',', httpMethods.Select(m => m.ToUpperInvariant()).Order(StringComparer.Ordinal));

        return $"{EndpointPrefix}{(methods.Length == 0 ? "*" : methods)} {NormalizeRoute(routePattern)}";
    }

    /// <summary>
    ///   Splits a <c>{resource}.{operation}</c> permission key, for example <c>admin.reports.read</c> into
    ///   <c>admin.reports</c> and <c>read</c>.
    /// </summary>
    /// <param name="permission">The permission key.</param>
    /// <returns>
    ///   The resource and operation, or <see langword="null"/> values for keys of another form, such as
    ///   <see cref="PermissionConvention.Endpoint"/> keys.
    /// </returns>
    public static (string? Resource, string? Operation) Split(string permission)
    {
        ArgumentNullException.ThrowIfNull(permission);

        int index = permission.LastIndexOf('.');
        if (index <= 0 || index == permission.Length - 1 || permission.AsSpan().IndexOfAny(':', ' ', '/') >= 0)
        {
            return (null, null);
        }

        return (permission[..index], permission[(index + 1)..]);
    }

    /// <summary>
    ///   Gets a display name for a resource key, for example <c>Admin / Order items</c> for <c>admin.order-items</c>.
    /// </summary>
    internal static string GetResourceDisplayName(string resource) =>
        string.Join(" / ", resource.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(PermissionOperation.GetDisplayName));

    /// <summary>
    ///   Normalizes a route to start with, and not end with, a slash. <c>MapGet("")</c> in a route group produces a
    ///   trailing slash, which would otherwise end up in the key.
    /// </summary>
    internal static string NormalizeRoute(string? routePattern) => "/" + (routePattern ?? string.Empty).Trim('/');
}
