using DynamicAuthorization.Mvc.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace DynamicAuthorization.Mvc.MsSqlServerStore
{
    public class RoleAccessStore : IRoleAccessStore
    {
        private readonly SqlOptions _options;
        private readonly ILogger<RoleAccessStore> _logger;

        public RoleAccessStore(SqlOptions options, ILogger<RoleAccessStore> logger)
        {
            _options = options;
            _logger = logger;
        }

        public async Task<bool> AddRoleAccessAsync(RoleAccess roleAccess)
        {
            try
            {
                using (var conn = new SqlConnection(_options.ConnectionString))
                {
                    const string insertCommand = "INSERT INTO RoleAccess VALUES(@RoleId, @Access)";
                    using (var cmd = new SqlCommand(insertCommand, conn))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@RoleId", roleAccess.RoleId);

                        var access = JsonConvert.SerializeObject(roleAccess.Controllers);
                        cmd.Parameters.AddWithValue("@Access", access);

                        conn.Open();
                        var affectedRows = await cmd.ExecuteNonQueryAsync();
                        return affectedRows > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error has occurred while inserting access into RoleAccess table");
                return false;
            }
        }

        public Task<bool> EditRoleAccessAsync(RoleAccess roleAccess)
        {
            throw new System.NotImplementedException();
        }

        public Task<bool> RemoveRoleAccessAsync(string roleId)
        {
            throw new System.NotImplementedException();
        }

        public async Task<RoleAccess> GetRoleAccessAsync(string roleId)
        {
            try
            {
                using (var conn = new SqlConnection(_options.ConnectionString))
                {
                    const string query = "SELECT [Id], [RoleId], [Access] FROM [RoleAccess] WHERE [RoleId] = @RoleId";
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@RoleId", roleId);
                        conn.Open();
                        var reader = await cmd.ExecuteReaderAsync();
                        if (!reader.Read())
                            return null;

                        var roleAccess = new RoleAccess();
                        roleAccess.Id = int.Parse(reader[0].ToString());
                        roleAccess.RoleId = reader[1].ToString();
                        var json = reader[2].ToString();
                        roleAccess.Controllers = JsonConvert.DeserializeObject<IEnumerable<MvcControllerInfo>>(json);

                        return roleAccess;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error has occurred while inserting access into RoleAccess table");
                return null;
            }
        }

        public Task<bool> HasAccessToActionAsync(string actionId, params string[] roles)
        {
            throw new System.NotImplementedException();
        }
    }
}