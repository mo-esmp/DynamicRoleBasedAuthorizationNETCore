namespace DynamicAuthorization.AspNetCore;

/// <summary>
///   The standard operations of the <see cref="PermissionConvention.Crud"/> convention. Any other operation, such as
///   <c>approve</c>, is a custom operation named after the action, page handler or route segment.
/// </summary>
public static class PermissionOperation
{
    /// <summary>Viewing a resource: <c>Index</c>, <c>Details</c>, <c>Get…</c>, or any GET request.</summary>
    public const string Read = "read";

    /// <summary>Creating a resource: <c>Create</c>, <c>Add…</c>, <c>New…</c>, or a POST to a collection.</summary>
    public const string Create = "create";

    /// <summary>Changing a resource: <c>Edit</c>, <c>Update…</c>, or a PUT or PATCH request.</summary>
    public const string Update = "update";

    /// <summary>Removing a resource: <c>Delete</c>, <c>Remove…</c>, or a DELETE request.</summary>
    public const string Delete = "delete";

    /// <summary>
    ///   Gets the standard operations, in CRUD order: read, create, update, delete.
    /// </summary>
    public static IReadOnlyList<string> Standard { get; } = [Read, Create, Update, Delete];

    /// <summary>
    ///   Determines whether an operation is one of the <see cref="Standard"/> operations.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <returns><see langword="true"/> for read, create, update and delete; otherwise, <see langword="false"/>.</returns>
    public static bool IsStandard(string operation) => Standard.Contains(operation, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///   Gets the sort order of an operation: the standard operations first, in CRUD order, then custom operations.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <returns>The sort order.</returns>
    public static int GetOrder(string? operation)
    {
        for (int i = 0; i < Standard.Count; i++)
        {
            if (string.Equals(Standard[i], operation, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return Standard.Count;
    }

    /// <summary>
    ///   Gets a display name for an operation key, for example <c>Mark paid</c> for <c>mark-paid</c>.
    /// </summary>
    internal static string GetDisplayName(string operation)
    {
        string text = operation.Replace('-', ' ');

        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}

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
    Endpoint,
}
