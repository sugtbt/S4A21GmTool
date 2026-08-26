using System;
using System.Text;

namespace DfoGmTool.ServerCore.Infrastructure
{
    /// A21 线上名和协议字符串用 GBK(936)。日志、配置、哈希仍用 UTF-8。
    public static class ClientTextEncoding
    {
        public const int CodePage = 936;

        private static readonly object Sync = new object();
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        private static Encoding _gbk;

        public static void EnsureInitialized()
        {
            if (_gbk != null)
                return;

            lock (Sync)
            {
                if (_gbk != null)
                    return;

                System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _gbk = System.Text.Encoding.GetEncoding(CodePage);
            }
        }

        public static byte[] GetBytes(string value)
        {
            EnsureInitialized();
            return _gbk.GetBytes(value ?? string.Empty);
        }

        public static string GetString(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;
            EnsureInitialized();
            return _gbk.GetString(bytes).TrimEnd('\0');
        }

        /// 读库内线上名（GBK；兼容未转换的旧 UTF-8 字节）。
        public static string ReadStoredName(object value)
        {
            if (value == null || value is DBNull)
                return string.Empty;
            if (value is byte[] bytes)
            {
                if (bytes.Length == 0)
                    return string.Empty;
                if (TryConvertLegacyUtf8WireToGbk(bytes, out var gbk))
                    return GetString(gbk);
                return GetString(bytes);
            }

            return value as string ?? string.Empty;
        }

        /// 旧 UTF-8 线上字节转为 GBK。ASCII 和已是 GBK 的中文不变。
        public static bool TryConvertLegacyUtf8WireToGbk(byte[] stored, out byte[] gbk)
        {
            EnsureInitialized();
            gbk = stored;
            if (stored == null || stored.Length == 0 || IsAscii(stored))
                return false;

            string text;
            try
            {
                text = StrictUtf8.GetString(stored);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            if (text.IndexOf('\uFFFD') >= 0)
                return false;

            var converted = GetBytes(text);
            if (BytesEqual(converted, stored))
                return false;

            gbk = converted;
            return true;
        }

        private static bool IsAscii(byte[] bytes)
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] > 0x7F)
                    return false;
            }

            return true;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }
    }
}
