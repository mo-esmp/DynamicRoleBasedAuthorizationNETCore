using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace DynamicAuthorization.AspNetCore.Tests.Infrastructure;

/// <summary>
///   Signs in the user named in the test headers, building the principal like ASP.NET Core Identity does (including
///   role claims). Challenges return 401 and forbids return 403.
/// </summary>
internal sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserIdHeader = "X-Test-UserId";
    public const string UserNameHeader = "X-Test-UserName";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserIdHeader, out StringValues userId))
            return AuthenticateResult.NoResult();

        UserManager<IdentityUser> users = Context.RequestServices.GetRequiredService<UserManager<IdentityUser>>();
        IdentityUser? user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
            return AuthenticateResult.Fail("Unknown test user.");

        ClaimsPrincipal principal = await Context.RequestServices.GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>().CreateAsync(user);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
