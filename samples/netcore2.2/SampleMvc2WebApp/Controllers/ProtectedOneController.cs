using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

namespace SampleMvc2WebApp.Controllers
{
    [Authorize]
    [DisplayName("Protected section 1")]
    public class ProtectedOneController : Controller
    {
        [DisplayName("List")]
        public IActionResult Index()
        {
            return View();
        }

        [DisplayName("Detail")]
        public IActionResult Detail()
        {
            return View();
        }
    }
}