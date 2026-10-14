using System.Net;
using DynamicAuthorization.AspNetCore.Tests.Infrastructure;

namespace DynamicAuthorization.AspNetCore.Tests;

/// <summary>
///   The <see cref="PermissionConvention.Endpoint"/> convention: one permission per action, page or route.
/// </summary>
public sealed class EndpointConventionTests
{
    private static Task<TestApp> StartAsync() =>
        TestApp.StartAsync(o => o.Authorization = a => a.Convention = PermissionConvention.Endpoint);

    [Fact]
    public async Task Catalog_lists_one_permission_per_endpoint()
    {
        await using TestApp app = await StartAsync();

        PermissionCatalog catalog = app.GetCatalog();

        foreach (string permission in new[] { "mvc::Orders:Index", "mvc::Orders:Details", "mvc::Orders:Edit", "mvc:Admin:Settings:Index", "page::/Secure/Index", "page::/Folder/Page", "endpoint:GET /api/items", "endpoint:GetNamed", "orders.export" })
        {
            Assert.NotNull(catalog.FindPermission(permission));
        }

        Assert.Null(catalog.FindPermission("orders.read"));
        Assert.Equal("Secure home", catalog.FindPermission("page::/Secure/Index")!.DisplayName);
        Assert.Equal("Secure area", catalog.FindPermission("page::/Secure/Index")!.Group);
    }

    [Fact]
    public async Task Each_action_requires_its_own_permission()
    {
        await using TestApp app = await StartAsync();
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "mvc::Orders:Index");
        HttpClient client = app.CreateClient(user);

        Assert.Equal(HttpStatusCode.OK, await client.GetStatusAsync("/Orders"));
        Assert.Equal(HttpStatusCode.Forbidden, await client.GetStatusAsync("/Orders/Details/1"));
    }

    [Fact]
    public async Task One_permission_covers_every_handler_of_a_page()
    {
        await using TestApp app = await StartAsync();
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "page::/Secure/Index");
        HttpClient client = app.CreateClient(user);

        Assert.Equal(HttpStatusCode.OK, await client.GetStatusAsync("/Secure"));
        Assert.Equal(HttpStatusCode.OK, await client.SendStatusAsync(HttpMethod.Post, "/Secure?handler=Delete"));
    }

    [Fact]
    public async Task Routes_and_explicit_permissions_are_supported()
    {
        await using TestApp app = await StartAsync();
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "endpoint:GET /api/items", "endpoint:GetNamed", "orders.export");
        HttpClient client = app.CreateClient(user);

        Assert.Equal(HttpStatusCode.OK, await client.GetStatusAsync("/api/items"));
        Assert.Equal(HttpStatusCode.OK, await client.GetStatusAsync("/api/named"));
        Assert.Equal(HttpStatusCode.OK, await client.GetStatusAsync("/Orders/Download"));
        Assert.Equal(HttpStatusCode.Forbidden, await client.SendStatusAsync(HttpMethod.Post, "/api/items"));
    }
}
