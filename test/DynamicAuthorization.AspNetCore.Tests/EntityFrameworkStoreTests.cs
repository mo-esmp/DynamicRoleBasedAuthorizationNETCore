using DynamicAuthorization.AspNetCore.EntityFrameworkCore;
using DynamicAuthorization.AspNetCore.Management;
using DynamicAuthorization.AspNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

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
        PermissionChangeSet unchanged = await app.WithScopeAsync(s => s.GetRequiredService<IPermissionStore>().SetPermissionsAsync(roleId, ["C", "a"]));
        IReadOnlySet<string> stored = await app.WithScopeAsync(s => s.GetRequiredService<IPermissionStore>().GetPermissionsAsync(roleId));

        Assert.True(first.HasChanges);
        Assert.False(unchanged.HasChanges);
        Assert.Equal(["a", "B"], first.Added);
        Assert.Empty(first.Removed);
        Assert.Equal(["c"], second.Added);
        Assert.Equal(["B"], second.Removed);
        Assert.Equal(2, stored.Count);
        Assert.Contains("A", stored);
        Assert.Contains("c", stored);
    }

    [Fact]
    public async Task Permissions_longer_than_the_column_are_rejected()
    {
        await using TestApp app = await TestApp.StartAsync();
        string roleId = await app.CreateRoleAsync("Editors");
        string permission = new('a', DynamicAuthorizationModelOptions.MaxPermissionLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            app.WithScopeAsync(s => s.GetRequiredService<IPermissionStore>().SetPermissionsAsync(roleId, [permission])));
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
    public async Task Role_names_are_required_and_renaming_to_a_taken_name_fails_without_changes()
    {
        await using TestApp app = await TestApp.StartAsync();
        string roleId = await app.CreateRoleAsync("Editors", "a");
        await app.CreateRoleAsync("Writers");

        ManagementResult<string> unnamed = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().CreateRoleAsync(" ", []));
        ManagementResult cleared = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().UpdateRoleAsync(roleId, "", []));
        ManagementResult taken = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().UpdateRoleAsync(roleId, "Writers", ["b"]));
        RoleDetails? role = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().FindRoleAsync(roleId));

        Assert.False(unnamed.Succeeded);
        Assert.False(cleared.Succeeded);
        Assert.False(taken.Succeeded);
        Assert.Equal("Editors", role!.Name);
        Assert.Equal(["a"], role.Permissions);
    }

    [Fact]
    public async Task Management_operations_join_the_callers_transaction()
    {
        await using TestApp app = await TestApp.StartAsync();

        ManagementResult<string> created = await app.WithScopeAsync(async s =>
        {
            await using IDbContextTransaction transaction = await s.GetRequiredService<TestDbContext>().Database.BeginTransactionAsync();
            ManagementResult<string> result = await s.GetRequiredService<IRoleManagementService>().CreateRoleAsync("Editors", ["a"]);
            await transaction.RollbackAsync();
            return result;
        });

        Assert.True(created.Succeeded);
        Assert.Empty(await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().GetRolesAsync()));
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
        ManagementResult updated = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().UpdateRoleAsync("missing", "Editors", []));
        ManagementResult deleted = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().DeleteRoleAsync("missing"));

        Assert.Null(role);
        Assert.False(updated.Succeeded);
        Assert.False(deleted.Succeeded);
    }

    [Fact]
    public async Task Unknown_users_are_reported_as_not_found()
    {
        await using TestApp app = await TestApp.StartAsync();
        await app.CreateUserAsync("alice");

        UserSummary? byId = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().FindUserAsync("missing"));
        UserSummary? byName = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().FindUserByNameAsync("bob"));
        UserSummary? blank = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().FindUserByNameAsync(" "));
        PagedResult<UserSummary> search = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().GetUsersAsync(new UserQuery("bob")));
        ManagementResult assigned = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().SetUserRolesAsync("missing", []));

        Assert.Null(byId);
        Assert.Null(byName);
        Assert.Null(blank);
        Assert.Empty(search.Items);
        Assert.Equal(0, search.TotalCount);
        Assert.False(assigned.Succeeded);
    }

    /// <summary>
    ///   Keys of other types than <see cref="string"/> can fail to parse; they are reported like unknown identifiers.
    /// </summary>
    [Theory]
    [InlineData("not-a-number")]
    [InlineData("99999999999")]
    [InlineData("")]
    public async Task Identifiers_that_are_not_valid_keys_are_reported_as_not_found(string id)
    {
        await using SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();
        await using ServiceProvider services = await CreateIntKeyServicesAsync(connection);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IServiceProvider provider = scope.ServiceProvider;
        IntUser user = new() { UserName = "alice" };
        Assert.True((await provider.GetRequiredService<UserManager<IntUser>>().CreateAsync(user)).Succeeded);
        IPermissionStore store = provider.GetRequiredService<IPermissionStore>();

        Assert.Null(await provider.GetRequiredService<IUserManagementService>().FindUserAsync(id));
        Assert.Null(await provider.GetRequiredService<IRoleManagementService>().FindRoleAsync(id));
        Assert.Empty(await store.GetPermissionsAsync(id));
        Assert.Empty(await provider.GetRequiredService<IUserRoleProvider>().GetUserRolesAsync(id));
        Assert.False((await provider.GetRequiredService<IUserManagementService>().SetUserRolesAsync(user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), [id])).Succeeded);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetPermissionsAsync(id, ["a"]));
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
    public async Task Setting_the_current_roles_again_succeeds()
    {
        await using TestApp app = await TestApp.StartAsync();
        string editors = await app.CreateRoleAsync("Editors");
        TestUser user = await app.CreateUserAsync("alice", "Editors");

        ManagementResult result = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().SetUserRolesAsync(user.Id, [editors, editors]));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Members_can_leave_a_super_admin_role_while_another_member_remains()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a =>
        {
            a.SuperAdminUsers.Clear();
            a.SuperAdminRoles.Add("Administrators");
        });
        string administrators = await app.CreateRoleAsync("Administrators");
        await app.CreateRoleAsync("Editors");
        TestUser alice = await app.CreateUserAsync("alice", "Administrators", "Editors");
        await app.CreateUserAsync("bob", "Administrators");

        ManagementResult leftEditors = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().SetUserRolesAsync(alice.Id, [administrators]));
        ManagementResult leftAdministrators = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().SetUserRolesAsync(alice.Id, []));

        Assert.True(leftEditors.Succeeded);
        Assert.True(leftAdministrators.Succeeded);
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
        {
            await app.CreateUserAsync($"user{i:00}");
        }

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
        await using ServiceProvider services = await CreateIntKeyServicesAsync(connection);
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IServiceProvider provider = scope.ServiceProvider;

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

    [Fact]
    public void Model_builder_rejects_models_without_an_identity_role()
    {
        ModelBuilder builder = new();
        builder.Entity<Animal>();
        builder.Entity<Dog>().HasBaseType<Animal>();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => builder.EnableDynamicAuthorization());

        Assert.Contains("No ASP.NET Core Identity role entity", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<ServiceProvider> CreateIntKeyServicesAsync(SqliteConnection connection)
    {
        ServiceCollection serviceCollection = new();
        serviceCollection.AddLogging();
        serviceCollection.AddDbContext<IntKeyDbContext>(o => o.UseSqlite(connection));
        serviceCollection.AddIdentityCore<IntUser>().AddRoles<IntRole>().AddEntityFrameworkStores<IntKeyDbContext>();
        serviceCollection.AddDynamicAuthorization().AddEntityFrameworkStore<IntKeyDbContext>();
        ServiceProvider services = serviceCollection.BuildServiceProvider();

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IntKeyDbContext>().Database.EnsureCreatedAsync();

        return services;
    }

    private class Animal
    {
        public int Id { get; set; }
    }

    private sealed class Dog : Animal;

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
