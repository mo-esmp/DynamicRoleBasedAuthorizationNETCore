using DynamicAuthorization.Mvc.Ui.ViewModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DynamicAuthorization.Mvc.Ui.Services
{
    public interface IIdentityService
    {
        Task<IEnumerable<UserRoleViewModel>> GetUsersRolesAsync();
    }
}