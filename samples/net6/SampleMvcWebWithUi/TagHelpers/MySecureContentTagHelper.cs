using DynamicAuthorization.Mvc.Core;
using DynamicAuthorization.Mvc.Core.Models;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SampleMvcWebWithUi.Data;
using SampleMvcWebWithUi.Models;

namespace SampleMvcWebWithUi
{
    [HtmlTargetElement("secure-content")]
    public class MySecureContentTagHelper : SecureContentTagHelper<ApplicationDbContext, ApplicationUser, ApplicationRole, int,
        ApplicationUserClaim, ApplicationUserRole, ApplicationUserLogin, ApplicationRoleClaim, ApplicationUserToken>
    {
        public MySecureContentTagHelper(
            ApplicationDbContext dbContext,
            DynamicAuthorizationOptions authorizationOptions,
            IRoleAccessStore roleAccessStore)
            : base(dbContext, authorizationOptions, roleAccessStore)
        {
        }
    }
}