using System;
using DfoGmTool.ServerCore.Game.DailyReset;
using DfoGmTool.ServerCore.Game.Inventory;
using Microsoft.Data.Sqlite;

namespace DfoGmTool.ServerCore.Game.ReviveCoin
{
    public sealed class ReviveCoinService
    {
        public const int ItemId = 1;
        public const short WalletSlot = 1;
        public const int ConsumableItemId = 42;

        public const string DailyClaimKey = "revive_coin_daily_claim";

        private readonly DailyResetService _dailyReset;

        public ReviveCoinService(DailyResetService dailyReset)
        {
            _dailyReset = dailyReset ?? throw new ArgumentNullException(nameof(dailyReset));
        }

        public static bool IsReviveCoinReward(int itemTemplateId)
        {
            return itemTemplateId == ItemId || itemTemplateId == ConsumableItemId;
        }

        public static int GrantToWallet(
            SqliteConnection conn,
            SqliteTransaction tx,
            int characterId,
            int count)
        {
            var effectiveCount = Math.Max(1, count);
            var current = InventoryMainVirtualCountRepository.LoadCurrencyCount(conn, tx, characterId, WalletSlot);
            var newTotal = current + effectiveCount;
            InventoryMainVirtualCountRepository.UpsertCurrencySlot(conn, tx, characterId, WalletSlot, newTotal);
            return newTotal;
        }

        internal bool TryGrantDaily(InventoryLease lease, out short assignedSlot)
        {
            assignedSlot = -1;
            try
            {
                if (lease == null)
                    return false;

                lock (lease.SyncRoot)
                {
                    if (lease.Inventory.CountMainItem(ItemId) > 0)
                        return false;

                    if (!_dailyReset.TryClaimFlag(lease.CharacterId, DailyClaimKey))
                        return false;

                    if (!lease.Inventory.SetMainVirtualCount(WalletSlot, ItemId, 1))
                        return false;

                    assignedSlot = WalletSlot;
                    return true;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[ReviveCoin] TryGrantDaily ERROR: {ex.Message}");
                return false;
            }
        }

        internal bool TryConsume(InventoryLease lease, out short slot, out int remaining)
        {
            slot = -1;
            remaining = 0;
            try
            {
                if (lease == null)
                    return false;

                lock (lease.SyncRoot)
                {
                    if (!lease.Inventory.TryConsumeMainItem(ItemId, 1, out var result)
                        || !result.Success)
                        return false;

                    slot = result.SlotIndex;
                    remaining = result.RemainingCount;
                    return true;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[ReviveCoin] TryConsume ERROR: {ex.Message}");
                return false;
            }
        }
    }
}
