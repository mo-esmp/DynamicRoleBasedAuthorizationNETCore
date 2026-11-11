using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DynamicAuthorization.AspNetCore.Tests.TestApplication.Pages.Secure;

[Authorize]
[DisplayName("Secure home")]
[PermissionResource("secure", DisplayName = "Secure area")]
[IgnoreAntiforgeryToken]
public sealed class SecureIndexModel : PageModel
{
    // secure.read
    public void OnGet()
    {
    }

    // An unnamed POST on a page that displays a resource changes it: secure.update.
    public IActionResult OnPost() => Content("saved");

    // secure.archive
    public IActionResult OnPostArchive() => Content("archived");

    // secure.delete
    public IActionResult OnPostDelete() => Content("deleted");

    // Razor Pages lists this override as a handler of a "Page" HTTP method; it must not become a permission.
    public override Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next) =>
        base.OnPageHandlerExecutionAsync(context, next);
}
