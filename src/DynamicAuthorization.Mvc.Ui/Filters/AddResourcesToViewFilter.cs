using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.IO;
using System.Text;

namespace DynamicAuthorization.Mvc.Ui.Filters
{
    internal class AddResourcesToViewFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var styles = SetResource("DynamicAuthorization.Mvc.Ui.wwwroot.css.jquery.bonsai.min.css");
            styles += SetResource("DynamicAuthorization.Mvc.Ui.wwwroot.css.site.min.css");

            var scripts = SetResource("DynamicAuthorization.Mvc.Ui.wwwroot.js.jquery.qubit.min.js");
            scripts += SetResource("DynamicAuthorization.Mvc.Ui.wwwroot.js.jquery.bonsai.min.js");
            scripts += SetResource("DynamicAuthorization.Mvc.Ui.wwwroot.js.site.min.js");

            var controller = context.Controller as Controller;
            controller.ViewData["Styles"] = styles;
            controller.ViewData["Scripts"] = scripts;
        }

        private static string SetResource(string resourceName)
        {
            var resourceStream = typeof(AddResourcesToViewFilter).Assembly.GetManifestResourceStream(resourceName);

            using (var reader = new StreamReader(resourceStream, Encoding.UTF8))
            {
                var resource = reader.ReadToEnd();
                return resource;
            }
        }
    }
}