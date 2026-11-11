namespace DynamicAuthorization.AspNetCore.Management;

/// <summary>
///   A query for a page of users.
/// </summary>
/// <param name="Search">
///   Text the username or email address must start with, or contain when <paramref name="Match"/> is
///   <see cref="UserSearchMatch.Contains"/>. Compared without regard to case.
/// </param>
/// <param name="Page">The one-based page number.</param>
/// <param name="PageSize">The number of users per page.</param>
/// <param name="Match">Where <paramref name="Search"/> must occur in the username or email address.</param>
public sealed record UserQuery(string? Search = null, int Page = 1, int PageSize = 25, UserSearchMatch Match = UserSearchMatch.StartsWith);
