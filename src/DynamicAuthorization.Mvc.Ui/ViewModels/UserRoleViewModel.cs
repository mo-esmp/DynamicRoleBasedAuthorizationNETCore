using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DynamicAuthorization.Mvc.Ui.ViewModels
{
    public class UserRoleViewModel
    {
        [Required]
        public string UserId { get; set; }

        public string UserName { get; set; }

        public IList<string> Roles { get; set; }
    }
}