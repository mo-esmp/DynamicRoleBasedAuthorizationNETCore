using DynamicRoleBasedAuthorization.Models;
using DynamicRoleBasedAuthorization.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DynamicRoleBasedAuthorization.Controllers
{
    public class RoleController : Controller
    {
        private readonly IMvcControllerDiscovery _mvcControllerDiscovery;
        private readonly RoleManager<ApplicationRole> _roleManager;

        public RoleController(IMvcControllerDiscovery mvcControllerDiscovery, RoleManager<ApplicationRole> roleManager)
        {
            _mvcControllerDiscovery = mvcControllerDiscovery;
            _roleManager = roleManager;
        }

        // GET: Role

        public async Task<ActionResult> Index()
        {
            var roles = await _roleManager.Roles.ToListAsync();

            return View(roles);
        }

        // GET: Role/Create
        public ActionResult Create()
        {
            ViewData["Controllers"] = _mvcControllerDiscovery.GetControllers();

            return View();
        }

        // POST: Role/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(RoleViewModel viewModel)
        {
            ViewData["Controllers"] = _mvcControllerDiscovery.GetControllers();

            if (!ModelState.IsValid)
                return View(viewModel);

            var role = new ApplicationRole { Name = viewModel.Name };
            if (viewModel.SelectedControllers != null && viewModel.SelectedControllers.Any())
            {
                foreach (var controller in viewModel.SelectedControllers)
                    foreach (var action in controller.Actions)
                        action.ControllerId = controller.Id;

                var accessJson = JsonConvert.SerializeObject(viewModel.SelectedControllers);
                role.Access = accessJson;
            }

            var result = await _roleManager.CreateAsync(role);
            if (result.Succeeded)
                return RedirectToAction(nameof(Index));

            foreach (var error in result.Errors)
                ModelState.AddModelError("", error.Description);

            return View(viewModel);
        }

        // GET: Role/Edit/5
        public async Task<ActionResult> Edit(string id)
        {
            ViewData["Controllers"] = _mvcControllerDiscovery.GetControllers();

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null)
                return NotFound();

            var viewModel = new RoleViewModel
            {
                Name = role.Name,
                SelectedControllers = JsonConvert.DeserializeObject<IEnumerable<MvcControllerInfo>>(role.Access)
            };

            return View(viewModel);
        }

        // POST: Role/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(string id, RoleViewModel viewModel)
        {
            ViewData["Controllers"] = _mvcControllerDiscovery.GetControllers();

            if (!ModelState.IsValid)
                return View(viewModel);

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null)
            {
                ModelState.AddModelError("", "Role not found");
                return View();
            }

            role.Name = viewModel.Name;
            if (viewModel.SelectedControllers != null && viewModel.SelectedControllers.Any())
            {
                foreach (var controller in viewModel.SelectedControllers)
                    foreach (var action in controller.Actions)
                        action.ControllerId = controller.Id;

                var accessJson = JsonConvert.SerializeObject(viewModel.SelectedControllers);
                role.Access = accessJson;
            }

            var result = await _roleManager.UpdateAsync(role);
            if (result.Succeeded)
                return RedirectToAction(nameof(Index));

            foreach (var error in result.Errors)
                ModelState.AddModelError("", error.Description);

            return View(viewModel);
        }

        // Delete: role/5
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
            if (result.Succeeded)
                return Ok(new { });

            foreach (var error in result.Errors)
                ModelState.AddModelError("Error", error.Description);

            return BadRequest(ModelState);
        }
    }
}