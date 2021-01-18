using DynamicAuthorization.Mvc.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Text;

namespace DynamicAuthorization.Mvc.MsSqlServerStore
{
    internal class SqlTableCreator
    {
        private readonly SqlOptions _options;
        private readonly ILogger<SqlTableCreator> _logger;

        public SqlTableCreator(SqlOptions options, ILogger<SqlTableCreator> logger)
        {
            _options = options;
            _logger = logger;
        }

        public void CreateTable()
        {
            try
            {
                using (var conn = new SqlConnection(_options.ConnectionString))
                {
                    var sql = GetTableDdl();
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        conn.Open();
                        cmd.ExecuteNonQuery();
                        _options.IsTableCreated = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error has occurred while creating RoleAccess table");
            }
        }

        private string GetTableDdl()
        {
            var sql = new StringBuilder();
            sql.AppendLine("IF NOT EXISTS (SELECT * FROM sys.all_objects WHERE object_id = OBJECT_ID(N'[dbo].[RoleAccess]') AND type IN ('U')) ");
            sql.AppendLine("BEGIN ");
            sql.AppendLine("CREATE TABLE [dbo].[RoleAccess] ( ");
            sql.AppendLine("[Id] int IDENTITY(1,1)  NOT NULL, ");
            sql.AppendLine($"[RoleId] {GetRoleIdType()}  NOT NULL, ");
            sql.AppendLine("[Access] nvarchar(max) COLLATE SQL_Latin1_General_CP1_CI_AS  NULL );");
            sql.AppendLine("ALTER TABLE [dbo].[RoleAccess] SET (LOCK_ESCALATION = TABLE);");
            sql.AppendLine("DBCC CHECKIDENT ('[dbo].[RoleAccess]', RESEED, 1);");
            sql.AppendLine("CREATE NONCLUSTERED INDEX [IX_RoleAccess_RoleId] ");
            sql.AppendLine("ON [dbo].[RoleAccess] ([RoleId] ASC);");
            sql.AppendLine("ALTER TABLE [dbo].[RoleAccess] ADD CONSTRAINT [PK_RoleAccess] PRIMARY KEY CLUSTERED ([Id]) ");
            sql.AppendLine("WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ");
            sql.AppendLine("ON [PRIMARY];");
            sql.AppendLine("ALTER TABLE [dbo].[RoleAccess] ADD CONSTRAINT [FK_RoleAccess_AspNetRoles_RoleId] ");
            sql.AppendLine("FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]) ON DELETE CASCADE ON UPDATE NO ACTION;");
            sql.AppendLine("END");

            return sql.ToString();
        }

        private string GetRoleIdType()
        {
            if (DynamicAuthorizationOptions.KeyType == typeof(Guid))
                return "uniqueidentifier";

            if (DynamicAuthorizationOptions.KeyType == typeof(string))
                return "nvarchar(450) COLLATE SQL_Latin1_General_CP1_CI_AS";

            return "int";
        }
    }
}