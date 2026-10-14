using System.ComponentModel;
using DynamicAuthorization.AspNetCore.Management;
using Microsoft.AspNetCore.Mvc;

namespace DynamicAuthorization.AspNetCore.Dashboard.Pages;

[DisplayName("Access check")]
[PermissionResource(DashboardConstants.AccessResource, DisplayName = "Dashboard / Access check")]
internal sealed class AccessModel : DashboardPageModel
{
    [BindProperty(SupportsGet = true, Name = "user")]
    public string? UserInput { get; set; }

    [BindProperty(SupportsGet = true, Name = "userId")]
    public string? UserId { get; set; }

    [BindProperty(SupportsGet = true, Name = "permission")]
    public string? Permission { get; set; }

    public PermissionCatalog Catalog { get; private set; } = new([], []);

    public UserSummary? Subject { get; private set; }

    public bool UserNotFound { get; private set; }

    public PermissionDescriptor? Descriptor { get; private set; }

    public PermissionEvaluation? Evaluation { get; private set; }

    public IReadOnlyList<EndpointAuthorizationInfo> Endpoints { get; private set; } = [];

    public string Explanation => Evaluation?.Decision switch
    {
        PermissionDecision.SuperAdminUser => "The user is listed in the super admin users, so every permission is granted.",
        PermissionDecision.SuperAdminRole => $"The user is a member of the super admin role '{Evaluation.GrantingRoles[0].Name}', so every permission is granted.",
        PermissionDecision.GrantedByRole when Evaluation.ImpliedBy is { } impliedBy =>
            $"Granted through {impliedBy}: any operation on a resource also lets users read it. Granted by:",
        PermissionDecision.GrantedByRole => Evaluation.GrantingRoles.Count == 1 ? "Granted by this role:" : "Granted by these roles:",
        _ when Subject?.Roles.Count == 0 => "The user has no roles.",
        _ => $"None of the user's roles ({string.Join(", ", Subject?.Roles.Select(r => r.Name) ?? [])}) grants this permission.",
    };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Catalog = GetService<IPermissionCatalogProvider>().GetCatalog();
        IUserManagementService users = GetService<IUserManagementService>();

        if (!string.IsNullOrEmpty(UserId))
        {
            Subject = await users.FindUserAsync(UserId, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(UserInput))
        {
            Subject = await users.FindUserByNameAsync(UserInput, cancellationToken);
        }

        UserNotFound = Subject is null && (!string.IsNullOrEmpty(UserId) || !string.IsNullOrWhiteSpace(UserInput));
        if (Subject is not null)
        {
            UserInput = Subject.UserName;
        }

        if (Subject is null || string.IsNullOrWhiteSpace(Permission))
        {
            return;
        }

        Descriptor = Catalog.FindPermission(Permission);
        Endpoints = [.. Catalog.GetEndpoints(Permission)];
        Evaluation = await GetService<IPermissionChecker>().EvaluateUserAsync(Subject.Id, Subject.UserName, Permission, cancellationToken);
    }
}
