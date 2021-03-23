using DynamicAuthorization.Mvc.Ui.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DynamicAuthorization.Mvc.Ui.Services
{
    internal class IdentityService<TDbContext> : IdentityService<TDbContext, IdentityUser, IdentityRole, string>
        where TDbContext : IdentityDbContext
    {
        public IdentityService(TDbContext context) : base(context)
        {
        }
    }

    internal class IdentityService<TDbContext, TUser> : IdentityService<TDbContext, TUser, IdentityRole, string>
        where TDbContext : IdentityDbContext<TUser>
        where TUser : IdentityUser
    {
        public IdentityService(TDbContext context) : base(context)
        {
        }
    }

    internal class
        IdentityService<TDbContext, TUser, TRole, TKey> : IdentityService<TDbContext, TUser, TRole, TKey, IdentityUserClaim<TKey>, IdentityUserRole<TKey>, IdentityUserLogin<TKey>, IdentityRoleClaim<TKey>, IdentityUserToken<TKey>>
        where TDbContext : IdentityDbContext<TUser, TRole, TKey>
        where TUser : IdentityUser<TKey>
        where TRole : IdentityRole<TKey>
        where TKey : IEquatable<TKey>

    {
        public IdentityService(TDbContext context) : base(context)
        {
        }
    }

    internal class IdentityService<TDbContext, TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken> : IIdentityService
        where TDbContext : IdentityDbContext<TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken>
        where TUser : IdentityUser<TKey>
        where TRole : IdentityRole<TKey>
        where TKey : IEquatable<TKey>
        where TUserClaim : IdentityUserClaim<TKey>
        where TUserRole : IdentityUserRole<TKey>
        where TUserLogin : IdentityUserLogin<TKey>
        where TRoleClaim : IdentityRoleClaim<TKey>
        where TUserToken : IdentityUserToken<TKey>
    {
        private readonly TDbContext _context;

        public IdentityService(TDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<UserRoleViewModel>> GetUsersRolesAsync()
        {
            var query = await (
                    from user in _context.Users
                    join ur in _context.UserRoles on user.Id equals ur.UserId into userRoles
                    from userRole in userRoles.DefaultIfEmpty()
                    join rle in _context.Roles on userRole.RoleId equals rle.Id into roles
                    from role in roles.DefaultIfEmpty()
                    select new { user, userRole, role }
                ).ToListAsync();

            var userList = new List<UserRoleViewModel>();

            foreach (var grp in query.GroupBy(q => q.user.Id))
            {
                var first = grp.First();
                userList.Add(new UserRoleViewModel
                {
                    UserId = first.user.Id.ToString(),
                    UserName = first.user.UserName,
                    Roles = first.role != null ? grp.Select(g => g.role).Select(r => r.Name).ToList() : new List<string>()
                });
            }

            return userList;
        }
    }
}