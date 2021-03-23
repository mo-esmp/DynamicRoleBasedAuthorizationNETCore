using DynamicAuthorization.Mvc.Ui.Filters;
using DynamicAuthorization.Mvc.Ui.Services;
using DynamicAuthorization.Mvc.Ui.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace DynamicAuthorization.Mvc.Ui.Controllers
{
    [Authorize, AddResourcesToViewFilter]
    [DisplayName("User Role Management")]
    public class UserRoleController<TRole, TUser, TKey> : Controller
    where TRole : IdentityRole<TKey>
    where TUser : IdentityUser<TKey>
    where TKey : IEquatable<TKey>
    {
        private readonly RoleManager<TRole> _roleManager;
        private readonly UserManager<TUser> _userManager;

        public UserRoleController(RoleManager<TRole> roleManager, UserManager<TUser> userManager)
        {
            _roleManager = roleManager;
            _userManager = userManager;
        }

        // GET: Access
        [DisplayName("User List")]
        public async Task<ActionResult> Index([FromServices] IIdentityService identityService)
        {
            var usersRoles = await identityService.GetUsersRolesAsync();

            return View(usersRoles);
        }

        // GET: Access/Edit
        [DisplayName("Edit User Roles")]
        public async Task<ActionResult> Edit(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return NotFound();

            var userRoles = await _userManager.GetRolesAsync(user);
            var userViewModel = new UserRoleViewModel
            {
                UserId = user.Id.ToString(),
                UserName = user.UserName,
                Roles = userRoles
            };

            var roles = _roleManager.Roles;
            ViewData["Roles"] = roles;

            return View(userViewModel);
        }

        // POST: Access/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(UserRoleViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                ViewData["Roles"] = _roleManager.Roles;
                return View(viewModel);
            }

            var user = await _userManager.FindByIdAsync(viewModel.UserId);
            if (user == null)
            {
                ModelState.AddModelError("", "User not found");
                ViewData["Roles"] = _roleManager.Roles;
                return View();
            }

            var userRoles = await _userManager.GetRolesAsync(user);
            if (userRoles.Any())
                await _userManager.RemoveFromRolesAsync(user, userRoles);

            if (viewModel.Roles != null)
                await _userManager.AddToRolesAsync(user, viewModel.Roles);

            return RedirectToAction("Index");
        }
    }
}