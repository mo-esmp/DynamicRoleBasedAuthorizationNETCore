using DynamicAuthorization.AspNetCore.Tests.Infrastructure;

namespace DynamicAuthorization.AspNetCore.Tests;

public sealed class TagHelperTests
{
    [Fact]
    public async Task Secure_content_renders_only_targets_the_user_can_access()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "secure.read", "reports.schedule");

        string html = await app.CreateClient(user).GetStringAsync("/Links");

        Assert.Contains("[secure-page]", html, StringComparison.Ordinal);
        Assert.Contains("[secure-page-relative]", html, StringComparison.Ordinal);
        Assert.Contains("[public-page]", html, StringComparison.Ordinal);
        Assert.Contains("[schedule]", html, StringComparison.Ordinal);
        Assert.DoesNotContain("[secure-archive]", html, StringComparison.Ordinal);
        Assert.DoesNotContain("[orders]", html, StringComparison.Ordinal);
        Assert.DoesNotContain("[orders-export]", html, StringComparison.Ordinal);
        Assert.DoesNotContain("[managers]", html, StringComparison.Ordinal);
        Assert.DoesNotContain("[missing]", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Secure_content_checks_the_permission_of_a_page_handler()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "secure.archive");

        string html = await app.CreateClient(user).GetStringAsync("/Links");

        Assert.Contains("[secure-archive]", html, StringComparison.Ordinal);
        Assert.Contains("[secure-page]", html, StringComparison.Ordinal); // secure.archive implies secure.read
    }

    [Fact]
    public async Task Secure_content_evaluates_explicit_permissions_and_roles_of_the_target()
    {
        await using TestApp app = await TestApp.StartAsync();
        await app.CreateRoleAsync("Exporters", "orders.export");
        TestUser user = await app.CreateUserAsync("alice", "Exporters", "Manager");

        string html = await app.CreateClient(user).GetStringAsync("/Links");

        Assert.Contains("[orders-export]", html, StringComparison.Ordinal);
        Assert.Contains("[managers]", html, StringComparison.Ordinal);
        Assert.Contains("[orders]", html, StringComparison.Ordinal); // orders.export implies orders.read
        Assert.DoesNotContain("[secure-page]", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Secure_content_renders_public_targets_for_anonymous_users()
    {
        await using TestApp app = await TestApp.StartAsync();

        string html = await app.CreateClient().GetStringAsync("/Links");

        Assert.Contains("[public-page]", html, StringComparison.Ordinal);
        Assert.DoesNotContain("[secure-page]", html, StringComparison.Ordinal);
        Assert.DoesNotContain("[schedule]", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Secure_content_renders_everything_for_super_admins()
    {
        await using TestApp app = await TestApp.StartAsync();

        string html = await app.CreateClient(await app.CreateSuperAdminAsync()).GetStringAsync("/Links");

        Assert.Contains("[secure-page]", html, StringComparison.Ordinal);
        Assert.Contains("[secure-archive]", html, StringComparison.Ordinal);
        Assert.Contains("[orders]", html, StringComparison.Ordinal);
        Assert.Contains("[schedule]", html, StringComparison.Ordinal);
        Assert.DoesNotContain("[missing]", html, StringComparison.Ordinal);
    }
}
