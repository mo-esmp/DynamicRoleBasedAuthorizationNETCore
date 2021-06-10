using Microsoft.Extensions.DependencyInjection;

namespace DynamicAuthorization.Mvc.Core
{
    /// <summary>
    /// An interface for configuring dynamic authorization services.
    /// </summary>
    public interface IDynamicAuthorizationBuilder
    {
        /// <summary>
        /// Gets the <see cref="IServiceCollection"/> where essential services are configured.
        /// </summary>
        IServiceCollection Services { get; }
    }
}