using System.Net;
using DynamicAuthorization.AspNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore.Tests;

/// <summary>
///   Endpoints are permission-checked exactly when the policy the authorization middleware evaluates for them contains
///   the dynamic permission requirement, whether it comes from the default policy, a named policy or an inline policy.
///   The catalog must agree with the runtime: a listed permission is always checked, and a checked permission is
///   always listed.
/// </summary>
public sealed class AuthorizationPolicyTests
{
    private static readonly AuthorizationPolicy s_dynamicPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new DynamicPermissionRequirement())
        .Build();

    private static readonly AuthorizationPolicy s_authenticatedPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Inline_policy_with_the_dynamic_requirement_is_permission_checked(bool applyToDefaultPolicy)
    {
        await using TestApp app = await TestApp.StartAsync(o =>
        {
            o.Authorization = a => a.ApplyToDefaultPolicy = applyToDefaultPolicy;
            o.Endpoints = e => e.MapGet("/api/inline", () => "inline").RequireAuthorization(s_dynamicPolicy);
        });
        TestUser granted = await app.CreateUserWithPermissionsAsync("alice", "inline.read");
        TestUser denied = await app.CreateUserWithPermissionsAsync("bob", "other.read");

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(granted).GetStatusAsync("/api/inline"));
        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(denied).GetStatusAsync("/api/inline"));
        Assert.Equal(EndpointProtection.DynamicPermission, app.GetCatalog().FindEndpoint("endpoint:GET /api/inline")?.Protection);
        Assert.NotNull(app.GetCatalog().FindPermission("inline.read"));
    }

    /// <summary>
    ///   RequireAuthorization(policy) adds an [Authorize] marker, but the middleware does not combine the default policy
    ///   with an inline policy. The catalog must not list a permission that is never checked.
    /// </summary>
    [Fact]
    public async Task Inline_policy_without_the_dynamic_requirement_replaces_the_default_policy()
    {
        await using TestApp app = await TestApp.StartAsync(o =>
            o.Endpoints = e => e.MapGet("/api/inline", () => "inline").RequireAuthorization(s_authenticatedPolicy));
        TestUser user = await app.CreateUserWithPermissionsAsync("alice", "other.read");

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(user).GetStatusAsync("/api/inline"));
        Assert.Equal(HttpStatusCode.Unauthorized, await app.CreateClient().GetStatusAsync("/api/inline"));
        Assert.Equal(EndpointProtection.PolicyOnly, app.GetCatalog().FindEndpoint("endpoint:GET /api/inline")?.Protection);
        Assert.Null(app.GetCatalog().FindPermission("inline.read"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Named_policy_with_the_dynamic_requirement_is_permission_checked(bool applyToDefaultPolicy)
    {
        await using TestApp app = await TestApp.StartAsync(o =>
        {
            o.Authorization = a => a.ApplyToDefaultPolicy = applyToDefaultPolicy;
            o.Services = s => s.AddAuthorizationBuilder()
                .AddPolicy("ManagersWithPermission", p => p.RequireRole("Manager").AddRequirements(new DynamicPermissionRequirement()));
            o.Endpoints = e => e.MapGet("/api/managed", () => "managed").RequireAuthorization("ManagersWithPermission");
        });
        await app.CreateRoleAsync("Readers", "managed.read");
        TestUser granted = await app.CreateUserAsync("alice", "Manager", "Readers");
        TestUser managerOnly = await app.CreateUserAsync("bob", "Manager");
        TestUser readerOnly = await app.CreateUserAsync("carol", "Readers");

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(granted).GetStatusAsync("/api/managed"));
        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(managerOnly).GetStatusAsync("/api/managed"));
        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(readerOnly).GetStatusAsync("/api/managed"));
        Assert.Equal(EndpointProtection.DynamicPermission, app.GetCatalog().FindEndpoint("endpoint:GET /api/managed")?.Protection);
        Assert.NotNull(app.GetCatalog().FindPermission("managed.read"));
    }

    [Fact]
    public async Task Named_policy_without_the_dynamic_requirement_is_not_permission_checked()
    {
        await using TestApp app = await TestApp.StartAsync(o =>
        {
            o.Services = s => s.AddAuthorizationBuilder().AddPolicy("Managers", p => p.RequireRole("Manager"));
            o.Endpoints = e => e.MapGet("/api/managed", () => "managed").RequireAuthorization("Managers");
        });
        TestUser manager = await app.CreateUserAsync("alice", "Manager");

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(manager).GetStatusAsync("/api/managed"));
        Assert.Equal(EndpointProtection.PolicyOnly, app.GetCatalog().FindEndpoint("endpoint:GET /api/managed")?.Protection);
        Assert.Null(app.GetCatalog().FindPermission("managed.read"));
    }

    /// <summary>
    ///   Adding the requirement to the default policy by hand is equivalent to <c>ApplyToDefaultPolicy</c>.
    /// </summary>
    [Fact]
    public async Task Default_policy_configured_by_the_application_is_recognized()
    {
        await using TestApp app = await TestApp.StartAsync(o =>
        {
            o.Authorization = a => a.ApplyToDefaultPolicy = false;
            o.Services = s => s.AddAuthorizationBuilder().SetDefaultPolicy(s_dynamicPolicy);
        });
        TestUser reader = await app.CreateUserWithPermissionsAsync("alice", "orders.read");
        TestUser other = await app.CreateUserWithPermissionsAsync("bob", "other.read");

        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(reader).GetStatusAsync("/Orders"));
        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(other).GetStatusAsync("/Orders"));
        Assert.Equal(EndpointProtection.DynamicPermission, app.GetCatalog().FindEndpoint("mvc::Orders:Index")?.Protection);
    }

    /// <summary>
    ///   The fallback policy applies to endpoints without authorization metadata, which have no permission in the
    ///   catalog. Requiring the endpoint's permission there would lock everyone but super admins out.
    /// </summary>
    [Fact]
    public async Task Fallback_policy_requiring_the_permission_of_the_endpoint_is_rejected_at_startup()
    {
        OptionsValidationException exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            TestApp.StartAsync(o => o.Services = s => s.AddAuthorizationBuilder().SetFallbackPolicy(s_dynamicPolicy)));

        Assert.Contains("fallback", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fallback_policy_can_require_an_explicit_permission()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Services = s => s.AddAuthorizationBuilder().SetFallbackPolicy(
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new DynamicPermissionRequirement("reports.schedule")).Build()));
        TestUser granted = await app.CreateUserWithPermissionsAsync("alice", "reports.schedule");
        TestUser denied = await app.CreateUserWithPermissionsAsync("bob", "reports.read");

        Assert.Equal(HttpStatusCode.Unauthorized, await app.CreateClient().GetStatusAsync("/Reports"));
        Assert.Equal(HttpStatusCode.Forbidden, await app.CreateClient(denied).GetStatusAsync("/Reports"));
        Assert.Equal(HttpStatusCode.OK, await app.CreateClient(granted).GetStatusAsync("/Reports"));
    }
}
