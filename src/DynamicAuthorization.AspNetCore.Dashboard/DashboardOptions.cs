using DynamicAuthorization.AspNetCore.Management;

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

    /// <summary>
    ///   Gets or sets where the text typed into the search on the users page must occur in a username or email
    ///   address. Defaults to <see cref="UserSearchMatch.StartsWith"/>, which the store answers from the indexes of
    ///   the Identity tables. <see cref="UserSearchMatch.Contains"/> finds the text anywhere but reads every user on
    ///   each search, which gets slower as the user table grows.
    /// </summary>
    public UserSearchMatch UserSearch { get; set; } = UserSearchMatch.StartsWith;

    internal string NormalizedRoutePrefix => RoutePrefix.Trim('/');
}
