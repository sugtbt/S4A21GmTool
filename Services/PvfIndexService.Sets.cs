using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using GmPvfLib;

namespace DfoGmTool.Services
{
    // 套装表来自 etc/equipmentpartset.etc；成员按 part set index + 部位 + 名称前缀对齐。
    // 件数在 2–20 才允许整套发放（两封邮件上限）。
    public sealed partial class PvfIndexService
    {
        private const int SetSendMinPieces = 2;
        private const int SetSendMaxPieces = 20;
        private const int SetNamePrefixMin = 2;
        private const int SetLooseMemberThreshold = 20;

        private static readonly Regex SetTableTokenPattern = new Regex(
            @"`([^`]*)`|\[([^\]]+)\]|(-?\d+)",
            RegexOptions.Compiled);

        private static readonly string[] SetSlotOrder =
        {
            "weapon", "title name",
            "coat", "shoulder", "pants", "shoes", "waist",
            "amulet", "wrist", "ring", "support", "magic stone", "support weapon",
            "hat avatar", "hair avatar", "face avatar", "coat avatar", "breast avatar",
            "waist avatar", "pants avatar", "shoes avatar", "skin avatar",
            "aurora avatar", "weapon avatar",
        };

        internal sealed class EquipmentSetInfo
        {
            public int Id;
            public string Name;
            public string ScriptPath;
            public List<string> SlotTags = new List<string>();
            public List<SetBonusInfo> Bonuses = new List<SetBonusInfo>();
        }

        internal sealed class SetBonusInfo
        {
            public int Pieces;
            public string Text;
        }

        public bool TryResolveSendableSet(
            int itemId,
            string jobLabel,
            out IReadOnlyList<int> memberIds,
            out string setName,
            out string error)
        {
            memberIds = Array.Empty<int>();
            setName = null;
            error = null;

            var items = _itemsById;
            if (items == null || !items.TryGetValue(itemId, out var seed))
            {
                error = "PVF 中没有这件物品";
                return false;
            }

            if (seed.PartSetIndex <= 0)
            {
                error = "该物品不属于套装";
                return false;
            }

            var members = ResolveSetMembers(seed, jobLabel);
            setName = ResolveSetName(seed.PartSetIndex) ?? seed.Name;
            if (members.Count < SetSendMinPieces || members.Count > SetSendMaxPieces)
            {
                error = members.Count <= 1
                    ? "无法对齐出可发放的整套（部件名对不上或职业不适用）"
                    : "套装部件超过两封邮件上限 " + SetSendMaxPieces;
                return false;
            }

            memberIds = members.Select(item => item.Id).ToArray();
            return true;
        }

        internal string ResolveSetName(int setId)
        {
            if (setId <= 0)
                return null;
            var sets = _setsById;
            if (sets != null && sets.TryGetValue(setId, out var set) && !string.IsNullOrWhiteSpace(set.Name))
                return set.Name.Trim();
            return null;
        }

        internal bool IsSetSendable(ItemEntry entry, string jobLabel)
        {
            if (entry == null || entry.PartSetIndex <= 0)
                return false;
            var count = ResolveSetMembers(entry, jobLabel).Count;
            return count >= SetSendMinPieces && count <= SetSendMaxPieces;
        }

        internal string ResolveJobBaseName(int job)
        {
            var jobs = _jobNames;
            if (jobs != null && jobs.TryGetValue(job, out var info) && !string.IsNullOrWhiteSpace(info.BaseName))
                return info.BaseName;
            return null;
        }

        private object BuildSetPreview(ItemEntry entry, string jobLabel)
        {
            if (entry == null || entry.PartSetIndex <= 0)
                return null;

            var set = GetSet(entry.PartSetIndex);
            var members = ResolveSetMembers(entry, jobLabel);
            var sendable = members.Count >= SetSendMinPieces && members.Count <= SetSendMaxPieces;
            return new
            {
                id = entry.PartSetIndex,
                name = ResolveSetName(entry.PartSetIndex) ?? ("套装 " + entry.PartSetIndex),
                sendable,
                pieces = members.Select(item => new
                {
                    itemId = item.Id,
                    name = item.Name,
                    tag = item.TypeTag,
                    rarity = item.Rarity,
                    hasIcon = !string.IsNullOrWhiteSpace(item.IconPath),
                }).ToArray(),
                bonuses = (set?.Bonuses ?? new List<SetBonusInfo>()).Select(bonus => new
                {
                    count = bonus.Pieces,
                    text = bonus.Text,
                }).ToArray(),
            };
        }

        private EquipmentSetInfo GetSet(int setId)
        {
            var sets = _setsById;
            return sets != null && sets.TryGetValue(setId, out var set) ? set : null;
        }

        private List<ItemEntry> ResolveSetMembers(ItemEntry seed, string jobLabel)
        {
            var result = new List<ItemEntry>();
            var items = _itemsById;
            var memberMap = _setMemberIds;
            if (seed == null || items == null || memberMap == null || seed.PartSetIndex <= 0)
                return result;
            if (!memberMap.TryGetValue(seed.PartSetIndex, out var ids) || ids.Count == 0)
                return result;

            var candidates = new List<ItemEntry>(Math.Min(ids.Count, 256));
            var prefix = seed.Name != null && seed.Name.Length >= SetNamePrefixMin
                ? seed.Name.Substring(0, SetNamePrefixMin)
                : null;
            var prefilter = ids.Count > SetLooseMemberThreshold && prefix != null;
            foreach (var id in ids)
            {
                if (!items.TryGetValue(id, out var item))
                    continue;
                if (!MatchesUsableJob(item, jobLabel))
                    continue;
                if (prefilter
                    && item.Id != seed.Id
                    && (item.Name == null || !item.Name.StartsWith(prefix, StringComparison.Ordinal)))
                    continue;
                candidates.Add(item);
            }

            if (candidates.Count == 0)
                return result;

            var set = GetSet(seed.PartSetIndex);
            var tight = candidates.Count <= SetLooseMemberThreshold
                && candidates.GroupBy(item => item.TypeTag ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .All(group => group.Count() == 1);

            if (tight)
            {
                result.AddRange(candidates);
            }
            else
            {
                foreach (var group in candidates.GroupBy(item => item.TypeTag ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                {
                    var picked = PickSlotMember(group.ToList(), seed);
                    if (picked != null)
                        result.Add(picked);
                }
            }

            result.Sort((a, b) =>
            {
                var rank = SlotRank(a.TypeTag, set?.SlotTags).CompareTo(SlotRank(b.TypeTag, set?.SlotTags));
                return rank != 0 ? rank : a.Id.CompareTo(b.Id);
            });
            return result;
        }

        private static ItemEntry PickSlotMember(List<ItemEntry> group, ItemEntry seed)
        {
            if (group == null || group.Count == 0)
                return null;
            for (var i = 0; i < group.Count; i++)
            {
                if (group[i].Id == seed.Id)
                    return group[i];
            }

            if (group.Count == 1)
            {
                var only = group[0];
                return CommonPrefixLength(seed.Name, only.Name) >= SetNamePrefixMin ? only : null;
            }

            ItemEntry best = null;
            var bestPrefix = -1;
            var tied = false;
            foreach (var item in group)
            {
                var prefix = CommonPrefixLength(seed.Name, item.Name);
                if (prefix > bestPrefix)
                {
                    best = item;
                    bestPrefix = prefix;
                    tied = false;
                }
                else if (prefix == bestPrefix)
                {
                    tied = true;
                }
            }

            if (tied || best == null || bestPrefix < SetNamePrefixMin)
                return null;
            return best;
        }

        private static bool MatchesUsableJob(ItemEntry item, string jobLabel)
        {
            if (string.IsNullOrWhiteSpace(jobLabel))
                return true;
            var usable = item.UsableJob;
            if (string.IsNullOrWhiteSpace(usable)
                || usable.IndexOf("全职业", StringComparison.Ordinal) >= 0)
                return true;
            return usable.IndexOf(jobLabel, StringComparison.Ordinal) >= 0;
        }

        private static int CommonPrefixLength(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
                return 0;
            var n = Math.Min(left.Length, right.Length);
            var i = 0;
            while (i < n && left[i] == right[i])
                i++;
            return i;
        }

        private static int SlotRank(string tag, List<string> etcTags)
        {
            if (etcTags != null)
            {
                for (var i = 0; i < etcTags.Count; i++)
                {
                    if (string.Equals(etcTags[i], tag, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }

            for (var i = 0; i < SetSlotOrder.Length; i++)
            {
                if (string.Equals(SetSlotOrder[i], tag, StringComparison.OrdinalIgnoreCase))
                    return 100 + i;
            }

            return 1000;
        }

        private void BuildEquipmentSets(
            PvfArchive archive,
            List<ItemEntry> searchList,
            out Dictionary<int, EquipmentSetInfo> setsById,
            out Dictionary<int, List<int>> setMemberIds)
        {
            setsById = new Dictionary<int, EquipmentSetInfo>();
            setMemberIds = new Dictionary<int, List<int>>();
            if (archive == null || searchList == null)
                return;

            foreach (var item in searchList)
            {
                if (item == null || item.PartSetIndex <= 0)
                    continue;
                if (!setMemberIds.TryGetValue(item.PartSetIndex, out var ids))
                {
                    ids = new List<int>();
                    setMemberIds[item.PartSetIndex] = ids;
                }
                ids.Add(item.Id);
            }

            foreach (var set in ParseEquipmentPartSetTable(archive.GetFileContent("etc/equipmentpartset.etc")))
            {
                if (!setMemberIds.ContainsKey(set.Id))
                    continue;
                setsById[set.Id] = set;
            }

            foreach (var id in setMemberIds.Keys)
            {
                if (!setsById.ContainsKey(id))
                    setsById[id] = new EquipmentSetInfo { Id = id };
            }

            foreach (var set in setsById.Values)
            {
                if (string.IsNullOrWhiteSpace(set.ScriptPath))
                    continue;
                set.Bonuses = ParseSetBonuses(archive, set.ScriptPath);
            }
        }

        private static List<EquipmentSetInfo> ParseEquipmentPartSetTable(string content)
        {
            var sets = new List<EquipmentSetInfo>();
            if (string.IsNullOrWhiteSpace(content))
                return sets;

            var tokens = new List<SetTableToken>();
            foreach (Match match in SetTableTokenPattern.Matches(content))
            {
                if (match.Groups[1].Success)
                    tokens.Add(new SetTableToken(SetTableTokenKind.Text, match.Groups[1].Value));
                else if (match.Groups[2].Success)
                    tokens.Add(new SetTableToken(SetTableTokenKind.Tag, match.Groups[2].Value.Trim()));
                else
                    tokens.Add(new SetTableToken(SetTableTokenKind.Number, match.Groups[3].Value));
            }

            var i = 0;
            while (i < tokens.Count)
            {
                if (tokens[i].Kind != SetTableTokenKind.Number
                    || !int.TryParse(tokens[i].Value, out var setId)
                    || setId <= 0)
                {
                    i++;
                    continue;
                }

                if (i + 2 >= tokens.Count
                    || tokens[i + 1].Kind != SetTableTokenKind.Text
                    || tokens[i + 2].Kind != SetTableTokenKind.Text)
                {
                    i++;
                    continue;
                }

                var set = new EquipmentSetInfo
                {
                    Id = setId,
                    ScriptPath = tokens[i + 1].Value.Trim(),
                    Name = tokens[i + 2].Value.Trim(),
                };
                i += 3;
                while (i + 3 < tokens.Count
                    && tokens[i].Kind == SetTableTokenKind.Text
                    && tokens[i + 1].Kind == SetTableTokenKind.Tag
                    && tokens[i + 2].Kind == SetTableTokenKind.Number
                    && tokens[i + 3].Kind == SetTableTokenKind.Number)
                {
                    var tag = tokens[i + 1].Value.Trim().Trim('[', ']');
                    if (tag.Length > 0)
                        set.SlotTags.Add(tag);
                    i += 4;
                }

                sets.Add(set);
            }

            return sets;
        }

        private List<SetBonusInfo> ParseSetBonuses(PvfArchive archive, string scriptPath)
        {
            var bonuses = new List<SetBonusInfo>();
            var path = NormalizeSetScriptPath(scriptPath);
            if (archive == null || path.Length == 0)
                return bonuses;

            var text = archive.GetFileContent(path);
            if (string.IsNullOrWhiteSpace(text))
                return bonuses;

            try
            {
                var root = new ScriptParser().Parse(text);
                if (root == null)
                    return bonuses;

                foreach (var node in root.GetChildren("piece set ability"))
                {
                    if (!TryReadFirstInt(node, text, out var pieces) || pieces <= 0)
                        continue;
                    var explain = ReadScriptText(node, text, "parameter basic explain");
                    var line = explain;
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        var stats = CollectScriptStats(node, text);
                        line = stats.Count == 0 ? null : string.Join("\n", stats);
                    }
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    bonuses.Add(new SetBonusInfo { Pieces = pieces, Text = line.Trim() });
                }

                bonuses.Sort((a, b) => a.Pieces.CompareTo(b.Pieces));
            }
            catch
            {
                Interlocked.Increment(ref _parseFailures);
            }

            return bonuses;
        }

        private static string NormalizeSetScriptPath(string scriptPath)
        {
            var path = (scriptPath ?? string.Empty).Replace('\\', '/').Trim().TrimStart('/');
            if (path.Length == 0)
                return string.Empty;
            if (!path.StartsWith("equipment/", StringComparison.OrdinalIgnoreCase))
                path = "equipment/" + path;
            return path;
        }

        private static bool TryReadFirstInt(ScriptNode node, string content, out int value)
        {
            value = 0;
            if (node?.DataItems == null)
                return false;
            foreach (var item in node.DataItems)
            {
                var raw = StripTicks(item.GetContent(content));
                if (int.TryParse(raw, out value))
                    return true;
            }
            return false;
        }

        private enum SetTableTokenKind
        {
            Text,
            Tag,
            Number,
        }

        private readonly struct SetTableToken
        {
            public SetTableToken(SetTableTokenKind kind, string value)
            {
                Kind = kind;
                Value = value ?? string.Empty;
            }

            public SetTableTokenKind Kind { get; }
            public string Value { get; }
        }
    }
}
