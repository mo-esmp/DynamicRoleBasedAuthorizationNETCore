using System.Collections.Generic;

namespace DynamicRoleBasedAuthorization.Models
{
    public class UserRoleViewModel
    {
        public string UserId { get; set; }

        public string UserName { get; set; }

        public IEnumerable<string> Roles { get; set; }
    }
}