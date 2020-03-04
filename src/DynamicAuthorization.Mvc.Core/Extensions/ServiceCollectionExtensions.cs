using DynamicAuthorization.Mvc.Core.Builder;
using DynamicAuthorization.Mvc.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace DynamicAuthorization.Mvc.Core.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IDynamicAuthorizationBuilder AddDynamicAuthorization(this IServiceCollection services, Action<DynamicAuthorizationOptions> options)
        {
            var dynamicAuthorizationOptions = new DynamicAuthorizationOptions();
            options.Invoke(dynamicAuthorizationOptions);
            services.AddSingleton(dynamicAuthorizationOptions);

            services.AddSingleton<IMvcControllerDiscovery, MvcControllerDiscovery>();

            services.Configure<MvcOptions>(mvcOptions =>
            {
                mvcOptions.Filters.Add(typeof(DynamicAuthorizationFilter));
            });

            IDynamicAuthorizationBuilder builder = new DynamicAuthorizationBuilder(services);

            return builder;
        }
    }
}