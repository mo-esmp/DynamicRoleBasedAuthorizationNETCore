using Microsoft.Extensions.DependencyInjection;

namespace DynamicAuthorization.Mvc.Core
{
    internal class DynamicAuthorizationOptionBuilder : IDynamicAuthorizationOptionBuilder
    {
        public DynamicAuthorizationOptionBuilder(IServiceCollection services)
        {
            Services = services;
        }

        public IServiceCollection Services { get; }
    }
}