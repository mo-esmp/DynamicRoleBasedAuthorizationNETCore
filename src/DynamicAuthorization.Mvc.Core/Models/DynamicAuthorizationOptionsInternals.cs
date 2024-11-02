using System;

namespace DynamicAuthorization.Mvc.Core
{
    internal class DynamicAuthorizationOptionsInternals
    {
        internal static string[]? DefaultAllowedAdmins { get; set; }

        internal static string[]? DefaultAllowedRoles { get; set; }

        internal static Type DbContextType { get; set; } = null!;

        internal static Type UserType { get; set; } = null!;

        internal static Type RoleType { get; set; } = null!;

        internal static Type KeyType { get; set; } = null!;

        internal static Type UserClaimType { get; set; } = null!;

        internal static Type UserRoleType { get; set; } = null!;

        internal static Type UserLoginType { get; set; } = null!;

        internal static Type RoleClaimType { get; set; } = null!;

        internal static Type UserTokenType { get; set; } = null!;
    }
}