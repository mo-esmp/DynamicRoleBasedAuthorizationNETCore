using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Checks permissions against the roles of the user and the permissions of those roles. Within its scope, usually
///   one request, the roles of a user and the permissions of a role are resolved once however many checks are made,
///   until the cache is invalidated or the cache duration passes. With caching disabled, nothing is kept.
/// </summary>
/// <remarks>
///   <para>
///     A permission is decided in this order: unauthenticated principals and users without an identifier are never
///     granted; a user listed in <see cref="DynamicAuthorizationOptions.SuperAdminUsers"/> and a member of a role listed
///     in <see cref="DynamicAuthorizationOptions.SuperAdminRoles"/> are granted everything; otherwise the permission is
///     granted by every role that holds it, or, for a <c>{resource}.read</c> permission while
///     <see cref="DynamicAuthorizationOptions.OperationsImplyRead"/> is on, by every role that holds any permission on
///     the resource. The <see cref="PermissionEvaluation"/> names the decision, the granting roles and, for an implied
///     read, the permission that implied it.
///   </para>
///   <para>
///     Roles and permissions are read through <see cref="DynamicAuthorizationCache"/>, which loads the roles of a user
///     with <see cref="IUserRoleProvider"/> and the permissions of every role it does not hold with one
///     <see cref="IPermissionStore"/> call. Checks made in this scope keep what they read in a memo, so that a page with
///     many <c>secure-content</c> tags resolves the user's roles and each role's permissions once; the memo is dropped
///     when the cache is invalidated and expires with the cache entries it was filled from. Like other scoped services,
///     the checker is not meant to be used concurrently, but the memo is guarded by a lock so that concurrent checks in
///     one scope cannot corrupt it.
///   </para>
/// </remarks>
internal sealed class PermissionChecker(
    IUserRoleProvider userRoleProvider,
    DynamicAuthorizationCache cache,
    IOptions<DynamicAuthorizationOptions> options) : IPermissionChecker
{
    // Guards the memo. Checks of one scope are expected to run one at a time; concurrent ones must not corrupt it.
    private readonly Lock _lock = new();

    // What this scope has resolved so far; see GetMemo for when it is replaced.
    private ScopeMemo? _memo;

    /// <inheritdoc />
    /// <remarks>
    ///   Only the decision is needed, so the evaluation stops at the first role that grants the permission.
    /// </remarks>
    public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission,
        CancellationToken cancellationToken = default)
    {
        PermissionEvaluation evaluation =
            await EvaluateCoreAsync(user, permission, stopAtFirstGrant: true, cancellationToken).ConfigureAwait(false);

        return evaluation.IsGranted;
    }

    /// <inheritdoc />
    /// <remarks>
    ///   Every granting role is collected, so that <see cref="PermissionEvaluation.GrantingRoles"/> is complete.
    /// </remarks>
    public Task<PermissionEvaluation> EvaluateAsync(ClaimsPrincipal user, string permission,
        CancellationToken cancellationToken = default) =>
        EvaluateCoreAsync(user, permission, stopAtFirstGrant: false, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    ///   The user is not required to exist: an unknown identifier has no roles and is not granted anything, unless the
    ///   username is a super admin user.
    /// </remarks>
    public Task<PermissionEvaluation> EvaluateUserAsync(string userId, string? userName, string permission,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentException.ThrowIfNullOrEmpty(permission);

        return EvaluateUserCoreAsync(userId, userName, permission, stopAtFirstGrant: false, cancellationToken);
    }

    /// <summary>
    ///   Evaluates a permission for a principal. Unauthenticated principals are never granted; for the others, the user
    ///   identifier comes from <see cref="IUserRoleProvider.GetUserId"/> and the username from the identity.
    /// </summary>
    /// <param name="user">The principal.</param>
    /// <param name="permission">The permission key.</param>
    /// <param name="stopAtFirstGrant">
    ///   Whether to stop at the first role that grants the permission, when only the decision is needed, instead of
    ///   collecting every granting role.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The evaluation.</returns>
    private Task<PermissionEvaluation> EvaluateCoreAsync(ClaimsPrincipal user, string permission, bool stopAtFirstGrant,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrEmpty(permission);

        if (user.Identity?.IsAuthenticated != true)
        {
            return Task.FromResult(new PermissionEvaluation(permission, PermissionDecision.NotAuthenticated, []));
        }

        return EvaluateUserCoreAsync(userRoleProvider.GetUserId(user), user.Identity.Name, permission, stopAtFirstGrant,
            cancellationToken);
    }

    /// <summary>
    ///   Evaluates a permission for a user given by identifier and name: super admin users first, then super admin
    ///   roles, then the permissions of the user's roles, which are read together.
    /// </summary>
    /// <param name="userId">The user identifier; without one, the permission is not granted.</param>
    /// <param name="userName">The username, used to recognize super admin users; <see langword="null"/> when unknown.</param>
    /// <param name="permission">The permission key.</param>
    /// <param name="stopAtFirstGrant">
    ///   Whether to stop at the first role that grants the permission, when only the decision is needed, instead of
    ///   collecting every granting role.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    ///   The evaluation. For a permission granted by roles, <see cref="PermissionEvaluation.ImpliedBy"/> names the
    ///   permission that implied it when no role holds the permission itself.
    /// </returns>
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
            return PermissionEvaluation.Create(permission, PermissionDecision.SuperAdminUser, []);
        }

        if (string.IsNullOrEmpty(userId))
        {
            return PermissionEvaluation.Create(permission, PermissionDecision.NotGranted, []);
        }

        IReadOnlyList<RoleReference> roles = await GetRolesAsync(userId, cancellationToken).ConfigureAwait(false);
        foreach (RoleReference role in roles)
        {
            if (settings.SuperAdminRoles.Contains(role.Name))
            {
                return PermissionEvaluation.Create(permission, PermissionDecision.SuperAdminRole, [role]);
            }
        }

        if (roles.Count == 0)
        {
            return PermissionEvaluation.Create(permission, PermissionDecision.NotGranted, []);
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
            : new PermissionEvaluation(permission, PermissionDecision.GrantedByRole, grantingRoles)
            {
                ImpliedBy = impliedBy
            };
    }

    /// <summary>
    ///   Gets the roles of a user: from the memo of this scope when it holds them, otherwise from the cache.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The roles of the user.</returns>
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
            .GetUserRolesAsync(userId,
                (services, ct) => services.GetRequiredService<IUserRoleProvider>().GetUserRolesAsync(userId, ct),
                cancellationToken)
            .ConfigureAwait(false);

        // When the cache was invalidated meanwhile, this memo has been superseded and the result is not kept.
        if (memo is null)
        {
            return roles.Value;
        }

        lock (_lock)
        {
            memo.UserId = userId;
            memo.Roles = roles.Value;
            memo.Keep(roles.ExpiresAt);
        }

        return roles.Value;
    }

    /// <summary>
    ///   Gets the permissions of roles, in the order of <paramref name="roles"/>: from the memo of this scope when it
    ///   holds them, otherwise from the cache, which loads every role it does not hold with one store call.
    /// </summary>
    /// <param name="roles">The roles.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The permissions of each role, in the order of <paramref name="roles"/>.</returns>
    private async Task<IReadOnlyList<PermissionSet>> GetPermissionsAsync(IReadOnlyList<RoleReference> roles,
        CancellationToken cancellationToken)
    {
        ScopeMemo? memo = GetMemo();
        if (memo is null)
        {
            return (await cache
                .GetRolePermissionsAsync([.. roles.Select(r => r.Id)], LoadPermissionsAsync, cancellationToken)
                .ConfigureAwait(false)).Value;
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

        if (missing is null)
        {
            return permissions;
        }


        string[] roleIds = [.. missing.Select(i => roles[i].Id)];
        Cached<IReadOnlyList<PermissionSet>> loaded = await cache
            .GetRolePermissionsAsync(roleIds, LoadPermissionsAsync, cancellationToken).ConfigureAwait(false);
        lock (_lock)
        {
            for (int i = 0; i < missing.Count; i++)
            {
                permissions[missing[i]] = loaded.Value[i];
                memo.Permissions[roleIds[i]] = loaded.Value[i];
            }

            memo.Keep(loaded.ExpiresAt);
        }


        return permissions;
    }

    /// <summary>
    ///   Gets the memo of this scope, or <see langword="null"/> when caching is disabled. The memo is replaced when the
    ///   cache has been invalidated since it was created, so that a change made during a request is seen by the checks
    ///   that follow it, and when the first cache entry it was filled from expires, so that a scope which outlives a
    ///   request, for example one held by a background service, sees changes made elsewhere when the cache does.
    /// </summary>
    /// <returns>The memo to read from and fill, or <see langword="null"/> when caching is disabled.</returns>
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
    ///   no permissions. Runs in the service scope of the cache load, not in the scope of the check.
    /// </summary>
    /// <param name="services">The service provider of the load, from which the store is resolved.</param>
    /// <param name="roleIds">The identifiers of the roles to load.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The permissions of every role in <paramref name="roleIds"/>, keyed by identifier.</returns>
    private static async Task<IReadOnlyDictionary<string, PermissionSet>> LoadPermissionsAsync(
        IServiceProvider services,
        IReadOnlyList<string> roleIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, IReadOnlySet<string>> permissions = await services
            .GetRequiredService<IPermissionStore>()
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
        /// <summary>
        ///   Gets the <see cref="DynamicAuthorizationCache.Version"/> the memo was created at; an invalidation since
        ///   then supersedes it.
        /// </summary>
        public long Version { get; } = version;

        /// <summary>
        ///   Gets when the first cache entry this memo was filled from expires, as <see cref="Environment.TickCount64"/>.
        /// </summary>
        public long ExpiresAt { get; private set; } = long.MaxValue;

        /// <summary>
        ///   Gets or sets the identifier of the user whose roles <see cref="Roles"/> holds.
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>
        ///   Gets or sets the roles of the user identified by <see cref="UserId"/>, or <see langword="null"/> when no
        ///   user has been checked yet.
        /// </summary>
        public IReadOnlyList<RoleReference>? Roles { get; set; }

        /// <summary>
        ///   Gets the permissions of every role seen so far, by role identifier.
        /// </summary>
        public Dictionary<string, PermissionSet> Permissions { get; } = new(StringComparer.Ordinal);

        /// <summary>
        ///   Records that the memo was filled from a cache entry expiring at <paramref name="expiresAt"/>, so that it
        ///   expires no later than that entry.
        /// </summary>
        /// <param name="expiresAt">When the entry expires, as <see cref="Environment.TickCount64"/>.</param>
        public void Keep(long expiresAt) => ExpiresAt = Math.Min(ExpiresAt, expiresAt);
    }
}
