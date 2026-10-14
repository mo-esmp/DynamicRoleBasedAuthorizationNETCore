using System.Reflection;
using DynamicAuthorization.AspNetCore;
using DynamicAuthorization.AspNetCore.Dashboard;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///   Extension methods for adding the dynamic authorization dashboard.
/// </summary>
public static class DashboardServiceCollectionExtensions
{
    /// <summary>
    ///   Adds the dashboard. Map it with <c>app.MapDynamicAuthorizationDashboard()</c>.
    /// </summary>
    /// <param name="builder">The dynamic authorization builder.</param>
    /// <param name="configure">Configures <see cref="DashboardOptions"/>.</param>
    /// <returns>The same builder so that multiple calls can be chained.</returns>
    public static IDynamicAuthorizationBuilder AddDashboard(this IDynamicAuthorizationBuilder builder, Action<DashboardOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        IServiceCollection services = builder.Services;
        OptionsBuilder<DashboardOptions> options = services.AddOptions<DashboardOptions>();
        if (configure is not null)
            options.Configure(configure);

        options
            .Validate(o => !string.IsNullOrWhiteSpace(o.RoutePrefix?.Trim('/')), $"{nameof(DashboardOptions.RoutePrefix)} must not be empty.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.AuthorizationPolicy), $"{nameof(DashboardOptions.AuthorizationPolicy)} must not be empty.");

        IMvcBuilder mvc = services.AddRazorPages();
        Assembly assembly = typeof(DashboardOptions).Assembly;
        if (!mvc.PartManager.ApplicationParts.OfType<AssemblyPart>().Any(p => p.Assembly == assembly))
            mvc.AddApplicationPart(assembly);

        services.TryAddSingleton<DashboardMarker>();
        services.TryAddSingleton<DashboardAssets>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<RazorPagesOptions>, ConfigureDashboardPages>());

        return builder;
    }
}
