using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DfoGmTool.ImagePack
{
    // NeoplePack_Bill：264 字节表项（偏移、大小、加密名），按需读 IMG 字节。
    internal sealed class NpkArchive
    {
        private const int MaxFiles = 20000;

        private readonly string _path;
        private readonly Dictionary<string, NpkEntry> _entries;

        private NpkArchive(string path, Dictionary<string, NpkEntry> entries)
        {
            _path = path;
            _entries = entries;
        }

        public static bool TryOpen(string path, out NpkArchive archive)
        {
            archive = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            try
            {
                using (var stream = OpenRead(path))
                {
                    var header = new byte[20];
                    if (stream.Read(header, 0, header.Length) != header.Length)
                        return false;
                    if (!Encoding.ASCII.GetString(header, 0, 15).StartsWith("NeoplePack_Bill", StringComparison.Ordinal))
                        return false;

                    var count = BitConverter.ToInt32(header, 16);
                    if (count <= 0 || count > MaxFiles)
                        return false;

                    var tableSize = checked(count * 264);
                    var table = new byte[tableSize];
                    if (stream.Read(table, 0, table.Length) != table.Length)
                        return false;

                    var entries = new Dictionary<string, NpkEntry>(count, StringComparer.OrdinalIgnoreCase);
                    var fileLength = stream.Length;
                    for (var i = 0; i < count; i++)
                    {
                        var row = i * 264;
                        var offset = BitConverter.ToInt32(table, row);
                        var size = BitConverter.ToInt32(table, row + 4);
                        if (offset < 0 || size <= 0 || offset + (long)size > fileLength)
                            continue;

                        var nameBytes = new byte[256];
                        Buffer.BlockCopy(table, row + 8, nameBytes, 0, 256);
                        var name = NpkNameCipher.NormalizeImgPath(NpkNameCipher.Decrypt(nameBytes));
                        if (string.IsNullOrEmpty(name) || entries.ContainsKey(name))
                            continue;
                        entries[name] = new NpkEntry(offset, size);
                    }

                    if (entries.Count == 0)
                        return false;

                    archive = new NpkArchive(path, entries);
                    return true;
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public IEnumerable<string> EntryNames
        {
            get { return _entries.Keys; }
        }

        public bool TryRead(string imgPath, out byte[] blob)
        {
            blob = null;
            if (!TryResolveEntry(imgPath, out var entry))
                return false;

            try
            {
                using (var stream = OpenRead(_path))
                {
                    stream.Seek(entry.Offset, SeekOrigin.Begin);
                    blob = new byte[entry.Size];
                    var read = 0;
                    while (read < blob.Length)
                    {
                        var n = stream.Read(blob, read, blob.Length - read);
                        if (n <= 0)
                            return false;
                        read += n;
                    }
                    return true;
                }
            }
            catch (IOException)
            {
                blob = null;
                return false;
            }
        }

        // 表项名可能带或不带 .img。
        private bool TryResolveEntry(string imgPath, out NpkEntry entry)
        {
            entry = default(NpkEntry);
            var key = NpkNameCipher.NormalizeImgPath(imgPath);
            if (key == null)
                return false;
            if (_entries.TryGetValue(key, out entry))
                return true;

            if (key.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
            {
                var withoutExt = key.Substring(0, key.Length - 4);
                return _entries.TryGetValue(withoutExt, out entry);
            }

            return _entries.TryGetValue(key + ".img", out entry);
        }

        private static FileStream OpenRead(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }

        private readonly struct NpkEntry
        {
            public NpkEntry(int offset, int size)
            {
                Offset = offset;
                Size = size;
            }

            public int Offset { get; }
            public int Size { get; }
        }
    }
}
