using DynamicRoleBasedAuthorization.Models;
using DynamicRoleBasedAuthorization.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
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
                {
                    foreach (var action in controller.Actions)
                    {
                        action.ControllerId = controller.Id;
                    }
                }

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
        public ActionResult Edit(int id)
        {
            return View();
        }

        // POST: Role/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, IFormCollection collection)
        {
            try
            {
                // TODO: Add update logic here

                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: Role/Delete/5
        public ActionResult Delete(int id)
        {
            return View();
        }

        // POST: Role/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, IFormCollection collection)
        {
            try
            {
                // TODO: Add delete logic here

                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }
    }
}