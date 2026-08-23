using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GmPvfLib;

namespace DfoGmTool.Services
{
    // 预览属性走脚本节点：装备根节点、[enchant]、[enchant table] 各档。
    // EquipmentFile 只覆盖发放所需字段，不负责展示。
    public sealed partial class PvfIndexService
    {
        private static readonly HashSet<string> PreviewSkipTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "name", "name2", "explain", "basic explain", "detail explain", "flavor text",
            "explain alpha", "dictionary text", "rarity possible explain",
            "icon", "icon mark", "extra icon", "field image", "move wav", "use wav",
            "image packs", "specific sound", "custom animation",
            "equipment type", "stackable type", "sub type", "attach type",
            "item group name", "item category", "equipment grade",
            "usable job", "suitable job", "animation job",
            "grade", "rarity", "minimum level", "maximum level",
            "part set index", "effect part set index", "reference effect part set index",
            "dynamic part set index", "piece set ability", "parameter basic explain",
            "set name", "set item",
            "layer variation", "equipment ani script", "variation", "expand ani",
            "level section ani", "level section ability", "level linear ability",
            "avatar type select", "avatar func filter", "avatar package preview info",
            "avatar emblem target type", "avatar emblem socket num", "emblem socket default",
            "avatar select ability change", "avatar select ability except",
            "hide equipment", "hide layer", "hide grow avatar", "full avatar",
            "spectrum avatar", "spectrum",
            "package data", "package data selection", "string data", "int data", "command",
            "packagable", "open confirm window on use", "expert type", "monster card id",
            "enchant", "enchant table", "enchant index",
            "input item", "output item", "need skill", "need material",
            "required skill", "required job skill",
            "booster info", "booster select category", "booster category num",
            "booster selection num", "booster category name", "consume item",
            "price", "repair price", "add repair price", "value", "add value",
            "add price", "cash", "finish point price", "creation rate",
            "cooltime group", "cooltime maintenance",
            "if", "then", "multiple then",
            "impossible contents",
            "usable period", "expiration date", "usable expired item",
            "rental service", "daily purchase limit",
            "ability case index", "enable dye", "dye type", "dye info",
            "aura hud icon", "aurora graphic effects", "aura pos datas", "aura ability",
            "linking quest index", "character name y revision", "character item check",
            "force result item rule", "revisionpvpgrade", "pvp",
            "colosseum disjoint info", "emancipate", "emancipate ticket",
            "limit upgradable level", "upgrade cost discount",
            "seed correction index", "weapon overspec per item",
            "no random", "special ability", "chat emoticon index",
            "all skill item container", "usable avatar index",
            "dont change grow type", "dungeon index check",
            "minimum itemmaking level", "minimum rank", "type",
            "creature species", "creature minimum level", "use count limit",
            "skill data up",
        };

        private static readonly Dictionary<string, PreviewStatSpec> PreviewStatSpecs =
            new Dictionary<string, PreviewStatSpec>(StringComparer.OrdinalIgnoreCase)
            {
                { "physical attack", Spec("physical attack", "物理攻击力", false) },
                { "add physical attack", Spec("physical attack", "物理攻击力", false) },
                { "equipment physical attack", Spec("equipment physical attack", "物理攻击力", false) },
                { "add equipment physical attack", Spec("equipment physical attack", "物理攻击力", false) },
                { "magical attack", Spec("magical attack", "魔法攻击力", false) },
                { "add magical attack", Spec("magical attack", "魔法攻击力", false) },
                { "equipment magical attack", Spec("equipment magical attack", "魔法攻击力", false) },
                { "add equipment magical attack", Spec("equipment magical attack", "魔法攻击力", false) },
                { "physical defense", Spec("physical defense", "物理防御力", false) },
                { "add physical defense", Spec("physical defense", "物理防御力", false) },
                { "equipment physical defense", Spec("equipment physical defense", "物理防御力", false) },
                { "add equipment physical defense", Spec("equipment physical defense", "物理防御力", false) },
                { "magical defense", Spec("magical defense", "魔法防御力", false) },
                { "add magical defense", Spec("magical defense", "魔法防御力", false) },
                { "equipment magical defense", Spec("equipment magical defense", "魔法防御力", false) },
                { "add equipment magical defense", Spec("equipment magical defense", "魔法防御力", false) },
                { "separate attack", Spec("separate attack", "独立攻击", false) },
                { "add separate attack", Spec("separate attack", "独立攻击", false) },
                { "HP MAX", Spec("hp max", "HP MAX", false) },
                { "MP MAX", Spec("mp max", "MP MAX", false) },
                { "HP MAX rate", Spec("hp max rate", "HP MAX", true) },
                { "MP MAX rate", Spec("mp max rate", "MP MAX", true) },
                { "HP regen speed", Spec("hp regen", "体力恢复", false) },
                { "MP regen speed", Spec("mp regen", "精神恢复", false) },
                { "attack speed", Spec("attack speed", "攻击速度", true) },
                { "add attack speed", Spec("attack speed", "攻击速度", true) },
                { "cast speed", Spec("cast speed", "施放速度", true) },
                { "add cast speed", Spec("cast speed", "施放速度", true) },
                { "move speed", Spec("move speed", "移动速度", true) },
                { "add move speed", Spec("move speed", "移动速度", true) },
                { "physical critical hit", Spec("phys crit", "物理暴击", true) },
                { "add physical critical hit", Spec("phys crit", "物理暴击", true) },
                { "magical critical hit", Spec("mag crit", "魔法暴击", true) },
                { "add magical critical hit", Spec("mag crit", "魔法暴击", true) },
                { "physical back attack critical hit", Spec("phys back crit", "背击物理暴击", true) },
                { "magical back attack critical hit", Spec("mag back crit", "背击魔法暴击", true) },
                { "hit recovery", Spec("hit recovery", "硬直", false) },
                { "attack success", Spec("attack success", "命中率", true) },
                { "jump power", Spec("jump power", "跳跃力", false) },
                { "stuck", Spec("stuck", "命中", true) },
                { "add stuck", Spec("stuck", "命中", true) },
                { "stuck resistance", Spec("stuck resistance", "回避率", true) },
                { "add stuck resistance", Spec("stuck resistance", "回避率", true) },
                { "inventory limit", Spec("inventory limit", "负重上限", false) },
                { "weight", Spec("weight", "重量", false) },
                { "durability", Spec("durability", "耐久度", false) },
                { "stack limit", Spec("stack limit", "堆叠上限", false) },
                { "cool time", Spec("cool time", "冷却", false) },
                { "fire attack", Spec("fire attack", "火属性强化", false) },
                { "water attack", Spec("water attack", "冰属性强化", false) },
                { "light attack", Spec("light attack", "光属性强化", false) },
                { "dark attack", Spec("dark attack", "暗属性强化", false) },
                { "all elemental attack", Spec("all elemental attack", "所有属性强化", false) },
                { "fire resistance", Spec("fire resistance", "火属性抗性", false) },
                { "water resistance", Spec("water resistance", "冰属性抗性", false) },
                { "light resistance", Spec("light resistance", "光属性抗性", false) },
                { "dark resistance", Spec("dark resistance", "暗属性抗性", false) },
                { "all elemental resistance", Spec("all elemental resistance", "所有属性抗性", false) },
                { "poison resistance", Spec("poison resistance", "中毒抗性", false) },
                { "bleeding resistance", Spec("bleeding resistance", "出血抗性", false) },
                { "freeze resistance", Spec("freeze resistance", "冰冻抗性", false) },
                { "curse resistance", Spec("curse resistance", "诅咒抗性", false) },
                { "sleep resistance", Spec("sleep resistance", "睡眠抗性", false) },
                { "slow resistance", Spec("slow resistance", "减速抗性", false) },
                { "burn resistance", Spec("burn resistance", "灼伤抗性", false) },
                { "confuse resistance", Spec("confuse resistance", "混乱抗性", false) },
                { "all activestatus resistance", Spec("all status resistance", "所有异常抗性", false) },
                { "anti evil", Spec("anti evil", "抗魔值", false) },
                { "lift up", Spec("lift up", "浮空力", false) },
                { "push aside", Spec("push aside", "击退力", false) },
                { "medal", Spec("medal", "勋章点数", false) },
                { "guild power war point", Spec("guild war", "公会积分", false) },
                { "strength", Spec("strength", "力量", false) },
                { "intelligence", Spec("intelligence", "智力", false) },
                { "vitality", Spec("vitality", "体力", false) },
                { "spirit", Spec("spirit", "精神", false) },
                { "all stat", Spec("all stat", "所有属性", false) },
                { "element tolerance fire", Spec("fire resistance", "火属性抗性", false) },
                { "element tolerance water", Spec("water resistance", "冰属性抗性", false) },
                { "element tolerance light", Spec("light resistance", "光属性抗性", false) },
                { "element tolerance dark", Spec("dark resistance", "暗属性抗性", false) },
                { "element tolerance all", Spec("all elemental resistance", "所有属性抗性", false) },
                { "activestatus tolerance all", Spec("all status resistance", "所有异常抗性", false) },
                { "physical skill attack", Spec("physical skill attack", "物理技能攻击力", false) },
                { "magical skill attack", Spec("magical skill attack", "魔法技能攻击力", false) },
            };

        private static readonly Dictionary<string, string> AvatarAbilityLabels =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "PHYSICAL_ATTACK", "物理攻击力" },
                { "MAGICAL_ATTACK", "魔法攻击力" },
                { "PHYSICAL_DEFENSE", "物理防御力" },
                { "MAGICAL_DEFENSE", "魔法防御力" },
                { "HP_MAX", "HP MAX" },
                { "MP_MAX", "MP MAX" },
                { "HP_REGENRATE", "体力恢复" },
                { "MP_REGENRATE", "精神恢复" },
                { "ATTACK_SPEED", "攻击速度" },
                { "CAST_SPEED", "施放速度" },
                { "MOVE_SPEED", "移动速度" },
                { "HIT_RECOVERY", "硬直" },
                { "INVENTORY_LIMIT", "负重上限" },
                { "FIRE_RESISTANCE", "火属性抗性" },
                { "WATER_RESISTANCE", "冰属性抗性" },
                { "LIGHT_RESISTANCE", "光属性抗性" },
                { "DARK_RESISTANCE", "暗属性抗性" },
                { "ALL_ELEMENTAL_RESISTANCE", "所有属性抗性" },
                { "EQUIPMENT_PHYSICAL_ATTACK", "物理攻击力" },
                { "EQUIPMENT_MAGICAL_ATTACK", "魔法攻击力" },
                { "EQUIPMENT_PHYSICAL_DEFENSE", "物理防御力" },
                { "EQUIPMENT_MAGICAL_DEFENSE", "魔法防御力" },
                { "JUMP_POWER", "跳跃力" },
                { "ACTIVESTATUS_TOLERANCE_ALL", "所有异常抗性" },
                { "ELEMENT_TOLERANCE_FIRE", "火属性抗性" },
                { "ELEMENT_TOLERANCE_WATER", "冰属性抗性" },
                { "ELEMENT_TOLERANCE_LIGHT", "光属性抗性" },
                { "ELEMENT_TOLERANCE_DARK", "暗属性抗性" },
                { "SKILL_LEVEL", "技能等级" },
            };

        private static readonly Dictionary<string, string> SlotLabels =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "weapon", "武器" }, { "coat", "上衣" }, { "shoulder", "头肩" },
                { "pants", "下装" }, { "shoes", "鞋" }, { "waist", "腰带" },
                { "amulet", "项链" }, { "wrist", "手镯" }, { "ring", "戒指" },
                { "support", "辅助装备" }, { "magic stone", "魔法石" },
                { "title name", "称号" }, { "flag", "公会勋章" },
            };

        private static readonly string[] PreviewStatOrder =
        {
            "equipment physical attack", "physical attack",
            "equipment magical attack", "magical attack",
            "equipment physical defense", "physical defense",
            "equipment magical defense", "magical defense",
            "separate attack",
            "hp max", "mp max", "hp max rate", "mp max rate",
            "hp regen", "mp regen",
            "attack speed", "cast speed", "move speed",
            "phys crit", "mag crit", "phys back crit", "mag back crit",
            "hit recovery", "attack success", "jump power",
            "stuck", "stuck resistance",
            "fire attack", "water attack", "light attack", "dark attack", "all elemental attack",
            "fire resistance", "water resistance", "light resistance", "dark resistance", "all elemental resistance",
            "poison resistance", "bleeding resistance", "freeze resistance", "curse resistance",
            "sleep resistance", "slow resistance", "burn resistance", "confuse resistance",
            "all status resistance", "anti evil", "lift up", "push aside",
            "inventory limit", "durability", "weight", "stack limit", "cool time",
            "medal", "guild war", "strength", "intelligence", "vitality", "spirit", "all stat",
            "physical skill attack", "magical skill attack",
        };

        private static readonly Regex TaggedNumberPattern = new Regex(
            @"\[(?<type>[^\]/\r\n]+)\](?<values>(?:[ \t\r\n]+-?\d+)+)",
            RegexOptions.Compiled);
        private static readonly Regex EmblemSocketPattern = new Regex(
            @"\[\s*([ABCDSM])\s+socket\s*\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static PreviewStatSpec Spec(string key, string label, bool percent)
        {
            return new PreviewStatSpec(key, label, percent);
        }

        private static List<string> CollectScriptStats(ScriptNode root, string content)
        {
            var stats = new List<string>();
            if (root?.Children == null || string.IsNullOrEmpty(content))
                return stats;

            var bags = new Dictionary<string, PreviewStatBag>(StringComparer.OrdinalIgnoreCase);
            var extras = new List<string>();
            CollectChildStats(root, content, bags, extras);
            FlushStatBags(stats, bags, extras);
            return stats;
        }

        private static void CollectChildStats(
            ScriptNode parent,
            string content,
            Dictionary<string, PreviewStatBag> bags,
            List<string> extras)
        {
            if (parent?.Children == null)
                return;
            foreach (var node in parent.Children)
                CollectNodeStat(node, content, bags, extras);
        }

        private static void CollectNodeStat(
            ScriptNode node,
            string content,
            Dictionary<string, PreviewStatBag> bags,
            List<string> extras)
        {
            var tag = (node?.Tag ?? string.Empty).Trim();
            if (tag.Length == 0 || tag.IndexOf('注') >= 0)
                return;

            if (string.Equals(tag, "enchant", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "increase status type", StringComparison.OrdinalIgnoreCase))
            {
                CollectEnchantSpecials(node, content, extras);
                CollectTaggedNumberStats(node, content, bags);
                CollectIncreaseStatusData(node, content, bags);
                return;
            }

            if (string.Equals(tag, "enchant table", StringComparison.OrdinalIgnoreCase))
            {
                CollectEnchantTable(node, content, bags, extras);
                return;
            }

            if (string.Equals(tag, "string data", StringComparison.OrdinalIgnoreCase))
            {
                AppendEnchantSlots(extras, node, content);
                return;
            }

            if (string.Equals(tag, "avatar emblem target type", StringComparison.OrdinalIgnoreCase))
            {
                AppendEmblemSocket(extras, node, content);
                return;
            }

            if (PreviewSkipTags.Contains(tag)
                || string.Equals(tag, "pvp", StringComparison.OrdinalIgnoreCase))
                return;

            if (string.Equals(tag, "avatar select ability", StringComparison.OrdinalIgnoreCase))
            {
                AppendAvatarSelectAbility(extras, node, content);
                return;
            }

            if (string.Equals(tag, "skill levelup", StringComparison.OrdinalIgnoreCase))
            {
                AppendSkillLevelUp(extras, node, content);
                return;
            }

            if (string.Equals(tag, "elemental property", StringComparison.OrdinalIgnoreCase))
            {
                AppendElementalProperty(extras, node, content);
                return;
            }

            if (string.Equals(tag, "item aura", StringComparison.OrdinalIgnoreCase))
            {
                AppendItemAura(extras, node, content);
                return;
            }

            if (string.Equals(tag, "room list move speed rate", StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadFirstNumber(node, content, out var rate))
                    extras.Add("城镇移动速度 " + FormatSigned((int)Math.Round(rate * 100)) + "%");
                return;
            }

            if (TryGetStatSpec(tag, out var spec))
            {
                AccumulateStat(bags, spec, node, content);
                return;
            }

            if ((node.Children == null || node.Children.Count == 0)
                && TryFormatUnknownStat(tag, node, content, out var unknown))
                extras.Add(unknown);
        }

        private static void CollectEnchantTable(
            ScriptNode table,
            string content,
            Dictionary<string, PreviewStatBag> bags,
            List<string> extras)
        {
            if (table?.Children == null)
                return;

            foreach (var index in table.GetChildren("enchant index"))
            {
                var tierBags = new Dictionary<string, PreviewStatBag>(StringComparer.OrdinalIgnoreCase);
                CollectEnchantSpecials(index, content, extras);
                CollectTaggedNumberStats(index, content, tierBags);
                foreach (var pair in tierBags)
                {
                    if (!bags.TryGetValue(pair.Key, out var bag))
                    {
                        bags[pair.Key] = pair.Value;
                        continue;
                    }
                    bag.MergeRange(pair.Value);
                }
            }
        }

        private static void CollectEnchantSpecials(ScriptNode node, string content, List<string> extras)
        {
            if (node?.Children == null)
                return;
            foreach (var child in node.Children)
            {
                var tag = (child.Tag ?? string.Empty).Trim();
                if (string.Equals(tag, "pvp", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(tag, "skill levelup", StringComparison.OrdinalIgnoreCase))
                    AppendSkillLevelUp(extras, child, content);
                else if (string.Equals(tag, "elemental property", StringComparison.OrdinalIgnoreCase))
                {
                    AppendElementalProperty(extras, child, content);
                }
                else if (string.Equals(tag, "avatar select ability", StringComparison.OrdinalIgnoreCase))
                    AppendAvatarSelectAbility(extras, child, content);
                else
                    CollectEnchantSpecials(child, content, extras);
            }
        }

        private static void CollectTaggedNumberStats(
            ScriptNode node,
            string content,
            Dictionary<string, PreviewStatBag> bags)
        {
            if (node == null || string.IsNullOrEmpty(content))
                return;
            var raw = node.GetContent(content);
            if (string.IsNullOrEmpty(raw))
                return;

            foreach (Match match in TaggedNumberPattern.Matches(raw))
            {
                var type = match.Groups["type"].Value.Trim();
                if (type.Length == 0 || PreviewSkipTags.Contains(type) || !TryGetStatSpec(type, out var spec))
                    continue;

                var values = new List<int>();
                foreach (var token in match.Groups["values"].Value.Split(
                    new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (TryParseInt(token, out var number))
                        values.Add(number);
                }
                if (values.Count > 0)
                    ApplyStatValues(bags, spec, values);
            }
        }

        private static void CollectIncreaseStatusData(
            ScriptNode node,
            string content,
            Dictionary<string, PreviewStatBag> bags)
        {
            if (node?.DataItems == null)
                return;
            foreach (var item in node.DataItems)
            {
                var raw = item.GetContent(content) ?? string.Empty;
                var match = Regex.Match(raw, @"\[(?<type>[^\]]+)\](?<values>(?:\s+-?\d+)*)");
                if (!match.Success)
                    continue;
                var type = match.Groups["type"].Value.Trim();
                if (!TryGetStatSpec(type, out var spec))
                    continue;
                var values = new List<int>();
                foreach (var token in match.Groups["values"].Value.Split(
                    new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (TryParseInt(token, out var number))
                        values.Add(number);
                }
                if (values.Count == 0)
                    continue;
                ApplyStatValues(bags, spec, values);
            }
        }

        private static void AppendEnchantSlots(List<string> extras, ScriptNode node, string content)
        {
            var slots = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(JoinDataItems(node, content) ?? string.Empty, @"\[([^\]/\r\n]+)\]"))
            {
                var tag = match.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(tag) || tag.IndexOf('/') >= 0 || tag.IndexOf('.') >= 0)
                    continue;
                if (!SlotLabels.TryGetValue(tag, out var label) || !seen.Add(tag))
                    continue;
                slots.Add(label);
            }
            if (slots.Count > 0)
                extras.Add("可附魔 " + string.Join(" / ", slots));
        }

        private static void AppendEmblemSocket(List<string> extras, ScriptNode node, string content)
        {
            var sockets = ReadEmblemSockets(JoinDataItems(node, content));
            if (sockets.Count > 0)
                extras.Add("徽章孔 " + string.Join(" / ", sockets));
        }

        private static List<string> ReadEmblemSockets(string raw)
        {
            var sockets = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(raw))
                return sockets;

            foreach (Match match in EmblemSocketPattern.Matches(raw))
            {
                var letter = match.Groups[1].Value.ToUpperInvariant();
                if (seen.Add(letter))
                    sockets.Add(letter);
            }

            if (sockets.Count == 0)
            {
                foreach (Match match in Regex.Matches(raw, @"\b([ABCDSM])\s+socket\b", RegexOptions.IgnoreCase))
                {
                    var letter = match.Groups[1].Value.ToUpperInvariant();
                    if (seen.Add(letter))
                        sockets.Add(letter);
                }
            }

            return sockets;
        }

        private static void FlushStatBags(
            List<string> stats,
            Dictionary<string, PreviewStatBag> bags,
            List<string> extras)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in PreviewStatOrder)
            {
                if (!seen.Add(key) || !bags.TryGetValue(key, out var bag))
                    continue;
                var line = bag.Format();
                if (line != null)
                    stats.Add(line);
            }

            foreach (var extra in extras)
            {
                var cleaned = RewriteScriptMarkers(extra);
                if (!string.IsNullOrWhiteSpace(cleaned))
                    stats.Add(cleaned);
            }
        }

        private static bool TryGetStatSpec(string tag, out PreviewStatSpec spec)
        {
            var key = NormalizeStatTag(tag);
            return PreviewStatSpecs.TryGetValue(key, out spec)
                || PreviewStatSpecs.TryGetValue(tag, out spec);
        }

        private static string NormalizeStatTag(string tag)
        {
            return (tag ?? string.Empty).Replace('_', ' ').Trim();
        }

        private static void AccumulateStat(
            Dictionary<string, PreviewStatBag> bags,
            PreviewStatSpec spec,
            ScriptNode node,
            string content)
        {
            if (!TryReadNumbers(node, content, out var values) || values.Count == 0)
                return;
            ApplyStatValues(bags, spec, values);
        }

        private static void ApplyStatValues(
            Dictionary<string, PreviewStatBag> bags,
            PreviewStatSpec spec,
            List<int> values)
        {
            if (!bags.TryGetValue(spec.Key, out var bag))
            {
                bag = new PreviewStatBag(spec.Label, spec.Percent);
                bags[spec.Key] = bag;
            }

            if (values.Count >= 2)
            {
                var low = Math.Min(values[0], values[1]);
                var high = Math.Max(values[0], values[1]);
                bag.AddRange(low, high);
            }
            else
                bag.Add(values[0]);
        }

        private static void TrimStackableMetaStats(List<string> stats)
        {
            if (stats == null || stats.Count == 0)
                return;
            stats.RemoveAll(line =>
                line.StartsWith("堆叠上限", StringComparison.Ordinal)
                || line.StartsWith("重量", StringComparison.Ordinal));
        }

        private static void LinkBeadCardStats(List<ItemEntry> items)
        {
            if (items == null || items.Count == 0)
                return;

            var byId = new Dictionary<int, ItemEntry>();
            foreach (var item in items)
            {
                if (item != null && !byId.ContainsKey(item.Id))
                    byId[item.Id] = item;
            }

            foreach (var item in items)
            {
                if (item == null || item.LinkedCardId <= 0)
                    continue;
                if (!byId.TryGetValue(item.LinkedCardId, out var card) || card.Stats == null)
                    continue;

                var merged = new List<string>();
                foreach (var line in card.Stats)
                {
                    if (!string.IsNullOrWhiteSpace(line)
                        && !line.StartsWith("重量", StringComparison.Ordinal)
                        && !line.StartsWith("堆叠上限", StringComparison.Ordinal)
                        && !merged.Contains(line))
                        merged.Add(line);
                }
                if (item.Stats != null)
                {
                    foreach (var line in item.Stats)
                    {
                        if (string.IsNullOrWhiteSpace(line)
                            || line.StartsWith("冷却", StringComparison.Ordinal)
                            || merged.Contains(line))
                            continue;
                        merged.Add(line);
                    }
                }
                item.Stats = merged;
            }
        }

        private static void AppendAvatarSelectAbility(List<string> extras, ScriptNode node, string content)
        {
            var tokens = Tokenize(node, content);
            for (var i = 0; i < tokens.Count; i++)
            {
                var name = StripTicks(tokens[i]).Trim('[', ']');
                if (string.IsNullOrEmpty(name) || char.IsDigit(name[0]))
                    continue;
                if (!AvatarAbilityLabels.TryGetValue(name, out var label))
                    label = name;

                var op = i + 1 < tokens.Count ? StripTicks(tokens[i + 1]) : "+";
                if (i + 2 >= tokens.Count || !TryParseInt(tokens[i + 2], out var value))
                    continue;

                extras.Add("可选属性 " + label + " " + FormatOperatorValue(op, value));
                i += 2;
            }
        }

        private static void AppendSkillLevelUp(List<string> extras, ScriptNode node, string content)
        {
            var tokens = Tokenize(node, content);
            for (var i = 0; i + 2 < tokens.Count; i += 3)
            {
                var job = StripTicks(tokens[i]).Trim('[', ']');
                if (!TryParseInt(tokens[i + 1], out var skillId) || !TryParseInt(tokens[i + 2], out var level))
                    continue;
                var jobLabel = JobLabels.TryGetValue(job, out var mapped) ? mapped : job;
                extras.Add("技能等级 " + FormatSigned(level) + " (" + jobLabel + " " + skillId + ")");
            }
        }

        private static void AppendItemAura(List<string> extras, ScriptNode node, string content)
        {
            var tokens = Tokenize(node, content);
            for (var i = 0; i + 2 < tokens.Count; i++)
            {
                var name = StripTicks(tokens[i]);
                if (string.IsNullOrEmpty(name) || name == "+" || name == "%" || TryParseInt(name, out _))
                    continue;
                var specKey = name.Replace('_', ' ');
                var label = PreviewStatSpecs.TryGetValue(specKey, out var spec) ? spec.Label : name;
                var op = i + 1 < tokens.Count ? StripTicks(tokens[i + 1]) : "+";
                if (i + 2 >= tokens.Count || !TryParseInt(tokens[i + 2], out var value))
                    continue;
                extras.Add("光环 " + label + " " + FormatOperatorValue(op, value));
                i += 2;
            }
        }

        private static bool TryFormatUnknownStat(string tag, ScriptNode node, string content, out string line)
        {
            line = null;
            var raw = JoinDataItems(node, content);
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            if (raw.IndexOf('/') >= 0 || raw.IndexOf(".img", StringComparison.OrdinalIgnoreCase) >= 0
                || raw.IndexOf(".ani", StringComparison.OrdinalIgnoreCase) >= 0
                || raw.IndexOf(".lay", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (!TryReadNumbers(node, content, out var values) || values.Count == 0)
                return false;
            if (IsHiddenMetaTag(tag))
                return false;

            var label = tag;
            if (AvatarAbilityLabels.TryGetValue(tag, out var mapped)
                || AvatarAbilityLabels.TryGetValue(NormalizeStatTag(tag), out mapped))
                label = mapped;
            else if (TryGetStatSpec(tag, out var spec))
                label = spec.Label;
            else if (IsAsciiIdent(tag))
                return false;

            var low = values[0];
            var high = values.Count >= 2 ? values[1] : values[0];
            if (low > high)
            {
                var tmp = low;
                low = high;
                high = tmp;
            }

            var builder = new StringBuilder(label);
            builder.Append(' ');
            if (low != high)
                builder.Append(FormatSigned(low)).Append(" ~ ").Append(FormatSigned(high));
            else
                builder.Append(FormatSigned(low));
            line = builder.ToString();
            return true;
        }

        private static bool IsHiddenMetaTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return true;
            return tag.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0
                || tag.IndexOf("confirm", StringComparison.OrdinalIgnoreCase) >= 0
                || tag.IndexOf("packag", StringComparison.OrdinalIgnoreCase) >= 0
                || tag.IndexOf("expert", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsAsciiIdent(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return false;
            foreach (var ch in tag)
            {
                if (ch > 127)
                    return false;
            }
            return true;
        }

        private static void AppendElementalProperty(List<string> extras, ScriptNode node, string content)
        {
            var labels = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in ExtractBracketTags(JoinDataItems(node, content)))
            {
                var label = TranslateElement(tag);
                if (string.IsNullOrWhiteSpace(label) || IsAsciiIdent(label) || !seen.Add(label))
                    continue;
                labels.Add(label);
            }

            if (labels.Count == 0)
            {
                var fallback = TranslateElement(StripTicks(JoinDataItems(node, content)));
                if (!string.IsNullOrWhiteSpace(fallback) && !IsAsciiIdent(fallback))
                    labels.Add(fallback);
            }

            if (labels.Count > 0)
            {
                var line = "附加属性 " + string.Join(" / ", labels);
                if (!extras.Contains(line))
                    extras.Add(line);
            }
        }

        private static List<string> ExtractBracketTags(string raw)
        {
            var tags = new List<string>();
            if (string.IsNullOrEmpty(raw))
                return tags;
            foreach (Match match in Regex.Matches(raw, @"\[([^\]/\r\n]+)\]"))
            {
                var tag = match.Groups[1].Value.Trim();
                if (tag.Length > 0)
                    tags.Add(tag);
            }
            return tags;
        }

        private static string TranslateElement(string raw)
        {
            var value = (raw ?? string.Empty).Trim().Trim('[', ']');
            if (value.EndsWith(" element", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - 8).Trim();
            if (value.StartsWith("fire", StringComparison.OrdinalIgnoreCase))
                return "火";
            if (value.StartsWith("water", StringComparison.OrdinalIgnoreCase) || value.StartsWith("ice", StringComparison.OrdinalIgnoreCase))
                return "冰";
            if (value.StartsWith("light", StringComparison.OrdinalIgnoreCase))
                return "光";
            if (value.StartsWith("dark", StringComparison.OrdinalIgnoreCase))
                return "暗";
            return value;
        }

        private static List<string> Tokenize(ScriptNode node, string content)
        {
            var tokens = new List<string>();
            if (node?.DataItems == null)
                return tokens;
            foreach (var item in node.DataItems)
            {
                foreach (var token in (item.GetContent(content) ?? string.Empty)
                    .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    tokens.Add(token.Trim());
            }
            return tokens;
        }

        private static bool TryReadNumbers(ScriptNode node, string content, out List<int> values)
        {
            values = new List<int>();
            foreach (var token in Tokenize(node, content))
            {
                if (TryParseInt(StripTicks(token), out var number))
                    values.Add(number);
            }
            return values.Count > 0;
        }

        private static bool TryReadFirstNumber(ScriptNode node, string content, out double value)
        {
            value = 0;
            foreach (var token in Tokenize(node, content))
            {
                if (double.TryParse(StripTicks(token), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    return true;
            }
            return false;
        }

        private static bool TryParseInt(string token, out int value)
        {
            return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static string FormatSigned(int value)
        {
            return value > 0
                ? "+" + value.ToString(CultureInfo.InvariantCulture)
                : value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatOperatorValue(string op, int value)
        {
            if (op == "%")
                return FormatSigned(value) + "%";
            return FormatSigned(value);
        }

        private readonly struct PreviewStatSpec
        {
            public PreviewStatSpec(string key, string label, bool percent)
            {
                Key = key;
                Label = label;
                Percent = percent;
            }

            public string Key { get; }
            public string Label { get; }
            public bool Percent { get; }
        }

        private sealed class PreviewStatBag
        {
            private int _low;
            private int _high;
            private bool _hasValue;

            public PreviewStatBag(string label, bool percent)
            {
                Label = label;
                Percent = percent;
            }

            public string Label { get; }
            public bool Percent { get; }

            public void Add(int value)
            {
                if (!_hasValue)
                {
                    _low = value;
                    _high = value;
                    _hasValue = true;
                    return;
                }

                _low += value;
                _high += value;
            }

            public void AddRange(int low, int high)
            {
                if (!_hasValue)
                {
                    _low = low;
                    _high = high;
                    _hasValue = true;
                    return;
                }

                _low += low;
                _high += high;
            }

            public void MergeRange(PreviewStatBag other)
            {
                if (other == null || !other._hasValue)
                    return;
                if (!_hasValue)
                {
                    _low = other._low;
                    _high = other._high;
                    _hasValue = true;
                    return;
                }

                if (other._low < _low)
                    _low = other._low;
                if (other._high > _high)
                    _high = other._high;
            }

            public string Format()
            {
                if (!_hasValue || (_low == 0 && _high == 0))
                    return null;
                if (string.Equals(Label, "重量", StringComparison.Ordinal) || string.Equals(Label, "耐久度", StringComparison.Ordinal)
                    || string.Equals(Label, "堆叠上限", StringComparison.Ordinal) || string.Equals(Label, "冷却", StringComparison.Ordinal))
                {
                    return Label + " " + _low.ToString(CultureInfo.InvariantCulture);
                }

                var suffix = Percent ? "%" : "";
                if (_low == _high)
                    return Label + " " + FormatSigned(_low) + suffix;
                return Label + " " + FormatSigned(_low) + suffix + " ~ " + FormatSigned(_high) + suffix;
            }
        }
    }
}
