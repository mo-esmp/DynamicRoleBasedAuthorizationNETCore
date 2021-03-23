using DynamicAuthorization.Mvc.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace DynamicAuthorization.Mvc.Core
{
    public abstract class SecureContentTagHelper<TDbContext>
        : SecureContentTagHelper<TDbContext, IdentityUser, IdentityRole, string>
        where TDbContext : IdentityDbContext
    {
        protected SecureContentTagHelper(
            TDbContext dbContext,
            DynamicAuthorizationOptions authorizationOptions,
            IRoleAccessStore roleAccessStore
        ) : base(dbContext, authorizationOptions, roleAccessStore)
        {
        }
    }

    public abstract class SecureContentTagHelper<TDbContext, TUser>
        : SecureContentTagHelper<TDbContext, TUser, IdentityRole, string>
        where TDbContext : IdentityDbContext<TUser>
        where TUser : IdentityUser
    {
        protected SecureContentTagHelper(
            TDbContext dbContext,
            DynamicAuthorizationOptions authorizationOptions,
            IRoleAccessStore roleAccessStore
        ) : base(dbContext, authorizationOptions, roleAccessStore)
        {
        }
    }

    public abstract class SecureContentTagHelper<TDbContext, TUser, TRole, TKey>
        : SecureContentTagHelper<TDbContext, TUser, TRole, TKey, IdentityUserClaim<TKey>, IdentityUserRole<TKey>, IdentityUserLogin<TKey>, IdentityRoleClaim<TKey>, IdentityUserToken<TKey>>
        where TDbContext : IdentityDbContext<TUser, TRole, TKey, IdentityUserClaim<TKey>, IdentityUserRole<TKey>, IdentityUserLogin<TKey>, IdentityRoleClaim<TKey>, IdentityUserToken<TKey>>
        where TUser : IdentityUser<TKey>
        where TRole : IdentityRole<TKey>
        where TKey : IEquatable<TKey>
    {
        protected SecureContentTagHelper(
            TDbContext dbContext,
            DynamicAuthorizationOptions authorizationOptions,
            IRoleAccessStore roleAccessStore
        ) : base(dbContext, authorizationOptions, roleAccessStore)
        {
        }
    }

    public abstract class SecureContentTagHelper<TDbContext, TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken> : TagHelper
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
        private readonly TDbContext _dbContext;
        private readonly DynamicAuthorizationOptions _authorizationOptions;
        private readonly IRoleAccessStore _roleAccessStore;

        protected SecureContentTagHelper(
            TDbContext dbContext,
            DynamicAuthorizationOptions authorizationOptions,
            IRoleAccessStore roleAccessStore
            )
        {
            _dbContext = dbContext;
            _authorizationOptions = authorizationOptions;
            _roleAccessStore = roleAccessStore;
        }

        [HtmlAttributeName("asp-area")]
        public string Area { get; set; }

        [HtmlAttributeName("asp-controller")]
        public string Controller { get; set; }

        [HtmlAttributeName("asp-action")]
        public string Action { get; set; }

        [ViewContext, HtmlAttributeNotBound]
        public ViewContext ViewContext { get; set; }

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            output.TagName = null;
            var user = ViewContext.HttpContext.User;

            if (!user.Identity.IsAuthenticated)
            {
                output.SuppressOutput();
                return;
            }

            if (user.Identity.Name.Equals(_authorizationOptions.DefaultAdminUser, StringComparison.CurrentCultureIgnoreCase))
                return;

            var actionId = $"{Area}:{Controller}:{Action}";

            var roles = await (
                from usr in _dbContext.Users
                join userRole in _dbContext.UserRoles on usr.Id equals userRole.UserId
                join role in _dbContext.Roles on userRole.RoleId equals role.Id
                where usr.UserName == user.Identity.Name
                select role.Id.ToString()
            ).ToArrayAsync();

            if (await _roleAccessStore.HasAccessToActionAsync(actionId, roles))
                return;

            output.SuppressOutput();
        }
    }
}