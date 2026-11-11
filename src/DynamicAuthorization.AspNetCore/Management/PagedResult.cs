namespace DynamicAuthorization.AspNetCore.Management;

/// <summary>
///   A page of items.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items on the page.</param>
/// <param name="TotalCount">The total number of items.</param>
/// <param name="Page">The one-based page number.</param>
/// <param name="PageSize">The number of items per page.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    /// <summary>
    ///   Gets the number of pages.
    /// </summary>
    public int PageCount => PageSize <= 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
}
