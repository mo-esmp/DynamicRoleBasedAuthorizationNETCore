namespace DynamicAuthorization.Mvc.Core
{
    /// <summary>
    ///     The options to be used by DynamicAuthorization UI to configure access level.
    /// </summary>
    public class UiAuthorizationOptions
    {
        /// <summary>
        ///     Gets or sets the type of the authentication.
        /// </summary>
        /// <value> The type of the authentication. </value>
        public AuthenticationType AuthenticationType { get; set; }
    }

    public enum AuthenticationType
    {
        Cookie,
        Jwt
    }
}