using DynamicAuthorization.Mvc.Core;
using DynamicAuthorization.Mvc.Core.Models;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SampleMvc2WebApp.Data;

namespace SampleMvc2WebApp
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