using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DynamicRoleBasedAuthorization.Tests.Areas.AuthorizedArea.Controllers
{
    [Area("AuthorizedArea"), Authorize]
    public class AuthorizedController : Controller
    {
        public string Action1()
        {
            return "Action1";
        }

        public string Action2()
        {
            return "Action2";
        }
    }
}