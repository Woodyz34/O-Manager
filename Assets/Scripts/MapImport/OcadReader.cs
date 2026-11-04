using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// Reads OCAD map files (.ocd), version 11 and newer (OCAD 11, 12, 2018, 2019, 2020 ...).
// The layout follows OCAD's own file format description: a header, then chains of index blocks that point at
// the symbols, the objects and the parameter strings (colours, georeferencing).
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
            if (version < 11)
            {
                throw new InvalidDataException("The file is saved in OCAD " + version +
                    ". The game reads OCAD 11 and newer. Save it again in a newer OCAD version (File > Export).");
            }
            var map = new MapFile { format = "OCAD", version = version + "." + subVersion };
            int symbolBlock = BitConverter.ToInt32(b, 8);
            int objectBlock = BitConverter.ToInt32(b, 12);
            int stringBlock = BitConverter.ToInt32(b, 32);

            var colorByNumber = new Dictionary<int, int>();
            ReadStrings(b, stringBlock, map, colorByNumber);
            ReadSymbols(b, symbolBlock, version, map, colorByNumber);
            foreach (MapSymbol symbol in map.symbols.Values)
            {
                if (symbol.area != null && symbol.area.borderNumber != 0 &&
                    map.symbols.TryGetValue(symbol.area.borderNumber, out MapSymbol border) && border.line != null)
                {
                    symbol.area.border = border;
                }
            }
            ReadObjects(b, objectBlock, map);
            return map;
        }

        // ---- Parameter strings: colours (type 9) and scale/georeferencing (type 1039) ----

        static void ReadStrings(byte[] b, int block, MapFile map, Dictionary<int, int> colorByNumber)
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
                    string text = DecodeString(b, pos, len);
                    if (type == 9) AddColor(text, map, colorByNumber);
                    else if (type == 1039) ReadScale(text, map);
                }
                block = next;
            }
        }

        static string DecodeString(byte[] b, int pos, int len)
        {
            int end = pos;
            while (end < pos + len && b[end] != 0) end++;
            return Encoding.UTF8.GetString(b, pos, end - pos);
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

        const int SymbolBaseSize = 796;   // the common symbol header in OCAD 11 and newer

        static void ReadSymbols(byte[] b, int block, int version, MapFile map, Dictionary<int, int> colorByNumber)
        {
            int guard = 0;
            while (block > 0 && block + 4 + 256 * 4 <= b.Length && guard++ < 10000)
            {
                int next = BitConverter.ToInt32(b, block);
                for (int k = 0; k < 256; k++)
                {
                    int pos = BitConverter.ToInt32(b, block + 4 + 4 * k);
                    if (pos <= 0 || pos + SymbolBaseSize > b.Length) continue;
                    try
                    {
                        MapSymbol symbol = ReadSymbol(b, pos, map, colorByNumber);
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

        static MapSymbol ReadSymbol(byte[] b, int pos, MapFile map, Dictionary<int, int> colors)
        {
            int size = BitConverter.ToInt32(b, pos);
            int number = BitConverter.ToInt32(b, pos + 4);
            int otp = b[pos + 8];
            int status = b[pos + 11];
            var s = new MapSymbol
            {
                key = number,
                major = number / 1000,
                minor = number % 1000,
                hidden = status == 2
            };
            s.code = s.minor == 0 ? s.major.ToString() : s.major + "." + s.minor;
            s.name = Encoding.Unicode.GetString(b, pos + 56, 128).Split('\0')[0];

            // The colours the symbol uses, as a fallback when the details can't be read
            int nColors = BitConverter.ToInt16(b, pos + 26);
            for (int i = 0; i < Math.Min(nColors, 14); i++)
            {
                int c = Col(colors, BitConverter.ToInt16(b, pos + 28 + 2 * i));
                if (c >= 0 && (s.mainColor < 0 || c > s.mainColor)) s.mainColor = c;
            }

            int d = pos + SymbolBaseSize;
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
                    s.area = ReadArea(b, d, end, colors);
                    s.area.borderNumber = BitConverter.ToInt32(b, d);
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

        static AreaStyle ReadArea(byte[] b, int d, int end, Dictionary<int, int> colors)
        {
            short H(int offset) => BitConverter.ToInt16(b, d + offset);
            var area = new AreaStyle();
            bool fillOn = b[d + 18] != 0;
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
            int structMode = H(20);
            if (structMode > 0)
            {
                area.patternWidth = Mm(H(22));
                area.patternHeight = Mm(H(24));
                area.patternAngle = H(26) / 10.0;
                area.patternShifted = structMode == 2;
                int dataSize = BitConverter.ToUInt16(b, d + 34);
                int start = d + 36;
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

        static void ReadObjects(byte[] b, int block, MapFile map)
        {
            int guard = 0;
            while (block > 0 && block + 4 + 256 * 40 <= b.Length && guard++ < 100000)
            {
                int next = BitConverter.ToInt32(b, block);
                for (int k = 0; k < 256; k++)
                {
                    int o = block + 4 + 40 * k;
                    int pos = BitConverter.ToInt32(b, o + 16);
                    int len = BitConverter.ToInt32(b, o + 20);
                    int sym = BitConverter.ToInt32(b, o + 24);
                    int status = b[o + 30];
                    int viewType = b[o + 31];
                    if (pos <= 0 || status != 1 || viewType != 0 || pos + 56 > b.Length) continue;
                    if (!map.symbols.TryGetValue(sym, out MapSymbol symbol) || symbol.hidden) continue;
                    try
                    {
                        MapObject obj = ReadObject(b, pos, len, symbol);
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

        static MapObject ReadObject(byte[] b, int pos, int len, MapSymbol symbol)
        {
            int otp = b[pos + 4];
            int nItems = BitConverter.ToInt32(b, pos + 44);
            int header = 56;
            if (nItems < 0 || pos + header + nItems * 8 > b.Length) return null;
            var obj = new MapObject { symbol = symbol, angle = BitConverter.ToInt16(b, pos + 6) / 10.0 };
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
                int nText = BitConverter.ToInt16(b, pos + 48);
                int textStart = pos + header + nItems * 8;
                if (nText > 0 && textStart + nText * 8 <= b.Length)
                {
                    obj.text = Encoding.Unicode.GetString(b, textStart, nText * 8).Split('\0')[0];
                }
            }
            return obj.parts.Count > 0 ? obj : null;
        }
    }
}
