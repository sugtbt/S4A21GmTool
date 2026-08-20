using System;
using DfoGmTool.ServerCore.Game.Inventory;

namespace DfoGmTool.ServerCore.Game.TitleBook
{
    internal static class TitleBookItemProjection
    {
        internal static TitleBookListEntrySnapshot ToListEntry(int bookIndex, ItemCore core)
        {
            if (core == null) throw new ArgumentNullException(nameof(core));

            return new TitleBookListEntrySnapshot
            {
                SlotIndex = unchecked((ushort)bookIndex),
                ItemId = core.ItemId,
                Value = core.Value,
                Attr = core.Attr,
                Durability = core.Durability,
                SealFlag = core.SealFlag,
                EnchantIndex = core.EnchantCardId,
                EnchantUpgradeCount = core.EnchantUpgradeCount,
                AmplifyType = core.AmplifyType,
                AmplifyValue = core.AmplifyValue,
            };
        }
    }
}
