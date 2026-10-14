namespace DynamicAuthorization.AspNetCore.Management;

/// <summary>
///   Manages the roles users are members of.
/// </summary>
public interface IUserManagementService
{
    /// <summary>
    ///   Gets a page of users, ordered by user name.
    /// </summary>
    /// <param name="query">The search text and page.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The page of users.</returns>
    Task<PagedResult<UserSummary>> GetUsersAsync(UserQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    ///   Finds a user by identifier.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The user, or <see langword="null"/> when it does not exist.</returns>
    Task<UserSummary?> FindUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    ///   Finds a user by user name or email address.
    /// </summary>
    /// <param name="userNameOrEmail">The user name or email address.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The user, or <see langword="null"/> when it does not exist.</returns>
    Task<UserSummary?> FindUserByNameAsync(string userNameOrEmail, CancellationToken cancellationToken = default);

    /// <summary>
    ///   Replaces the roles a user is a member of.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="roleIds">The identifiers of the roles the user should be a member of.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result.</returns>
    Task<ManagementResult> SetUserRolesAsync(string userId, IEnumerable<string> roleIds, CancellationToken cancellationToken = default);
}

/// <summary>
///   A user and the roles they are a member of.
/// </summary>
/// <param name="Id">The user identifier.</param>
/// <param name="UserName">The user name.</param>
/// <param name="Email">The email address.</param>
/// <param name="Roles">The roles of the user.</param>
public sealed record UserSummary(string Id, string UserName, string? Email, IReadOnlyList<RoleReference> Roles);

/// <summary>
///   A query for a page of users.
/// </summary>
/// <param name="Search">Text the user name or email address must contain.</param>
/// <param name="Page">The one-based page number.</param>
/// <param name="PageSize">The number of users per page.</param>
public sealed record UserQuery(string? Search = null, int Page = 1, int PageSize = 25);

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
