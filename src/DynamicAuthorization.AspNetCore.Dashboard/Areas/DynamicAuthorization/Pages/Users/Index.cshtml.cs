using System.ComponentModel;
using DynamicAuthorization.AspNetCore.Management;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore.Dashboard.Pages.Users;

[DisplayName("Users")]
[PermissionResource(DashboardConstants.UsersResource, DisplayName = "Dashboard / Users")]
internal sealed class IndexModel : DashboardPageModel
{
    private const int PageSize = 25;

    private DynamicAuthorizationOptions _settings = new();

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    // "page" is reserved for Razor Pages routing, hence "p".
    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    public PagedResult<UserSummary> Users { get; private set; } = new([], 0, 1, PageSize);

    public bool IsSuperAdmin(UserSummary user) =>
        _settings.SuperAdminUsers.Contains(user.UserName) || user.Roles.Any(r => _settings.SuperAdminRoles.Contains(r.Name));

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        _settings = GetService<IOptions<DynamicAuthorizationOptions>>().Value;
        Users = await GetService<IUserManagementService>().GetUsersAsync(new UserQuery(Query, PageNumber, PageSize), cancellationToken);
    }
}
