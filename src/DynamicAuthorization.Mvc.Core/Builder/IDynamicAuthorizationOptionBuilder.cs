using Microsoft.Extensions.DependencyInjection;

namespace DynamicAuthorization.Mvc.Core
{
    /// <summary>
    /// An interface for configuring dynamic authorization options.
    /// </summary>
    public interface IDynamicAuthorizationOptionBuilder
    {
        /// <summary>
        /// Gets the <see cref="IServiceCollection"/> where essential services are configured.
        /// </summary>
        IServiceCollection Services { get; }
    }
}