using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using DynamicAuthorization.AspNetCore.Management;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DynamicAuthorization.AspNetCore.EntityFrameworkCore;

/// <summary>
///   Imports role access saved by DynamicAuthorization.Mvc 1.x into the Entity Framework Core store.
/// </summary>
/// <remarks>
///   Version 1 stored, per role, the controllers and actions the role could access. Each action is granted the
///   permissions its endpoints require today, for example <c>orders.update</c> for <c>Orders/Edit</c>. Run the import
///   after the application's endpoints are mapped. Imports merge with existing permissions, skip roles that no longer
///   exist, and can safely run more than once.
///   <para>
///     Permissions of the <see cref="PermissionConvention.Crud"/> convention can be broader than 1.x actions: a role
///     that could open <c>Orders/Index</c> but not <c>Orders/Details</c> gets <c>orders.read</c>, which covers both.
///     Review <see cref="DynamicAuthorizationV1ImportResult.WidenedAccess"/> after importing.
///   </para>
/// </remarks>
public static partial class DynamicAuthorizationV1Import
{
    /// <summary>
    ///   Imports the file written by the 1.x JSON store (<c>DynamicAuthorization.Mvc.JsonStore</c>).
    /// </summary>
    /// <param name="services">The application services.</param>
    /// <param name="filePath">The path of the JSON file, <c>RoleAccess.json</c> by default in 1.x.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>What was imported.</returns>
    public static async Task<DynamicAuthorizationV1ImportResult> ImportDynamicAuthorizationV1JsonStoreAsync(
        this IServiceProvider services,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        string json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);

        return await services.ImportDynamicAuthorizationV1Async(ParseJsonStore(json), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///   Imports the table written by the 1.x SQL Server store (<c>DynamicAuthorization.Mvc.MsSqlServerStore</c>),
    ///   reading it through the store's <see cref="DbContext"/>.
    /// </summary>
    /// <param name="services">The application services.</param>
    /// <param name="tableName">The name of the 1.x table, <c>RoleAccess</c> by default.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>What was imported.</returns>
    public static async Task<DynamicAuthorizationV1ImportResult> ImportDynamicAuthorizationV1SqlServerStoreAsync(
        this IServiceProvider services,
        string tableName = "RoleAccess",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!TableNameRegex().IsMatch(tableName))
        {
            throw new ArgumentException($"'{tableName}' is not a valid table name.", nameof(tableName));
        }

        Dictionary<string, IReadOnlyList<string>> roleAccess = new(StringComparer.Ordinal);
        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            IdentityModelTypes types = scope.ServiceProvider.GetRequiredService<IdentityModelTypes>();
            DbContext context = (DbContext)scope.ServiceProvider.GetRequiredService(types.Context);
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
#pragma warning disable CA2100 // The table name is validated above.
                command.CommandText = $"SELECT [RoleId], [Access] FROM {tableName}";
#pragma warning restore CA2100
                await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    string? roleId = Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(roleId) && !await reader.IsDBNullAsync(1, cancellationToken).ConfigureAwait(false))
                    {
                        roleAccess[roleId] = ParseAccess(reader.GetString(1));
                    }
                }
            }
            finally
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }

        return await services.ImportDynamicAuthorizationV1Async(roleAccess, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///   Grants roles the permissions required by 1.x actions, keeping the permissions they already have.
    /// </summary>
    /// <param name="services">The application services.</param>
    /// <param name="roleAccess">The 1.x actions, as <c>mvc:{area}:{controller}:{action}</c> keys, by role identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>What was imported.</returns>
    public static async Task<DynamicAuthorizationV1ImportResult> ImportDynamicAuthorizationV1Async(
        this IServiceProvider services,
        IReadOnlyDictionary<string, IReadOnlyList<string>> roleAccess,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(roleAccess);

        PermissionCatalog? catalog = services.GetService<IPermissionCatalogProvider>()?.GetCatalog();
        bool operationsImplyRead = services.GetService<IOptions<DynamicAuthorizationOptions>>()?.Value.OperationsImplyRead ?? false;

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IRoleManagementService roles = scope.ServiceProvider.GetRequiredService<IRoleManagementService>();
        IPermissionStore store = scope.ServiceProvider.GetRequiredService<IPermissionStore>();

        int granted = 0;
        SortedSet<string> unmatched = new(StringComparer.OrdinalIgnoreCase);
        SortedSet<string> widened = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string roleId, IReadOnlyList<string> actions) in roleAccess)
        {
            RoleDetails? role = await roles.FindRoleAsync(roleId, cancellationToken).ConfigureAwait(false);
            if (role is null)
            {
                continue;
            }

            List<string> permissions = catalog is null ? [.. actions] : MapActions(catalog, actions, unmatched);
            if (catalog is not null)
            {
                FindWidenedAccess(catalog, role.Name, actions, permissions, operationsImplyRead, widened);
            }

            PermissionChangeSet changes = await store
                .SetPermissionsAsync(role.Id, role.Permissions.Concat(permissions), cancellationToken)
                .ConfigureAwait(false);
            granted += changes.Added.Count;
        }

        return new DynamicAuthorizationV1ImportResult(granted, [.. unmatched], [.. widened]);
    }

    private static List<string> MapActions(PermissionCatalog catalog, IReadOnlyList<string> actions, ISet<string> unmatched)
    {
        // An action can have several endpoints (GET and POST) that require different permissions; grant them all.
        List<string> permissions = [];
        foreach (string action in actions)
        {
            List<string> required =
            [
                .. catalog.Endpoints
                    .Where(e => e.Permission is not null &&
                                string.Equals(e.TargetKey, action, StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.Permission!.Permission)
            ];
            if (required.Count == 0)
            {
                unmatched.Add(action);
            }

            permissions.AddRange(required);
        }

        return [.. permissions.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static void FindWidenedAccess(
        PermissionCatalog catalog,
        string roleName,
        IReadOnlyList<string> actions,
        List<string> permissions,
        bool operationsImplyRead,
        ISet<string> widened)
    {
        HashSet<string> allowed = new(actions, StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> effective = permissions;
        if (operationsImplyRead)
        {
            effective = effective.Concat(permissions
                .Select(p => PermissionKeys.Split(p).Resource)
                .OfType<string>()
                .Select(r => PermissionKeys.ForOperation(r, PermissionOperation.Read)));
        }

        foreach (string permission in effective.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (EndpointAuthorizationInfo endpoint in catalog.GetEndpoints(permission))
            {
                if (endpoint.TargetKey.StartsWith(PermissionKeys.ActionPrefix, StringComparison.OrdinalIgnoreCase) && !allowed.Contains(endpoint.TargetKey))
                {
                    widened.Add($"{roleName}: {permission} also grants {endpoint.TargetKey}");
                }
            }
        }
    }

    /// <summary>
    ///   Parses the file written by the 1.x JSON store.
    /// </summary>
    /// <param name="json">The file content.</param>
    /// <returns>The 1.x actions, as <c>mvc:{area}:{controller}:{action}</c> keys, by role identifier.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseJsonStore(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        Dictionary<string, IReadOnlyList<string>> result = new(StringComparer.Ordinal);
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        // The store keeps one array per collection, e.g. { "roleAccess": [ { "roleId": "...", "controllers": [...] } ] }.
        foreach (JsonProperty collection in document.RootElement.EnumerateObject())
        {
            if (collection.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement item in collection.Value.EnumerateArray())
            {
                if (GetString(item, "RoleId") is { Length: > 0 } roleId
                    && TryGetProperty(item, "Controllers", out JsonElement controllers))
                {
                    result[roleId] = ParseControllers(controllers);
                }
            }
        }

        return result;
    }

    /// <summary>
    ///   Parses the controllers and actions a role could access in 1.x, as stored in the SQL Server store's
    ///   <c>Access</c> column.
    /// </summary>
    /// <param name="accessJson">The JSON array of controllers.</param>
    /// <returns>The actions, as <c>mvc:{area}:{controller}:{action}</c> keys.</returns>
    public static IReadOnlyList<string> ParseAccess(string accessJson)
    {
        ArgumentNullException.ThrowIfNull(accessJson);

        using JsonDocument document = JsonDocument.Parse(accessJson);

        return ParseControllers(document.RootElement);
    }

    private static List<string> ParseControllers(JsonElement controllers)
    {
        List<string> permissions = [];
        if (controllers.ValueKind != JsonValueKind.Array)
        {
            return permissions;
        }

        foreach (JsonElement controller in controllers.EnumerateArray())
        {
            string? controllerName = GetString(controller, "Name");
            if (string.IsNullOrEmpty(controllerName) || !TryGetProperty(controller, "Actions", out JsonElement actions)
                || actions.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            string? area = GetString(controller, "AreaName");
            foreach (JsonElement action in actions.EnumerateArray())
            {
                if (GetString(action, "Name") is { Length: > 0 } actionName)
                {
                    permissions.Add(PermissionKeys.ForAction(string.IsNullOrEmpty(area) ? null : area, controllerName, actionName));
                }
            }
        }

        return [.. permissions.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static string? GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        // 1.x wrote PascalCase (SQL Server store) or camelCase (JSON store) property names.
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_\.\[\]]+$")]
    private static partial Regex TableNameRegex();
}

/// <summary>
///   The outcome of importing 1.x role access.
/// </summary>
/// <param name="GrantedCount">The number of permissions granted to roles.</param>
/// <param name="UnmatchedActions">1.x actions that no longer exist, or no longer require a permission.</param>
/// <param name="WidenedAccess">
///   Endpoints that roles can now access although 1.x did not grant them, because a permission covers more endpoints
///   than the 1.x action did, for example <c>Editors: orders.read also grants mvc::Orders:Details</c>.
/// </param>
public sealed record DynamicAuthorizationV1ImportResult(int GrantedCount, IReadOnlyList<string> UnmatchedActions, IReadOnlyList<string> WidenedAccess);
