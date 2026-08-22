using System;
using System.Text;

namespace DfoGmTool.ImagePack
{
    // 表项名 256 字节 XOR：前缀 "puchikon@neople dungeon and fighter "，其后循环填充 "DNF"。
    internal static class NpkNameCipher
    {
        private static readonly byte[] Key = CreateNameKey();

        private static byte[] CreateNameKey()
        {
            var prefix = Encoding.ASCII.GetBytes("puchikon@neople dungeon and fighter ");
            var key = new byte[256];
            Buffer.BlockCopy(prefix, 0, key, 0, prefix.Length);
            var dnf = Encoding.ASCII.GetBytes("DNF");
            for (var i = prefix.Length; i < key.Length; i++)
                key[i] = dnf[(i - prefix.Length) % dnf.Length];
            return key;
        }

        public static string Decrypt(byte[] raw)
        {
            if (raw == null || raw.Length == 0)
                return string.Empty;

            var decoded = new byte[raw.Length];
            var n = raw.Length < Key.Length ? raw.Length : Key.Length;
            for (var i = 0; i < n; i++)
                decoded[i] = (byte)(raw[i] ^ Key[i]);
            for (var i = n; i < raw.Length; i++)
                decoded[i] = raw[i];

            var end = Array.IndexOf(decoded, (byte)0);
            if (end < 0)
                end = decoded.Length;

            var text = Encoding.ASCII.GetString(decoded, 0, end).Replace('\\', '/').Trim();
            var img = text.IndexOf(".img", StringComparison.OrdinalIgnoreCase);
            if (img >= 0)
                return text.Substring(0, img + 4);

            var builder = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (char.IsLetterOrDigit(ch) || ch == '/' || ch == '_' || ch == '-' || ch == '.')
                    builder.Append(ch);
                else
                    break;
            }

            return builder.ToString();
        }

        public static string NormalizeImgPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var normalized = path.Trim().Trim('`').Replace('\\', '/');
            while (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized.Substring(2);
            if (normalized.StartsWith("/", StringComparison.Ordinal))
                normalized = normalized.Substring(1);
            if (!normalized.StartsWith("sprite/", StringComparison.OrdinalIgnoreCase))
                normalized = "sprite/" + normalized;
            return normalized.ToLowerInvariant();
        }
    }
}
