using Microsoft.AspNetCore.Identity;

namespace SampleMvcWebWithUi.Models
{
    public class ApplicationUserRole : IdentityUserRole<int>
    {
        internal ApplicationUser User { get; set; }

        internal ApplicationRole Role { get; set; }
    }
}