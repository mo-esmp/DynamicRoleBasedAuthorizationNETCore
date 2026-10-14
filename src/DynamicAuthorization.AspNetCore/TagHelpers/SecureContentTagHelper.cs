using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Routing;

namespace DynamicAuthorization.AspNetCore.TagHelpers;

/// <summary>
///   Renders its content only when the current user may access the target page, action or permission.
/// </summary>
/// <example>
///   <code>
///   &lt;secure-content asp-page="/Orders/Index"&gt;&lt;a asp-page="/Orders/Index"&gt;Orders&lt;/a&gt;&lt;/secure-content&gt;
///   &lt;secure-content asp-controller="Reports" asp-action="Export"&gt;...&lt;/secure-content&gt;
///   &lt;secure-content asp-permission="orders.delete"&gt;...&lt;/secure-content&gt;
///   </code>
/// </example>
/// <remarks>
///   Pages and actions are evaluated with their complete authorization policy, so content linking to a page that
///   allows anonymous access is always rendered, and content linking to a page protected by roles is rendered for
///   members of those roles. Unknown targets are never rendered.
/// </remarks>
[HtmlTargetElement("secure-content")]
public sealed class SecureContentTagHelper(
    IPermissionChecker permissionChecker,
    IPermissionCatalogProvider catalogProvider,
    IAuthorizationService authorizationService,
    IAuthorizationPolicyProvider policyProvider) : TagHelper
{
    private const string AreaAttributeName = "asp-area";

    /// <summary>
    ///   Gets or sets the permission key to check.
    /// </summary>
    [HtmlAttributeName("asp-permission")]
    public string? Permission { get; set; }

    /// <summary>
    ///   Gets or sets the target page, for example <c>/Orders/Index</c>. Relative paths are resolved against the
    ///   current page.
    /// </summary>
    [HtmlAttributeName("asp-page")]
    public string? Page { get; set; }

    /// <summary>
    ///   Gets or sets the handler of the target page, for example <c>Delete</c> for <c>OnPostDelete</c>. Handlers can
    ///   require their own permission, such as <c>orders.delete</c> on the orders edit page.
    /// </summary>
    [HtmlAttributeName("asp-page-handler")]
    public string? PageHandler { get; set; }

    /// <summary>
    ///   Gets or sets the area of the target page or action. When omitted, the current area is used.
    /// </summary>
    [HtmlAttributeName(AreaAttributeName)]
    public string? Area { get; set; }

    /// <summary>
    ///   Gets or sets the controller of the target action. When omitted, the current controller is used.
    /// </summary>
    [HtmlAttributeName("asp-controller")]
    public string? Controller { get; set; }

    /// <summary>
    ///   Gets or sets the target action. When omitted, the current action is used.
    /// </summary>
    [HtmlAttributeName("asp-action")]
    public string? Action { get; set; }

    /// <summary>
    ///   Gets or sets the current view context.
    /// </summary>
    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        output.TagName = null;
        if (!await IsAuthorizedAsync(context).ConfigureAwait(false))
        {
            output.SuppressOutput();
        }
    }

    private async Task<bool> IsAuthorizedAsync(TagHelperContext context)
    {
        HttpContext httpContext = ViewContext.HttpContext;
        if (!string.IsNullOrWhiteSpace(Permission))
        {
            return await permissionChecker.HasPermissionAsync(httpContext.User, Permission, httpContext.RequestAborted).ConfigureAwait(false);
        }

        string targetKey = GetTargetKey(context);
        EndpointAuthorizationInfo? target = catalogProvider.GetCatalog().FindEndpoint(targetKey, string.IsNullOrWhiteSpace(Page) ? null : PageHandler);
        if (target is null)
        {
            return false;
        }

        if (target.Protection is EndpointProtection.AllowAnonymous)
        {
            return true;
        }

        // Evaluate the endpoint's complete policy, exactly as the authorization middleware would.
        EndpointMetadataCollection metadata = target.Endpoint.Metadata;
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

        policy ??= await policyProvider.GetFallbackPolicyAsync().ConfigureAwait(false);
        if (policy is null)
        {
            return true;
        }

        // The entry itself is the resource, so the dynamic permission check uses the permission of the target handler.
        AuthorizationResult result = await authorizationService.AuthorizeAsync(httpContext.User, target, policy).ConfigureAwait(false);

        return result.Succeeded;
    }

    private string GetTargetKey(TagHelperContext context)
    {
        RouteValueDictionary ambientValues = ViewContext.RouteData.Values;
        string? area = context.AllAttributes.ContainsName(AreaAttributeName)
            ? NullIfEmpty(Area)
            : NullIfEmpty(ambientValues["area"] as string);

        if (!string.IsNullOrWhiteSpace(Page))
        {
            return PermissionKeys.ForPage(area, ResolvePagePath(Page, ambientValues["page"] as string));
        }

        string? controller = Controller ?? ambientValues["controller"] as string;
        string? action = Action ?? ambientValues["action"] as string;
        if (string.IsNullOrEmpty(controller) || string.IsNullOrEmpty(action))
        {
            throw new InvalidOperationException(
                "The <secure-content> tag helper requires an 'asp-permission', 'asp-page', or 'asp-controller' and 'asp-action' attribute.");
        }

        return PermissionKeys.ForAction(area, controller, action);
    }

    private static string ResolvePagePath(string page, string? currentPage)
    {
        if (page.StartsWith('/'))
        {
            return page;
        }

        // Relative page names resolve against the folder of the current page, like the anchor tag helper.
        string folder = currentPage is null ? "/" : currentPage[..(currentPage.LastIndexOf('/') + 1)];
        Stack<string> segments = new();
        foreach (string segment in (folder + page).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                segments.TryPop(out _);
            }
            else if (segment != ".")
            {
                segments.Push(segment);
            }
        }

        return "/" + string.Join('/', segments.Reverse());
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
