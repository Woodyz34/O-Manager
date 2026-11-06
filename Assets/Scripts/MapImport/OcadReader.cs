using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// Reads OCAD map files (.ocd) from OCAD 6 up to the newest (OCAD 6-10, 11, 12, 2018, 2019, 2020 ...).
// The layout follows OCAD's own file format description (and OpenOrienteering Mapper's notes on the old
// versions): a header, then chains of index blocks that point at the symbols, the objects and the parameter
// strings (colours, georeferencing). The versions differ in how big the symbol and object headers are:
//  - OCAD 6-8: 16-bit symbol numbers (101.0 is stored as 1010), colours and scale in the file header
//  - OCAD 9-11: colours and scale as parameter strings, 32-bit symbol numbers, 40-byte object headers
//  - OCAD 12 and newer: 56-byte object headers and a few more area symbol fields
namespace MapImport
{
    public static class OcadReader
    {
        const int OcadMark = 0x0CAD;

        public static bool IsOcad(byte[] bytes)
        {
            return bytes != null && bytes.Length > 64 && BitConverter.ToInt16(bytes, 0) == OcadMark;
        }

        // Only reads the header: "OCAD 12", "OCAD 2018" ...
        public static string VersionOf(byte[] bytes)
        {
            if (!IsOcad(bytes)) return null;
            int version = BitConverter.ToInt16(bytes, 4);
            return "OCAD " + version;
        }

        public static MapFile Read(byte[] b)
        {
            if (!IsOcad(b))
            {
                throw new InvalidDataException("This is not an OCAD file.");
            }
            int version = BitConverter.ToInt16(b, 4);
            int subVersion = b[6];
            if (version < 6)
            {
                throw new InvalidDataException("The file is saved in OCAD " + version +
                    ". The game reads OCAD 6 and newer. Open it in a newer OCAD and save it again.");
            }
            var map = new MapFile { format = "OCAD", version = version + "." + subVersion };
            var v = new Layout(version);
            int symbolBlock = BitConverter.ToInt32(b, 8);
            int objectBlock = BitConverter.ToInt32(b, 12);

            var colorByNumber = new Dictionary<int, int>();
            if (v.old)
            {
                ReadOldColors(b, map, colorByNumber);
                ReadOldSetup(b, BitConverter.ToInt32(b, 16), map);
            }
            else
            {
                ReadStrings(b, BitConverter.ToInt32(b, 32), v, map, colorByNumber);
            }
            ReadSymbols(b, symbolBlock, v, map, colorByNumber);
            foreach (MapSymbol symbol in map.symbols.Values)
            {
                if (symbol.area != null && symbol.area.borderNumber != 0 &&
                    map.symbols.TryGetValue(symbol.area.borderNumber, out MapSymbol border) && border.line != null)
                {
                    symbol.area.border = border;
                }
            }
            ReadObjects(b, objectBlock, v, map);
            return map;
        }

        // What differs between the OCAD versions
        class Layout
        {
            public readonly int version;
            public readonly bool old;            // OCAD 6-8
            public readonly bool utf8;           // OCAD 11 and newer write text as UTF-8/UTF-16, older as 8-bit
            public readonly int symbolHeader;    // size of the common symbol header
            public readonly int objectHeader;    // size of the object header before the coordinates
            public readonly int areaElements;    // where an area symbol's pattern starts, after the symbol header

            public Layout(int version)
            {
                this.version = version;
                old = version <= 8;
                utf8 = version >= 11;
                symbolHeader = old ? 348 : version <= 10 ? 572 : 796;
                objectHeader = old ? 32 : version <= 11 ? 40 : 56;
                areaElements = version >= 12 ? 36 : 32;
            }

            // OCAD 6-8 number symbols as 1010 for 101.0; the game uses 101000 like the newer versions
            public int SymbolKey(int number) => old ? (number / 10) * 1000 + number % 10 : number;
        }

        static string Latin(byte[] b, int pos, int len)
        {
            var chars = new char[len];
            for (int i = 0; i < len; i++) chars[i] = (char)b[pos + i];
            return new string(chars);
        }

        // A Pascal string: one length byte, then the 8-bit characters
        static string Pascal(byte[] b, int pos, int max)
        {
            int len = Math.Min(b[pos], max);
            return Latin(b, pos + 1, len);
        }

        // ---- OCAD 6-8: colours in the file header, scale in the setup block ----

        static void ReadOldColors(byte[] b, MapFile map, Dictionary<int, int> colorByNumber)
        {
            const int header = 48;           // the symbol header comes right after the file header
            int count = BitConverter.ToUInt16(b, header);
            const int first = header + 24;   // after the colour count, separation count, screens and 4 spare bytes
            for (int i = 0; i < Math.Min(count, 256); i++)
            {
                int o = first + 72 * i;
                if (o + 72 > b.Length) break;
                int number = BitConverter.ToUInt16(b, o);
                // CMYK in half percent (0-200)
                double c = b[o + 4] / 200.0, m = b[o + 5] / 200.0, y = b[o + 6] / 200.0, k = b[o + 7] / 200.0;
                var color = new MapColor
                {
                    name = Pascal(b, o + 8, 31),
                    number = number,
                    r = (byte)Math.Round(255 * (1 - Math.Min(1, c)) * (1 - Math.Min(1, k))),
                    g = (byte)Math.Round(255 * (1 - Math.Min(1, m)) * (1 - Math.Min(1, k))),
                    b = (byte)Math.Round(255 * (1 - Math.Min(1, y)) * (1 - Math.Min(1, k))),
                    priority = map.colors.Count
                };
                if (!colorByNumber.ContainsKey(number)) colorByNumber[number] = map.colors.Count;
                map.colors.Add(color);
            }
        }

        static void ReadOldSetup(byte[] b, int setup, MapFile map)
        {
            map.scale = 10000;
            if (setup <= 0 || setup + 56 > b.Length) return;
            double scale = BitConverter.ToDouble(b, setup + 24);
            if (scale > 100 && scale < 1000000) map.scale = scale;
            map.refEast = BitConverter.ToDouble(b, setup + 32);
            map.refNorth = BitConverter.ToDouble(b, setup + 40);
            map.gridAngle = BitConverter.ToDouble(b, setup + 48);
            map.georeferenced = map.refEast != 0 || map.refNorth != 0;
        }

        // ---- Parameter strings: colours (type 9) and scale/georeferencing (type 1039) ----

        static void ReadStrings(byte[] b, int block, Layout v, MapFile map, Dictionary<int, int> colorByNumber)
        {
            int guard = 0;
            while (block > 0 && block + 4 + 256 * 16 <= b.Length && guard++ < 10000)
            {
                int next = BitConverter.ToInt32(b, block);
                for (int k = 0; k < 256; k++)
                {
                    int o = block + 4 + 16 * k;
                    int pos = BitConverter.ToInt32(b, o);
                    int len = BitConverter.ToInt32(b, o + 4);
                    int type = BitConverter.ToInt32(b, o + 8);
                    if (pos <= 0 || len <= 0 || pos + len > b.Length) continue;
                    string text = DecodeString(b, pos, len, v.utf8);
                    if (type == 9) AddColor(text, map, colorByNumber);
                    else if (type == 1039) ReadScale(text, map);
                }
                block = next;
            }
        }

        static string DecodeString(byte[] b, int pos, int len, bool utf8)
        {
            int end = pos;
            while (end < pos + len && b[end] != 0) end++;
            return utf8 ? Encoding.UTF8.GetString(b, pos, end - pos) : Latin(b, pos, end - pos);
        }

        static Dictionary<char, string> Fields(string text, out string first)
        {
            var fields = new Dictionary<char, string>();
            string[] parts = text.Split('\t');
            first = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i].Length > 0 && !fields.ContainsKey(parts[i][0]))
                {
                    fields[parts[i][0]] = parts[i].Substring(1);
                }
            }
            return fields;
        }

        static double Num(Dictionary<char, string> f, char key, double fallback = 0)
        {
            if (f.TryGetValue(key, out string s) &&
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                return v;
            }
            return fallback;
        }

        static void AddColor(string text, MapFile map, Dictionary<int, int> colorByNumber)
        {
            var f = Fields(text, out string name);
            int number = (int)Num(f, 'n', -1);
            double c = Num(f, 'c') / 100, m = Num(f, 'm') / 100, y = Num(f, 'y') / 100, k = Num(f, 'k') / 100;
            var color = new MapColor
            {
                name = name,
                number = number,
                r = (byte)Math.Round(255 * (1 - Math.Min(1, c)) * (1 - Math.Min(1, k))),
                g = (byte)Math.Round(255 * (1 - Math.Min(1, m)) * (1 - Math.Min(1, k))),
                b = (byte)Math.Round(255 * (1 - Math.Min(1, y)) * (1 - Math.Min(1, k))),
                priority = map.colors.Count
            };
            if (number >= 0 && !colorByNumber.ContainsKey(number))
            {
                colorByNumber[number] = map.colors.Count;
            }
            map.colors.Add(color);
        }

        static void ReadScale(string text, MapFile map)
        {
            var f = Fields(text, out _);
            map.scale = Num(f, 'm', 10000);
            map.refEast = Num(f, 'x');
            map.refNorth = Num(f, 'y');
            map.gridAngle = Num(f, 'a');
            map.crsCode = (int)Num(f, 'i');
            map.georeferenced = Num(f, 'r') > 0 && (map.refEast != 0 || map.refNorth != 0);
        }

        // ---- Symbols ----

        static void ReadSymbols(byte[] b, int block, Layout v, MapFile map, Dictionary<int, int> colorByNumber)
        {
            int guard = 0;
            while (block > 0 && block + 4 + 256 * 4 <= b.Length && guard++ < 10000)
            {
                int next = BitConverter.ToInt32(b, block);
                for (int k = 0; k < 256; k++)
                {
                    int pos = BitConverter.ToInt32(b, block + 4 + 4 * k);
                    if (pos <= 0 || pos + v.symbolHeader > b.Length) continue;
                    try
                    {
                        MapSymbol symbol = ReadSymbol(b, pos, v, colorByNumber);
                        if (symbol != null) map.symbols[symbol.key] = symbol;
                    }
                    catch (Exception e)
                    {
                        map.warnings.Add("Symbol at " + pos + " could not be read: " + e.Message);
                    }
                }
                block = next;
            }
        }

        static int Col(Dictionary<int, int> colorByNumber, int number)
        {
            return colorByNumber.TryGetValue(number, out int index) ? index : -1;
        }

        static MapSymbol ReadSymbol(byte[] b, int pos, Layout v, Dictionary<int, int> colors)
        {
            int size, number, otp;
            if (v.old)
            {
                size = BitConverter.ToUInt16(b, pos);
                number = v.SymbolKey(BitConverter.ToUInt16(b, pos + 2));
                otp = BitConverter.ToUInt16(b, pos + 4);
                // OCAD 6-8 types: 1 point, 2 line (or line text), 3 area, 4 text, 5 rectangle
                if (otp == 2 && b[pos + 6] == 1) otp = 6;
                else if (otp == 5) otp = 7;
            }
            else
            {
                size = BitConverter.ToInt32(b, pos);
                number = BitConverter.ToInt32(b, pos + 4);
                otp = b[pos + 8];
            }
            int status = b[pos + 11];
            var s = new MapSymbol
            {
                key = number,
                major = number / 1000,
                minor = number % 1000,
                hidden = status == 2
            };
            s.code = s.minor == 0 ? s.major.ToString() : s.major + "." + s.minor;
            if (v.old) s.name = Pascal(b, pos + 52, 31);
            else if (!v.utf8) s.name = Pascal(b, pos + 56, 31);
            else s.name = Encoding.Unicode.GetString(b, pos + 56, 128).Split('\0')[0];

            // The colours the symbol uses, as a fallback when the details can't be read
            if (v.old)
            {
                // a bit set for each of the 256 colours the symbol uses
                for (int bit = 0; bit < 256; bit++)
                {
                    if ((b[pos + 20 + bit / 8] & (1 << (bit % 8))) == 0) continue;
                    int c = Col(colors, bit);
                    if (c >= 0 && (s.mainColor < 0 || c > s.mainColor)) s.mainColor = c;
                }
            }
            else
            {
                int nColors = BitConverter.ToInt16(b, pos + 26);
                for (int i = 0; i < Math.Min(nColors, 14); i++)
                {
                    int c = Col(colors, BitConverter.ToInt16(b, pos + 28 + 2 * i));
                    if (c >= 0 && (s.mainColor < 0 || c > s.mainColor)) s.mainColor = c;
                }
            }

            int d = pos + v.symbolHeader;
            int end = pos + size;
            switch (otp)
            {
                case 1:
                    s.kind = SymbolKind.Point;
                    int dataSize = BitConverter.ToUInt16(b, d);
                    s.point = ReadElements(b, d + 4, d + 4 + dataSize * 8, colors);
                    break;
                case 2:
                    s.kind = SymbolKind.Line;
                    s.line = ReadLine(b, d, end, colors);
                    break;
                case 3:
                    s.kind = SymbolKind.Area;
                    s.area = ReadArea(b, d, end, v, colors);
                    // OCAD 6-8 have no border symbol; their two first fields are a spare word and "fill on"
                    s.area.borderNumber = v.old ? 0 : BitConverter.ToInt32(b, d);
                    break;
                case 4:
                case 6:
                    s.kind = SymbolKind.Text;
                    break;
                case 7:
                    s.kind = SymbolKind.Line;   // rectangle symbols: drawn as their outline
                    s.line = new LineStyle { color = s.mainColor, width = 0.1 };
                    break;
                default:
                    s.kind = SymbolKind.Other;
                    break;
            }
            return s;
        }

        static double Mm(int hundredths) => hundredths / 100.0;

        static LineStyle ReadLine(byte[] b, int d, int end, Dictionary<int, int> colors)
        {
            short H(int i) => BitConverter.ToInt16(b, d + 2 * i);
            var line = new LineStyle
            {
                color = Col(colors, H(0)),
                width = Mm(H(1)),
                roundCaps = H(2) == 1,
                mainLength = Mm(H(5)),
                endLength = Mm(H(6)),
                mainGap = Mm(H(7))
            };
            int nPrim = H(11);
            line.midCount = Math.Max(1, (int)nPrim);
            line.midDistance = Mm(H(12));
            int dblMode = H(13);
            int dblFlags = H(14);
            if (dblMode != 0)
            {
                if ((dblFlags & 1) != 0) line.fillColor = Col(colors, H(15));
                line.leftColor = Col(colors, H(16));
                line.rightColor = Col(colors, H(17));
                line.doubleWidth = Mm(H(18));
                line.leftWidth = Mm(H(19));
                line.rightWidth = Mm(H(20));
            }
            if (line.width <= 0) line.color = -1;
            if (line.leftWidth <= 0) line.leftColor = -1;
            if (line.rightWidth <= 0) line.rightColor = -1;
            if (line.doubleWidth <= 0) line.fillColor = -1;

            int primSize = H(32);   // in coordinate pairs (8 bytes)
            int elements = d + 76;
            if (nPrim > 0 && primSize > 0 && elements + primSize * 8 <= end)
            {
                line.midElements = ReadElements(b, elements, elements + primSize * 8, colors);
            }
            return line;
        }

        static AreaStyle ReadArea(byte[] b, int d, int end, Layout v, Dictionary<int, int> colors)
        {
            short H(int offset) => BitConverter.ToInt16(b, d + offset);
            var area = new AreaStyle();
            bool fillOn = v.old ? H(2) != 0 : b[d + 18] != 0;
            if (fillOn) area.fillColor = Col(colors, H(4));
            int hatchMode = H(6);
            if (hatchMode > 0)
            {
                area.hatchColor = Col(colors, H(8));
                area.hatchWidth = Mm(H(10));
                area.hatchDistance = Mm(H(12));
                area.hatchAngle1 = H(14) / 10.0;
                area.hatchAngle2 = H(16) / 10.0;
                area.hatchCount = hatchMode == 2 ? 2 : 1;
            }
            int structMode = b[d + 20];   // one byte; OCAD 12 uses the next byte for something else
            if (structMode > 0)
            {
                area.patternWidth = Mm(H(22));
                area.patternHeight = Mm(H(24));
                area.patternAngle = H(26) / 10.0;
                area.patternShifted = structMode == 2;
                int start = d + v.areaElements;
                int dataSize = BitConverter.ToUInt16(b, start - 2);
                if (start + dataSize * 8 <= end)
                {
                    area.pattern = ReadElements(b, start, start + dataSize * 8, colors);
                }
            }
            return area;
        }

        static List<SymbolElement> ReadElements(byte[] b, int start, int end, Dictionary<int, int> colors)
        {
            var list = new List<SymbolElement>();
            int p = start;
            while (p + 16 <= end && p + 16 <= b.Length)
            {
                int type = BitConverter.ToInt16(b, p);
                int color = BitConverter.ToInt16(b, p + 4);
                int lineWidth = BitConverter.ToInt16(b, p + 6);
                int diameter = BitConverter.ToInt16(b, p + 8);
                int nPoly = BitConverter.ToInt16(b, p + 10);
                if (type < 1 || type > 4 || nPoly < 0 || p + 16 + nPoly * 8 > b.Length) break;
                var e = new SymbolElement
                {
                    type = (SymbolElement.ElementType)type,
                    color = Col(colors, color),
                    lineWidth = Mm(lineWidth),
                    diameter = Mm(diameter)
                };
                var pts = new List<double>();
                ReadCoords(b, p + 16, nPoly, pts, null);
                e.x = new double[pts.Count / 2];
                e.y = new double[pts.Count / 2];
                for (int i = 0; i < e.x.Length; i++) { e.x[i] = pts[2 * i]; e.y[i] = pts[2 * i + 1]; }
                list.Add(e);
                p += 16 + nPoly * 8;
            }
            return list;
        }

        // Reads OCAD coordinates (0.01 mm, the low 8 bits are flags) into mm, flattening curves.
        // With parts given, a new part starts at every "first point of a hole".
        static void ReadCoords(byte[] b, int start, int count, List<double> pts, List<double[]> parts)
        {
            int i = 0;
            while (i < count)
            {
                int xi = BitConverter.ToInt32(b, start + 8 * i);
                int yi = BitConverter.ToInt32(b, start + 8 * i + 4);
                double x = (xi >> 8) / 100.0, y = (yi >> 8) / 100.0;
                bool holeStart = (yi & 2) != 0;
                bool curve = (xi & 1) != 0;
                if (holeStart && parts != null && pts.Count >= 4)
                {
                    parts.Add(pts.ToArray());
                    pts.Clear();
                }
                if (curve && i + 2 < count && pts.Count >= 2)
                {
                    double x2 = (BitConverter.ToInt32(b, start + 8 * (i + 1)) >> 8) / 100.0;
                    double y2 = (BitConverter.ToInt32(b, start + 8 * (i + 1) + 4) >> 8) / 100.0;
                    double x3 = (BitConverter.ToInt32(b, start + 8 * (i + 2)) >> 8) / 100.0;
                    double y3 = (BitConverter.ToInt32(b, start + 8 * (i + 2) + 4) >> 8) / 100.0;
                    MapGeometry.AddBezier(pts, pts[pts.Count - 2], pts[pts.Count - 1], x, y, x2, y2, x3, y3);
                    i += 3;
                    continue;
                }
                pts.Add(x);
                pts.Add(y);
                i++;
            }
        }

        // ---- Objects ----

        static void ReadObjects(byte[] b, int block, Layout v, MapFile map)
        {
            int entrySize = v.old ? 24 : 40;
            int guard = 0;
            while (block > 0 && block + 4 + 256 * entrySize <= b.Length && guard++ < 100000)
            {
                int next = BitConverter.ToInt32(b, block);
                for (int k = 0; k < 256; k++)
                {
                    int o = block + 4 + entrySize * k;
                    int pos = BitConverter.ToInt32(b, o + 16);
                    int sym;
                    if (v.old)
                    {
                        sym = BitConverter.ToInt16(b, o + 22);
                        if (sym <= 0) continue;   // deleted
                        sym = v.SymbolKey(sym);
                    }
                    else
                    {
                        sym = BitConverter.ToInt32(b, o + 24);
                        int status = b[o + 30];
                        int viewType = b[o + 31];
                        if (status != 1 || viewType != 0) continue;
                    }
                    if (pos <= 0 || pos + v.objectHeader > b.Length) continue;
                    if (!map.symbols.TryGetValue(sym, out MapSymbol symbol) || symbol.hidden) continue;
                    try
                    {
                        MapObject obj = ReadObject(b, pos, v, symbol);
                        if (obj != null) map.objects.Add(obj);
                    }
                    catch (Exception e)
                    {
                        map.warnings.Add("Object at " + pos + " could not be read: " + e.Message);
                    }
                }
                block = next;
            }
        }

        static MapObject ReadObject(byte[] b, int pos, Layout v, MapSymbol symbol)
        {
            int otp, nItems, nText, angle;
            bool unicodeText = true;
            if (v.old)
            {
                otp = b[pos + 2];
                unicodeText = b[pos + 3] != 0;
                nItems = BitConverter.ToUInt16(b, pos + 4);
                nText = BitConverter.ToUInt16(b, pos + 6);
                angle = BitConverter.ToInt16(b, pos + 8);
                if (otp == 5) otp = 7;   // rectangle in OCAD 6-8
            }
            else if (v.version <= 11)
            {
                otp = b[pos + 4];
                angle = BitConverter.ToInt16(b, pos + 6);
                nItems = BitConverter.ToInt32(b, pos + 8);
                nText = BitConverter.ToUInt16(b, pos + 12);
            }
            else
            {
                otp = b[pos + 4];
                angle = BitConverter.ToInt16(b, pos + 6);
                nItems = BitConverter.ToInt32(b, pos + 44);
                nText = BitConverter.ToUInt16(b, pos + 48);
            }
            int header = v.objectHeader;
            if (nItems < 0 || pos + header + nItems * 8 > b.Length) return null;
            var obj = new MapObject { symbol = symbol, angle = angle / 10.0 };
            switch (otp)
            {
                case 1: obj.kind = SymbolKind.Point; break;
                case 2: obj.kind = SymbolKind.Line; break;
                case 3: obj.kind = SymbolKind.Area; break;
                case 4: case 5: case 6: obj.kind = SymbolKind.Text; break;
                case 7: obj.kind = SymbolKind.Line; break;
                default: return null;
            }
            var pts = new List<double>();
            ReadCoords(b, pos + header, nItems, pts, obj.kind == SymbolKind.Area ? obj.parts : null);
            if (pts.Count >= 2) obj.parts.Add(pts.ToArray());
            if (obj.kind == SymbolKind.Text)
            {
                int textStart = pos + header + nItems * 8;
                if (nText > 0 && textStart + nText * 8 <= b.Length)
                {
                    obj.text = (unicodeText ? Encoding.Unicode.GetString(b, textStart, nText * 8)
                                            : Latin(b, textStart, nText * 8)).Split('\0')[0];
                }
            }
            return obj.parts.Count > 0 ? obj : null;
        }
    }
}
