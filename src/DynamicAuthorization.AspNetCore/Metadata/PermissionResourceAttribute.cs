namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Metadata that names the resource of an endpoint.
/// </summary>
public interface IPermissionResourceMetadata
{
    /// <summary>
    ///   Gets the resource key, for example <c>orders</c>.
    /// </summary>
    string Resource { get; }

    /// <summary>
    ///   Gets the resource name shown in the dashboard.
    /// </summary>
    string? DisplayName { get; }
}

/// <summary>
///   Names the resource of a controller or page model, replacing the one derived from the controller name or page
///   folder. Endpoints with the same resource share its permissions, for example <c>orders.read</c>.
/// </summary>
/// <remarks>
///   With the <see cref="PermissionConvention.Endpoint"/> convention, only the display name is used, to group the
///   permissions in the dashboard.
/// </remarks>
/// <param name="resource">The resource key, for example <c>orders</c> or <c>admin.reports</c>.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PermissionResourceAttribute(string resource) : Attribute, IPermissionResourceMetadata
{
    /// <inheritdoc />
    public string Resource { get; } = resource;

    /// <inheritdoc />
    public string? DisplayName { get; set; }
}
