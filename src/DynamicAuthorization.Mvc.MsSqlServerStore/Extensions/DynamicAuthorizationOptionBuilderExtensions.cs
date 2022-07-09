using DynamicAuthorization.Mvc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace DynamicAuthorization.Mvc.MsSqlServerStore.Extensions
{
    /// <summary>
    ///   SQL Server data store specific extension methods for <see cref="IDynamicAuthorizationOptionBuilder"/>.
    /// </summary>
    public static class DynamicAuthorizationOptionBuilderExtensions
    {
        /// <summary>
        ///   Configures the Dynamic Authorization to connect to a SQL Server database.
        /// </summary>
        /// <param name="builder">
        ///   The options builder being used to configure the Dynamic Authorization.
        /// </param>
        /// <param name="options">An action to allow configure SQL Server data store.</param>
        /// <exception cref="ArgumentNullException">throw if options builder is null.</exception>
        /// <exception cref="ArgumentNullException">throw is options is null.</exception>
        /// <returns>The same authorization option builder so that multiple calls can be chained.</returns>
        public static IDynamicAuthorizationOptionBuilder UseSqlServerStore(this IDynamicAuthorizationOptionBuilder builder, Action<SqlOptions> options)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var serviceProvider = builder.Services.BuildServiceProvider();
            var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetService<ILogger<SqlTableCreator>>();

            var sqlOptions = new SqlOptions();
            options.Invoke(sqlOptions);

            var tableCreator = new SqlTableCreator(sqlOptions, logger);
            tableCreator.CreateTable();

            scope.Dispose();
            serviceProvider.Dispose();

            builder.Services.AddSingleton(sqlOptions);
            builder.Services.AddScoped<IRoleAccessStore, RoleAccessStore>();

            return builder;
        }
    }
}