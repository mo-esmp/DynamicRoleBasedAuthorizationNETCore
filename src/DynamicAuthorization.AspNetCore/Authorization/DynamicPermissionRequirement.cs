using Microsoft.AspNetCore.Authorization;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Requires the user to have been granted a permission.
/// </summary>
/// <remarks>
///   Without an explicit permission, the permission is resolved from the endpoint being authorized. Add the
///   requirement to your own policies to combine dynamic permissions with other requirements:
///   <code>
///   options.AddPolicy("CanExportReports", policy => policy.AddRequirements(new DynamicPermissionRequirement("reports.export")));
///   </code>
/// </remarks>
public sealed class DynamicPermissionRequirement : IAuthorizationRequirement
{
    /// <summary>
    ///   Initializes a new instance of the <see cref="DynamicPermissionRequirement"/> class that requires the
    ///   permission of the endpoint being authorized.
    /// </summary>
    public DynamicPermissionRequirement()
    {
    }

    /// <summary>
    ///   Initializes a new instance of the <see cref="DynamicPermissionRequirement"/> class that requires the given
    ///   permission.
    /// </summary>
    /// <param name="permission">The permission key.</param>
    public DynamicPermissionRequirement(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Permission = permission;
    }

    /// <summary>
    ///   Gets the required permission, or <see langword="null"/> to require the permission of the endpoint being
    ///   authorized.
    /// </summary>
    public string? Permission { get; }

    /// <inheritdoc />
    public override string ToString() =>
        Permission is null
            ? $"{nameof(DynamicPermissionRequirement)}: requires the permission of the endpoint."
            : $"{nameof(DynamicPermissionRequirement)}: requires the '{Permission}' permission.";
}
