using DfoGmTool.ServerCore.Game.Currency;
using DfoGmTool.ServerCore.GameWorld;
using GmPvfLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    public sealed class ItemMetadata
    {
        public string ItemKind { get; set; } = "unknown";

        public string StackableType { get; set; }

        public string PvfFilePath { get; set; }

        public int BuyGold { get; set; }

        public int BuyCoin { get; set; }

        public int SellGold { get; set; }

        /// <summary>PVF item weight used by inventory-capacity checks.</summary>
        public int Weight { get; set; }

        public ushort Durability { get; set; }

        public int StackLimit { get; set; }

        public int NeedMaterialId { get; set; }

        public int NeedMaterialCount { get; set; }

        public int Grade { get; set; }

        public int MinimumLevel { get; set; }

        public int Rarity { get; set; }

        public string EquipmentType { get; set; }

        public string ItemCategory { get; set; }

        public string AttachType { get; set; }

        /// <summary>
        /// Maximum number of successful transfers for PVF [trade limit] items.
        /// The 86 client stores the remaining count in the high three bits of
        /// the common inventory attr/extData0 byte.
        /// </summary>
        public int TradeLimitMax { get; set; }

        public IReadOnlyList<string> ImpossibleContents { get; set; } = Array.Empty<string>();

        public bool IsSealed => string.Equals(AttachType?.Trim('[', ']', ' '), "sealing", StringComparison.OrdinalIgnoreCase);

        public bool IsStackable => string.Equals(ItemKind, "stackable", StringComparison.Ordinal);

        public bool IsMaterialExchange => NeedMaterialId > 0 && NeedMaterialCount > 0;

        internal static ItemMetadata CreateDefaultStackable()
            => new ItemMetadata { ItemKind = "stackable" };

        internal bool IsPrimaryStackableFamily(string family)
        {
            if (string.IsNullOrWhiteSpace(family)
                || !TryGetPrimaryStackableTag(out var tag))
            {
                return false;
            }

            var familyEnd = 0;
            while (familyEnd < tag.Length && !char.IsWhiteSpace(tag[familyEnd]))
                familyEnd++;
            if (familyEnd == 0)
                return false;

            return string.Equals(
                tag.Substring(0, familyEnd),
                family.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        public void GetSlotRange(out int slotStart, out int slotEnd)
        {
            if (string.Equals(ItemKind, "equipment", StringComparison.Ordinal))
            {
                if (IsFlagEquipmentType())
                { slotStart = 0; slotEnd = 48; return; }

                slotStart = 9; slotEnd = 64; return;
            }
            var st = NormalizeStackableType();
            TryGetPrimaryStackableTag(out var primaryTag);
            if (string.Equals(primaryTag, "flag gem", StringComparison.OrdinalIgnoreCase)
                || string.Equals(primaryTag, "guardian gem", StringComparison.OrdinalIgnoreCase)
                || string.Equals(primaryTag, "guild gem", StringComparison.OrdinalIgnoreCase))
            { slotStart = 49; slotEnd = 97; return; }
            if (string.Equals(primaryTag, "material expert job", StringComparison.OrdinalIgnoreCase))
            { slotStart = 233; slotEnd = 288; return; }
            if (string.Equals(primaryTag, "avatar emblem", StringComparison.OrdinalIgnoreCase))
            { slotStart = 289; slotEnd = 344; return; }
            if (IsPrimaryStackableFamily("material"))
            {
                if (st.EndsWith("4", StringComparison.Ordinal))
                { slotStart = 345; slotEnd = 359; }
                else
                { slotStart = 121; slotEnd = 176; }
                return;
            }
            if (IsPrimaryStackableFamily("quest"))
            { slotStart = 177; slotEnd = 232; return; }
            slotStart = 65; slotEnd = 120;
        }

        private bool TryGetPrimaryStackableTag(out string tag)
        {
            tag = string.Empty;
            if (!IsStackable)
                return false;

            var normalized = NormalizeStackableType();
            if (normalized.Length < 3 || normalized[0] != '[')
                return false;

            var end = normalized.IndexOf(']', 1);
            if (end <= 1)
                return false;

            tag = normalized.Substring(1, end - 1).Trim();
            return tag.Length > 0;
        }

        private string NormalizeStackableType()
            => (StackableType ?? string.Empty).Replace("`", string.Empty).Trim();

        private bool IsFlagEquipmentType()
        {
            var normalized = (EquipmentType ?? string.Empty).Replace("`", string.Empty).Trim();
            return normalized.StartsWith("[flag]", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class ItemSellRates
    {
        public int Equipment { get; set; } = 200;  
        public int NonStackable { get; set; } = 150; 
        public int Stackable { get; set; } = 30;    

        public static ItemSellRates Parse(string content)
        {
            var r = new ItemSellRates();
            if (string.IsNullOrEmpty(content)) return r;
            
            var m = System.Text.RegularExpressions.Regex.Match(content, @"\]\s*\r?\n\s*(\d+)\s+(\d+)\s+(\d+)");
            if (m.Success)
            {
                r.Equipment = int.Parse(m.Groups[1].Value);
                r.NonStackable = int.Parse(m.Groups[2].Value);
                r.Stackable = int.Parse(m.Groups[3].Value);
            }
            return r;
        }
    }

    public static class ItemMetadataResolver 
    {
        // GM local-patch: readonly 改可写以支持运行时切换 PVF 后的缓存重置
        internal static Lazy<LstFile> EquipmentList = new Lazy<LstFile>(() => LstFile.Parse(PvfArchiveAccessor.ReadText("equipment/equipment.lst")));
        private static Lazy<LstFile> StackableList = new Lazy<LstFile>(() => LstFile.Parse(PvfArchiveAccessor.ReadText("stackable/stackable.lst")));
        private static Lazy<ItemSellRates> SellRates = new Lazy<ItemSellRates>(() => ItemSellRates.Parse(PvfArchiveAccessor.ReadText("equipment/pricetable.tbl")));
        private static readonly ConcurrentDictionary<int, Lazy<ItemMetadata>> MetadataCache
            = new ConcurrentDictionary<int, Lazy<ItemMetadata>>();
        // PvfArchiveAccessor与equipment.lst都是进程级不可变Lazy，装备类型也按进程缓存。
        private static readonly ConcurrentDictionary<int, Lazy<string>> EquipmentTypeCache
            = new ConcurrentDictionary<int, Lazy<string>>();
        private static readonly ConcurrentDictionary<int, Lazy<byte>> EmblemSocketTypeCache
            = new ConcurrentDictionary<int, Lazy<byte>>();

        // GM local-patch: 运行时切换 PVF 的缓存重置(台账 local-patch 惯例; 含 MR40 新增的 MetadataCache)
        internal static void ResetForPvfChange()
        {
            EquipmentList = new Lazy<LstFile>(() => LstFile.Parse(PvfArchiveAccessor.ReadText("equipment/equipment.lst")));
            StackableList = new Lazy<LstFile>(() => LstFile.Parse(PvfArchiveAccessor.ReadText("stackable/stackable.lst")));
            SellRates = new Lazy<ItemSellRates>(() => ItemSellRates.Parse(PvfArchiveAccessor.ReadText("equipment/pricetable.tbl")));
            MetadataCache.Clear();
            EquipmentTypeCache.Clear();
            EmblemSocketTypeCache.Clear();
        }
        private static readonly Regex AvatarSocketRegex = new Regex(@"\[\s*([ABCDSM])\s+socket\s*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private const string AvatarTypeSelectTag = "[avatar type select]";
        private const string AvatarTypeSelectEndTag = "[/avatar type select]";
        private const string EmblemSocketDefaultTag = "[emblem socket default]";
        private const string EmblemSocketDefaultEndTag = "[/emblem socket default]";
        private const string AvatarEmblemSocketNumTag = "[avatar emblem socket num]";

        public static void Warmup()
        {
            _ = EquipmentList.Value;
            _ = StackableList.Value;
        }

        public static ItemMetadata Resolve(int itemTemplateId)
        {
            return MetadataCache.GetOrAdd(
                itemTemplateId,
                id => new Lazy<ItemMetadata>(() => ResolveCore(id))).Value;
        }

        private static ItemMetadata ResolveCore(int itemTemplateId)
        {
            var equipmentEntry = EquipmentList.Value.GetById(itemTemplateId);
            if (equipmentEntry != null)
            {
                var equipment = EquipmentFile.Parse(PvfArchiveAccessor.ReadText(Path.Combine("equipment", equipmentEntry.FilePath)));
                ResolveNeedMaterial(equipment.NeedMaterial, out var equipmentNeedMatId, out var equipmentNeedMatCount);
                // Keep legacy ordinary-NPC pricing intact.  Only entries that
                // actually exchange [need material] use PVF's price correction.
                var buyGold = equipmentNeedMatId > 0 && equipmentNeedMatCount > 0
                    ? ResolveBuyGold(equipment.Price, equipment.AddPrice)
                    : Math.Max(0, equipment.Price >= 0 ? equipment.Price : equipment.Value);
                
                
                var baseSellPrice = equipment.Value >= 0 ? equipment.Value : buyGold;
                var sellGold = Math.Max(1, baseSellPrice * SellRates.Value.Equipment / 1000);
                // 只有武器和防具有耐久度，其他装备类型（首饰/魔法石/称号/装扮/宠物等）无耐久。
                var eqType = NormalizeEquipmentType(equipment.EquipmentType);
                var hasDurability = equipment.Durability > 0 && HasDurabilityByType(eqType);
                var durability = hasDurability ? equipment.Durability : 0;

                return new ItemMetadata
                {
                    ItemKind = "equipment",
                    PvfFilePath = equipmentEntry.FilePath,
                    BuyGold = buyGold,
                    SellGold = sellGold,
                    Weight = Math.Max(0, equipment.Weight),
                    Durability = (ushort)durability,
                    StackLimit = 1,
                    Grade = equipment.Grade,
                    MinimumLevel = equipment.MinimumLevel,
                    Rarity = equipment.Rarity,
                    EquipmentType = NormalizeEquipmentType(equipment.EquipmentType),
                    ItemCategory = equipment.ItemCategory,
                    AttachType = equipment.AttachType,
                    ImpossibleContents = equipment.ImpossibleContentItems,
                    NeedMaterialId = equipmentNeedMatId,
                    NeedMaterialCount = equipmentNeedMatCount,
                };
            }

            var stackableEntry = StackableList.Value.GetById(itemTemplateId);
            if (stackableEntry != null)
            {
                var stackable = StackableItemFile.Parse(PvfArchiveAccessor.ReadText(Path.Combine("stackable", stackableEntry.FilePath)));
                var sellGold = stackable.Value >= 0
                    ? stackable.Value / 5
                    : (stackable.Price > 0 ? stackable.Price / 5 : 0);

                int needMatId = 0, needMatCount = 0;
                if (!string.IsNullOrWhiteSpace(stackable.NeedMaterial))
                {
                    var parts = stackable.NeedMaterial.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        int.TryParse(parts[0], out needMatId);
                        int.TryParse(parts[1], out needMatCount);
                    }
                }

                
                
                // Keep legacy ordinary-NPC pricing intact.  Material exchanges
                // are the only path where [add price] corrects [price] and
                // [value] must never become a purchase cost.
                var buyGold = needMatId > 0 && needMatCount > 0
                    ? ResolveBuyGold(stackable.Price, stackable.AddPrice)
                    : Math.Max(0, stackable.Price >= 0 ? stackable.Price : stackable.Value);
                return new ItemMetadata
                {
                    ItemKind = "stackable",
                    StackableType = stackable.StackableType,
                    PvfFilePath = stackableEntry.FilePath,
                    BuyGold = buyGold,
                    SellGold = sellGold,
                    Weight = Math.Max(0, stackable.Weight),
                    Durability = 0,
                    StackLimit = stackable.StackLimit,
                    NeedMaterialId = needMatId,
                    NeedMaterialCount = needMatCount,
                    Grade = stackable.Grade,
                    MinimumLevel = stackable.MinimumLevel,
                    Rarity = stackable.Rarity,
                    ItemCategory = stackable.ItemCategory,
                    AttachType = stackable.AttachType,
                    TradeLimitMax = Math.Max(0, stackable.TradeLimit),
                    ImpossibleContents = stackable.ImpossibleContentItems,
                };
            }

            return new ItemMetadata
            {
                ItemKind = "special",
                BuyGold = 0,
                SellGold = 1,
                Durability = 0,
                StackLimit = 1,
            };
        }

        public static LstEntry GetStackableEntry(int itemTemplateId)
        {
            return StackableList.Value.GetById(itemTemplateId);
        }

        // [value] is the NPC sell value only.  NPC purchase cost comes solely
        // from [price], adjusted by the optional signed [add price] field.
        // A missing [price] therefore means a zero-gold purchase, even when an
        // exchange also defines [need material].
        internal static int ResolveBuyGold(int price, int addPrice)
        {
            if (price < 0)
                return 0;

            var effectivePrice = (long)price + addPrice;
            return effectivePrice <= 0 ? 0 : effectivePrice > int.MaxValue ? int.MaxValue : (int)effectivePrice;
        }

        private static void ResolveNeedMaterial(string needMaterial, out int itemId, out int count)
        {
            itemId = 0;
            count = 0;
            if (string.IsNullOrWhiteSpace(needMaterial))
                return;

            var parts = needMaterial.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return;

            int.TryParse(parts[0], out itemId);
            int.TryParse(parts[1], out count);
            if (itemId <= 0 || count <= 0)
            {
                itemId = 0;
                count = 0;
            }
        }

        public static LstEntry GetEquipmentEntry(int itemTemplateId)
        {
            return EquipmentList.Value.GetById(itemTemplateId);
        }

        public static bool TryLoadEquipmentFile(int itemTemplateId, out EquipmentFile equipment)
        {
            equipment = null;
            var equipmentEntry = EquipmentList.Value.GetById(itemTemplateId);
            if (equipmentEntry == null)
                return false;

            equipment = EquipmentFile.Parse(PvfArchiveAccessor.ReadText(Path.Combine("equipment", equipmentEntry.FilePath)));
            return true;
        }

        public static bool TryLoadStackableFile(int itemTemplateId, out StackableItemFile stackable)
        {
            return TryLoadStackable(itemTemplateId, out stackable);
        }

        public static string ResolveEquipmentType(int itemTemplateId)
        {
            return TryGetEquipmentType(itemTemplateId, out var equipmentType)
                ? equipmentType
                : null;
        }

        internal static bool TryResolveItemKind(int itemTemplateId, out byte itemKind)
        {
            itemKind = ItemCore.KindUnknown;
            if (itemTemplateId <= 0)
                return false;

            ItemMetadata metadata;
            try
            {
                metadata = Resolve(itemTemplateId);
            }
            catch
            {
                return false;
            }

            return TryResolveItemKind(itemTemplateId, metadata, out itemKind);
        }

        internal static bool TryResolveItemKind(int itemTemplateId, ItemMetadata metadata, out byte itemKind)
        {
            itemKind = ItemCore.KindUnknown;
            if (EpicPieceCatalogService.IsEpicPieceId(itemTemplateId))
            {
                itemKind = ItemCore.KindEpicPiece;
                return true;
            }

            if (metadata == null)
                return false;

            if (string.Equals(metadata.ItemKind, "equipment", StringComparison.Ordinal))
            {
                itemKind = ResolveEquipmentItemKind(metadata);
                return true;
            }

            if (metadata.IsStackable)
            {
                itemKind = ResolveStackableItemKind(itemTemplateId, metadata);
                return true;
            }

            return false;
        }

        public static byte ResolveEmblemSocketType(int itemTemplateId)
        {
            return EmblemSocketTypeCache.GetOrAdd(
                itemTemplateId,
                id => new Lazy<byte>(() => ResolveEmblemSocketTypeCore(id))).Value;
        }

        private static byte ResolveEmblemSocketTypeCore(int itemTemplateId)
        {
            var stackableEntry = StackableList.Value.GetById(itemTemplateId);
            if (stackableEntry == null)
                return 0;

            StackableItemFile stackable;
            try
            {
                stackable = StackableItemFile.Parse(PvfArchiveAccessor.ReadText(Path.Combine("stackable", stackableEntry.FilePath)));
            }
            catch (Exception ex)
            {
                FileLogger.Log($"  [EmblemAttach] ResolveEmblemSocketType(0x{itemTemplateId:X8}) failed: {ex.Message}");
                return 0;
            }

            return stackable.AvatarEmblemSocketType;
        }

        public static IReadOnlyList<byte> ResolveAvatarSocketTypes(int itemTemplateId)
        {
            return ResolveAvatarOpenSocketTypes(itemTemplateId);
        }

        public static IReadOnlyList<byte> ResolveAvatarOpenSocketTypes(int itemTemplateId)
        {
            var result = new List<byte>();
            var equipmentEntry = EquipmentList.Value.GetById(itemTemplateId);
            if (equipmentEntry == null)
                return result;

            try
            {
                var text = PvfArchiveAccessor.ReadText(Path.Combine("equipment", equipmentEntry.FilePath));
                var section = ExtractAvatarTypeSelectSection(text);
                AddAvatarSocketMatches(result, section, 5);
            }
            catch (Exception ex)
            {
                FileLogger.Log($"  [AvatarSocket] ResolveAvatarOpenSocketTypes(0x{itemTemplateId:X8}) failed: {ex.Message}");
            }

            return result;
        }

        public static IReadOnlyList<byte> ResolveAvatarDefaultSocketTypes(int itemTemplateId)
        {
            var result = new List<byte>();
            var equipmentEntry = EquipmentList.Value.GetById(itemTemplateId);
            if (equipmentEntry == null)
                return result;

            try
            {
                var text = PvfArchiveAccessor.ReadText(Path.Combine("equipment", equipmentEntry.FilePath));
                AddAvatarSocketMatches(result, ExtractEmblemSocketDefaultSection(text), ResolveAvatarEmblemSocketNum(text));
            }
            catch (Exception ex)
            {
                FileLogger.Log($"  [AvatarSocket] ResolveAvatarDefaultSocketTypes(0x{itemTemplateId:X8}) failed: {ex.Message}");
            }

            return result;
        }

        private static void AddAvatarSocketMatches(List<byte> result, string section, int maxCount)
        {
            if (result == null || string.IsNullOrEmpty(section) || maxCount <= 0)
                return;

            foreach (Match match in AvatarSocketRegex.Matches(section))
            {
                if (!match.Success || match.Groups.Count < 2)
                    continue;

                if (TryMapAvatarSocketCode(match.Groups[1].Value[0], out var socketType))
                {
                    result.Add(socketType);
                    if (result.Count >= maxCount)
                        break;
                }
            }
        }

        public static byte ResolveAvatarSocketType(int itemTemplateId)
        {
            var socketTypes = ResolveAvatarSocketTypes(itemTemplateId);
            return socketTypes != null && socketTypes.Count > 0 ? socketTypes[0] : (byte)0;
        }

        private static string ExtractAvatarTypeSelectSection(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var start = text.IndexOf(AvatarTypeSelectTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return string.Empty;

            start += AvatarTypeSelectTag.Length;
            var end = text.IndexOf(AvatarTypeSelectEndTag, start, StringComparison.OrdinalIgnoreCase);
            return end > start ? text.Substring(start, end - start) : text.Substring(start);
        }

        private static string ExtractEmblemSocketDefaultSection(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var start = text.IndexOf(EmblemSocketDefaultTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return string.Empty;

            start += EmblemSocketDefaultTag.Length;
            var end = text.IndexOf(EmblemSocketDefaultEndTag, start, StringComparison.OrdinalIgnoreCase);
            return end > start ? text.Substring(start, end - start) : text.Substring(start);
        }

        private static int ResolveAvatarEmblemSocketNum(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 5;

            var start = text.IndexOf(AvatarEmblemSocketNumTag, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return 5;

            start += AvatarEmblemSocketNumTag.Length;
            var end = text.IndexOf('[', start);
            var section = end > start ? text.Substring(start, end - start) : text.Substring(start);
            foreach (var token in section.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token, out var count))
                    return Math.Max(0, Math.Min(5, count));
            }

            return 5;
        }

        private static bool TryMapAvatarSocketCode(char code, out byte socketType)
        {
            switch (char.ToUpperInvariant(code))
            {
                case 'A':
                    socketType = 0x01;
                    return true;
                case 'B':
                    socketType = 0x02;
                    return true;
                case 'C':
                    socketType = 0x04;
                    return true;
                case 'D':
                    socketType = 0x08;
                    return true;
                case 'S':
                    socketType = 0x10;
                    return true;
                case 'M':
                    socketType = 0xEF;
                    return true;
                default:
                    socketType = 0;
                    return false;
            }
        }

        public static bool TryValidateEnchantByBeadTarget(int beadItemTemplateId, int targetItemTemplateId, byte enchantUpgradeCount, out int enchantCardItemId, out string rejectReason)
        {
            enchantCardItemId = 0;
            rejectReason = null;

            if (!TryLoadStackable(beadItemTemplateId, out var bead))
            {
                rejectReason = "bead is not found in stackable.lst";
                return false;
            }

            if (bead.MonsterCardId > 0)
                enchantCardItemId = bead.MonsterCardId;
            else if (bead.EnchantIndex > 0)
                enchantCardItemId = bead.EnchantIndex;
            else
            {
                rejectReason = "bead has no monster card id/enchant index";
                return false;
            }

            // 宝珠直接声明 target item id 时，只有白名单装备能被附魔。
            if (bead.TargetItemIds != null && bead.TargetItemIds.Count > 0 && !bead.TargetItemIds.Contains(targetItemTemplateId))
            {
                rejectReason = "target item id is not allowed by bead target item id";
                return false;
            }

            if (!TryGetEquipmentType(targetItemTemplateId, out var targetEquipmentType))
            {
                rejectReason = "target is not found in equipment.lst";
                return false;
            }

            StackableItemFile card = null;
            if (bead.MonsterCardId > 0)
            {
                if (!TryLoadStackable(bead.MonsterCardId, out card))
                {
                    rejectReason = "monster card is not found in stackable.lst";
                    return false;
                }
            }
            else
            {
                TryLoadStackable(enchantCardItemId, out card);
            }

            if (card != null)
            {
                // monster card 的 string data: 第一个是图片资源，后续是允许附魔的 equipment type。
                var allowedTypes = ExtractAllowedEquipmentTypes(card.StringDataItems);
                if (allowedTypes.Count > 0 && !allowedTypes.Contains(targetEquipmentType))
                {
                    rejectReason = "target equipment type is not allowed by monster card string data";
                    return false;
                }

                if (card.EnchantTable.Count > 0 && !card.EnchantTable.Contains(enchantUpgradeCount))
                {
                    rejectReason = "enchant upgrade count is not allowed by monster card enchant table";
                    return false;
                }

                if (card.EnchantTable.Count == 0 && enchantUpgradeCount != 0)
                {
                    rejectReason = "monster card has no enchant table for upgraded bead";
                    return false;
                }
            }

            if (card == null && enchantUpgradeCount != 0)
            {
                rejectReason = "upgraded enchant bead requires monster card enchant table";
                return false;
            }

            return true;
        }

        public static bool TryValidatePetEnchantByBeadTarget(int beadItemTemplateId, int targetItemTemplateId, byte enchantUpgradeCount, out int enchantCardItemId, out string rejectReason)
        {
            enchantCardItemId = 0;
            rejectReason = null;

            if (!TryLoadStackable(beadItemTemplateId, out var bead))
            {
                rejectReason = "bead is not found in stackable.lst";
                return false;
            }

            if (bead.MonsterCardId > 0)
                enchantCardItemId = bead.MonsterCardId;
            else if (bead.EnchantIndex > 0)
                enchantCardItemId = bead.EnchantIndex;
            else
            {
                rejectReason = "bead has no monster card id/enchant index";
                return false;
            }

            if (bead.TargetItemIds != null && bead.TargetItemIds.Count > 0 && !bead.TargetItemIds.Contains(targetItemTemplateId))
            {
                rejectReason = "target item id is not allowed by bead target item id";
                return false;
            }

            StackableItemFile card = null;
            if (bead.MonsterCardId > 0)
            {
                if (!TryLoadStackable(bead.MonsterCardId, out card))
                {
                    rejectReason = "monster card is not found in stackable.lst";
                    return false;
                }
            }
            else
            {
                TryLoadStackable(enchantCardItemId, out card);
            }

            if (card != null)
            {
                var allowedTypes = ExtractAllowedEquipmentTypes(card.StringDataItems);
                if (allowedTypes.Count == 0 || !allowedTypes.Contains("[creature]"))
                {
                    rejectReason = "target equipment type is not allowed by monster card string data";
                    return false;
                }

                if (card.EnchantTable.Count > 0 && !card.EnchantTable.Contains(enchantUpgradeCount))
                {
                    rejectReason = "enchant upgrade count is not allowed by monster card enchant table";
                    return false;
                }

                if (card.EnchantTable.Count == 0 && enchantUpgradeCount != 0)
                {
                    rejectReason = "monster card has no enchant table for upgraded bead";
                    return false;
                }
            }

            if (card == null && enchantUpgradeCount != 0)
            {
                rejectReason = "upgraded enchant bead requires monster card enchant table";
                return false;
            }

            return true;
        }

        private static bool TryLoadStackable(int itemTemplateId, out StackableItemFile stackable)
        {
            stackable = null;
            var stackableEntry = StackableList.Value.GetById(itemTemplateId);
            if (stackableEntry == null)
                return false;

            stackable = StackableItemFile.Parse(PvfArchiveAccessor.ReadText(Path.Combine("stackable", stackableEntry.FilePath)));
            return true;
        }

        private static bool TryGetEquipmentType(int itemTemplateId, out string equipmentType)
        {
            var equipmentList = EquipmentList.Value;
            var cached = EquipmentTypeCache.GetOrAdd(
                itemTemplateId,
                id => new Lazy<string>(() => LoadEquipmentType(equipmentList, id)));
            equipmentType = cached.Value;
            return !string.IsNullOrWhiteSpace(equipmentType);
        }

        private static string LoadEquipmentType(LstFile equipmentList, int itemTemplateId)
        {
            var equipmentEntry = equipmentList.GetById(itemTemplateId);
            if (equipmentEntry == null)
                return null;

            var equipment = EquipmentFile.Parse(PvfArchiveAccessor.ReadText(Path.Combine("equipment", equipmentEntry.FilePath)));
            return NormalizeEquipmentType(equipment.EquipmentType);
        }

        private static HashSet<string> ExtractAllowedEquipmentTypes(List<string> stringDataItems)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (stringDataItems == null || stringDataItems.Count <= 1)
                return result;

            for (var i = 1; i < stringDataItems.Count; i++)
            {
                var normalized = NormalizeEquipmentType(stringDataItems[i]);
                if (!string.IsNullOrWhiteSpace(normalized))
                    result.Add(normalized);
            }

            return result;
        }

        private static bool HasDurabilityByType(string normalizedType)
        {
            if (string.IsNullOrEmpty(normalizedType))
                return false;
            // 武器
            if (normalizedType == "[weapon]" || normalizedType == "[support weapon]" || normalizedType == "[charm]")
                return true;
            // 防具
            if (normalizedType == "[coat]" || normalizedType == "[pants]"
                || normalizedType == "[shoulder]" || normalizedType == "[shoes]"
                || normalizedType == "[waist]")
                return true;
            return false;
        }

        // 全部修理("一键修理")适用的装备类型 (客户端 handler 过滤一致)。
        // 只修这 13 类穿戴装备; 
        private static readonly HashSet<string> RepairAllEquipmentTypes = new HashSet<string>
        {
            "[weapon]", "[coat]", "[pants]", "[hat]", "[shoulder]", "[waist]",
            "[shoes]", "[amulet]", "[wrist]", "[ring]", "[support]",
            "[aurora avatar]", "[magic stone]",
        };

        // itemTemplateId 是否属于"全部修理"适用的装备类型。
        public static bool IsRepairAllEligible(int itemTemplateId)
        {
            return TryGetEquipmentType(itemTemplateId, out var type)
                && type != null
                && RepairAllEquipmentTypes.Contains(type);
        }

        private static string NormalizeEquipmentType(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var start = raw.IndexOf('[', StringComparison.Ordinal);
            var end = start >= 0 ? raw.IndexOf(']', start + 1) : -1;
            if (start < 0 || end <= start)
                return raw.Trim('`', ' ', '\t', '\r', '\n').ToLowerInvariant();

            return raw.Substring(start, end - start + 1).ToLowerInvariant();
        }

        /// <summary>
        /// 判断物品是否为克隆装扮。克隆装扮的 PVF [item category] 段值为 "clear avatar"。
        /// </summary>
        public static bool IsCloneAvatarItem(int itemTemplateId)
        {
            if (!TryLoadEquipmentFile(itemTemplateId, out var equipment))
                return false;

            return equipment.ClearAvatar == 1
                || string.Equals(equipment.ItemCategory, "clear avatar", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsNameTagItem(int itemTemplateId)
        {
            var meta = Resolve(itemTemplateId);
            return meta != null
                && string.Equals(meta.ItemKind, "equipment", StringComparison.Ordinal)
                && string.Equals(meta.EquipmentType, "[name tag]", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPetInventoryEquipment(int itemTemplateId)
        {
            return CreatureExtraResolver.IsPetInventoryEquipment(itemTemplateId);
        }

        internal static bool IsCreatureItem(int itemTemplateId)
        {
            try
            {
                return CreatureExtraResolver.HasCreatureExtra(itemTemplateId);
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[ItemMetadataResolver] IsCreatureItem(0x{itemTemplateId:X8}) failed: {ex.Message}");
                return false;
            }
        }

        internal static bool IsAvatarItem(ItemMetadata metadata)
        {
            var path = metadata?.PvfFilePath;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var normalizedPath = "/" + path.Replace('\\', '/').Trim('/');
            return normalizedPath.IndexOf("/avatar/", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedPath.IndexOf("/at_avatar/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsPetConsumableItem(ItemMetadata metadata)
        {
            if (metadata == null || !metadata.IsStackable || string.IsNullOrWhiteSpace(metadata.StackableType))
                return false;
            var stackableType = metadata.StackableType.Replace("`", "").Trim();
            return stackableType.StartsWith("[creature]", StringComparison.OrdinalIgnoreCase)
                || stackableType.StartsWith("[feed]", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGuardianGemStackable(ItemMetadata metadata, string stackableType)
        {
            if (string.Equals(stackableType, "flag gem", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stackableType, "guardian gem", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stackableType, "guild gem", StringComparison.OrdinalIgnoreCase)
                || string.Equals(stackableType, "gem", StringComparison.OrdinalIgnoreCase)
                || stackableType.IndexOf("guardian gem", StringComparison.OrdinalIgnoreCase) >= 0
                || stackableType.IndexOf("守护珠", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var path = metadata?.PvfFilePath;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var normalizedPath = path.Replace('\\', '/').ToLowerInvariant();
            return normalizedPath.IndexOf("guardian", StringComparison.Ordinal) >= 0
                && normalizedPath.IndexOf("gem", StringComparison.Ordinal) >= 0;
        }

        private static byte ResolveEquipmentItemKind(ItemMetadata metadata)
        {
            if (IsAvatarItem(metadata))
                return ItemCore.KindAvatar;

            var equipmentType = NormalizePvfKindTag(metadata.EquipmentType);
            if (equipmentType == "creature")
                return ItemCore.KindCreature;

            if (equipmentType == "artifact red"
                || equipmentType == "artifact blue"
                || equipmentType == "artifact green")
                return ItemCore.KindCreatureEquipment;

            if (equipmentType == "flag")
                return ItemCore.KindGuildMedal;

            return ItemCore.KindEquipment;
        }

        private static byte ResolveStackableItemKind(int itemTemplateId, ItemMetadata metadata)
        {
            if (IsPetConsumableItem(metadata))
                return ItemCore.KindCreatureConsumable;

            var stackableType = NormalizePvfKindTag(metadata.StackableType);
            if (stackableType == "avatar emblem")
                return ItemCore.KindAvatarEmblem;

            if (IsGuardianGemStackable(metadata, stackableType))
                return ItemCore.KindGuardianGem;

            if (stackableType == "material expert job")
                return ItemCore.KindExpertJobMaterial;

            if (metadata.IsPrimaryStackableFamily("quest"))
                return ItemCore.KindQuest;

            if (metadata.IsPrimaryStackableFamily("material"))
                return IsSpecialMaterialItem(itemTemplateId)
                    ? ItemCore.KindSpecialMaterial
                    : ItemCore.KindMaterial;

            return ItemCore.KindConsumable;
        }

        private static bool IsSpecialMaterialItem(int itemTemplateId)
        {
            return CurrencyService.IsAccountWarehouseItem(itemTemplateId);
        }

        private static string NormalizePvfKindTag(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Replace("`", string.Empty).Trim();
            if (normalized.Length >= 2 && normalized[0] == '[')
            {
                var end = normalized.IndexOf(']', 1);
                if (end > 1)
                    normalized = normalized.Substring(1, end - 1);
            }

            return normalized.Trim().ToLowerInvariant();
        }
    }
}
