using DynamicAuthorization.AspNetCore.Tests.Infrastructure;

namespace DynamicAuthorization.AspNetCore.Tests;

public sealed class DiscoveryTests
{
    [Fact]
    public async Task Catalog_lists_one_permission_per_resource_and_operation()
    {
        await using TestApp app = await TestApp.StartAsync();

        PermissionCatalog catalog = app.GetCatalog();

        string[] orders = catalog.Permissions.Where(p => p.Resource == "orders").Select(p => p.Permission).ToArray();
        Assert.Equal(
            ["orders.read", "orders.create", "orders.update", "orders.delete", "orders.archive", "orders.export", "orders.report"],
            orders);
        Assert.Null(catalog.FindPermission("mvc::Orders:Index"));
        Assert.Null(catalog.FindPermission("orders.public"));
        Assert.Null(catalog.FindPermission("orders.mine"));
    }

    [Fact]
    public async Task Permissions_describe_their_resource_and_operation()
    {
        await using TestApp app = await TestApp.StartAsync();

        PermissionCatalog catalog = app.GetCatalog();

        PermissionDescriptor read = Assert.IsType<PermissionDescriptor>(catalog.FindPermission("orders.read"));
        Assert.Equal(("orders", "read", "Read", "Orders"), (read.Resource, read.Operation, read.DisplayName, read.Group));
        Assert.Equal("Archive", catalog.FindPermission("orders.archive")!.DisplayName);
        Assert.Equal("Export orders", catalog.FindPermission("orders.export")!.DisplayName);
        Assert.Equal("Export reports", catalog.FindPermission("reports.export")!.DisplayName);
    }

    [Theory]
    [InlineData("legacy.read", "Legacy")]
    [InlineData("admin.settings.read", "Admin / Settings")]
    [InlineData("secure.read", "Secure area")]
    [InlineData("folder.read", "Folder")]
    [InlineData("folder.update", "Folder")]
    [InlineData("products.create", "Products")]
    [InlineData("reports.schedule", "Reports")]
    public async Task Permissions_are_grouped_by_resource(string permission, string group)
    {
        await using TestApp app = await TestApp.StartAsync();

        Assert.Equal(group, app.GetCatalog().FindPermission(permission)?.Group);
    }

    [Fact]
    public async Task Controller_names_win_over_api_tags_for_shared_resources()
    {
        await using TestApp app = await TestApp.StartAsync();

        PermissionCatalog catalog = app.GetCatalog();

        Assert.All(catalog.GetEndpoints("orders.read"), e => Assert.Equal("Orders", e.Permission!.Group));
        Assert.Contains(catalog.GetEndpoints("orders.read"), e => e.Route == "/api/v1/orders");
        Assert.Contains(catalog.GetEndpoints("orders.read"), e => e.Route == "/api/legacy-orders");
        Assert.Equal("Items API", catalog.FindPermission("items.read")!.Group);
    }

    [Fact]
    public async Task Razor_page_handlers_are_listed_with_their_own_permissions()
    {
        await using TestApp app = await TestApp.StartAsync();

        EndpointAuthorizationInfo[] entries = app.GetCatalog().Endpoints.Where(e => e.TargetKey == "page::/Secure/Index").ToArray();

        Assert.Equal(
            ["GET  secure.read", "POST  secure.update", "POST Archive secure.archive", "POST Delete secure.delete"],
            entries.Select(e => $"{e.HttpMethods[0]} {e.Handler} {e.Permission!.Permission}").Order());
        Assert.All(entries, e => Assert.Equal("/Secure", e.Route));
    }

    [Theory]
    [InlineData("items.read")]
    [InlineData("items.create")]
    [InlineData("items.update")]
    [InlineData("items.delete")]
    [InlineData("items.ship")]
    [InlineData("items.purge")]
    [InlineData("named.read")]
    [InlineData("strict.read")]
    [InlineData("groups.read")]
    [InlineData("products.read")]
    [InlineData("products.create")]
    [InlineData("products.update")]
    [InlineData("products.delete")]
    public async Task Catalog_contains_api_permissions(string permission)
    {
        await using TestApp app = await TestApp.StartAsync();

        Assert.NotNull(app.GetCatalog().FindPermission(permission));
    }

    [Fact]
    public async Task Custom_permissions_are_listed_with_their_resource()
    {
        await using TestApp app = await TestApp.StartAsync();

        PermissionDescriptor permission = Assert.IsType<PermissionDescriptor>(app.GetCatalog().FindPermission("reports.schedule"));

        Assert.Equal(PermissionSource.Custom, permission.Source);
        Assert.Equal(("reports", "schedule", "Schedule reports"), (permission.Resource, permission.Operation, permission.DisplayName));
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
    public async Task Actions_with_several_http_methods_are_listed_per_method()
    {
        await using TestApp app = await TestApp.StartAsync();

        EndpointAuthorizationInfo[] endpoints = app.GetCatalog().Endpoints.Where(e => e.TargetKey == "mvc::Orders:Edit").ToArray();

        Assert.Equal(2, endpoints.Length);
        Assert.Contains(endpoints, e => e.HttpMethods.SequenceEqual(["GET"]));
        Assert.Contains(endpoints, e => e.HttpMethods.SequenceEqual(["POST"]));
        Assert.All(endpoints, e => Assert.Equal("orders.update", e.Permission?.Permission));
    }

    [Fact]
    public async Task Group_root_endpoints_have_no_trailing_slash_in_their_route()
    {
        await using TestApp app = await TestApp.StartAsync();

        Assert.Contains(app.GetCatalog().Endpoints, e => e.Route == "/api/groups" && e.Permission?.Permission == "groups.read");
    }

    [Fact]
    public async Task Conventional_route_templates_are_not_listed_as_endpoints()
    {
        await using TestApp app = await TestApp.StartAsync();

        Assert.DoesNotContain(app.GetCatalog().Endpoints, e => e.Route.Contains("{controller", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Orders", "orders")]
    [InlineData("OrderItems", "order-items")]
    [InlineData("APIKeys", "api-keys")]
    [InlineData("Orders API", "orders-api")]
    [InlineData("DynamicAuthorization", "dynamic-authorization")]
    [InlineData("v2", "v2")]
    public void Names_are_converted_to_key_segments(string name, string expected) =>
        Assert.Equal(expected, PermissionKeys.ToSegment(name));

    [Theory]
    [InlineData(null, "Orders", "Edit", "mvc::Orders:Edit")]
    [InlineData("Admin", "Settings", "Index", "mvc:Admin:Settings:Index")]
    public void Action_keys_include_the_area(string? area, string controller, string action, string expected) =>
        Assert.Equal(expected, PermissionKeys.ForAction(area, controller, action));

    [Fact]
    public void Route_keys_are_normalized() =>
        Assert.Equal("endpoint:GET,POST /api/orders/{id}", PermissionKeys.ForRoute(["post", "GET"], "api/orders/{id}"));
}
