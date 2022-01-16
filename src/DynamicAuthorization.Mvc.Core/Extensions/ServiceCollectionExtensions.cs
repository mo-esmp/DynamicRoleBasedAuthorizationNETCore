using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace DynamicAuthorization.Mvc.Core.Extensions
{
    /// <summary>
    ///   Extension methods for setting up Dynamic Authorization related services in an <see cref="IServiceCollection"/>.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        ///   Registers the Dynamic Authorization as a service in the <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="services">The services.</param>
        /// <param name="optionsBuilder">
        ///   An action to configure the <see cref="DynamicAuthorizationOptionBuilder"/> for the
        ///   Dynamic Authorization.
        /// </param>
        /// <param name="defaultAdminUser">
        ///   The default user to access all controllers without needs for creating role and related
        ///   accesses in database.
        /// </param>
        /// <exception cref="ArgumentNullException">services</exception>
        /// <exception cref="ArgumentNullException">optionsBuilder</exception>
        /// <returns>The same service collection so that multiple calls can be chained.</returns>
        public static IDynamicAuthorizationOptionBuilder AddDynamicAuthorization<TDbContext>(
            this IServiceCollection services,
            Action<DynamicAuthorizationOptionBuilder> optionsBuilder,
            string defaultAdminUser
            ) where TDbContext : DbContext
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            if (optionsBuilder == null)
                throw new ArgumentNullException(nameof(optionsBuilder));

            if (defaultAdminUser == null)
                throw new ArgumentNullException(nameof(defaultAdminUser));

            var baseType = typeof(TDbContext).BaseType;
            var paramsLength = baseType.GetGenericArguments().Length;
            Type userType;
            Type roleType;
            Type keyType;

            switch (paramsLength)
            {
                case 1:
                    userType = baseType.GetGenericArguments()[0];
                    DynamicAuthorizationOptions.UserType = userType;
                    DynamicAuthorizationOptions.RoleType = typeof(IdentityRole);
                    DynamicAuthorizationOptions.KeyType = typeof(string);
                    services.Configure<MvcOptions>(mvcOptions =>
                    {
                        mvcOptions.Filters.Add(typeof(DynamicAuthorizationFilter<,>).MakeGenericType(typeof(TDbContext), userType));
                    });
                    break;

                case 3:
                    userType = baseType.GetGenericArguments()[0];
                    roleType = baseType.GetGenericArguments()[1];
                    keyType = baseType.GetGenericArguments()[2];
                    DynamicAuthorizationOptions.UserType = userType;
                    DynamicAuthorizationOptions.RoleType = roleType;
                    DynamicAuthorizationOptions.KeyType = keyType;
                    services.Configure<MvcOptions>(mvcOptions =>
                    {
                        mvcOptions.Filters.Add(typeof(DynamicAuthorizationFilter<,,,>)
                            .MakeGenericType(typeof(TDbContext), userType, roleType, keyType));
                    });
                    break;

                case 8:
                    userType = baseType.GetGenericArguments()[0];
                    roleType = baseType.GetGenericArguments()[1];
                    keyType = baseType.GetGenericArguments()[2];
                    var userClaimType = baseType.GetGenericArguments()[3];
                    var userRoleType = baseType.GetGenericArguments()[4];
                    var userLoginType = baseType.GetGenericArguments()[5];
                    var roleClaimType = baseType.GetGenericArguments()[6];
                    var userTokenType = baseType.GetGenericArguments()[7];
                    DynamicAuthorizationOptions.UserType = userType;
                    DynamicAuthorizationOptions.RoleType = roleType;
                    DynamicAuthorizationOptions.KeyType = keyType;
                    DynamicAuthorizationOptions.UserClaimType = userClaimType;
                    DynamicAuthorizationOptions.UserRoleType = userRoleType;
                    DynamicAuthorizationOptions.UserLoginType = userLoginType;
                    DynamicAuthorizationOptions.RoleClaimType = roleClaimType;
                    DynamicAuthorizationOptions.UserTokenType = userTokenType;
                    services.Configure<MvcOptions>(mvcOptions =>
                    {
                        mvcOptions.Filters.Add(typeof(DynamicAuthorizationFilter<,,,,,,,,>)
                            .MakeGenericType(typeof(TDbContext), userType, roleType, keyType, userClaimType, userRoleType, userLoginType, roleClaimType, userTokenType));
                    });
                    break;

                default:
                    DynamicAuthorizationOptions.UserType = typeof(IdentityUser);
                    DynamicAuthorizationOptions.RoleType = typeof(IdentityRole);
                    DynamicAuthorizationOptions.KeyType = typeof(string);
                    services.Configure<MvcOptions>(mvcOptions =>
                    {
                        mvcOptions.Filters.Add(typeof(DynamicAuthorizationFilter<>).MakeGenericType(typeof(TDbContext)));
                    });
                    break;
            }

            services.AddSingleton<IMvcControllerDiscovery, MvcControllerDiscovery>();

            IDynamicAuthorizationOptionBuilder builder = new DynamicAuthorizationOptionBuilder(services);

            DynamicAuthorizationOptions.DbContextType = typeof(TDbContext);

            return builder;
        }
    }
}