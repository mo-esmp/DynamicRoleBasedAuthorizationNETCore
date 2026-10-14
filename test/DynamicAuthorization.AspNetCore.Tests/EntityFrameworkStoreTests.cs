using DynamicAuthorization.AspNetCore.EntityFrameworkCore;
using DynamicAuthorization.AspNetCore.Management;
using DynamicAuthorization.AspNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DynamicAuthorization.AspNetCore.Tests;

public sealed class EntityFrameworkStoreTests
{
    [Fact]
    public async Task SetPermissions_returns_the_changes_and_ignores_case_and_duplicates()
    {
        await using TestApp app = await TestApp.StartAsync();
        string roleId = await app.CreateRoleAsync("Editors");

        PermissionChangeSet first = await app.WithScopeAsync(s => s.GetRequiredService<IPermissionStore>().SetPermissionsAsync(roleId, ["a", "B", "b", " ", "a "]));
        PermissionChangeSet second = await app.WithScopeAsync(s => s.GetRequiredService<IPermissionStore>().SetPermissionsAsync(roleId, ["A", "c"]));
        IReadOnlySet<string> stored = await app.WithScopeAsync(s => s.GetRequiredService<IPermissionStore>().GetPermissionsAsync(roleId));

        Assert.Equal(["a", "B"], first.Added);
        Assert.Empty(first.Removed);
        Assert.Equal(["c"], second.Added);
        Assert.Equal(["B"], second.Removed);
        Assert.Equal(2, stored.Count);
        Assert.Contains("A", stored);
        Assert.Contains("c", stored);
    }

    [Fact]
    public async Task Roles_can_be_created_found_updated_and_listed()
    {
        await using TestApp app = await TestApp.StartAsync();
        string roleId = await app.CreateRoleAsync("Editors", "mvc::Orders:Index", "mvc::Orders:Edit");
        await app.CreateUserAsync("alice", "Editors");

        ManagementResult updated = await app.WithScopeAsync(s =>
            s.GetRequiredService<IRoleManagementService>().UpdateRoleAsync(roleId, " Writers ", ["mvc::Orders:Edit", "page::/Secure/Index"]));
        RoleDetails? role = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().FindRoleAsync(roleId));
        IReadOnlyList<RoleSummary> roles = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().GetRolesAsync());

        Assert.True(updated.Succeeded);
        Assert.NotNull(role);
        Assert.Equal("Writers", role.Name);
        Assert.Equal(["mvc::Orders:Edit", "page::/Secure/Index"], role.Permissions.Order());
        RoleSummary summary = Assert.Single(roles);
        Assert.Equal(new RoleSummary(roleId, "Writers", 1, 2), summary);
    }

    [Fact]
    public async Task Creating_a_role_with_a_duplicate_name_fails_without_changes()
    {
        await using TestApp app = await TestApp.StartAsync();
        await app.CreateRoleAsync("Editors", "a");

        ManagementResult<string> result = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().CreateRoleAsync("editors", ["b"]));
        IReadOnlyList<RoleGrant> grants = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().GetGrantsAsync());

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
        RoleGrant grant = Assert.Single(grants);
        Assert.Equal("a", grant.Permission);
    }

    [Fact]
    public async Task Deleting_a_role_removes_its_permissions_and_memberships()
    {
        await using TestApp app = await TestApp.StartAsync();
        string roleId = await app.CreateRoleAsync("Editors", "a", "b");
        TestUser user = await app.CreateUserAsync("alice", "Editors");

        ManagementResult result = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().DeleteRoleAsync(roleId));
        IReadOnlyList<RoleGrant> grants = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().GetGrantsAsync());
        UserSummary? summary = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().FindUserAsync(user.Id));

        Assert.True(result.Succeeded);
        Assert.Empty(grants);
        Assert.NotNull(summary);
        Assert.Empty(summary.Roles);
    }

    [Fact]
    public async Task Super_admin_roles_cannot_be_renamed_or_deleted()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a => a.SuperAdminRoles.Add("Administrators"));
        string roleId = await app.CreateRoleAsync("Administrators");

        ManagementResult renamed = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().UpdateRoleAsync(roleId, "Admins", []));
        ManagementResult deleted = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().DeleteRoleAsync(roleId));

        Assert.False(renamed.Succeeded);
        Assert.False(deleted.Succeeded);
    }

    [Fact]
    public async Task Unknown_roles_are_reported_as_not_found()
    {
        await using TestApp app = await TestApp.StartAsync();

        RoleDetails? role = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().FindRoleAsync("missing"));
        ManagementResult deleted = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().DeleteRoleAsync("missing"));

        Assert.Null(role);
        Assert.False(deleted.Succeeded);
    }

    [Fact]
    public async Task User_roles_can_be_replaced()
    {
        await using TestApp app = await TestApp.StartAsync();
        string editors = await app.CreateRoleAsync("Editors");
        string writers = await app.CreateRoleAsync("Writers");
        TestUser user = await app.CreateUserAsync("alice", "Editors");

        ManagementResult result = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().SetUserRolesAsync(user.Id, [writers]));
        UserSummary? summary = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().FindUserAsync(user.Id));

        Assert.True(result.Succeeded);
        RoleReference role = Assert.Single(summary!.Roles);
        Assert.Equal(new RoleReference(writers, "Writers"), role);
        Assert.NotEqual(editors, role.Id);
    }

    [Fact]
    public async Task Setting_an_unknown_role_fails_without_changes()
    {
        await using TestApp app = await TestApp.StartAsync();
        string editors = await app.CreateRoleAsync("Editors");
        TestUser user = await app.CreateUserAsync("alice", "Editors");

        ManagementResult result = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().SetUserRolesAsync(user.Id, ["missing"]));
        UserSummary? summary = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().FindUserAsync(user.Id));

        Assert.False(result.Succeeded);
        Assert.Equal(editors, Assert.Single(summary!.Roles).Id);
    }

    [Fact]
    public async Task The_last_super_admin_cannot_be_removed_from_the_super_admin_role()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a =>
        {
            a.SuperAdminUsers.Clear();
            a.SuperAdminRoles.Add("Administrators");
        });
        TestUser user = await app.CreateUserAsync("alice", "Administrators");

        ManagementResult result = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().SetUserRolesAsync(user.Id, []));

        Assert.False(result.Succeeded);
        Assert.Contains("last member", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Users_can_be_searched_and_paged()
    {
        await using TestApp app = await TestApp.StartAsync();
        for (int i = 1; i <= 30; i++)
            await app.CreateUserAsync($"user{i:00}");
        await app.CreateUserAsync("alice", "Editors");

        PagedResult<UserSummary> secondPage = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().GetUsersAsync(new UserQuery(Page: 2, PageSize: 25)));
        PagedResult<UserSummary> search = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().GetUsersAsync(new UserQuery("ALI")));
        UserSummary? byEmail = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().FindUserByNameAsync("Alice@Example.com"));

        Assert.Equal(31, secondPage.TotalCount);
        Assert.Equal(2, secondPage.PageCount);
        Assert.Equal(6, secondPage.Items.Count);
        UserSummary alice = Assert.Single(search.Items);
        Assert.Equal("Editors", Assert.Single(alice.Roles).Name);
        Assert.Equal(alice.Id, byEmail?.Id);
    }

    [Fact]
    public async Task Model_maps_role_permissions_to_their_own_table_with_a_cascading_foreign_key()
    {
        await using TestApp app = await TestApp.StartAsync();

        IEntityType entity = await app.WithScopeAsync(s =>
            Task.FromResult(s.GetRequiredService<TestDbContext>().Model.FindEntityType(typeof(RolePermission<string>))!));

        Assert.Equal("AspNetRolePermissions", entity.GetTableName());
        IForeignKey foreignKey = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(typeof(IdentityRole), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.Equal(DynamicAuthorizationModelOptions.MaxPermissionLength, entity.FindProperty(nameof(RolePermission<string>.Permission))!.GetMaxLength());
    }

    [Fact]
    public async Task Store_explains_how_to_fix_a_model_without_dynamic_authorization()
    {
        await using SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();
        await using ServiceProvider services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<ContextWithoutDynamicAuthorization>(o => o.UseSqlite(connection))
            .AddDynamicAuthorization().AddEntityFrameworkStore<ContextWithoutDynamicAuthorization>().Services
            .BuildServiceProvider();
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IPermissionStore>().GetPermissionsAsync("1"));

        Assert.Contains("EnableDynamicAuthorization", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_rejects_contexts_without_identity_roles() =>
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddDynamicAuthorization().AddEntityFrameworkStore<DbContext>());

    /// <summary>
    ///   Integer keys, customized Identity entities and an intermediate base context. DynamicAuthorization.Mvc 1.x only
    ///   inspected the context's direct base type and failed for this context.
    /// </summary>
    [Fact]
    public async Task Works_with_integer_keys_custom_entities_and_an_intermediate_base_context()
    {
        await using SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();
        ServiceCollection serviceCollection = new();
        serviceCollection.AddLogging();
        serviceCollection.AddDbContext<IntKeyDbContext>(o => o.UseSqlite(connection));
        serviceCollection.AddIdentityCore<IntUser>().AddRoles<IntRole>().AddEntityFrameworkStores<IntKeyDbContext>();
        serviceCollection.AddDynamicAuthorization().AddEntityFrameworkStore<IntKeyDbContext>();
        await using ServiceProvider services = serviceCollection.BuildServiceProvider();

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IServiceProvider provider = scope.ServiceProvider;
        await provider.GetRequiredService<IntKeyDbContext>().Database.EnsureCreatedAsync();

        ManagementResult<string> role = await provider.GetRequiredService<IRoleManagementService>().CreateRoleAsync("Editors", ["mvc::Orders:Index"]);
        IntUser user = new() { UserName = "alice", Email = "alice@example.com" };
        Assert.True((await provider.GetRequiredService<UserManager<IntUser>>().CreateAsync(user)).Succeeded);
        ManagementResult assigned = await provider.GetRequiredService<IUserManagementService>().SetUserRolesAsync(user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), [role.Value!]);
        PermissionEvaluation evaluation = await provider.GetRequiredService<IPermissionChecker>()
            .EvaluateUserAsync(user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), user.UserName, "mvc::Orders:Index");

        Assert.True(role.Succeeded);
        Assert.True(assigned.Succeeded, string.Join(" ", assigned.Errors));
        Assert.True(evaluation.IsGranted);
        Assert.Equal("Editors", Assert.Single(evaluation.GrantingRoles).Name);
    }

    private sealed class ContextWithoutDynamicAuthorization(DbContextOptions<ContextWithoutDynamicAuthorization> options)
        : IdentityDbContext(options);

    internal sealed class IntUser : IdentityUser<int>;

    internal sealed class IntRole : IdentityRole<int>;

    internal sealed class IntUserRole : IdentityUserRole<int>;

    internal sealed class IntUserClaim : IdentityUserClaim<int>;

    internal sealed class IntUserLogin : IdentityUserLogin<int>;

    internal sealed class IntRoleClaim : IdentityRoleClaim<int>;

    internal sealed class IntUserToken : IdentityUserToken<int>;

    internal abstract class IntKeyDbContextBase<TContext>(DbContextOptions<TContext> options)
        : IdentityDbContext<IntUser, IntRole, int, IntUserClaim, IntUserRole, IntUserLogin, IntRoleClaim, IntUserToken>(options)
        where TContext : DbContext;

    internal sealed class IntKeyDbContext(DbContextOptions<IntKeyDbContext> options) : IntKeyDbContextBase<IntKeyDbContext>(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.EnableDynamicAuthorization(o => o.TableName = "RolePermissions");
        }
    }
}
