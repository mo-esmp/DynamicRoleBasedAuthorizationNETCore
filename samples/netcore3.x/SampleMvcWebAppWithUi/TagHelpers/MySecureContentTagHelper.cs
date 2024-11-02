using DynamicAuthorization.Mvc.Core;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SampleMvcWebAppWithUi.Data;
using SampleMvcWebAppWithUi.Models;

namespace SampleMvcWebAppWithUi
{
    [HtmlTargetElement("secure-content")]
    public class MySecureContentTagHelper : SecureContentTagHelper<ApplicationDbContext, ApplicationUser>
    {
        public MySecureContentTagHelper(ApplicationDbContext dbContext, IRoleAccessStore roleAccessStore
            )
            : base(dbContext, roleAccessStore)
        {
        }
    }
}