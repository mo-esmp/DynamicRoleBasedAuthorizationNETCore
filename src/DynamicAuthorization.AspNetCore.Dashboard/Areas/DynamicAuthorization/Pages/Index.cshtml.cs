using System.ComponentModel;
using DynamicAuthorization.AspNetCore.Management;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore.Dashboard.Pages;

[DisplayName("Overview")]
[PermissionResource(DashboardConstants.OverviewResource, DisplayName = "Dashboard / Overview")]
internal sealed class IndexModel : DashboardPageModel
{
    private const int MaxOrphanedGrants = 8;

    public string ApplicationName { get; private set; } = string.Empty;

    public DynamicAuthorizationOptions Settings { get; private set; } = new();

    public IReadOnlyList<RoleSummary> Roles { get; private set; } = [];

    public int UserCount { get; private set; }

    public int PermissionCount { get; private set; }

    public int GrantCount { get; private set; }

    public int EndpointCount { get; private set; }

    public int PermissionEndpointCount { get; private set; }

    public int UnprotectedEndpointCount { get; private set; }

    public IReadOnlyList<OrphanedGrant> OrphanedGrants { get; private set; } = [];

    public int OrphanedGrantCount { get; private set; }

    public IReadOnlyList<RoleSummary> RolesWithoutUsers { get; private set; } = [];

    public IReadOnlyList<RoleSummary> RolesWithoutPermissions { get; private set; } = [];

    public bool NeedsAttention => UnprotectedEndpointCount > 0 || OrphanedGrantCount > 0 || RolesWithoutUsers.Count > 0 || RolesWithoutPermissions.Count > 0;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        IRoleManagementService roleService = GetService<IRoleManagementService>();
        PermissionCatalog catalog = GetService<IPermissionCatalogProvider>().GetCatalog();
        ApplicationName = GetService<IHostEnvironment>().ApplicationName;
        Settings = GetService<IOptions<DynamicAuthorizationOptions>>().Value;

        Roles = await roleService.GetRolesAsync(cancellationToken);
        PagedResult<UserSummary> users = await GetService<IUserManagementService>().GetUsersAsync(new UserQuery(PageSize: 1), cancellationToken);
        IReadOnlyList<RoleGrant> grants = await roleService.GetGrantsAsync(cancellationToken);

        UserCount = users.TotalCount;
        PermissionCount = catalog.Permissions.Count;
        GrantCount = grants.Count;
        EndpointCount = catalog.Endpoints.Count;
        PermissionEndpointCount = catalog.Endpoints.Count(e => e.Protection == EndpointProtection.DynamicPermission);
        UnprotectedEndpointCount = catalog.Endpoints.Count(e => e.Protection == EndpointProtection.None);

        List<OrphanedGrant> orphaned = [.. grants
            .Where(g => catalog.FindPermission(g.Permission) is null)
            .GroupBy(g => g.Permission, StringComparer.OrdinalIgnoreCase)
            .Select(g => new OrphanedGrant(g.Key, [.. g.Select(r => new RoleReference(r.RoleId, r.RoleName))]))];
        OrphanedGrantCount = orphaned.Count;
        OrphanedGrants = [.. orphaned.Take(MaxOrphanedGrants)];

        RolesWithoutUsers = [.. Roles.Where(r => r.UserCount == 0)];
        RolesWithoutPermissions = [.. Roles.Where(r => r.PermissionCount == 0 && !Settings.SuperAdminRoles.Contains(r.Name))];
    }
}

internal sealed record OrphanedGrant(string Permission, IReadOnlyList<RoleReference> Roles);
