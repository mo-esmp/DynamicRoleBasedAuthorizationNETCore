namespace DynamicAuthorization.AspNetCore.EntityFrameworkCore;

/// <summary>
///   A permission granted to a role.
/// </summary>
/// <typeparam name="TKey">The type of the role key.</typeparam>
public class RolePermission<TKey>
    where TKey : IEquatable<TKey>
{
    /// <summary>
    ///   Gets or sets the identifier of the role the permission is granted to.
    /// </summary>
    public virtual TKey RoleId { get; set; } = default!;

    /// <summary>
    ///   Gets or sets the permission key.
    /// </summary>
    public virtual string Permission { get; set; } = string.Empty;
}

/// <summary>
///   Options for the dynamic authorization entity model.
/// </summary>
public sealed class DynamicAuthorizationModelOptions
{
    /// <summary>
    ///   The maximum length of a permission key.
    /// </summary>
    public const int MaxPermissionLength = 256;

    /// <summary>
    ///   Gets or sets the name of the table that stores role permissions. Defaults to <c>AspNetRolePermissions</c>.
    /// </summary>
    public string TableName { get; set; } = "AspNetRolePermissions";

    /// <summary>
    ///   Gets or sets the schema of the role permissions table. Defaults to the model's default schema.
    /// </summary>
    public string? Schema { get; set; }
}
