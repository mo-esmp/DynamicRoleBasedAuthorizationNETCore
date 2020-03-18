using Microsoft.AspNetCore.Identity;

namespace DynamicRoleBasedAuthorization.Tests.TestSetup
{
    public class InitialData
    {
        public static IdentityUser SuperUser => new IdentityUser("some@one.com");

        public static IdentityUser AdminUser => new IdentityUser("admin@mail.com");

        public static IdentityUser OrdinaryUser => new IdentityUser("ordinary@mail.com");

        public static IdentityRole AdminRole => new IdentityRole("AdminRole");

        public static IdentityRole RestrictedRole => new IdentityRole("RestrictedRole");

        public const string DefaultPassword = "123@Qaz45";
    }
}