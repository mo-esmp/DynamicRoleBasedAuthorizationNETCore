using DynamicAuthorization.Mvc.Core.Builder;
using DynamicAuthorization.Mvc.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace DynamicAuthorization.Mvc.Core.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IDynamicAuthorizationBuilder AddDynamicAuthorization<TDbContext>(this IServiceCollection services,
            Action<DynamicAuthorizationOptions> options)
            where TDbContext : DbContext
        {
            var dynamicAuthorizationOptions = new DynamicAuthorizationOptions();
            options.Invoke(dynamicAuthorizationOptions);
            services.AddSingleton(dynamicAuthorizationOptions);

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

            IDynamicAuthorizationBuilder builder = new DynamicAuthorizationBuilder(services);

            DynamicAuthorizationOptions.DbContextType = typeof(TDbContext);

            return builder;
        }
    }
}