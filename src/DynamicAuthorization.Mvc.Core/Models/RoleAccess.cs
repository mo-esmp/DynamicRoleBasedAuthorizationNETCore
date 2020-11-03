using System.Collections.Generic;

namespace DynamicAuthorization.Mvc.Core
{
    public class RoleAccess
    {
        public int Id { get; set; }

        public string RoleId { get; set; }

        public IEnumerable<MvcControllerInfo> Controllers { get; set; }
    }
}