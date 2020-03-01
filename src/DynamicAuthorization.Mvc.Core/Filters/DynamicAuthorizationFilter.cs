using DynamicAuthorization.Mvc.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace DynamicAuthorization.Mvc.Core
{
    public class DynamicAuthorizationFilter : IAuthorizationFilter, IAsyncAuthorizationFilter
    {
        private readonly DynamicAuthorizationOptions _authorizationOptions;
        private readonly IRoleAccessStore _roleAccessStore;

        public DynamicAuthorizationFilter(DynamicAuthorizationOptions authorizationOptions, IRoleAccessStore roleAccessStore)
        {
            _authorizationOptions = authorizationOptions;
            _roleAccessStore = roleAccessStore;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            OnAuthorizationAsync(context).RunSynchronously();
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

            if (await _roleAccessStore.HasAccessToActionAsync(userName, actionId))
                return;

            context.Result = new ForbidResult();
        }

#if NETCORE3

        private static bool IsProtectedAction(AuthorizationFilterContext context)
        {
            var controllerActionDescriptor = (ControllerActionDescriptor)context.ActionDescriptor;
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

        private bool IsProtectedAction(AuthorizationFilterContext context)
        {
            if (context.Filters.Any(item => item is IAllowAnonymousFilter))
                return false;

            var controllerActionDescriptor = (ControllerActionDescriptor)context.ActionDescriptor;
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

        private bool IsUserAuthenticated(AuthorizationFilterContext context)
        {
            return context.HttpContext.User.Identity.IsAuthenticated;
        }

        private string GetActionId(AuthorizationFilterContext context)
        {
            var controllerActionDescriptor = (ControllerActionDescriptor)context.ActionDescriptor;
            var area = controllerActionDescriptor.ControllerTypeInfo.GetCustomAttribute<AreaAttribute>()?.RouteValue;
            var controller = controllerActionDescriptor.ControllerName;
            var action = controllerActionDescriptor.ActionName;

            return $"{area}:{controller}:{action}";
        }
    }
}