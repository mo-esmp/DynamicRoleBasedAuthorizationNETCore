using DynamicAuthorization.AspNetCore.EntityFrameworkCore;
using DynamicAuthorization.AspNetCore.Management;
using DynamicAuthorization.AspNetCore.Tests.Infrastructure;

namespace DynamicAuthorization.AspNetCore.Tests;

public sealed class V1ImportTests
{
    // The file written by DynamicAuthorization.Mvc.JsonStore 1.x (camelCase, one array per collection).
    private const string JsonStoreFile = """
        {
          "roleAccess": [
            {
              "id": 0,
              "roleId": "role-1",
              "controllers": [
                {
                  "id": ":Orders",
                  "name": "Orders",
                  "displayName": "Order Management",
                  "areaName": null,
                  "actions": [
                    { "id": ":Orders:Index", "name": "Index", "displayName": null, "controllerId": ":Orders" },
                    { "id": ":Orders:Edit", "name": "Edit", "displayName": null, "controllerId": ":Orders" }
                  ]
                },
                {
                  "id": "Admin:Settings",
                  "name": "Settings",
                  "areaName": "Admin",
                  "actions": [ { "name": "Index" } ]
                }
              ]
            },
            { "id": 0, "roleId": "role-2", "controllers": [] }
          ]
        }
        """;

    // The Access column written by DynamicAuthorization.Mvc.MsSqlServerStore 1.x (PascalCase).
    private const string SqlServerAccessColumn = """
        [{"Id":":Orders","Name":"Orders","DisplayName":null,"AreaName":null,"Actions":[{"Id":":Orders:Delete","Name":"Delete","DisplayName":null,"ControllerId":":Orders"}]}]
        """;

    [Fact]
    public void Json_store_file_is_converted_to_mvc_permissions()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> roles = DynamicAuthorizationV1Import.ParseJsonStore(JsonStoreFile);

        Assert.Equal(["mvc::Orders:Index", "mvc::Orders:Edit", "mvc:Admin:Settings:Index"], roles["role-1"]);
        Assert.Empty(roles["role-2"]);
    }

    [Fact]
    public void Sql_server_access_column_is_converted_to_mvc_permissions() =>
        Assert.Equal(["mvc::Orders:Delete"], DynamicAuthorizationV1Import.ParseAccess(SqlServerAccessColumn));

    [Fact]
    public async Task Import_merges_permissions_skips_missing_roles_and_can_run_again()
    {
        await using TestApp app = await TestApp.StartAsync();
        string roleId = await app.CreateRoleAsync("Editors", "secure.read");
        Dictionary<string, IReadOnlyList<string>> access = new()
        {
            [roleId] = ["mvc::Orders:Index", "mvc::Orders:Edit", "mvc::Removed:Index"],
            ["missing-role"] = ["mvc::Orders:Delete"],
        };

        DynamicAuthorizationV1ImportResult first = await app.Services.ImportDynamicAuthorizationV1Async(access);
        DynamicAuthorizationV1ImportResult second = await app.Services.ImportDynamicAuthorizationV1Async(access);
        RoleDetails? role = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().FindRoleAsync(roleId));

        Assert.Equal(2, first.GrantedCount);
        Assert.Equal(0, second.GrantedCount);
        Assert.Equal(["orders.read", "orders.update", "secure.read"], role!.Permissions.Order());
        Assert.Equal(["mvc::Removed:Index"], first.UnmatchedActions);
    }

    [Fact]
    public async Task Import_reports_access_that_crud_permissions_widen()
    {
        await using TestApp app = await TestApp.StartAsync();
        string roleId = await app.CreateRoleAsync("Editors");

        DynamicAuthorizationV1ImportResult result = await app.Services.ImportDynamicAuthorizationV1Async(
            new Dictionary<string, IReadOnlyList<string>> { [roleId] = ["mvc::Orders:Index"] });

        Assert.Contains("Editors: orders.read also grants mvc::Orders:Details", result.WidenedAccess);
        Assert.DoesNotContain(result.WidenedAccess, w => w.EndsWith("mvc::Orders:Index", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_keeps_endpoint_keys_with_the_endpoint_convention()
    {
        await using TestApp app = await TestApp.StartAsync(o => o.Authorization = a => a.Convention = PermissionConvention.Endpoint);
        string roleId = await app.CreateRoleAsync("Editors");

        DynamicAuthorizationV1ImportResult result = await app.Services.ImportDynamicAuthorizationV1Async(
            new Dictionary<string, IReadOnlyList<string>> { [roleId] = ["mvc::Orders:Index", "mvc::Orders:Edit"] });
        RoleDetails? role = await app.WithScopeAsync(s => s.GetRequiredService<IRoleManagementService>().FindRoleAsync(roleId));

        Assert.Equal(2, result.GrantedCount);
        Assert.Empty(result.WidenedAccess);
        Assert.Equal(["mvc::Orders:Edit", "mvc::Orders:Index"], role!.Permissions.Order());
    }
}
