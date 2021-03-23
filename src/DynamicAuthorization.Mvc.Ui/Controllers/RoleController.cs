using DynamicAuthorization.Mvc.Core;
using DynamicAuthorization.Mvc.Core.Models;
using DynamicAuthorization.Mvc.Ui.Filters;
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
    [DisplayName("Role Management")]
    public class RoleController<TRole, TKey> : Controller
        where TRole : IdentityRole<TKey>
        where TKey : IEquatable<TKey>
    {
        private readonly RoleManager<TRole> _roleManager;
        private readonly IMvcControllerDiscovery _mvcControllerDiscovery;
        private readonly IRoleAccessStore _roleAccessStore;

        public RoleController(
            IMvcControllerDiscovery mvcControllerDiscovery,
            IRoleAccessStore roleAccessStore,
            RoleManager<TRole> roleManager)
        {
            _mvcControllerDiscovery = mvcControllerDiscovery;
            _roleAccessStore = roleAccessStore;
            _roleManager = roleManager;
        }

        // GET: Role
        [DisplayName("Role List")]
        public ActionResult Index()
        {
            var roles = _roleManager.Roles;

            return View(roles);
        }

        [DisplayName("Create Role")]
        // GET: Role/Create
        public ActionResult Create()
        {
            var controllers = _mvcControllerDiscovery.GetControllers();
            ViewData["Controllers"] = controllers;

            return View();
        }

        // POST: Role/Create
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(RoleViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                ViewData["Controllers"] = _mvcControllerDiscovery.GetControllers();
                return View(viewModel);
            }

            //var role1 = new IdentityRole { Name = viewModel.Name };
            var role = (TRole)Activator.CreateInstance(DynamicAuthorizationOptions.RoleType);
            role.GetType().GetProperty("Name")?.SetValue(role, viewModel.Name, null);

            var result = await _roleManager.CreateAsync(role);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError("", error.Description);

                ViewData["Controllers"] = _mvcControllerDiscovery.GetControllers();
                return View(viewModel);
            }

            if (viewModel.SelectedControllers != null && viewModel.SelectedControllers.Any())
            {
                foreach (var controller in viewModel.SelectedControllers)
                    foreach (var action in controller.Actions)
                        action.ControllerId = controller.Id;

                var roleAccess = new RoleAccess
                {
                    Controllers = viewModel.SelectedControllers.ToList(),
                    RoleId = role.GetType().GetProperty("Id")?.GetValue(role).ToString()
                };
                await _roleAccessStore.AddRoleAccessAsync(roleAccess);
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: Role/Edit/5
        [DisplayName("Edit Role")]
        public async Task<ActionResult> Edit(string id)
        {
            ViewData["Controllers"] = _mvcControllerDiscovery.GetControllers();
            var role = await _roleManager.FindByIdAsync(id);
            if (role == null)
                return NotFound();

            var accessList = await _roleAccessStore.GetRoleAccessAsync(role.Id.ToString());
            var viewModel = new RoleViewModel
            {
                Name = role.Name,
                SelectedControllers = accessList?.Controllers
            };

            return View(viewModel);
        }

        // POST: Role/Edit/5
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(string id, RoleViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                ViewData["Controllers"] = _mvcControllerDiscovery.GetControllers();
                return View(viewModel);
            }

            // Check role exit
            var role = await _roleManager.FindByIdAsync(id);
            if (role == null)
            {
                ModelState.AddModelError("", "Role not found");
                ViewData["Controllers"] = _mvcControllerDiscovery.GetControllers();
                return View();
            }

            // Update role if role's name is changed
            if (role.Name != viewModel.Name)
            {
                role.Name = viewModel.Name;
                var result = await _roleManager.UpdateAsync(role);
                if (!result.Succeeded)
                {
                    foreach (var error in result.Errors)
                        ModelState.AddModelError("", error.Description);

                    ViewData["Controllers"] = _mvcControllerDiscovery.GetControllers();
                    return View(viewModel);
                }
            }

            // Update role access list
            if (viewModel.SelectedControllers != null && viewModel.SelectedControllers.Any())
            {
                foreach (var controller in viewModel.SelectedControllers)
                    foreach (var action in controller.Actions)
                        action.ControllerId = controller.Id;
            }

            var roleAccess = new RoleAccess
            {
                Controllers = viewModel.SelectedControllers?.ToList(),
                RoleId = role.Id.ToString()
            };
            await _roleAccessStore.EditRoleAccessAsync(roleAccess);

            return RedirectToAction(nameof(Index));
        }

        // POST: Role/Delete/5
        [HttpDelete("role/{id}")]
        public async Task<ActionResult> Delete(string id)
        {
            var role = await _roleManager.FindByIdAsync(id);
            if (role == null)
            {
                ModelState.AddModelError("Error", "Role not found");
                return BadRequest(ModelState);
            }

            var result = await _roleManager.DeleteAsync(role);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError("Error", error.Description);

                return BadRequest(ModelState);
            }

            await _roleAccessStore.RemoveRoleAccessAsync(role.Id.ToString());

            return Ok(new { });
        }
    }
}