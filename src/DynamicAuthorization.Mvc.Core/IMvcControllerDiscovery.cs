using System.Collections.Generic;

namespace DynamicAuthorization.Mvc.Core
{
    public interface IMvcControllerDiscovery
    {
        IEnumerable<MvcControllerInfo> GetControllers();
    }
}