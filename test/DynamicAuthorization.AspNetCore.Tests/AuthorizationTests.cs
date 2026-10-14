using System.Net;
using DynamicAuthorization.AspNetCore.Management;
using DynamicAuthorization.AspNetCore.Tests.Infrastructure;

namespace DynamicAuthorization.AspNetCore.Tests;

public sealed class AuthorizationTests
{
    [Fact]
    public async Task Anonymous_request_to_a_protected_action_is_challenged()
    {
        await using TestApp app = await TestApp.StartAsync();

        HttpResponseMessage response = await app.CreateClient().GetAsync("/Orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task User_without_the_permission_is_forbidden()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserAsync("alice");

        HttpResponseMessage response = await app.CreateClient(user).GetAsync("/Orders");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task User_whose_role_grants_the_permission_can_access_the_action()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "mvc::Orders:Index");

        HttpResponseMessage response = await app.CreateClient(user).GetAsync("/Orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("orders", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Permission_keys_are_case_insensitive()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "MVC::orders:INDEX");

        HttpResponseMessage response = await app.CreateClient(user).GetAsync("/Orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task One_permission_covers_every_http_method_of_an_action()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "mvc::Orders:Edit");
        HttpClient client = app.CreateClient(user);

        HttpResponseMessage get = await client.GetAsync("/Orders/Edit/1");
        HttpResponseMessage post = await client.PostAsync("/Orders/Edit/1", null);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
    }

    [Fact]
    public async Task Super_admin_user_can_access_everything()
    {
        await using TestApp app = await TestApp.StartAsync();
        HttpClient client = app.CreateClient(await app.CreateSuperAdminAsync());

        foreach (string url in new[] { "/Orders", "/Orders/Delete/1", "/Secure", "/Folder/Page", "/Admin/Settings", "/api/items" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Super_admin_user_names_are_compared_case_insensitively()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserAsync("ROOT");

        HttpResponseMessage response = await app.CreateClient(user).GetAsync("/Orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Members_of_a_super_admin_role_can_access_everything()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a => a.SuperAdminRoles.Add("Administrators"));
        TestUser user = await app.CreateUserAsync("alice", "Administrators");

        HttpResponseMessage response = await app.CreateClient(user).GetAsync("/Secure");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AllowAnonymous_action_is_public()
    {
        await using TestApp app = await TestApp.StartAsync();

        HttpResponseMessage response = await app.CreateClient().GetAsync("/Orders/Public");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Explicit_permission_attribute_replaces_the_derived_permission()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser derived = await app.CreateUserWithPermissionsAsync("alice", "mvc::Orders:Delete");
        TestUser explicitPermission = await app.CreateUserWithPermissionsAsync("bob", "orders.delete");

        Assert.Equal(HttpStatusCode.Forbidden, (await app.CreateClient(derived).GetAsync("/Orders/Delete/1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient(explicitPermission).GetAsync("/Orders/Delete/1")).StatusCode);
    }

    [Fact]
    public async Task Ignored_action_only_requires_a_signed_in_user()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserAsync("alice");

        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/Orders/Mine")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient(user).GetAsync("/Orders/Mine")).StatusCode);
    }

    [Fact]
    public async Task Ignored_areas_only_require_a_signed_in_user()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a => a.IgnoredAreas.Add("Admin"));
        TestUser user = await app.CreateUserAsync("alice");

        HttpResponseMessage response = await app.CreateClient(user).GetAsync("/Admin/Settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Role_based_authorize_attribute_is_not_permission_checked()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser manager = await app.CreateUserAsync("alice", "Manager");
        TestUser other = await app.CreateUserWithPermissionsAsync("bob", "mvc::Managers:Index");

        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient(manager).GetAsync("/Managers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.CreateClient(other).GetAsync("/Managers")).StatusCode);
    }

    [Fact]
    public async Task Inherited_and_repeated_authorize_attributes_are_supported()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser denied = await app.CreateUserAsync("alice");
        TestUser granted = await app.CreateUserWithPermissionsAsync("bob", "mvc::Inherited:Index");

        Assert.Equal(HttpStatusCode.Forbidden, (await app.CreateClient(denied).GetAsync("/Inherited")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient(granted).GetAsync("/Inherited")).StatusCode);
    }

    [Fact]
    public async Task Area_action_requires_the_area_permission()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser withoutArea = await app.CreateUserWithPermissionsAsync("alice", "mvc::Settings:Index");
        TestUser withArea = await app.CreateUserWithPermissionsAsync("bob", "mvc:Admin:Settings:Index");

        Assert.Equal(HttpStatusCode.Forbidden, (await app.CreateClient(withoutArea).GetAsync("/Admin/Settings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient(withArea).GetAsync("/Admin/Settings")).StatusCode);
    }

    [Fact]
    public async Task Razor_page_with_authorize_attribute_requires_its_permission()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser denied = await app.CreateUserAsync("alice");
        TestUser granted = await app.CreateUserWithPermissionsAsync("bob", "page::/Secure/Index");

        Assert.Equal(HttpStatusCode.Forbidden, (await app.CreateClient(denied).GetAsync("/Secure")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient(granted).GetAsync("/Secure")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient(granted).GetAsync("/Secure/Index")).StatusCode);
    }

    [Fact]
    public async Task Razor_page_protected_by_a_folder_convention_requires_its_permission()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser denied = await app.CreateUserAsync("alice");
        TestUser granted = await app.CreateUserWithPermissionsAsync("bob", "page::/Folder/Page");

        Assert.Equal(HttpStatusCode.Forbidden, (await app.CreateClient(denied).GetAsync("/Folder/Page")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient(granted).GetAsync("/Folder/Page")).StatusCode);
    }

    [Fact]
    public async Task Razor_page_without_authorization_is_public()
    {
        await using TestApp app = await TestApp.StartAsync();

        HttpResponseMessage response = await app.CreateClient().GetAsync("/Public");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Minimal_api_with_RequireAuthorization_requires_its_route_permission()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser denied = await app.CreateUserAsync("alice");
        TestUser granted = await app.CreateUserWithPermissionsAsync("bob", "endpoint:GET /api/items");

        Assert.Equal(HttpStatusCode.Forbidden, (await app.CreateClient(denied).GetAsync("/api/items")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient(granted).GetAsync("/api/items")).StatusCode);
    }

    [Fact]
    public async Task Minimal_api_with_RequirePermission_requires_the_explicit_permission()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser granted = await app.CreateUserWithPermissionsAsync("alice", "items.delete");

        HttpResponseMessage response = await app.CreateClient(granted).DeleteAsync("/api/items/5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("deleted 5", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Named_minimal_api_uses_its_endpoint_name_as_permission()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser granted = await app.CreateUserWithPermissionsAsync("alice", "endpoint:GetNamed");

        HttpResponseMessage response = await app.CreateClient(granted).GetAsync("/api/named");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Custom_policy_can_require_a_permission()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser denied = await app.CreateUserAsync("alice");
        TestUser granted = await app.CreateUserWithPermissionsAsync("bob", "reports.schedule");

        Assert.Equal(HttpStatusCode.Forbidden, (await app.CreateClient(denied).GetAsync("/api/schedule")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient(granted).GetAsync("/api/schedule")).StatusCode);
    }

    [Fact]
    public async Task Without_the_default_policy_only_explicitly_dynamic_endpoints_are_permission_checked()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a => a.ApplyToDefaultPolicy = false);
        TestUser user = await app.CreateUserAsync("alice");
        HttpClient client = app.CreateClient(user);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/strict/reports")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Orders/Delete/1")).StatusCode);
    }

    [Fact]
    public async Task Revoking_a_permission_takes_effect_immediately()
    {
        await using TestApp app = await TestApp.StartAsync();
        string roleId = await app.CreateRoleAsync("Editors", "mvc::Orders:Index");
        TestUser user = await app.CreateUserAsync("alice", "Editors");
        HttpClient client = app.CreateClient(user);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Orders")).StatusCode);

        await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().UpdateRoleAsync(roleId, "Editors", []));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Orders")).StatusCode);
    }

    [Fact]
    public async Task Removing_a_user_from_a_role_takes_effect_immediately()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "mvc::Orders:Index");
        HttpClient client = app.CreateClient(user);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Orders")).StatusCode);

        ManagementResult result = await app.WithScopeAsync(s => s.GetRequiredService<IUserManagementService>().SetUserRolesAsync(user.Id, []));

        Assert.True(result.Succeeded);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Orders")).StatusCode);
    }

    [Fact]
    public async Task Permission_checker_explains_its_decision()
    {
        await using TestApp app = await TestApp.StartAsync();
        await app.CreateRoleAsync("Editors", "mvc::Orders:Index");
        await app.CreateRoleAsync("Writers", "mvc::Orders:Index", "mvc::Orders:Edit");
        TestUser user = await app.CreateUserAsync("alice", "Editors", "Writers");

        PermissionEvaluation granted = await app.WithScopeAsync(s => s.GetRequiredService<IPermissionChecker>().EvaluateUserAsync(user.Id, user.UserName, "mvc::Orders:Index"));
        PermissionEvaluation denied = await app.WithScopeAsync(s => s.GetRequiredService<IPermissionChecker>().EvaluateUserAsync(user.Id, user.UserName, "mvc::Orders:Delete"));
        PermissionEvaluation superAdmin = await app.WithScopeAsync(s => s.GetRequiredService<IPermissionChecker>().EvaluateUserAsync("unknown", TestApp.SuperAdminUserName, "anything"));

        Assert.Equal(PermissionDecision.GrantedByRole, granted.Decision);
        Assert.Equal(["Editors", "Writers"], granted.GrantingRoles.Select(r => r.Name).Order());
        Assert.Equal(PermissionDecision.NotGranted, denied.Decision);
        Assert.Equal(PermissionDecision.SuperAdminUser, superAdmin.Decision);
    }
}
