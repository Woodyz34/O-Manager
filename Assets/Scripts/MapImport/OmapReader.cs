using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

// Reads OpenOrienteering Mapper maps (.omap / .xmap), the XML format. Coordinates in the file are in
// 1/1000 mm with y pointing down; they are turned into mm with y up like the OCAD reader.
namespace MapImport
{
    public static class OmapReader
    {
        public static bool IsOmap(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 64) return false;
            string head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 400));
            return head.Contains("<map") && head.Contains("openorienteering");
        }

        public static string VersionOf(byte[] bytes)
        {
            string head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 400));
            int i = head.IndexOf("version=\"", head.IndexOf("<map", StringComparison.Ordinal), StringComparison.Ordinal);
            if (i < 0) return "OpenOrienteering Mapper";
            int j = head.IndexOf('"', i + 9);
            return "OpenOrienteering Mapper (format " + head.Substring(i + 9, j - i - 9) + ")";
        }

        public static MapFile Read(byte[] bytes)
        {
            if (!IsOmap(bytes))
            {
                throw new InvalidDataException("This is not an OpenOrienteering Mapper file in XML format. " +
                    "Save it as .omap (XML) in Mapper.");
            }
            var doc = new XmlDocument();
            using (var stream = new MemoryStream(bytes))
            {
                doc.Load(stream);
            }
            XmlElement root = doc.DocumentElement;
            // Newer files wrap everything after the colours in <barrier>
            XmlElement body = Child(root, "barrier") ?? root;
            var map = new MapFile { format = "OpenOrienteering Mapper", version = root.GetAttribute("version") };

            var georef = Child(root, "georeferencing");
            if (georef != null)
            {
                map.scale = Num(georef, "scale", 10000);
                map.gridAngle = Num(georef, "grivation", 0);
                var projected = Child(georef, "projected_crs");
                var refPoint = projected != null ? Child(projected, "ref_point") : null;
                if (refPoint != null)
                {
                    map.refEast = Num(refPoint, "x", 0);
                    map.refNorth = Num(refPoint, "y", 0);
                    map.georeferenced = map.refEast != 0 || map.refNorth != 0;
                }
                var parameter = projected != null ? Child(projected, "parameter") : null;
                if (parameter != null) int.TryParse(parameter.InnerText, out map.crsCode);
            }

            var colors = Child(root, "colors") ?? Child(body, "colors");
            if (colors != null)
            {
                foreach (XmlElement c in Children(colors, "color"))
                {
                    var color = new MapColor { name = c.GetAttribute("name"), number = (int)Num(c, "priority", map.colors.Count) };
                    var rgb = Child(c, "rgb");
                    if (rgb != null)
                    {
                        color.r = (byte)Math.Round(255 * Num(rgb, "r", 0));
                        color.g = (byte)Math.Round(255 * Num(rgb, "g", 0));
                        color.b = (byte)Math.Round(255 * Num(rgb, "b", 0));
                    }
                    else
                    {
                        double cc = Num(c, "c", 0), m = Num(c, "m", 0), y = Num(c, "y", 0), k = Num(c, "k", 0);
                        color.r = (byte)Math.Round(255 * (1 - cc) * (1 - k));
                        color.g = (byte)Math.Round(255 * (1 - m) * (1 - k));
                        color.b = (byte)Math.Round(255 * (1 - y) * (1 - k));
                    }
                    color.priority = map.colors.Count;
                    var spots = Child(c, "spotcolors");
                    if (spots != null)
                    {
                        if (Child(spots, "namedcolor") != null) color.spot = color.number;   // an ink of its own
                        double best = 0;
                        int first = -1;
                        foreach (XmlElement part in Children(spots, "component"))
                        {
                            int ink = (int)Num(part, "spotcolor", -1);
                            if (first < 0) first = ink;
                            double factor = Num(part, "factor", 1);
                            if (factor > best) { best = factor; color.spot = ink; }
                        }
                        if (first >= 0 && best <= 0)
                        {
                            if (spots.GetAttribute("knockout") == "true") color.maskSpot = first;
                            else color.invisible = true;
                        }
                    }
                    map.colors.Add(color);
                }
            }

            var symbols = Child(body, "symbols") ?? Child(root, "symbols");
            if (symbols != null)
            {
                foreach (XmlElement s in Children(symbols, "symbol"))
                {
                    MapSymbol symbol = ReadSymbol(s, map);
                    if (s.HasAttribute("id")) map.symbols[(int)Num(s, "id", -1)] = symbol;
                }
                // Combined symbols point at other symbols by id
                foreach (XmlElement s in Children(symbols, "symbol"))
                {
                    if (!s.HasAttribute("id")) continue;
                    MapSymbol symbol = map.symbols[(int)Num(s, "id", -1)];
                    ResolveCombined(s, symbol, map);
                }
            }

            var parts = Child(body, "parts") ?? Child(root, "parts");
            if (parts != null)
            {
                foreach (XmlElement part in Children(parts, "part"))
                {
                    var objects = Child(part, "objects");
                    if (objects == null) continue;
                    foreach (XmlElement o in Children(objects, "object"))
                    {
                        MapObject obj = ReadObject(o, map);
                        if (obj != null) map.objects.Add(obj);
                    }
                }
            }
            return map;
        }

        // ---- XML helpers ----

        static XmlElement Child(XmlElement e, string name)
        {
            foreach (XmlNode n in e.ChildNodes)
            {
                if (n is XmlElement c && c.LocalName == name) return c;
            }
            return null;
        }

        static IEnumerable<XmlElement> Children(XmlElement e, string name)
        {
            foreach (XmlNode n in e.ChildNodes)
            {
                if (n is XmlElement c && c.LocalName == name) yield return c;
            }
        }

        static double Num(XmlElement e, string attribute, double fallback)
        {
            string s = e.GetAttribute(attribute);
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
        }

        static bool Bool(XmlElement e, string attribute) => e.GetAttribute(attribute) == "true";

        static int ColorIndex(XmlElement e, string attribute, MapFile map)
        {
            int c = (int)Num(e, attribute, -1);
            return c >= 0 && c < map.colors.Count ? c : -1;
        }

        // ---- Symbols ----

        static MapSymbol ReadSymbol(XmlElement s, MapFile map)
        {
            var symbol = new MapSymbol
            {
                key = (int)Num(s, "id", -1),
                code = s.GetAttribute("code"),
                name = s.GetAttribute("name").Trim(),
                hidden = Bool(s, "is_hidden")
            };
            ParseCode(symbol);
            int type = (int)Num(s, "type", 0);
            switch (type)
            {
                case 1:
                    symbol.kind = SymbolKind.Point;
                    var ps = Child(s, "point_symbol");
                    if (ps != null) symbol.point = ReadPointElements(ps, map);
                    break;
                case 2:
                    symbol.kind = SymbolKind.Line;
                    var ls = Child(s, "line_symbol");
                    if (ls != null) symbol.line = ReadLine(ls, map);
                    break;
                case 4:
                    symbol.kind = SymbolKind.Area;
                    var area = Child(s, "area_symbol");
                    if (area != null) symbol.area = ReadArea(area, map);
                    break;
                case 8:
                    symbol.kind = SymbolKind.Text;
                    break;
                case 16:
                    symbol.kind = SymbolKind.Other;   // decided when the parts are known
                    break;
            }
            symbol.mainColor = MainColor(symbol);
            return symbol;
        }

        static void ParseCode(MapSymbol symbol)
        {
            string code = symbol.code ?? "";
            int dot = code.IndexOf('.');
            string a = dot >= 0 ? code.Substring(0, dot) : code;
            string b = dot >= 0 ? code.Substring(dot + 1) : "";
            int.TryParse(a, out symbol.major);
            int minor;
            if (int.TryParse(b, out minor)) symbol.minor = minor;
        }

        static int MainColor(MapSymbol s)
        {
            if (s.area != null)
            {
                if (s.area.fillColor >= 0) return s.area.fillColor;
                if (s.area.hatchColor >= 0) return s.area.hatchColor;
            }
            if (s.line != null)
            {
                if (s.line.color >= 0) return s.line.color;
                if (s.line.fillColor >= 0) return s.line.fillColor;
                if (s.line.leftColor >= 0) return s.line.leftColor;
            }
            if (s.point != null)
            {
                foreach (SymbolElement e in s.point) if (e.color >= 0) return e.color;
            }
            return -1;
        }

        static void ResolveCombined(XmlElement s, MapSymbol symbol, MapFile map)
        {
            var combined = Child(s, "combined_symbol");
            if (combined == null) return;
            symbol.parts = new List<MapSymbol>();
            foreach (XmlElement part in Children(combined, "part"))
            {
                MapSymbol p = null;
                if (Bool(part, "private"))
                {
                    var inner = Child(part, "symbol");
                    if (inner != null) p = ReadSymbol(inner, map);
                }
                else if (map.symbols.TryGetValue((int)Num(part, "symbol", -1), out MapSymbol shared))
                {
                    p = shared;
                }
                if (p != null) symbol.parts.Add(p);
            }
            // A combined symbol behaves like an area if any part is an area, otherwise like a line
            symbol.kind = SymbolKind.Line;
            foreach (MapSymbol p in symbol.parts)
            {
                if (p.kind == SymbolKind.Area) symbol.kind = SymbolKind.Area;
            }
            foreach (MapSymbol p in symbol.parts)
            {
                if (symbol.kind == SymbolKind.Area && p.area != null && symbol.area == null) symbol.area = p.area;
                if (p.line != null && symbol.line == null) symbol.line = p.line;
            }
            if (symbol.kind == SymbolKind.Area && symbol.area != null)
            {
                foreach (MapSymbol p in symbol.parts)
                {
                    if (p.line != null) { symbol.area.border = p; break; }
                }
            }
            symbol.mainColor = MainColor(symbol);
        }

        static LineStyle ReadLine(XmlElement l, MapFile map)
        {
            var line = new LineStyle
            {
                color = ColorIndex(l, "color", map),
                width = Num(l, "line_width", 0) / 1000.0,
                roundCaps = Num(l, "cap_style", 0) == 1
            };
            if (line.width <= 0) line.color = -1;
            if (Bool(l, "dashed"))
            {
                line.mainLength = Num(l, "dash_length", 4000) / 1000.0;
                line.mainGap = Num(l, "break_length", 1000) / 1000.0;
            }
            var mid = Child(l, "mid_symbol");
            if (mid != null)
            {
                var ms = Child(mid, "symbol");
                var mps = ms != null ? Child(ms, "point_symbol") : null;
                if (mps != null)
                {
                    line.midElements = ReadPointElements(mps, map);
                    line.midCount = Math.Max(1, (int)Num(l, "mid_symbols_per_spot", 1));
                    line.midDistance = Num(l, "mid_symbol_distance", 0) / 1000.0;
                    line.midSpacing = Num(l, "segment_length", 4000) / 1000.0;
                }
            }
            var borders = Child(l, "borders");
            if (borders != null)
            {
                // A double line: the main line is the fill, the borders sit at +-shift
                int i = 0;
                foreach (XmlElement border in Children(borders, "border"))
                {
                    int color = ColorIndex(border, "color", map);
                    double width = Num(border, "width", 0) / 1000.0;
                    double shift = Num(border, "shift", 0) / 1000.0;
                    if (i == 0) { line.leftColor = width > 0 ? color : -1; line.leftWidth = width; line.doubleWidth = 2 * shift - width; }
                    else { line.rightColor = width > 0 ? color : -1; line.rightWidth = width; }
                    i++;
                }
                if (i == 1) { line.rightColor = line.leftColor; line.rightWidth = line.leftWidth; }
                if (line.doubleWidth < 0) line.doubleWidth = 0;
            }
            return line;
        }

        static AreaStyle ReadArea(XmlElement a, MapFile map)
        {
            var area = new AreaStyle { fillColor = ColorIndex(a, "inner_color", map) };
            foreach (XmlElement pattern in Children(a, "pattern"))
            {
                int type = (int)Num(pattern, "type", 0);
                double angle = Num(pattern, "angle", 0) * 180.0 / Math.PI;
                if (type == 1)
                {
                    int color = ColorIndex(pattern, "color", map);
                    if (area.hatchCount == 0)
                    {
                        area.hatchColor = color;
                        area.hatchWidth = Num(pattern, "line_width", 0) / 1000.0;
                        area.hatchDistance = Num(pattern, "line_spacing", 1000) / 1000.0;
                        area.hatchAngle1 = angle;
                        area.hatchCount = 1;
                    }
                    else if (area.hatchCount == 1)
                    {
                        area.hatchAngle2 = angle;
                        area.hatchCount = 2;
                    }
                }
                else if (type == 2 && area.pattern == null)
                {
                    var s = Child(pattern, "symbol");
                    var ps = s != null ? Child(s, "point_symbol") : null;
                    if (ps != null)
                    {
                        area.pattern = ReadPointElements(ps, map);
                        area.patternWidth = Num(pattern, "point_distance", 1000) / 1000.0;
                        area.patternHeight = Num(pattern, "line_spacing", 1000) / 1000.0;
                        area.patternAngle = angle;
                        area.patternShifted = false;
                    }
                }
            }
            return area;
        }

        static List<SymbolElement> ReadPointElements(XmlElement ps, MapFile map)
        {
            var list = new List<SymbolElement>();
            double inner = Num(ps, "inner_radius", 0) / 1000.0;
            int innerColor = ColorIndex(ps, "inner_color", map);
            double outerWidth = Num(ps, "outer_width", 0) / 1000.0;
            int outerColor = ColorIndex(ps, "outer_color", map);
            if (inner > 0 && innerColor >= 0)
            {
                list.Add(new SymbolElement { type = SymbolElement.ElementType.Dot, color = innerColor, diameter = 2 * inner, x = new[] { 0.0 }, y = new[] { 0.0 } });
            }
            if (outerWidth > 0 && outerColor >= 0)
            {
                list.Add(new SymbolElement { type = SymbolElement.ElementType.Circle, color = outerColor, lineWidth = outerWidth, diameter = 2 * inner + outerWidth, x = new[] { 0.0 }, y = new[] { 0.0 } });
            }
            foreach (XmlElement element in Children(ps, "element"))
            {
                var s = Child(element, "symbol");
                var o = Child(element, "object");
                if (s == null || o == null) continue;
                var pts = new List<double>();
                var parts = new List<double[]>();
                ReadCoords(o, pts, parts);
                if (pts.Count >= 2) parts.Add(pts.ToArray());
                if (parts.Count == 0) continue;
                int type = (int)Num(s, "type", 0);
                double[] p = parts[0];
                var e = new SymbolElement { x = new double[p.Length / 2], y = new double[p.Length / 2] };
                for (int i = 0; i < e.x.Length; i++) { e.x[i] = p[2 * i]; e.y[i] = p[2 * i + 1]; }
                if (type == 1)
                {
                    var inner2 = Child(s, "point_symbol");
                    if (inner2 == null) continue;
                    foreach (SymbolElement sub in ReadPointElements(inner2, map))
                    {
                        for (int i = 0; i < sub.x.Length; i++) { sub.x[i] += e.x[0]; sub.y[i] += e.y[0]; }
                        list.Add(sub);
                    }
                    continue;
                }
                if (type == 2)
                {
                    var ls = Child(s, "line_symbol");
                    if (ls == null) continue;
                    e.type = SymbolElement.ElementType.Line;
                    e.color = ColorIndex(ls, "color", map);
                    e.lineWidth = Num(ls, "line_width", 0) / 1000.0;
                }
                else if (type == 4)
                {
                    var asym = Child(s, "area_symbol");
                    if (asym == null) continue;
                    e.type = SymbolElement.ElementType.Area;
                    e.color = ColorIndex(asym, "inner_color", map);
                }
                else continue;
                if (e.color >= 0) list.Add(e);
            }
            return list;
        }

        // ---- Objects ----

        static MapObject ReadObject(XmlElement o, MapFile map)
        {
            if (!map.symbols.TryGetValue((int)Num(o, "symbol", -1), out MapSymbol symbol) || symbol.hidden) return null;
            int type = (int)Num(o, "type", 0);
            var obj = new MapObject { symbol = symbol, angle = Num(o, "rotation", 0) * 180.0 / Math.PI };
            if (type == 0) obj.kind = SymbolKind.Point;
            else if (type == 4) obj.kind = SymbolKind.Text;
            else obj.kind = symbol.kind == SymbolKind.Area ? SymbolKind.Area : SymbolKind.Line;
            var pts = new List<double>();
            ReadCoords(o, pts, obj.parts);
            if (pts.Count >= 2) obj.parts.Add(pts.ToArray());
            if (obj.kind == SymbolKind.Text)
            {
                var text = Child(o, "text");
                obj.text = text != null ? text.InnerText : "";
            }
            if (obj.kind == SymbolKind.Area)
            {
                var pattern = Child(o, "pattern");
                if (pattern != null) obj.angle = Num(pattern, "rotation", 0) * 180.0 / Math.PI;
            }
            return obj.parts.Count > 0 ? obj : null;
        }

        // "x y flags;x y;..." in 1/1000 mm, y down. Flag 1: a curve starts here, 16: last point of a part.
        static void ReadCoords(XmlElement o, List<double> pts, List<double[]> parts)
        {
            var coords = Child(o, "coords");
            if (coords == null) return;
            var raw = new List<(double x, double y, int f)>();
            bool any = false;
            foreach (XmlElement c in Children(coords, "coord"))
            {
                any = true;
                raw.Add((Num(c, "x", 0), Num(c, "y", 0), (int)Num(c, "flags", 0)));
            }
            if (!any)
            {
                foreach (string item in coords.InnerText.Split(';'))
                {
                    string[] f = item.Trim().Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length < 2) continue;
                    double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x);
                    double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y);
                    int flags = 0;
                    if (f.Length > 2) int.TryParse(f[2], out flags);
                    raw.Add((x, y, flags));
                }
            }
            int i = 0;
            while (i < raw.Count)
            {
                double x = raw[i].x / 1000.0, y = -raw[i].y / 1000.0;
                int flags = raw[i].f;
                pts.Add(x);
                pts.Add(y);
                if ((flags & 1) != 0 && i + 3 < raw.Count)
                {
                    MapGeometry.AddBezier(pts, x, y,
                        raw[i + 1].x / 1000.0, -raw[i + 1].y / 1000.0,
                        raw[i + 2].x / 1000.0, -raw[i + 2].y / 1000.0,
                        raw[i + 3].x / 1000.0, -raw[i + 3].y / 1000.0);
                    int endFlags = raw[i + 3].f;
                    i += 3;
                    // The end point of the curve is already added; it can start another curve or end a part
                    if ((endFlags & 1) != 0)
                    {
                        pts.RemoveRange(pts.Count - 2, 2);
                        continue;
                    }
                    if ((endFlags & 16) != 0 && parts != null)
                    {
                        if (pts.Count >= 4) parts.Add(pts.ToArray());
                        pts.Clear();
                    }
                    i++;
                    continue;
                }
                if ((flags & 16) != 0 && parts != null)
                {
                    if (pts.Count >= 4) parts.Add(pts.ToArray());
                    pts.Clear();
                }
                i++;
            }
        }
    }
}
