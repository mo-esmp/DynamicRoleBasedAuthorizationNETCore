using DynamicAuthorization.Mvc.Core;
using DynamicAuthorization.Mvc.Core.Models;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SampleMvcWebApp.Data;

namespace SampleMvcWebApp
{
    [HtmlTargetElement("secure-content")]
    public class MySecureContentTagHelper : SecureContentTagHelper<ApplicationDbContext>
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