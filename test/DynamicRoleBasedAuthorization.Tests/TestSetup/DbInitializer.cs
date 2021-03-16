using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;

namespace DynamicRoleBasedAuthorization.Tests.TestSetup
{
    internal class DbInitializer
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public DbInitializer(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task InitializeDbAsync()
        {
            await _userManager.CreateAsync(InitialData.SuperUser, InitialData.DefaultPassword);
            await _userManager.CreateAsync(InitialData.AdminUser, InitialData.DefaultPassword);
            await _userManager.CreateAsync(InitialData.OrdinaryUser, InitialData.DefaultPassword);
            await _roleManager.CreateAsync(InitialData.AdminRole);
            await _roleManager.CreateAsync(InitialData.RestrictedRole);
        }
    }
}