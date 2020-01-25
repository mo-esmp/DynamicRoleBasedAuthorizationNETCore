namespace DynamicRoleBasedAuthorization.Models
{
    public class DynamicAuthorizationOptions
    {
        /// <summary>
        /// Sets the default admin user. Authorization check will be suppressed.
        /// </summary>
        /// <value>The default admin user.</value>
        public string DefaultAdminUser { get; set; }
    }
}