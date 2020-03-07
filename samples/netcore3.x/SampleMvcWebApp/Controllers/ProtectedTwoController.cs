using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;

namespace SampleMvcWebApp.Controllers
{
    [Authorize]
    [DisplayName("Protected section 2")]
    public class ProtectedTwoController : Controller
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