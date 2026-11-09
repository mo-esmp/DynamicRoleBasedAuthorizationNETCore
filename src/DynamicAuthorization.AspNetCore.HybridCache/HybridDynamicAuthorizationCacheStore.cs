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

    // Markers are read from the distributed cache and kept here for LocalCacheDuration, not through the cache's own
    // local copies: an invalidation made here updates the copy at once, the default of a missing marker is written
    // nowhere, and the time a copy expires is known, so that a value's deadline can be bounded by it.
    private readonly ConcurrentDictionary<string, MarkerCopy> _markers = new(StringComparer.Ordinal);

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

        // Reads what the distributed cache holds, without touching any copy: a marker that is not there reads as
        // "never", and that default must not be written anywhere, where it could land after an invalidation wrote the
        // real time and hide it.
        _markerReadOptions = new HybridCacheEntryOptions
        {
            Expiration = _lifetime,
            LocalCacheExpiration = _localLifetime,
            Flags = HybridCacheEntryFlags.DisableLocalCacheRead | HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCacheWrite,
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
        (CachedRoles roles, long validUntil) = await ReadAsync(
            key,
            UserMarkerKey(userId),
            async (startedAt, ct) => new CachedRoles([.. await load(ct).ConfigureAwait(false)], startedAt, startedAt + _lifetime),
            cancellationToken).ConfigureAwait(false);

        return new CachedValue<IReadOnlyList<RoleReference>>(roles.Roles, ExpiresAt(key, roles, validUntil));
    }

    public async ValueTask<CachedValue<IReadOnlyDictionary<string, IReadOnlySet<string>>>> GetRolePermissionsAsync(
        IReadOnlyList<string> roleIds,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, IReadOnlySet<string>>>> load,
        CancellationToken cancellationToken)
    {
        Task<(CachedPermissions Item, long ValidUntil)>[] reads = new Task<(CachedPermissions, long)>[roleIds.Count];
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

        (CachedPermissions Item, long ValidUntil)[] results = await Task.WhenAll(reads).ConfigureAwait(false);
        Dictionary<string, IReadOnlySet<string>> permissions = new(roleIds.Count, StringComparer.Ordinal);
        DateTimeOffset expiresAt = DateTimeOffset.MaxValue;
        for (int i = 0; i < roleIds.Count; i++)
        {
            permissions[roleIds[i]] = results[i].Item.Set;
            DateTimeOffset itemExpiresAt = ExpiresAt(RoleKey(roleIds[i]), results[i].Item, results[i].ValidUntil);
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
        // The marker reaches the other instances when their copy of it expires; they then drop every entry loaded
        // before it. Here, every entry carries the tag and is dropped at once.
        await WriteMarkerAsync(_generationKey, cancellationToken).ConfigureAwait(false);
        await _cache.RemoveByTagAsync(_allTags[0], cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask InvalidateAsync(string key, string markerKey, CancellationToken cancellationToken)
    {
        // The marker makes every instance drop an entry of the key loaded before now, including the result of a load
        // running on another instance; removing the key takes the entry out of the distributed cache, so that the
        // other instances miss and load again once their local copies expire.
        await WriteMarkerAsync(markerKey, cancellationToken).ConfigureAwait(false);
        await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///   Records an invalidation in a marker, here at once and in the distributed cache so that the marker never moves
    ///   backwards: two instances invalidating the same key at the same time write in either order, so each reads the
    ///   marker back and writes again when it holds an older time. Both verify, so the newer time is what remains.
    /// </summary>
    private async ValueTask WriteMarkerAsync(string markerKey, CancellationToken cancellationToken)
    {
        long now = DateTimeOffset.UtcNow.UtcTicks;
        Remember(markerKey, now, now + _localLifetime.Ticks);
        await _cache.SetAsync(markerKey, now, _entryOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            long stored = await FetchMarkerAsync(markerKey, cancellationToken).ConfigureAwait(false);
            if (stored >= now)
            {
                return;
            }

            await _cache.SetAsync(markerKey, now, _entryOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///   Reads an entry, creating it with <paramref name="create"/> when it is not cached. An entry whose load started
    ///   before the key, or everything, was last invalidated is dropped: the caller whose load it was is served with
    ///   it, like the callers waiting for a load of the in-memory store, and any other caller reads again.
    /// </summary>
    /// <returns>The entry, and until when the markers that validated it are known, as UTC ticks.</returns>
    private async ValueTask<(TItem Item, long ValidUntil)> ReadAsync<TItem>(string key, string markerKey, Func<DateTimeOffset, CancellationToken, Task<TItem>> create, CancellationToken cancellationToken)
        where TItem : class, ICachedItem
    {
        LoadState<TItem> state = new(create);
        TItem item = await _cache.GetOrCreateAsync(key, state, static (s, ct) => s.LoadAsync(ct), _entryOptions, _allTags, cancellationToken).ConfigureAwait(false);

        // An entry expires a lifetime after its load started. A load that took longer than that completes with an
        // entry that has already expired, which the cache would keep for another lifetime, and any marker written
        // during the load may have expired with it.
        bool expired = item.ExpiresAt <= DateTimeOffset.UtcNow;
        (bool invalidated, long validUntil) = await InvalidatedSinceAsync(markerKey, item.LoadStartedAt, cancellationToken).ConfigureAwait(false);
        if (!expired && !invalidated)
        {
            return (item, validUntil);
        }

        await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

        return state.Loaded ? (item, validUntil) : await ReadAsync(key, markerKey, create, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///   Tells whether an entry whose load started at <paramref name="loadStartedAt"/> predates an invalidation of its
    ///   key or of everything, on any instance, which the markers record, and until when the copies of those markers
    ///   are current.
    /// </summary>
    private async ValueTask<(bool Invalidated, long ValidUntil)> InvalidatedSinceAsync(string markerKey, DateTimeOffset loadStartedAt, CancellationToken cancellationToken)
    {
        long startedAt = loadStartedAt.UtcTicks;
        MarkerCopy all = await MarkerAsync(_generationKey, cancellationToken).ConfigureAwait(false);
        MarkerCopy key = await MarkerAsync(markerKey, cancellationToken).ConfigureAwait(false);

        return (startedAt <= all.InvalidatedAt || startedAt <= key.InvalidatedAt, Math.Min(all.ExpiresAt, key.ExpiresAt));
    }

    /// <summary>
    ///   Gets the copy of a marker, reading the marker from the distributed cache when the copy has expired.
    /// </summary>
    private async ValueTask<MarkerCopy> MarkerAsync(string markerKey, CancellationToken cancellationToken)
    {
        long now = DateTimeOffset.UtcNow.UtcTicks;
        if (_markers.TryGetValue(markerKey, out MarkerCopy? copy) && copy.ExpiresAt > now)
        {
            return copy;
        }

        long stored = await FetchMarkerAsync(markerKey, cancellationToken).ConfigureAwait(false);

        return Remember(markerKey, stored, now + _localLifetime.Ticks);
    }

    private ValueTask<long> FetchMarkerAsync(string markerKey, CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync(markerKey, static _ => new ValueTask<long>(0L), _markerReadOptions, cancellationToken: cancellationToken);

    /// <summary>
    ///   Keeps a marker's copy: the latest invalidation time known, current until <paramref name="expiresAt"/>.
    /// </summary>
    private MarkerCopy Remember(string markerKey, long invalidatedAt, long expiresAt)
    {
        MarkerCopy copy = _markers.AddOrUpdate(
            markerKey,
            static (_, state) => new MarkerCopy(state.invalidatedAt, state.expiresAt),
            static (_, existing, state) => new MarkerCopy(Math.Max(existing.InvalidatedAt, state.invalidatedAt), state.expiresAt),
            (invalidatedAt, expiresAt));

        if (_markers.Count > PruneAbove)
        {
            long now = DateTimeOffset.UtcNow.UtcTicks;
            foreach (KeyValuePair<string, MarkerCopy> stale in _markers)
            {
                if (stale.Value.ExpiresAt < now)
                {
                    _markers.TryRemove(stale);
                }
            }
        }

        return copy;
    }

    /// <summary>
    ///   Tells when a value read from an entry stops being valid for its reader: when the entry expires, when the
    ///   local copy it was read from does, or when the copies of the markers that validated it do, whichever comes
    ///   first.
    /// </summary>
    private DateTimeOffset ExpiresAt(string key, ICachedItem item, long validUntil)
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

        DateTimeOffset local = new(Math.Min(copy.ExpiresAt, validUntil), TimeSpan.Zero);

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
    ///   The copy of a marker: the latest invalidation time known, and until when the copy is current, as UTC ticks.
    /// </summary>
    private sealed record MarkerCopy(long InvalidatedAt, long ExpiresAt);

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
