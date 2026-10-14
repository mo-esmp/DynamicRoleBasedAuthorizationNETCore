using System.ComponentModel;
using DynamicAuthorization.AspNetCore.Management;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore.Dashboard.Pages.Users;

[DisplayName("Edit user roles")]
[PermissionGroup(DashboardConstants.PermissionGroup)]
internal sealed class EditModel : DashboardPageModel
{
    [BindProperty]
    public List<string> RoleIds { get; set; } = new List<string>();

    public UserSummary Subject { get; private set; } = null!;

    public IReadOnlyList<RoleSummary> Roles { get; private set; } = new List<RoleSummary>();

    public IReadOnlyList<EffectivePermissionView> EffectivePermissions { get; private set; } = new List<EffectivePermissionView>();

    public bool IsSuperAdminUser { get; private set; }

    public RoleReference? SuperAdminRole { get; private set; }

    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        RoleIds = Subject.Roles.Select(r => r.Id).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string id, CancellationToken cancellationToken)
    {
        UserSummary? user = await GetService<IUserManagementService>().FindUserAsync(id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        ManagementResult result = await GetService<IUserManagementService>().SetUserRolesAsync(id, RoleIds, cancellationToken);
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            await LoadAsync(id, cancellationToken);
            return Page();
        }

        SetStatus($"The roles of '{user.UserName}' were saved.");
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(string id, CancellationToken cancellationToken)
    {
        UserSummary? user = await GetService<IUserManagementService>().FindUserAsync(id, cancellationToken);
        if (user is null)
        {
            return false;
        }

        IRoleManagementService roleService = GetService<IRoleManagementService>();
        DynamicAuthorizationOptions settings = GetService<IOptions<DynamicAuthorizationOptions>>().Value;
        PermissionCatalog catalog = GetService<IPermissionCatalogProvider>().GetCatalog();

        Subject = user;
        Roles = await roleService.GetRolesAsync(cancellationToken);
        IsSuperAdminUser = settings.SuperAdminUsers.Contains(user.UserName);
        SuperAdminRole = user.Roles.FirstOrDefault(r => settings.SuperAdminRoles.Contains(r.Name));

        HashSet<string> roleIds = user.Roles.Select(r => r.Id).ToHashSet();
        IReadOnlyList<RoleGrant> grants = await roleService.GetGrantsAsync(cancellationToken);
        EffectivePermissions = grants
            .Where(g => roleIds.Contains(g.RoleId))
            .GroupBy(g => g.Permission, StringComparer.OrdinalIgnoreCase)
            .Select(g => new EffectivePermissionView(g.Key, catalog.FindPermission(g.Key), g.Select(r => new RoleReference(r.RoleId, r.RoleName)).ToList()))
            .OrderBy(p => p.Descriptor?.Group ?? "~", StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Descriptor?.DisplayName ?? p.Permission, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return true;
    }
}

internal sealed record EffectivePermissionView(string Permission, PermissionDescriptor? Descriptor, IReadOnlyList<RoleReference> Roles);
