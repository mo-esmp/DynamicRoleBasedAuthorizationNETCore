using System.ComponentModel;
using DynamicAuthorization.AspNetCore.Management;
using Microsoft.AspNetCore.Mvc;

namespace DynamicAuthorization.AspNetCore.Dashboard.Pages.Roles;

// The page requires authz.roles.update; its Delete handler requires authz.roles.delete.
[DisplayName("Edit roles")]
[PermissionResource(DashboardConstants.RolesResource, DisplayName = "Dashboard / Roles")]
internal sealed class EditModel : RoleFormModel
{
    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        RoleDetails? role = await GetService<IRoleManagementService>().FindRoleAsync(id, cancellationToken);
        if (role is null)
        {
            return NotFound();
        }

        SetRole(role);
        Input.Name = role.Name;
        Input.Permissions = [.. role.Permissions];
        BuildPermissions();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string id, CancellationToken cancellationToken)
    {
        IRoleManagementService roles = GetService<IRoleManagementService>();
        RoleDetails? role = await roles.FindRoleAsync(id, cancellationToken);
        if (role is null)
        {
            return NotFound();
        }

        SetRole(role);
        if (!ModelState.IsValid)
        {
            return Redisplay();
        }

        ManagementResult result = await roles.UpdateRoleAsync(id, Input.Name, Input.Permissions, cancellationToken);
        if (!result.Succeeded)
        {
            return Redisplay(result);
        }

        SetStatus($"The role '{Input.Name.Trim()}' was saved.");
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id, CancellationToken cancellationToken)
    {
        IRoleManagementService roles = GetService<IRoleManagementService>();
        RoleDetails? role = await roles.FindRoleAsync(id, cancellationToken);
        if (role is null)
        {
            return NotFound();
        }

        ManagementResult result = await roles.DeleteRoleAsync(id, cancellationToken);
        if (result.Succeeded)
        {
            SetStatus($"The role '{role.Name}' was deleted.");
        }
        else
        {
            SetError(result.Errors);
        }

        return RedirectToPage("Index");
    }
}
