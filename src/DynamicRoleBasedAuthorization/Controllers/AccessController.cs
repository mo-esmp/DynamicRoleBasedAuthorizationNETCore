using DynamicRoleBasedAuthorization.Data;
using DynamicRoleBasedAuthorization.Models;
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

        public AccessController(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        // GET: Access
        [Description("Access List")]
        public async Task<ActionResult> Index()
        {
            var users = await (from user in _dbContext.Users
                               let userRoles = _dbContext.UserRoles.Where(ur => ur.UserId == user.Id)
                               select new UserRoleViewModel
                               {
                                   UserId = user.Id,
                                   UserName = user.UserName,
                                   Roles = _dbContext.Roles.Where(r => userRoles.Any(ur => r.Id == ur.RoleId)).Select(r => r.Name)
                               }).ToListAsync();

            return View(users);
        }

        // GET: Access/Edit
        [Description("Edit Access")]
        public async Task<ActionResult> Edit(string id)
        {
            //var user = await (from user in _dbContext.Users
            //                  let userRoles = _dbContext.UserRoles.Where(ur => ur.UserId == user.Id)
            //                  where user.Id == id
            //                  select new UserRoleViewModel
            //                  {
            //                      UserId = user.Id,
            //                      UserName = user.UserName,
            //                      Roles = _dbContext.Roles.Where(r => userRoles.Any(ur => r.Id == ur.RoleId)).Select(r => r.Name)
            //                  })
            //    .SingleOrDefaultAsync();

            //if (user == null)
            //    return HttpNotFound();

            //var roles = await _dbContext.Roles.ToListAsync();

            //var viewModel = new EditUserRoleViewModel
            //{
            //    UserId = user.UserId,
            //    UserName = user.UserName,
            //    SelectedRoles = user.Roles,
            //    Roles = roles
            //};

            return View();
        }

        // POST: Access/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(/*EditUserRoleViewModel viewModel*/)
        {
            //_dbContext = HttpContext.GetOwinContext().Get<ApplicationDbContext>();
            //if (!ModelState.IsValid)
            //{
            //    viewModel.Roles = await _dbContext.Roles.ToListAsync();
            //    return View(viewModel);
            //}

            //var user = _dbContext.Users.Find(viewModel.UserId);
            //user.Roles.Clear();
            //foreach (var roleId in viewModel.SelectedRoles)
            //{
            //    user.Roles.Add(new IdentityUserRole<> { RoleId = roleId });
            //}
            //await _dbContext.SaveChangesAsync();

            return RedirectToAction("Index");
        }
    }
}