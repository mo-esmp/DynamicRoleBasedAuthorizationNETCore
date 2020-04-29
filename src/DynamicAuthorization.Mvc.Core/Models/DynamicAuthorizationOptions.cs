using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DynamicAuthorization.Mvc.Ui")]

namespace DynamicAuthorization.Mvc.Core.Models
{
    public class DynamicAuthorizationOptions
    {
        public string DefaultAdminUser { get; set; }

        internal static Type DbContextType { get; set; }

        internal static Type UserType { get; set; }

        internal static Type RoleType { get; set; }

        internal static Type KeyType { get; set; }
    }
}