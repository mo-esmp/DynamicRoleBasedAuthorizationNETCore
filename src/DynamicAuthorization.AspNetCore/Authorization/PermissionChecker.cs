using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Checks permissions against the roles of the user and the permissions of those roles. Within its scope, usually
///   one request, the roles of a user and the permissions of a role are resolved once however many checks are made,
///   until the cache is invalidated or the cache duration passes. With caching disabled, nothing is kept.
/// </summary>
internal sealed class PermissionChecker(
    IUserRoleProvider userRoleProvider,
    DynamicAuthorizationCache cache,
    IOptions<DynamicAuthorizationOptions> options) : IPermissionChecker
{
    private readonly Lock _lock = new();
    private ScopeMemo? _memo;

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
        {
            return Task.FromResult(new PermissionEvaluation(permission, PermissionDecision.NotAuthenticated, []));
        }

        return EvaluateUserCoreAsync(userRoleProvider.GetUserId(user), user.Identity.Name, permission, stopAtFirstGrant, cancellationToken);
    }

    private async Task<PermissionEvaluation> EvaluateUserCoreAsync(
        string? userId,
        string? userName,
        string permission,
        bool stopAtFirstGrant,
        CancellationToken cancellationToken)
    {
        DynamicAuthorizationOptions settings = options.Value;
        if (userName is not null && settings.SuperAdminUsers.Contains(userName))
        {
            return new PermissionEvaluation(permission, PermissionDecision.SuperAdminUser, []);
        }

        if (string.IsNullOrEmpty(userId))
        {
            return new PermissionEvaluation(permission, PermissionDecision.NotGranted, []);
        }

        IReadOnlyList<RoleReference> roles = await GetRolesAsync(userId, cancellationToken).ConfigureAwait(false);
        foreach (RoleReference role in roles)
        {
            if (settings.SuperAdminRoles.Contains(role.Name))
            {
                return new PermissionEvaluation(permission, PermissionDecision.SuperAdminRole, [role]);
            }
        }

        if (roles.Count == 0)
        {
            return new PermissionEvaluation(permission, PermissionDecision.NotGranted, []);
        }

        IReadOnlyList<PermissionSet> permissions = await GetPermissionsAsync(roles, cancellationToken).ConfigureAwait(false);
        List<RoleReference> grantingRoles = [];
        string? impliedBy = null;
        for (int i = 0; i < roles.Count; i++)
        {
            PermissionSet granted = permissions[i];
            if (!granted.Contains(permission))
            {
                string? implying = settings.OperationsImplyRead ? granted.FindImplying(permission) : null;
                if (implying is null)
                {
                    continue;
                }

                impliedBy ??= implying;
            }

            grantingRoles.Add(roles[i]);
            if (stopAtFirstGrant)
            {
                break;
            }
        }

        return grantingRoles.Count == 0
            ? new PermissionEvaluation(permission, PermissionDecision.NotGranted, [])
            : new PermissionEvaluation(permission, PermissionDecision.GrantedByRole, grantingRoles) { ImpliedBy = impliedBy };
    }

    /// <summary>
    ///   Gets the roles of a user: from the memo of this scope when it holds them, otherwise from the cache.
    /// </summary>
    private async Task<IReadOnlyList<RoleReference>> GetRolesAsync(string userId, CancellationToken cancellationToken)
    {
        ScopeMemo? memo = GetMemo();
        if (memo is not null)
        {
            lock (_lock)
            {
                if (memo.Roles is { } memoized && string.Equals(memo.UserId, userId, StringComparison.Ordinal))
                {
                    return memoized;
                }
            }
        }

        // Loads resolve the role provider from a scope of their own, see DynamicAuthorizationCache.
        Cached<IReadOnlyList<RoleReference>> roles = await cache
            .GetUserRolesAsync(userId, (services, ct) => services.GetRequiredService<IUserRoleProvider>().GetUserRolesAsync(userId, ct), cancellationToken)
            .ConfigureAwait(false);

        // When the cache was invalidated meanwhile, this memo has been superseded and the result is not kept.
        if (memo is not null)
        {
            lock (_lock)
            {
                memo.UserId = userId;
                memo.Roles = roles.Value;
                memo.Keep(roles.ExpiresAt);
            }
        }

        return roles.Value;
    }

    /// <summary>
    ///   Gets the permissions of roles, in the order of <paramref name="roles"/>: from the memo of this scope when it
    ///   holds them, otherwise from the cache, which loads every role it does not hold with one store call.
    /// </summary>
    private async Task<IReadOnlyList<PermissionSet>> GetPermissionsAsync(IReadOnlyList<RoleReference> roles, CancellationToken cancellationToken)
    {
        ScopeMemo? memo = GetMemo();
        if (memo is null)
        {
            return (await cache.GetRolePermissionsAsync([.. roles.Select(r => r.Id)], LoadPermissionsAsync, cancellationToken).ConfigureAwait(false)).Value;
        }

        PermissionSet[] permissions = new PermissionSet[roles.Count];
        List<int>? missing = null;
        lock (_lock)
        {
            for (int i = 0; i < roles.Count; i++)
            {
                if (memo.Permissions.TryGetValue(roles[i].Id, out PermissionSet? memoized))
                {
                    permissions[i] = memoized;
                }
                else
                {
                    (missing ??= []).Add(i);
                }
            }
        }

        if (missing is not null)
        {
            string[] roleIds = [.. missing.Select(i => roles[i].Id)];
            Cached<IReadOnlyList<PermissionSet>> loaded = await cache.GetRolePermissionsAsync(roleIds, LoadPermissionsAsync, cancellationToken).ConfigureAwait(false);
            lock (_lock)
            {
                for (int i = 0; i < missing.Count; i++)
                {
                    permissions[missing[i]] = loaded.Value[i];
                    memo.Permissions[roleIds[i]] = loaded.Value[i];
                }

                memo.Keep(loaded.ExpiresAt);
            }
        }

        return permissions;
    }

    /// <summary>
    ///   Gets the memo of this scope, or <see langword="null"/> when caching is disabled. The memo is replaced when the
    ///   cache has been invalidated since it was created, so that a change made during a request is seen by the checks
    ///   that follow it, and when the first cache entry it was filled from expires, so that a scope which outlives a
    ///   request, for example one held by a background service, sees changes made elsewhere when the cache does.
    /// </summary>
    private ScopeMemo? GetMemo()
    {
        if (options.Value.CacheDuration <= TimeSpan.Zero)
        {
            return null;
        }

        long version = cache.Version;
        long now = Environment.TickCount64;
        lock (_lock)
        {
            if (_memo is null || _memo.Version != version || now >= _memo.ExpiresAt)
            {
                _memo = new ScopeMemo(version);
            }

            return _memo;
        }
    }

    /// <summary>
    ///   Loads the permissions of roles from the store, in one call, and indexes them. Roles the store leaves out have
    ///   no permissions.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, PermissionSet>> LoadPermissionsAsync(
        IServiceProvider services,
        IReadOnlyList<string> roleIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, IReadOnlySet<string>> permissions = await services.GetRequiredService<IPermissionStore>()
            .GetPermissionsAsync(roleIds, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, PermissionSet> sets = new(roleIds.Count, StringComparer.Ordinal);
        foreach (string roleId in roleIds)
        {
            sets[roleId] = permissions.TryGetValue(roleId, out IReadOnlySet<string>? granted) && granted.Count > 0
                ? new PermissionSet(granted)
                : PermissionSet.Empty;
        }

        return sets;
    }

    /// <summary>
    ///   What one scope has resolved so far: the roles of the last user checked and the permissions of every role seen,
    ///   kept no longer than the cache entries they were read from.
    /// </summary>
    private sealed class ScopeMemo(long version)
    {
        public long Version { get; } = version;

        /// <summary>
        ///   Gets when the first cache entry this memo was filled from expires, as <see cref="Environment.TickCount64"/>.
        /// </summary>
        public long ExpiresAt { get; private set; } = long.MaxValue;

        public string? UserId { get; set; }

        public IReadOnlyList<RoleReference>? Roles { get; set; }

        public Dictionary<string, PermissionSet> Permissions { get; } = new(StringComparer.Ordinal);

        public void Keep(long expiresAt) => ExpiresAt = Math.Min(ExpiresAt, expiresAt);
    }
}
