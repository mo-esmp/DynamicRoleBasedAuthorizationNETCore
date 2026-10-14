using DynamicAuthorization.AspNetCore.Management;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SampleWebApp.Data;

/// <summary>
///   Creates the database and demo accounts. The password of the demo accounts is set in appsettings.json.
/// </summary>
public static class SeedData
{
    public const string AdminUserName = "admin@example.com";
    public const string EditorUserName = "editor@example.com";
    public const string ViewerUserName = "viewer@example.com";

    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IServiceProvider provider = scope.ServiceProvider;
        await provider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();

        UserManager<IdentityUser> users = provider.GetRequiredService<UserManager<IdentityUser>>();
        if (await users.Users.AnyAsync())
        {
            return;
        }

        string password = configuration["Seed:Password"]
                          ?? throw new InvalidOperationException("Set Seed:Password in appsettings.json to create the demo accounts.");

        IRoleManagementService roles = provider.GetRequiredService<IRoleManagementService>();
        await roles.CreateRoleAsync("Order editors", ["orders.update", "orders.delete", "orders.export"]);
        await roles.CreateRoleAsync("Order viewers", ["orders.read"]);
        await roles.CreateRoleAsync("Report readers", ["admin.reports.read"]);

        await CreateUserAsync(users, AdminUserName, password);
        await CreateUserAsync(users, EditorUserName, password, "Order editors", "Report readers");
        await CreateUserAsync(users, ViewerUserName, password, "Order viewers");
    }

    private static async Task CreateUserAsync(UserManager<IdentityUser> users, string userName, string password, params string[] roles)
    {
        IdentityUser user = new(userName) { Email = userName, EmailConfirmed = true };
        IdentityResult result = await users.CreateAsync(user, password);
        if (result.Succeeded && roles.Length > 0)
        {
            result = await users.AddToRolesAsync(user, roles);
        }

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}
