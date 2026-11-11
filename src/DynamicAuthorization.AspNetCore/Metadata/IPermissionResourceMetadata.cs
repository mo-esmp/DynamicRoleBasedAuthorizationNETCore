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
