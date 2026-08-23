using System;
using System.IO;
using System.IO.Compression;

namespace DfoGmTool.ImagePack
{
    // 把解码后的 RGBA 编成 PNG，给浏览器 <img> 用。
    internal static class PngEncoder
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private static readonly uint[] CrcTable = CreateCrcTable();

        public static byte[] EncodeRgba(int width, int height, byte[] rgba)
        {
            if (width <= 0 || height <= 0 || rgba == null || rgba.Length < width * height * 4)
                throw new ArgumentException("RGBA 尺寸不匹配。");

            using (var output = new MemoryStream(64 + rgba.Length / 2))
            {
                output.Write(Signature, 0, Signature.Length);
                WriteChunk(output, "IHDR", BuildIhdr(width, height));
                WriteChunk(output, "IDAT", CompressScanlines(width, height, rgba));
                WriteChunk(output, "IEND", Array.Empty<byte>());
                return output.ToArray();
            }
        }

        private static byte[] BuildIhdr(int width, int height)
        {
            var ihdr = new byte[13];
            WriteInt32(ihdr, 0, width);
            WriteInt32(ihdr, 4, height);
            ihdr[8] = 8;
            ihdr[9] = 6;
            return ihdr;
        }

        private static byte[] CompressScanlines(int width, int height, byte[] rgba)
        {
            var stride = width * 4;
            var raw = new byte[height * (1 + stride)];
            for (var y = 0; y < height; y++)
            {
                var dest = y * (1 + stride);
                raw[dest] = 0;
                Buffer.BlockCopy(rgba, y * stride, raw, dest + 1, stride);
            }

            using (var compressed = new MemoryStream())
            {
                using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
                    zlib.Write(raw, 0, raw.Length);
                return compressed.ToArray();
            }
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            var typeBytes = new[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
            WriteInt32(output, data.Length);
            output.Write(typeBytes, 0, 4);
            if (data.Length > 0)
                output.Write(data, 0, data.Length);
            WriteInt32(output, (int)Crc(typeBytes, data));
        }

        private static void WriteInt32(Stream output, int value)
        {
            output.WriteByte((byte)(value >> 24));
            output.WriteByte((byte)(value >> 16));
            output.WriteByte((byte)(value >> 8));
            output.WriteByte((byte)value);
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static uint Crc(byte[] type, byte[] data)
        {
            var crc = 0xFFFFFFFF;
            for (var i = 0; i < type.Length; i++)
                crc = CrcTable[(crc ^ type[i]) & 0xFF] ^ (crc >> 8);
            for (var i = 0; i < data.Length; i++)
                crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }

        private static uint[] CreateCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                var crc = i;
                for (var j = 0; j < 8; j++)
                    crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
                table[i] = crc;
            }
            return table;
        }
    }
}
