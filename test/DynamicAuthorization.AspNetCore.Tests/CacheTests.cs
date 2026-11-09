using System.Collections.Concurrent;
using System.Security.Claims;
using DynamicAuthorization.AspNetCore.Management;
using DynamicAuthorization.AspNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore.Tests;

/// <summary>
///   The cache store a test runs against: the default in memory, or the one on HybridCache.
/// </summary>
public enum CacheKind
{
    Memory,
    Hybrid,
}

/// <summary>
///   Consistency and cost of the cache of user roles and role permissions. The contract every store provides: a
///   change is never hidden by a load that started before it, concurrent misses share one load that outlives the
///   request that started it, failed loads are not kept, uncommitted data stays out of the cache, and a request
///   resolves roles and permissions once however many checks it makes. The in-memory store also loads the permissions
///   of every role with one store call; HybridCache reads one key at a time.
/// </summary>
public sealed class CacheTests
{
    private static readonly ClaimsPrincipal s_user = Principal("user");

    [Theory]
    [InlineData(CacheKind.Memory)]
    [InlineData(CacheKind.Hybrid)]
    public async Task Invalidating_a_role_during_a_load_discards_the_loaded_permissions(CacheKind kind)
    {
        StoreGate gate = new() { Gated = true };
        await using ServiceProvider services = BuildServices(gate, kind: kind);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IPermissionChecker checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();

        Task<bool> pending = checker.HasPermissionAsync(s_user, "orders.delete");
        Assert.Equal(1, gate.Calls);
        gate.Granted = false;
        await services.GetRequiredService<IDynamicAuthorizationCache>().InvalidateRoleAsync("role0");
        gate.Release();

        // The check that started before the change still sees the old grant; the next one reloads.
        Assert.True(await pending);
        Assert.False(await checker.HasPermissionAsync(s_user, "orders.delete"));
        Assert.Equal(2, gate.Calls);
    }

    [Theory]
    [InlineData(CacheKind.Memory)]
    [InlineData(CacheKind.Hybrid)]
    public async Task Concurrent_misses_share_one_load(CacheKind kind)
    {
        StoreGate gate = new() { Gated = true };
        await using ServiceProvider services = BuildServices(gate, kind: kind);

        Task<bool>[] checks = [.. Enumerable.Range(0, 32).Select(_ => CheckAsync(services))];
        Assert.Equal(1, gate.Calls);
        gate.Release();

        Assert.All(await Task.WhenAll(checks), Assert.True);
        Assert.Equal(1, gate.Calls);
    }

    [Theory]
    [InlineData(CacheKind.Memory)]
    [InlineData(CacheKind.Hybrid)]
    public async Task Failed_loads_are_not_cached(CacheKind kind)
    {
        StoreGate gate = new() { Fail = true };
        await using ServiceProvider services = BuildServices(gate, kind: kind);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CheckAsync(services));
        gate.Fail = false;

        Assert.True(await CheckAsync(services));
        Assert.Equal(2, gate.Calls);
    }

    [Theory]
    [InlineData(CacheKind.Memory)]
    [InlineData(CacheKind.Hybrid)]
    public async Task Cancelling_one_caller_does_not_fail_the_shared_load(CacheKind kind)
    {
        StoreGate gate = new() { Gated = true };
        await using ServiceProvider services = BuildServices(gate, kind: kind);
        using CancellationTokenSource cancellation = new();
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        Task<bool> cancelled = scope.ServiceProvider.GetRequiredService<IPermissionChecker>().HasPermissionAsync(s_user, "orders.delete", cancellation.Token);
        Task<bool> other = CheckAsync(services);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        gate.Release();

        Assert.True(await other);
        Assert.Equal(1, gate.Calls);
    }

    /// <summary>
    ///   A shared load must not borrow the scoped services of the request that started it: that request can end, and
    ///   dispose them, while other requests still wait for the load.
    /// </summary>
    [Theory]
    [InlineData(CacheKind.Memory)]
    [InlineData(CacheKind.Hybrid)]
    public async Task Shared_loads_survive_the_scope_of_the_request_that_started_them(CacheKind kind)
    {
        StoreGate gate = new() { Gated = true };
        await using ServiceProvider services = BuildServices(gate, kind: kind);
        AsyncServiceScope first = services.CreateAsyncScope();

        Task<bool> started = first.ServiceProvider.GetRequiredService<IPermissionChecker>().HasPermissionAsync(s_user, "orders.delete");
        Task<bool> waiting = CheckAsync(services);
        Assert.Equal(1, gate.Calls);
        await first.DisposeAsync();
        gate.Release();

        Assert.True(await started);
        Assert.True(await waiting);
        Assert.Equal(1, gate.Calls);
    }

    /// <summary>
    ///   Request authorization for a user with many roles costs one store call on a cold cache: the permissions of
    ///   every role are loaded together instead of one role at a time.
    /// </summary>
    [Fact]
    public async Task A_cold_check_loads_the_permissions_of_every_role_with_one_store_call()
    {
        StoreGate gate = new();
        await using ServiceProvider services = BuildServices(gate, roleCount: 20);
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        AuthorizationResult result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(s_user, null, [new DynamicPermissionRequirement("orders.delete")]);

        Assert.True(result.Succeeded);
        Assert.Equal(1, gate.Calls);
        Assert.Equal(20, gate.RolesLoaded);
    }

    /// <summary>
    ///   Stores that do not implement the batch read still work: the default implementation reads one role at a time.
    /// </summary>
    [Fact]
    public async Task Stores_without_a_batch_read_are_loaded_one_role_at_a_time()
    {
        StoreGate gate = new();
        await using ServiceProvider services = BuildServices(gate, roleCount: 3, batchStore: false);

        Assert.True(await CheckAsync(services));
        Assert.Equal(3, gate.Calls);
        Assert.Equal(3, gate.RolesLoaded);
    }

    /// <summary>
    ///   A page with many secure-content tags makes many checks in one request; the request resolves the user's roles
    ///   and the permissions of each role once, and a check for the same user never costs more than the first. The
    ///   in-memory store loads the three roles with one call; HybridCache loads them one at a time.
    /// </summary>
    [Theory]
    [InlineData(CacheKind.Memory, 1)]
    [InlineData(CacheKind.Hybrid, 3)]
    public async Task A_request_resolves_roles_and_permissions_once_however_many_checks_it_makes(CacheKind kind, int storeCalls)
    {
        StoreGate gate = new();
        FixedRolesProvider roles = new(3);
        await using ServiceProvider services = BuildServices(gate, roles, kind: kind);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IPermissionChecker checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();

        List<bool> results = [];
        for (int i = 0; i < 10; i++)
        {
            results.Add(await checker.HasPermissionAsync(s_user, "orders.delete"));
            results.Add(await checker.HasPermissionAsync(s_user, "orders.read")); // implied by orders.delete
            results.Add((await checker.EvaluateAsync(s_user, "invoices.read")).IsGranted);
        }

        Assert.Equal([.. Enumerable.Repeat<bool[]>([true, true, false], 10).SelectMany(r => r)], results);
        Assert.Equal(1, roles.Calls);
        Assert.Equal(storeCalls, gate.Calls);
        Assert.Equal(3, gate.RolesLoaded);
    }

    /// <summary>
    ///   Disabling the cache disables what a scope keeps as well, whichever store is registered: every check reads the
    ///   roles and, with one call, the permissions of every role, so a scope that lives long sees changes made
    ///   elsewhere at once.
    /// </summary>
    [Theory]
    [InlineData(CacheKind.Memory)]
    [InlineData(CacheKind.Hybrid)]
    public async Task Disabling_the_cache_reads_the_store_on_every_check(CacheKind kind)
    {
        StoreGate gate = new();
        FixedRolesProvider roles = new(3);
        await using ServiceProvider services = BuildServices(gate, roles, o => o.CacheDuration = TimeSpan.Zero, kind: kind);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IPermissionChecker checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();

        Assert.True(await checker.HasPermissionAsync(s_user, "orders.delete"));
        gate.Granted = false;
        Assert.False(await checker.HasPermissionAsync(s_user, "orders.delete"));

        Assert.Equal(2, roles.Calls);
        Assert.Equal(2, gate.Calls);
        Assert.Equal(6, gate.RolesLoaded);
    }

    /// <summary>
    ///   What a scope keeps expires with the cache duration, so a scope that outlives a request, for example one held
    ///   by a background service, sees a change made on another instance once the cache would.
    /// </summary>
    [Theory]
    [InlineData(CacheKind.Memory)]
    [InlineData(CacheKind.Hybrid)]
    public async Task What_a_scope_keeps_expires_with_the_cache_duration(CacheKind kind)
    {
        StoreGate gate = new();
        await using ServiceProvider services = BuildServices(gate, new FixedRolesProvider(1), o => o.CacheDuration = TimeSpan.FromMilliseconds(50), kind: kind);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IPermissionChecker checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();

        Assert.True(await checker.HasPermissionAsync(s_user, "orders.delete"));
        gate.Granted = false; // a change made elsewhere: nothing invalidates the local cache
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        Assert.False(await checker.HasPermissionAsync(s_user, "orders.delete"));
        Assert.Equal(2, gate.Calls);
    }

    /// <summary>
    ///   What a request resolved is dropped when the cache is invalidated, so a change made during the request is seen
    ///   by the checks that follow it.
    /// </summary>
    [Theory]
    [InlineData(CacheKind.Memory)]
    [InlineData(CacheKind.Hybrid)]
    public async Task A_change_during_a_request_is_seen_by_its_later_checks(CacheKind kind)
    {
        StoreGate gate = new();
        await using ServiceProvider services = BuildServices(gate, kind: kind);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IPermissionChecker checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();

        Assert.True(await checker.HasPermissionAsync(s_user, "orders.delete"));
        gate.Granted = false;
        await services.GetRequiredService<IDynamicAuthorizationCache>().InvalidateRoleAsync("role0");

        Assert.False(await checker.HasPermissionAsync(s_user, "orders.delete"));
        Assert.Equal(2, gate.Calls);
    }

    /// <summary>
    ///   An invalidation that lands while the shared load of several roles is starting still discards the result for
    ///   the invalidated role, or for every role: all entries are registered before the load starts, so there is none
    ///   the invalidation can miss.
    /// </summary>
    [Theory]
    [InlineData("role2", 4)]
    [InlineData(null, 6)]
    public async Task Invalidating_while_a_batch_load_starts_discards_the_invalidated_permissions(string? roleId, int rolesLoaded)
    {
        StoreGate gate = new();
        await using ServiceProvider services = BuildServices(gate, roleCount: 3);
        IDynamicAuthorizationCache cache = services.GetRequiredService<IDynamicAuthorizationCache>();
        gate.OnLoad = () => roleId is null ? cache.InvalidateAllAsync() : cache.InvalidateRoleAsync(roleId);

        Assert.True(await CheckAsync(services));
        gate.OnLoad = null;
        Assert.True(await CheckAsync(services));

        // The second check reloads the invalidated roles only.
        Assert.Equal(2, gate.Calls);
        Assert.Equal(rolesLoaded, gate.RolesLoaded);
    }

    /// <summary>
    ///   A check reuses the pending loads of another check for the roles they share and loads only the rest.
    /// </summary>
    [Fact]
    public async Task Checks_with_overlapping_roles_share_the_pending_loads()
    {
        StoreGate gate = new() { Gated = true };
        UserRolesProvider roles = new(new() { ["alice"] = ["role0", "role1", "role2"], ["bob"] = ["role1", "role2", "role3"] });
        await using ServiceProvider services = BuildServices(gate, roles);

        Task<bool> alice = CheckAsync(services, Principal("alice"));
        Task<bool> bob = CheckAsync(services, Principal("bob"));
        Assert.Equal(2, gate.Calls);
        Assert.Equal(4, gate.RolesLoaded);
        gate.Release();

        Assert.True(await alice);
        Assert.True(await bob);
    }

    /// <summary>
    ///   A cache store registered by the application is the one permission checks read through and invalidations
    ///   reach, and it receives loads it can call.
    /// </summary>
    [Fact]
    public async Task Custom_cache_stores_are_used_by_permission_checks()
    {
        StoreGate gate = new();
        RecordingCacheStore recording = new();
        ServiceCollection collection = new();
        collection.AddLogging();
        collection.AddSingleton<IDynamicAuthorizationCacheStore>(recording);
        collection.AddDynamicAuthorization();
        collection.AddSingleton(gate);
        collection.AddSingleton<IUserRoleProvider>(new FixedRolesProvider(1));
        collection.AddScoped<IPermissionStore, GatedPermissionStore>();
        await using ServiceProvider services = collection.BuildServiceProvider();

        Assert.True(await CheckAsync(services));
        await services.GetRequiredService<IDynamicAuthorizationCache>().InvalidateRoleAsync("role0");
        await services.GetRequiredService<IDynamicAuthorizationCache>().InvalidateUserAsync("user");

        Assert.Equal(["roles:user", "permissions:role0", "invalidate-role:role0", "invalidate-user:user"], recording.Calls);
        Assert.Equal(1, gate.Calls);
    }

    /// <summary>
    ///   Two instances with HybridCache on one distributed cache share what either of them loads, and an invalidation
    ///   on one of them, of a role or of everything, reaches the other once its local copies expire: it reloads instead
    ///   of serving the stale entry.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Instances_sharing_a_distributed_cache_share_entries_and_invalidations(bool everything)
    {
        StoreGate gate = new();
        RecordingDistributedCache distributedCache = new();
        TimeSpan local = TimeSpan.FromMilliseconds(200);
        await using ServiceProvider first = BuildServices(gate, new FixedRolesProvider(1), kind: CacheKind.Hybrid, distributedCache: distributedCache, localCacheDuration: local);
        await using ServiceProvider second = BuildServices(gate, new FixedRolesProvider(1), kind: CacheKind.Hybrid, distributedCache: distributedCache, localCacheDuration: local);

        Assert.True(await CheckAsync(first));
        await distributedCache.WaitForAsync("dynamic-authorization:user:user", "dynamic-authorization:role:role0");
        Assert.True(await CheckAsync(second)); // read from the shared cache, not loaded again
        Assert.Equal(1, gate.Calls);

        gate.Granted = false;
        IDynamicAuthorizationCache cache = second.GetRequiredService<IDynamicAuthorizationCache>();
        await (everything ? cache.InvalidateAllAsync() : cache.InvalidateRoleAsync("role0"));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (await CheckAsync(first))
        {
            await Task.Delay(50, timeout.Token);
        }

        Assert.Equal(2, gate.Calls);
    }

    /// <summary>
    ///   A load running on one instance while another instance invalidates its key must not republish the old data to
    ///   the shared cache: the caller waiting for the load is served, the next reader loads again.
    /// </summary>
    [Fact]
    public async Task Instances_do_not_keep_a_load_that_another_instance_invalidated_meanwhile()
    {
        StoreGate loadingGate = new() { Gated = true };
        RecordingDistributedCache distributedCache = new();
        TimeSpan local = TimeSpan.FromMilliseconds(200);
        await using ServiceProvider loading = BuildServices(loadingGate, new FixedRolesProvider(1), kind: CacheKind.Hybrid, distributedCache: distributedCache, localCacheDuration: local);
        await using ServiceProvider invalidating = BuildServices(new StoreGate(), new FixedRolesProvider(1), kind: CacheKind.Hybrid, distributedCache: distributedCache, localCacheDuration: local);

        Task<bool> pending = CheckAsync(loading);
        await loadingGate.Started;
        loadingGate.Granted = false;
        await invalidating.GetRequiredService<IDynamicAuthorizationCache>().InvalidateRoleAsync("role0");
        loadingGate.Release();

        Assert.True(await pending); // the caller whose load it was still sees what it loaded
        Assert.False(await CheckAsync(loading));
        Assert.Equal(2, loadingGate.Calls);
    }

    /// <summary>
    ///   A value read from an entry is reported to expire with the local copy it was read from, not a full local
    ///   lifetime after every read, so a scope that keeps it never outlives the copy.
    /// </summary>
    [Fact]
    public async Task Hybrid_values_expire_with_the_local_copy_they_were_read_from()
    {
        StoreGate gate = new();
        TimeSpan local = TimeSpan.FromMilliseconds(500);
        await using ServiceProvider services = BuildServices(gate, new FixedRolesProvider(1), kind: CacheKind.Hybrid, localCacheDuration: local);
        IDynamicAuthorizationCacheStore store = services.GetRequiredService<IDynamicAuthorizationCacheStore>();
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, IReadOnlySet<string>>>> load =
            async (ids, ct) => ids.ToDictionary(id => id, _ => (IReadOnlySet<string>)new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.Ordinal);

        DateTimeOffset before = DateTimeOffset.UtcNow;
        CachedValue<IReadOnlyDictionary<string, IReadOnlySet<string>>> first = await store.GetRolePermissionsAsync(["role0"], load, CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        CachedValue<IReadOnlyDictionary<string, IReadOnlySet<string>>> second = await store.GetRolePermissionsAsync(["role0"], load, CancellationToken.None);

        Assert.Equal(first.ExpiresAt, second.ExpiresAt);
        Assert.InRange(first.ExpiresAt, before + local, DateTimeOffset.UtcNow + local);
    }

    /// <summary>
    ///   Loads read committed data: a check inside an open transaction neither sees nor caches its uncommitted changes,
    ///   so rolling the transaction back leaves nothing stale behind.
    /// </summary>
    [Fact]
    public async Task Permissions_changed_inside_an_open_transaction_are_not_cached()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.FileDatabase = true);
        string roleId = await app.CreateRoleAsync("Editors");
        TestUser user = await app.CreateUserAsync("alice", "Editors");
        ClaimsPrincipal principal = await app.CreatePrincipalAsync(user);

        bool insideTransaction = await app.WithScopeAsync(async s =>
        {
            await using IDbContextTransaction transaction = await s.GetRequiredService<TestDbContext>().Database.BeginTransactionAsync();
            ManagementResult result = await s.GetRequiredService<IRoleManagementService>().UpdateRoleAsync(roleId, "Editors", ["orders.delete"]);
            Assert.True(result.Succeeded, string.Join(" ", result.Errors));
            bool granted = await s.GetRequiredService<IPermissionChecker>().HasPermissionAsync(principal, "orders.delete");
            await transaction.RollbackAsync();
            return granted;
        });
        bool afterRollback = await CheckAsync(app, principal);
        await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().UpdateRoleAsync(roleId, "Editors", ["orders.delete"]));

        Assert.False(insideTransaction);
        Assert.False(afterRollback);
        Assert.True(await CheckAsync(app, principal));
    }

    [Fact]
    public async Task Role_memberships_changed_inside_an_open_transaction_are_not_cached()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.FileDatabase = true);
        string roleId = await app.CreateRoleAsync("Editors", "orders.delete");
        TestUser user = await app.CreateUserAsync("alice");
        ClaimsPrincipal principal = await app.CreatePrincipalAsync(user);

        bool insideTransaction = await app.WithScopeAsync(async s =>
        {
            await using IDbContextTransaction transaction = await s.GetRequiredService<TestDbContext>().Database.BeginTransactionAsync();
            ManagementResult result = await s.GetRequiredService<IUserManagementService>().SetUserRolesAsync(user.Id, [roleId]);
            Assert.True(result.Succeeded, string.Join(" ", result.Errors));
            bool granted = await s.GetRequiredService<IPermissionChecker>().HasPermissionAsync(principal, "orders.delete");
            await transaction.RollbackAsync();
            return granted;
        });
        bool afterRollback = await CheckAsync(app, principal);
        await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().SetUserRolesAsync(user.Id, [roleId]));

        Assert.False(insideTransaction);
        Assert.False(afterRollback);
        Assert.True(await CheckAsync(app, principal));
    }

    private static Task<bool> CheckAsync(TestApp app, ClaimsPrincipal principal) =>
        app.WithScopeAsync(s => s.GetRequiredService<IPermissionChecker>().HasPermissionAsync(principal, "orders.delete"));

    /// <summary>
    ///   Checks the permission in a scope of its own, like a request would.
    /// </summary>
    private static async Task<bool> CheckAsync(ServiceProvider services, ClaimsPrincipal? principal = null)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IPermissionChecker>().HasPermissionAsync(principal ?? s_user, "orders.delete");
    }

    private static ClaimsPrincipal Principal(string userId) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"));


    private static ServiceProvider BuildServices(StoreGate gate, int roleCount = 1, bool batchStore = true, CacheKind kind = CacheKind.Memory) =>
        BuildServices(gate, new FixedRolesProvider(roleCount), batchStore: batchStore, kind: kind);

    private static ServiceProvider BuildServices(
        StoreGate gate,
        IUserRoleProvider roles,
        Action<DynamicAuthorizationOptions>? configure = null,
        bool batchStore = true,
        CacheKind kind = CacheKind.Memory,
        IDistributedCache? distributedCache = null,
        TimeSpan? localCacheDuration = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        IDynamicAuthorizationBuilder builder = services.AddDynamicAuthorization(configure);
        if (kind == CacheKind.Hybrid)
        {
            if (distributedCache is not null)
            {
                services.AddSingleton(distributedCache);
            }

            builder.UseHybridCache(o => o.LocalCacheDuration = localCacheDuration ?? o.LocalCacheDuration);
        }

        services.AddSingleton(gate);
        services.AddSingleton<IUserRoleProvider>(roles);
        if (batchStore)
        {
            services.AddScoped<IPermissionStore, GatedPermissionStore>();
        }
        else
        {
            services.AddScoped<IPermissionStore, SingleRolePermissionStore>();
        }

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///   Puts every user in the roles <c>role0</c> to <c>role{count - 1}</c>, and counts how often it is asked.
    /// </summary>
    private sealed class FixedRolesProvider(int count) : IUserRoleProvider
    {
        private readonly IReadOnlyList<RoleReference> _roles = [.. Enumerable.Range(0, count).Select(i => new RoleReference($"role{i}", $"Role {i}"))];
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string? GetUserId(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier);

        public Task<IReadOnlyList<RoleReference>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return Task.FromResult(_roles);
        }
    }

    /// <summary>
    ///   Puts each user in the roles configured for them.
    /// </summary>
    private sealed class UserRolesProvider(Dictionary<string, string[]> roles) : IUserRoleProvider
    {
        public string? GetUserId(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier);

        public Task<IReadOnlyList<RoleReference>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RoleReference>>([.. roles[userId].Select(r => new RoleReference(r, r))]);
    }

    /// <summary>
    ///   State shared by the scoped stores of one service provider: how often any of them was called, how many roles
    ///   those calls loaded, and whether loads are held back, fail, or grant the permission.
    /// </summary>
    private sealed class StoreGate
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        private int _rolesLoaded;

        public bool Gated { get; init; }

        /// <summary>
        ///   Gets a task that completes when the first load has started and, when gated, waits at the gate.
        /// </summary>
        public Task Started => _started.Task;

        public bool Fail { get; set; }

        public bool Granted { get; set; } = true;

        /// <summary>
        ///   Gets or sets what happens as a load starts, before it reads anything: a change racing with the load.
        /// </summary>
        public Func<ValueTask>? OnLoad { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public int RolesLoaded => Volatile.Read(ref _rolesLoaded);

        public void Release() => _gate.TrySetResult();

        /// <summary>
        ///   Counts a store call and runs its load: fails, waits for the gate, and grants the permission as configured.
        /// </summary>
        public async Task<IReadOnlySet<string>> LoadAsync(int roles)
        {
            Interlocked.Increment(ref _calls);
            Interlocked.Add(ref _rolesLoaded, roles);
            if (OnLoad is not null)
            {
                await OnLoad();
            }

            if (Fail)
            {
                throw new InvalidOperationException("The store is unavailable.");
            }

            // What a load returns is decided when it starts, like a query that runs before a change commits.
            bool granted = Granted;
            _started.TrySetResult();
            if (Gated)
            {
                await _gate.Task;
            }

            return new HashSet<string>(granted ? ["orders.delete"] : [], StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    ///   A scoped store that is disposed with its scope, like the Entity Framework store, and fails afterwards like a
    ///   disposed DbContext would. It loads the permissions of several roles with one call.
    /// </summary>
    private sealed class GatedPermissionStore(StoreGate gate) : IPermissionStore, IDisposable
    {
        private bool _disposed;

        public void Dispose() => _disposed = true;

        public async Task<IReadOnlySet<string>> GetPermissionsAsync(string roleId, CancellationToken cancellationToken = default)
        {
            IReadOnlySet<string> permissions = await gate.LoadAsync(1);
            ObjectDisposedException.ThrowIf(_disposed, this);

            return permissions;
        }

        public async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetPermissionsAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default)
        {
            string[] ids = [.. roleIds];
            IReadOnlySet<string> permissions = await gate.LoadAsync(ids.Length);
            ObjectDisposedException.ThrowIf(_disposed, this);

            return ids.ToDictionary(id => id, _ => permissions, StringComparer.Ordinal);
        }

        public Task<PermissionChangeSet> SetPermissionsAsync(string roleId, IEnumerable<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    ///   A store that only implements the required members, so the batch read falls back to one call per role.
    /// </summary>
    private sealed class SingleRolePermissionStore(StoreGate gate) : IPermissionStore
    {
        public Task<IReadOnlySet<string>> GetPermissionsAsync(string roleId, CancellationToken cancellationToken = default) => gate.LoadAsync(1);

        public Task<PermissionChangeSet> SetPermissionsAsync(string roleId, IEnumerable<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    ///   An in-memory distributed cache that records its operations, standing in for the cache several instances
    ///   share. HybridCache writes to it after it has served the value, so tests wait for the entries to be there.
    /// </summary>
    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly MemoryDistributedCache _inner = new(Options.Create(new MemoryDistributedCacheOptions()));
        private readonly ConcurrentQueue<string> _operations = new();

        public async Task WaitForAsync(params string[] keys)
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            while (keys.Any(key => _inner.Get(key) is null))
            {
                if (timeout.IsCancellationRequested)
                {
                    Assert.Fail($"The distributed cache did not receive {string.Join(", ", keys)}. Operations: {string.Join(" | ", _operations)}");
                }

                await Task.Delay(10);
            }
        }

        public byte[]? Get(string key) => Record("get", key, _inner.Get(key));

        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Record("get", key, await _inner.GetAsync(key, token));

        public void Refresh(string key) => _inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);

        public void Remove(string key) => _inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            _operations.Enqueue("set " + key);
            _inner.Set(key, value, options);
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            _operations.Enqueue("set " + key);

            return _inner.SetAsync(key, value, options, token);
        }

        private byte[]? Record(string operation, string key, byte[]? result)
        {
            _operations.Enqueue($"{operation} {key} -> {(result is null ? "miss" : "hit")}");

            return result;
        }
    }

    /// <summary>
    ///   A cache store that keeps nothing and records what it is asked, to prove that a registered store is the one
    ///   permission checks go through.
    /// </summary>
    private sealed class RecordingCacheStore : IDynamicAuthorizationCacheStore
    {
        public List<string> Calls { get; } = [];

        public async ValueTask<CachedValue<IReadOnlyList<RoleReference>>> GetUserRolesAsync(string userId, Func<CancellationToken, Task<IReadOnlyList<RoleReference>>> load, CancellationToken cancellationToken)
        {
            Calls.Add("roles:" + userId);

            return new CachedValue<IReadOnlyList<RoleReference>>(await load(cancellationToken), DateTimeOffset.MaxValue);
        }

        public async ValueTask<CachedValue<IReadOnlyDictionary<string, IReadOnlySet<string>>>> GetRolePermissionsAsync(IReadOnlyList<string> roleIds, Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, IReadOnlySet<string>>>> load, CancellationToken cancellationToken)
        {
            Calls.Add("permissions:" + string.Join(",", roleIds));

            return new CachedValue<IReadOnlyDictionary<string, IReadOnlySet<string>>>(await load(roleIds, cancellationToken), DateTimeOffset.MaxValue);
        }

        public ValueTask InvalidateUserAsync(string userId, CancellationToken cancellationToken)
        {
            Calls.Add("invalidate-user:" + userId);

            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateRoleAsync(string roleId, CancellationToken cancellationToken)
        {
            Calls.Add("invalidate-role:" + roleId);

            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateAllAsync(CancellationToken cancellationToken)
        {
            Calls.Add("invalidate-all");

            return ValueTask.CompletedTask;
        }
    }
}
