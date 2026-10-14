using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DynamicAuthorization.AspNetCore.Management;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore.Dashboard.Pages.Roles;

[DisplayName("Create, edit and delete roles")]
[PermissionGroup(DashboardConstants.PermissionGroup)]
internal sealed class EditModel : DashboardPageModel
{
    private const string UnknownPermissionsGroup = "Not found in this application";
    private const int OpenAllGroupsThreshold = 3;

    [BindProperty]
    public RoleInput Input { get; set; } = new();

    public string? RoleId { get; private set; }

    public string RoleName { get; private set; } = string.Empty;

    public bool IsNew => RoleId is null;

    public bool IsSuperAdminRole { get; private set; }

    public IReadOnlyList<PermissionGroupView> Groups { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string? id, CancellationToken cancellationToken)
    {
        if (id is not null)
        {
            RoleDetails? role = await GetService<IRoleManagementService>().FindRoleAsync(id, cancellationToken);
            if (role is null)
                return NotFound();

            SetRole(role);
            Input.Name = role.Name;
            Input.Permissions = [.. role.Permissions];
        }

        BuildGroups();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? id, CancellationToken cancellationToken)
    {
        IRoleManagementService roles = GetService<IRoleManagementService>();
        if (id is not null)
        {
            RoleDetails? role = await roles.FindRoleAsync(id, cancellationToken);
            if (role is null)
                return NotFound();

            SetRole(role);
        }

        if (!ModelState.IsValid)
        {
            BuildGroups();
            return Page();
        }

        ManagementResult result = id is null
            ? await roles.CreateRoleAsync(Input.Name, Input.Permissions, cancellationToken)
            : await roles.UpdateRoleAsync(id, Input.Name, Input.Permissions, cancellationToken);

        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
                ModelState.AddModelError(string.Empty, error);

            BuildGroups();
            return Page();
        }

        SetStatus(id is null ? $"The role '{Input.Name.Trim()}' was created." : $"The role '{Input.Name.Trim()}' was saved.");
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id, CancellationToken cancellationToken)
    {
        IRoleManagementService roles = GetService<IRoleManagementService>();
        RoleDetails? role = await roles.FindRoleAsync(id, cancellationToken);
        if (role is null)
            return NotFound();

        ManagementResult result = await roles.DeleteRoleAsync(id, cancellationToken);
        if (result.Succeeded)
            SetStatus($"The role '{role.Name}' was deleted.");
        else
            SetError(result.Errors);

        return RedirectToPage("Index");
    }

    private void SetRole(RoleDetails role)
    {
        RoleId = role.Id;
        RoleName = role.Name;
        IsSuperAdminRole = GetService<IOptions<DynamicAuthorizationOptions>>().Value.SuperAdminRoles.Contains(role.Name);
    }

    private void BuildGroups()
    {
        HashSet<string> granted = new(Input.Permissions, StringComparer.OrdinalIgnoreCase);
        PermissionCatalog catalog = GetService<IPermissionCatalogProvider>().GetCatalog();

        List<PermissionGroupView> groups = [.. catalog.Permissions
            .GroupBy(p => p.Group, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PermissionGroupView(
                g.Key,
                null,
                [.. g.Select(p => new PermissionItemView(p.Permission, p.DisplayName, p.Source, granted.Contains(p.Permission)))]))];

        // Keep grants this application does not know about (renamed endpoints, or another application sharing the
        // database) visible, so that saving the role does not silently revoke them.
        List<string> unknown = [.. granted.Where(p => catalog.FindPermission(p) is null).Order(StringComparer.OrdinalIgnoreCase)];
        if (unknown.Count > 0)
        {
            groups.Add(new PermissionGroupView(
                UnknownPermissionsGroup,
                "These permissions are granted but nothing in this application declares them. They may belong to a renamed or removed endpoint, or to another application that shares this database. Clear them to revoke them.",
                [.. unknown.Select(p => new PermissionItemView(p, p, null, true))]));
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

internal sealed record PermissionGroupView(string Name, string? Description, IReadOnlyList<PermissionItemView> Items)
{
    public bool IsOpen { get; init; }
}

internal sealed record PermissionItemView(string Permission, string DisplayName, PermissionSource? Source, bool Granted);
