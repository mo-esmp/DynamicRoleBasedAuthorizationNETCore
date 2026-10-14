namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Sets the group that the permissions of a controller or page model are listed under in the dashboard.
/// </summary>
/// <remarks>
///   Without this attribute, controllers are grouped by their <see cref="System.ComponentModel.DisplayNameAttribute"/>
///   (or name) and pages by their folder.
/// </remarks>
/// <param name="name">The group name.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PermissionGroupAttribute(string name) : Attribute
{
    /// <summary>
    ///   Gets the group name.
    /// </summary>
    public string Name { get; } = name;
}
