using DynamicAuthorization.AspNetCore;
using DynamicAuthorization.AspNetCore.EntityFrameworkCore;
using DynamicAuthorization.AspNetCore.Management;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///   Extension methods for storing dynamic authorization data with Entity Framework Core.
/// </summary>
public static class DynamicAuthorizationEntityFrameworkBuilderExtensions
{
    /// <summary>
    ///   Stores role permissions in an ASP.NET Core Identity context and resolves user roles from it.
    /// </summary>
    /// <typeparam name="TContext">
    ///   The context, which must derive from <c>IdentityDbContext</c> with roles (directly or through a base class) and
    ///   call <see cref="DynamicAuthorizationModelBuilderExtensions.EnableDynamicAuthorization"/> in <c>OnModelCreating</c>.
    /// </typeparam>
    /// <param name="builder">The dynamic authorization builder.</param>
    /// <returns>The same builder so that multiple calls can be chained.</returns>
    public static IDynamicAuthorizationBuilder AddEntityFrameworkStore<TContext>(this IDynamicAuthorizationBuilder builder)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        IdentityModelTypes types = IdentityModelTypes.FromContext(typeof(TContext));
        IServiceCollection services = builder.Services;

        services.TryAddSingleton(types);
        services.TryAddScoped(
            typeof(IPermissionStore),
            typeof(EntityFrameworkPermissionStore<,>).MakeGenericType(types.Context, types.Key));
        services.TryAddScoped(
            typeof(IUserRoleProvider),
            typeof(EntityFrameworkUserRoleProvider<,,,>).MakeGenericType(types.Context, types.Role, types.Key, types.UserRole));
        services.TryAddScoped(
            typeof(IRoleManagementService),
            typeof(EntityFrameworkRoleManagementService<,,,>).MakeGenericType(types.Context, types.Role, types.Key, types.UserRole));
        services.TryAddScoped(
            typeof(IUserManagementService),
            typeof(EntityFrameworkUserManagementService<,,,,>).MakeGenericType(types.Context, types.User, types.Role, types.Key, types.UserRole));

        return builder;
    }
}
