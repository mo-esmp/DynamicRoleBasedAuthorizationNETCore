namespace DynamicAuthorization.AspNetCore.HybridCache;

/// <summary>
///   Options for caching dynamic authorization data in <c>HybridCache</c>.
/// </summary>
public sealed class DynamicAuthorizationHybridCacheOptions
{
    /// <summary>
    ///   Gets or sets how long an entry read from the distributed cache is kept in the memory of the instance before
    ///   it is read again. An invalidation made on another instance removes the entry from the distributed cache, so
    ///   the other instances see it within this time; the entry itself lives in the distributed cache for
    ///   <see cref="DynamicAuthorizationOptions.CacheDuration"/>. Defaults to five seconds.
    /// </summary>
    public TimeSpan LocalCacheDuration { get; set; } = TimeSpan.FromSeconds(5);
}
