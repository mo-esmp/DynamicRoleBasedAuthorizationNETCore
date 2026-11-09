namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Provides the permissions and endpoints discovered in the application.
/// </summary>
public interface IPermissionCatalogProvider
{
    /// <summary>
    ///   Gets the current catalog. The catalog is rebuilt when the application's endpoints change.
    /// </summary>
    /// <returns>The catalog.</returns>
    PermissionCatalog GetCatalog();
}
