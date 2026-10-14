using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace DynamicAuthorization.AspNetCore.Dashboard;

internal static class DashboardConstants
{
    public const string AreaName = "DynamicAuthorization";
    public const string PermissionGroup = "Authorization dashboard";
    public const string StatusKey = "DynamicAuthorization.Status";
    public const string ErrorKey = "DynamicAuthorization.Error";
}

/// <summary>
///   Registered by <c>AddDashboard()</c>. The dashboard pages return 404 without it, so that referencing the package
///   without enabling the dashboard never exposes the pages.
/// </summary>
internal sealed class DashboardMarker;

/// <summary>
///   Serves the dashboard pages under the configured prefix and protects them with the configured policy.
/// </summary>
internal sealed class ConfigureDashboardPages(IOptions<DashboardOptions> dashboardOptions) : IConfigureOptions<RazorPagesOptions>
{
    public void Configure(RazorPagesOptions options)
    {
        DashboardOptions dashboard = dashboardOptions.Value;
        string prefix = dashboard.NormalizedRoutePrefix;

        options.Conventions.AddAreaFolderRouteModelConvention(DashboardConstants.AreaName, "/", model =>
        {
            foreach (SelectorModel selector in model.Selectors)
            {
                AttributeRouteModel? route = selector.AttributeRouteModel;
                if (route?.Template is { } template && template.StartsWith(DashboardConstants.AreaName, StringComparison.OrdinalIgnoreCase))
                    route.Template = prefix + template[DashboardConstants.AreaName.Length..];
            }
        });

        options.Conventions.AuthorizeAreaFolder(DashboardConstants.AreaName, "/", dashboard.AuthorizationPolicy);
    }
}

/// <summary>
///   Serves the dashboard's stylesheet, script and icon from embedded resources, so the dashboard works without the
///   host's static file configuration.
/// </summary>
internal sealed class DashboardAssets
{
    public const string PathSegment = "_assets";
    private const string ResourcePrefix = "DynamicAuthorization.Dashboard.Assets.";

    private readonly Dictionary<string, Asset> _assets = new(StringComparer.OrdinalIgnoreCase);

    public DashboardAssets()
    {
        Assembly assembly = typeof(DashboardAssets).Assembly;
        foreach (string resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                continue;

            using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
            using MemoryStream buffer = new();
            stream.CopyTo(buffer);
            byte[] content = buffer.ToArray();
            string name = resourceName[ResourcePrefix.Length..];
            string version = Convert.ToHexStringLower(SHA256.HashData(content))[..16];
            _assets[name] = new Asset(content, GetContentType(name), version, new EntityTagHeaderValue($"\"{version}\""));
        }
    }

    public static object Metadata { get; } = new AssetEndpointMetadata();

    public string GetVersion(string name) => _assets.TryGetValue(name, out Asset? asset) ? asset.Version : string.Empty;

    public IResult Serve(HttpContext context, string file)
    {
        if (!_assets.TryGetValue(file, out Asset? asset))
            return Results.NotFound();

        // Versioned URLs never change; unversioned ones are revalidated with the ETag.
        context.Response.Headers.CacheControl = context.Request.Query.ContainsKey("v")
            ? "public, max-age=31536000, immutable"
            : "no-cache";
        context.Response.Headers.XContentTypeOptions = "nosniff";

        return Results.Bytes(asset.Content, asset.ContentType, entityTag: asset.ETag);
    }

    private static string GetContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };

    private sealed record Asset(byte[] Content, string ContentType, string Version, EntityTagHeaderValue ETag);

    private sealed class AssetEndpointMetadata : IExcludeFromPermissionCatalogMetadata;
}
