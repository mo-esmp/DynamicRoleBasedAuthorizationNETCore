using DynamicAuthorization.Mvc.Core;
using DynamicAuthorization.Mvc.Core.Models;
using DynamicAuthorization.Mvc.Ui.Filters;
using DynamicAuthorization.Mvc.Ui.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

namespace DynamicAuthorization.Mvc.Ui
{
    public static class ServiceCollectionExtensions
    {
        public static IDynamicAuthorizationBuilder AddUi(this IDynamicAuthorizationBuilder builder, IMvcBuilder mvcBuilder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            if (mvcBuilder == null)
                throw new ArgumentNullException(nameof(mvcBuilder));

            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.FullName.StartsWith("DynamicAuthorization.Mvc.Ui")).ToList();
            foreach (var assembly in assemblies)
            {
                if (assembly.FullName.Contains("Views"))
                    mvcBuilder.PartManager.ApplicationParts.Add(new CompiledRazorAssemblyPart(assembly));
                else
                {
                    mvcBuilder.PartManager.ApplicationParts.Add(new AssemblyPart(assembly));
                    var manifestResourceNames = assembly.GetManifestResourceNames();
                }
            }

            builder.Services.AddScoped<AddResourcesToViewFilter>();

            if (DynamicAuthorizationOptions.UserClaimType == null)
                builder.Services.AddScoped(
                    typeof(IIdentityService),
                    typeof(IdentityService<,,,>).MakeGenericType(
                        DynamicAuthorizationOptions.DbContextType,
                        DynamicAuthorizationOptions.UserType,
                        DynamicAuthorizationOptions.RoleType,
                        DynamicAuthorizationOptions.KeyType
                        ));
            else
                builder.Services.AddScoped(
                    typeof(IIdentityService),
                    typeof(IdentityService<,,,,,,,,>).MakeGenericType(
                        DynamicAuthorizationOptions.DbContextType,
                        DynamicAuthorizationOptions.UserType,
                        DynamicAuthorizationOptions.RoleType,
                        DynamicAuthorizationOptions.KeyType,
                        DynamicAuthorizationOptions.UserClaimType,
                        DynamicAuthorizationOptions.UserRoleType,
                        DynamicAuthorizationOptions.UserLoginType,
                        DynamicAuthorizationOptions.RoleClaimType,
                        DynamicAuthorizationOptions.UserTokenType
                    ));

            mvcBuilder.ConfigureApplicationPartManager(c =>
            {
                c.FeatureProviders.Add(new GenericRestControllerFeatureProvider());
            });

            builder.Services.Configure<MvcOptions>(mvcOptions =>
            {
                mvcOptions.Conventions.Add(new GenericRestControllerNameConvention());
            });

            return builder;
        }
    }
}