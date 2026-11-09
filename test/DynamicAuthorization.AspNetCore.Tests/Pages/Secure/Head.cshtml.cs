using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DynamicAuthorization.AspNetCore.Tests.TestApplication.Pages.Secure;

/// <summary>
///   A page with a named HEAD handler. Razor Pages then never runs GET handlers for HEAD requests, whatever handler
///   name the request carries; a HEAD request that names a GET handler renders the page without running a handler.
/// </summary>
[Authorize]
[IgnoreAntiforgeryToken]
public sealed class SecureHeadModel : PageModel
{
    // secure.read
    public void OnGet()
    {
    }

    // secure.delete
    public IActionResult OnGetDelete() => Content("deleted");

    // secure.read
    public IActionResult OnHeadOther() => Content("other");
}
