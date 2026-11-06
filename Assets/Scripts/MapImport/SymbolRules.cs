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
        // Old maps (ISOM 2000, ISSOM 2007, many OCAD 6-10 files) number some symbols differently: building 526,
        // forbidden area 527, paved area 529, uncrossable water 304. In the new standards 526-529 are points or
        // lines, never areas, so an area symbol with one of those numbers tells the map uses the old numbering.
        public static void MarkOldNumbers(MapFile map)
        {
            bool old = false;
            foreach (MapSymbol s in map.symbols.Values)
            {
                if (s.kind == SymbolKind.Area && (s.major == 526 || s.major == 527 || s.major == 529)) { old = true; break; }
            }
            foreach (MapSymbol s in map.symbols.Values) s.oldNumbers = old;
        }

        public static TerrainRule For(MapSymbol s, bool sprint)
        {
            if (s == null) return TerrainRule.None;
            if (s.oldNumbers) return ForOld(s, sprint);
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

        public static bool IsBuilding(MapSymbol s) => s != null && s.kind == SymbolKind.Area && (s.oldNumbers ? s.major == 526 : s.major == 521);

        // Areas nobody may enter: out of bounds, forbidden, course-setting overprint
        public static bool IsOutOfBounds(MapSymbol s) => s != null && (s.major >= 700 || (s.oldNumbers ? s.major == 527 || s.major == 528 : s.major == 520));

        // ISOM 2000 / ISSOM 2007 numbering. The names help where the two old standards differ (304 is uncrossable
        // water in ISSOM but a crossable stream in ISOM 2000).
        static TerrainRule ForOld(MapSymbol s, bool sprint)
        {
            string name = (s.name ?? "").ToLowerInvariant();
            int m = s.major;
            bool area = s.kind == SymbolKind.Area;
            bool line = s.kind == SymbolKind.Line;
            bool point = s.kind == SymbolKind.Point;
            bool impassable = name.Contains("opasserb") || name.Contains("uncrossable") || name.Contains("impassable") || name.Contains("not be entered");
            bool canopy = name.Contains("skärmtak") || name.Contains("canopy") || name.Contains("överbyggnad");

            if (area)
            {
                switch (m)
                {
                    case 301: return Blocked();                                           // lake
                    case 302: return Blocked();                                           // pond
                    case 304: return impassable || sprint ? Blocked() : Ground(0.5f, sprint); // uncrossable body of water (ISSOM)
                    case 305: return Ground(sprint ? 0.4f : 0.5f, sprint);                // crossable shallow water
                    case 308: return impassable ? Blocked() : Ground(0.8f, sprint);       // uncrossable marsh (ISOM 2000)
                    case 309: return impassable ? Blocked() : Ground(0.8f, sprint);       // marsh (ISOM 2000) / uncrossable marsh (ISSOM)
                    case 310: return Ground(0.8f, sprint);                                // marsh
                    case 311: return Ground(0.9f, sprint);                                // indistinct marsh
                    case 210: return Ground(0.85f, sprint);                               // stony ground
                    case 211: return Ground(0.85f, sprint);                               // open sandy ground
                    case 212: return Ground(1.2f, sprint);                                // bare rock
                    case 401: return Ground(1.3f, sprint);
                    case 402: return Ground(1.2f, sprint);
                    case 403: return Ground(1.3f, sprint);
                    case 404: return Ground(1.2f, sprint);
                    case 405: return Ground(1.0f, sprint);
                    case 406: return Ground(0.7f, sprint);
                    case 407: return Ground(0.7f, sprint);
                    case 408: return Ground(0.6f, sprint);
                    case 409: return Ground(0.6f, sprint);
                    case 410: return impassable ? Blocked() : Ground(0.4f, sprint);       // vegetation, very difficult
                    case 411: return Ground(0.8f, sprint);
                    case 412: case 415: return Ground(1.3f, sprint);                      // cultivated land
                    case 413: case 414: return Ground(1.0f, sprint);                      // orchard, vineyard
                    case 421: return Blocked();                                           // impassable vegetation (ISSOM)
                    case 526: return canopy ? Ground(1.4f, sprint) : Blocked();           // building (canopy: run under it)
                    case 527: return Blocked();                                           // forbidden area / settlement
                    case 528: return Blocked();                                           // permanently out of bounds
                    case 529:                                                             // paved area
                        if (name.Contains("grus") || name.Contains("gravel")) return Ground(1.4f, sprint);
                        return Ground(1.5f, sprint);
                    case 709: case 714: return Blocked();                                 // out of bounds (course setting)
                }
                if (m == 206 && impassable) return Blocked();
                return TerrainRule.None;
            }

            if (line)
            {
                switch (m)
                {
                    case 201: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // impassable cliff
                    case 301: case 302: return new TerrainRule(TerrainRole.BlockingLine, 0f);   // lake shore
                    case 304: return impassable ? new TerrainRule(TerrainRole.BlockingLine, 0f) : TerrainRule.None;
                    case 308: case 309: return impassable ? new TerrainRule(TerrainRole.BlockingLine, 0f) : TerrainRule.None;
                    case 421: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // impassable vegetation edge
                    case 501: return Path(1.55f);                                         // motorway (ISOM 2000)
                    case 502: return Path(1.55f);                                         // major road
                    case 503: return Path(1.5f);                                          // minor road
                    case 504: return Path(1.55f);                                         // road
                    case 505: return Path(1.45f);                                         // vehicle track
                    case 506: return Path(1.35f);                                         // footpath
                    case 507: return Path(1.2f);                                          // small path
                    case 508: return Path(1.2f);                                          // less distinct path
                    case 509: return Path(1.4f);                                          // narrow ride
                    case 512: return new TerrainRule(TerrainRole.Bridge, 1.4f);           // bridge
                    case 513: case 519:                                                   // stone wall (ISOM 2000 513, ISSOM 519)
                        if (impassable) return new TerrainRule(TerrainRole.BlockingLine, 0f);
                        return new TerrainRule(TerrainRole.SlowLine, 0.5f);
                    case 514: return new TerrainRule(TerrainRole.SlowLine, 0.8f);         // ruined wall
                    case 515:                                                             // high wall (ISOM 2000) / railway (ISSOM)
                        return impassable || name.Contains("mur") || name.Contains("wall") ? new TerrainRule(TerrainRole.BlockingLine, 0f) : TerrainRule.None;
                    case 516: case 522: return new TerrainRule(TerrainRole.SlowLine, 0.8f);   // fence, railing
                    case 518:                                                             // high fence (ISOM 2000) / tunnel (ISSOM)
                        return name.Contains("tunnel") || name.Contains("genomgång") ? TerrainRule.None : new TerrainRule(TerrainRole.BlockingLine, 0f);
                    case 521: case 524: return new TerrainRule(TerrainRole.BlockingLine, 0f); // impassable wall, impassable fence
                    case 526: return canopy ? TerrainRule.None : new TerrainRule(TerrainRole.BlockingLine, 0f); // building outline
                    case 527: case 528: return new TerrainRule(TerrainRole.BlockingLine, 0f);   // out of bounds edge
                    case 529:                                                             // paved paths; paved area edges and stairs
                        if (name.Contains("stig") || name.Contains("path") || name.Contains("väg") || name.Contains("road")) return Path(1.45f);
                        return TerrainRule.None;
                    case 533: case 534: return impassable ? new TerrainRule(TerrainRole.BlockingLine, 0f) : TerrainRule.None; // pipeline
                    case 707: return new TerrainRule(TerrainRole.BlockingLine, 0f);
                }
                if (impassable && (m == 410)) return new TerrainRule(TerrainRole.BlockingLine, 0f);
                return TerrainRule.None;
            }

            if (point)
            {
                if (m == 525 || m == 519 && name.Contains("grind") || m == 708) return new TerrainRule(TerrainRole.Crossing, 1.2f);   // gate, crossing point
                return TerrainRule.None;   // 206 is an ordinary boulder in the old standards
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

        // Contour interval in metres: the one the player gave, otherwise the usual one for the scale
        public static double Equidistance(MapFile map) => map.contourInterval > 0 ? map.contourInterval : TypicalInterval(LooksLikeSprint(map) ? "sprint" : "forest");

        // The most usual contour interval for a map type: sprint maps (ISSprOM) 2 m, forest maps (ISOM) 5 m.
        // Maps that are both are mostly park and town maps at 1:5000-1:10000 with 2.5 m.
        public static double TypicalInterval(string mapType) => mapType == "sprint" ? 2.0 : mapType == "both" ? 2.5 : 5.0;
    }
}
