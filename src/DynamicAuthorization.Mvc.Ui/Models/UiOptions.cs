using DynamicAuthorization.Mvc.Core;

namespace DynamicAuthorization.Mvc.Ui
{
    /// <summary>
    ///   The options to be used by DynamicAuthorization UI to configure user management dashboard.
    ///   You normally use a <see cref="DynamicAuthorizationOptionBuilder"/> to create instances of
    ///   this class.
    /// </summary>
    public class UiAuthorizationOptions
    {
        /// <summary>
        ///   Gets or sets the type of the authentication.
        /// </summary>
        /// <value>The type of the authentication.</value>
        public AuthenticationType AuthenticationType { get; set; }

        /// <summary>
        ///   Gets or sets the route prefix to access role management dashboard via browser. The
        ///   default value is <c>dynamic-access</c> and you can the dashboard by using <c>http://localhost/dynamic-access</c>
        /// </summary>
        /// <value>The route prefix.</value>
        public string RoutePrefix { get; set; } = "dynamic-access";
    }

    public enum AuthenticationType
    {
        Cookie,
        Jwt
    }
}