namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   The kind of endpoint a permission was discovered from.
/// </summary>
public enum PermissionSource
{
    /// <summary>An MVC controller action.</summary>
    Controller,

    /// <summary>A Razor Page.</summary>
    Page,

    /// <summary>Any other endpoint, for example a Minimal API endpoint.</summary>
    Endpoint,

    /// <summary>A permission added with <see cref="DynamicAuthorizationOptions.AddPermission"/>.</summary>
    Custom,
}
