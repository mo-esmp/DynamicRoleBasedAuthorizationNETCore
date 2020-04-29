using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DynamicRoleBasedAuthorization.Tests.Controllers
{
    public class ActionAuthorizedController : Controller
    {
        public string NonAuthorizedAction()
        {
            return "NonAuthorizedAction";
        }

        [Authorize]
        public string AuthorizedAction()
        {
            return "AuthorizedAction";
        }
    }
}