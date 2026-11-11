using System.Net;
using DynamicAuthorization.AspNetCore.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;

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
        Assert.Contains("[secure-missing-handler]", html, StringComparison.Ordinal); // unknown handlers fall back to the page
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

    /// <summary>
    ///   With Razor runtime compilation, only the endpoint of the compiled page has the authorization metadata of the
    ///   page model. The policy of a target must be evaluated on that endpoint, or its content is shown to everyone.
    /// </summary>
    [Fact]
    public async Task Secure_content_checks_pages_with_runtime_compilation()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.RazorRuntimeCompilation = true);
        TestUser archiver = await app.CreateUserWithPermissionsAsync("alice", "secure.archive");
        TestUser reader = await app.CreateUserWithPermissionsAsync("bob", "secure.read");
        TestUser other = await app.CreateUserWithPermissionsAsync("carol", "folder.read");

        string forArchiver = await app.CreateClient(archiver).GetStringAsync("/Links");
        string forReader = await app.CreateClient(reader).GetStringAsync("/Links");
        string forOther = await app.CreateClient(other).GetStringAsync("/Links");
        string forAnonymous = await app.CreateClient().GetStringAsync("/Links");

        Assert.Contains("[secure-page]", forArchiver, StringComparison.Ordinal);
        Assert.Contains("[secure-archive]", forArchiver, StringComparison.Ordinal);
        Assert.Contains("[secure-page]", forReader, StringComparison.Ordinal);
        Assert.DoesNotContain("[secure-archive]", forReader, StringComparison.Ordinal);
        Assert.DoesNotContain("[secure-page]", forOther, StringComparison.Ordinal);
        Assert.DoesNotContain("[secure-archive]", forOther, StringComparison.Ordinal);
        Assert.DoesNotContain("[secure-page]", forAnonymous, StringComparison.Ordinal);
        Assert.Contains("[public-page]", forAnonymous, StringComparison.Ordinal);
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
        Assert.Contains("[public-parent]", html, StringComparison.Ordinal);
        Assert.Contains("[public-current]", html, StringComparison.Ordinal);
        Assert.Contains("[orders-public]", html, StringComparison.Ordinal);
        Assert.DoesNotContain("[secure-page]", html, StringComparison.Ordinal);
        Assert.DoesNotContain("[schedule]", html, StringComparison.Ordinal);
        Assert.DoesNotContain("[audits]", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Secure_content_evaluates_requirements_declared_by_attributes()
    {
        await using TestApp app = await TestApp.StartAsync();
        TestUser auditor = await app.CreateUserAsync("alice", "Auditor");
        TestUser other = await app.CreateUserAsync("bob");

        Assert.Contains("[audits]", await app.CreateClient(auditor).GetStringAsync("/Links"), StringComparison.Ordinal);
        Assert.DoesNotContain("[audits]", await app.CreateClient(other).GetStringAsync("/Links"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Secure_content_without_a_target_fails()
    {
        await using TestApp app = await TestApp.StartAsync();

        HttpResponseMessage response = await app.CreateClient().GetAsync("/NoTarget");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("requires an 'asp-permission'", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
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

    /// <summary>
    ///   Targets without authorization metadata are evaluated against the fallback policy, as the authorization
    ///   middleware evaluates them. The policy here passes requests, whose resource is the HttpContext, and fails the
    ///   evaluation of the tag helper, whose resource is the catalog entry of the target.
    /// </summary>
    [Fact]
    public async Task Secure_content_applies_the_fallback_policy_to_targets_without_authorization_metadata()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Services = s => s.AddAuthorizationBuilder().SetFallbackPolicy(
            new AuthorizationPolicyBuilder().RequireAssertion(c => c.Resource is HttpContext).Build()));

        string html = await app.CreateClient(await app.CreateUserAsync("alice")).GetStringAsync("/Links");

        Assert.DoesNotContain("[public-page]", html, StringComparison.Ordinal); // /Public has no authorization metadata
        Assert.Contains("[orders-public]", html, StringComparison.Ordinal); // [AllowAnonymous] wins over the fallback
    }

    /// <summary>
    ///   Like the authorization middleware, the tag helper combines the policy of a target once per endpoint when the
    ///   policy provider allows caching policies; a provider whose policies change must say so to be consulted on
    ///   every render.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Secure_content_caches_endpoint_policies_only_when_the_provider_allows_it(bool allowsCaching)
    {
        TogglingPolicyProvider provider = new(allowsCaching);
        await using TestApp app = await TestApp.StartAsync(o => o.Services = s => s.AddSingleton<IAuthorizationPolicyProvider>(provider));
        HttpClient client = app.CreateClient(await app.CreateUserAsync("alice"));

        string before = await client.GetStringAsync("/Links");
        provider.Deny = true;
        string after = await client.GetStringAsync("/Links");

        Assert.Contains("[secure-page]", before, StringComparison.Ordinal);
        Assert.Equal(allowsCaching, after.Contains("[secure-page]", StringComparison.Ordinal));
    }

    /// <summary>
    ///   Grants every policy to authenticated users until <see cref="Deny"/> is set, then denies every policy.
    /// </summary>
    private sealed class TogglingPolicyProvider(bool allowsCaching) : IAuthorizationPolicyProvider
    {
        public bool Deny { get; set; }

        public bool AllowsCachingPolicies => allowsCaching;

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
            Task.FromResult(Deny
                ? new AuthorizationPolicyBuilder().RequireAssertion(_ => false).Build()
                : new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => Task.FromResult<AuthorizationPolicy?>(null);

        public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) => await GetDefaultPolicyAsync();
    }
}
