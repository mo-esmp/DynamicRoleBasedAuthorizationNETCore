using DynamicAuthorization.Mvc.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace DynamicAuthorization.Mvc.Core
{
    public class DynamicAuthorizationFilter<TDbContext> : DynamicAuthorizationFilter<TDbContext, IdentityUser, IdentityRole, string>
        where TDbContext : IdentityDbContext
    {
        public DynamicAuthorizationFilter(
            DynamicAuthorizationOptions authorizationOptions,
            TDbContext dbContext,
            IRoleAccessStore roleAccessStore
        ) : base(authorizationOptions, dbContext, roleAccessStore)
        {
        }
    }

    public class DynamicAuthorizationFilter<TDbContext, TUser> : DynamicAuthorizationFilter<TDbContext, TUser, IdentityRole, string>
        where TDbContext : IdentityDbContext<TUser>
        where TUser : IdentityUser
    {
        public DynamicAuthorizationFilter(
            DynamicAuthorizationOptions authorizationOptions,
            TDbContext dbContext,
            IRoleAccessStore roleAccessStore)
            : base(authorizationOptions, dbContext, roleAccessStore)
        {
        }
    }

    public class DynamicAuthorizationFilter<TDbContext, TUser, TRole, TKey>
        : DynamicAuthorizationFilter<TDbContext, TUser, TRole, TKey, IdentityUserClaim<TKey>, IdentityUserRole<TKey>, IdentityUserLogin<TKey>, IdentityRoleClaim<TKey>, IdentityUserToken<TKey>>
        where TDbContext : IdentityDbContext<TUser, TRole, TKey>
        where TUser : IdentityUser<TKey>
        where TRole : IdentityRole<TKey>
        where TKey : IEquatable<TKey>
    {
        public DynamicAuthorizationFilter(
            DynamicAuthorizationOptions authorizationOptions,
            TDbContext dbContext,
            IRoleAccessStore roleAccessStore)
            : base(authorizationOptions, dbContext, roleAccessStore)
        {
        }
    }

    public class DynamicAuthorizationFilter<TDbContext, TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken> : IAsyncAuthorizationFilter
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
        private readonly DynamicAuthorizationOptions _authorizationOptions;
        private readonly TDbContext _dbContext;
        private readonly IRoleAccessStore _roleAccessStore;

        public DynamicAuthorizationFilter(
            DynamicAuthorizationOptions authorizationOptions,
            TDbContext dbContext,
            IRoleAccessStore roleAccessStore
        )
        {
            _authorizationOptions = authorizationOptions;
            _roleAccessStore = roleAccessStore;
            _dbContext = dbContext;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (!IsProtectedAction(context))
                return;

            if (!IsUserAuthenticated(context))
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            var userName = context.HttpContext.User.Identity.Name;
            if (userName.Equals(_authorizationOptions.DefaultAdminUser, StringComparison.CurrentCultureIgnoreCase))
                return;

            var actionId = GetActionId(context);

            var roles = await (
                from user in _dbContext.Users
                join userRole in _dbContext.UserRoles on user.Id equals userRole.UserId
                join role in _dbContext.Roles on userRole.RoleId equals role.Id
                where user.UserName == userName
                select role.Id.ToString()
            ).ToArrayAsync();

            if (await _roleAccessStore.HasAccessToActionAsync(actionId, roles))
                return;

            context.Result = new ForbidResult();
        }

#if NETCORE3 || NET5

        private static bool IsProtectedAction(AuthorizationFilterContext context)
        {
            var controllerActionDescriptor = context.ActionDescriptor as ControllerActionDescriptor;
            if (controllerActionDescriptor == null)
                return false;

            var controllerTypeInfo = controllerActionDescriptor.ControllerTypeInfo;

            var anonymousAttribute = controllerTypeInfo.GetCustomAttribute<AllowAnonymousAttribute>();
            if (anonymousAttribute != null)
                return false;

            var actionMethodInfo = controllerActionDescriptor.MethodInfo;
            anonymousAttribute = actionMethodInfo.GetCustomAttribute<AllowAnonymousAttribute>();
            if (anonymousAttribute != null)
                return false;

            var authorizeAttribute = controllerTypeInfo.GetCustomAttribute<AuthorizeAttribute>();
            if (authorizeAttribute != null)
                return true;

            authorizeAttribute = actionMethodInfo.GetCustomAttribute<AuthorizeAttribute>();
            if (authorizeAttribute != null)
                return true;

            return false;
        }

#else

        private static bool IsProtectedAction(AuthorizationFilterContext context)
        {
            if (context.Filters.Any(item => item is IAllowAnonymousFilter))
                return false;

            var controllerActionDescriptor = context.ActionDescriptor as ControllerActionDescriptor;
            if (controllerActionDescriptor == null)
                return false;

            var controllerTypeInfo = controllerActionDescriptor.ControllerTypeInfo;
            var actionMethodInfo = controllerActionDescriptor.MethodInfo;

            var authorizeAttribute = controllerTypeInfo.GetCustomAttribute<AuthorizeAttribute>();
            if (authorizeAttribute != null)
                return true;

            authorizeAttribute = actionMethodInfo.GetCustomAttribute<AuthorizeAttribute>();
            if (authorizeAttribute != null)
                return true;

            return false;
        }

#endif

        private static bool IsUserAuthenticated(AuthorizationFilterContext context)
        {
            return context.HttpContext.User.Identity.IsAuthenticated;
        }

        private static string GetActionId(AuthorizationFilterContext context)
        {
            var controllerActionDescriptor = (ControllerActionDescriptor)context.ActionDescriptor;
            var area = controllerActionDescriptor.ControllerTypeInfo.GetCustomAttribute<AreaAttribute>()?.RouteValue;
            var controller = controllerActionDescriptor.ControllerName;
            var action = controllerActionDescriptor.ActionName;

            return $"{area}:{controller}:{action}";
        }
    }
}