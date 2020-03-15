using Microsoft.AspNetCore.Identity;

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

        public void InitializeDb()
        {
            _userManager.CreateAsync(InitialData.SuperUser, "123@Qaz45").GetAwaiter().GetResult();
            _userManager.CreateAsync(InitialData.AdminUser, "123@Qaz45").GetAwaiter().GetResult();
            _userManager.CreateAsync(InitialData.OrdinaryUser, "123@Qaz45").GetAwaiter().GetResult();
            _roleManager.CreateAsync(InitialData.AdminRole).GetAwaiter().GetResult();
            _roleManager.CreateAsync(InitialData.RestrictedRole).GetAwaiter().GetResult();
        }
    }
}