namespace DynamicAuthorization.AspNetCore.HybridCache;

/// <summary>
///   Options for caching dynamic authorization data in <c>HybridCache</c>.
/// </summary>
public sealed class DynamicAuthorizationHybridCacheOptions
{
    /// <summary>
    ///   Gets or sets how long an entry read from the distributed cache is kept in the memory of the instance before
    ///   it is read again. An invalidation made on another instance is seen within this time; the entry itself lives
    ///   in the distributed cache for <see cref="DynamicAuthorizationOptions.CacheDuration"/>. Defaults to five
    ///   seconds.
    /// </summary>
    public TimeSpan LocalCacheDuration { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///   Gets or sets the prefix of every cache key and tag. Applications that share a distributed cache but not
    ///   their users and roles must use different prefixes, or they read each other's entries and an invalidation in
    ///   one reaches the other. Defaults to <c>dynamic-authorization:</c>.
    /// </summary>
    public string KeyPrefix { get; set; } = "dynamic-authorization:";
}
