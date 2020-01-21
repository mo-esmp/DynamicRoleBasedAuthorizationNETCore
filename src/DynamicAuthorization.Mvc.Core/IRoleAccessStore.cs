using System.Threading.Tasks;

namespace DynamicAuthorization.Mvc.Core
{
    public interface IRoleAccessStore
    {
        Task AddRoleAccessAsync(RoleAccess roleAccess);

        Task EditRoleAccessAsync(RoleAccess roleAccess);

        Task RemoveRoleAccessAsync(string roleId);

        bool HasAccessToAction(string actionId, params string[] roles);
    }
}