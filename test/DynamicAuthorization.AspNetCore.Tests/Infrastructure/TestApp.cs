using DynamicAuthorization.AspNetCore.Dashboard;
using DynamicAuthorization.AspNetCore.Management;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicAuthorization.AspNetCore.Tests.Infrastructure;

/// <summary>
///   An in-memory application with MVC controllers, Razor Pages and Minimal APIs, ASP.NET Core Identity on SQLite,
///   and dynamic authorization. Every test starts its own instance.
/// </summary>
internal sealed class TestApp : IAsyncDisposable
{
    public const string SuperAdminUserName = "root";

    private readonly WebApplication _app;
    private readonly SqliteConnection _connection;

    private TestApp(WebApplication app, SqliteConnection connection)
    {
        _app = app;
        _connection = connection;
    }

    public IServiceProvider Services => _app.Services;

    public static async Task<TestApp> StartAsync(Action<TestAppOptions>? configure = null)
    {
        TestAppOptions options = new();
        configure?.Invoke(options);

        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(TestApp).Assembly.GetName().Name,
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();

        builder.Services.AddDbContext<TestDbContext>(o => o.UseSqlite(connection));
        builder.Services
            .AddIdentityCore<IdentityUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<TestDbContext>();
        builder.Services
            .AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
        builder.Services.AddAuthorization(o =>
            o.AddPolicy("CanSchedule", p => p.RequireAuthenticatedUser().AddRequirements(new DynamicPermissionRequirement("reports.schedule"))));
        builder.Services.AddControllersWithViews();
        builder.Services.AddRazorPages(o => o.Conventions.AuthorizeFolder("/Folder"));

        IDynamicAuthorizationBuilder authorization = builder.Services
            .AddDynamicAuthorization(o =>
            {
                o.SuperAdminUsers.Add(SuperAdminUserName);
                o.AddPermission("reports.schedule", "Schedule reports", "Reports");
                options.Authorization?.Invoke(o);
            })
            .AddEntityFrameworkStore<TestDbContext>();

        if (options.Dashboard)
        {
            authorization.AddDashboard(o => options.DashboardOptions?.Invoke(o));
        }

        WebApplication app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllerRoute("areas", "{area:exists}/{controller=Home}/{action=Index}/{id?}");
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        app.MapRazorPages();
        MapMinimalApis(app);
        if (options.Dashboard)
        {
            app.MapDynamicAuthorizationDashboard();
        }

        await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        }

        await app.StartAsync();

        return new TestApp(app, connection);
    }

    public HttpClient CreateClient(TestUser? user = null)
    {
        HttpClient client = _app.GetTestClient();
        if (user is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, user.Id);
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserNameHeader, user.UserName);
        }

        return client;
    }

    public async Task<TestUser> CreateUserAsync(string userName, params string[] roles)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<IdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (string role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(role)));
            }
        }

        IdentityUser user = new(userName) { Email = $"{userName}@example.com" };
        EnsureSucceeded(await users.CreateAsync(user));
        if (roles.Length > 0)
        {
            EnsureSucceeded(await users.AddToRolesAsync(user, roles));
        }

        return new TestUser(user.Id, userName);
    }

    public Task<TestUser> CreateSuperAdminAsync() => CreateUserAsync(SuperAdminUserName);

    /// <summary>
    ///   Creates a role that grants the given permissions, and a user in that role.
    /// </summary>
    public async Task<TestUser> CreateUserWithPermissionsAsync(string userName, params string[] permissions)
    {
        string role = $"{userName}-role";
        await CreateRoleAsync(role, permissions);

        return await CreateUserAsync(userName, role);
    }

    public Task<string> CreateRoleAsync(string name, params string[] permissions) =>
        WithScopeAsync(async services =>
        {
            ManagementResult<string> result = await services.GetRequiredService<IRoleManagementService>().CreateRoleAsync(name, permissions);
            Assert.True(result.Succeeded, string.Join(" ", result.Errors));
            return result.Value!;
        });

    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        return await action(scope.ServiceProvider);
    }

    public PermissionCatalog GetCatalog() => Services.GetRequiredService<IPermissionCatalogProvider>().GetCatalog();

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static void MapMinimalApis(WebApplication app)
    {
        RouteGroupBuilder items = app.MapGroup("/api/items").RequireAuthorization().WithTags("Items API");
        items.MapGet("", () => "items");                                         // items.read
        items.MapPost("", () => "created");                                      // items.create
        items.MapPut("/{id:int}", (int id) => $"updated {id}");                  // items.update
        items.MapDelete("/{id:int}", (int id) => $"deleted {id}");               // items.delete
        items.MapPost("/{id:int}/ship", (int id) => $"shipped {id}");            // items.ship
        items.MapDelete("", () => "purged").RequirePermission("items.purge", "Purge items");
        items.MapPost("/{id:int}/restore", (int id) => $"restored {id}").RequirePermissionOperation(PermissionOperation.Update);

        app.MapGet("/api/named", () => "named").RequireAuthorization().WithName("GetNamed");
        app.MapGet("/api/public", () => "public");
        app.MapGet("/api/schedule", () => "scheduled").RequireAuthorization("CanSchedule");
        app.MapGroup("/api/strict").RequireDynamicAuthorization().MapGet("/reports", () => "strict");
        app.MapGroup("/api/groups").RequireAuthorization().MapGet("", () => "groups");

        // The same resource as OrdersController: orders.read covers all of them.
        app.MapGet("/api/v1/orders", () => "api orders").RequireAuthorization();
        app.MapGet("/api/legacy-orders", () => "legacy orders").RequireAuthorization().WithPermissionResource("orders");
    }

    private static void EnsureSucceeded(IdentityResult result) =>
        Assert.True(result.Succeeded, string.Join(" ", result.Errors.Select(e => e.Description)));
}

internal sealed class TestAppOptions
{
    public Action<DynamicAuthorizationOptions>? Authorization { get; set; }

    public bool Dashboard { get; set; }

    public Action<DashboardOptions>? DashboardOptions { get; set; }
}

internal sealed record TestUser(string Id, string UserName);
