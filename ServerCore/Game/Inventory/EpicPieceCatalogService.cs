using DfoGmTool.ServerCore.GameWorld;
using DfoGmTool.ServerCore.Infrastructure;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DfoGmTool.ServerCore.Game.Inventory
{
    internal sealed class EpicPieceCatalogEntry
    {
        public int Index { get; set; }
        public int OutputEquipmentId { get; set; }
        public int EpicPieceId { get; set; }
    }

    internal static class EpicPieceCatalogService
    {
        private const string EpicPieceInfoPath = "etc/epicpieceinfo.etc";
        private static readonly object Sync = new object();
        private static Lazy<CatalogData> Catalog = new Lazy<CatalogData>(LoadCatalog);

        internal static int Count => Catalog.Value.Entries.Count;

        internal static IReadOnlyList<EpicPieceCatalogEntry> Entries => Catalog.Value.Entries;

        internal static bool IsEpicPieceId(int itemId)
            => itemId > 0 && Catalog.Value.IndexByPieceId.ContainsKey(itemId);

        internal static bool TryGetIndexByPieceId(int itemId, out int index)
            => Catalog.Value.IndexByPieceId.TryGetValue(itemId, out index);

        internal static bool TryGetEntryByPieceId(int itemId, out EpicPieceCatalogEntry entry)
        {
            entry = null;
            if (!TryGetIndexByPieceId(itemId, out var index))
                return false;

            entry = Catalog.Value.Entries[index];
            return true;
        }

        internal static void ResetForPvfChange()
        {
            lock (Sync)
                Catalog = new Lazy<CatalogData>(LoadCatalog);
        }

        private static CatalogData LoadCatalog()
        {
            try
            {
                return Parse(PvfArchiveAccessor.ReadText(EpicPieceInfoPath));
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[EpicPieceCatalog] load failed: {ex.Message}");
                return CatalogData.Empty;
            }
        }

        private static CatalogData Parse(string text)
        {
            var dropBlock = ExtractTaggedBlock(text, "equipment piece drop info");
            var pieceValues = ParseIntList(ExtractTaggedBlock(dropBlock, "piece list"));
            if (pieceValues.Count == 0 || pieceValues.Count % 2 != 0)
                return CatalogData.Empty;

            var entries = new List<EpicPieceCatalogEntry>();
            var indexByPieceId = new Dictionary<int, int>();
            var entryByOutputId = new Dictionary<int, EpicPieceCatalogEntry>();
            for (var offset = 0; offset + 1 < pieceValues.Count; offset += 2)
            {
                var outputId = pieceValues[offset];
                var pieceId = pieceValues[offset + 1];
                if (outputId <= 0 || pieceId <= 0)
                    continue;
                if (indexByPieceId.ContainsKey(pieceId)
                    || entryByOutputId.ContainsKey(outputId))
                    continue;

                var entry = new EpicPieceCatalogEntry
                {
                    Index = entries.Count,
                    OutputEquipmentId = outputId,
                    EpicPieceId = pieceId,
                };
                entries.Add(entry);
                indexByPieceId[pieceId] = entry.Index;
                entryByOutputId[outputId] = entry;
            }

            return new CatalogData(entries, indexByPieceId);
        }

        private static string ExtractTaggedBlock(string text, string tagName)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(tagName))
                return string.Empty;

            var open = "[" + tagName + "]";
            var close = "[/" + tagName + "]";
            var openIndex = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            if (openIndex < 0)
                return string.Empty;

            var start = openIndex + open.Length;
            var closeIndex = text.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);
            return closeIndex < 0
                ? text.Substring(start)
                : text.Substring(start, closeIndex - start);
        }

        private static List<int> ParseIntList(string text)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            foreach (Match match in Regex.Matches(text, @"-?\d+"))
            {
                if (int.TryParse(match.Value, out var value))
                    result.Add(value);
            }
            return result;
        }

        private sealed class CatalogData
        {
            internal static readonly CatalogData Empty =
                new CatalogData(
                    new List<EpicPieceCatalogEntry>(),
                    new Dictionary<int, int>());

            internal CatalogData(
                List<EpicPieceCatalogEntry> entries,
                Dictionary<int, int> indexByPieceId)
            {
                Entries = entries;
                IndexByPieceId = indexByPieceId;
            }

            internal List<EpicPieceCatalogEntry> Entries { get; }
            internal Dictionary<int, int> IndexByPieceId { get; }
        }
    }
}
