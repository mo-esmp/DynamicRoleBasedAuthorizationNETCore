using Microsoft.AspNetCore.Identity;

namespace SampleMvcWebWithUi.Models
{
    public class ApplicationRoleClaim : IdentityRoleClaim<int>
    {
        public virtual ApplicationRole Role { get; set; }
    }
}