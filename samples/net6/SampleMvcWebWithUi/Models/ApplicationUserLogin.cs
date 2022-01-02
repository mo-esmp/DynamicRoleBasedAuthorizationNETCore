using Microsoft.AspNetCore.Identity;

namespace SampleMvcWebWithUi.Models
{
    public class ApplicationUserLogin : IdentityUserLogin<int>
    {
        public virtual ApplicationUser User { get; set; }
    }
}