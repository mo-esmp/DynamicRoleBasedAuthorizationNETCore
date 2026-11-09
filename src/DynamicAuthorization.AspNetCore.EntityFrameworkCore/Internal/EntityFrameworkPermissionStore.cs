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

    public async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetPermissionsAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleIds);

        // Every identifier gets an entry, keyed as it was given; identifiers that are not keys have no permissions.
        Dictionary<string, IReadOnlySet<string>> permissions = new(StringComparer.Ordinal);
        List<KeyValuePair<string, TKey>> keys = [];
        foreach (string roleId in roleIds)
        {
            if (permissions.TryAdd(roleId, s_empty) && IdentityKey<TKey>.TryParse(roleId, out TKey? key))
            {
                keys.Add(new KeyValuePair<string, TKey>(roleId, key));
            }
        }

        if (keys.Count == 0)
        {
            return permissions;
        }

        List<TKey> roleKeys = [.. keys.Select(k => k.Value)];
        var rows = await context.RolePermissions<TKey>()
            .AsNoTracking()
            .Where(p => roleKeys.Contains(p.RoleId))
            .Select(p => new { p.RoleId, p.Permission })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        ILookup<TKey, string> byRole = rows.ToLookup(r => r.RoleId, r => r.Permission);
        List<KeyValuePair<string, TKey>> unresolved = [];
        foreach (KeyValuePair<string, TKey> requested in keys)
        {
            if (byRole.Contains(requested.Value))
            {
                permissions[requested.Key] = byRole[requested.Value].ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                unresolved.Add(requested);
            }
        }

        // A requested key without rows under its own spelling either has no permissions or is spelled differently
        // from the stored key, which only a database that compares keys its own way, case-insensitively for example,
        // can tell. Value keys compare by value everywhere. For the others, ask the database whether it matches any of
        // them and, when it does, let it associate the rows too, with the single-role query for each of them.
        if (unresolved.Count == 0 || rows.Count == 0 || typeof(TKey).IsValueType)
        {
            return permissions;
        }

        List<TKey> unresolvedKeys = [.. unresolved.Select(k => k.Value)];
        bool matchesAny = await context.RolePermissions<TKey>()
            .AsNoTracking()
            .AnyAsync(p => unresolvedKeys.Contains(p.RoleId), cancellationToken)
            .ConfigureAwait(false);
        if (matchesAny)
        {
            foreach ((string roleId, _) in unresolved)
            {
                permissions[roleId] = await GetPermissionsAsync(roleId, cancellationToken).ConfigureAwait(false);
            }
        }

        return permissions;
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
        List<RolePermission<TKey>> removed = [.. existing.Where(p => !desired.Contains(p.Permission))];
        List<string> added =
            [.. desired.Where(p => !existingPermissions.Contains(p)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)];
        if (removed.Count == 0 && added.Count == 0)
        {
            return PermissionChangeSet.Empty;
        }

        set.RemoveRange(removed);
        set.AddRange(added.Select(p => new RolePermission<TKey> { RoleId = key, Permission = p }));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Cache entries are keyed by the formatted key; the caller's identifier may be written differently.
        await cache.InvalidateRoleAsync(IdentityKey<TKey>.Format(key), cancellationToken).ConfigureAwait(false);

        return new PermissionChangeSet(added,
            [.. removed.Select(p => p.Permission).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)]);
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
