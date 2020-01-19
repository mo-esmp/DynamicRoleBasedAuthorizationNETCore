using DynamicAuthorization.Mvc.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace DynamicAuthorization.Mvc.Core.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDynamicAuthorization(this IServiceCollection services, Action<DynamicAuthorizationOptions> options)
        {
            var dynamicAuthorizationOptions = new DynamicAuthorizationOptions();
            options.Invoke(dynamicAuthorizationOptions);
            services.AddSingleton(dynamicAuthorizationOptions);

            services.AddSingleton<IMvcControllerDiscovery, MvcControllerDiscovery>();

            services.Configure<MvcOptions>(mvcOptions =>
            {
                mvcOptions.Filters.Add(typeof(DynamicAuthorizationFilter));
            });

            return services;
        }
    }
}