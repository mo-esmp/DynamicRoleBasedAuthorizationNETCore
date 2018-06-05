using DynamicRoleBasedAuthorization.Data;
using DynamicRoleBasedAuthorization.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace DynamicRoleBasedAuthorization.Controllers
{
    [Description("Access")]
    //[CustomAuthorize]
    public class AccessController : Controller
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly RoleManager<ApplicationRole> _roleManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public AccessController(
            ApplicationDbContext dbContext,
            RoleManager<ApplicationRole> roleManager,
            UserManager<ApplicationUser> userManager
            )
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _dbContext = dbContext;
        }

        // GET: Access
        [Description("Access List")]
        public async Task<ActionResult> Index()
        {
            var users = await (from user in _dbContext.Users
                               let userRoles = _dbContext.UserRoles.Where(ur => ur.UserId == user.Id)
                               let roles = _dbContext.Roles.Where(r => userRoles.Any(ur => r.Id == ur.RoleId)).Select(r => r.Name)
                               select new UserRoleViewModel
                               {
                                   UserId = user.Id,
                                   UserName = user.UserName,
                                   Roles = roles
                               }).ToListAsync();

            return View(users);
        }

        // GET: Access/Edit
        [Description("Edit Access")]
        public async Task<ActionResult> Edit(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return NotFound();

            var userRoles = await _userManager.GetRolesAsync(user);
            var userViewModel = new UserRoleViewModel
            {
                UserId = user.Id,
                UserName = user.UserName,
                Roles = userRoles
            };

            var roles = await _roleManager.Roles.ToListAsync();
            ViewData["Roles"] = roles;

            return View(userViewModel);
        }

        // POST: Access/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(UserRoleViewModel viewModel)
        {
            var roles = await _roleManager.Roles.ToListAsync();
            ViewData["Roles"] = roles;

            if (!ModelState.IsValid)
                return View(viewModel);

            var user = _dbContext.Users.Find(viewModel.UserId);
            if (user == null)
            {
                ModelState.AddModelError("", "User not found");
                return View();
            }

            var userRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, userRoles);
            await _userManager.AddToRolesAsync(user, viewModel.Roles);

            return RedirectToAction("Index");
        }
    }
}