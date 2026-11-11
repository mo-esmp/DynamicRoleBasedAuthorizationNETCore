using System.ComponentModel;
using DynamicAuthorization.AspNetCore.Management;
using Microsoft.AspNetCore.Mvc;

namespace DynamicAuthorization.AspNetCore.Dashboard.Pages.Roles;

[DisplayName("Create roles")]
[PermissionResource(DashboardConstants.RolesResource, DisplayName = "Dashboard / Roles")]
internal sealed class CreateModel : RoleFormModel
{
    public void OnGet() => BuildPermissions();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Redisplay();
        }

        ManagementResult result = await GetService<IRoleManagementService>().CreateRoleAsync(Input.Name, Input.Permissions, cancellationToken);
        if (!result.Succeeded)
        {
            return Redisplay(result);
        }

        SetStatus($"The role '{Input.Name.Trim()}' was created.");
        return RedirectToPage("Index");
    }
}
