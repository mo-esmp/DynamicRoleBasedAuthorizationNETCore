using DynamicAuthorization.AspNetCore.Management;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore.EntityFrameworkCore;

internal sealed class EntityFrameworkUserManagementService<TContext, TUser, TRole, TKey, TUserRole>(
    TContext context,
    IServiceProvider services,
    IDynamicAuthorizationCache cache,
    IOptions<DynamicAuthorizationOptions> options) : IUserManagementService
    where TContext : DbContext
    where TUser : IdentityUser<TKey>
    where TRole : IdentityRole<TKey>
    where TKey : notnull, IEquatable<TKey>
    where TUserRole : IdentityUserRole<TKey>
{
    private const int MaxPageSize = 200;

    private UserManager<TUser> UserManager =>
        services.GetService<UserManager<TUser>>()
        ?? throw new InvalidOperationException($"UserManager<{typeof(TUser).Name}> is not registered. Register ASP.NET Core Identity first.");

    public async Task<PagedResult<UserSummary>> GetUsersAsync(UserQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        int page = Math.Max(1, query.Page);
        int pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        IQueryable<TUser> users = context.Set<TUser>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string search = Normalize(query.Search.Trim());
            users = users.Where(u =>
                (u.NormalizedUserName != null && u.NormalizedUserName.Contains(search))
                || (u.NormalizedEmail != null && u.NormalizedEmail.Contains(search)));
        }

        int totalCount = await users.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await users
            .OrderBy(u => u.UserName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new { u.Id, u.UserName, u.Email })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<TKey, List<RoleReference>> roles = await GetRolesAsync([.. items.Select(u => u.Id)], cancellationToken).ConfigureAwait(false);
        List<UserSummary> summaries = [.. items.Select(u => ToSummary(u.Id, u.UserName, u.Email, roles))];

        return new PagedResult<UserSummary>(summaries, totalCount, page, pageSize);
    }

    public async Task<UserSummary?> FindUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!IdentityKey<TKey>.TryParse(userId, out TKey? key))
            return null;

        return await FindAsync(context.Set<TUser>().Where(u => u.Id.Equals(key)), cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserSummary?> FindUserByNameAsync(string userNameOrEmail, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userNameOrEmail))
            return null;

        string normalized = Normalize(userNameOrEmail.Trim());

        return await FindAsync(
            context.Set<TUser>().Where(u => u.NormalizedUserName == normalized || u.NormalizedEmail == normalized),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ManagementResult> SetUserRolesAsync(string userId, IEnumerable<string> roleIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleIds);

        UserManager<TUser> userManager = UserManager;
        TUser? user = IdentityKey<TKey>.TryParse(userId, out TKey? userKey)
            ? await userManager.FindByIdAsync(userId).ConfigureAwait(false)
            : null;
        if (user is null || userKey is null)
            return ManagementResult.Fail($"The user '{userId}' was not found.");

        List<TKey> roleKeys = [];
        foreach (string roleId in roleIds.Distinct(StringComparer.Ordinal))
        {
            if (!IdentityKey<TKey>.TryParse(roleId, out TKey? roleKey))
                return ManagementResult.Fail($"The role '{roleId}' was not found.");

            roleKeys.Add(roleKey);
        }

        var desiredRoles = await context.Set<TRole>()
            .AsNoTracking()
            .Where(r => roleKeys.Contains(r.Id))
            .Select(r => new { r.Id, r.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (desiredRoles.Count != roleKeys.Count)
            return ManagementResult.Fail("One or more roles were not found.");

        var currentRoles = await (
                from userRole in context.Set<TUserRole>()
                join role in context.Set<TRole>() on userRole.RoleId equals role.Id
                where userRole.UserId.Equals(userKey)
                select new { role.Id, role.Name })
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<string> rolesToAdd = [.. desiredRoles.Where(d => !currentRoles.Any(c => c.Id.Equals(d.Id))).Select(r => r.Name!)];
        List<string> rolesToRemove = [.. currentRoles.Where(c => !desiredRoles.Any(d => d.Id.Equals(c.Id))).Select(r => r.Name!)];
        if (rolesToAdd.Count == 0 && rolesToRemove.Count == 0)
            return ManagementResult.Ok();

        ManagementResult? lockout = await CheckSuperAdminLockoutAsync(currentRoles.Where(c => rolesToRemove.Contains(c.Name!)).Select(c => (c.Id, c.Name!)), cancellationToken)
            .ConfigureAwait(false);
        if (lockout is not null)
            return lockout;

        ManagementResult result = await context.ExecuteInTransactionAsync(
            async _ =>
            {
                if (rolesToRemove.Count > 0)
                {
                    IdentityResult removed = await userManager.RemoveFromRolesAsync(user, rolesToRemove).ConfigureAwait(false);
                    if (!removed.Succeeded)
                        return ManagementResult.Fail(removed.Errors.Select(e => e.Description));
                }

                if (rolesToAdd.Count > 0)
                {
                    IdentityResult added = await userManager.AddToRolesAsync(user, rolesToAdd).ConfigureAwait(false);
                    if (!added.Succeeded)
                        return ManagementResult.Fail(added.Errors.Select(e => e.Description));
                }

                return ManagementResult.Ok();
            },
            cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
            cache.InvalidateUser(userId);

        return result;
    }

    /// <summary>
    ///   Prevents removing the last member of a super admin role when no super admin user is configured, which would
    ///   leave nobody able to manage access.
    /// </summary>
    private async Task<ManagementResult?> CheckSuperAdminLockoutAsync(IEnumerable<(TKey Id, string Name)> removedRoles, CancellationToken cancellationToken)
    {
        DynamicAuthorizationOptions settings = options.Value;
        if (settings.SuperAdminUsers.Count > 0)
            return null;

        foreach ((TKey roleId, string roleName) in removedRoles)
        {
            if (!settings.SuperAdminRoles.Contains(roleName))
                continue;

            int members = await context.Set<TUserRole>().CountAsync(ur => ur.RoleId.Equals(roleId), cancellationToken).ConfigureAwait(false);
            if (members <= 1)
                return ManagementResult.Fail($"The user is the last member of the super admin role '{roleName}' and cannot be removed from it.");
        }

        return null;
    }

    private async Task<UserSummary?> FindAsync(IQueryable<TUser> query, CancellationToken cancellationToken)
    {
        var user = await query
            .AsNoTracking()
            .Select(u => new { u.Id, u.UserName, u.Email })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
            return null;

        Dictionary<TKey, List<RoleReference>> roles = await GetRolesAsync([user.Id], cancellationToken).ConfigureAwait(false);

        return ToSummary(user.Id, user.UserName, user.Email, roles);
    }

    private async Task<Dictionary<TKey, List<RoleReference>>> GetRolesAsync(List<TKey> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
            return [];

        var rows = await (
                from userRole in context.Set<TUserRole>()
                join role in context.Set<TRole>() on userRole.RoleId equals role.Id
                where userIds.Contains(userRole.UserId)
                orderby role.Name
                select new { userRole.UserId, role.Id, role.Name })
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Select(r => new RoleReference(IdentityKey<TKey>.Format(r.Id), r.Name ?? string.Empty)).ToList());
    }

    private static UserSummary ToSummary(TKey id, string? userName, string? email, Dictionary<TKey, List<RoleReference>> roles) =>
        new(IdentityKey<TKey>.Format(id), userName ?? string.Empty, email, roles.GetValueOrDefault(id) ?? []);

    private string Normalize(string value) =>
        services.GetService<ILookupNormalizer>()?.NormalizeName(value) ?? value.ToUpperInvariant();
}
