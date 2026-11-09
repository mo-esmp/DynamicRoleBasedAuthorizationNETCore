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
///     An invalidation writes a marker to the cache, for the key or for everything, and removes the entry of the key
///     from the distributed cache. Every entry carries the time its load started, and every read compares it with the
///     markers: an entry loaded before an invalidation is dropped, wherever it was loaded, so the result of a load
///     that was running during an invalidation, which <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/>
///     keeps, is served to the callers waiting for it, as with the in-memory store, and to nobody else. The local
///     copies of the markers expire like the entries', so the other instances see an invalidation within
///     <see cref="DynamicAuthorizationHybridCacheOptions.LocalCacheDuration"/>. Tags are not used for that: an
///     invalidation by tag is not seen by the other instances, which read the tag once and keep it.
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
    private const int PruneAbove = 1024;

    // Lifetimes are capped so that they can be added to the current time.
    private static readonly TimeSpan s_maxLifetime = TimeSpan.FromDays(365 * 100);

    private readonly Microsoft.Extensions.Caching.Hybrid.HybridCache _cache;
    private readonly HybridCacheEntryOptions _entryOptions;
    private readonly HybridCacheEntryOptions _markerReadOptions;
    private readonly TimeSpan _lifetime;
    private readonly TimeSpan _localLifetime;
    private readonly string _keyPrefix;
    private readonly string _generationKey;
    private readonly string[] _allTags;

    // When each key, and everything, was last invalidated on this instance, as UTC ticks. The markers in the cache
    // carry the same, for every instance; these make an invalidation made here visible at once, whatever the cache's
    // local copy of the marker says.
    private readonly ConcurrentDictionary<string, long> _invalidatedHere = new(StringComparer.Ordinal);
    private long _allInvalidatedHereAt;

    // The local copy of an entry lives LocalCacheDuration from the time it was written, which HybridCache does not tell.
    // It keeps the same instance of an immutable entry while the copy lives, so a new instance for a key is a new copy,
    // and its expiry is recorded then.
    private readonly ConcurrentDictionary<string, LocalCopy> _localCopies = new(StringComparer.Ordinal);

    public HybridDynamicAuthorizationCacheStore(
        Microsoft.Extensions.Caching.Hybrid.HybridCache cache,
        IOptions<DynamicAuthorizationOptions> options,
        IOptions<DynamicAuthorizationHybridCacheOptions> hybridOptions)
    {
        _cache = cache;
        _lifetime = Cap(options.Value.CacheDuration);
        _localLifetime = Cap(hybridOptions.Value.LocalCacheDuration < _lifetime ? hybridOptions.Value.LocalCacheDuration : _lifetime);
        _entryOptions = new HybridCacheEntryOptions { Expiration = _lifetime, LocalCacheExpiration = _localLifetime };

        // A marker that is not there reads as "never"; that default must not reach the distributed cache, where it
        // could land after an invalidation wrote the real time and hide it from every instance.
        _markerReadOptions = new HybridCacheEntryOptions
        {
            Expiration = _lifetime,
            LocalCacheExpiration = _localLifetime,
            Flags = HybridCacheEntryFlags.DisableDistributedCacheWrite,
        };
        _keyPrefix = hybridOptions.Value.KeyPrefix;
        _generationKey = _keyPrefix + "invalidated:all";
        _allTags = [_keyPrefix + "all"];
    }

    public async ValueTask<CachedValue<IReadOnlyList<RoleReference>>> GetUserRolesAsync(
        string userId,
        Func<CancellationToken, Task<IReadOnlyList<RoleReference>>> load,
        CancellationToken cancellationToken)
    {
        string key = UserKey(userId);
        CachedRoles roles = await ReadAsync(
            key,
            UserMarkerKey(userId),
            async (startedAt, ct) => new CachedRoles([.. await load(ct).ConfigureAwait(false)], startedAt, startedAt + _lifetime),
            cancellationToken).ConfigureAwait(false);

        return new CachedValue<IReadOnlyList<RoleReference>>(roles.Roles, ExpiresAt(key, roles));
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
                RoleMarkerKey(roleId),
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
            DateTimeOffset itemExpiresAt = ExpiresAt(RoleKey(roleIds[i]), results[i]);
            expiresAt = itemExpiresAt < expiresAt ? itemExpiresAt : expiresAt;
        }

        return new CachedValue<IReadOnlyDictionary<string, IReadOnlySet<string>>>(permissions, expiresAt);
    }

    public ValueTask InvalidateUserAsync(string userId, CancellationToken cancellationToken) =>
        InvalidateAsync(UserKey(userId), UserMarkerKey(userId), cancellationToken);

    public ValueTask InvalidateRoleAsync(string roleId, CancellationToken cancellationToken) =>
        InvalidateAsync(RoleKey(roleId), RoleMarkerKey(roleId), cancellationToken);

    public async ValueTask InvalidateAllAsync(CancellationToken cancellationToken)
    {
        long now = DateTimeOffset.UtcNow.UtcTicks;
        Volatile.Write(ref _allInvalidatedHereAt, now);
        _invalidatedHere.Clear();

        // The marker reaches the other instances when their local copy of it expires; they then drop every entry
        // loaded before it. Here, every entry carries the tag and is dropped at once.
        await _cache.SetAsync(_generationKey, now, _entryOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        await _cache.RemoveByTagAsync(_allTags[0], cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask InvalidateAsync(string key, string markerKey, CancellationToken cancellationToken)
    {
        long now = DateTimeOffset.UtcNow.UtcTicks;
        _invalidatedHere[key] = now;
        if (_invalidatedHere.Count > PruneAbove)
        {
            // Invalidations older than the lifetime of an entry cannot make any entry stale any more.
            long threshold = now - _lifetime.Ticks;
            foreach (KeyValuePair<string, long> stamp in _invalidatedHere)
            {
                if (stamp.Value < threshold)
                {
                    _invalidatedHere.TryRemove(stamp);
                }
            }
        }

        // The marker makes every instance drop an entry of the key loaded before now, including the result of a load
        // running on another instance; removing the key takes the entry out of the distributed cache, so that the
        // other instances miss and load again once their local copies expire.
        await _cache.SetAsync(markerKey, now, _entryOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///   Reads an entry, creating it with <paramref name="create"/> when it is not cached. An entry whose load started
    ///   before the key, or everything, was last invalidated is dropped: the caller whose load it was is served with
    ///   it, like the callers waiting for a load of the in-memory store, and any other caller reads again.
    /// </summary>
    private async ValueTask<TItem> ReadAsync<TItem>(string key, string markerKey, Func<DateTimeOffset, CancellationToken, Task<TItem>> create, CancellationToken cancellationToken)
        where TItem : class, ICachedItem
    {
        LoadState<TItem> state = new(create);
        TItem item = await _cache.GetOrCreateAsync(key, state, static (s, ct) => s.LoadAsync(ct), _entryOptions, _allTags, cancellationToken).ConfigureAwait(false);
        if (!await InvalidatedSinceAsync(key, markerKey, item.LoadStartedAt, cancellationToken).ConfigureAwait(false))
        {
            return item;
        }

        await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

        return state.Loaded ? item : await ReadAsync(key, markerKey, create, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///   Tells whether an entry whose load started at <paramref name="loadStartedAt"/> predates an invalidation of its
    ///   key or of everything: one made here, or one made on any instance, which the markers in the cache record. The
    ///   local copies of the markers live as long as any entry's.
    /// </summary>
    private async ValueTask<bool> InvalidatedSinceAsync(string key, string markerKey, DateTimeOffset loadStartedAt, CancellationToken cancellationToken)
    {
        long startedAt = loadStartedAt.UtcTicks;
        if (startedAt <= Volatile.Read(ref _allInvalidatedHereAt)
            || (_invalidatedHere.TryGetValue(key, out long invalidatedHereAt) && startedAt <= invalidatedHereAt))
        {
            return true;
        }

        return startedAt <= await MarkerAsync(_generationKey, cancellationToken).ConfigureAwait(false)
            || startedAt <= await MarkerAsync(markerKey, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<long> MarkerAsync(string markerKey, CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync(markerKey, static _ => new ValueTask<long>(0L), _markerReadOptions, cancellationToken: cancellationToken);

    /// <summary>
    ///   Tells when a value read from an entry stops being valid for its reader: when the entry expires, or when the
    ///   local copy it was read from does, whichever comes first.
    /// </summary>
    private DateTimeOffset ExpiresAt(string key, ICachedItem item)
    {
        long now = DateTimeOffset.UtcNow.UtcTicks;
        LocalCopy copy = _localCopies.AddOrUpdate(
            key,
            static (_, state) => new LocalCopy(state.item, state.now + state.lifetime),
            static (_, existing, state) => ReferenceEquals(existing.Item, state.item) ? existing : new LocalCopy(state.item, state.now + state.lifetime),
            (item, now, lifetime: _localLifetime.Ticks));

        if (_localCopies.Count > PruneAbove)
        {
            foreach (KeyValuePair<string, LocalCopy> expired in _localCopies)
            {
                if (expired.Value.ExpiresAt < now)
                {
                    _localCopies.TryRemove(expired);
                }
            }
        }

        DateTimeOffset local = new(copy.ExpiresAt, TimeSpan.Zero);

        return item.ExpiresAt < local ? item.ExpiresAt : local;
    }

    // Markers have a namespace of their own, so that no identifier can make an entry key equal a marker key.
    private string UserKey(string userId) => _keyPrefix + "user:" + userId;

    private string RoleKey(string roleId) => _keyPrefix + "role:" + roleId;

    private string UserMarkerKey(string userId) => _keyPrefix + "invalidated:user:" + userId;

    private string RoleMarkerKey(string roleId) => _keyPrefix + "invalidated:role:" + roleId;

    private static TimeSpan Cap(TimeSpan lifetime) => lifetime > s_maxLifetime ? s_maxLifetime : lifetime;

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
    ///   An entry instance seen on this instance and when its local copy expires, as UTC ticks.
    /// </summary>
    private sealed record LocalCopy(object Item, long ExpiresAt);

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
