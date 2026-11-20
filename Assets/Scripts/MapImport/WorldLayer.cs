using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

// What the terrain is made of, in enough detail to build a 3D world of a small part of the map (the view of a TV
// control, TV/TvWorldBuilder.cs). The walkability grid only knows how quick each 10 m cell is; this keeps, for
// every 2 m cell, what grows there (white forest, the greens, open land, marsh, bare rock ...), and the stones,
// knolls, cliffs, paths, streams, walls and power lines as the map file has them.
// Written by the map import as world.bytes in the map's package. Plain C#, safe on a background thread.
namespace MapImport
{
    public static class WorldCell
    {
        // The low four bits: what the ground is
        public const byte Forest = 0, Open = 1, RoughOpen = 2, OpenTrees = 3, RoughOpenTrees = 4, LightGreen = 5, Green = 6,
            DarkGreen = 7, Cultivated = 8, Water = 9, BareRock = 10, Sand = 11, Paved = 12, Building = 13, Plot = 14,
            Impassable = 15;   // impassable vegetation (ISSprOM 410, the Swedish 411): looks like dark green, nobody gets through
        public const byte BaseMask = 0x0F;
        // Things drawn on top of the ground
        public const byte Marsh = 0x10, Undergrowth = 0x20, Stony = 0x40, Boulders = 0x80;
    }

    public enum WorldPointKind : byte
    {
        None = 0, Boulder, LargeBoulder, BoulderCluster, BoulderField, StonyGround, Knoll, LongKnoll, Depression, Pit,
        RockyPit, Cairn, BigTree, Bush, Spring, Well, WaterHole, Tower, SmallTower, FodderRack, Feature, RootStock,
        // Since 2026-11-20 the X of a man-made feature (530, 531) apart from the brown landform X (115); Feature is what
        // older packages have for all three
        ManMade, Landform
    }

    public enum WorldLineKind : byte
    {
        None = 0, Cliff, SmallCliff, EarthBank, EarthWall, Gully, Stream, SmallStream, Ditch, NarrowMarsh,
        WideRoad, Road, Track, Footpath, SmallPath, FaintPath, Ride, Railway, PowerLine, MajorPowerLine,
        Wall, RuinedWall, HighWall, Fence, RuinedFence, HighFence, Log,
        // 2026-11-20: the same passability as the 2D grid (SymbolRules)
        Hedge,      // impassable vegetation drawn as a line (ISSprOM 410.1, 411)
        Barrier,    // any other line nobody may cross (out-of-bounds boundary, uncrossable water's edge, impassable pipeline)
        Stairs,     // a stairway: up or down, over the cliff or wall it crosses, slowly
        Bridge      // a bridge or tunnel (512) and a crossing point (710): a way over or through
    }

    public struct WorldPoint
    {
        public WorldPointKind kind;
        public float e, n;          // metres, the game's map coordinates
        public float angle;         // degrees, counter-clockwise
    }

    public class WorldLine
    {
        public WorldLineKind kind;
        public float[] xy;          // e0, n0, e1, n1, ...
        public float minE, minN, maxE, maxN;

        public void Bounds()
        {
            minE = minN = float.MaxValue;
            maxE = maxN = float.MinValue;
            for (int i = 0; i + 1 < xy.Length; i += 2)
            {
                if (xy[i] < minE) minE = xy[i];
                if (xy[i] > maxE) maxE = xy[i];
                if (xy[i + 1] < minN) minN = xy[i + 1];
                if (xy[i + 1] > maxN) maxN = xy[i + 1];
            }
        }
    }

    public class WorldLayer
    {
        public const float CellSize = 2f;
        const int Magic = 0x444C574F;   // "OWLD"
        // 2 (2026-11-20): built with the passability rules of the 2D grid (Impassable cells, Hedge, Barrier, Stairs, Bridge)
        const int Version = 2;
        public int version = Version;
        const long MaxCells = 120000000;

        public float cell = CellSize;
        public int cols, rows;
        public double minE, minN;       // the lower left corner of cell (0, 0), in the game's map coordinates
        public byte[] cells;            // rows * cols, row 0 at the south
        public List<WorldPoint> points = new List<WorldPoint>();
        public List<WorldLine> lines = new List<WorldLine>();

        // What is at a place; outside the layer it is plain forest
        public byte At(double e, double n)
        {
            int c = (int)Math.Floor((e - minE) / cell), r = (int)Math.Floor((n - minN) / cell);
            if (c < 0 || r < 0 || c >= cols || r >= rows) return WorldCell.Forest;
            return cells[r * cols + c];
        }

        // ---- From a map file ----

        // extent: the part of the paper the map's grid covers (after the map edge); offE/offN: what the package adds
        // to the paper's metres to get the game's coordinates (MapPackageInfo.offsetEast/offsetNorth)
        // sprint: the map is a sprint map (impassable vegetation, hedges)
        public static WorldLayer Build(MapFile map, MapExtent extent, double offE, double offN, bool sprint = false)
        {
            double mpm = map.MetersPerMm;
            var w = new WorldLayer();
            double width = extent.Width * mpm, height = extent.Height * mpm;
            // A huge map gets bigger cells rather than no layer
            while (Math.Ceiling(width / w.cell) * Math.Ceiling(height / w.cell) > MaxCells) w.cell *= 2f;
            w.cols = Math.Max(1, (int)Math.Ceiling(width / w.cell));
            w.rows = Math.Max(1, (int)Math.Ceiling(height / w.cell));
            w.minE = extent.minX * mpm + offE;
            w.minN = extent.minY * mpm + offN;
            w.cells = new byte[w.rows * w.cols];

            // Areas, bottom colour first, the way they lie on the paper
            var areas = new List<(int prio, int order, MapObject obj, byte set, byte flag, bool clear)>();
            int order = 0;
            foreach (MapObject o in map.objects)
            {
                if (o.kind != SymbolKind.Area || o.symbol == null) continue;
                if (!AreaRule(o.symbol, sprint, out byte set, out byte flag, out bool clear)) continue;
                areas.Add((GridBuilder.TopColor(o.symbol), order++, o, set, flag, clear));
            }
            areas.Sort((a, b) => a.prio != b.prio ? b.prio.CompareTo(a.prio) : a.order.CompareTo(b.order));
            double scale = mpm / w.cell;
            byte[] cells = w.cells;
            int cols = w.cols;
            foreach (var a in areas)
            {
                byte set = a.set, flag = a.flag;
                bool clear = a.clear;
                GridBuilder.FillCells(a.obj.parts, extent.minX, extent.minY, scale, w.cols, w.rows, (r, c) =>
                {
                    int i = r * cols + c;
                    if (set != 255) cells[i] = (byte)((clear ? 0 : cells[i] & ~WorldCell.BaseMask) | set);
                    cells[i] |= flag;
                });
            }

            double[] edge = map.boundary != null && map.boundary.Length >= 6 ? map.boundary : null;
            foreach (MapObject o in map.objects)
            {
                if (o.symbol == null || o.parts.Count == 0) continue;
                if (o.kind == SymbolKind.Point)
                {
                    WorldPointKind kind = PointKind(o.symbol);
                    if (kind == WorldPointKind.None) continue;
                    double x = o.parts[0][0], y = o.parts[0][1];
                    if (x < extent.minX || y < extent.minY || x > extent.maxX || y > extent.maxY) continue;
                    if (edge != null && !MapBoundary.Inside(edge, x, y)) continue;
                    w.points.Add(new WorldPoint { kind = kind, e = (float)(x * mpm + offE), n = (float)(y * mpm + offN), angle = (float)o.angle });
                }
                else if (o.kind == SymbolKind.Line)
                {
                    WorldLineKind kind = LineKind(o.symbol, sprint);
                    if (kind == WorldLineKind.None) continue;
                    foreach (double[] part in o.parts)
                    {
                        List<double[]> pieces = edge != null ? MapBoundary.ClipLine(part, edge) : new List<double[]> { part };
                        foreach (double[] p in pieces)
                        {
                            if (p.Length < 4) continue;
                            var line = new WorldLine { kind = kind, xy = new float[p.Length] };
                            for (int i = 0; i + 1 < p.Length; i += 2)
                            {
                                line.xy[i] = (float)(p[i] * mpm + offE);
                                line.xy[i + 1] = (float)(p[i + 1] * mpm + offN);
                            }
                            line.Bounds();
                            // Lines wholly outside the grid are no use
                            if (line.maxE < w.minE || line.maxN < w.minN || line.minE > w.minE + w.cols * w.cell || line.minN > w.minN + w.rows * w.cell) continue;
                            w.lines.Add(line);
                        }
                    }
                }
            }
            return w;
        }

        // What an area symbol does to the cells under it. set = 255: the ground stays, only the flag is added.
        static bool AreaRule(MapSymbol s, bool sprint, out byte set, out byte flag, out bool clear)
        {
            set = 255;
            flag = 0;
            clear = false;
            if (SymbolRules.IsBuilding(s)) { set = WorldCell.Building; clear = true; return true; }
            if (GroundKind.Of(s) == GroundKind.Canopy) { set = WorldCell.Paved; clear = true; return true; }
            bool blocked = SymbolRules.For(s, sprint).role == TerrainRole.Blocked;
            switch (SymbolRules.Isom2017Major(s))
            {
                case 307: set = WorldCell.Water; flag = WorldCell.Marsh; clear = true; return true;   // uncrossable marsh
                case 410: case 411:
                    if (blocked) { set = WorldCell.Impassable; return true; }
                    break;
                case 709: case 714: set = WorldCell.Plot; clear = true; return true;               // out of bounds
            }
            switch (SymbolRules.Isom2017Major(s))
            {
                case 208: case 209: flag = WorldCell.Boulders; return true;
                case 210: case 211: case 212: flag = WorldCell.Stony; return true;
                case 213: set = WorldCell.Sand; clear = true; return true;
                case 214: set = WorldCell.BareRock; clear = true; return true;
                case 301: case 302: set = WorldCell.Water; clear = true; return true;
                case 307: case 308: case 310: flag = WorldCell.Marsh; return true;
                case 401: set = WorldCell.Open; return true;
                case 402: set = WorldCell.OpenTrees; return true;
                case 403: set = WorldCell.RoughOpen; return true;
                case 404: set = WorldCell.RoughOpenTrees; return true;
                case 405: set = WorldCell.Forest; return true;
                case 406: set = WorldCell.LightGreen; return true;
                case 407: case 409: flag = WorldCell.Undergrowth; return true;
                case 408: case 411: set = WorldCell.Green; return true;
                case 410: set = WorldCell.DarkGreen; return true;
                case 412: set = WorldCell.Cultivated; return true;
                case 413: case 414: set = WorldCell.OpenTrees; return true;
                case 501: case 522: set = WorldCell.Paved; clear = true; return true;
                case 520: set = WorldCell.Plot; clear = true; return true;
            }
            // A map maker's own symbol for ground nobody may enter (named so; e.g. 999): out of bounds like a garden
            if (blocked && SymbolRules.IsForbiddenArea(s)) { set = WorldCell.Plot; clear = true; return true; }
            return false;
        }

        static WorldPointKind PointKind(MapSymbol s)
        {
            string name = (s.name ?? "").ToLowerInvariant();
            if (name.Contains("rotvälta") || name.Contains("root stock") || name.Contains("rootstock")) return WorldPointKind.RootStock;
            switch (SymbolRules.Isom2017Major(s))
            {
                case 109: return WorldPointKind.Knoll;
                case 110: return WorldPointKind.LongKnoll;
                case 111: return WorldPointKind.Depression;
                case 112: return WorldPointKind.Pit;
                case 203: return WorldPointKind.RockyPit;
                case 204: return WorldPointKind.Boulder;
                case 205: return WorldPointKind.LargeBoulder;
                case 207: return WorldPointKind.BoulderCluster;
                case 208: case 209: return WorldPointKind.BoulderField;
                case 210: case 211: case 212: return WorldPointKind.StonyGround;
                case 303: return WorldPointKind.WaterHole;
                case 311: return WorldPointKind.Well;
                case 312: return WorldPointKind.Spring;
                case 417: return WorldPointKind.BigTree;
                case 418: return WorldPointKind.Bush;
                case 524: return WorldPointKind.Tower;
                case 525: return WorldPointKind.SmallTower;
                case 526: return WorldPointKind.Cairn;
                case 527: return WorldPointKind.FodderRack;
                case 115: return WorldPointKind.Landform;
                case 530: case 531: return WorldPointKind.ManMade;
            }
            return WorldPointKind.None;
        }

        static WorldLineKind LineKind(MapSymbol s, bool sprint)
        {
            string name = (s.name ?? "").ToLowerInvariant();
            if (name.Contains("vindfäl") || name.Contains("windfall") || name.Contains("fallen tree")) return WorldLineKind.Log;
            TerrainRole role = SymbolRules.For(s, sprint).role;
            if (role == TerrainRole.Stairs) return WorldLineKind.Stairs;
            int major = SymbolRules.Isom2017Major(s);
            if (role == TerrainRole.Bridge || major == 512) return WorldLineKind.Bridge;
            if (role == TerrainRole.BlockingLine && (major == 410 || major == 411 || major == 421)) return WorldLineKind.Hedge;
            // The 3D runner jumps an ordinary wall or fence, but one the 2D rules say is impassable stays impassable
            if (role == TerrainRole.BlockingLine && (major == 513 || major == 516)) return major == 513 ? WorldLineKind.HighWall : WorldLineKind.HighFence;
            switch (major)
            {
                case 104: return WorldLineKind.EarthBank;
                case 105: case 106: return WorldLineKind.EarthWall;
                case 107: case 108: return WorldLineKind.Gully;
                case 201: return WorldLineKind.Cliff;
                case 202: return WorldLineKind.SmallCliff;
                case 304: return WorldLineKind.Stream;
                case 305: return WorldLineKind.SmallStream;
                case 306: return WorldLineKind.Ditch;
                case 309: return WorldLineKind.NarrowMarsh;
                case 502: return WorldLineKind.WideRoad;
                case 503: return WorldLineKind.Road;
                case 504: return WorldLineKind.Track;
                case 505: return WorldLineKind.Footpath;
                case 506: return WorldLineKind.SmallPath;
                case 507: return WorldLineKind.FaintPath;
                case 508: return WorldLineKind.Ride;
                case 509: return WorldLineKind.Railway;
                case 510: return WorldLineKind.PowerLine;
                case 511: return WorldLineKind.MajorPowerLine;
                case 513: return WorldLineKind.Wall;
                case 514: return WorldLineKind.RuinedWall;
                case 515: return WorldLineKind.HighWall;
                case 516: return WorldLineKind.Fence;
                case 517: return WorldLineKind.RuinedFence;
                case 518: return WorldLineKind.HighFence;
            }
            // Building outlines are closed by the building itself; every other impassable line is a barrier
            if (role == TerrainRole.BlockingLine && major != 521 && major != 526) return WorldLineKind.Barrier;
            return WorldLineKind.None;
        }

        // ---- The file ----

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            {
                using (var head = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
                {
                    head.Write(Magic);
                    head.Write(Version);
                }
                using (var zip = new DeflateStream(ms, CompressionLevel.Optimal, true))
                using (var w = new BinaryWriter(zip))
                {
                    w.Write(cell);
                    w.Write(cols);
                    w.Write(rows);
                    w.Write(minE);
                    w.Write(minN);
                    w.Write(cells);
                    w.Write(points.Count);
                    foreach (WorldPoint p in points)
                    {
                        w.Write((byte)p.kind);
                        w.Write(p.e);
                        w.Write(p.n);
                        w.Write(p.angle);
                    }
                    w.Write(lines.Count);
                    foreach (WorldLine line in lines)
                    {
                        w.Write((byte)line.kind);
                        w.Write(line.xy.Length);
                        var raw = new byte[line.xy.Length * 4];
                        Buffer.BlockCopy(line.xy, 0, raw, 0, raw.Length);
                        w.Write(raw);
                    }
                }
                return ms.ToArray();
            }
        }

        // null when the bytes are not a world layer this version of the game reads
        public static WorldLayer FromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12) return null;
            try
            {
                int fileVersion = BitConverter.ToInt32(bytes, 4);
                if (BitConverter.ToInt32(bytes, 0) != Magic || fileVersion < 1 || fileVersion > Version) return null;
                using (var ms = new MemoryStream(bytes, 8, bytes.Length - 8))
                using (var zip = new DeflateStream(ms, CompressionMode.Decompress))
                using (var r = new BinaryReader(zip))
                {
                    var w = new WorldLayer { version = fileVersion, cell = r.ReadSingle(), cols = r.ReadInt32(), rows = r.ReadInt32(), minE = r.ReadDouble(), minN = r.ReadDouble() };
                    if (w.cell <= 0f || w.cols <= 0 || w.rows <= 0 || (long)w.cols * w.rows > MaxCells) return null;
                    w.cells = ReadAll(r, w.cols * w.rows);
                    int pointCount = r.ReadInt32();
                    if (pointCount < 0 || pointCount > 5000000) return null;
                    for (int i = 0; i < pointCount; i++)
                    {
                        w.points.Add(new WorldPoint { kind = (WorldPointKind)r.ReadByte(), e = r.ReadSingle(), n = r.ReadSingle(), angle = r.ReadSingle() });
                    }
                    int lineCount = r.ReadInt32();
                    if (lineCount < 0 || lineCount > 5000000) return null;
                    for (int i = 0; i < lineCount; i++)
                    {
                        var line = new WorldLine { kind = (WorldLineKind)r.ReadByte() };
                        int n = r.ReadInt32();
                        if (n < 0 || n > 4000000) return null;
                        byte[] raw = ReadAll(r, n * 4);
                        line.xy = new float[n];
                        Buffer.BlockCopy(raw, 0, line.xy, 0, raw.Length);
                        line.Bounds();
                        w.lines.Add(line);
                    }
                    return w;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        // A deflate stream hands out the bytes a piece at a time
        static byte[] ReadAll(BinaryReader r, int count)
        {
            var buffer = new byte[count];
            int got = 0;
            while (got < count)
            {
                int n = r.Read(buffer, got, count - got);
                if (n <= 0) throw new EndOfStreamException();
                got += n;
            }
            return buffer;
        }
    }
}
