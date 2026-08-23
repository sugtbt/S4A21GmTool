using System;
using DfoGmTool.ServerCore.Game.Inventory;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.CharacterData
{
    internal sealed class CharacterItemStateRepository
    {
        internal static void LoadInto(
            SqliteConnection connection,
            int characterId,
            InventoryItemStateBook itemStates)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (itemStates == null)
                throw new ArgumentNullException(nameof(itemStates));

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT state_kind, item_id, expire_time
FROM character_item_states
WHERE character_id = @cid
ORDER BY state_kind, item_id;";
                command.Parameters.AddWithValue("@cid", characterId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        itemStates.Attach(
                            reader.GetString(0),
                            reader.GetInt32(1),
                            reader.GetInt32(2));
                    }
                }
            }
        }

        internal static void SaveAll(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            InventoryItemStateBook itemStates)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (itemStates == null)
                throw new ArgumentNullException(nameof(itemStates));

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM character_item_states WHERE character_id = @cid;";
                command.Parameters.AddWithValue("@cid", characterId);
                command.ExecuteNonQuery();
            }

            foreach (var entry in itemStates.GetEntries())
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
INSERT INTO character_item_states (
    character_id, state_kind, item_id, expire_time, updated_at
) VALUES (
    @cid, @kind, @iid, @expireTime, CURRENT_TIMESTAMP
);";
                    command.Parameters.AddWithValue("@cid", characterId);
                    command.Parameters.AddWithValue("@kind", entry.StateKind);
                    command.Parameters.AddWithValue("@iid", entry.ItemId);
                    command.Parameters.AddWithValue("@expireTime", entry.ExpireTime);
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
