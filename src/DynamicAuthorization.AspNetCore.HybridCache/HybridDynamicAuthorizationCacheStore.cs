using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore.HybridCache;

/// <summary>
///   Keeps user roles and role permissions in a <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/>: in the
///   memory of the instance for <see cref="DynamicAuthorizationHybridCacheOptions.LocalCacheDuration"/> and, when one
///   is registered, in the distributed cache every instance shares for <see cref="DynamicAuthorizationOptions.CacheDuration"/>.
///   <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/> coalesces concurrent loads of one key and does not
///   keep failed loads.
/// </summary>
/// <remarks>
///   <para>
///     Invalidating a user or a role removes the entry from the distributed cache, so the other instances miss it and
///     load again once their local copies expire. Invalidating everything writes a generation marker to the cache;
///     every read compares the time its entry's load started with the marker, whose local copy expires like the
///     entries', and drops entries loaded before it. Tags are not used for that: an invalidation by tag is not seen by
///     the other instances until their local copies expire and they read the tag again, which
///     <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/> does not do.
///   </para>
///   <para>
///     <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/> keeps the result of a load that was running when
///     its key was invalidated, so the store also remembers when it last invalidated each key here and drops an entry
///     loaded before that: the callers waiting for that load are served, as with the in-memory store, and the next
///     reader loads again. An invalidation of a key made on another instance while a load runs here is not seen until
///     the local copy expires.
///   </para>
///   <para>
///     <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/> reads one key at a time, so the permissions of
///     several roles that are not cached are loaded one role at a time, each load shared by every instance that
///     reads the role. A value is reported to expire when its local copy does, so that a reader keeping it sees a
///     change made elsewhere when the next read would.
///   </para>
/// </remarks>
internal sealed class HybridDynamicAuthorizationCacheStore : IDynamicAuthorizationCacheStore
{
    private const string KeyPrefix = "dynamic-authorization:";
    private const string AllTag = "dynamic-authorization";
    private const string GenerationKey = KeyPrefix + "generation";
    private const int PruneAbove = 1024;

    private static readonly string[] s_allTags = [AllTag];

    private readonly Microsoft.Extensions.Caching.Hybrid.HybridCache _cache;
    private readonly HybridCacheEntryOptions _entryOptions;
    private readonly TimeSpan _lifetime;
    private readonly TimeSpan _localLifetime;

    // When each key, and everything, was last invalidated here, as UTC ticks; entries whose load started before are stale.
    private readonly ConcurrentDictionary<string, long> _invalidated = new(StringComparer.Ordinal);
    private long _allInvalidatedAt;

    public HybridDynamicAuthorizationCacheStore(
        Microsoft.Extensions.Caching.Hybrid.HybridCache cache,
        IOptions<DynamicAuthorizationOptions> options,
        IOptions<DynamicAuthorizationHybridCacheOptions> hybridOptions)
    {
        _cache = cache;
        _lifetime = options.Value.CacheDuration;
        _localLifetime = hybridOptions.Value.LocalCacheDuration < _lifetime ? hybridOptions.Value.LocalCacheDuration : _lifetime;
        _entryOptions = new HybridCacheEntryOptions { Expiration = _lifetime, LocalCacheExpiration = _localLifetime };
    }

    public async ValueTask<CachedValue<IReadOnlyList<RoleReference>>> GetUserRolesAsync(
        string userId,
        Func<CancellationToken, Task<IReadOnlyList<RoleReference>>> load,
        CancellationToken cancellationToken)
    {
        CachedRoles roles = await ReadAsync(
            UserKey(userId),
            async (startedAt, ct) => new CachedRoles([.. await load(ct).ConfigureAwait(false)], startedAt, startedAt + _lifetime),
            cancellationToken).ConfigureAwait(false);

        return new CachedValue<IReadOnlyList<RoleReference>>(roles.Roles, ExpiresAt(roles));
    }

    public async ValueTask<CachedValue<IReadOnlyDictionary<string, IReadOnlySet<string>>>> GetRolePermissionsAsync(
        IReadOnlyList<string> roleIds,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, IReadOnlySet<string>>>> load,
        CancellationToken cancellationToken)
    {
        Task<CachedPermissions>[] reads = new Task<CachedPermissions>[roleIds.Count];
        for (int i = 0; i < roleIds.Count; i++)
        {
            string roleId = roleIds[i];
            reads[i] = ReadAsync(
                RoleKey(roleId),
                async (startedAt, ct) =>
                {
                    IReadOnlyDictionary<string, IReadOnlySet<string>> loaded = await load([roleId], ct).ConfigureAwait(false);
                    string[] permissions = loaded.TryGetValue(roleId, out IReadOnlySet<string>? granted) ? [.. granted] : [];

                    return new CachedPermissions(permissions, startedAt, startedAt + _lifetime);
                },
                cancellationToken).AsTask();
        }

        CachedPermissions[] results = await Task.WhenAll(reads).ConfigureAwait(false);
        Dictionary<string, IReadOnlySet<string>> permissions = new(roleIds.Count, StringComparer.Ordinal);
        DateTimeOffset expiresAt = DateTimeOffset.MaxValue;
        for (int i = 0; i < roleIds.Count; i++)
        {
            permissions[roleIds[i]] = results[i].Set;
            DateTimeOffset itemExpiresAt = ExpiresAt(results[i]);
            expiresAt = itemExpiresAt < expiresAt ? itemExpiresAt : expiresAt;
        }

        return new CachedValue<IReadOnlyDictionary<string, IReadOnlySet<string>>>(permissions, expiresAt);
    }

    public ValueTask InvalidateUserAsync(string userId, CancellationToken cancellationToken) => InvalidateAsync(UserKey(userId), cancellationToken);

    public ValueTask InvalidateRoleAsync(string roleId, CancellationToken cancellationToken) => InvalidateAsync(RoleKey(roleId), cancellationToken);

    public async ValueTask InvalidateAllAsync(CancellationToken cancellationToken)
    {
        long now = DateTimeOffset.UtcNow.UtcTicks;
        Volatile.Write(ref _allInvalidatedAt, now);
        _invalidated.Clear();

        // The generation marker reaches the other instances when their local copy of it expires; they then drop every
        // entry loaded before it. Here, every entry carries the tag and is dropped at once.
        await _cache.SetAsync(GenerationKey, now, _entryOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        await _cache.RemoveByTagAsync(AllTag, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask InvalidateAsync(string key, CancellationToken cancellationToken)
    {
        long now = DateTimeOffset.UtcNow.UtcTicks;
        _invalidated[key] = now;
        if (_invalidated.Count > PruneAbove)
        {
            // Invalidations older than the lifetime of an entry cannot make any entry stale any more.
            long threshold = now - _lifetime.Ticks;
            foreach (KeyValuePair<string, long> stamp in _invalidated)
            {
                if (stamp.Value < threshold)
                {
                    _invalidated.TryRemove(stamp);
                }
            }
        }

        // Removing the key takes the entry out of the distributed cache, so that the other instances miss and load
        // again once their local copies expire.
        await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
    }

    // A value is kept by its reader no longer than the local copy it was read from lives, so that an invalidation
    // made elsewhere is seen when the next read would see it.
    private DateTimeOffset ExpiresAt(ICachedItem item)
    {
        DateTimeOffset local = DateTimeOffset.UtcNow + _localLifetime;

        return item.ExpiresAt < local ? item.ExpiresAt : local;
    }

    /// <summary>
    ///   Reads an entry, creating it with <paramref name="create"/> when it is not cached. An entry whose load started
    ///   before the key was last invalidated here is dropped: the caller whose load it was is served with it, like the
    ///   callers waiting for a load of the in-memory store, and any other caller reads again.
    /// </summary>
    private async ValueTask<TItem> ReadAsync<TItem>(string key, Func<DateTimeOffset, CancellationToken, Task<TItem>> create, CancellationToken cancellationToken)
        where TItem : class, ICachedItem
    {
        LoadState<TItem> state = new(create);
        TItem item = await _cache.GetOrCreateAsync(key, state, static (s, ct) => s.LoadAsync(ct), _entryOptions, s_allTags, cancellationToken).ConfigureAwait(false);
        if (!await InvalidatedSinceAsync(key, item.LoadStartedAt, cancellationToken).ConfigureAwait(false))
        {
            return item;
        }

        await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

        return state.Loaded ? item : await ReadAsync(key, create, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///   Tells whether an entry whose load started at <paramref name="loadStartedAt"/> predates an invalidation: of
    ///   its key or of everything here, or of everything on any instance, which the generation marker records.
    /// </summary>
    private async ValueTask<bool> InvalidatedSinceAsync(string key, DateTimeOffset loadStartedAt, CancellationToken cancellationToken)
    {
        long startedAt = loadStartedAt.UtcTicks;
        if (startedAt <= Volatile.Read(ref _allInvalidatedAt)
            || (_invalidated.TryGetValue(key, out long invalidatedAt) && startedAt <= invalidatedAt))
        {
            return true;
        }

        // The marker's local copy lives as long as any entry's, so an invalidation of everything made elsewhere is seen
        // when the entries loaded before it would be read again anyway.
        long generation = await _cache.GetOrCreateAsync(GenerationKey, static _ => new ValueTask<long>(0L), _entryOptions, cancellationToken: cancellationToken).ConfigureAwait(false);

        return startedAt <= generation;
    }

    private static string UserKey(string userId) => KeyPrefix + "user:" + userId;

    private static string RoleKey(string roleId) => KeyPrefix + "role:" + roleId;

    /// <summary>
    ///   Runs the load of one read, and remembers that this read was the one that loaded.
    /// </summary>
    private sealed class LoadState<TItem>(Func<DateTimeOffset, CancellationToken, Task<TItem>> create)
        where TItem : class, ICachedItem
    {
        public bool Loaded { get; private set; }

        public async ValueTask<TItem> LoadAsync(CancellationToken cancellationToken)
        {
            Loaded = true;

            return await create(DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///   A cached entry: when its load started and when it expires.
    /// </summary>
    internal interface ICachedItem
    {
        DateTimeOffset LoadStartedAt { get; }

        DateTimeOffset ExpiresAt { get; }
    }

    /// <summary>
    ///   The cached roles of a user. Immutable, so that the cache keeps the instance in memory instead of serializing
    ///   it on every read.
    /// </summary>
    [ImmutableObject(true)]
    internal sealed class CachedRoles(RoleReference[] roles, DateTimeOffset loadStartedAt, DateTimeOffset expiresAt) : ICachedItem
    {
        public RoleReference[] Roles { get; } = roles;

        public DateTimeOffset LoadStartedAt { get; } = loadStartedAt;

        public DateTimeOffset ExpiresAt { get; } = expiresAt;
    }

    /// <summary>
    ///   The cached permissions of a role. The set is built once per instance and kept with it, so that readers see
    ///   the same set and index it once.
    /// </summary>
    [ImmutableObject(true)]
    internal sealed class CachedPermissions(string[] permissions, DateTimeOffset loadStartedAt, DateTimeOffset expiresAt) : ICachedItem
    {
        private IReadOnlySet<string>? _set;

        public string[] Permissions { get; } = permissions;

        public DateTimeOffset LoadStartedAt { get; } = loadStartedAt;

        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        [JsonIgnore]
        public IReadOnlySet<string> Set => _set ??= new HashSet<string>(Permissions, StringComparer.OrdinalIgnoreCase);
    }
}
