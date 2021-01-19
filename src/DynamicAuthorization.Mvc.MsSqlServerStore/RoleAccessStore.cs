using DynamicAuthorization.Mvc.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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

        public async Task<bool> EditRoleAccessAsync(RoleAccess roleAccess)
        {
            try
            {
                int affectedRows;
                using (var conn = new SqlConnection(_options.ConnectionString))
                {
                    const string insertCommand = "UPDATE RoleAccess SET [Access] = @Access WHERE [RoleId] = @RoleId";
                    using (var cmd = new SqlCommand(insertCommand, conn))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@RoleId", roleAccess.RoleId);
                        if (roleAccess.Controllers != null)
                        {
                            var access = JsonConvert.SerializeObject(roleAccess.Controllers);
                            cmd.Parameters.AddWithValue("@Access", access);
                        }
                        else
                            cmd.Parameters.AddWithValue("@Access", DBNull.Value);

                        conn.Open();
                        affectedRows = await cmd.ExecuteNonQueryAsync();
                    }
                }

                if (affectedRows > 0)
                    return true;

                return await AddRoleAccessAsync(roleAccess);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error has occurred while editing access into RoleAccess table");
                return false;
            }
        }

        public async Task<bool> RemoveRoleAccessAsync(string roleId)
        {
            try
            {
                using (var conn = new SqlConnection(_options.ConnectionString))
                {
                    const string insertCommand = "DELETE FROM RoleAccess WHERE [RoleId] = @RoleId";
                    using (var cmd = new SqlCommand(insertCommand, conn))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@RoleId", roleId);

                        conn.Open();
                        var affectedRows = await cmd.ExecuteNonQueryAsync();

                        return affectedRows > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error has occurred while deleting access from RoleAccess table");
                return false;
            }
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
                _logger.LogError(ex, "An error has occurred while getting data from RoleAccess table");
                return null;
            }
        }

        public async Task<bool> HasAccessToActionAsync(string actionId, params string[] roles)
        {
            try
            {
                using (var conn = new SqlConnection(_options.ConnectionString))
                {
                    using (var cmd = new SqlCommand())
                    {
                        var parameters = new string[roles.Length];
                        for (var i = 0; i < roles.Length; i++)
                        {
                            parameters[i] = $"@RoleId{i}";
                            cmd.Parameters.AddWithValue(parameters[i], roles[i]);
                        }
                        var query = $"SELECT [Access] FROM [RoleAccess] WHERE [RoleId] IN ({string.Join(", ", parameters)})";

                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = query;
                        cmd.Connection = conn;

                        conn.Open();
                        var reader = await cmd.ExecuteReaderAsync();

                        var list = new List<MvcActionInfo>();
                        while (reader.Read())
                        {
                            var json = reader[0].ToString();
                            if (string.IsNullOrEmpty(json))
                                continue;

                            var controllers = JsonConvert.DeserializeObject<IEnumerable<MvcControllerInfo>>(json);
                            list.AddRange(controllers.SelectMany(c => c.Actions));
                        }

                        return list.Any(a => a.Id == actionId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error has occurred while getting data from RoleAccess table");
                return false;
            }
        }
    }
}