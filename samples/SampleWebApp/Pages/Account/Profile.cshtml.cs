using DynamicAuthorization.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SampleWebApp.Pages.Account;

// Every signed-in user may see their profile, so no permission is required.
[Authorize]
[IgnoreDynamicAuthorization]
public sealed class ProfileModel : PageModel
{
    public void OnGet()
    {
    }
}
