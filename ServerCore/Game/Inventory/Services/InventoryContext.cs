using System;
using System.Collections.Generic;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal static class InventoryContext
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<Guid, int> SessionOwnership = new Dictionary<Guid, int>();
        private static readonly Dictionary<int, InventoryLease> CharacterLeases = new Dictionary<int, InventoryLease>();
        private static long _nextVersion;

        public static InventoryLease Register(Guid sessionId, InventoryService inventory)
        {
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));

            return Register(sessionId, inventory.CharacterId, inventory);
        }

        public static InventoryLease Register(Guid sessionId, int characterId, InventoryService inventory)
        {
            if (sessionId == Guid.Empty)
                throw new ArgumentException("注册在线背包需要有效的 sessionId。", nameof(sessionId));
            if (characterId <= 0)
                throw new ArgumentOutOfRangeException(nameof(characterId), "注册在线背包需要有效的角色 ID。");
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));
            if (inventory.CharacterId > 0 && inventory.CharacterId != characterId)
                throw new ArgumentException("注册在线背包时，参数角色 ID 与背包角色 ID 不一致。", nameof(characterId));

            InventoryLease removedForSession;
            InventoryLease removedForCharacter;
            InventoryLease replacedForSameSession;
            InventoryLease lease;
            lock (SyncRoot)
            {
                removedForSession = RemovePreviousCharacterForSession(sessionId, characterId);
                removedForCharacter = RemovePreviousSessionForCharacter(sessionId, characterId);
                replacedForSameSession = CharacterLeases.TryGetValue(characterId, out var existingLease)
                    && existingLease.IsOwnedBy(sessionId)
                        ? existingLease
                        : null;

                lease = new InventoryLease(sessionId, characterId, inventory, ++_nextVersion);
                SessionOwnership[sessionId] = characterId;
                CharacterLeases[characterId] = lease;
            }

            SaveRemovedLease(removedForSession);
            SaveRemovedLease(removedForCharacter);
            SaveRemovedLease(replacedForSameSession);
            return lease;
        }

        public static bool Unregister(Guid sessionId)
        {
            if (sessionId == Guid.Empty)
                return false;

            InventoryLease removed;
            lock (SyncRoot)
            {
                if (!SessionOwnership.TryGetValue(sessionId, out var characterId))
                    return false;

                SessionOwnership.Remove(sessionId);
                removed = RemoveCharacterLeaseIfOwnedBy(characterId, sessionId);
            }

            SaveRemovedLease(removed);
            return removed != null;
        }

        public static bool Unregister(Guid sessionId, int characterId)
        {
            if (sessionId == Guid.Empty || characterId <= 0)
                return false;

            InventoryLease removed;
            lock (SyncRoot)
            {
                if (SessionOwnership.TryGetValue(sessionId, out var ownedCharacterId)
                    && ownedCharacterId == characterId)
                    SessionOwnership.Remove(sessionId);

                removed = RemoveCharacterLeaseIfOwnedBy(characterId, sessionId);
            }

            SaveRemovedLease(removed);
            return removed != null;
        }

        public static InventoryService Get(int characterId)
        {
            return TryGet(characterId, out var inventory) ? inventory : null;
        }

        public static bool TryGet(int characterId, out InventoryService inventory)
        {
            inventory = null;
            if (characterId <= 0)
                return false;

            lock (SyncRoot)
            {
                if (!CharacterLeases.TryGetValue(characterId, out var lease))
                    return false;

                inventory = lease.Inventory;
                return true;
            }
        }

        public static bool TryGetLease(int characterId, out InventoryLease lease)
        {
            lease = null;
            if (characterId <= 0)
                return false;

            lock (SyncRoot)
            {
                return CharacterLeases.TryGetValue(characterId, out lease);
            }
        }

        public static IReadOnlyList<InventoryLease> GetLeasesSnapshot()
        {
            lock (SyncRoot)
                return new List<InventoryLease>(CharacterLeases.Values);
        }

        public static bool TryGetForSession(Guid sessionId, int characterId, out InventoryService inventory)
        {
            inventory = null;
            if (sessionId == Guid.Empty || characterId <= 0)
                return false;

            lock (SyncRoot)
            {
                if (!SessionOwnership.TryGetValue(sessionId, out var ownedCharacterId)
                    || ownedCharacterId != characterId)
                    return false;
                if (!CharacterLeases.TryGetValue(characterId, out var lease)
                    || !lease.IsOwnedBy(sessionId))
                    return false;

                inventory = lease.Inventory;
                return true;
            }
        }

        private static InventoryLease RemovePreviousCharacterForSession(Guid sessionId, int characterId)
        {
            if (!SessionOwnership.TryGetValue(sessionId, out var oldCharacterId)
                || oldCharacterId == characterId)
                return null;

            SessionOwnership.Remove(sessionId);
            return RemoveCharacterLeaseIfOwnedBy(oldCharacterId, sessionId);
        }

        private static InventoryLease RemovePreviousSessionForCharacter(Guid sessionId, int characterId)
        {
            if (!CharacterLeases.TryGetValue(characterId, out var oldLease)
                || oldLease.IsOwnedBy(sessionId))
                return null;

            if (SessionOwnership.TryGetValue(oldLease.SessionId, out var oldOwnedCharacterId)
                && oldOwnedCharacterId == characterId)
                SessionOwnership.Remove(oldLease.SessionId);

            CharacterLeases.Remove(characterId);
            return oldLease;
        }

        private static InventoryLease RemoveCharacterLeaseIfOwnedBy(int characterId, Guid sessionId)
        {
            if (!CharacterLeases.TryGetValue(characterId, out var lease)
                || !lease.IsOwnedBy(sessionId))
                return null;

            CharacterLeases.Remove(characterId);
            return lease;
        }

        private static void SaveRemovedLease(InventoryLease lease)
        {
            if (lease == null)
                return;

            InventoryPersistenceService.SaveDirty(lease);
        }
    }
}
