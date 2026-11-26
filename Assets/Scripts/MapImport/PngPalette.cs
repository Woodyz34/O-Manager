using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

// Saves a map picture as a PNG with a palette of at most 256 colours (one byte a pixel), without dithering.
//
// Why (2026-11-26): an RGBA PNG of a big map is far over GitHub's 100 MB limit (Oslo 16084 x 16380: 153 MB), so a new
// import could not be pushed. A map has a few dozen real colours plus their blends along edges; 256 colours keep the
// real ones exact and the edges look the same (like pngquant --nofs: Oslo 153 -> about 44 MB, mean error under 1/255).
//
// How: count every colour, start from the most common colours that differ from each other, move the palette to the
// weighted middle of what it covers a few times (k-means), then let each palette colour be the most common real
// colour it covers, so white stays white and every map colour stays exact. Plain C#, safe on a background thread.
namespace MapImport
{
    public static class PngPalette
    {
        // rgba: rows from the top, 4 bytes a pixel (alpha is ignored: map pictures are opaque)
        public static void Write(string path, byte[] rgba, int width, int height)
        {
            byte[] palette;
            byte[] lookup = BuildPalette(rgba, out palette);
            using (var file = File.Create(path))
                WriteIndexed(file, rgba, lookup, palette, width, height);
        }

        // A table from 24-bit colour to palette index, and the palette (3 bytes a colour)
        static byte[] BuildPalette(byte[] rgba, out byte[] palette)
        {
            var counts = new int[1 << 24];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                int c = (rgba[i] << 16) | (rgba[i + 1] << 8) | rgba[i + 2];
                if (counts[c] < int.MaxValue) counts[c]++;
            }
            var colours = new List<int>();
            for (int c = 0; c < counts.Length; c++) if (counts[c] > 0) colours.Add(c);
            colours.Sort((a, b) => counts[b].CompareTo(counts[a]));   // most common first
            int n = colours.Count;
            var col = colours.ToArray();
            var weight = new long[n];
            for (int i = 0; i < n; i++) weight[i] = counts[col[i]];

            // Starting palette: the most common colours, each at least some distance from those already taken
            var centres = new List<int>();
            var taken = new bool[n];
            for (int minDist = 48 * 48; centres.Count < 256 && minDist >= 1; minDist /= 4)
            {
                for (int i = 0; i < n && centres.Count < 256; i++)
                {
                    if (taken[i]) continue;
                    bool far = true;
                    foreach (int c in centres)
                        if (Dist(col[i], c) < minDist) { far = false; break; }
                    if (!far) continue;
                    centres.Add(col[i]);
                    taken[i] = true;
                }
            }
            int k = centres.Count;
            var cr = new double[k]; var cg = new double[k]; var cb = new double[k];
            for (int j = 0; j < k; j++) { cr[j] = centres[j] >> 16; cg[j] = (centres[j] >> 8) & 255; cb[j] = centres[j] & 255; }

            var nearest = new int[n];
            if (n > k)
            {
                for (int iteration = 0; iteration < 5; iteration++)
                {
                    Assign(col, cr, cg, cb, nearest);
                    var sr = new double[k]; var sg = new double[k]; var sb = new double[k]; var sw = new double[k];
                    for (int i = 0; i < n; i++)
                    {
                        int j = nearest[i], c = col[i]; double w = weight[i];
                        sr[j] += w * (c >> 16); sg[j] += w * ((c >> 8) & 255); sb[j] += w * (c & 255); sw[j] += w;
                    }
                    for (int j = 0; j < k; j++)
                        if (sw[j] > 0) { cr[j] = sr[j] / sw[j]; cg[j] = sg[j] / sw[j]; cb[j] = sb[j] / sw[j]; }
                }
            }
            Assign(col, cr, cg, cb, nearest);

            // Each palette colour becomes the most common real colour it covers (the list is sorted, so the first one met)
            var final = new int[k];
            var set = new bool[k];
            for (int i = 0; i < n; i++)
            {
                int j = nearest[i];
                if (!set[j]) { final[j] = col[i]; set[j] = true; }
            }
            for (int j = 0; j < k; j++)
                if (!set[j]) final[j] = ((int)Math.Round(cr[j]) << 16) | ((int)Math.Round(cg[j]) << 8) | (int)Math.Round(cb[j]);
            for (int j = 0; j < k; j++) { cr[j] = final[j] >> 16; cg[j] = (final[j] >> 8) & 255; cb[j] = final[j] & 255; }
            Assign(col, cr, cg, cb, nearest);

            palette = new byte[k * 3];
            for (int j = 0; j < k; j++) { palette[j * 3] = (byte)(final[j] >> 16); palette[j * 3 + 1] = (byte)(final[j] >> 8); palette[j * 3 + 2] = (byte)final[j]; }
            var lookup = new byte[1 << 24];
            for (int i = 0; i < n; i++) lookup[col[i]] = (byte)nearest[i];
            return lookup;
        }

        static int Dist(int a, int b)
        {
            int dr = (a >> 16) - (b >> 16), dg = ((a >> 8) & 255) - ((b >> 8) & 255), db = (a & 255) - (b & 255);
            return dr * dr + dg * dg + db * db;
        }

        static void Assign(int[] col, double[] cr, double[] cg, double[] cb, int[] nearest)
        {
            int k = cr.Length;
            for (int i = 0; i < col.Length; i++)
            {
                int c = col[i];
                double r = c >> 16, g = (c >> 8) & 255, b = c & 255, best = double.MaxValue;
                int bestJ = 0;
                for (int j = 0; j < k; j++)
                {
                    double dr = r - cr[j], d = dr * dr;
                    if (d >= best) continue;
                    double dg = g - cg[j]; d += dg * dg;
                    if (d >= best) continue;
                    double db = b - cb[j]; d += db * db;
                    if (d < best) { best = d; bestJ = j; }
                }
                nearest[i] = bestJ;
            }
        }

        static void WriteIndexed(Stream output, byte[] rgba, byte[] lookup, byte[] palette, int width, int height)
        {
            output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            var header = new byte[13];
            BigEndian(header, 0, width);
            BigEndian(header, 4, height);
            header[8] = 8;   // bits per index
            header[9] = 3;   // palette
            Chunk(output, "IHDR", header, header.Length);
            Chunk(output, "PLTE", palette, palette.Length);

            using (var compressed = new MemoryStream())
            {
                compressed.WriteByte(0x78);   // zlib header: deflate, 32 KB window
                compressed.WriteByte(0xDA);
                uint a = 1, b = 0;            // Adler-32 of the uncompressed rows
                var row = new byte[width + 1];
                using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, true))
                {
                    for (int y = 0; y < height; y++)
                    {
                        row[0] = 0;   // no filter: indices are not amounts, so differences do not help
                        int i = y * width * 4;
                        for (int x = 0; x < width; x++, i += 4)
                            row[x + 1] = lookup[(rgba[i] << 16) | (rgba[i + 1] << 8) | rgba[i + 2]];
                        for (int j = 0; j <= width; j++)
                        {
                            a = (a + row[j]) % 65521;
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
