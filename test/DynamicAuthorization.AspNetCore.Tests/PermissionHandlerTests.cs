using System.Security.Claims;
using DynamicAuthorization.AspNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.SignalR;

namespace DynamicAuthorization.AspNetCore.Tests;

/// <summary>
///   The permission resolved for each kind of authorization resource, when the requirement names no permission.
/// </summary>
public sealed class PermissionHandlerTests
{
    [Fact]
    public async Task String_resources_are_checked_as_permissions()
    {
        await using TestApp app = await TestApp.StartAsync();
        (ClaimsPrincipal granted, ClaimsPrincipal denied) = await CreateUsersAsync(app);

        Assert.True(await AuthorizeAsync(app, granted, "orders.read"));
        Assert.False(await AuthorizeAsync(app, denied, "orders.read"));
    }

    [Fact]
    public async Task Endpoint_resources_require_the_permission_to_navigate_to_them()
    {
        await using TestApp app = await TestApp.StartAsync();
        (ClaimsPrincipal granted, ClaimsPrincipal denied) = await CreateUsersAsync(app);
        Endpoint endpoint = app.GetCatalog().FindEndpoint("mvc::Orders:Index")!.Endpoint;

        Assert.True(await AuthorizeAsync(app, granted, endpoint));
        Assert.False(await AuthorizeAsync(app, denied, endpoint));
    }

    [Fact]
    public async Task Mvc_filter_contexts_are_checked_with_the_permission_of_their_endpoint()
    {
        await using TestApp app = await TestApp.StartAsync();
        (ClaimsPrincipal granted, ClaimsPrincipal denied) = await CreateUsersAsync(app);
        Endpoint endpoint = app.GetCatalog().FindEndpoint("mvc::Orders:Index")!.Endpoint;

        Assert.True(await AuthorizeAsync(app, granted, CreateFilterContext(endpoint)));
        Assert.False(await AuthorizeAsync(app, denied, CreateFilterContext(endpoint)));
        Assert.False(await AuthorizeAsync(app, granted, CreateFilterContext(endpoint: null)));
    }

    [Fact]
    public async Task Requests_without_an_endpoint_are_denied()
    {
        await using TestApp app = await TestApp.StartAsync();
        (ClaimsPrincipal granted, _) = await CreateUsersAsync(app);

        Assert.False(await AuthorizeAsync(app, granted, new DefaultHttpContext()));
    }

    [Fact]
    public async Task Excluded_endpoints_and_calls_without_a_resource_are_not_permission_checked()
    {
        await using TestApp app = await TestApp.StartAsync();
        (_, ClaimsPrincipal denied) = await CreateUsersAsync(app);
        EndpointAuthorizationInfo excluded = app.GetCatalog().FindEndpoint("mvc::Orders:Mine")!;

        Assert.True(await AuthorizeAsync(app, denied, excluded));
        Assert.True(await AuthorizeAsync(app, denied, resource: null));
        Assert.True(await AuthorizeAsync(app, denied, new object()));
    }

    [Fact]
    public async Task Hub_methods_are_checked_only_when_they_declare_a_permission()
    {
        await using TestApp app = await TestApp.StartAsync();
        (ClaimsPrincipal granted, ClaimsPrincipal denied) = await CreateUsersAsync(app);

        Assert.True(await AuthorizeAsync(app, granted, CreateHubInvocation(app, granted, nameof(ChatHub.Send))));
        Assert.False(await AuthorizeAsync(app, denied, CreateHubInvocation(app, denied, nameof(ChatHub.Send))));
        Assert.True(await AuthorizeAsync(app, denied, CreateHubInvocation(app, denied, nameof(ChatHub.Read))));
    }

    private static async Task<(ClaimsPrincipal Granted, ClaimsPrincipal Denied)> CreateUsersAsync(TestApp app)
    {
        TestUser granted = await app.CreateUserWithPermissionsAsync("alice", "orders.read", "chat.send");
        TestUser denied = await app.CreateUserAsync("bob");

        return (await app.CreatePrincipalAsync(granted), await app.CreatePrincipalAsync(denied));
    }

    private static Task<bool> AuthorizeAsync(TestApp app, ClaimsPrincipal user, object? resource) =>
        app.WithScopeAsync(async s =>
            (await s.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, resource, [new DynamicPermissionRequirement()])).Succeeded);

    private static AuthorizationFilterContext CreateFilterContext(Endpoint? endpoint)
    {
        DefaultHttpContext httpContext = new();
        httpContext.SetEndpoint(endpoint);

        return new AuthorizationFilterContext(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()), []);
    }

    private static HubInvocationContext CreateHubInvocation(TestApp app, ClaimsPrincipal user, string method) =>
        new(new TestHubCallerContext(user), app.Services, new ChatHub(), typeof(ChatHub).GetMethod(method)!, []);

    public sealed class ChatHub : Hub
    {
        [Permission("chat.send")]
        public void Send()
        {
        }

        public void Read()
        {
        }
    }

    private sealed class TestHubCallerContext(ClaimsPrincipal user) : HubCallerContext
    {
        public override string ConnectionId => "connection";

        public override string? UserIdentifier => null;

        public override ClaimsPrincipal User => user;

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override IFeatureCollection Features { get; } = new FeatureCollection();

        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort()
        {
        }
    }
}
