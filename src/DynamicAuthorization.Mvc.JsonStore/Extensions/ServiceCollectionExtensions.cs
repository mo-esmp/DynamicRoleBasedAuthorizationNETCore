using DynamicAuthorization.Mvc.Core;
using JsonFlatFileDataStore;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace DynamicAuthorization.Mvc.JsonStore.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDynamicAuthorization(this IServiceCollection services)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            AddRequiredServices(services, new JsonOptions());

            return services;
        }

        public static IServiceCollection AddDynamicAuthorization(this IServiceCollection services, Action<JsonOptions> options)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var jsonOptions = new JsonOptions();
            options.Invoke(jsonOptions);

            if (jsonOptions.FileName == null)
                throw new NullReferenceException(nameof(jsonOptions.FileName));

            AddRequiredServices(services, jsonOptions);

            return services;
        }

        private static void AddRequiredServices(IServiceCollection services, JsonOptions jsonOptions)
        {
            services.AddSingleton(jsonOptions);
            services.AddSingleton(new DataStore(jsonOptions.FileName));
            services.AddScoped<IRoleAccessStore, RoleAccessStore>();
        }
    }
}