using System;
using DfoGmTool.ServerCore.Sqlite;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.Services
{
    // A21 GM 只接受当前基线库。A12 或 82B ItemCore 库在任何物品解析前拒绝。
    internal static class A21DatabaseGuard
    {
        internal static void EnsureA21Baseline(SqliteConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            if (SqliteMigrations.HasCurrentBaseline(connection)
                && !HasLegacyItemCoreConstraint(connection))
                return;

            throw new InvalidOperationException(BuildRejectionMessage(connection));
        }

        private static string BuildRejectionMessage(SqliteConnection connection)
        {
            if (LooksLikeA12(connection))
            {
                return "这是 A12 数据库，A21 GM 工具不会解析。"
                    + "请选择 servers4a21 的 inventory.db"
                    + "（需要 schema_metadata.baseline_id=86jp-database-v1，ItemCore 99 字节）。";
            }

            return "数据库不是 A21 当前基线（86jp-database-v1），已停止解析。"
                + "请打开 A21 服务端 Data/inventory.db，不要使用 A12 或其他旧库。";
        }

        private static bool LooksLikeA12(SqliteConnection connection)
        {
            return TableExists(connection, "character_invisible_falgs")
                || TableExists(connection, "character_new_items")
                || !TableExists(connection, "character_quest_completions")
                || !TableExists(connection, "schema_metadata")
                || HasLegacyItemCoreConstraint(connection);
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
