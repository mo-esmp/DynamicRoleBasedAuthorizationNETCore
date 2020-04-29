using DynamicAuthorization.Mvc.Core;
using DynamicAuthorization.Mvc.Core.Models;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SampleMvcWebAppWithUi.Data;
using SampleMvcWebAppWithUi.Models;

namespace SampleMvcWebAppWithUi
{
    [HtmlTargetElement("secure-content")]
    public class MySecureContentTagHelper : SecureContentTagHelper<ApplicationDbContext, ApplicationUser>
    {
        public MySecureContentTagHelper(
            ApplicationDbContext dbContext,
            DynamicAuthorizationOptions authorizationOptions,
            IRoleAccessStore roleAccessStore
            )
            : base(dbContext, authorizationOptions, roleAccessStore)
        {
        }
    }
}