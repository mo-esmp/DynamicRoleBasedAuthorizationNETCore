namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   Infers CRUD operations from names and HTTP methods.
/// </summary>
internal static class OperationConventions
{
    // Matched against the first word of a PascalCase name, so "EditPost" and "DeleteConfirmed" match but "Editor" does not.
    private static readonly Dictionary<string, string> s_words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Index"] = PermissionOperation.Read,
        ["Details"] = PermissionOperation.Read,
        ["Detail"] = PermissionOperation.Read,
        ["List"] = PermissionOperation.Read,
        ["Get"] = PermissionOperation.Read,
        ["View"] = PermissionOperation.Read,
        ["Show"] = PermissionOperation.Read,
        ["Search"] = PermissionOperation.Read,
        ["Find"] = PermissionOperation.Read,
        ["Browse"] = PermissionOperation.Read,
        ["Create"] = PermissionOperation.Create,
        ["Add"] = PermissionOperation.Create,
        ["New"] = PermissionOperation.Create,
        ["Insert"] = PermissionOperation.Create,
        ["Post"] = PermissionOperation.Create,
        ["Edit"] = PermissionOperation.Update,
        ["Update"] = PermissionOperation.Update,
        ["Modify"] = PermissionOperation.Update,
        ["Put"] = PermissionOperation.Update,
        ["Patch"] = PermissionOperation.Update,
        ["Delete"] = PermissionOperation.Delete,
        ["Remove"] = PermissionOperation.Delete,
        ["Destroy"] = PermissionOperation.Delete,
    };

    /// <summary>
    ///   Gets the operation named by the first word of an action, page, handler or endpoint name.
    /// </summary>
    public static string? FromName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        int end = 1;
        while (end < name.Length && char.IsLetterOrDigit(name[end]) && !char.IsUpper(name[end]))
        {
            end++;
        }

        return s_words.GetValueOrDefault(name[..end]);
    }

    /// <summary>
    ///   Gets the operation implied by HTTP methods that have unambiguous CRUD semantics. POST, and any mix of
    ///   methods, has none.
    /// </summary>
    public static string? FromHttpMethods(IReadOnlyCollection<string> httpMethods)
    {
        if (httpMethods.Count == 0)
        {
            return null;
        }

        if (httpMethods.All(m => IsAny(m, "GET", "HEAD")))
        {
            return PermissionOperation.Read;
        }

        if (httpMethods.All(m => IsAny(m, "PUT", "PATCH")))
        {
            return PermissionOperation.Update;
        }

        return httpMethods.All(m => IsAny(m, "DELETE")) ? PermissionOperation.Delete : null;
    }

    public static bool IsSafeMethod(string httpMethod) => IsAny(httpMethod, "GET", "HEAD");

    private static bool IsAny(string value, params string[] candidates) =>
        candidates.Any(c => string.Equals(value, c, StringComparison.OrdinalIgnoreCase));
}
