namespace DynamicAuthorization.AspNetCore.Management;

/// <summary>
///   Manages the roles users are members of.
/// </summary>
public interface IUserManagementService
{
    /// <summary>
    ///   Gets a page of users, ordered by username without regard to case.
    /// </summary>
    /// <param name="query">The search text and page.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The page of users.</returns>
    Task<PagedResult<UserSummary>> GetUsersAsync(UserQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    ///   Counts the users. The default implementation reads the total of a page of one user; implement it to count
    ///   in the store without loading a user and their roles.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of users.</returns>
    async Task<int> CountUsersAsync(CancellationToken cancellationToken = default) =>
        (await GetUsersAsync(new UserQuery(PageSize: 1), cancellationToken).ConfigureAwait(false)).TotalCount;

    /// <summary>
    ///   Finds a user by identifier.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The user, or <see langword="null"/> when it does not exist.</returns>
    Task<UserSummary?> FindUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    ///   Finds a user by username or email address.
    /// </summary>
    /// <param name="userNameOrEmail">The username or email address.</param>
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
