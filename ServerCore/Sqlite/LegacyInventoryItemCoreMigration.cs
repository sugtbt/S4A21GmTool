using System;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Sqlite
{
    internal static class LegacyInventoryItemCoreMigration
    {
        private static readonly string[] LegacyTables =
        {
            "character_items",
            "account_cargo_items",
            "account_cargo_new_items",
            "character_titlebook",
            "character_new_titlebook",
            "character_equipped_entries",
            "character_new_items",
            "item_audit_log",
            "character_achievement_chunks",
            "character_achievement_complete",
            "character_pet_welcome_cache",
            "character_sort_item_locks",
        };

        internal sealed class Result
        {
            internal long BeforeVersion { get; set; }

            internal long AfterVersion { get; set; }

            internal long PaddedItemCoreRows { get; set; }

            internal int DroppedLegacyTables { get; set; }
        }

        internal static bool CanApply(SqliteConnection connection)
        {
            if (TableExists(connection, null, "schema_metadata"))
                return false;

            if (TableExists(connection, null, "mailbox_attachments"))
                return true;

            foreach (var tableName in LegacyTables)
            {
                if (TableExists(connection, null, tableName))
                    return true;
            }

            return false;
        }

        internal static Result Apply(
            SqliteConnection connection,
            string schemaSql)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (string.IsNullOrWhiteSpace(schemaSql))
                throw new ArgumentException("schema SQL is required.", nameof(schemaSql));
            if (!CanApply(connection))
                throw new InvalidOperationException("数据库不是可识别的旧库存结构，拒绝执行主动迁移。");

            var result = new Result
            {
                BeforeVersion = SqliteMigrations.ReadVersion(connection),
            };

            using (var transaction = connection.BeginTransaction())
            {
                ExecuteSql(connection, transaction, schemaSql);
                result.PaddedItemCoreRows = CountLegacyItemCoreRows(connection, transaction);
                ImportAccountCargoNewItems(connection, transaction);
                ImportCharacterNewTitleBook(connection, transaction);
                ImportCharacterAchievements(connection, transaction);

                SqliteMigrations.ApplyExpandItemCoreTo99(connection, transaction);
                result.DroppedLegacyTables = DropLegacyTables(connection, transaction);
                SqliteMigrations.MarkCurrent(connection, transaction);

                transaction.Commit();
            }

            result.AfterVersion = SqliteMigrations.ReadVersion(connection);
            return result;
        }

        private static long CountLegacyItemCoreRows(
            SqliteConnection connection,
            SqliteTransaction transaction)
        {
            long count = 0;
            count += CountRows(connection, transaction, "character_new_items");
            count += CountRows(connection, transaction, "account_cargo_new_items");
            count += CountRows(connection, transaction, "character_new_titlebook");
            count += CountRows(connection, transaction, "character_inventory_items");
            count += CountRows(connection, transaction, "account_inventory_items");
            count += CountRows(connection, transaction, "character_titlebook_items");
            count += CountRows(connection, transaction, "mailbox_attachments");
            return count;
        }

        private static long CountRows(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string tableName)
        {
            if (!TableExists(connection, transaction, tableName))
                return 0;

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    $"SELECT COUNT(*) FROM {tableName} WHERE item_core IS NOT NULL AND length(item_core) = 82;";
                return Convert.ToInt64(command.ExecuteScalar());
            }
        }

        private static void ImportAccountCargoNewItems(
            SqliteConnection connection,
            SqliteTransaction transaction)
        {
            if (!TableExists(connection, transaction, "account_cargo_new_items")
                || !TableExists(connection, transaction, "account_inventory_items"))
                return;

            EnsureLegacyItemCoreLengths(connection, transaction, "account_cargo_new_items", nullable: false);
            EnsureNoDuplicateGroups(
                connection,
                transaction,
                @"
SELECT COUNT(*)
FROM (
    SELECT account_id, slot_index
    FROM account_cargo_new_items
    GROUP BY account_id, slot_index
    HAVING COUNT(*) > 1
);",
                "account_cargo_new_items 存在重复 account/slot，无法迁移。");

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT COUNT(*)
FROM account_cargo_new_items
WHERE list_type <> 12;";
                var unsupportedCount = Convert.ToInt64(command.ExecuteScalar());
                if (unsupportedCount != 0)
                {
                    throw new InvalidOperationException(
                        $"account_cargo_new_items 存在 {unsupportedCount} 条非账号仓库 list_type，无法迁移。");
                }
            }

            ExecuteSql(connection, transaction, @"
INSERT INTO account_inventory_items (
    item_uid, account_id, slot_index, item_core, created_at, updated_at
)
SELECT src.item_uid,
       src.account_id,
       src.slot_index,
       CASE
           WHEN length(src.item_core) = 82 THEN CAST(src.item_core || zeroblob(17) AS BLOB)
           ELSE src.item_core
       END,
       COALESCE(src.created_at, CURRENT_TIMESTAMP),
       COALESCE(src.updated_at, CURRENT_TIMESTAMP)
FROM account_cargo_new_items src
WHERE src.account_id IN (SELECT account_id FROM accounts);");
        }

        private static void ImportCharacterNewTitleBook(
            SqliteConnection connection,
            SqliteTransaction transaction)
        {
            if (!TableExists(connection, transaction, "character_new_titlebook")
                || !TableExists(connection, transaction, "character_titlebook_items"))
                return;

            EnsureLegacyItemCoreLengths(connection, transaction, "character_new_titlebook", nullable: false);
            EnsureNoDuplicateGroups(
                connection,
                transaction,
                @"
SELECT COUNT(*)
FROM (
    SELECT character_id, category, slot_index
    FROM character_new_titlebook
    GROUP BY character_id, category, slot_index
    HAVING COUNT(*) > 1
);",
                "character_new_titlebook 存在重复 character/category/slot，无法迁移。");

            ExecuteSql(connection, transaction, @"
INSERT INTO character_titlebook_items (
    character_id, category, slot_index, item_core, updated_at
)
SELECT src.character_id,
       src.category,
       src.slot_index,
       CASE
           WHEN length(src.item_core) = 82 THEN CAST(src.item_core || zeroblob(17) AS BLOB)
           ELSE src.item_core
       END,
       COALESCE(src.updated_at, CURRENT_TIMESTAMP)
FROM character_new_titlebook src
WHERE src.character_id IN (SELECT character_id FROM characters);");
        }

        private static void ImportCharacterAchievements(
            SqliteConnection connection,
            SqliteTransaction transaction)
        {
            if (!TableExists(connection, transaction, "character_achievement_complete")
                || !TableExists(connection, transaction, "character_achievements"))
                return;

            EnsureNoDuplicateGroups(
                connection,
                transaction,
                @"
SELECT COUNT(*)
FROM (
    SELECT character_id, achievement_id
    FROM character_achievement_complete
    GROUP BY character_id, achievement_id
    HAVING COUNT(*) > 1
);",
                "character_achievement_complete 存在重复 character/achievement，无法迁移。");

            ExecuteSql(connection, transaction, @"
INSERT INTO character_achievements (
    character_id, sort_order, achievement_id, p1, p2, p3, p4
)
SELECT src.character_id,
       src.sort_order,
       src.achievement_id,
       src.p1,
       src.p2,
       src.p3,
       src.p4
FROM character_achievement_complete src
WHERE src.character_id IN (SELECT character_id FROM characters);");
        }

        private static void EnsureLegacyItemCoreLengths(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string tableName,
            bool nullable)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = nullable
                    ? $"SELECT COUNT(*) FROM {tableName} WHERE item_core IS NOT NULL AND length(item_core) NOT IN (82, 99);"
                    : $"SELECT COUNT(*) FROM {tableName} WHERE item_core IS NULL OR length(item_core) NOT IN (82, 99);";
                var invalidCount = Convert.ToInt64(command.ExecuteScalar());
                if (invalidCount != 0)
                {
                    throw new InvalidOperationException(
                        $"{tableName}.item_core 存在 {invalidCount} 条非82/99字节数据，无法迁移。");
                }
            }
        }

        private static void EnsureNoDuplicateGroups(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string sql,
            string message)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                var duplicateCount = Convert.ToInt64(command.ExecuteScalar());
                if (duplicateCount != 0)
                    throw new InvalidOperationException(message);
            }
        }

        private static int DropLegacyTables(
            SqliteConnection connection,
            SqliteTransaction transaction)
        {
            var dropped = 0;
            foreach (var tableName in LegacyTables)
            {
                if (!TableExists(connection, transaction, tableName))
                    continue;

                ExecuteSql(connection, transaction, $"DROP TABLE IF EXISTS {tableName};");
                dropped++;
            }

            return dropped;
        }

        private static bool TableExists(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string tableName)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT COUNT(*)
FROM sqlite_master
WHERE type = 'table' AND name = @name;";
                command.Parameters.AddWithValue("@name", tableName);
                return Convert.ToInt32(command.ExecuteScalar()) != 0;
            }
        }

        private static void ExecuteSql(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
    }
}
