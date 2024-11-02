using DynamicAuthorization.Mvc.Core;
using JsonFlatFileDataStore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Reflection;

namespace DynamicAuthorization.Mvc.JsonStore.Extensions
{
    /// <summary>
    ///   JSON data store specific extension methods for <see cref="IDynamicAuthorizationOptionBuilder"/>.
    /// </summary>
    public static class DynamicAuthorizationOptionBuilderExtensions
    {
        private static readonly string Directory = Path.GetDirectoryName(typeof(RoleAccessStore).GetTypeInfo().Assembly.Location);

        /// <summary>
        ///   Configures the Dynamic Authorization to connect to a JSON database.
        /// </summary>
        /// <param name="builder">
        ///   The options builder being used to configure the Dynamic Authorization.
        /// </param>
        /// <param name="options">An action to allow configure JSON data store.</param>
        /// <exception cref="ArgumentNullException">throw if option builder is null.</exception>
        /// <returns>The same authorization option builder so that multiple calls can be chained.</returns>
        public static IDynamicAuthorizationOptionBuilder UseJsonStore(this IDynamicAuthorizationOptionBuilder builder, Action<JsonOptions> options = null)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            var jsonOptions = new JsonOptions();

            if (options != null)
            {
                options.Invoke(jsonOptions);

                if (jsonOptions.FilePath == null)
                    throw new NullReferenceException(nameof(jsonOptions.FilePath));
            }

            if (jsonOptions.FilePath == JsonOptions.DefaultRoleStoreName)
                jsonOptions.FilePath = Path.Combine(Directory, JsonOptions.DefaultRoleStoreName);

            builder.Services.AddSingleton(jsonOptions);
            builder.Services.AddSingleton(provider => new DataStore(jsonOptions.FilePath, keyProperty: "roleId"));
            builder.Services.AddScoped<IRoleAccessStore, RoleAccessStore>();

            return builder;
        }
    }
}