using DynamicAuthorization.AspNetCore;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
///   Extension methods for applying dynamic authorization to endpoints, such as Minimal APIs and route groups.
/// </summary>
public static class DynamicAuthorizationEndpointConventionBuilderExtensions
{
    /// <summary>
    ///   Requires an authenticated user who has been granted the permission of each endpoint. Use this for endpoints
    ///   that should be permission-checked even when <see cref="DynamicAuthorizationOptions.ApplyToDefaultPolicy"/>
    ///   is disabled.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The builder.</returns>
    public static TBuilder RequireDynamicAuthorization<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.RequireAuthorization(DynamicAuthorizationDefaults.PolicyName);
    }

    /// <summary>
    ///   Requires an authenticated user who has been granted an explicit permission.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="permission">The permission key, for example <c>orders.delete</c>.</param>
    /// <param name="displayName">The display name shown in the dashboard.</param>
    /// <param name="group">The group the permission is listed under in the dashboard.</param>
    /// <returns>The builder.</returns>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission, string? displayName = null, string? group = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new PermissionAttribute(permission) { DisplayName = displayName, Group = group });
    }

    /// <summary>
    ///   Excludes endpoints from the dynamic permission check. Other authorization, such as requiring an
    ///   authenticated user, still applies.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The builder.</returns>
    public static TBuilder IgnoreDynamicAuthorization<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new IgnoreDynamicAuthorizationAttribute());
    }
}
