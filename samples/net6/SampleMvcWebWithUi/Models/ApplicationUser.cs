using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;

namespace SampleMvcWebWithUi.Models
{
    public class ApplicationUser : IdentityUser<int>
    {
        public string Name { get; set; }

        public virtual ICollection<ApplicationUserClaim> Claims { get; set; }

        public virtual ICollection<ApplicationUserLogin> Logins { get; set; }

        public virtual ICollection<ApplicationUserToken> Tokens { get; set; }

        public virtual ICollection<ApplicationUserRole> UserRoles { get; set; }
    }
}