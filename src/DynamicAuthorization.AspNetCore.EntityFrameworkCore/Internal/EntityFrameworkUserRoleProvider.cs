using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore.EntityFrameworkCore;

internal sealed class EntityFrameworkUserRoleProvider<TContext, TRole, TKey, TUserRole>(
    TContext context,
    IOptions<IdentityOptions> identityOptions) : IUserRoleProvider
    where TContext : DbContext
    where TRole : IdentityRole<TKey>
    where TKey : IEquatable<TKey>
    where TUserRole : IdentityUserRole<TKey>
{
    public string? GetUserId(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal.FindFirstValue(identityOptions.Value.ClaimsIdentity.UserIdClaimType);
    }

    public async Task<IReadOnlyList<RoleReference>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!IdentityKey<TKey>.TryParse(userId, out TKey? key))
            return [];

        var roles = await (
                from userRole in context.Set<TUserRole>()
                join role in context.Set<TRole>() on userRole.RoleId equals role.Id
                where userRole.UserId.Equals(key)
                select new { role.Id, role.Name })
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. roles.Select(r => new RoleReference(IdentityKey<TKey>.Format(r.Id), r.Name ?? string.Empty))];
    }
}
