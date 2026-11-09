using System.Transactions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Caches user roles and role permissions in memory. Pending loads are cached too, so that concurrent requests for
///   the same key share one load, and an invalidation during a load removes the entry before the result is published.
///   The permissions of several roles that are not cached are loaded together. Loads run in a service scope of their
///   own, outside any ambient transaction: they never depend on the request that started them, which may end first,
///   and they read committed data only.
/// </summary>
internal sealed class DynamicAuthorizationCache : IDynamicAuthorizationCache, IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    // Entry creation is serialized so that concurrent misses, for one key or for a batch of keys, share a single load.
    // Only dictionary operations run under the lock; loads start after it is released.
    private readonly Lock _registration = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<DynamicAuthorizationOptions> _options;
    private CancellationTokenSource _generation = new();
    private long _version;

    public DynamicAuthorizationCache(IServiceScopeFactory scopeFactory, IOptions<DynamicAuthorizationOptions> options)
    {
        _scopeFactory = scopeFactory;
        _options = options;
    }

    /// <summary>
    ///   Gets a number that changes whenever an entry is invalidated. Callers that keep results for the duration of a
    ///   request compare it to know whether a change has been made since.
    /// </summary>
    public long Version => Volatile.Read(ref _version);

    /// <summary>
    ///   Gets the roles of a user, loading them with <paramref name="load"/> when they are not cached. The load
    ///   receives a service provider of its own.
    /// </summary>
    public Task<Cached<IReadOnlyList<RoleReference>>> GetUserRolesAsync(
        string userId,
        Func<IServiceProvider, CancellationToken, Task<IReadOnlyList<RoleReference>>> load,
        CancellationToken cancellationToken) =>
        GetOrCreateAsync(UserKey(userId), load, cancellationToken);

    /// <summary>
    ///   Gets the permissions of roles, in the order of <paramref name="roleIds"/>, loading those that are not cached
    ///   together with <paramref name="load"/>. The load receives a service provider of its own and the identifiers to
    ///   load, and returns the permissions of each of them. The result expires when the first of its entries does.
    /// </summary>
    public Task<Cached<IReadOnlyList<PermissionSet>>> GetRolePermissionsAsync(
        IReadOnlyList<string> roleIds,
        Func<IServiceProvider, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, PermissionSet>>> load,
        CancellationToken cancellationToken) =>
        GetOrCreateManyAsync(roleIds, RoleKey, load, cancellationToken);

    public void InvalidateUser(string userId)
    {
        _cache.Remove(UserKey(userId));
        Interlocked.Increment(ref _version);
    }

    public void InvalidateRole(string roleId)
    {
        _cache.Remove(RoleKey(roleId));
        Interlocked.Increment(ref _version);
    }

    public void InvalidateAll()
    {
        // Entries are bound to the current generation; cancelling it expires all of them at once.
        CancellationTokenSource previous = Interlocked.Exchange(ref _generation, new CancellationTokenSource());
        previous.Cancel();
        Interlocked.Increment(ref _version);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _generation.Dispose();
    }

    private async Task<Cached<T>> GetOrCreateAsync<T>(string key, Func<IServiceProvider, CancellationToken, Task<T>> load, CancellationToken cancellationToken)
        where T : class
    {
        TimeSpan duration = _options.Value.CacheDuration;
        if (duration <= TimeSpan.Zero)
        {
            return new Cached<T>(await ExecuteAsync(load, cancellationToken).ConfigureAwait(false), Environment.TickCount64);
        }

        Entry<T> entry;
        lock (_registration)
        {
            entry = GetOrAddLoad(key, () => ExecuteAsync(load, CancellationToken.None), duration);
        }

        // The load is shared with other callers and runs to completion even when this caller is cancelled.
        return new Cached<T>(await entry.Load.Value.WaitAsync(cancellationToken).ConfigureAwait(false), entry.ExpiresAt);
    }

    private async Task<Cached<IReadOnlyList<T>>> GetOrCreateManyAsync<T>(
        IReadOnlyList<string> ids,
        Func<string, string> toKey,
        Func<IServiceProvider, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, T>>> load,
        CancellationToken cancellationToken)
        where T : class
    {
        if (ids.Count == 0)
        {
            return new Cached<IReadOnlyList<T>>([], long.MaxValue);
        }

        TimeSpan duration = _options.Value.CacheDuration;
        if (duration <= TimeSpan.Zero)
        {
            IReadOnlyDictionary<string, T> loaded = await ExecuteAsync((services, ct) => load(services, ids, ct), cancellationToken).ConfigureAwait(false);

            return new Cached<IReadOnlyList<T>>([.. ids.Select(id => Pick(loaded, id))], Environment.TickCount64);
        }

        // Entries that exist, complete or pending, are used as they are. The others share one load of all of them,
        // from which each entry picks its own result. Finding and registering the entries is one step under the lock,
        // so that concurrent checks with overlapping roles share loads instead of duplicating them, and every entry is
        // in the cache before the load starts: an invalidation during the load removes the entry, whichever role it is
        // for, and an invalidation of everything leaves no entry bound to the old generation.
        Entry<T>?[] entries = new Entry<T>?[ids.Count];
        lock (_registration)
        {
            List<string>? missing = null;
            for (int i = 0; i < ids.Count; i++)
            {
                if (_cache.TryGetValue(toKey(ids[i]), out Entry<T>? cached) && cached is not null)
                {
                    entries[i] = cached;
                }
                else
                {
                    (missing ??= []).Add(ids[i]);
                }
            }

            if (missing is not null)
            {
                Lazy<Task<IReadOnlyDictionary<string, T>>> batch = new(
                    () => ExecuteAsync((services, ct) => load(services, missing, ct), CancellationToken.None),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                for (int i = 0; i < ids.Count; i++)
                {
                    if (entries[i] is null)
                    {
                        string id = ids[i];
                        entries[i] = GetOrAddLoad(toKey(id), async () => Pick(await batch.Value.ConfigureAwait(false), id), duration);
                    }
                }
            }
        }

        // Start every load that has not started before awaiting any, so that the load of the missing roles does not
        // wait for a pending load of another caller to complete.
        Task<T>[] pending = new Task<T>[ids.Count];
        long expiresAt = long.MaxValue;
        for (int i = 0; i < ids.Count; i++)
        {
            pending[i] = entries[i]!.Load.Value;
            expiresAt = Math.Min(expiresAt, entries[i]!.ExpiresAt);
        }

        T[] results = new T[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            results[i] = await pending[i].WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return new Cached<IReadOnlyList<T>>(results, expiresAt);
    }

    /// <summary>
    ///   Gets the entry of a key, creating it when there is none. Runs under <see cref="_registration"/>.
    /// </summary>
    private Entry<T> GetOrAddLoad<T>(string key, Func<Task<T>> load, TimeSpan duration)
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
        entry = new Entry<T>(pending, Environment.TickCount64 + (long)Math.Min(duration.TotalMilliseconds, long.MaxValue / 2));

        MemoryCacheEntryOptions entryOptions = new() { AbsoluteExpirationRelativeToNow = duration };
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

    /// <summary>
    ///   Runs a load with services of its own, so that it does not depend on the scope of the request that started it,
    ///   and outside any ambient transaction, so that it reads committed data only.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(Func<IServiceProvider, CancellationToken, Task<T>> load, CancellationToken cancellationToken)
    {
        using TransactionScope suppress = new(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

        return await load(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
    }

    private static T Pick<T>(IReadOnlyDictionary<string, T> loaded, string id) =>
        loaded.TryGetValue(id, out T? value) ? value : throw new InvalidOperationException($"The load returned no entry for '{id}'.");

    private static string UserKey(string userId) => "user:" + userId;

    private static string RoleKey(string roleId) => "role:" + roleId;

    /// <summary>
    ///   A cached load and when its entry expires, as <see cref="Environment.TickCount64"/>.
    /// </summary>
    private sealed class Entry<T>(Lazy<Task<T>> load, long expiresAt)
        where T : class
    {
        public Lazy<Task<T>> Load { get; } = load;

        public long ExpiresAt { get; } = expiresAt;
    }
}

/// <summary>
///   A value read from the cache and when the entry it was read from expires, as <see cref="Environment.TickCount64"/>,
///   so that a caller keeping the value does not keep it longer than the cache would.
/// </summary>
internal readonly record struct Cached<T>(T Value, long ExpiresAt);
