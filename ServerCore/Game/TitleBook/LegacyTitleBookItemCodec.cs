using System;
using DfoGmTool.ServerCore.Game.Inventory;

namespace DfoGmTool.ServerCore.Game.TitleBook
{
    internal static class LegacyTitleBookItemCodec
    {
        internal const int CommonNetworkSize = 84;
        internal const int PersistedRecordSize = CommonNetworkSize + 1;
        internal const int TitleBookListEntrySize = 22;

        internal static ItemCore DecodePersistedRecord(byte[] record)
        {
            var data = Normalize(record, PersistedRecordSize);
            var itemId = BitConverter.ToInt32(data, 2);
            if (itemId <= 0)
                return new ItemCore();

            var core = CreateTitleCore(itemId);
            core.Value = BitConverter.ToInt32(data, 6);
            core.Attr = data[10];
            core.Durability = BitConverter.ToUInt16(data, 11);
            core.SealFlag = data[13];
            core.EnchantCardId = BitConverter.ToInt32(data, 14);
            core.EnchantUpgradeCount = data[18];
            core.AmplifyType = data[19];
            core.AmplifyValue = BitConverter.ToUInt16(data, 20);
            core.Marker16 = BitConverter.ToInt32(data, 22);
            core.SetChronicleOptions(DecodeChronicle(Slice(data, 26, 17)));
            core.ExpireTime = BitConverter.ToInt32(data, 43);
            ApplyTailData(core, Slice(data, 47, 37));
            core.EquipmentLockId = data[CommonNetworkSize];
            return core;
        }

        internal static bool TryDecodeListEntry(byte[] blob, int offset, out ushort bookIndex, out ItemCore core)
        {
            bookIndex = 0;
            core = new ItemCore();
            if (blob == null || offset < 0 || offset + TitleBookListEntrySize > blob.Length)
                return false;

            bookIndex = BitConverter.ToUInt16(blob, offset);
            var itemId = BitConverter.ToInt32(blob, offset + 2);
            if (itemId <= 0)
                return true;

            core = CreateTitleCore(itemId);
            core.Value = BitConverter.ToInt32(blob, offset + 6);
            core.Attr = blob[offset + 10];
            core.Durability = BitConverter.ToUInt16(blob, offset + 11);
            core.SealFlag = blob[offset + 13];
            core.EnchantCardId = BitConverter.ToInt32(blob, offset + 14);
            core.EnchantUpgradeCount = blob[offset + 18];
            core.AmplifyType = blob[offset + 19];
            core.AmplifyValue = BitConverter.ToUInt16(blob, offset + 20);
            core.Marker16 = 0;
            return true;
        }

        private static ItemCore CreateTitleCore(int itemId)
        {
            var core = ItemCore.Create(ItemCore.KindEquipment, itemId);
            if (ItemMetadataResolver.TryResolveItemKind(itemId, out var itemKind))
                core.ItemKind = itemKind;
            return core;
        }

        private static ChronicleOption[] DecodeChronicle(byte[] raw)
        {
            var data = Normalize(raw, 17);
            var count = Math.Min(data[0], (byte)2);
            if (count == 0)
                return Array.Empty<ChronicleOption>();

            var result = new ChronicleOption[count];
            var off = 1;
            for (var i = 0; i < count; i++)
            {
                result[i] = new ChronicleOption
                {
                    OptionId = BitConverter.ToInt32(data, off),
                    CharacJob = data[off + 4],
                    FirstGrowType = data[off + 5],
                    EquipmentType = data[off + 6],
                    OptionNo = data[off + 7],
                };
                off += 8;
            }

            return result;
        }

        private static void ApplyTailData(ItemCore core, byte[] rawTail)
        {
            var tail = Normalize(rawTail, 37);
            core.EmblemSocketCount = tail[0];
            core.EmblemId1 = BitConverter.ToInt32(tail, 1);
            core.EmblemId2 = BitConverter.ToInt32(tail, 5);
            core.Rune = BitConverter.ToUInt16(tail, 9);
            core.RandomOption0.Type = tail[12];
            core.RandomOption1.Type = tail[13];
            core.RandomOption2.Type = tail[14];
            core.RandomOption0.Value1 = tail[15];
            core.RandomOption1.Value1 = tail[16];
            core.RandomOption2.Value1 = tail[17];
            core.RandomOption0.Value2 = tail[18];
            core.RandomOption1.Value2 = tail[19];
            core.RandomOption2.Value2 = tail[20];
            core.RandomOptionState = tail[21];
            core.RandomOptionChangedIndex = tail[22];
            core.RandomOptionChangeState = tail[23];
            core.RandomOptionChange.Type = tail[24];
            core.RandomOptionChange.Value1 = tail[25];
            core.RandomOptionChange.Value2 = tail[26];
            core.GenuineUpgrade = tail[27];
            core.EmancipateEquipmentLevel = tail[28];
            core.TradeRestriction = tail[29];
            core.TailUnknown0 = BitConverter.ToUInt16(tail, 30);
            core.TailUnknown1 = tail[32];
            core.TailUnknown2 = tail[33];
            core.TailUnknown3 = tail[34];
            core.RemainUseCount = tail[35];
            core.SortLockFlag = tail[36];
        }

        private static byte[] Slice(byte[] data, int offset, int length)
        {
            var result = new byte[length];
            if (data == null || offset >= data.Length)
                return result;

            Buffer.BlockCopy(data, offset, result, 0, Math.Min(length, data.Length - offset));
            return result;
        }

        private static byte[] Normalize(byte[] data, int length)
        {
            var result = new byte[length];
            if (data == null)
                return result;

            Buffer.BlockCopy(data, 0, result, 0, Math.Min(length, data.Length));
            return result;
        }
    }
}
