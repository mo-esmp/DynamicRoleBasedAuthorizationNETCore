using DynamicAuthorization.Mvc.Core;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SampleMvcWebApp.Data;

namespace SampleMvcWebApp
{
    [HtmlTargetElement("secure-content")]
    public class MySecureContentTagHelper : SecureContentTagHelper<ApplicationDbContext>
    {
        public MySecureContentTagHelper(ApplicationDbContext dbContext, IRoleAccessStore roleAccessStore)
            : base(dbContext, roleAccessStore)
        {
        }
    }
}