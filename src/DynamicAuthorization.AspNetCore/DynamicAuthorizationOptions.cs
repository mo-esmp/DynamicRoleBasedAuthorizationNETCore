namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Options for dynamic authorization.
/// </summary>
public sealed class DynamicAuthorizationOptions
{
    /// <summary>
    ///   Gets the user names that are granted every permission. Use this to bootstrap the first administrator
    ///   before any role has been configured. Comparison is case-insensitive.
    /// </summary>
    public ISet<string> SuperAdminUsers { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///   Gets the role names whose members are granted every permission. Comparison is case-insensitive.
    /// </summary>
    public ISet<string> SuperAdminRoles { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///   Gets or sets a value indicating whether the dynamic permission check is added to the default
    ///   authorization policy. When <see langword="true"/> (the default), every endpoint protected by a plain
    ///   <c>[Authorize]</c> attribute, <c>RequireAuthorization()</c> or <c>AuthorizeFolder()</c> requires a
    ///   granted permission, without any further configuration.
    /// </summary>
    public bool ApplyToDefaultPolicy { get; set; } = true;

    /// <summary>
    ///   Gets the areas whose endpoints are never permission-checked; they only require an authenticated user.
    ///   Contains <c>Identity</c> by default, so the ASP.NET Core Identity UI (for example, the account management
    ///   pages) stays available to every signed-in user.
    /// </summary>
    public ISet<string> IgnoredAreas { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Identity" };

    /// <summary>
    ///   Gets or sets how long user role memberships and role permissions are cached in memory.
    ///   Changes made through the management services are applied immediately on the current instance;
    ///   other changes (and changes made on other instances) become visible when the cache expires.
    ///   <see cref="TimeSpan.Zero"/> disables caching. Defaults to one minute.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///   Gets permissions that are not bound to an endpoint, for example permissions checked imperatively with
    ///   <see cref="IPermissionChecker"/> or the <c>secure-content</c> tag helper.
    /// </summary>
    public IList<PermissionDefinition> Permissions { get; } = new List<PermissionDefinition>();

    /// <summary>
    ///   Adds a permission that is not bound to an endpoint.
    /// </summary>
    /// <param name="permission">The permission key, for example <c>reports.export</c>.</param>
    /// <param name="displayName">The display name shown in the dashboard.</param>
    /// <param name="group">The group the permission is listed under in the dashboard.</param>
    /// <returns>The same options instance so that multiple calls can be chained.</returns>
    public DynamicAuthorizationOptions AddPermission(string permission, string? displayName = null, string? group = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Permissions.Add(new PermissionDefinition(permission, displayName, group));

        return this;
    }
}

/// <summary>
///   A permission that is not bound to an endpoint.
/// </summary>
/// <param name="Permission">The permission key.</param>
/// <param name="DisplayName">The display name shown in the dashboard.</param>
/// <param name="Group">The group the permission is listed under in the dashboard.</param>
public sealed record PermissionDefinition(string Permission, string? DisplayName = null, string? Group = null);
