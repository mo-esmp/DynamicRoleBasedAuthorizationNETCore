using System.Threading.Tasks;

namespace DynamicAuthorization.Mvc.Core
{
    public interface IRoleAccessStore
    {
        Task<bool> HasAccessToActionAsync(string userName, string actionId);
    }
}