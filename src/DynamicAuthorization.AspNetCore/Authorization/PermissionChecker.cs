using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore;

internal sealed class PermissionChecker(
    IUserRoleProvider userRoleProvider,
    IPermissionStore permissionStore,
    DynamicAuthorizationCache cache,
    IOptionsMonitor<DynamicAuthorizationOptions> options) : IPermissionChecker
{
    public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default)
    {
        PermissionEvaluation evaluation = await EvaluateCoreAsync(user, permission, stopAtFirstGrant: true, cancellationToken).ConfigureAwait(false);

        return evaluation.IsGranted;
    }

    public Task<PermissionEvaluation> EvaluateAsync(ClaimsPrincipal user, string permission, CancellationToken cancellationToken = default) =>
        EvaluateCoreAsync(user, permission, stopAtFirstGrant: false, cancellationToken);

    public Task<PermissionEvaluation> EvaluateUserAsync(string userId, string? userName, string permission, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentException.ThrowIfNullOrEmpty(permission);

        return EvaluateUserCoreAsync(userId, userName, permission, stopAtFirstGrant: false, cancellationToken);
    }

    private Task<PermissionEvaluation> EvaluateCoreAsync(ClaimsPrincipal user, string permission, bool stopAtFirstGrant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrEmpty(permission);

        if (user.Identity?.IsAuthenticated != true)
            return Task.FromResult(new PermissionEvaluation(permission, PermissionDecision.NotAuthenticated, []));

        return EvaluateUserCoreAsync(userRoleProvider.GetUserId(user), user.Identity.Name, permission, stopAtFirstGrant, cancellationToken);
    }

    private async Task<PermissionEvaluation> EvaluateUserCoreAsync(
        string? userId,
        string? userName,
        string permission,
        bool stopAtFirstGrant,
        CancellationToken cancellationToken)
    {
        DynamicAuthorizationOptions settings = options.CurrentValue;
        if (userName is not null && settings.SuperAdminUsers.Contains(userName))
            return new PermissionEvaluation(permission, PermissionDecision.SuperAdminUser, []);

        if (string.IsNullOrEmpty(userId))
            return new PermissionEvaluation(permission, PermissionDecision.NotGranted, []);

        IReadOnlyList<RoleReference> roles = await cache
            .GetUserRolesAsync(userId, ct => userRoleProvider.GetUserRolesAsync(userId, ct), cancellationToken)
            .ConfigureAwait(false);

        foreach (RoleReference role in roles)
        {
            if (settings.SuperAdminRoles.Contains(role.Name))
                return new PermissionEvaluation(permission, PermissionDecision.SuperAdminRole, [role]);
        }

        List<RoleReference> grantingRoles = [];
        foreach (RoleReference role in roles)
        {
            IReadOnlySet<string> permissions = await cache
                .GetRolePermissionsAsync(role.Id, ct => permissionStore.GetPermissionsAsync(role.Id, ct), cancellationToken)
                .ConfigureAwait(false);

            if (!permissions.Contains(permission))
                continue;

            grantingRoles.Add(role);
            if (stopAtFirstGrant)
                break;
        }

        return grantingRoles.Count == 0
            ? new PermissionEvaluation(permission, PermissionDecision.NotGranted, [])
            : new PermissionEvaluation(permission, PermissionDecision.GrantedByRole, grantingRoles);
    }
}
