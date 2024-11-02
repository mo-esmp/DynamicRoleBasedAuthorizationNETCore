using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DynamicAuthorization.Mvc.Ui"),
           InternalsVisibleTo("DynamicAuthorization.Mvc.MsSqlServerStore")]

namespace DynamicAuthorization.Mvc.Core
{
    public class DynamicAuthorizationOptions
    {
        public string[]? DefaultAllowedAdmins { get; set; }

        public string[]? DefaultAllowedRoles { get; set; }
    }
}