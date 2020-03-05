using DynamicAuthorization.Mvc.Core;
using JsonFlatFileDataStore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;

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

            if (jsonOptions.FilePath == null)
                throw new NullReferenceException(nameof(jsonOptions.FilePath));

            AddRequiredServices(builder.Services, jsonOptions);

            return builder;
        }

        private static void AddRequiredServices(IServiceCollection services, JsonOptions jsonOptions)
        {
            if (jsonOptions.FilePath == "RoleAccess.json")
                jsonOptions.FilePath = $"{Directory.GetCurrentDirectory()}\\{jsonOptions.FilePath}";

            services.AddSingleton(jsonOptions);
            services.AddSingleton(provider => new DataStore(jsonOptions.FilePath, keyProperty: "roleId"));
            services.AddScoped<IRoleAccessStore, RoleAccessStore>();
        }
    }
}