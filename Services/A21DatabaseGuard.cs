using System;
using DfoGmTool.ServerCore.Sqlite;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    // 与服务端 SqliteMigrations 同一条准入：schema_metadata.baseline_id=86jp-database-v1。
    // 已迁移的 A21 库可能仍带 character_invisible_falgs 等遗留表，不能据此判成 A12。
    internal static class A21DatabaseGuard
    {
        internal static void EnsureA21Baseline(SqliteConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            if (SqliteMigrations.HasCurrentBaseline(connection))
                return;

            throw new InvalidOperationException(BuildRejectionMessage(connection));
        }

        private static string BuildRejectionMessage(SqliteConnection connection)
        {
            if (HasLegacyItemCoreConstraint(connection) || !TableExists(connection, "schema_metadata"))
            {
                return "这是 A12 数据库，A21 GM 工具不会解析。"
                    + "请选择 servers4a21 的 inventory.db"
                    + "（需要 schema_metadata.baseline_id=86jp-database-v1）。";
            }

            return "数据库不是 A21 当前基线（86jp-database-v1），已停止解析。"
                + "请打开 A21 服务端 Data/inventory.db，不要使用 A12 或其他旧库。";
        }

        private static bool HasLegacyItemCoreConstraint(SqliteConnection connection)
        {
            foreach (var tableName in new[]
            {
                "character_inventory_items",
                "account_inventory_items",
                "character_titlebook_items",
                "mailbox_attachments",
            })
            {
                var createSql = ReadTableSql(connection, tableName);
                if (string.IsNullOrEmpty(createSql))
                    continue;
                if (createSql.IndexOf("length(item_core) = 82", StringComparison.OrdinalIgnoreCase) >= 0
                    || createSql.IndexOf("length(item_core)=82", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool TableExists(SqliteConnection connection, string tableName)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT COUNT(*)
FROM sqlite_master
WHERE type = 'table' AND name = @name;";
                command.Parameters.AddWithValue("@name", tableName);
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        private static string ReadTableSql(SqliteConnection connection, string tableName)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT sql
FROM sqlite_master
WHERE type = 'table' AND name = @name;";
                command.Parameters.AddWithValue("@name", tableName);
                var value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? null : Convert.ToString(value);
            }
        }
    }
}
