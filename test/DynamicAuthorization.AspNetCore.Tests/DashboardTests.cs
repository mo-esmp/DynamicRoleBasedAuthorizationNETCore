using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using DynamicAuthorization.AspNetCore.Management;
using DynamicAuthorization.AspNetCore.Tests.Infrastructure;

namespace DynamicAuthorization.AspNetCore.Tests;

public sealed partial class DashboardTests
{
    [Theory]
    [InlineData("/authz")]
    [InlineData("/authz/Endpoints")]
    [InlineData("/authz/Roles")]
    [InlineData("/authz/Roles/Create")]
    [InlineData("/authz/Users")]
    [InlineData("/authz/Access")]
    public async Task Pages_render_for_super_admins(string url)
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        TestUser admin = await app.CreateSuperAdminAsync();

        HttpResponseMessage response = await app.CreateClient(admin).GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Dynamic Authorization", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pages_with_data_render_for_super_admins()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        string roleId = await app.CreateRoleAsync("Editors", "orders.read", "orphaned.permission");
        TestUser user = await app.CreateUserAsync("alice", "Editors");
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        string overview = await client.GetStringAsync("/authz");
        string role = await client.GetStringAsync($"/authz/Roles/Edit/{roleId}");
        string userPage = await client.GetStringAsync($"/authz/Users/Edit/{user.Id}");
        string access = await client.GetStringAsync("/authz/Access?user=alice&permission=orders.read");

        Assert.Contains("orphaned.permission", overview, StringComparison.Ordinal);
        Assert.Contains("Not found in this application", role, StringComparison.Ordinal);
        Assert.Contains("orders.read", userPage, StringComparison.Ordinal);
        Assert.Contains("da-result-granted", access, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pages_are_challenged_for_anonymous_users_and_forbidden_without_permission()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        TestUser user = await app.CreateUserAsync("alice");

        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/authz")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.CreateClient(user).GetAsync("/authz")).StatusCode);
    }

    [Theory]
    [InlineData("authz.overview.read")]
    [InlineData("authz.endpoints.read")]
    [InlineData("authz.roles.read")]
    [InlineData("authz.roles.create")]
    [InlineData("authz.roles.update")]
    [InlineData("authz.roles.delete")]
    [InlineData("authz.users.read")]
    [InlineData("authz.users.update")]
    [InlineData("authz.access.read")]
    public async Task Dashboard_operations_are_permissions_that_can_be_granted(string permission)
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);

        Assert.NotNull(app.GetCatalog().FindPermission(permission));
    }

    [Fact]
    public async Task Dashboard_has_only_its_crud_permissions()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);

        string[] permissions = app.GetCatalog().Permissions
            .Where(p => p.Resource?.StartsWith("authz.", StringComparison.Ordinal) == true)
            .Select(p => p.Permission)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "authz.access.read", "authz.endpoints.read", "authz.overview.read", "authz.roles.create", "authz.roles.delete",
                "authz.roles.read", "authz.roles.update", "authz.users.read", "authz.users.update",
            ],
            permissions);
    }

    [Fact]
    public async Task Each_page_requires_its_operation()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "authz.roles.read");
        HttpClient client = app.CreateClient(user);

        PermissionDescriptor? permission = app.GetCatalog().FindPermission("authz.roles.read");

        Assert.Equal("Dashboard / Roles", permission?.Group);
        Assert.Equal("Read", permission?.DisplayName);
        Assert.Equal(HttpStatusCode.OK, await client.GetStatusAsync("/authz/Roles"));
        Assert.Equal(HttpStatusCode.Forbidden, await client.GetStatusAsync("/authz/Roles/Create"));
        Assert.Equal(HttpStatusCode.Forbidden, await client.GetStatusAsync("/authz/Users"));
    }

    [Fact]
    public async Task Navigation_and_actions_only_show_what_the_user_can_do()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "authz.roles.read");

        string html = await app.CreateClient(user).GetStringAsync("/authz/Roles");

        Assert.Contains("href=\"/authz/Roles\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/authz/Users\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("New role", html, StringComparison.Ordinal);
        Assert.DoesNotContain("?handler=Delete", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_a_role_requires_the_delete_permission()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        string roleId = await app.CreateRoleAsync("Editors");
        TestUser editor = await app.CreateUserWithPermissionsAsync("alice", "authz.roles.update");
        HttpClient client = app.CreateClient(editor);

        string html = await client.GetStringAsync($"/authz/Roles/Edit/{roleId}");
        HttpResponseMessage deleted = await PostFormAsync(client, $"/authz/Roles/Edit/{roleId}", $"/authz/Roles/Edit/{roleId}?handler=Delete", []);

        Assert.DoesNotContain("Delete role", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, deleted.StatusCode);
    }

    [Fact]
    public async Task Role_form_shows_resources_as_a_matrix_of_operations()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);

        string html = await app.CreateClient(await app.CreateSuperAdminAsync()).GetStringAsync("/authz/Roles/Create");

        Assert.Contains("data-matrix-row", html, StringComparison.Ordinal);
        Assert.Contains("value=\"orders.read\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"orders.archive\"", html, StringComparison.Ordinal);
        Assert.Contains("data-implies-read=\"true\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Route_prefix_can_be_changed()
    {
        await using TestApp app = await TestApp.StartAsync(o =>
        {
            o.Dashboard = true;
            o.DashboardOptions = d => d.RoutePrefix = "/admin/access/";
        });
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        HttpResponseMessage roles = await client.GetAsync("/admin/access/Roles");
        HttpResponseMessage css = await client.GetAsync("/admin/access/_assets/dashboard.css");
        HttpResponseMessage defaultPrefix = await client.GetAsync("/authz");

        Assert.Equal(HttpStatusCode.OK, roles.StatusCode);
        Assert.Contains("/admin/access/_assets/dashboard.css?v=", await roles.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, defaultPrefix.StatusCode);
    }

    [Fact]
    public async Task Pages_are_not_reachable_when_the_dashboard_is_not_enabled()
    {
        await using TestApp app = await TestApp.StartAsync();
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/DynamicAuthorization")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/DynamicAuthorization/Users")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/authz")).StatusCode);
    }

    [Fact]
    public async Task Roles_can_be_created_and_deleted_through_the_dashboard()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        HttpResponseMessage created = await PostFormAsync(client, "/authz/Roles/Create", "/authz/Roles/Create",
        [
            new("Input.Name", "Editors"),
            new("Input.Permissions", "orders.read"),
            new("Input.Permissions", "secure.update"),
        ]);
        RoleSummary role = Assert.Single(await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().GetRolesAsync()));

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal("/authz/Roles", created.Headers.Location?.OriginalString);
        Assert.Equal("Editors", role.Name);
        Assert.Equal(2, role.PermissionCount);

        HttpResponseMessage deleted = await PostFormAsync(client, $"/authz/Roles/Edit/{role.Id}", $"/authz/Roles/Edit/{role.Id}?handler=Delete", []);

        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Empty(await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().GetRolesAsync()));
    }

    [Fact]
    public async Task Invalid_role_form_is_redisplayed_with_errors()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        HttpResponseMessage response = await PostFormAsync(client, "/authz/Roles/Create", "/authz/Roles/Create", [new("Input.Name", "")]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Enter a name for the role.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Posting_without_an_antiforgery_token_is_rejected()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        HttpResponseMessage response = await client.PostAsync("/authz/Roles/Create", new FormUrlEncodedContent([new("Input.Name", "Editors")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().GetRolesAsync()));
    }

    [Fact]
    public async Task User_roles_can_be_changed_through_the_dashboard()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        string roleId = await app.CreateRoleAsync("Editors", "orders.read");
        TestUser user = await app.CreateUserAsync("alice");
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        HttpResponseMessage response = await PostFormAsync(client, $"/authz/Users/Edit/{user.Id}", $"/authz/Users/Edit/{user.Id}", [new("RoleIds", roleId)]);
        HttpResponseMessage orders = await app.CreateClient(user).GetAsync("/Orders");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, orders.StatusCode);
    }

    [Fact]
    public async Task Assets_are_served_anonymously_with_caching_headers()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/authz/_assets/dashboard.css?v=1");
        using HttpRequestMessage conditional = new(HttpMethod.Get, "/authz/_assets/dashboard.css");
        conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        HttpResponseMessage notModified = await client.SendAsync(conditional);
        HttpResponseMessage missing = await client.GetAsync("/authz/_assets/missing.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Assets_are_not_listed_as_endpoints()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);

        Assert.DoesNotContain(app.GetCatalog().Endpoints, e => e.Route.Contains("_assets", StringComparison.Ordinal));
    }

    /// <summary>
    ///   Loads a form page to get the antiforgery cookie and token, then posts the form like a browser would.
    /// </summary>
    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string formUrl, string postUrl, KeyValuePair<string, string>[] fields)
    {
        HttpResponseMessage form = await client.GetAsync(formUrl);
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        string html = await form.Content.ReadAsStringAsync();
        string token = AntiforgeryTokenRegex().Match(html).Groups[1].Value;
        Assert.NotEmpty(token);

        using HttpRequestMessage request = new(HttpMethod.Post, postUrl)
        {
            Content = new FormUrlEncodedContent([.. fields, new("__RequestVerificationToken", token)]),
        };
        foreach (string cookie in form.Headers.GetValues("Set-Cookie"))
        {
            request.Headers.Add("Cookie", cookie.Split(';')[0]);
        }

        return await client.SendAsync(request);
    }

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="([^"]+)""")]
    private static partial Regex AntiforgeryTokenRegex();
}
