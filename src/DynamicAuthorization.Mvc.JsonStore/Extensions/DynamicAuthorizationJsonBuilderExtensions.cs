using DynamicAuthorization.Mvc.Core;
using JsonFlatFileDataStore;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace DynamicAuthorization.Mvc.JsonStore.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IDynamicAuthorizationBuilder AddJsonStore(this IDynamicAuthorizationBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            AddRequiredServices(builder.Services, new JsonOptions());

            return builder;
        }

        public static IDynamicAuthorizationBuilder AddJsonStore(this IDynamicAuthorizationBuilder builder, Action<JsonOptions> options)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var jsonOptions = new JsonOptions();
            options.Invoke(jsonOptions);

            if (jsonOptions.FileName == null)
                throw new NullReferenceException(nameof(jsonOptions.FileName));

            AddRequiredServices(builder.Services, jsonOptions);

            return builder;
        }

        private static void AddRequiredServices(IServiceCollection services, JsonOptions jsonOptions)
        {
            services.AddSingleton(jsonOptions);
            services.AddSingleton(new DataStore(jsonOptions.FileName));
            services.AddScoped<IRoleAccessStore, RoleAccessStore>();
        }
    }
}