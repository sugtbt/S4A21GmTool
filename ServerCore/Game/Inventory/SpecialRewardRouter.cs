using Microsoft.Data.Sqlite;
using System;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed class SpecialRewardOutcome
    {
        public SpecialRewardKind Kind { get; set; }
        public int ItemTemplateId { get; set; }
        public int Count { get; set; }
        public short WalletSlot { get; set; }
        public int WalletNewTotal { get; set; }
    }

    public enum SpecialRewardKind
    {
        Premium,
        ReviveCoin,
    }

    internal static class SpecialRewardRouter
    {
        internal static bool TryRoute(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int characterId,
            int accountId,
            int itemTemplateId,
            int count,
            out SpecialRewardOutcome outcome)
        {
            outcome = null;

            if (Premium.PremiumService.IsContractItem(itemTemplateId))
            {
                outcome = new SpecialRewardOutcome
                {
                    Kind = SpecialRewardKind.Premium,
                    ItemTemplateId = itemTemplateId,
                    Count = Math.Max(1, count),
                };
                return true;
            }

            if (ReviveCoin.ReviveCoinService.IsReviveCoinReward(itemTemplateId))
            {
                var effectiveCount = Math.Max(1, count);
                var newTotal = ReviveCoin.ReviveCoinService.GrantToWallet(connection, transaction, characterId, effectiveCount);
                outcome = new SpecialRewardOutcome
                {
                    Kind = SpecialRewardKind.ReviveCoin,
                    ItemTemplateId = itemTemplateId,
                    Count = effectiveCount,
                    WalletSlot = ReviveCoin.ReviveCoinService.WalletSlot,
                    WalletNewTotal = newTotal,
                };
                return true;
            }

            return false;
        }
    }
}
