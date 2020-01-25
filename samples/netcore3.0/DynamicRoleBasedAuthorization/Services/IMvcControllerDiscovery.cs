using DynamicRoleBasedAuthorization.Models;
using System.Collections.Generic;

namespace DynamicRoleBasedAuthorization.Services
{
    public interface IMvcControllerDiscovery
    {
        IEnumerable<MvcControllerInfo> GetControllers();
    }
}