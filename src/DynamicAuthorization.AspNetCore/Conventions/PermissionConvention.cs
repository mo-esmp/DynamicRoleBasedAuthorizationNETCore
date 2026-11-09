namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   How permissions are derived from endpoints.
/// </summary>
public enum PermissionConvention
{
    /// <summary>
    ///   One permission per resource and operation, for example <c>orders.read</c> and <c>orders.update</c>. The
    ///   resource comes from the controller, page folder or route; the operation from the action, page or handler
    ///   name, or the HTTP method.
    /// </summary>
    Crud,

    /// <summary>
    ///   One permission per MVC action, Razor Page or route, for example <c>mvc::Orders:Edit</c>.
    /// </summary>
    Endpoint
}
