using DynamicAuthorization.Mvc.Core.Models;
using System;
using System.Text;

namespace DynamicAuthorization.Mvc.MsSqlServerStore
{
    internal class SqlTableCreator
    {
        public void CreateTable()
        {
            try
            {
                using (var conn = _sqlConnectionFactory.Create())
                {
                    var sql = GetTableDdl();
                    using (var cmd = conn.CreateCommand(sql))
                    {
                        conn.Open();
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception creating table AccessRole:\n{ex}");
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
            sql.AppendLine("CREATE NONCLUSTERED INDEX [IX_AspNetRoleClaims_RoleId] ");
            sql.AppendLine("ON [dbo].[AspNetRoleClaims] ([RoleId] ASC);");
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
            if (DynamicAuthorizationOptions.RoleType == typeof(Guid))
                return "uniqueidentifier";

            if (DynamicAuthorizationOptions.RoleType == typeof(string))
                return "nvarchar(450) COLLATE SQL_Latin1_General_CP1_CI_AS";

            return "int";
        }
    }
}