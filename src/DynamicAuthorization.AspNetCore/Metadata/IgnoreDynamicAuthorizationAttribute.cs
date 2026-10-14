namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Metadata that excludes an endpoint from the dynamic permission check.
/// </summary>
public interface IIgnoreDynamicAuthorizationMetadata;

/// <summary>
///   Excludes a controller, action or page from the dynamic permission check. The endpoint still requires an
///   authenticated user when it is decorated with <c>[Authorize]</c>, which makes this attribute a good fit for
///   pages every signed-in user may use, such as a profile page.
/// </summary>
/// <remarks>
///   An explicit <see cref="PermissionAttribute"/> on the same endpoint takes precedence.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class IgnoreDynamicAuthorizationAttribute : Attribute, IIgnoreDynamicAuthorizationMetadata;
