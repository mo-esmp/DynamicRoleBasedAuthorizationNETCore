using DynamicAuthorization.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///   Extension methods for registering dynamic authorization in an <see cref="IServiceCollection"/>.
/// </summary>
public static class DynamicAuthorizationServiceCollectionExtensions
{
    /// <summary>
    ///   Adds dynamic authorization. Add a store, for example with <c>AddEntityFrameworkStore&lt;TContext&gt;()</c>,
    ///   to persist the permissions granted to roles.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures <see cref="DynamicAuthorizationOptions"/>.</param>
    /// <returns>A builder to configure dynamic authorization further.</returns>
    public static IDynamicAuthorizationBuilder AddDynamicAuthorization(
        this IServiceCollection services,
        Action<DynamicAuthorizationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        OptionsBuilder<DynamicAuthorizationOptions> options = services.AddOptions<DynamicAuthorizationOptions>();
        if (configure is not null)
            options.Configure(configure);

        options.Validate(o => o.CacheDuration >= TimeSpan.Zero, $"{nameof(DynamicAuthorizationOptions.CacheDuration)} must not be negative.");

        services.AddAuthorization();
        services.TryAddSingleton<DynamicAuthorizationCache>();
        services.TryAddSingleton<IDynamicAuthorizationCache>(sp => sp.GetRequiredService<DynamicAuthorizationCache>());
        services.TryAddSingleton<EndpointAuthorizationResolver>();
        services.TryAddSingleton<IPermissionCatalogProvider, PermissionCatalogProvider>();
        services.TryAddScoped<IPermissionChecker, PermissionChecker>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, DynamicPermissionHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<AuthorizationOptions>, ConfigureAuthorizationOptions>());

        return new DynamicAuthorizationBuilder(services);
    }
}
