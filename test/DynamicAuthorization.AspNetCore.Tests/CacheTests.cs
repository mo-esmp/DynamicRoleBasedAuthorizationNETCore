using System.Security.Claims;
using DynamicAuthorization.AspNetCore.Management;
using DynamicAuthorization.AspNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DynamicAuthorization.AspNetCore.Tests;

/// <summary>
///   Consistency and cost of the in-memory cache of user roles and role permissions: a change is never hidden by a
///   load that started before it, concurrent misses share one load, uncommitted data stays out of the cache, and
///   authorization loads no more roles than it needs.
/// </summary>
public sealed class CacheTests
{
    private static readonly ClaimsPrincipal s_user = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user")], "Test"));

    [Fact]
    public async Task Invalidating_a_role_during_a_load_discards_the_loaded_permissions()
    {
        GatedPermissionStore store = new() { Gated = true };
        await using ServiceProvider services = BuildServices(store);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IPermissionChecker checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();

        Task<bool> pending = checker.HasPermissionAsync(s_user, "orders.delete");
        Assert.Equal(1, store.Calls);
        store.Granted = false;
        services.GetRequiredService<IDynamicAuthorizationCache>().InvalidateRole("role0");
        store.Release();

        // The check that started before the change still sees the old grant; the next one reloads.
        Assert.True(await pending);
        Assert.False(await checker.HasPermissionAsync(s_user, "orders.delete"));
        Assert.Equal(2, store.Calls);
    }

    [Fact]
    public async Task Concurrent_misses_share_one_load()
    {
        GatedPermissionStore store = new() { Gated = true };
        await using ServiceProvider services = BuildServices(store);

        Task<bool>[] checks = [.. Enumerable.Range(0, 32).Select(_ => CheckAsync(services))];
        Assert.Equal(1, store.Calls);
        store.Release();

        Assert.All(await Task.WhenAll(checks), Assert.True);
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task Failed_loads_are_not_cached()
    {
        GatedPermissionStore store = new() { Fail = true };
        await using ServiceProvider services = BuildServices(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CheckAsync(services));
        store.Fail = false;

        Assert.True(await CheckAsync(services));
        Assert.Equal(2, store.Calls);
    }

    [Fact]
    public async Task Cancelling_one_caller_does_not_fail_the_shared_load()
    {
        GatedPermissionStore store = new() { Gated = true };
        await using ServiceProvider services = BuildServices(store);
        using CancellationTokenSource cancellation = new();
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        Task<bool> cancelled = scope.ServiceProvider.GetRequiredService<IPermissionChecker>().HasPermissionAsync(s_user, "orders.delete", cancellation.Token);
        Task<bool> other = CheckAsync(services);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        store.Release();

        Assert.True(await other);
        Assert.Equal(1, store.Calls);
    }

    /// <summary>
    ///   Request authorization only needs a decision, so it stops at the first granting role instead of loading the
    ///   permissions of every role.
    /// </summary>
    [Fact]
    public async Task Authorization_stops_at_the_first_granting_role()
    {
        GatedPermissionStore store = new();
        await using ServiceProvider services = BuildServices(store, roleCount: 20);
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        AuthorizationResult result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(s_user, null, [new DynamicPermissionRequirement("orders.delete")]);

        Assert.True(result.Succeeded);
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task Permissions_read_inside_a_rolled_back_transaction_are_not_cached()
    {
        await using TestApp app = await TestApp.StartAsync();
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

        Assert.True(insideTransaction);
        Assert.False(await app.WithScopeAsync(s => s.GetRequiredService<IPermissionChecker>().HasPermissionAsync(principal, "orders.delete")));
    }

    [Fact]
    public async Task Roles_read_inside_a_rolled_back_transaction_are_not_cached()
    {
        await using TestApp app = await TestApp.StartAsync();
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

        Assert.True(insideTransaction);
        Assert.False(await app.WithScopeAsync(s => s.GetRequiredService<IPermissionChecker>().HasPermissionAsync(principal, "orders.delete")));
    }

    /// <summary>
    ///   Checks the permission in a scope of its own, like a request would.
    /// </summary>
    private static async Task<bool> CheckAsync(ServiceProvider services)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IPermissionChecker>().HasPermissionAsync(s_user, "orders.delete");
    }

    private static ServiceProvider BuildServices(IPermissionStore store, int roleCount = 1)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDynamicAuthorization();
        services.AddSingleton(store);
        services.AddSingleton<IUserRoleProvider>(new FixedRolesProvider(roleCount));

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///   Puts every user in the roles <c>role0</c> to <c>role{count - 1}</c>.
    /// </summary>
    private sealed class FixedRolesProvider(int count) : IUserRoleProvider
    {
        private readonly IReadOnlyList<RoleReference> _roles = [.. Enumerable.Range(0, count).Select(i => new RoleReference($"role{i}", $"Role {i}"))];

        public string? GetUserId(ClaimsPrincipal principal) => principal.FindFirstValue(ClaimTypes.NameIdentifier);

        public Task<IReadOnlyList<RoleReference>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_roles);
    }

    /// <summary>
    ///   A store whose loads can be held back, fail, or change their answer between calls.
    /// </summary>
    private sealed class GatedPermissionStore : IPermissionStore
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public bool Gated { get; init; }

        public bool Fail { get; set; }

        public bool Granted { get; set; } = true;

        public int Calls => Volatile.Read(ref _calls);

        public void Release() => _gate.TrySetResult();

        public async Task<IReadOnlySet<string>> GetPermissionsAsync(string roleId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
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

        public Task<PermissionChangeSet> SetPermissionsAsync(string roleId, IEnumerable<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
