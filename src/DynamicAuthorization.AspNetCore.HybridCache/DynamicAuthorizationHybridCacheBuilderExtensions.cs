using DynamicAuthorization.AspNetCore;
using DynamicAuthorization.AspNetCore.HybridCache;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///   Extension methods for caching dynamic authorization data in a <see cref="HybridCache"/>.
/// </summary>
public static class DynamicAuthorizationHybridCacheBuilderExtensions
{
    /// <summary>
    ///   Caches user roles and role permissions in the application's <see cref="HybridCache"/> instead of the memory
    ///   of the instance. With a distributed cache registered as well (<c>IDistributedCache</c>, for example Redis),
    ///   every instance shares the cached data, and an invalidation on one instance reaches the others within
    ///   <see cref="DynamicAuthorizationHybridCacheOptions.LocalCacheDuration"/>. Registers <see cref="HybridCache"/>
    ///   with its defaults when the application has not; configure it with <c>services.AddHybridCache(options => ...)</c>
    ///   before or after this call.
    /// </summary>
    /// <param name="builder">The dynamic authorization builder.</param>
    /// <param name="configure">Configures <see cref="DynamicAuthorizationHybridCacheOptions"/>.</param>
    /// <returns>The same builder so that multiple calls can be chained.</returns>
    public static IDynamicAuthorizationBuilder UseHybridCache(
        this IDynamicAuthorizationBuilder builder,
        Action<DynamicAuthorizationHybridCacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!builder.Services.Any(d => d.ServiceType == typeof(HybridCache)))
        {
            builder.Services.AddHybridCache();
        }

        OptionsBuilder<DynamicAuthorizationHybridCacheOptions> options = builder.Services.AddOptions<DynamicAuthorizationHybridCacheOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options
            .Validate(o => o.LocalCacheDuration > TimeSpan.Zero, $"{nameof(DynamicAuthorizationHybridCacheOptions.LocalCacheDuration)} must be positive.")
            .ValidateOnStart();

        builder.Services.Replace(ServiceDescriptor.Singleton<IDynamicAuthorizationCacheStore, HybridDynamicAuthorizationCacheStore>());

        return builder;
    }
}
