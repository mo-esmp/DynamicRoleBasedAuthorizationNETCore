using Microsoft.AspNetCore.Authorization;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Metadata that names the permission required to access an endpoint.
/// </summary>
public interface IPermissionMetadata
{
    /// <summary>
    ///   Gets the permission key.
    /// </summary>
    string Permission { get; }

    /// <summary>
    ///   Gets the display name shown in the dashboard.
    /// </summary>
    string? DisplayName { get; }

    /// <summary>
    ///   Gets the group the permission is listed under in the dashboard.
    /// </summary>
    string? Group { get; }
}

/// <summary>
///   Requires an authenticated user who has been granted the given permission.
/// </summary>
/// <remarks>
///   Use an explicit permission when several endpoints should be granted together, or when the permission should
///   survive renaming the controller, action or page. When applied to both a controller and an action, the action
///   attribute wins.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PermissionAttribute : AuthorizeAttribute, IPermissionMetadata
{
    /// <summary>
    ///   Initializes a new instance of the <see cref="PermissionAttribute"/> class.
    /// </summary>
    /// <param name="permission">The permission key, for example <c>orders.delete</c>.</param>
    public PermissionAttribute(string permission)
        : base(DynamicAuthorizationDefaults.PolicyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Permission = permission;
    }

    /// <inheritdoc />
    public string Permission { get; }

    /// <inheritdoc />
    public string? DisplayName { get; set; }

    /// <inheritdoc />
    public string? Group { get; set; }
}
