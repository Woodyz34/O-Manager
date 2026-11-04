using System;
using System.IO;
using System.IO.Compression;

// Writes and measures PNG files without Unity, so the big map pictures can be saved on a background thread.
namespace MapImport
{
    public static class PngFile
    {
        // rgba: rows from the top, 4 bytes a pixel
        public static void Write(Stream output, byte[] rgba, int width, int height)
        {
            output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            var header = new byte[13];
            BigEndian(header, 0, width);
            BigEndian(header, 4, height);
            header[8] = 8;   // bits per channel
            header[9] = 6;   // RGBA
            Chunk(output, "IHDR", header, header.Length);

            using (var compressed = new MemoryStream())
            {
                compressed.WriteByte(0x78);   // zlib header: deflate, 32 KB window
                compressed.WriteByte(0x01);
                uint a = 1, b = 0;            // Adler-32 of the uncompressed rows
                int stride = width * 4;
                var row = new byte[stride + 1];
                using (var deflate = new DeflateStream(compressed, CompressionLevel.Fastest, true))
                {
                    for (int y = 0; y < height; y++)
                    {
                        // Filter "Sub": each byte minus the same channel of the pixel to its left. Flat map colours become zeros.
                        int start = y * stride;
                        row[0] = 1;
                        for (int i = 0; i < stride; i++)
                        {
                            byte left = i >= 4 ? rgba[start + i - 4] : (byte)0;
                            row[i + 1] = (byte)(rgba[start + i] - left);
                        }
                        for (int i = 0; i <= stride; i++)
                        {
                            a = (a + row[i]) % 65521;
                            b = (b + a) % 65521;
                        }
                        deflate.Write(row, 0, row.Length);
                    }
                }
                uint adler = (b << 16) | a;
                compressed.WriteByte((byte)(adler >> 24));
                compressed.WriteByte((byte)(adler >> 16));
                compressed.WriteByte((byte)(adler >> 8));
                compressed.WriteByte((byte)adler);
                Chunk(output, "IDAT", compressed.GetBuffer(), (int)compressed.Length);
            }
            Chunk(output, "IEND", new byte[0], 0);
        }

        public static void Write(string path, byte[] rgba, int width, int height)
        {
            using (var file = File.Create(path))
            {
                Write(file, rgba, width, height);
            }
        }

        // The size of a PNG from its header, without reading the picture
        public static bool TrySize(byte[] png, out int width, out int height)
        {
            width = height = 0;
            if (png == null || png.Length < 24 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71) return false;
            width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            return width > 0 && height > 0;
        }

        // A smaller copy (rows from the top), each pixel the average of the ones it covers
        public static byte[] Shrink(byte[] rgba, int width, int height, int maxSide, out int newWidth, out int newHeight)
        {
            double f = Math.Min(1.0, maxSide / (double)Math.Max(width, height));
            newWidth = Math.Max(1, (int)Math.Round(width * f));
            newHeight = Math.Max(1, (int)Math.Round(height * f));
            var result = new byte[newWidth * newHeight * 4];
            for (int y = 0; y < newHeight; y++)
            {
                int y0 = (int)((long)y * height / newHeight), y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * height / newHeight));
                for (int x = 0; x < newWidth; x++)
                {
                    int x0 = (int)((long)x * width / newWidth), x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * width / newWidth));
                    long r = 0, g = 0, bl = 0, al = 0, n = 0;
                    for (int yy = y0; yy < y1; yy++)
                    {
                        int i = (yy * width + x0) * 4;
                        for (int xx = x0; xx < x1; xx++, i += 4)
                        {
                            r += rgba[i]; g += rgba[i + 1]; bl += rgba[i + 2]; al += rgba[i + 3]; n++;
                        }
                    }
                    int o = (y * newWidth + x) * 4;
                    result[o] = (byte)(r / n); result[o + 1] = (byte)(g / n); result[o + 2] = (byte)(bl / n); result[o + 3] = (byte)(al / n);
                }
            }
            return result;
        }

        static void BigEndian(byte[] b, int offset, int value)
        {
            b[offset] = (byte)(value >> 24);
            b[offset + 1] = (byte)(value >> 16);
            b[offset + 2] = (byte)(value >> 8);
            b[offset + 3] = (byte)value;
        }

        static uint[] crcTable;

        static void Chunk(Stream s, string type, byte[] data, int length)
        {
            if (crcTable == null)
            {
                var table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                    table[n] = c;
                }
                crcTable = table;
            }
            var head = new byte[8];
            BigEndian(head, 0, length);
            for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
            s.Write(head, 0, 8);
            s.Write(data, 0, length);
            uint crc = 0xFFFFFFFF;
            for (int i = 4; i < 8; i++) crc = crcTable[(crc ^ head[i]) & 0xFF] ^ (crc >> 8);
            for (int i = 0; i < length; i++) crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            crc ^= 0xFFFFFFFF;
            var tail = new byte[4];
            BigEndian(tail, 0, (int)crc);
            s.Write(tail, 0, 4);
        }
    }
}
