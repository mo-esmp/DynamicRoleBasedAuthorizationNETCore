using DynamicAuthorization.AspNetCore.Tests.Infrastructure;

namespace DynamicAuthorization.AspNetCore.Tests;

public sealed class DiscoveryTests
{
    [Fact]
    public async Task Catalog_contains_protected_mvc_actions()
    {
        await using TestApp app = await TestApp.StartAsync();

        PermissionCatalog catalog = app.GetCatalog();

        Assert.NotNull(catalog.FindPermission("mvc::Orders:Index"));
        Assert.NotNull(catalog.FindPermission("mvc::Orders:Edit"));
        Assert.NotNull(catalog.FindPermission("mvc::Reports:Export"));
        Assert.NotNull(catalog.FindPermission("mvc::Inherited:Index"));
        Assert.NotNull(catalog.FindPermission("mvc:Admin:Settings:Index"));
        Assert.Null(catalog.FindPermission("mvc::Orders:Public"));
        Assert.Null(catalog.FindPermission("mvc::Orders:Mine"));
        Assert.Null(catalog.FindPermission("mvc::Reports:Index"));
        Assert.Null(catalog.FindPermission("mvc::Managers:Index"));
    }

    [Fact]
    public async Task Explicit_permission_replaces_the_derived_key()
    {
        await using TestApp app = await TestApp.StartAsync();

        PermissionCatalog catalog = app.GetCatalog();

        PermissionDescriptor permission = Assert.IsType<PermissionDescriptor>(catalog.FindPermission("orders.delete"));
        Assert.Equal("Delete orders", permission.DisplayName);
        Assert.Equal("Orders", permission.Group);
        Assert.Null(catalog.FindPermission("mvc::Orders:Delete"));
    }

    [Fact]
    public async Task Catalog_contains_protected_razor_pages()
    {
        await using TestApp app = await TestApp.StartAsync();

        PermissionCatalog catalog = app.GetCatalog();

        PermissionDescriptor secure = Assert.IsType<PermissionDescriptor>(catalog.FindPermission("page::/Secure/Index"));
        Assert.Equal("Secure home", secure.DisplayName);
        Assert.Equal("Secure area", secure.Group);
        Assert.Equal(PermissionSource.Page, secure.Source);
        PermissionDescriptor folder = Assert.IsType<PermissionDescriptor>(catalog.FindPermission("page::/Folder/Page"));
        Assert.Equal("Folder", folder.Group);
        Assert.Null(catalog.FindPermission("page::/Public"));
    }

    [Fact]
    public async Task Catalog_contains_protected_minimal_apis()
    {
        await using TestApp app = await TestApp.StartAsync();

        PermissionCatalog catalog = app.GetCatalog();

        Assert.NotNull(catalog.FindPermission("endpoint:GET /api/items"));
        Assert.NotNull(catalog.FindPermission("endpoint:GetNamed"));
        PermissionDescriptor delete = Assert.IsType<PermissionDescriptor>(catalog.FindPermission("items.delete"));
        Assert.Equal("Delete items", delete.DisplayName);
        Assert.Equal("Items", delete.Group);
        Assert.Null(catalog.FindPermission("endpoint:GET /api/public"));
    }

    [Fact]
    public async Task Custom_permissions_are_listed()
    {
        await using TestApp app = await TestApp.StartAsync();

        PermissionDescriptor permission = Assert.IsType<PermissionDescriptor>(app.GetCatalog().FindPermission("reports.schedule"));

        Assert.Equal(PermissionSource.Custom, permission.Source);
        Assert.Equal("Schedule reports", permission.DisplayName);
        Assert.Equal("Reports", permission.Group);
    }

    [Fact]
    public async Task Display_names_and_groups_come_from_attributes()
    {
        await using TestApp app = await TestApp.StartAsync();

        PermissionCatalog catalog = app.GetCatalog();

        Assert.Equal("Export reports", catalog.FindPermission("mvc::Reports:Export")!.DisplayName);
        Assert.Equal("Reports", catalog.FindPermission("mvc::Reports:Export")!.Group);
        Assert.Equal("Orders", catalog.FindPermission("mvc::Orders:Index")!.Group);
        Assert.Equal("Inherited", catalog.FindPermission("mvc::Inherited:Index")!.Group);
        Assert.Equal("Admin / Settings", catalog.FindPermission("mvc:Admin:Settings:Index")!.Group);
    }

    [Theory]
    [InlineData("mvc::Orders:Index", EndpointProtection.DynamicPermission)]
    [InlineData("mvc::Orders:Public", EndpointProtection.AllowAnonymous)]
    [InlineData("mvc::Orders:Mine", EndpointProtection.Excluded)]
    [InlineData("mvc::Reports:Index", EndpointProtection.None)]
    [InlineData("mvc::Managers:Index", EndpointProtection.PolicyOnly)]
    [InlineData("page::/Public", EndpointProtection.None)]
    [InlineData("endpoint:GET /api/schedule", EndpointProtection.PolicyOnly)]
    public async Task Catalog_reports_how_endpoints_are_protected(string targetKey, EndpointProtection expected)
    {
        await using TestApp app = await TestApp.StartAsync();

        EndpointAuthorizationInfo endpoint = Assert.IsType<EndpointAuthorizationInfo>(app.GetCatalog().FindEndpoint(targetKey));

        Assert.Equal(expected, endpoint.Protection);
    }

    [Fact]
    public async Task Pages_reachable_through_several_routes_are_listed_once()
    {
        await using TestApp app = await TestApp.StartAsync();

        EndpointAuthorizationInfo[] endpoints = [.. app.GetCatalog().Endpoints.Where(e => e.TargetKey == "page::/Secure/Index")];

        EndpointAuthorizationInfo endpoint = Assert.Single(endpoints);
        Assert.Equal("/Secure", endpoint.Route);
    }

    [Fact]
    public async Task Actions_with_several_http_methods_are_listed_per_method()
    {
        await using TestApp app = await TestApp.StartAsync();

        EndpointAuthorizationInfo[] endpoints = [.. app.GetCatalog().Endpoints.Where(e => e.TargetKey == "mvc::Orders:Edit")];

        Assert.Equal(2, endpoints.Length);
        Assert.Contains(endpoints, e => e.HttpMethods.SequenceEqual(["GET"]));
        Assert.Contains(endpoints, e => e.HttpMethods.SequenceEqual(["POST"]));
    }

    [Fact]
    public async Task Group_root_endpoints_have_no_trailing_slash_in_their_key()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "endpoint:GET /api/groups");

        HttpResponseMessage response = await app.CreateClient(user).GetAsync("/api/groups");

        Assert.NotNull(app.GetCatalog().FindPermission("endpoint:GET /api/groups"));
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Conventional_route_templates_are_not_listed_as_endpoints()
    {
        await using TestApp app = await TestApp.StartAsync();

        Assert.DoesNotContain(app.GetCatalog().Endpoints, e => e.Route.Contains("{controller", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, "Orders", "Edit", "mvc::Orders:Edit")]
    [InlineData("Admin", "Settings", "Index", "mvc:Admin:Settings:Index")]
    public void Action_keys_include_the_area(string? area, string controller, string action, string expected) =>
        Assert.Equal(expected, PermissionKeys.ForAction(area, controller, action));

    [Fact]
    public void Route_keys_are_normalized() =>
        Assert.Equal("endpoint:GET,POST /api/orders/{id}", PermissionKeys.ForRoute(["post", "GET"], "api/orders/{id}"));
}
