using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// From a map file to everything the game needs for a new map: the grid, the map objects (stones, paths, streams ...)
// and the picture. Plain C#, so the heavy work can run on a background thread.
namespace MapImport
{
    [Serializable]
    public class MapPackageInfo
    {
        public string id;
        public string name;
        public string mapType = "forest";     // "forest", "sprint" or "both"
        public string sourceFile;
        public string sourceFormat;           // "OCAD 12.4", "OpenOrienteering Mapper 9"
        public float scale;
        public float cellSize;
        public int rows, cols;
        // Grid bounds in the game's convention: the centres of the first and last cells, in metres
        public float minx, miny, maxx, maxy;
        // Picture: its size, how many pixels to a metre on the ground, and the pixel (from the bottom left)
        // at the centre of the grid. The drawn picture starts at the grid's bottom left corner.
        public int pictureWidth, pictureHeight;
        public float pixelsPerMeter;
        public float pivotX, pivotY;
        public bool ownPicture;               // the player gave a PNG instead of the drawn one
        public bool hasHeights;
        public float contourInterval;         // metres, used for the heights
        // Where the map lies in the national grid, when the file says so
        public bool georeferenced;
        public double refEast, refNorth;
        public float gridAngle;
        public int crs;
        // The game's coordinates are the paper's in metres plus these, so they are never negative
        // (courses are sent as whole positive metres): ground = paper mm * scale / 1000 + offset
        public double offsetEast, offsetNorth;
        public string author;
        // Who sent the map in and the club that owns it, so the game's owner can ask if a club gets in touch
        public string sender;
        public string ownerClub;
        public string contact;
        public string created;                // ISO date
        public bool permissionConfirmed;      // the uploader says the map's owner allows it
    }

    public class MapPackage
    {
        public MapPackageInfo info;
        public byte[] grid;            // GridBinary version 2
        public string pointsJson;      // GeoJSON, same layer names as the old Illern files
        public string linesJson;
        public byte[] pictureRgba;     // row 0 at the top
        // On the paper, in mm: the grid, and every object of the map (an exported PNG of the whole map covers that)
        public MapExtent gridExtent;
        public MapExtent fullExtent;
        public double metersPerMm;
        public List<string> log = new List<string>();
    }

    public static class MapPackageBuilder
    {
        public const float ForestCell = 10f;
        public const float SprintCell = 2f;

        // Reads either file type
        public static MapFile ReadMapFile(byte[] bytes)
        {
            if (OcadReader.IsOcad(bytes)) return OcadReader.Read(bytes);
            if (OmapReader.IsOmap(bytes)) return OmapReader.Read(bytes);
            throw new InvalidDataException("The file is not a map file the game knows. It reads OCAD files (.ocd, version 11 or newer) and OpenOrienteering Mapper files (.omap/.xmap).");
        }

        public static string DescribeVersion(byte[] bytes)
        {
            if (OcadReader.IsOcad(bytes)) return OcadReader.VersionOf(bytes);
            if (OmapReader.IsOmap(bytes)) return OmapReader.VersionOf(bytes);
            return null;
        }

        // pixelsPerMm: picture resolution (24 is about 600 dpi); maxPictureSide caps it
        public static MapPackage Build(MapFile map, string name, bool sprint, double pixelsPerMm, int maxPictureSide,
                                       bool drawPicture, Action<string, float> progress = null)
        {
            var pkg = new MapPackage();
            void Log(string s) { pkg.log.Add(s); }
            progress?.Invoke("Reading the map", 0.05f);
            MapExtent extent = MapExtent.Of(map);
            float cell = sprint ? SprintCell : ForestCell;

            progress?.Invoke("Building the grid and the heights", 0.15f);
            MapGrid2 grid = GridBuilder.Build(map, extent, sprint, cell, Log);
            double mpm = map.MetersPerMm;
            var gridExtent = new MapExtent
            {
                minX = extent.minX,
                minY = extent.minY,
                maxX = extent.minX + grid.cols * cell / mpm,
                maxY = extent.minY + grid.rows * cell / mpm
            };
            pkg.gridExtent = gridExtent;
            pkg.fullExtent = MapExtent.Full(map);
            pkg.metersPerMm = mpm;

            // Coordinates start at 1000 m, so a course's points are never negative
            double offE = 1000 - Math.Floor(grid.minE), offN = 1000 - Math.Floor(grid.minN);
            var info = new MapPackageInfo
            {
                offsetEast = offE,
                offsetNorth = offN,
                name = name,
                mapType = sprint ? "sprint" : "forest",
                sourceFormat = (map.format + " " + map.version).Trim(),
                scale = (float)map.scale,
                cellSize = cell,
                rows = grid.rows,
                cols = grid.cols,
                minx = (float)(grid.minE + offE + cell / 2.0),
                miny = (float)(grid.minN + offN + cell / 2.0),
                maxx = (float)(grid.minE + offE + (grid.cols - 0.5) * cell),
                maxy = (float)(grid.minN + offN + (grid.rows - 0.5) * cell),
                hasHeights = grid.hasHeights,
                contourInterval = (float)SymbolRules.Equidistance(map),
                georeferenced = map.georeferenced,
                refEast = map.refEast,
                refNorth = map.refNorth,
                gridAngle = (float)map.gridAngle,
                crs = map.crsCode,
                created = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            };
            pkg.info = info;
            pkg.grid = WriteGrid(grid, info);

            progress?.Invoke("Picking out stones, paths and streams", 0.35f);
            WriteLayers(map, offE, offN, out pkg.pointsJson, out pkg.linesJson);

            if (drawPicture)
            {
                progress?.Invoke("Drawing the map picture", 0.45f);
                double ppm = MapPainter.PixelsPerMm(gridExtent, pixelsPerMm, maxPictureSide - 4);
                // The picture starts at the grid's bottom left corner and is made a multiple of 4 pixels each way,
                // so the graphics card can keep it compressed
                int w = (int)Math.Ceiling(gridExtent.Width * ppm / 4.0) * 4;
                int h = (int)Math.Ceiling(gridExtent.Height * ppm / 4.0) * 4;
                var pictureExtent = new MapExtent
                {
                    minX = gridExtent.minX,
                    minY = gridExtent.minY,
                    maxX = gridExtent.minX + (w - 0.01) / ppm,
                    maxY = gridExtent.minY + (h - 0.01) / ppm
                };
                var painter = new MapPainter(map, pictureExtent, ppm);
                painter.Paint(p => progress?.Invoke("Drawing the map picture", 0.45f + 0.5f * p));
                pkg.pictureRgba = painter.rgba;
                info.pictureWidth = painter.width;
                info.pictureHeight = painter.height;
                info.pixelsPerMeter = (float)(ppm / mpm);
                info.pivotX = (float)(gridExtent.Width * ppm / 2.0);
                info.pivotY = (float)(gridExtent.Height * ppm / 2.0);
            }
            progress?.Invoke("Done", 1f);
            return pkg;
        }

        // Fits a PNG the player exported from OCAD or OOM. It has to show the whole map (all objects), so it covers
        // fullExtent; the result is null and a reason is given if its shape does not match.
        public static bool FitOwnPicture(MapPackage pkg, int width, int height, out string problem)
        {
            problem = null;
            MapExtent full = pkg.fullExtent;
            double ratio = width / (double)height, expected = full.Width / full.Height;
            if (Math.Abs(ratio / expected - 1) > 0.03)
            {
                problem = string.Format(CultureInfo.InvariantCulture,
                    "The picture has the wrong shape ({0} x {1} pixels). Export the whole map, all objects, as PNG: it should be about {2:0.00} times as wide as it is high.",
                    width, height, expected);
                return false;
            }
            double ppmX = width / full.Width, ppmY = height / full.Height;
            MapExtent g = pkg.gridExtent;
            pkg.info.pictureWidth = width;
            pkg.info.pictureHeight = height;
            pkg.info.pixelsPerMeter = (float)(ppmX / pkg.metersPerMm);
            pkg.info.pivotX = (float)((g.minX - full.minX + g.Width / 2) * ppmX);
            pkg.info.pivotY = (float)((g.minY - full.minY + g.Height / 2) * ppmY);
            pkg.info.ownPicture = true;
            return true;
        }

        // ---- The package folder ----

        public const int ThumbnailSide = 768;

        // Writes map.json, grid.bytes, points.json, lines.json, picture.png and (for a drawn picture) thumb.png.
        // Safe on a background thread. ownPng: a picture the player gave (FitOwnPicture first); its thumbnail is made by the caller.
        public static void WriteFolder(string folder, MapPackage pkg, byte[] ownPng, Action<string, float> progress = null)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "grid.bytes"), pkg.grid);
            File.WriteAllText(Path.Combine(folder, "points.json"), pkg.pointsJson);
            File.WriteAllText(Path.Combine(folder, "lines.json"), pkg.linesJson);
            if (ownPng != null)
            {
                File.WriteAllBytes(Path.Combine(folder, "picture.png"), ownPng);
            }
            else if (pkg.pictureRgba != null)
            {
                progress?.Invoke("Saving the map picture", 0.96f);
                PngFile.Write(Path.Combine(folder, "picture.png"), pkg.pictureRgba, pkg.info.pictureWidth, pkg.info.pictureHeight);
                byte[] thumb = PngFile.Shrink(pkg.pictureRgba, pkg.info.pictureWidth, pkg.info.pictureHeight, ThumbnailSide, out int tw, out int th);
                PngFile.Write(Path.Combine(folder, "thumb.png"), thumb, tw, th);
                pkg.pictureRgba = null;
            }
            WriteInfo(folder, pkg.info);
        }

        // map.json, written by hand so it works without Unity (Unity's JsonUtility reads it back)
        public static void WriteInfo(string folder, MapPackageInfo i)
        {
            var sb = new StringBuilder("{\n");
            void S(string k, string v) { sb.Append("  \"").Append(k).Append("\": \"").Append(Escape(v ?? "")).Append("\",\n"); }
            void N(string k, double v) { sb.Append("  \"").Append(k).Append("\": ").Append(v.ToString("R", CultureInfo.InvariantCulture)).Append(",\n"); }
            void B(string k, bool v) { sb.Append("  \"").Append(k).Append("\": ").Append(v ? "true" : "false").Append(",\n"); }
            S("id", i.id); S("name", i.name); S("mapType", i.mapType); S("sourceFile", i.sourceFile); S("sourceFormat", i.sourceFormat);
            N("scale", i.scale); N("cellSize", i.cellSize); N("rows", i.rows); N("cols", i.cols);
            N("minx", i.minx); N("miny", i.miny); N("maxx", i.maxx); N("maxy", i.maxy);
            N("pictureWidth", i.pictureWidth); N("pictureHeight", i.pictureHeight); N("pixelsPerMeter", i.pixelsPerMeter);
            N("pivotX", i.pivotX); N("pivotY", i.pivotY); B("ownPicture", i.ownPicture); B("hasHeights", i.hasHeights); N("contourInterval", i.contourInterval);
            B("georeferenced", i.georeferenced); N("refEast", i.refEast); N("refNorth", i.refNorth); N("gridAngle", i.gridAngle); N("crs", i.crs);
            N("offsetEast", i.offsetEast); N("offsetNorth", i.offsetNorth);
            S("author", i.author); S("sender", i.sender); S("ownerClub", i.ownerClub); S("contact", i.contact); S("created", i.created);
            sb.Append("  \"permissionConfirmed\": ").Append(i.permissionConfirmed ? "true" : "false").Append("\n}\n");
            File.WriteAllText(Path.Combine(folder, "map.json"), sb.ToString());
        }

        // ---- Grid file: GridBinary version 2 (speed and height per cell) ----

        public static byte[] WriteGrid(MapGrid2 g, MapPackageInfo info)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(0x4452474F);   // "OGRD"
                w.Write(2);
                w.Write(info.minx);
                w.Write(info.miny);
                w.Write(info.maxx);
                w.Write(info.maxy);
                w.Write(g.cellSize);
                w.Write(g.rows);
                w.Write(g.cols);
                var buffer = new byte[g.speed.Length * 4];
                Buffer.BlockCopy(g.speed, 0, buffer, 0, buffer.Length);
                w.Write(buffer);
                w.Write(g.hasHeights);
                Buffer.BlockCopy(g.height, 0, buffer, 0, buffer.Length);
                w.Write(buffer);
                w.Flush();
                return ms.ToArray();
            }
        }

        // ---- Map objects for the game (points and lines), as GeoJSON in metres ----

        // The game's miss logic looks for these Swedish names, so other symbol sets get them too
        static readonly Dictionary<int, string> SwedishNames = new Dictionary<int, string>
        {
            { 101, "Höjdkurva" }, { 102, "Stödkurva" }, { 103, "Hjälpkurva" },
            { 109, "Punkthöjd" }, { 110, "Avlång punkthöjd" }, { 111, "Liten naturlig grop" }, { 112, "Liten grävd grop" },
            { 201, "Opasserbar brant" }, { 202, "Passerbar brant/Skrent" }, { 204, "Sten" }, { 205, "Stor sten" },
            { 206, "Jättestor sten" }, { 207, "Grupp av stenar" }, { 208, "Blockterräng" }, { 210, "Stenig mark" },
            { 304, "Större bäck" }, { 305, "Passerbart litet vattendrag" }, { 306, "Mindre dike" }, { 309, "Smal sankmark, surdrag" },
            { 415, "Exakt begränsningslinje" }, { 416, "Beståndsgräns" },
            { 502, "Större väg 0.5" }, { 503, "Smal väg" }, { 504, "Körväg" }, { 505, "Stor stig" }, { 506, "Liten stig" },
            { 507, "Liten otydlig stig" }, { 508, "Rågång/Drivningsväg" }, { 509, "Järnväg" },
            { 513, "Stenmur" }, { 516, "Staket / stängsel" }, { 518, "Högt stängsel" }
        };

        public static string LayerName(MapSymbol s)
        {
            string name = SwedishNames.TryGetValue(s.major, out string sv) ? sv : s.name;
            return (s.major * 1000 + Math.Min(s.minor, 999)).ToString("000000", CultureInfo.InvariantCulture) + " " + name;
        }

        public static void WriteLayers(MapFile map, double offsetEast, double offsetNorth, out string pointsJson, out string linesJson)
        {
            double mpm = map.MetersPerMm;
            var points = new StringBuilder("{\"type\":\"FeatureCollection\",\"features\":[");
            var lines = new StringBuilder("{\"type\":\"FeatureCollection\",\"features\":[");
            bool firstPoint = true, firstLine = true;
            foreach (MapObject o in map.objects)
            {
                if (!MapExtent.IsTerrain(o.symbol)) continue;
                string layer = Escape(LayerName(o.symbol));
                if (o.kind == SymbolKind.Point)
                {
                    if (!firstPoint) points.Append(',');
                    firstPoint = false;
                    points.Append("{\"type\":\"Feature\",\"properties\":{\"Layer\":\"").Append(layer)
                          .Append("\"},\"geometry\":{\"type\":\"Point\",\"coordinates\":[");
                    AppendCoord(points, o.parts[0][0] * mpm + offsetEast, o.parts[0][1] * mpm + offsetNorth);
                    points.Append("]}}");
                }
                else if (o.kind == SymbolKind.Line)
                {
                    foreach (double[] p in o.parts)
                    {
                        if (p.Length < 4) continue;
                        if (!firstLine) lines.Append(',');
                        firstLine = false;
                        lines.Append("{\"type\":\"Feature\",\"properties\":{\"Layer\":\"").Append(layer)
                             .Append("\"},\"geometry\":{\"type\":\"LineString\",\"coordinates\":[");
                        // Curves were flattened finely for drawing; every metre or so is plenty for the colliders
                        double lastX = double.NaN, lastY = double.NaN;
                        int n = p.Length / 2;
                        bool firstCoord = true;
                        for (int i = 0; i < n; i++)
                        {
                            double x = p[2 * i] * mpm + offsetEast, y = p[2 * i + 1] * mpm + offsetNorth;
                            bool last = i == n - 1;
                            if (!last && !double.IsNaN(lastX) && MapGeometry.Dist(x, y, lastX, lastY) < 1.0) continue;
                            if (!firstCoord) lines.Append(',');
                            firstCoord = false;
                            lines.Append('[');
                            AppendCoord(lines, x, y);
                            lines.Append(']');
                            lastX = x; lastY = y;
                        }
                        lines.Append("]}}");
                    }
                }
            }
            points.Append("]}");
            lines.Append("]}");
            pointsJson = points.ToString();
            linesJson = lines.ToString();
        }

        static void AppendCoord(StringBuilder sb, double x, double y)
        {
            sb.Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(',').Append(y.ToString("0.##", CultureInfo.InvariantCulture));
        }

        static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
