using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   The default <see cref="IDynamicAuthorizationCacheStore"/>: keeps user roles and role permissions in the memory
///   of the instance. Pending loads are cached too, so that concurrent reads of the same key share one load, and an
///   invalidation during a load removes the entry before the result is published. The permissions of several roles
///   that are not cached are loaded together.
/// </summary>
internal sealed class MemoryDynamicAuthorizationCacheStore : IDynamicAuthorizationCacheStore, IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    // Entry creation is serialized so that concurrent misses, for one key or for a batch of keys, share a single load.
    // Only dictionary operations run under the lock; loads start after it is released.
    private readonly Lock _registration = new();
    private readonly IOptions<DynamicAuthorizationOptions> _options;
    private CancellationTokenSource _generation = new();

    public MemoryDynamicAuthorizationCacheStore(IOptions<DynamicAuthorizationOptions> options)
    {
        _options = options;
    }

    public async ValueTask<CachedValue<IReadOnlyList<RoleReference>>> GetUserRolesAsync(
        string userId,
        Func<CancellationToken, Task<IReadOnlyList<RoleReference>>> load,
        CancellationToken cancellationToken)
    {
        Entry<IReadOnlyList<RoleReference>> entry;
        lock (_registration)
        {
            entry = GetOrAddLoad(UserKey(userId), () => load(CancellationToken.None), Lifetime);
        }

        // The load is shared with other callers and runs to completion even when this caller is cancelled.
        return new CachedValue<IReadOnlyList<RoleReference>>(
            await entry.Load.Value.WaitAsync(cancellationToken).ConfigureAwait(false),
            entry.ExpiresAt);
    }

    public async ValueTask<CachedValue<IReadOnlyDictionary<string, IReadOnlySet<string>>>> GetRolePermissionsAsync(
        IReadOnlyList<string> roleIds,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, IReadOnlySet<string>>>> load,
        CancellationToken cancellationToken)
    {
        Dictionary<string, IReadOnlySet<string>> permissions = new(roleIds.Count, StringComparer.Ordinal);
        if (roleIds.Count == 0)
        {
            return new CachedValue<IReadOnlyDictionary<string, IReadOnlySet<string>>>(permissions, DateTimeOffset.MaxValue);
        }

        // Entries that exist, complete or pending, are used as they are. The others share one load of all of them,
        // from which each entry picks its own result. Finding and registering the entries is one step under the lock,
        // so that concurrent checks with overlapping roles share loads instead of duplicating them, and every entry is
        // in the cache before the load starts: an invalidation during the load removes the entry, whichever role it is
        // for, and an invalidation of everything leaves no entry bound to the old generation.
        TimeSpan lifetime = Lifetime;
        Entry<IReadOnlySet<string>>?[] entries = new Entry<IReadOnlySet<string>>?[roleIds.Count];
        lock (_registration)
        {
            List<string>? missing = null;
            for (int i = 0; i < roleIds.Count; i++)
            {
                if (_cache.TryGetValue(RoleKey(roleIds[i]), out Entry<IReadOnlySet<string>>? cached) && cached is not null)
                {
                    entries[i] = cached;
                }
                else
                {
                    (missing ??= []).Add(roleIds[i]);
                }
            }

            if (missing is not null)
            {
                Lazy<Task<IReadOnlyDictionary<string, IReadOnlySet<string>>>> batch = new(
                    () => load(missing, CancellationToken.None),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                for (int i = 0; i < roleIds.Count; i++)
                {
                    if (entries[i] is null)
                    {
                        string roleId = roleIds[i];
                        entries[i] = GetOrAddLoad(RoleKey(roleId), async () => Pick(await batch.Value.ConfigureAwait(false), roleId), lifetime);
                    }
                }
            }
        }

        // Start every load that has not started before awaiting any, so that the load of the missing roles does not
        // wait for a pending load of another caller to complete.
        Task<IReadOnlySet<string>>[] pending = new Task<IReadOnlySet<string>>[roleIds.Count];
        DateTimeOffset expiresAt = DateTimeOffset.MaxValue;
        for (int i = 0; i < roleIds.Count; i++)
        {
            pending[i] = entries[i]!.Load.Value;
            expiresAt = entries[i]!.ExpiresAt < expiresAt ? entries[i]!.ExpiresAt : expiresAt;
        }

        for (int i = 0; i < roleIds.Count; i++)
        {
            permissions[roleIds[i]] = await pending[i].WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return new CachedValue<IReadOnlyDictionary<string, IReadOnlySet<string>>>(permissions, expiresAt);
    }

    public ValueTask InvalidateUserAsync(string userId, CancellationToken cancellationToken)
    {
        _cache.Remove(UserKey(userId));

        return ValueTask.CompletedTask;
    }

    public ValueTask InvalidateRoleAsync(string roleId, CancellationToken cancellationToken)
    {
        _cache.Remove(RoleKey(roleId));

        return ValueTask.CompletedTask;
    }

    public ValueTask InvalidateAllAsync(CancellationToken cancellationToken)
    {
        // Entries are bound to the current generation; cancelling it expires all of them at once.
        CancellationTokenSource previous = Interlocked.Exchange(ref _generation, new CancellationTokenSource());
        previous.Cancel();

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _cache.Dispose();
        _generation.Dispose();
    }

    // How long entries live; the duration is positive, caching is bypassed otherwise, and capped so that it can be added
    // to the current time.
    private TimeSpan Lifetime
    {
        get
        {
            TimeSpan duration = _options.Value.CacheDuration;

            return duration > MaxLifetime ? MaxLifetime : duration;
        }
    }

    private static TimeSpan MaxLifetime => TimeSpan.FromDays(365 * 100);

    /// <summary>
    ///   Gets the entry of a key, creating it when there is none. Runs under <see cref="_registration"/>.
    /// </summary>
    private Entry<T> GetOrAddLoad<T>(string key, Func<Task<T>> load, TimeSpan lifetime)
        where T : class
    {
        if (_cache.TryGetValue(key, out Entry<T>? cached) && cached is not null)
        {
            return cached;
        }

        // The pending load is cached before it starts: invalidating the key while it runs removes this entry, so the
        // result reaches the callers waiting for it but is not kept.
        Entry<T> entry = null!;
        Lazy<Task<T>> pending = new(() => LoadAsync(key, entry, load), LazyThreadSafetyMode.ExecutionAndPublication);
        entry = new Entry<T>(pending, DateTimeOffset.UtcNow + lifetime);

        MemoryCacheEntryOptions entryOptions = new() { AbsoluteExpirationRelativeToNow = lifetime };
        entryOptions.ExpirationTokens.Add(new CancellationChangeToken(Volatile.Read(ref _generation).Token));
        _cache.Set(key, entry, entryOptions);

        return entry;
    }

    private async Task<T> LoadAsync<T>(string key, Entry<T> entry, Func<Task<T>> load)
        where T : class
    {
        try
        {
            return await load().ConfigureAwait(false);
        }
        catch
        {
            // Failures are not cached: the next request retries the load.
            if (_cache.TryGetValue(key, out Entry<T>? current) && ReferenceEquals(current, entry))
            {
                _cache.Remove(key);
            }

            throw;
        }
    }

    private static IReadOnlySet<string> Pick(IReadOnlyDictionary<string, IReadOnlySet<string>> loaded, string roleId) =>
        loaded.TryGetValue(roleId, out IReadOnlySet<string>? value)
            ? value
            : throw new InvalidOperationException($"The load returned no entry for '{roleId}'.");

    private static string UserKey(string userId) => "user:" + userId;

    private static string RoleKey(string roleId) => "role:" + roleId;

    /// <summary>
    ///   A cached load and when its entry expires.
    /// </summary>
    private sealed class Entry<T>(Lazy<Task<T>> load, DateTimeOffset expiresAt)
        where T : class
    {
        public Lazy<Task<T>> Load { get; } = load;

        public DateTimeOffset ExpiresAt { get; } = expiresAt;
    }
}
