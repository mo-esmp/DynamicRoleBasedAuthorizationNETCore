namespace DynamicAuthorization.AspNetCore.Dashboard;

/// <summary>
///   Options for the dynamic authorization dashboard.
/// </summary>
public sealed class DashboardOptions
{
    /// <summary>
    ///   Gets or sets the path the dashboard is served from. Defaults to <c>authz</c>, i.e. <c>/authz</c>.
    /// </summary>
    public string RoutePrefix { get; set; } = "authz";

    /// <summary>
    ///   Gets or sets the authorization policy that protects the dashboard pages. Defaults to the dynamic
    ///   authorization policy, so every dashboard page is a permission that can be granted to roles, and super admins
    ///   can always access the dashboard. Set it to another policy name to protect the dashboard differently.
    /// </summary>
    public string AuthorizationPolicy { get; set; } = DynamicAuthorizationDefaults.PolicyName;

    /// <summary>
    ///   Gets or sets the title shown in the dashboard header.
    /// </summary>
    public string Title { get; set; } = "Dynamic Authorization";

    /// <summary>
    ///   Gets or sets the URL of the "back to application" link in the dashboard header, or <see langword="null"/>
    ///   to hide the link. Defaults to <c>~/</c>.
    /// </summary>
    public string? ApplicationUrl { get; set; } = "~/";

    internal string NormalizedRoutePrefix => RoutePrefix.Trim('/');
}
