using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

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

internal sealed class PermissionCatalogProvider : IPermissionCatalogProvider, IDisposable
{
    private const string CustomPermissionsGroup = "Custom permissions";

    private readonly EndpointDataSource _endpointDataSource;
    private readonly EndpointAuthorizationResolver _resolver;
    private readonly IOptionsMonitor<DynamicAuthorizationOptions> _options;
    private readonly IDisposable _changeSubscription;
    private PermissionCatalog? _catalog;

    public PermissionCatalogProvider(
        EndpointDataSource endpointDataSource,
        EndpointAuthorizationResolver resolver,
        IOptionsMonitor<DynamicAuthorizationOptions> options)
    {
        _endpointDataSource = endpointDataSource;
        _resolver = resolver;
        _options = options;
        _changeSubscription = ChangeToken.OnChange(endpointDataSource.GetChangeToken, () => Volatile.Write(ref _catalog, null));
    }

    public PermissionCatalog GetCatalog()
    {
        PermissionCatalog? catalog = Volatile.Read(ref _catalog);
        if (catalog is null)
        {
            catalog = Build();
            Volatile.Write(ref _catalog, catalog);
        }

        return catalog;
    }

    public void Dispose() => _changeSubscription.Dispose();

    private PermissionCatalog Build()
    {
        List<EndpointAuthorizationInfo> endpoints = _endpointDataSource.Endpoints
            .Where(e => !IsInfrastructureEndpoint(e))
            .Select(_resolver.Describe)
            // A page or action can be reachable through several routes (e.g. /Orders and /Orders/Index); list it once.
            .GroupBy(e => (Key: e.TargetKey.ToUpperInvariant(), Methods: string.Join(',', e.HttpMethods), e.Protection))
            .Select(g => g.MinBy(e => e.Route.Length)!)
            .OrderBy(e => e.Source)
            .ThenBy(e => e.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Route, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Dictionary<string, PermissionDescriptor> permissions = new(StringComparer.OrdinalIgnoreCase);
        foreach (EndpointAuthorizationInfo endpoint in endpoints)
        {
            if (endpoint.Permission is not null)
                permissions.TryAdd(endpoint.Permission.Permission, endpoint.Permission);
        }

        foreach (PermissionDefinition definition in _options.CurrentValue.Permissions)
        {
            permissions.TryAdd(
                definition.Permission,
                new PermissionDescriptor(
                    definition.Permission,
                    definition.DisplayName ?? definition.Permission,
                    definition.Group ?? CustomPermissionsGroup,
                    PermissionSource.Custom));
        }

        IEnumerable<PermissionDescriptor> orderedPermissions = permissions.Values
            .OrderBy(p => p.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase);

        return new PermissionCatalog(orderedPermissions, endpoints);
    }

    private static bool IsInfrastructureEndpoint(Endpoint endpoint)
    {
        EndpointMetadataCollection metadata = endpoint.Metadata;

        // MVC's conventional route templates only generate links; they never handle requests.
        if (metadata.GetMetadata<ISuppressMatchingMetadata>()?.SuppressMatching == true)
            return true;

        // Fallbacks (mapped with Order = int.MaxValue) without authorization, such as the development static file
        // fallback of MapStaticAssets().
        if (endpoint is RouteEndpoint { Order: int.MaxValue } && metadata.GetMetadata<IAuthorizeData>() is null)
            return true;

        foreach (object item in metadata)
        {
            // Static assets mapped with MapStaticAssets() and endpoints that opt out explicitly.
            if (item is IExcludeFromPermissionCatalogMetadata
                || item.GetType().FullName == "Microsoft.AspNetCore.StaticAssets.StaticAssetDescriptor")
                return true;
        }

        return false;
    }
}

/// <summary>
///   Metadata that hides an endpoint from the permission catalog and the dashboard's endpoint list. Infrastructure
///   endpoints, such as the dashboard's own static files, use it.
/// </summary>
public interface IExcludeFromPermissionCatalogMetadata;
