using Microsoft.AspNetCore.Identity;

namespace SampleMvcWebWithUi.Models
{
    public class ApplicationUserClaim : IdentityUserClaim<int>
    {
        public virtual ApplicationUser User { get; set; }
    }
}