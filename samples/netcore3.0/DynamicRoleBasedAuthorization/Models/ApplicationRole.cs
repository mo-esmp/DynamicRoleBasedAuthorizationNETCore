using Microsoft.AspNetCore.Identity;

namespace DynamicRoleBasedAuthorization.Models
{
    public class ApplicationRole : IdentityRole
    {
        public string Access { get; set; }
    }
}