using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;

namespace SampleMvcWebWithUi.Models
{
    public class ApplicationRole : IdentityRole<int>
    {
        public ICollection<ApplicationUserRole> UserRoles { get; set; }

        public ICollection<ApplicationRoleClaim> RoleClaims { get; set; }
    }
}