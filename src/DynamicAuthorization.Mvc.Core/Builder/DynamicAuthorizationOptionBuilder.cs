using Microsoft.Extensions.DependencyInjection;
using System;

namespace DynamicAuthorization.Mvc.Core
{
    /// <inheritdoc cref="IDynamicAuthorizationOptionBuilder"/>
    public class DynamicAuthorizationOptionBuilder : IDynamicAuthorizationOptionBuilder
    {
        private readonly IServiceCollection _services;

        public DynamicAuthorizationOptionBuilder(IServiceCollection services)
        {
            _services = services;
        }

        IServiceCollection IDynamicAuthorizationOptionBuilder.Services => _services;

        public IDynamicAuthorizationOptionBuilder AddDefaultAllowedAdminsAndRoles(Action<DynamicAuthorizationOptions> allowedUserRoleOptions)
        {
            if (allowedUserRoleOptions == null)
                throw new ArgumentNullException(nameof(allowedUserRoleOptions));

            var dynamicAuthorizationOptions = new DynamicAuthorizationOptions();
            allowedUserRoleOptions.Invoke(dynamicAuthorizationOptions);

            if (dynamicAuthorizationOptions.DefaultAllowedAdmins == null && dynamicAuthorizationOptions.DefaultAllowedRoles == null)
                throw new InvalidOperationException($"One of \"{nameof(DynamicAuthorizationOptions.DefaultAllowedAdmins)}\" or" +
                                                    $" \"{nameof(DynamicAuthorizationOptions.DefaultAllowedRoles)}\" properties should be initialized.");

            DynamicAuthorizationOptionsInternals.DefaultAllowedAdmins = dynamicAuthorizationOptions.DefaultAllowedAdmins;
            DynamicAuthorizationOptionsInternals.DefaultAllowedRoles = dynamicAuthorizationOptions.DefaultAllowedRoles;

            return this;
        }
    }
}