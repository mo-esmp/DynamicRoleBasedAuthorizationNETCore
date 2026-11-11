using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DynamicAuthorization.AspNetCore.Tests.TestApplication.Pages.Folder;

/// <summary>
///   A page with a named handler but no unnamed GET handler: GET renders the page without running a handler.
/// </summary>
[IgnoreAntiforgeryToken]
public sealed class FolderActionsModel : PageModel
{
    // folder.run
    public IActionResult OnPostRun() => Content("ran");
}
