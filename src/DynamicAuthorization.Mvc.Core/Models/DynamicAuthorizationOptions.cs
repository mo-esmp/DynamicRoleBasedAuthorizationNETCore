using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DynamicAuthorization.Mvc.Ui"),
           InternalsVisibleTo("DynamicAuthorization.Mvc.MsSqlServerStore")]

namespace DynamicAuthorization.Mvc.Core.Models
{
    public class DynamicAuthorizationOptions
    {
        public string DefaultAdminUser { get; set; }

        internal static Type DbContextType { get; set; }

        internal static Type UserType { get; set; }

        internal static Type RoleType { get; set; }

        internal static Type KeyType { get; set; }

        internal static Type UserClaimType { get; set; }

        internal static Type UserRoleType { get; set; }

        internal static Type UserLoginType { get; set; }

        internal static Type RoleClaimType { get; set; }

        internal static Type UserTokenType { get; set; }
    }
}