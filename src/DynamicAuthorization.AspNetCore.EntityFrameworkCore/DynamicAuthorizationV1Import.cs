using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using DynamicAuthorization.AspNetCore.Management;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicAuthorization.AspNetCore.EntityFrameworkCore;

/// <summary>
///   Imports role access saved by DynamicAuthorization.Mvc 1.x into the Entity Framework Core store.
/// </summary>
/// <remarks>
///   Version 1 stored, per role, the controllers and actions the role could access. Each action becomes an
///   <c>mvc:{area}:{controller}:{action}</c> permission. Imports merge with existing permissions, skip roles that no
///   longer exist, and can safely run more than once.
/// </remarks>
public static partial class DynamicAuthorizationV1Import
{
    /// <summary>
    ///   Imports the file written by the 1.x JSON store (<c>DynamicAuthorization.Mvc.JsonStore</c>).
    /// </summary>
    /// <param name="services">The application services.</param>
    /// <param name="filePath">The path of the JSON file, <c>RoleAccess.json</c> by default in 1.x.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of permissions that were granted.</returns>
    public static async Task<int> ImportDynamicAuthorizationV1JsonStoreAsync(
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
    /// <returns>The number of permissions that were granted.</returns>
    public static async Task<int> ImportDynamicAuthorizationV1SqlServerStoreAsync(
        this IServiceProvider services,
        string tableName = "RoleAccess",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!TableNameRegex().IsMatch(tableName))
            throw new ArgumentException($"'{tableName}' is not a valid table name.", nameof(tableName));

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
                        roleAccess[roleId] = ParseAccess(reader.GetString(1));
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
    ///   Grants permissions to roles, keeping the permissions they already have.
    /// </summary>
    /// <param name="services">The application services.</param>
    /// <param name="roleAccess">The permissions to grant, by role identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of permissions that were granted.</returns>
    public static async Task<int> ImportDynamicAuthorizationV1Async(
        this IServiceProvider services,
        IReadOnlyDictionary<string, IReadOnlyList<string>> roleAccess,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(roleAccess);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IRoleManagementService roles = scope.ServiceProvider.GetRequiredService<IRoleManagementService>();
        IPermissionStore store = scope.ServiceProvider.GetRequiredService<IPermissionStore>();

        int granted = 0;
        foreach ((string roleId, IReadOnlyList<string> permissions) in roleAccess)
        {
            RoleDetails? role = await roles.FindRoleAsync(roleId, cancellationToken).ConfigureAwait(false);
            if (role is null)
                continue;

            PermissionChangeSet changes = await store
                .SetPermissionsAsync(role.Id, role.Permissions.Concat(permissions), cancellationToken)
                .ConfigureAwait(false);
            granted += changes.Added.Count;
        }

        return granted;
    }

    /// <summary>
    ///   Parses the file written by the 1.x JSON store.
    /// </summary>
    /// <param name="json">The file content.</param>
    /// <returns>The permissions, by role identifier.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseJsonStore(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        Dictionary<string, IReadOnlyList<string>> result = new(StringComparer.Ordinal);
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return result;

        // The store keeps one array per collection, e.g. { "roleAccess": [ { "roleId": "...", "controllers": [...] } ] }.
        foreach (JsonProperty collection in document.RootElement.EnumerateObject())
        {
            if (collection.Value.ValueKind != JsonValueKind.Array)
                continue;

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
    /// <returns>The permission keys.</returns>
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
            return permissions;

        foreach (JsonElement controller in controllers.EnumerateArray())
        {
            string? controllerName = GetString(controller, "Name");
            if (string.IsNullOrEmpty(controllerName) || !TryGetProperty(controller, "Actions", out JsonElement actions)
                || actions.ValueKind != JsonValueKind.Array)
                continue;

            string? area = GetString(controller, "AreaName");
            foreach (JsonElement action in actions.EnumerateArray())
            {
                if (GetString(action, "Name") is { Length: > 0 } actionName)
                    permissions.Add(PermissionKeys.ForAction(string.IsNullOrEmpty(area) ? null : area, controllerName, actionName));
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
