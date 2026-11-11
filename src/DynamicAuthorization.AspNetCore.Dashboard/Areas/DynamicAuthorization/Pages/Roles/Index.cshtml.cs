using System.ComponentModel;
using DynamicAuthorization.AspNetCore.Management;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore.Dashboard.Pages.Roles;

[DisplayName("Roles")]
[PermissionResource(DashboardConstants.RolesResource, DisplayName = "Dashboard / Roles")]
internal sealed class IndexModel : DashboardPageModel
{
    public IReadOnlyList<RoleSummary> Roles { get; private set; } = new List<RoleSummary>();

    public ISet<string> SuperAdminRoles { get; private set; } = new HashSet<string>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        SuperAdminRoles = GetService<IOptions<DynamicAuthorizationOptions>>().Value.SuperAdminRoles;
        Roles = await GetService<IRoleManagementService>().GetRolesAsync(cancellationToken);
    }
}
