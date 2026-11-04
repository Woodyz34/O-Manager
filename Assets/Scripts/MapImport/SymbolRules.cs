using System;

// What a map symbol means for a runner. Symbol numbers follow ISOM 2017 (forest) and ISSprOM 2019 (sprint);
// the Swedish SOFT symbol sets use the same numbers (506 = small path, 521 = building ...).
// Speeds are compared with runnable forest (1.0) and are the ones the old grid script used.
namespace MapImport
{
    public enum TerrainRole
    {
        None,           // no effect on running (text, contours, boundaries ...)
        Ground,         // an area that sets the speed of the ground
        Blocked,        // an area nobody can enter (lake, building, out of bounds)
        Path,           // a line that is quick to run along
        BlockingLine,   // a line nobody can cross (impassable cliff, high fence, impassable wall)
        SlowLine,       // a line that takes a moment to cross (stone wall, fence)
        Crossing,       // a point that opens a way through a blocking line (gate, crossing point)
        Bridge          // a line that is a way over water or another obstacle
    }

    public struct TerrainRule
    {
        public TerrainRole role;
        public float speed;

        public TerrainRule(TerrainRole role, float speed = 1f)
        {
            this.role = role;
            this.speed = speed;
        }

        public static readonly TerrainRule None = new TerrainRule(TerrainRole.None, 1f);
    }

    public static class SymbolRules
    {
        public static TerrainRule For(MapSymbol s, bool sprint)
        {
            if (s == null) return TerrainRule.None;
            string name = (s.name ?? "").ToLowerInvariant();
            int m = s.major, minor = s.minor;
            bool area = s.kind == SymbolKind.Area;
            bool line = s.kind == SymbolKind.Line;
            bool point = s.kind == SymbolKind.Point;

            if (area)
            {
                switch (m)
                {
                    case 301: return Blocked();                                           // uncrossable water
                    case 302: return Ground(sprint ? 0f : 0.5f, sprint);                   // shallow water
                    case 307: return Blocked();                                           // uncrossable marsh
                    case 308: return Ground(0.8f, sprint);                                // marsh
                    case 310: return Ground(0.9f, sprint);                                // indistinct marsh
                    case 401: return Ground(1.3f, sprint);                                // open land
                    case 402: return Ground(1.2f, sprint);                                // open land, scattered trees
                    case 403: return Ground(1.3f, sprint);                                // rough open land
                    case 404: return Ground(1.2f, sprint);                                // rough open, scattered trees
                    case 405: return Ground(1.0f, sprint);                                // forest
                    case 406: return Ground(0.7f, sprint);                                // slow running
                    case 407: return Ground(0.7f, sprint);                                // slow running, good visibility
                    case 408: return Ground(0.6f, sprint);                                // difficult to run
                    case 409: return Ground(0.6f, sprint);                                // walk, good visibility
                    case 410:
                        // ISSprOM 410 is impassable vegetation; ISOM 410 is very difficult
                        if (sprint || name.Contains("impassable") || name.Contains("opasserbar")) return Blocked();
                        return Ground(0.6f, sprint);
                    case 411: return Ground(sprint ? 0.6f : 0.8f, sprint);                // runnable in one direction
                    case 412: return Ground(1.3f, sprint);                                // cultivated land
                    case 413: return Ground(1.0f, sprint);                                // orchard
                    case 414: return Ground(1.0f, sprint);                                // vineyard
                    case 210: return Ground(0.85f, sprint);                               // stony ground
                    case 211: return Ground(0.7f, sprint);
                    case 212: return Ground(0.5f, sprint);
                    case 213: return Ground(0.85f, sprint);                               // sandy ground
                    case 214: return Ground(1.2f, sprint);                                // bare rock
                    case 501:
                        if (name.Contains("grus") || name.Contains("gravel") || name.Contains("unpaved")) return Ground(1.4f, sprint);
                        return Ground(1.5f, sprint);                                      // paved area
                    case 503: return Ground(1.4f, sprint);                                // meeting places, squares
                    case 520: return Blocked();                                           // area that shall not be entered
                    case 521: return Blocked();                                           // building
                    case 522: return Ground(1.4f, sprint);                                // canopy: you can run under it
                    case 709: return Blocked();                                           // out of bounds (course setting)
                    case 714: return Blocked();                                           // temporary out of bounds
                }
                if (m == 206) return Blocked();                                           // gigantic boulder
                if (m == 528 || m == 529) return Blocked();
                return TerrainRule.None;
            }

            if (line)
            {
                switch (m)
                {
                    case 201: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // impassable cliff
                    case 301: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // lake shore
                    case 307: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // uncrossable marsh edge
                    case 502: return Path(1.55f);                                         // wide road, cycle path
                    case 503: return Path(1.5f);                                          // road
                    case 504: return Path(1.55f);                                         // vehicle track
                    case 505: return Path(1.45f);                                         // footpath
                    case 506: return Path(1.35f);                                         // small path
                    case 507: return Path(1.2f);                                          // less distinct path
                    case 508: return Path(1.4f);                                          // narrow ride
                    case 509: return sprint ? TerrainRule.None : Path(1.3f);             // railway
                    case 512: return new TerrainRule(TerrainRole.Bridge, 1.4f);           // bridge / tunnel
                    case 513:
                        if (name.Contains("opasserbar") || name.Contains("impassable")) return new TerrainRule(TerrainRole.BlockingLine, 0f);
                        return new TerrainRule(TerrainRole.SlowLine, 0.5f);               // stone wall: over, but slower
                    case 514: return new TerrainRule(TerrainRole.SlowLine, 0.8f);         // ruined wall
                    case 515: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // impassable wall
                    case 516: return new TerrainRule(TerrainRole.SlowLine, 0.8f);         // fence
                    case 517: return TerrainRule.None;                                    // ruined fence
                    case 518: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // impassable fence
                    case 521: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // building outline
                    case 529: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // prominent impassable line
                    case 532: return Path(1.2f);                                          // stairway
                    case 534: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // impassable pipeline
                    case 707: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // uncrossable boundary (course)
                }
                // Paved paths drawn as lines (ISSprOM "footprint" lines) and road edge lines
                if (m == 501 && (name.Contains("footprint") || name.Contains("sidlinje") || name.Contains("road edge"))) return Path(1.45f);
                return TerrainRule.None;
            }

            if (point)
            {
                if (m == 519 || m == 512 || m == 708) return new TerrainRule(TerrainRole.Crossing, 1.2f); // crossing point, footbridge
                if (m == 206) return Blocked();
                return TerrainRule.None;
            }
            return TerrainRule.None;
        }

        static TerrainRule Ground(float speed, bool sprint)
        {
            return speed <= 0f ? Blocked() : new TerrainRule(TerrainRole.Ground, speed);
        }

        static TerrainRule Blocked() => new TerrainRule(TerrainRole.Blocked, 0f);

        static TerrainRule Path(float speed) => new TerrainRule(TerrainRole.Path, speed);

        // Contours and slope lines used for heights
        public static bool IsContour(MapSymbol s) => s != null && s.kind == SymbolKind.Line && (s.major == 101 || s.major == 102);
        public static bool IsIndexContour(MapSymbol s) => s != null && s.major == 102 && s.kind == SymbolKind.Line;
        public static bool IsSlopeTick(MapSymbol s) => s != null && s.kind == SymbolKind.Point && (s.major == 101 || s.major == 102) && s.minor > 0;
        public static bool IsWater(MapSymbol s) => s != null && s.kind == SymbolKind.Area && (s.major == 301 || s.major == 302);

        // Guess sprint or forest from the symbols: sprint maps (ISSprOM) are drawn at 1:4000 or 1:5000
        public static bool LooksLikeSprint(MapFile map)
        {
            return map.scale <= 5000;
        }

        // Contour interval in metres
        public static double Equidistance(MapFile map) => map.scale <= 5000 ? 2.5 : 5.0;
    }
}
