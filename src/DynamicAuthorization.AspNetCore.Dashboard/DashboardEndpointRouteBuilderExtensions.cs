using DynamicAuthorization.AspNetCore;
using DynamicAuthorization.AspNetCore.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
///   Extension methods for mapping the dynamic authorization dashboard.
/// </summary>
public static class DashboardEndpointRouteBuilderExtensions
{
    /// <summary>
    ///   Maps the dashboard pages and their static files under <see cref="DashboardOptions.RoutePrefix"/>.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <exception cref="InvalidOperationException">The dashboard services have not been added.</exception>
    public static void MapDynamicAuthorizationDashboard(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        IServiceProvider services = endpoints.ServiceProvider;
        if (services.GetService<DashboardMarker>() is null)
        {
            throw new InvalidOperationException(
                "The dashboard services are not registered. Call services.AddDynamicAuthorization().AddDashboard() first.");
        }

        DashboardOptions options = services.GetRequiredService<IOptions<DashboardOptions>>().Value;
        DashboardAssets assets = services.GetRequiredService<DashboardAssets>();

        // Reuses the application's Razor Pages endpoints when they are already mapped.
        endpoints.MapRazorPages();
        endpoints
            .MapGet($"/{options.NormalizedRoutePrefix}/{DashboardAssets.PathSegment}/{{file}}", (HttpContext context, string file) => assets.Serve(context, file))
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithDisplayName("Dynamic Authorization dashboard assets")
            .WithMetadata(DashboardAssets.Metadata);
    }
}
