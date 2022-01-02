using Microsoft.AspNetCore.Identity;

namespace SampleMvcWebWithUi.Models
{
    public class ApplicationUserToken : IdentityUserToken<int>
    {
        public virtual ApplicationUser User { get; set; }
    }
}