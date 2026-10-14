using Microsoft.EntityFrameworkCore;

namespace DynamicAuthorization.AspNetCore.EntityFrameworkCore;

internal sealed class EntityFrameworkPermissionStore<TContext, TKey>(TContext context, IDynamicAuthorizationCache cache) : IPermissionStore
    where TContext : DbContext
    where TKey : IEquatable<TKey>
{
    private static readonly IReadOnlySet<string> s_empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(string roleId, CancellationToken cancellationToken = default)
    {
        if (!IdentityKey<TKey>.TryParse(roleId, out TKey? key))
        {
            return s_empty;
        }

        List<string> permissions = await context.RolePermissions<TKey>()
            .AsNoTracking()
            .Where(p => p.RoleId.Equals(key))
            .Select(p => p.Permission)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<PermissionChangeSet> SetPermissionsAsync(string roleId, IEnumerable<string> permissions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        if (!IdentityKey<TKey>.TryParse(roleId, out TKey? key))
        {
            throw new ArgumentException($"'{roleId}' is not a valid role identifier.", nameof(roleId));
        }

        HashSet<string> desired = Normalize(permissions);
        DbSet<RolePermission<TKey>> set = context.RolePermissions<TKey>();
        List<RolePermission<TKey>> existing = await set
            .Where(p => p.RoleId.Equals(key))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        HashSet<string> existingPermissions = existing.Select(p => p.Permission).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<RolePermission<TKey>> removed = existing.Where(p => !desired.Contains(p.Permission)).ToList();
        List<string> added = desired.Where(p => !existingPermissions.Contains(p)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        if (removed.Count == 0 && added.Count == 0)
        {
            return PermissionChangeSet.Empty;
        }

        set.RemoveRange(removed);
        set.AddRange(added.Select(p => new RolePermission<TKey> { RoleId = key, Permission = p }));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        cache.InvalidateRole(roleId);

        return new PermissionChangeSet(added, removed.Select(p => p.Permission).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static HashSet<string> Normalize(IEnumerable<string> permissions)
    {
        return permissions
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p =>
            {
                string trimmed = p.Trim();
                if (trimmed.Length > DynamicAuthorizationModelOptions.MaxPermissionLength)
                {
                    throw new ArgumentException($"The permission '{trimmed}' is longer than {DynamicAuthorizationModelOptions.MaxPermissionLength} characters.", nameof(permissions));
                }

                return trimmed;
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
