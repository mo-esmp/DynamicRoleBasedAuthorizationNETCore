using DynamicAuthorization.Mvc.Core;
using System.Threading.Tasks;

namespace DynamicAuthorization.Mvc.MsSqlServerStore
{
    public class RoleAccessStore : IRoleAccessStore
    {
        public Task<bool> AddRoleAccessAsync(RoleAccess roleAccess)
        {
            throw new System.NotImplementedException();
        }

        public Task<bool> EditRoleAccessAsync(RoleAccess roleAccess)
        {
            throw new System.NotImplementedException();
        }

        public Task<bool> RemoveRoleAccessAsync(string roleId)
        {
            throw new System.NotImplementedException();
        }

        public Task<RoleAccess> GetRoleAccessAsync(string roleId)
        {
            throw new System.NotImplementedException();
        }

        public Task<bool> HasAccessToActionAsync(string actionId, params string[] roles)
        {
            throw new System.NotImplementedException();
        }
    }
}