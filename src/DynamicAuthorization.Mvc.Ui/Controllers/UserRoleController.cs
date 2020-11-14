using DynamicAuthorization.Mvc.Core.Models;
using DynamicAuthorization.Mvc.Ui.Filters;
using DynamicAuthorization.Mvc.Ui.Services;
using DynamicAuthorization.Mvc.Ui.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Internal;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace DynamicAuthorization.Mvc.Ui.Controllers
{
    [Authorize, AddResourcesToViewFilter]
    [DisplayName("User Role Management")]
    public class UserRoleController : Controller
    {
        private readonly dynamic _roleManager;
        private readonly dynamic _userManager;

        public UserRoleController(IServiceProvider serviceProvider)
        {
            _roleManager = serviceProvider.GetService(typeof(RoleManager<>).MakeGenericType(DynamicAuthorizationOptions.RoleType));
            _userManager = serviceProvider.GetService(typeof(UserManager<>).MakeGenericType(DynamicAuthorizationOptions.UserType));
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
                UserId = user.Id,
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

            List<string> userRoles = await _userManager.GetRolesAsync(user);
            if (userRoles.Any())
                await _userManager.RemoveFromRolesAsync(user, userRoles);

            if (viewModel.Roles != null)
                await _userManager.AddToRolesAsync(user, viewModel.Roles);

            return RedirectToAction("Index");
        }
    }
}