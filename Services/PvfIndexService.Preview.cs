using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GmPvfLib;

namespace DfoGmTool.Services
{
    // 索引构建时提取预览字段；运行时只读，不再打开 PVF。
    public sealed partial class PvfIndexService
    {
        private static readonly Regex FormatPlaceholderPattern = new Regex(
            @"%[A-Za-z][A-Za-z0-9 ]*%",
            RegexOptions.Compiled);
        private static readonly Regex NewlineCodePattern = new Regex(@"#n", RegexOptions.Compiled);
        private static readonly Regex ScriptMarkerPattern = new Regex(
            @"\[([^\]/\r\n]+)\]",
            RegexOptions.Compiled);
        private static readonly Dictionary<string, string> JobLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "all", "全职业" },
            { "swordman", "鬼剑士" },
            { "fighter", "格斗家" },
            { "gunner", "神枪手" },
            { "mage", "魔法师" },
            { "priest", "圣职者" },
            { "thief", "暗夜使者" },
            { "knight", "骑士" },
            { "demonic lancer", "魔枪士" },
            { "demonic swordman", "黑暗武士" },
            { "at swordman", "女鬼剑" },
            { "at fighter", "女格斗" },
            { "at gunner", "女枪手" },
            { "at mage", "男法师" },
            { "creator mage", "缔造者" },
        };

        public bool TryGetIcon(int itemId, out string iconPath, out int iconFrame, out string markPath, out int markFrame)
        {
            iconPath = null;
            iconFrame = 0;
            markPath = null;
            markFrame = 0;
            var items = _itemsById;
            if (items == null || !items.TryGetValue(itemId, out var entry) || string.IsNullOrWhiteSpace(entry.IconPath))
                return false;

            iconPath = entry.IconPath;
            iconFrame = entry.IconFrame;
            markPath = entry.IconMarkPath;
            markFrame = entry.IconMarkFrame;
            return true;
        }

        public object GetItemPreview(int itemId)
        {
            var items = _itemsById;
            if (items == null)
                return new { success = false, error = _buildError != null ? "索引构建失败: " + _buildError : "物品索引还在构建中, 稍等几秒再看预览" };

            if (!items.TryGetValue(itemId, out var entry))
                return new { success = false, error = "PVF 中没有这件物品" };

            return new
            {
                success = true,
                itemId = entry.Id,
                name = entry.Name,
                kind = entry.Kind,
                tag = entry.TypeTag,
                segment = entry.Segment,
                special = entry.Special,
                rarity = entry.Rarity,
                minLevel = entry.MinLevel,
                hasIcon = !string.IsNullOrWhiteSpace(entry.IconPath),
                explain = entry.Explain,
                basicExplain = entry.BasicExplain,
                detailExplain = entry.DetailExplain,
                flavorText = entry.FlavorText,
                usableJob = entry.UsableJob,
                stats = entry.Stats ?? new List<string>(),
                set = BuildSetPreview(entry, null),
                templateExpiration = new
                {
                    known = true,
                    absoluteExpireTime = entry.AbsoluteExpirationUnixTime,
                    usablePeriodDays = entry.UsablePeriodDays,
                    dailyDeleteItem = entry.DailyDeleteItem,
                    invalid = entry.HasInvalidExpirationDefinition,
                },
            };
        }

        private static void FillPreview(ItemEntry entry, PvfModelBase model, string text)
        {
            if (entry == null || model?.Root == null || string.IsNullOrEmpty(text))
                return;

            if (TryReadIcon(model.Root, text, "icon", out var iconPath, out var iconFrame))
            {
                entry.IconPath = iconPath;
                entry.IconFrame = iconFrame;
            }

            if (TryReadIcon(model.Root, text, "icon mark", out var markPath, out var markFrame))
            {
                entry.IconMarkPath = markPath;
                entry.IconMarkFrame = markFrame;
            }

            entry.Explain = ReadScriptText(model.Root, text, "explain");
            entry.BasicExplain = ReadScriptText(model.Root, text, "basic explain")
                ?? ReadScriptText(model.Root, text, "parameter basic explain");
            entry.DetailExplain = ReadScriptText(model.Root, text, "detail explain");
            entry.FlavorText = ReadScriptText(model.Root, text, "flavor text");
            entry.UsableJob = ReadUsableJob(model.Root, text);
            entry.LinkedCardId = ReadLinkedCardId(model.Root, text);
            entry.Stats = CollectScriptStats(model.Root, text);
            if (string.Equals(entry.Kind, "stackable", StringComparison.Ordinal))
                TrimStackableMetaStats(entry.Stats);
        }

        private static int ReadLinkedCardId(ScriptNode root, string content)
        {
            var node = root?.GetChild("monster card id");
            return TryReadFirstInt(node, content, out var id) ? id : 0;
        }

        private static bool TryReadIcon(ScriptNode root, string content, string tag, out string path, out int frame)
        {
            path = null;
            frame = 0;
            var node = root.GetChild(tag);
            if (node == null || node.DataItems == null || node.DataItems.Count == 0)
                return false;

            var line = JoinDataItems(node, content);
            var match = BacktickPattern.Match(line);
            if (!match.Success)
                return false;

            path = match.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(path))
                return false;

            var rest = line.Substring(match.Index + match.Length);
            var frameMatch = BacktickPattern.Match(rest);
            if (frameMatch.Success)
                int.TryParse(frameMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out frame);
            else
            {
                foreach (var token in rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(token.Trim('`'), NumberStyles.Integer, CultureInfo.InvariantCulture, out frame))
                        break;
                }
            }

            return true;
        }

        // 路径和帧号可能分在多条数据项里。
        private static string JoinDataItems(ScriptNode node, string content)
        {
            if (node?.DataItems == null || node.DataItems.Count == 0)
                return string.Empty;

            var builder = new StringBuilder();
            foreach (var item in node.DataItems)
            {
                var piece = item.GetContent(content);
                if (string.IsNullOrWhiteSpace(piece))
                    continue;
                if (builder.Length > 0)
                    builder.Append(' ');
                builder.Append(piece);
            }

            return builder.ToString();
        }

        private static string ReadScriptText(ScriptNode root, string content, string tag)
        {
            var node = root.GetChild(tag);
            if (node?.DataItems == null || node.DataItems.Count == 0)
                return null;

            var lines = new List<string>();
            foreach (var item in node.DataItems)
            {
                var cleaned = SanitizeScriptText(StripTicks(item.GetContent(content)));
                if (!string.IsNullOrWhiteSpace(cleaned))
                    lines.Add(cleaned);
            }

            return lines.Count == 0 ? null : string.Join("\n", lines);
        }

        private static string ReadUsableJob(ScriptNode root, string content)
        {
            var node = root.GetChild("usable job");
            if (node?.DataItems == null || node.DataItems.Count == 0)
                return null;

            var labels = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in node.DataItems)
            {
                foreach (Match match in BacktickPattern.Matches(item.GetContent(content) ?? string.Empty))
                {
                    var raw = match.Groups[1].Value.Trim().Trim('[', ']');
                    if (string.IsNullOrWhiteSpace(raw) || !seen.Add(raw))
                        continue;
                    labels.Add(JobLabels.TryGetValue(raw, out var label) ? label : raw);
                }
            }

            return labels.Count == 0 ? null : string.Join(" / ", labels);
        }

        private static string StripTicks(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            value = value.Trim();
            if (value.Length >= 2 && value[0] == '`' && value[value.Length - 1] == '`')
                value = value.Substring(1, value.Length - 2);
            return value.Trim();
        }

        private static string RewriteScriptMarkers(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var sockets = ReadEmblemSockets(text);
            text = EmblemSocketPattern.Replace(text, string.Empty);
            text = ScriptMarkerPattern.Replace(text, match =>
            {
                var tag = match.Groups[1].Value.Trim();
                if (JobLabels.TryGetValue(tag, out var job))
                    return job;
                if (SlotLabels.TryGetValue(tag, out var slot))
                    return slot;
                var element = TranslateElement(tag);
                if (!string.Equals(element, tag, StringComparison.OrdinalIgnoreCase) && !IsAsciiIdent(element))
                    return element;
                if (PreviewSkipTags.Contains(tag) || IsAsciiIdent(tag))
                    return string.Empty;
                return match.Value;
            });
            text = text.Replace("`", "");
            if (sockets.Count > 0 && text.IndexOf("徽章孔", StringComparison.Ordinal) < 0)
                text = text.Trim() + (text.Trim().Length > 0 ? " " : "") + "徽章孔 " + string.Join(" / ", sockets);

            while (text.IndexOf("  ", StringComparison.Ordinal) >= 0)
                text = text.Replace("  ", " ");
            return text.Trim();
        }

        private static string SanitizeScriptText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            var text = value.Replace("%%", "\u0001");
            text = NewlineCodePattern.Replace(text, "\n");
            text = FormatPlaceholderPattern.Replace(text, string.Empty);
            text = text.Replace("\u0001", "%");
            text = RewriteScriptMarkers(text);

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var builder = new StringBuilder();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;
                if (builder.Length > 0)
                    builder.Append('\n');
                builder.Append(trimmed);
            }

            return builder.Length == 0 ? null : builder.ToString();
        }
    }
}
