using Microsoft.AspNetCore.Html;

namespace DynamicAuthorization.AspNetCore.Dashboard;

/// <summary>
///   Markup shared by several dashboard pages.
/// </summary>
internal static class DashboardHtml
{
    public static IHtmlContent ProtectionBadge(EndpointProtection protection) => protection switch
    {
        EndpointProtection.DynamicPermission => Badge("da-badge-accent", DashboardIcons.Lock, "Permission"),
        EndpointProtection.PolicyOnly => Badge("da-badge-info", DashboardIcons.Shield, "Roles or policy"),
        EndpointProtection.Excluded => Badge(string.Empty, DashboardIcons.Person, "Any signed-in user"),
        EndpointProtection.AllowAnonymous => Badge(string.Empty, DashboardIcons.Globe, "Anonymous"),
        _ => Badge("da-badge-warning", DashboardIcons.Warning, "No authorization"),
    };

    public static string ProtectionDescription(EndpointProtection protection) => protection switch
    {
        EndpointProtection.DynamicPermission => "Requires a permission granted to one of the user's roles.",
        EndpointProtection.PolicyOnly => "Protected by roles or policies in code; not managed here.",
        EndpointProtection.Excluded => "Requires a signed-in user but no permission.",
        EndpointProtection.AllowAnonymous => "Explicitly allows anonymous access.",
        _ => "Has no authorization metadata, so anyone can access it unless a fallback policy applies.",
    };

    public static string SourceName(PermissionSource source) => source switch
    {
        PermissionSource.Controller => "MVC",
        PermissionSource.Page => "Razor Page",
        PermissionSource.Endpoint => "Endpoint",
        _ => "Custom",
    };

    private static HtmlString Badge(string modifier, IHtmlContent icon, string text)
    {
        using StringWriter writer = new();
        icon.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);

        return new HtmlString($"""<span class="da-badge {modifier}">{writer}{text}</span>""");
    }
}
