using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DynamicAuthorization.AspNetCore.Tests.TestApplication.Pages.Secure;

[Authorize]
[DisplayName("Secure home")]
[PermissionGroup("Secure area")]
public sealed class SecureIndexModel : PageModel
{
    public void OnGet()
    {
    }
}
