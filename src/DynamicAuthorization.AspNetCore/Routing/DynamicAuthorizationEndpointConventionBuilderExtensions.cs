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
    /// <param name="permission">The permission key, for example <c>orders.approve</c>.</param>
    /// <param name="displayName">The display name shown in the dashboard.</param>
    /// <returns>The builder.</returns>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission, string? displayName = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new PermissionAttribute(permission) { DisplayName = displayName });
    }

    /// <summary>
    ///   Requires an authenticated user who has been granted an operation on the endpoint's resource, replacing the
    ///   inferred operation. For example, <c>RequirePermissionOperation(PermissionOperation.Update)</c> on
    ///   <c>POST /api/orders/{id}</c> requires <c>orders.update</c>.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="operation">The operation, for example <see cref="PermissionOperation.Update"/> or <c>approve</c>.</param>
    /// <param name="displayName">The display name shown in the dashboard.</param>
    /// <returns>The builder.</returns>
    public static TBuilder RequirePermissionOperation<TBuilder>(this TBuilder builder, string operation, string? displayName = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        return builder.WithMetadata(new PermissionAttribute { Operation = operation, DisplayName = displayName });
    }

    /// <summary>
    ///   Names the resource of endpoints, replacing the one derived from the route. Endpoints with the same resource
    ///   share its permissions.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="resource">The resource key, for example <c>orders</c>.</param>
    /// <param name="displayName">The resource name shown in the dashboard.</param>
    /// <returns>The builder.</returns>
    public static TBuilder WithPermissionResource<TBuilder>(this TBuilder builder, string resource, string? displayName = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        return builder.WithMetadata(new PermissionResourceAttribute(resource) { DisplayName = displayName });
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
