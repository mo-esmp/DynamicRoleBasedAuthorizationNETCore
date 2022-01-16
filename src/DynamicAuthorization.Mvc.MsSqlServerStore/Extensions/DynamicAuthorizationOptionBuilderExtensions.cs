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
        /// <param name="optionsBuilder">
        ///   The optionsBuilder being used to configure the Dynamic Authorization.
        /// </param>
        /// <param name="options">An action to allow configure SQL Server data store.</param>
        /// <exception cref="ArgumentNullException">throw if optionsBuilder is null.</exception>
        /// <exception cref="ArgumentNullException">throw is options is null.</exception>
        public static IDynamicAuthorizationOptionBuilder UseSqlServer(this IDynamicAuthorizationOptionBuilder optionsBuilder, Action<SqlOptions> options)
        {
            if (optionsBuilder == null)
                throw new ArgumentNullException(nameof(optionsBuilder));

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var serviceProvider = optionsBuilder.Services.BuildServiceProvider();
            var scope = serviceProvider.CreateScope();
            var logger = scope.ServiceProvider.GetService<ILogger<SqlTableCreator>>();

            var sqlOptions = new SqlOptions();
            options.Invoke(sqlOptions);

            var tableCreator = new SqlTableCreator(sqlOptions, logger);
            tableCreator.CreateTable();

            scope.Dispose();
            serviceProvider.Dispose();

            optionsBuilder.Services.AddSingleton(sqlOptions);
            optionsBuilder.Services.AddScoped<IRoleAccessStore, RoleAccessStore>();

            return optionsBuilder;
        }
    }
}