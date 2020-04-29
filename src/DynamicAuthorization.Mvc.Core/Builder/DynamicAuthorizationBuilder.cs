using Microsoft.Extensions.DependencyInjection;

namespace DynamicAuthorization.Mvc.Core.Builder
{
    internal class DynamicAuthorizationBuilder : IDynamicAuthorizationBuilder
    {
        public DynamicAuthorizationBuilder(IServiceCollection services)
        {
            Services = services;
        }

        public IServiceCollection Services { get; }
    }
}