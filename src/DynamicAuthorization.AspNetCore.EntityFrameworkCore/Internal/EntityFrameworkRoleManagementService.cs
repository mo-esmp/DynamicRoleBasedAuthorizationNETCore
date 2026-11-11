using DynamicAuthorization.AspNetCore.Management;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore.EntityFrameworkCore;

internal sealed class EntityFrameworkRoleManagementService<TContext, TRole, TKey, TUserRole>(
    TContext context,
    IServiceProvider services,
    IPermissionStore permissionStore,
    IDynamicAuthorizationCache cache,
    IOptions<DynamicAuthorizationOptions> options) : IRoleManagementService
    where TContext : DbContext
    where TRole : IdentityRole<TKey>
    where TKey : IEquatable<TKey>
    where TUserRole : IdentityUserRole<TKey>
{
    private RoleManager<TRole> RoleManager =>
        services.GetService<RoleManager<TRole>>()
        ?? throw new InvalidOperationException(
            $"RoleManager<{typeof(TRole).Name}> is not registered. Enable roles with AddRoles<{typeof(TRole).Name}>() " +
            "or register Identity with AddIdentity<TUser, TRole>().");

    public async Task<IReadOnlyList<RoleSummary>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        IQueryable<TUserRole> userRoles = context.Set<TUserRole>();
        IQueryable<RolePermission<TKey>> permissions = context.RolePermissions<TKey>();

        var roles = await context.Set<TRole>()
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                UserCount = userRoles.Count(ur => ur.RoleId.Equals(r.Id)),
                PermissionCount = permissions.Count(p => p.RoleId.Equals(r.Id)),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. roles.Select(r => new RoleSummary(IdentityKey<TKey>.Format(r.Id), r.Name ?? string.Empty, r.UserCount, r.PermissionCount))];
    }

    public async Task<RoleDetails?> FindRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        if (!IdentityKey<TKey>.TryParse(roleId, out TKey? key))
        {
            return null;
        }

        var role = await context.Set<TRole>()
            .AsNoTracking()
            .Where(r => r.Id.Equals(key))
            .Select(r => new { r.Id, r.Name })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (role is null)
        {
            return null;
        }

        string id = IdentityKey<TKey>.Format(role.Id);
        IReadOnlySet<string> granted = await permissionStore.GetPermissionsAsync(id, cancellationToken).ConfigureAwait(false);

        return new RoleDetails(id, role.Name ?? string.Empty, granted);
    }

    public Task<IReadOnlyList<RoleGrant>> GetGrantsAsync(CancellationToken cancellationToken = default) =>
        ReadGrantsAsync(context.RolePermissions<TKey>(), cancellationToken);

    public async Task<IReadOnlyList<RoleGrant>> GetGrantsAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleIds);

        // Identifiers that are not valid keys belong to no role, as everywhere in this service.
        List<TKey> keys = [];
        foreach (string roleId in roleIds)
        {
            if (IdentityKey<TKey>.TryParse(roleId, out TKey? key))
            {
                keys.Add(key);
            }
        }

        if (keys.Count == 0)
        {
            return [];
        }

        return await ReadGrantsAsync(context.RolePermissions<TKey>().Where(p => keys.Contains(p.RoleId)), cancellationToken).ConfigureAwait(false);
    }

    public async Task<ManagementResult<string>> CreateRoleAsync(string name, IEnumerable<string> permissions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        if (string.IsNullOrWhiteSpace(name))
        {
            return ManagementResult.Fail<string>("The role name is required.");
        }

        RoleManager<TRole> roleManager = RoleManager;
        List<string> granted = [.. permissions];
        string roleName = name.Trim();
        string normalizedName = roleManager.NormalizeKey(roleName);

        // A retrying execution strategy runs the creation again when the commit fails, also when the database completed
        // the commit and only its acknowledgement was lost. The stamp identifies the role of this call, so that such a
        // retry returns the role instead of reporting that its name is taken.
        string stamp = Guid.NewGuid().ToString();
        bool retrying = false;

        ManagementResult<string> result = await context.ExecuteInTransactionAsync(
            async ct =>
            {
                if (retrying)
                {
                    var committed = await context.Set<TRole>()
                        .AsNoTracking()
                        .Where(r => r.NormalizedName == normalizedName && r.ConcurrencyStamp == stamp)
                        .Select(r => new { r.Id })
                        .FirstOrDefaultAsync(ct)
                        .ConfigureAwait(false);
                    if (committed is not null)
                    {
                        return ManagementResult.Ok(IdentityKey<TKey>.Format(committed.Id));
                    }
                }

                retrying = true;

                TRole role = Activator.CreateInstance<TRole>();
                role.Name = roleName;
                role.ConcurrencyStamp = stamp;

                IdentityResult created = await roleManager.CreateAsync(role).ConfigureAwait(false);
                if (!created.Succeeded)
                {
                    return ManagementResult.Fail<string>(created.Errors.Select(e => e.Description));
                }

                string roleId = IdentityKey<TKey>.Format(role.Id);
                await permissionStore.SetPermissionsAsync(roleId, granted, ct).ConfigureAwait(false);

                return ManagementResult.Ok(roleId);
            },
            cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            await InvalidateAfterCommitAsync(result.Value!).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<ManagementResult> UpdateRoleAsync(string roleId, string name, IEnumerable<string> permissions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        if (string.IsNullOrWhiteSpace(name))
        {
            return ManagementResult.Fail("The role name is required.");
        }

        RoleManager<TRole> roleManager = RoleManager;
        TRole? role = await FindTrackedRoleAsync(roleManager, roleId).ConfigureAwait(false);
        if (role is null)
        {
            return RoleNotFound(roleId);
        }

        string newName = name.Trim();
        bool renamed = !string.Equals(role.Name, newName, StringComparison.Ordinal);
        if (renamed && IsSuperAdminRole(role.Name))
        {
            return ManagementResult.Fail(
                $"The role '{role.Name}' is a super admin role and cannot be renamed. Remove it from {nameof(DynamicAuthorizationOptions.SuperAdminRoles)} first.");
        }

        List<string> granted = [.. permissions];
        ManagementResult result = await context.ExecuteInTransactionAsync(
            async ct =>
            {
                if (renamed)
                {
                    role.Name = newName;
                    IdentityResult updated = await roleManager.UpdateAsync(role).ConfigureAwait(false);
                    if (!updated.Succeeded)
                    {
                        return ManagementResult.Fail(updated.Errors.Select(e => e.Description));
                    }
                }

                await permissionStore.SetPermissionsAsync(roleId, granted, ct).ConfigureAwait(false);

                return ManagementResult.Ok();
            },
            cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            // Role names are cached with user memberships and used to recognize super admin roles.
            if (renamed)
            {
                await cache.InvalidateAllAsync(CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await InvalidateAfterCommitAsync(roleId).ConfigureAwait(false);
            }
        }

        return result;
    }

    public async Task<ManagementResult> DeleteRoleAsync(string roleId, CancellationToken cancellationToken = default)
    {
        RoleManager<TRole> roleManager = RoleManager;
        TRole? role = await FindTrackedRoleAsync(roleManager, roleId).ConfigureAwait(false);
        if (role is null)
        {
            return RoleNotFound(roleId);
        }

        if (IsSuperAdminRole(role.Name))
        {
            return ManagementResult.Fail(
                $"The role '{role.Name}' is a super admin role and cannot be deleted. Remove it from {nameof(DynamicAuthorizationOptions.SuperAdminRoles)} first.");
        }

        ManagementResult result = await context.ExecuteInTransactionAsync(
            async ct =>
            {
                // Remove grants explicitly so that providers without cascading deletes behave the same.
                await permissionStore.SetPermissionsAsync(roleId, [], ct).ConfigureAwait(false);

                IdentityResult deleted = await roleManager.DeleteAsync(role).ConfigureAwait(false);

                return deleted.Succeeded ? ManagementResult.Ok() : ManagementResult.Fail(deleted.Errors.Select(e => e.Description));
            },
            cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            await cache.InvalidateAllAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    ///   The store invalidates a role when it saves its permissions, which happens before the transaction commits. A
    ///   load that starts in between reads the permissions from before the change, so invalidate again after the
    ///   commit. When the caller owns the transaction, the commit happens later and the caller has to invalidate.
    ///   Cache entries are keyed by the formatted key; the caller's identifier may be written differently. The change is
    ///   committed, so the invalidation is not left to the caller's cancellation.
    /// </summary>
    private ValueTask InvalidateAfterCommitAsync(string roleId) =>
        cache.InvalidateRoleAsync(IdentityKey<TKey>.Normalize(roleId), CancellationToken.None);

    private async Task<IReadOnlyList<RoleGrant>> ReadGrantsAsync(IQueryable<RolePermission<TKey>> permissions, CancellationToken cancellationToken)
    {
        var grants = await (
                from permission in permissions
                join role in context.Set<TRole>() on permission.RoleId equals role.Id
                orderby role.Name, permission.Permission
                select new { role.Id, role.Name, permission.Permission })
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. grants.Select(g => new RoleGrant(IdentityKey<TKey>.Format(g.Id), g.Name ?? string.Empty, g.Permission))];
    }

    private static async Task<TRole?> FindTrackedRoleAsync(RoleManager<TRole> roleManager, string roleId) =>
        IdentityKey<TKey>.TryParse(roleId, out _) ? await roleManager.FindByIdAsync(roleId).ConfigureAwait(false) : null;

    private static ManagementResult RoleNotFound(string roleId) => ManagementResult.Fail($"The role '{roleId}' was not found.");

    private bool IsSuperAdminRole(string? roleName) => roleName is not null && options.Value.SuperAdminRoles.Contains(roleName);
}
