using System.Threading.Tasks;

namespace DynamicAuthorization.Mvc.Core
{
    public interface IRoleAccessStore
    {
        Task<bool> AddRoleAccessAsync(RoleAccess roleAccess);

        Task<bool> EditRoleAccessAsync(RoleAccess roleAccess);

        Task<bool> RemoveRoleAccessAsync(string roleId);

        Task<RoleAccess> GetRoleAccessAsync(string roleId);

        Task<bool> HasAccessToActionAsync(string actionId, params string[] roles);
    }
}