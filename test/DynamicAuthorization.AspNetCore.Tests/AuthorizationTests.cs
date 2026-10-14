using System.Net;
using System.Security.Claims;
using DynamicAuthorization.AspNetCore.Management;
using DynamicAuthorization.AspNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace DynamicAuthorization.AspNetCore.Tests;

/// <summary>
///   Authorization with the default CRUD convention: one permission per resource and operation.
/// </summary>
public sealed class AuthorizationTests
{
    [Fact]
    public async Task Anonymous_request_to_a_protected_action_is_challenged()
    {
        await using TestApp app = await TestApp.StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await app.CreateClient().GetStatusAsync("/Orders"));
    }

    [Fact]
    public async Task User_without_the_permission_is_forbidden()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserAsync("alice");

        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(user).GetStatusAsync("/Orders"));
    }

    [Theory]
    [InlineData("/Orders")]
    [InlineData("/Orders/Index")]
    [InlineData("/Orders/Details/1")]
    [InlineData("/Orders/Summary")]
    public async Task Read_covers_index_details_and_other_get_actions(string url)
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser reader = await app.CreateUserWithPermissionsAsync("alice", "orders.read");

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(reader).GetStatusAsync(url));
    }

    [Theory]
    [InlineData("GET", "/Orders/Create", "orders.create")]
    [InlineData("POST", "/Orders/Create", "orders.create")]
    [InlineData("GET", "/Orders/Edit/1", "orders.update")]
    [InlineData("POST", "/Orders/Edit/1", "orders.update")]
    [InlineData("GET", "/Orders/Delete/1", "orders.delete")]
    [InlineData("POST", "/Orders/Delete/1", "orders.delete")]
    [InlineData("POST", "/Orders/Purge", "orders.delete")]
    [InlineData("POST", "/Orders/Archive/1", "orders.archive")]
    [InlineData("GET", "/Orders/Report", "orders.report")]
    [InlineData("GET", "/Orders/Download", "orders.export")]
    public async Task Each_action_requires_the_permission_of_its_operation(string method, string url, string permission)
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser reader = await app.CreateUserWithPermissionsAsync("reader", "orders.read");
        TestUser granted = await app.CreateUserWithPermissionsAsync("granted", permission);

        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(reader).SendStatusAsync(new HttpMethod(method), url));
        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(granted).SendStatusAsync(new HttpMethod(method), url));
    }

    [Fact]
    public async Task Permission_keys_are_case_insensitive()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "ORDERS.Read");

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(user).GetStatusAsync("/Orders"));
    }

    [Theory]
    [InlineData("orders.update")]
    [InlineData("orders.archive")]
    [InlineData("orders.export")]
    public async Task Any_operation_implies_read(string permission)
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", permission);

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(user).GetStatusAsync("/Orders"));
        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(user).GetStatusAsync("/Orders/Details/1"));
    }

    [Fact]
    public async Task Operations_do_not_imply_read_when_disabled()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a => a.OperationsImplyRead = false);
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "orders.update");

        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(user).GetStatusAsync("/Orders"));
        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(user).GetStatusAsync("/Orders/Edit/1"));
    }

    [Fact]
    public async Task Read_does_not_imply_other_operations_or_other_resources()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "admin.settings.update");
        HttpClient client = app.CreateClient(user);

        Assert.Equal(HttpStatusCode.OK, await client.GetStatusAsync("/Admin/Settings"));
        Assert.Equal(HttpStatusCode.Forbidden, await client.GetStatusAsync("/Orders"));
    }

    [Fact]
    public async Task Super_admin_user_can_access_everything()
    {
        await using TestApp app = await TestApp.StartAsync();
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        foreach (string url in new[] { "/Orders", "/Orders/Download", "/Secure", "/Folder/Edit", "/Admin/Settings", "/api/items", "/api/products" })
        {
            Assert.Equal(HttpStatusCode.OK, await client.GetStatusAsync(url));
        }
    }

    [Fact]
    public async Task Super_admin_user_names_are_compared_case_insensitively()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserAsync("ROOT");

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(user).GetStatusAsync("/Orders"));
    }

    [Fact]
    public async Task Members_of_a_super_admin_role_can_access_everything()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a => a.SuperAdminRoles.Add("Administrators"));
        TestUser user = await app.CreateUserAsync("alice", "Administrators");

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(user).SendStatusAsync(HttpMethod.Post, "/Secure?handler=Delete"));
    }

    [Fact]
    public async Task AllowAnonymous_action_is_public()
    {
        await using TestApp app = await TestApp.StartAsync();

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient().GetStatusAsync("/Orders/Public"));
    }

    [Fact]
    public async Task Ignored_action_only_requires_a_signed_in_user()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserAsync("alice");

        Assert.Equal(HttpStatusCode.Unauthorized, await app.CreateClient().GetStatusAsync("/Orders/Mine"));
        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(user).GetStatusAsync("/Orders/Mine"));
    }

    [Fact]
    public async Task Ignored_minimal_api_only_requires_a_signed_in_user()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserAsync("alice");

        Assert.Equal(HttpStatusCode.Unauthorized, await app.CreateClient().GetStatusAsync("/api/ignored"));
        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(user).GetStatusAsync("/api/ignored"));
        Assert.Equal(EndpointProtection.Excluded, app.GetCatalog().FindEndpoint("endpoint:GET /api/ignored")?.Protection);
    }

    [Fact]
    public async Task Requirements_declared_by_attributes_are_not_permission_checked()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser auditor = await app.CreateUserAsync("alice", "Auditor");
        TestUser other = await app.CreateUserWithPermissionsAsync("bob", "audits.read");

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(auditor).GetStatusAsync("/Audits"));
        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(other).GetStatusAsync("/Audits"));
        Assert.Equal(EndpointProtection.PolicyOnly, app.GetCatalog().FindEndpoint("mvc::Audits:Index")?.Protection);
    }

    [Fact]
    public async Task Ignored_areas_only_require_a_signed_in_user()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a => a.IgnoredAreas.Add("Admin"));
        TestUser user = await app.CreateUserAsync("alice");

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(user).GetStatusAsync("/Admin/Settings"));
    }

    [Fact]
    public async Task Role_based_authorize_attribute_is_not_permission_checked()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser manager = await app.CreateUserAsync("alice", "Manager");
        TestUser other = await app.CreateUserWithPermissionsAsync("bob", "managers.read");

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(manager).GetStatusAsync("/Managers"));
        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(other).GetStatusAsync("/Managers"));
    }

    [Fact]
    public async Task Inherited_and_repeated_authorize_attributes_are_supported()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser denied = await app.CreateUserAsync("alice");
        TestUser granted = await app.CreateUserWithPermissionsAsync("bob", "legacy.read");

        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(denied).GetStatusAsync("/Inherited"));
        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(granted).GetStatusAsync("/Inherited"));
    }

    [Fact]
    public async Task Area_is_part_of_the_resource()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser withoutArea = await app.CreateUserWithPermissionsAsync("alice", "settings.read");
        TestUser withArea = await app.CreateUserWithPermissionsAsync("bob", "admin.settings.read");

        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(withoutArea).GetStatusAsync("/Admin/Settings"));
        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(withArea).GetStatusAsync("/Admin/Settings"));
    }

    [Theory]
    [InlineData("GET", "/Secure", "secure.read")]
    [InlineData("POST", "/Secure", "secure.update")]
    [InlineData("POST", "/Secure?handler=Archive", "secure.archive")]
    [InlineData("POST", "/Secure?handler=Delete", "secure.delete")]
    public async Task Razor_page_handlers_require_the_permission_of_their_operation(string method, string url, string permission)
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a => a.OperationsImplyRead = false);
        TestUser denied = await app.CreateUserWithPermissionsAsync("denied", "secure.other");
        TestUser granted = await app.CreateUserWithPermissionsAsync("granted", permission);

        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(denied).SendStatusAsync(new HttpMethod(method), url));
        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(granted).SendStatusAsync(new HttpMethod(method), url));
    }

    [Fact]
    public async Task Head_requests_require_the_permission_of_the_get_handler()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a => a.OperationsImplyRead = false);
        TestUser reader = await app.CreateUserWithPermissionsAsync("alice", "secure.read");
        TestUser editor = await app.CreateUserWithPermissionsAsync("bob", "secure.update");

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(reader).SendStatusAsync(HttpMethod.Head, "/Secure"));
        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(editor).SendStatusAsync(HttpMethod.Head, "/Secure"));
    }

    [Theory]
    [InlineData("GET", "/Folder/Actions", "folder.read")]
    [InlineData("POST", "/Folder/Actions?handler=Run", "folder.run")]
    // No unnamed POST handler: the request only renders the page, like a GET.
    [InlineData("POST", "/Folder/Actions", "folder.read")]
    public async Task Razor_page_without_an_unnamed_get_handler_requires_read_to_render(string method, string url, string permission)
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a => a.OperationsImplyRead = false);
        TestUser denied = await app.CreateUserWithPermissionsAsync("denied", "folder.other");
        TestUser granted = await app.CreateUserWithPermissionsAsync("granted", permission);

        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(denied).SendStatusAsync(new HttpMethod(method), url));
        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(granted).SendStatusAsync(new HttpMethod(method), url));
    }

    [Fact]
    public async Task Razor_page_protected_by_a_folder_convention_uses_the_folder_as_resource()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser reader = await app.CreateUserWithPermissionsAsync("alice", "folder.read");
        TestUser editor = await app.CreateUserWithPermissionsAsync("bob", "folder.update");

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(reader).GetStatusAsync("/Folder/Page"));
        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(reader).GetStatusAsync("/Folder/Edit"));
        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(editor).GetStatusAsync("/Folder/Edit"));
    }

    [Fact]
    public async Task Razor_page_without_authorization_is_public()
    {
        await using TestApp app = await TestApp.StartAsync();

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient().GetStatusAsync("/Public"));
    }

    [Theory]
    [InlineData("GET", "/api/items", "items.read")]
    [InlineData("POST", "/api/items", "items.create")]
    [InlineData("PUT", "/api/items/1", "items.update")]
    [InlineData("POST", "/api/items/1", "items.update")]
    [InlineData("DELETE", "/api/items/1", "items.delete")]
    [InlineData("POST", "/api/items/1/ship", "items.ship")]
    [InlineData("DELETE", "/api/items", "items.purge")]
    [InlineData("POST", "/api/items/1/restore", "items.update")]
    [InlineData("GET", "/api/named", "named.read")]
    [InlineData("GET", "/api/groups", "groups.read")]
    public async Task Minimal_apis_require_the_permission_of_their_operation(string method, string url, string permission)
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a => a.OperationsImplyRead = false);
        TestUser denied = await app.CreateUserWithPermissionsAsync("denied", "items.other");
        TestUser granted = await app.CreateUserWithPermissionsAsync("granted", permission);

        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(denied).SendStatusAsync(new HttpMethod(method), url));
        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(granted).SendStatusAsync(new HttpMethod(method), url));
    }

    [Theory]
    [InlineData("GET", "/api/products", "products.read")]
    [InlineData("GET", "/api/products/1", "products.read")]
    [InlineData("POST", "/api/products", "products.create")]
    [InlineData("PUT", "/api/products/1", "products.update")]
    [InlineData("DELETE", "/api/products/1", "products.delete")]
    public async Task Api_controllers_require_the_permission_of_their_operation(string method, string url, string permission)
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a => a.OperationsImplyRead = false);
        TestUser granted = await app.CreateUserWithPermissionsAsync("granted", permission);
        TestUser reader = await app.CreateUserWithPermissionsAsync("reader", permission == "products.read" ? "products.create" : "products.read");

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(granted).SendStatusAsync(new HttpMethod(method), url));
        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(reader).SendStatusAsync(new HttpMethod(method), url));
    }

    [Fact]
    public async Task Endpoints_of_the_same_resource_share_its_permissions()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser reader = await app.CreateUserWithPermissionsAsync("alice", "orders.read");
        HttpClient client = app.CreateClient(reader);

        Assert.Equal(HttpStatusCode.OK, await client.GetStatusAsync("/Orders"));
        Assert.Equal(HttpStatusCode.OK, await client.GetStatusAsync("/api/v1/orders"));
        Assert.Equal(HttpStatusCode.OK, await client.GetStatusAsync("/api/legacy-orders"));
    }

    [Fact]
    public async Task Custom_policy_can_require_a_permission()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser denied = await app.CreateUserAsync("alice");
        TestUser granted = await app.CreateUserWithPermissionsAsync("bob", "reports.schedule");

        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(denied).GetStatusAsync("/api/schedule"));
        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(granted).GetStatusAsync("/api/schedule"));
    }

    [Fact]
    public async Task Without_the_default_policy_only_explicitly_dynamic_endpoints_are_permission_checked()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a => a.ApplyToDefaultPolicy = false);
        TestUser user = await app.CreateUserAsync("alice");
        HttpClient client = app.CreateClient(user);

        Assert.Equal(HttpStatusCode.OK, await client.GetStatusAsync("/Orders"));
        Assert.Equal(HttpStatusCode.Forbidden, await client.GetStatusAsync("/api/strict/reports"));
        Assert.Equal(HttpStatusCode.Forbidden, await client.GetStatusAsync("/Orders/Download"));
    }

    [Fact]
    public async Task Revoking_a_permission_takes_effect_immediately()
    {
        await using TestApp app = await TestApp.StartAsync();
        string roleId = await app.CreateRoleAsync("Editors", "orders.read");
        TestUser user = await app.CreateUserAsync("alice", "Editors");
        HttpClient client = app.CreateClient(user);
        Assert.Equal(HttpStatusCode.OK, await client.GetStatusAsync("/Orders"));

        await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().UpdateRoleAsync(roleId, "Editors", []));

        Assert.Equal(HttpStatusCode.Forbidden, await client.GetStatusAsync("/Orders"));
    }

    [Fact]
    public async Task Removing_a_user_from_a_role_takes_effect_immediately()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "orders.read");
        HttpClient client = app.CreateClient(user);
        Assert.Equal(HttpStatusCode.OK, await client.GetStatusAsync("/Orders"));

        ManagementResult result = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().SetUserRolesAsync(user.Id, []));

        Assert.True(result.Succeeded);
        Assert.Equal(HttpStatusCode.Forbidden, await client.GetStatusAsync("/Orders"));
    }

    [Fact]
    public async Task Role_changes_made_outside_the_management_services_apply_once_the_cache_is_invalidated()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "orders.read");
        HttpClient client = app.CreateClient(user);
        Assert.Equal(HttpStatusCode.OK, await client.GetStatusAsync("/Orders"));

        await RemoveFromRoleWithUserManagerAsync(app, user, "alice-role");
        HttpStatusCode cached = await client.GetStatusAsync("/Orders");
        app.Services.GetRequiredService<IDynamicAuthorizationCache>().InvalidateUser(user.Id);

        Assert.Equal(HttpStatusCode.OK, cached);
        Assert.Equal(HttpStatusCode.Forbidden, await client.GetStatusAsync("/Orders"));
    }

    [Fact]
    public async Task Role_changes_apply_immediately_when_caching_is_disabled()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a => a.CacheDuration = TimeSpan.Zero);
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "orders.read");
        HttpClient client = app.CreateClient(user);
        Assert.Equal(HttpStatusCode.OK, await client.GetStatusAsync("/Orders"));

        await RemoveFromRoleWithUserManagerAsync(app, user, "alice-role");

        Assert.Equal(HttpStatusCode.Forbidden, await client.GetStatusAsync("/Orders"));
    }

    [Fact]
    public async Task Permission_checker_denies_anonymous_users_and_users_without_an_identifier()
    {
        await using TestApp app = await TestApp.StartAsync();
        ClaimsPrincipal withoutId = new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "Test"));

        PermissionEvaluation anonymous = await app.WithScopeAsync(s =>
            s.GetRequiredService<IPermissionChecker>().EvaluateAsync(new ClaimsPrincipal(new ClaimsIdentity()), "orders.read"));
        PermissionEvaluation unidentified = await app.WithScopeAsync(s => s.GetRequiredService<IPermissionChecker>().EvaluateAsync(withoutId, "orders.read"));
        bool hasPermission = await app.WithScopeAsync(s => s.GetRequiredService<IPermissionChecker>().HasPermissionAsync(withoutId, "orders.read"));

        Assert.Equal(PermissionDecision.NotAuthenticated, anonymous.Decision);
        Assert.Equal(PermissionDecision.NotGranted, unidentified.Decision);
        Assert.False(hasPermission);
    }

    [Fact]
    public async Task Permission_checker_explains_its_decision()
    {
        await using TestApp app = await TestApp.StartAsync();
        await app.CreateRoleAsync("Editors", "orders.read");
        await app.CreateRoleAsync("Writers", "orders.read", "orders.update");
        await app.CreateRoleAsync("Archivers", "orders.archive");
        TestUser user = await app.CreateUserAsync("alice", "Editors", "Writers");
        TestUser archiver = await app.CreateUserAsync("bob", "Archivers");

        PermissionEvaluation granted = await EvaluateAsync(app, user, "orders.read");
        PermissionEvaluation denied = await EvaluateAsync(app, user, "orders.delete");
        PermissionEvaluation implied = await EvaluateAsync(app, archiver, "orders.read");
        PermissionEvaluation superAdmin = await app.WithScopeAsync(s =>
            s.GetRequiredService<IPermissionChecker>().EvaluateUserAsync("unknown", TestApp.SuperAdminUserName, "anything"));

        Assert.Equal(PermissionDecision.GrantedByRole, granted.Decision);
        Assert.Equal(["Editors", "Writers"], granted.GrantingRoles.Select(r => r.Name).Order());
        Assert.Null(granted.ImpliedBy);
        Assert.Equal(PermissionDecision.NotGranted, denied.Decision);
        Assert.Equal(PermissionDecision.GrantedByRole, implied.Decision);
        Assert.Equal("orders.archive", implied.ImpliedBy);
        Assert.Equal(PermissionDecision.SuperAdminUser, superAdmin.Decision);
    }

    private static Task<bool> RemoveFromRoleWithUserManagerAsync(TestApp app, TestUser user, string role) =>
        app.WithScopeAsync(async s =>
        {
            UserManager<IdentityUser> users = s.GetRequiredService<UserManager<IdentityUser>>();
            IdentityResult result = await users.RemoveFromRoleAsync((await users.FindByIdAsync(user.Id))!, role);
            Assert.True(result.Succeeded);
            return result.Succeeded;
        });

    private static Task<PermissionEvaluation> EvaluateAsync(TestApp app, TestUser user, string permission) =>
        app.WithScopeAsync(s => s.GetRequiredService<IPermissionChecker>().EvaluateUserAsync(user.Id, user.UserName, permission));
}
