using System.Security.Claims;
using DynamicAuthorization.AspNetCore.Management;
using DynamicAuthorization.AspNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DynamicAuthorization.AspNetCore.Tests;

/// <summary>
///   Consistency and cost of the in-memory cache of user roles and role permissions: a change is never hidden by a
///   load that started before it, concurrent misses share one load that outlives the request that started it,
///   uncommitted data stays out of the cache, a request resolves roles and permissions once however many checks it
///   makes, and a cold check loads the permissions of every role with one store call.
/// </summary>
public sealed class CacheTests
{
    private static readonly ClaimsPrincipal s_user = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user")], "Test"));

    [Fact]
    public async Task Invalidating_a_role_during_a_load_discards_the_loaded_permissions()
    {
        StoreGate gate = new() { Gated = true };
        await using ServiceProvider services = BuildServices(gate);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IPermissionChecker checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();

        Task<bool> pending = checker.HasPermissionAsync(s_user, "orders.delete");
        Assert.Equal(1, gate.Calls);
        gate.Granted = false;
        services.GetRequiredService<IDynamicAuthorizationCache>().InvalidateRole("role0");
        gate.Release();

        // The check that started before the change still sees the old grant; the next one reloads.
        Assert.True(await pending);
        Assert.False(await checker.HasPermissionAsync(s_user, "orders.delete"));
        Assert.Equal(2, gate.Calls);
    }

    [Fact]
    public async Task Concurrent_misses_share_one_load()
    {
        StoreGate gate = new() { Gated = true };
        await using ServiceProvider services = BuildServices(gate);

        Task<bool>[] checks = [.. Enumerable.Range(0, 32).Select(_ => CheckAsync(services))];
        Assert.Equal(1, gate.Calls);
        gate.Release();

        Assert.All(await Task.WhenAll(checks), Assert.True);
        Assert.Equal(1, gate.Calls);
    }

    [Fact]
    public async Task Failed_loads_are_not_cached()
    {
        StoreGate gate = new() { Fail = true };
        await using ServiceProvider services = BuildServices(gate);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CheckAsync(services));
        gate.Fail = false;

        Assert.True(await CheckAsync(services));
        Assert.Equal(2, gate.Calls);
    }

    [Fact]
    public async Task Cancelling_one_caller_does_not_fail_the_shared_load()
    {
        StoreGate gate = new() { Gated = true };
        await using ServiceProvider services = BuildServices(gate);
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
    [Fact]
    public async Task Shared_loads_survive_the_scope_of_the_request_that_started_them()
    {
        StoreGate gate = new() { Gated = true };
        await using ServiceProvider services = BuildServices(gate);
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
    ///   and the permissions of each role once. With caching disabled, every lookup would otherwise be a store call.
    /// </summary>
    [Fact]
    public async Task A_request_resolves_roles_and_permissions_once_however_many_checks_it_makes()
    {
        StoreGate gate = new();
        FixedRolesProvider roles = new(3);
        await using ServiceProvider services = BuildServices(gate, roles, o => o.CacheDuration = TimeSpan.Zero);
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
        Assert.Equal(1, gate.Calls);
        Assert.Equal(3, gate.RolesLoaded);
    }

    /// <summary>
    ///   What a request resolved is dropped when the cache is invalidated, so a change made during the request is seen
    ///   by the checks that follow it.
    /// </summary>
    [Fact]
    public async Task A_change_during_a_request_is_seen_by_its_later_checks()
    {
        StoreGate gate = new();
        await using ServiceProvider services = BuildServices(gate);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IPermissionChecker checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();

        Assert.True(await checker.HasPermissionAsync(s_user, "orders.delete"));
        gate.Granted = false;
        services.GetRequiredService<IDynamicAuthorizationCache>().InvalidateRole("role0");

        Assert.False(await checker.HasPermissionAsync(s_user, "orders.delete"));
        Assert.Equal(2, gate.Calls);
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
    private static async Task<bool> CheckAsync(ServiceProvider services)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IPermissionChecker>().HasPermissionAsync(s_user, "orders.delete");
    }

    private static ServiceProvider BuildServices(StoreGate gate, int roleCount = 1, bool batchStore = true) =>
        BuildServices(gate, new FixedRolesProvider(roleCount), batchStore: batchStore);

    private static ServiceProvider BuildServices(
        StoreGate gate,
        FixedRolesProvider roles,
        Action<DynamicAuthorizationOptions>? configure = null,
        bool batchStore = true)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDynamicAuthorization(configure);
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
    ///   State shared by the scoped stores of one service provider: how often any of them was called, how many roles
    ///   those calls loaded, and whether loads are held back, fail, or grant the permission.
    /// </summary>
    private sealed class StoreGate
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        private int _rolesLoaded;

        public bool Gated { get; init; }

        public bool Fail { get; set; }

        public bool Granted { get; set; } = true;

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
            if (Fail)
            {
                throw new InvalidOperationException("The store is unavailable.");
            }

            bool granted = Granted;
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
}
