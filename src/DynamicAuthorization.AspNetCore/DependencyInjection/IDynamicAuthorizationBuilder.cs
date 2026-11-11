using Microsoft.Extensions.DependencyInjection;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Configures dynamic authorization services.
/// </summary>
public interface IDynamicAuthorizationBuilder
{
    /// <summary>
    ///   Gets the <see cref="IServiceCollection"/> where dynamic authorization services are configured.
    /// </summary>
    IServiceCollection Services { get; }
}

internal sealed class DynamicAuthorizationBuilder(IServiceCollection services) : IDynamicAuthorizationBuilder
{
    public IServiceCollection Services { get; } = services;
}
