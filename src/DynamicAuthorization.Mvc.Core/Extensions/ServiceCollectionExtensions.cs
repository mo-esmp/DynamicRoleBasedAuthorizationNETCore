using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicAuthorization.Mvc.Core.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDynamicAuthorization(this IServiceCollection services)
        {
            services.AddSingleton<IMvcControllerDiscovery, MvcControllerDiscovery>();

            services.Configure<MvcOptions>(options =>
            {
                options.Filters.Add(typeof(DynamicAuthorizationFilter));
            });

            return services;
        }
    }
}