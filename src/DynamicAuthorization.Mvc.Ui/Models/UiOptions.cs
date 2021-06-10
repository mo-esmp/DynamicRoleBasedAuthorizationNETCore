namespace DynamicAuthorization.Mvc.Ui
{
    /// <summary>
    ///     The options to be used by DynamicAuthorization to configure user management dashboard. You normally use a <see
    ///     cref="DynamicAuthUiOptionsBuilder"/> to create instances of this class.
    /// </summary>
    public class UiOptions
    {
        /// <summary>
        ///     Gets or sets the route prefix to access log dashboard via browser. The default value
        ///     is <c> user-management </c> and you can the dashboard by using <c>
        ///     http://localhost/user-management </c>
        /// </summary>
        /// <value> The route prefix. </value>
        public string RoutePrefix { get; set; } = "user-management";

        /// <summary>
        ///     Gets or sets the type of the authentication.
        /// </summary>
        /// <value> The type of the authentication. </value>
        internal string AuthType { get; set; }
    }
}