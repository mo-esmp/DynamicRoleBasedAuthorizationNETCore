using Microsoft.AspNetCore.Authorization;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Metadata that overrides the permission required to access an endpoint.
/// </summary>
public interface IPermissionMetadata
{
    /// <summary>
    ///   Gets the explicit permission key, or <see langword="null"/> to derive it from the convention.
    /// </summary>
    string? Permission { get; }

    /// <summary>
    ///   Gets the operation that replaces the inferred one, keeping the resource, or <see langword="null"/> to infer it.
    ///   Only used by the <see cref="PermissionConvention.Crud"/> convention.
    /// </summary>
    string? Operation { get; }

    /// <summary>
    ///   Gets the display name shown in the dashboard.
    /// </summary>
    string? DisplayName { get; }
}

/// <summary>
///   Requires an authenticated user who has been granted a permission, and optionally overrides which one.
/// </summary>
/// <remarks>
///   <code>
///   [Permission]                                          // the permission derived by the convention
///   [Permission("orders.approve")]                        // an explicit permission key
///   [Permission(Operation = PermissionOperation.Update)]  // the derived resource with another operation
///   </code>
///   Apply it to controllers, actions, page models and page handler methods. The most specific attribute wins.
///   A handler method attribute only takes effect when its page requires a permission.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PermissionAttribute : AuthorizeAttribute, IPermissionMetadata
{
    /// <summary>
    ///   Initializes a new instance of the <see cref="PermissionAttribute"/> class that requires the permission
    ///   derived by the convention, or the one set with <see cref="Operation"/>.
    /// </summary>
    public PermissionAttribute()
        : base(DynamicAuthorizationDefaults.PolicyName)
    {
    }

    /// <summary>
    ///   Initializes a new instance of the <see cref="PermissionAttribute"/> class that requires an explicit permission.
    /// </summary>
    /// <param name="permission">The permission key, for example <c>orders.approve</c>.</param>
    public PermissionAttribute(string permission)
        : base(DynamicAuthorizationDefaults.PolicyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Permission = permission;
    }

    /// <inheritdoc />
    public string? Permission { get; }

    /// <inheritdoc />
    public string? Operation { get; set; }

    /// <inheritdoc />
    public string? DisplayName { get; set; }
}
