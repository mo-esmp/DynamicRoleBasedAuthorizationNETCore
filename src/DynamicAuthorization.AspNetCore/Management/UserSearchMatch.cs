namespace DynamicAuthorization.AspNetCore.Management;

/// <summary>
///   Where the search text of a <see cref="UserQuery"/> must occur in a username or email address.
/// </summary>
public enum UserSearchMatch
{
    /// <summary>
    ///   At the start. A store can answer this from an index on the normalized username and email address, if the
    ///   database can seek the index for a prefix.
    /// </summary>
    StartsWith,

    /// <summary>
    ///   Anywhere. No index helps with this, so a store reads every user on each search, which gets slower as the
    ///   user table grows.
    /// </summary>
    Contains,
}
