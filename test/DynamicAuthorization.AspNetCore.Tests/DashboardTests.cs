using System.Net;
using System.Text.RegularExpressions;
using DynamicAuthorization.AspNetCore.Dashboard;
using DynamicAuthorization.AspNetCore.Management;
using DynamicAuthorization.AspNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.ApplicationParts;

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

        string[] permissions =
        [
            .. app.GetCatalog().Permissions
                .Where(p => p.Resource?.StartsWith("authz.", StringComparison.Ordinal) == true)
                .Select(p => p.Permission)
                .Order(StringComparer.Ordinal)
        ];

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

    [Fact]
    public async Task Roles_can_be_changed_through_the_dashboard()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        string roleId = await app.CreateRoleAsync("Editors", "orders.read");
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        HttpResponseMessage saved = await PostFormAsync(client, $"/authz/Roles/Edit/{roleId}", $"/authz/Roles/Edit/{roleId}",
            [new("Input.Name", " Writers "), new("Input.Permissions", "orders.update")]);
        RoleDetails? role = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().FindRoleAsync(roleId));

        Assert.Contains("The role &#x27;Writers&#x27; was saved.", await FollowRedirectAsync(client, saved), StringComparison.Ordinal);
        Assert.Equal("Writers", role!.Name);
        Assert.Equal(["orders.update"], role.Permissions);
    }

    [Fact]
    public async Task Role_changes_that_fail_are_redisplayed_with_the_reason()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        string roleId = await app.CreateRoleAsync("Editors");
        await app.CreateRoleAsync("Writers");
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        HttpResponseMessage unnamed = await PostFormAsync(client, $"/authz/Roles/Edit/{roleId}", $"/authz/Roles/Edit/{roleId}", [new("Input.Name", "")]);
        HttpResponseMessage renamed = await PostFormAsync(client, $"/authz/Roles/Edit/{roleId}", $"/authz/Roles/Edit/{roleId}", [new("Input.Name", "Writers")]);
        HttpResponseMessage created = await PostFormAsync(client, "/authz/Roles/Create", "/authz/Roles/Create", [new("Input.Name", "Writers")]);

        Assert.Equal(HttpStatusCode.OK, unnamed.StatusCode);
        Assert.Contains("Enter a name for the role.", await unnamed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Contains("is already taken", await renamed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Contains("is already taken", await created.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Super_admin_roles_are_marked_and_cannot_be_deleted_through_the_dashboard()
    {
        await using TestApp app = await TestApp.StartAsync(o =>
        {
            o.Dashboard = true;
            o.Authorization = a => a.SuperAdminRoles.Add("Administrators");
        });
        string roleId = await app.CreateRoleAsync("Administrators");
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        string roles = await client.GetStringAsync("/authz/Roles");
        string role = await client.GetStringAsync($"/authz/Roles/Edit/{roleId}");
        HttpResponseMessage deleted = await PostFormAsync(client, $"/authz/Roles/Edit/{roleId}", $"/authz/Roles/Edit/{roleId}?handler=Delete", []);

        Assert.Contains("Super admin", roles, StringComparison.Ordinal);
        Assert.DoesNotContain("?handler=Delete", roles, StringComparison.Ordinal);
        Assert.Contains("Members of this role are super admins.", role, StringComparison.Ordinal);
        Assert.Contains("cannot be deleted", await FollowRedirectAsync(client, deleted), StringComparison.Ordinal);
        Assert.NotNull(await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().FindRoleAsync(roleId)));
    }

    [Theory]
    [InlineData("GET", "/authz/Roles/Edit/missing")]
    [InlineData("POST", "/authz/Roles/Edit/missing")]
    [InlineData("POST", "/authz/Roles/Edit/missing?handler=Delete")]
    [InlineData("GET", "/authz/Users/Edit/missing")]
    [InlineData("POST", "/authz/Users/Edit/missing")]
    public async Task Unknown_roles_and_users_are_not_found(string method, string url)
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        HttpResponseMessage response = method == "GET"
            ? await client.GetAsync(url)
            : await PostFormAsync(client, "/authz/Roles/Create", url, [new("Input.Name", "Editors")]);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task User_role_changes_that_fail_are_redisplayed_with_the_reason()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        TestUser user = await app.CreateUserAsync("alice");
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        HttpResponseMessage response = await PostFormAsync(client, $"/authz/Users/Edit/{user.Id}", $"/authz/Users/Edit/{user.Id}", [new("RoleIds", "missing")]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("One or more roles were not found.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task User_page_explains_super_admin_access_and_implied_permissions()
    {
        await using TestApp app = await TestApp.StartAsync(o =>
        {
            o.Dashboard = true;
            o.Authorization = a => a.SuperAdminRoles.Add("Administrators");
        });
        TestUser root = await app.CreateSuperAdminAsync();
        TestUser administrator = await app.CreateUserAsync("alice", "Administrators");
        TestUser editor = await app.CreateUserWithPermissionsAsync("bob", "orders.update");
        HttpClient client = app.CreateClient(root);

        string rootPage = await client.GetStringAsync($"/authz/Users/Edit/{root.Id}");
        string administratorPage = await client.GetStringAsync($"/authz/Users/Edit/{administrator.Id}");
        string editorPage = await client.GetStringAsync($"/authz/Users/Edit/{editor.Id}");

        Assert.Contains("listed in the super admin users", rootPage, StringComparison.Ordinal);
        Assert.Contains("member of the super admin role <strong>Administrators</strong>", administratorPage, StringComparison.Ordinal);
        Assert.Contains("implied by <span class=\"da-mono\">orders.update</span>", editorPage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Users_can_be_searched_and_paged_through_the_dashboard()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        for (int i = 1; i <= 30; i++)
        {
            await app.CreateUserAsync($"user{i:00}");
        }

        await app.CreateUserAsync("alice", "Editors");
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        string firstPage = await client.GetStringAsync("/authz/Users");
        string secondPage = await client.GetStringAsync("/authz/Users?p=2");
        string search = await client.GetStringAsync("/authz/Users?q=ali");
        string noMatch = await client.GetStringAsync("/authz/Users?q=nobody");

        Assert.Contains("aria-label=\"Pagination\"", firstPage, StringComparison.Ordinal);
        Assert.DoesNotContain("user30", firstPage, StringComparison.Ordinal);
        Assert.Contains("user30", secondPage, StringComparison.Ordinal);
        Assert.Contains("<span class=\"da-badge\">Editors</span>", search, StringComparison.Ordinal);
        Assert.DoesNotContain("user01", search, StringComparison.Ordinal);
        Assert.Contains("No users match the search.", noMatch, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overview_lists_what_needs_attention()
    {
        await using TestApp app = await TestApp.StartAsync(o =>
        {
            o.Dashboard = true;
            o.Authorization = a => a.SuperAdminRoles.Add("Administrators");
        });
        await app.CreateRoleAsync("Legacy", [.. Enumerable.Range(1, 10).Select(i => $"removed.permission{i}")]);
        await app.CreateRoleAsync("Empty");

        string html = await app.CreateClient(await app.CreateSuperAdminAsync()).GetStringAsync("/authz");

        Assert.Contains("And 2 more granted permissions", html, StringComparison.Ordinal);
        Assert.Contains("2 roles have no users", html, StringComparison.Ordinal);
        Assert.Contains("1 role grants no permissions", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"da-badge da-badge-accent\">Administrators</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Everything looks good.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overview_shows_when_no_super_admins_are_configured()
    {
        await using TestApp app = await TestApp.StartAsync(o =>
        {
            o.Dashboard = true;
            o.Authorization = a => a.SuperAdminUsers.Clear();
        });
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "authz.overview.read");

        string html = await app.CreateClient(user).GetStringAsync("/authz");

        Assert.Equal(2, Regex.Count(html, "<span class=\"da-subtle\">None</span>"));
    }

    [Fact]
    public async Task Access_check_explains_each_decision()
    {
        await using TestApp app = await TestApp.StartAsync(o =>
        {
            o.Dashboard = true;
            o.Authorization = a => a.SuperAdminRoles.Add("Administrators");
        });
        await app.CreateRoleAsync("Readers", "orders.read");
        await app.CreateRoleAsync("Viewers", "orders.read");
        await app.CreateRoleAsync("Writers", "orders.update");
        TestUser administrator = await app.CreateUserAsync("admin", "Administrators");
        await app.CreateUserAsync("alice", "Readers");
        await app.CreateUserAsync("bob", "Readers", "Viewers");
        await app.CreateUserAsync("carol", "Writers");
        await app.CreateUserAsync("dave", "Readers");
        TestUser noRoles = await app.CreateUserAsync("erin");
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        Task<string> CheckAsync(string query) => client.GetStringAsync($"/authz/Access?{query}");

        Assert.Contains("listed in the super admin users", await CheckAsync("user=root&permission=orders.read"), StringComparison.Ordinal);
        Assert.Contains("member of the super admin role", await CheckAsync($"userId={administrator.Id}&permission=orders.read"), StringComparison.Ordinal);
        Assert.Contains("Granted by this role:", await CheckAsync("user=alice&permission=orders.read"), StringComparison.Ordinal);
        Assert.Contains("Granted by these roles:", await CheckAsync("user=bob&permission=orders.read"), StringComparison.Ordinal);
        Assert.Contains("Granted through orders.update", await CheckAsync("user=carol&permission=orders.read"), StringComparison.Ordinal);
        Assert.Contains("None of the user&#x27;s roles (Readers) grants this permission.", await CheckAsync("user=dave&permission=orders.delete"), StringComparison.Ordinal);
        Assert.Contains("The user has no roles.", await CheckAsync($"userId={noRoles.Id}&permission=orders.read"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Access_check_reports_unknown_users_and_keeps_unknown_permissions()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        TestUser user = await app.CreateUserAsync("alice");
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        string unknownUser = await client.GetStringAsync("/authz/Access?user=nobody&permission=orders.read");
        string unknownId = await client.GetStringAsync("/authz/Access?userId=missing&permission=orders.read");
        string unknownPermission = await client.GetStringAsync($"/authz/Access?userId={user.Id}&permission=removed.permission");

        Assert.Contains("No user named <strong>nobody</strong> was found.", unknownUser, StringComparison.Ordinal);
        Assert.Contains("No user named <strong>missing</strong> was found.", unknownId, StringComparison.Ordinal);
        Assert.Contains("<option value=\"removed.permission\" selected>removed.permission</option>", unknownPermission, StringComparison.Ordinal);
        Assert.Contains("da-result-denied", unknownPermission, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Endpoints_page_links_the_roles_that_grant_each_permission()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Dashboard = true);
        string roleId = await app.CreateRoleAsync("Editors", "orders.read");

        string html = await app.CreateClient(await app.CreateSuperAdminAsync()).GetStringAsync("/authz/Endpoints");

        Assert.Contains($"<a class=\"da-badge\" href=\"/authz/Roles/Edit/{roleId}\">Editors</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Role_form_groups_permissions_that_are_not_resource_operations()
    {
        await using TestApp app = await TestApp.StartAsync(o =>
        {
            o.Dashboard = true;
            o.Authorization = a =>
            {
                a.Convention = PermissionConvention.Endpoint;
                a.AddPermission("export-everything", "Export everything");
            };
        });

        string html = await app.CreateClient(await app.CreateSuperAdminAsync()).GetStringAsync("/authz/Roles/Create");

        Assert.Contains("value=\"export-everything\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"mvc::Orders:Index\"", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"da-badge\">Custom</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"da-badge\">MVC</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mapping_the_dashboard_requires_its_services()
    {
        await using WebApplication app = WebApplication.CreateBuilder().Build();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(app.MapDynamicAuthorizationDashboard);

        Assert.Contains("AddDashboard()", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dashboard_pages_are_added_when_the_host_does_not_discover_them()
    {
        ServiceCollection services = new();

        services.AddDynamicAuthorization().AddDashboard();

        ApplicationPartManager parts = Assert.IsType<ApplicationPartManager>(
            services.Single(d => d.ServiceType == typeof(ApplicationPartManager)).ImplementationInstance);
        Assert.Contains(parts.ApplicationParts, p => p is AssemblyPart part && part.Assembly == typeof(DashboardOptions).Assembly);
    }

    /// <summary>
    ///   Follows a redirect with the cookies it set, like a browser: status messages are kept in TempData cookies.
    /// </summary>
    private static async Task<string> FollowRedirectAsync(HttpClient client, HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using HttpRequestMessage request = new(HttpMethod.Get, response.Headers.Location);
        foreach (string cookie in response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies) ? cookies : [])
        {
            request.Headers.Add("Cookie", cookie.Split(';')[0]);
        }

        HttpResponseMessage page = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        return await page.Content.ReadAsStringAsync();
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
