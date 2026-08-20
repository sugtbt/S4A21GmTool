using System;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal sealed class InventoryLease
    {
        public InventoryLease(Guid sessionId, int characterId, InventoryService inventory, long version)
        {
            if (sessionId == Guid.Empty)
                throw new ArgumentException("背包租约必须绑定有效的 sessionId。", nameof(sessionId));
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId), "背包租约必须绑定有效的角色 ID。");

            SessionId = sessionId;
            CharacterId = characterId;
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            Version = version;
        }

        public Guid SessionId { get; }

        public int CharacterId { get; }

        public int AccountId => Inventory.AccountId;

        public InventoryService Inventory { get; }

        public object SyncRoot { get; } = new object();

        public long Version { get; }

        public bool IsOwnedBy(Guid sessionId)
        {
            return SessionId == sessionId;
        }
    }
}
