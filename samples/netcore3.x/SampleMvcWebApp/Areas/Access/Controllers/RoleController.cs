using DynamicAuthorization.Mvc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SampleMvcWebApp.Areas.Access.Models;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace SampleMvcWebApp.Areas.Access.Controllers
{
    [Area("Access"), Authorize]
    [DisplayName("Role Management")]
    public class RoleController : Controller
    {
        private readonly IMvcControllerDiscovery _mvcControllerDiscovery;
        private readonly IRoleAccessStore _roleAccessStore;
        private readonly RoleManager<IdentityRole> _roleManager;

        public RoleController(
            IMvcControllerDiscovery mvcControllerDiscovery,
            IRoleAccessStore roleAccessStore,
            RoleManager<IdentityRole> roleManager
            )
        {
            _mvcControllerDiscovery = mvcControllerDiscovery;
            _roleManager = roleManager;
            _roleAccessStore = roleAccessStore;
        }

        // GET: Role
        [DisplayName("Role List")]
        public async Task<ActionResult> Index()
        {
            var roles = await _roleManager.Roles.ToListAsync();

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

            var role = new IdentityRole { Name = viewModel.Name };
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
                    RoleId = role.Id
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

            var accessList = await _roleAccessStore.GetRoleAccessAsync(role.Id);
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
                RoleId = role.Id
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

            await _roleAccessStore.RemoveRoleAccessAsync(role.Id);

            return Ok(new { });
        }
    }
}