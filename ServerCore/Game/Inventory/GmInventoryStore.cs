using System;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    // GM自有: 离线背包加载/保存助手。
    // 加载走服务端 InventoryService.LoadFromDb(离线/诊断场景允许),
    // 保存复用服务端 InventoryPersistenceService.SaveDirtyInTransaction 的同事务写入,
    // 与服务端在线保存共用同一套 dirty 槽位语义, GM 不直接拼 SQL 写物品主表。
    internal static class GmInventoryStore
    {
        internal static InventoryService Load(
            SqliteConnection connection,
            int characterId,
            int accountId)
        {
            return InventoryService.LoadFromDb(connection, characterId, accountId);
        }

        internal static bool Save(
            SqliteConnection connection,
            int characterId,
            InventoryService inventory)
        {
            if (inventory == null)
                return false;

            var lease = new InventoryLease(Guid.NewGuid(), characterId, inventory, version: 1);
            using (var transaction = connection.BeginTransaction())
            {
                if (!InventoryPersistenceService.SaveDirtyInTransaction(
                        connection,
                        transaction,
                        lease))
                    return false;

                transaction.Commit();
            }

            inventory.ClearDirtyState();
            return true;
        }
    }
}
