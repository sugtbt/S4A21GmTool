using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace DfoGmTool.ImagePack
{
    // 只读 ImagePacks2。先按 sprite 路径猜 NPK 文件名，对不上再扫一次 sprite_item*.NPK 建表项索引。
    public sealed class ImagePackLibrary
    {
        private const int MaxCachedPng = 2048;
        private const int MaxCachedImg = 256;

        private readonly string _root;
        private readonly ConcurrentDictionary<string, NpkArchive> _archives =
            new ConcurrentDictionary<string, NpkArchive>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte[]> _pngCache =
            new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte[]> _imgCache =
            new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> _missing =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> _missingImg =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _imgToNpk =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _itemIndexGate = new object();
        private volatile bool _itemIndexBuilt;
        private byte[] _windowChromePng;
        private bool _windowChromeResolved;

        private ImagePackLibrary(string root)
        {
            _root = root;
        }

        public string Root => _root;

        public static ImagePackLibrary TryOpen(string configuredPath)
        {
            return TryResolveRoot(configuredPath, out var root)
                ? new ImagePackLibrary(root)
                : null;
        }

        public static bool TryResolveRoot(string configuredPath, out string root)
        {
            root = null;
            if (string.IsNullOrWhiteSpace(configuredPath))
                return false;

            try
            {
                root = Path.GetFullPath(configuredPath.Trim());
            }
            catch (Exception)
            {
                return false;
            }

            if (!Directory.Exists(root))
                return false;

            var nested = Path.Combine(root, "ImagePacks2");
            if (!HasKnownPack(root) && HasKnownPack(nested))
                root = nested;
            return HasKnownPack(root);
        }

        public bool TryRenderWindowChrome(out byte[] png)
        {
            if (_windowChromeResolved)
            {
                png = _windowChromePng;
                return png != null;
            }

            _windowChromeResolved = true;
            png = null;
            var cells = new byte[9][];
            var widths = new int[9];
            var heights = new int[9];
            for (var i = 0; i < 9; i++)
            {
                if (!TryDecode("sprite/interface/windowcommon.img", i, out widths[i], out heights[i], out cells[i]))
                    return false;
            }

            var col0 = Max3(widths[0], widths[3], widths[6]);
            var col1 = Max3(widths[1], widths[4], widths[7]);
            var col2 = Max3(widths[2], widths[5], widths[8]);
            var row0 = Max3(heights[0], heights[1], heights[2]);
            var row1 = Max3(heights[3], heights[4], heights[5]);
            var row2 = Max3(heights[6], heights[7], heights[8]);
            var canvasWidth = col0 + col1 + col2;
            var canvasHeight = row0 + row1 + row2;
            if (canvasWidth <= 0 || canvasHeight <= 0 || canvasWidth > 256 || canvasHeight > 256)
                return false;

            var canvas = new byte[canvasWidth * canvasHeight * 4];
            var xs = new[] { 0, col0, col0 + col1 };
            var ys = new[] { 0, row0, row0 + row1 };
            for (var i = 0; i < 9; i++)
                NpkImageDecoder.Blit(canvas, canvasWidth, canvasHeight, cells[i], widths[i], heights[i], xs[i % 3], ys[i / 3]);

            png = PngEncoder.EncodeRgba(canvasWidth, canvasHeight, canvas);
            _windowChromePng = png;
            return true;
        }

        public bool TryRenderPng(string imgPath, int frame, string markPath, int markFrame, out byte[] png)
        {
            png = null;
            var normalized = NpkNameCipher.NormalizeImgPath(imgPath);
            if (normalized == null)
                return false;

            var markNormalized = NpkNameCipher.NormalizeImgPath(markPath);
            var cacheKey = normalized + "#" + frame + "#" + (markNormalized ?? "") + "#" + markFrame;
            if (_pngCache.TryGetValue(cacheKey, out png))
                return true;
            if (_missing.ContainsKey(cacheKey))
                return false;

            if (!TryDecode(normalized, frame, out var width, out var height, out var rgba))
            {
                _missing[cacheKey] = 0;
                return false;
            }

            if (markNormalized != null
                && TryDecode(markNormalized, markFrame, out var markWidth, out var markHeight, out var markRgba))
            {
                NpkImageDecoder.Blit(rgba, width, height, markRgba, markWidth, markHeight, 0, 0);
            }

            png = PngEncoder.EncodeRgba(width, height, rgba);
            Remember(_pngCache, cacheKey, png, MaxCachedPng);
            return true;
        }

        private bool TryDecode(string imgPath, int frame, out int width, out int height, out byte[] rgba)
        {
            width = 0;
            height = 0;
            rgba = null;
            if (!TryReadImg(imgPath, out var blob))
                return false;
            return NpkImageDecoder.TryDecodeFrame(blob, frame, out width, out height, out rgba);
        }

        private bool TryReadImg(string imgPath, out byte[] blob)
        {
            if (_imgCache.TryGetValue(imgPath, out blob))
                return true;

            blob = null;
            if (_missingImg.ContainsKey(imgPath))
                return false;

            if (_imgToNpk.TryGetValue(imgPath, out var knownNpk)
                && TryReadFromArchive(knownNpk, imgPath, out blob))
            {
                Remember(_imgCache, imgPath, blob, MaxCachedImg);
                return true;
            }

            foreach (var npkPath in EnumerateCandidateNpkFiles(imgPath))
            {
                if (!TryReadFromArchive(npkPath, imgPath, out blob))
                    continue;

                _imgToNpk[imgPath] = npkPath;
                Remember(_imgCache, imgPath, blob, MaxCachedImg);
                return true;
            }

            EnsureItemNameIndex();
            if (_imgToNpk.TryGetValue(imgPath, out var indexedNpk)
                && TryReadFromArchive(indexedNpk, imgPath, out blob))
            {
                Remember(_imgCache, imgPath, blob, MaxCachedImg);
                return true;
            }

            _missingImg[imgPath] = 0;
            return false;
        }

        private void EnsureItemNameIndex()
        {
            if (_itemIndexBuilt)
                return;

            lock (_itemIndexGate)
            {
                if (_itemIndexBuilt)
                    return;

                try
                {
                    foreach (var npkPath in Directory.EnumerateFiles(_root, "sprite_item*.NPK"))
                    {
                        var archive = _archives.GetOrAdd(npkPath, path =>
                        {
                            NpkArchive opened;
                            return NpkArchive.TryOpen(path, out opened) ? opened : null;
                        });
                        if (archive == null)
                            continue;
                        foreach (var name in archive.EntryNames)
                            _imgToNpk.TryAdd(name, npkPath);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                _itemIndexBuilt = true;
            }
        }

        private bool TryReadFromArchive(string npkPath, string imgPath, out byte[] blob)
        {
            blob = null;
            var archive = _archives.GetOrAdd(npkPath, path =>
            {
                NpkArchive opened;
                return NpkArchive.TryOpen(path, out opened) ? opened : null;
            });
            return archive != null && archive.TryRead(imgPath, out blob);
        }

        private IEnumerable<string> EnumerateCandidateNpkFiles(string imgPath)
        {
            var withoutExt = imgPath;
            if (withoutExt.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
                withoutExt = withoutExt.Substring(0, withoutExt.Length - 4);

            var parts = withoutExt.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var take = parts.Length; take >= 1; take--)
            {
                var full = Path.Combine(_root, string.Join("_", parts, 0, take) + ".NPK");
                if (seen.Add(full) && File.Exists(full))
                    yield return full;
            }

            if (parts.Length >= 2 && string.Equals(parts[1], "interface", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var extra in new[] { "sprite_interface.NPK", "sprite_interface2.NPK" })
                {
                    var full = Path.Combine(_root, extra);
                    if (seen.Add(full) && File.Exists(full))
                        yield return full;
                }
            }
        }

        private static void Remember<T>(ConcurrentDictionary<string, T> cache, string key, T value, int maxCount)
        {
            if (cache.Count >= maxCount)
                cache.Clear();
            cache[key] = value;
        }

        private static int Max3(int a, int b, int c)
        {
            return a > b ? (a > c ? a : c) : (b > c ? b : c);
        }

        private static bool HasKnownPack(string directory)
        {
            return Directory.Exists(directory)
                && (File.Exists(Path.Combine(directory, "sprite_item.NPK"))
                    || File.Exists(Path.Combine(directory, "sprite_item_stackable.NPK"))
                    || File.Exists(Path.Combine(directory, "sprite_interface.NPK")));
        }
    }
}
