using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal static class NameTagStateRepository
    {
        internal static NameTagState Load(SqliteConnection connection, int characterId)
        {
            var state = new NameTagState();
            if (connection == null || characterId <= 0)
                return state;

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT item_id, expire_time
FROM character_name_tag_state
WHERE character_id = @characterId
LIMIT 1;";
                command.Parameters.AddWithValue("@characterId", characterId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return state;

                    state.Set(reader.GetInt32(0), reader.GetInt32(1));
                }
            }

            return state;
        }

        internal static void Upsert(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int itemId,
            int expireTime)
        {
            if (connection == null || transaction == null || characterId <= 0)
                return;

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO character_name_tag_state (
    character_id, item_id, expire_time, updated_at
) VALUES (
    @characterId, @itemId, @expireTime, CURRENT_TIMESTAMP
)
ON CONFLICT(character_id) DO UPDATE SET
    item_id = excluded.item_id,
    expire_time = excluded.expire_time,
    updated_at = CURRENT_TIMESTAMP;";
                command.Parameters.AddWithValue("@characterId", characterId);
                command.Parameters.AddWithValue("@itemId", Math.Max(0, itemId));
                command.Parameters.AddWithValue("@expireTime", Math.Max(0, expireTime));
                command.ExecuteNonQuery();
            }
        }

        internal static bool ClearExpired(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            uint now)
        {
            if (connection == null || transaction == null || characterId <= 0)
                return false;

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
UPDATE character_name_tag_state
SET item_id = 0,
    expire_time = 0,
    updated_at = CURRENT_TIMESTAMP
WHERE character_id = @characterId
  AND item_id > 0
  AND expire_time > 0
  AND expire_time <= @now;";
                command.Parameters.AddWithValue("@characterId", characterId);
                command.Parameters.AddWithValue("@now", now);
                return command.ExecuteNonQuery() > 0;
            }
        }

        internal static void EnsureTableAndMigrateLegacy(SqliteConnection connection)
        {
            if (connection == null)
                return;

            using (var transaction = connection.BeginTransaction())
            {
                EnsureTable(connection, transaction);
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
SELECT c.character_id,
       COALESCE(s.name_tag_item_id, 0),
       COALESCE(s.name_tag_expire_time, 0),
       COALESCE(e.item_id, 0),
       COALESCE(e.expire_time, 0)
FROM characters c
LEFT JOIN character_subtype1_fields s ON s.character_id = c.character_id
LEFT JOIN character_equipped_entries e ON e.character_id = c.character_id AND e.slot = 28;";
                    var rows = new List<(int characterId, int itemId, int expireTime)>();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var characterId = reader.GetInt32(0);
                            var itemId = reader.GetInt64(1) > 0
                                ? Convert.ToInt32(reader.GetInt64(1))
                                : Convert.ToInt32(reader.GetInt64(3));
                            var expireTime = reader.GetInt64(2) > 0
                                ? Convert.ToInt32(reader.GetInt64(2))
                                : Convert.ToInt32(reader.GetInt64(4));

                            if (itemId <= 0)
                                continue;

                            rows.Add((characterId, itemId, expireTime));
                        }
                    }

                    foreach (var row in rows)
                        Upsert(connection, transaction, row.characterId, row.itemId, row.expireTime);
                }

                transaction.Commit();
            }
        }

        private static void EnsureTable(SqliteConnection connection, SqliteTransaction transaction)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS character_name_tag_state (
    character_id INTEGER PRIMARY KEY,
    item_id INTEGER NOT NULL DEFAULT 0,
    expire_time INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);";
                command.ExecuteNonQuery();
            }
        }
    }
}
