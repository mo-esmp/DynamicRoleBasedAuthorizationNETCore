using DynamicAuthorization.Mvc.Core;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DynamicAuthorization.Mvc.Ui.ViewModels
{
    public class RoleViewModel
    {
        [Required]
        [StringLength(256, ErrorMessage = "The {0} must be at least {2} characters long.")]
        public string Name { get; set; }

        public IEnumerable<MvcControllerInfo> SelectedControllers { get; set; }
    }
}