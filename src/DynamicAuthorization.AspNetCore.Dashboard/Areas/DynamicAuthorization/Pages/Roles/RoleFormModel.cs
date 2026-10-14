using System.ComponentModel.DataAnnotations;
using DynamicAuthorization.AspNetCore.Management;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore.Dashboard.Pages.Roles;

/// <summary>
///   The role form shared by the create and edit pages.
/// </summary>
internal abstract class RoleFormModel : DashboardPageModel
{
    private const string UnknownPermissionsGroup = "Not found in this application";
    private const int OpenAllGroupsThreshold = 3;

    [BindProperty]
    public RoleInput Input { get; set; } = new();

    public string? RoleId { get; protected set; }

    public string RoleName { get; protected set; } = string.Empty;

    public bool IsNew => RoleId is null;

    public bool IsSuperAdminRole { get; protected set; }

    public bool OperationsImplyRead { get; private set; }

    public IReadOnlyList<ResourceRowView> Resources { get; private set; } = [];

    public IReadOnlyList<PermissionGroupView> Groups { get; private set; } = [];

    protected void SetRole(RoleDetails role)
    {
        RoleId = role.Id;
        RoleName = role.Name;
        IsSuperAdminRole = GetService<IOptions<DynamicAuthorizationOptions>>().Value.SuperAdminRoles.Contains(role.Name);
    }

    protected IActionResult Redisplay(ManagementResult? result = null)
    {
        foreach (string error in result?.Errors ?? [])
        {
            ModelState.AddModelError(string.Empty, error);
        }

        BuildPermissions();
        return Page();
    }

    /// <summary>
    ///   Lays out the grantable permissions: <c>{resource}.{operation}</c> permissions as a resource × operation
    ///   matrix, and every other permission as a tree grouped by resource or folder.
    /// </summary>
    protected void BuildPermissions()
    {
        HashSet<string> granted = new(Input.Permissions, StringComparer.OrdinalIgnoreCase);
        PermissionCatalog catalog = GetService<IPermissionCatalogProvider>().GetCatalog();
        OperationsImplyRead = GetService<IOptions<DynamicAuthorizationOptions>>().Value.OperationsImplyRead;

        PermissionItemView ToItem(PermissionDescriptor p) =>
            new(p.Permission, p.DisplayName, p.Operation, p.Source, granted.Contains(p.Permission));

        Resources =
        [
            .. catalog.Permissions
                .Where(p => p.Resource is not null && p.Operation is not null)
                .GroupBy(p => p.Resource!, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ResourceRowView(
                    g.Key,
                    g.First().Group,
                    [
                        .. PermissionOperation.Standard
                            .Select(op =>
                                g.FirstOrDefault(p =>
                                        string.Equals(p.Operation, op, StringComparison.OrdinalIgnoreCase)) is
                                    { } p
                                    ? ToItem(p)
                                    : null)
                    ],
                    [.. g.Where(p => !PermissionOperation.IsStandard(p.Operation!)).Select(ToItem)]))
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
        ];

        List<PermissionGroupView> groups =
        [
            .. catalog.Permissions
                .Where(p => p.Resource is null || p.Operation is null)
                .GroupBy(p => p.Group, StringComparer.OrdinalIgnoreCase)
                .Select(g => new PermissionGroupView(g.Key, null, [.. g.Select(ToItem)]))
        ];

        // Keep grants this application does not know about (renamed endpoints, or another application sharing the
        // database) visible, so that saving the role does not silently revoke them.
        List<string> unknown =
        [
            .. granted.Where(p => catalog.FindPermission(p) is null).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
        ];
        if (unknown.Count > 0)
        {
            groups.Add(new PermissionGroupView(
                UnknownPermissionsGroup,
                "These permissions are granted but nothing in this application declares them. They may belong to a renamed or removed endpoint, or to another application that shares this database. Clear them to revoke them.",
                [.. unknown.Select(p => new PermissionItemView(p, p, null, null, true))]));
        }

        bool openAll = groups.Count <= OpenAllGroupsThreshold;
        Groups = [.. groups.Select(g => g with { IsOpen = openAll || g.Items.Any(i => i.Granted) })];
    }
}

internal sealed class RoleInput
{
    [Required(ErrorMessage = "Enter a name for the role.")]
    [StringLength(256, ErrorMessage = "The name must be at most {1} characters long.")]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    public List<string> Permissions { get; set; } = [];
}

/// <summary>
///   A resource and its operations: one slot per standard operation (read, create, update, delete), empty when no
///   endpoint uses it, then the custom operations.
/// </summary>
internal sealed record ResourceRowView(
    string Resource,
    string Name,
    IReadOnlyList<PermissionItemView?> StandardOperations,
    IReadOnlyList<PermissionItemView> CustomOperations)
{
    public IEnumerable<PermissionItemView> All => StandardOperations.OfType<PermissionItemView>().Concat(CustomOperations);
}

internal sealed record PermissionGroupView(string Name, string? Description, IReadOnlyList<PermissionItemView> Items)
{
    public bool IsOpen { get; init; }
}

internal sealed record PermissionItemView(string Permission, string DisplayName, string? Operation, PermissionSource? Source, bool Granted);
