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
        Bridge,         // a line that is a way over water or another obstacle
        SlowArea,       // an area that slows down whatever ground it lies on (stony ground): speed is a factor
        Stairs          // a stairway: a slow way up or down, also over a cliff, a retaining wall or an impassable wall it crosses
    }

    public struct TerrainRule
    {
        public TerrainRole role;
        public float speed;              // for SlowArea: the factor the ground under it is multiplied by

        public TerrainRule(TerrainRole role, float speed = 1f)
        {
            this.role = role;
            this.speed = speed;
        }

        public static readonly TerrainRule None = new TerrainRule(TerrainRole.None, 1f);
    }

    public static class SymbolRules
    {
        // Goes up by one every time the rules below change what can be run on, so maps imported with older rules can be
        // told apart (MapPackageInfo.rulesVersion) and the player asked to build them again.
        // 2 (2026-11-20): ISOM 2017-2 / ISSprOM 2019-2 check: hedges (410.1, 411), 513.1 wall, stairs, canopies,
        // 707 marked route (not a barrier), 708 out-of-bounds boundary, 710 crossing, green minimum-width lines.
        // 3 (2026-11-20): stairs as quick as a road going up, 0.6 going down (GroundKind.Stairs, StairsSlopes).
        // 4 (2026-11-20): contour heights: hilltops no longer turned into hollows (ContourHeights), the grid's heights change.
        // 5 (2026-11-23): slope ticks keep their direction in points.json (the run-yourself ground knows knolls from
        //    hollows everywhere), and ticks drawn as a point of 101.0 itself count for the grid's heights too.
        // 6 (2026-11-23): the world layer keeps the outlines of gardens and out-of-bounds areas, so the 3D town's garden
        //    edges (hedges, walls, fences) run straight along them instead of the zigzag of the 2 m cells.
        // 7 (2026-11-24): forest maps: a road close beside a cliff, fence or shore it does not cross is no longer cut
        //    off by it (GridBuilder.OpenPathsAlongLines; the road over the water at Delsjön).
        public const int RulesVersion = 9;   // 8 (2026-11-26): small buildings and ruins drawn as points; 9 (2026-11-26): forest buildings drawn as areas keep their outline (3D houses)

        // Stairs: up (and on the flat) as quick as a road (Anders, 2026-11-20: running up stairs is quick), down slowly.
        // Which way is down is worked out from the heights when the grid is loaded (StairsSlopes).
        public const float StairsSpeed = 1.35f;
        public const float StairsDownSpeed = 0.6f;
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

        // The rule for a symbol with the map's terrain sliders (see TerrainSpeeds) applied; null = every slider in the middle
        public static TerrainRule For(MapSymbol s, bool sprint, TerrainSpeeds speeds = null)
        {
            TerrainRule rule = Normal(s, sprint);
            if (s == null || rule.role == TerrainRole.None || rule.role == TerrainRole.Blocked) return rule;
            if (speeds == null) speeds = TerrainSpeeds.Default;
            TerrainKind kind = KindOf(s);
            if (kind == TerrainKind.Other) return rule;
            switch (rule.role)
            {
                case TerrainRole.Ground: rule.speed = Math.Max(0f, speeds.Ground(kind, rule.speed)); break;
                case TerrainRole.Path: rule.speed = speeds.Line(kind, rule.speed); break;
                case TerrainRole.SlowArea: rule.speed = speeds.StonyFactor(rule.speed); break;
            }
            return rule;
        }

        // Which slider a symbol belongs to
        public static TerrainKind KindOf(MapSymbol s)
        {
            if (s == null) return TerrainKind.Other;
            int m = s.major;
            if (s.kind == SymbolKind.Area)
            {
                switch (m)
                {
                    case 405: case 413: case 414: return TerrainKind.Forest;
                    case 406: case 407: return TerrainKind.LightGreen;
                    case 408: case 409: case 411: return TerrainKind.MidGreen;
                    case 410: return TerrainKind.DarkGreen;
                    case 310: return TerrainKind.Marsh;
                    case 401: case 402: case 412: return TerrainKind.Open;
                    case 403: case 404: return TerrainKind.RoughOpen;
                    case 208: case 210: return TerrainKind.Stony;
                }
                if (s.oldNumbers)
                {
                    if (m == 308 || m == 309 || m == 311) return TerrainKind.Marsh;
                    if (m == 415) return TerrainKind.Open;
                    if (m == 529) return TerrainKind.Paved;
                }
                else
                {
                    if (m == 308) return TerrainKind.Marsh;
                    if (m == 501 || m == 503) return TerrainKind.Paved;
                    if (m == 209 || m == 211 || m == 212) return TerrainKind.Stony;
                }
                return TerrainKind.Other;
            }
            if (s.kind == SymbolKind.Line)
            {
                if (s.oldNumbers)
                {
                    if (m >= 501 && m <= 505) return TerrainKind.Road;
                    if (m >= 506 && m <= 509) return TerrainKind.Path;
                }
                else
                {
                    if (m >= 502 && m <= 504) return TerrainKind.Road;
                    if (m >= 505 && m <= 508) return TerrainKind.Path;
                }
            }
            return TerrainKind.Other;
        }

        // The rule with every slider in the middle. Normal speeds (2026-11-13, the player's order, quickest first):
        // asphalt 150 %, roads 140 % (vehicle tracks 135 %), small paths 120 %, open land 115 % (grass on a sprint map
        // 130 %), white forest 100 %, rough open land / felled areas (hygge) 80 %, marsh 80 %, light green 70 %, mid green 55 %,
        // dark green 35 %; stony ground 20 % slower than the ground under it.
        static TerrainRule Normal(MapSymbol s, bool sprint)
        {
            if (s == null) return TerrainRule.None;
            if (s.oldNumbers) return ForOld(s, sprint);
            string name = (s.name ?? "").ToLowerInvariant();
            int m = s.major, minor = s.minor;
            bool area = s.kind == SymbolKind.Area;
            bool line = s.kind == SymbolKind.Line;
            bool point = s.kind == SymbolKind.Point;
            // A multi-level map's tag line (the edge of a lower level, triangles pointing out of it) is no bridge: as
            // one it opened a way through every wall under it. Tunnels closes it when the grid is loaded.
            if (line && MultiLevelTag(name)) return TerrainRule.None;

            if (area)
            {
                switch (m)
                {
                    case 301: return Blocked();                                           // uncrossable water
                    case 302: return Ground(sprint ? 0.4f : 0.5f, sprint);                 // shallow water: may be waded (blue edge; a black edge is a line of its own)
                    case 307: return Blocked();                                           // uncrossable marsh
                    case 308: return Ground(0.8f, sprint);                                // marsh
                    case 310: return Ground(0.9f, sprint);                                // indistinct marsh
                    case 401: return Ground(sprint ? 1.3f : 1.15f, sprint);                            // open land
                    case 402: return Ground(sprint ? 1.2f : 1.1f, sprint);                             // open land, scattered trees
                    case 403: return Ground(0.8f, sprint);                                // rough open land (hygge)
                    case 404: return Ground(0.75f, sprint);                               // rough open, scattered trees
                    case 405: return Ground(1.0f, sprint);                                // forest
                    case 406: return Ground(0.7f, sprint);                                // slow running
                    case 407: return Ground(0.7f, sprint);                                // slow running, good visibility
                    case 408: return Ground(0.55f, sprint);                               // difficult to run
                    case 409: return Ground(0.55f, sprint);                               // walk, good visibility
                    case 410:
                        // ISSprOM 410 is impassable vegetation; ISOM 2017-2 410 is "Vegetation: fight" (passable, very slow)
                        if (sprint || ImpassableName(name)) return Blocked();
                        return Ground(TerrainSpeeds.NormalDark, sprint);
                    case 411:
                        // Not in ISOM 2017-2. The Swedish symbol sets use 411 for impassable vegetation / hedges
                        // (2026-11-20, a player: "411 is impassable"); only a name that says one direction is runnable.
                        if (OneWayName(name) && !ImpassableName(name)) return Ground(sprint ? 0.6f : 0.8f, sprint);
                        return Blocked();
                    case 412: return Ground(sprint ? 1.3f : 1.15f, sprint);                            // cultivated land
                    case 413: return Ground(1.0f, sprint);                                // orchard
                    case 414: return Ground(1.0f, sprint);                                // vineyard
                    case 113: return Ground(0.9f, sprint);                                // broken ground
                    case 114: return Ground(0.75f, sprint);                               // very broken ground
                    // Stony ground and boulder fields slow down the ground they lie on (stony ground in dark green is
                    // slower still), they don't replace it
                    case 208: return Stony(0.8f);                                         // boulder field
                    case 209: return Stony(0.6f);                                         // dense boulder field
                    case 210: return Stony(0.8f) ;                                        // stony ground, slow running
                    case 211: return Stony(0.7f);                                         // stony ground, walk
                    case 212: return Stony(0.5f);                                         // stony ground, fight
                    case 213: return Ground(0.85f, sprint);                               // sandy ground
                    case 214: return Ground(1.2f, sprint);                                // bare rock
                    case 501:
                        if (name.Contains("grus") || name.Contains("gravel") || name.Contains("unpaved")) return Ground(1.4f, sprint);
                        return Ground(1.5f, sprint);                                      // paved area
                    case 503: return Ground(1.4f, sprint);                                // meeting places, squares
                    case 520: return Blocked();                                           // area that shall not be entered (olive green)
                    case 521:
                        // Building; some symbol sets put the canopy under the building number (521.1 "Skärmtak")
                        if (CanopyName(name)) return Ground(1.4f, sprint);
                        return Blocked();
                    case 522: return Ground(1.4f, sprint);                                // canopy: an accessible, runnable area with a roof
                    case 709: return Blocked();                                           // out-of-bounds area (course setting)
                    case 714: return Blocked();                                           // temporary construction or closed area
                }
                if (m == 206)
                {
                    // Gigantic boulder or rock pillar; some maps borrow the number for their own area (Ängaryd: "Path area")
                    if (name.Contains("path") || name.Contains("stig") || name.Contains("väg") || name.Contains("paved")) return Ground(1.4f, sprint);
                    return Blocked();
                }
                if (m == 528 || m == 529) return Blocked();
                // A map maker's own symbol (e.g. 999) for an area nobody may enter: known by its name
                if (ForbiddenName(name)) return Blocked();
                return TerrainRule.None;
            }

            if (line)
            {
                switch (m)
                {
                    case 201: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // impassable cliff
                    case 301: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // uncrossable water: bank line
                    case 304: return new TerrainRule(TerrainRole.SlowLine, 0.7f);         // crossable watercourse (over 2 m wide): wade it
                    case 307: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // uncrossable marsh edge
                    case 406: return GreenLine(name, sprint, 0.7f);                      // slow running, minimum width
                    case 407: return GreenLine(name, sprint, 0.7f);
                    case 408: return GreenLine(name, sprint, 0.55f);                     // walk, minimum width
                    case 409: return GreenLine(name, sprint, 0.55f);
                    case 410: return HedgeLine(name, sprint, false);                     // hedge / impassable vegetation, minimum width
                    case 411: return HedgeLine(name, sprint, true);
                    case 502: return Path(1.5f);                                          // wide road, cycle path
                    case 503: return Path(1.4f);                                          // road
                    case 504: return Path(1.35f);                                         // vehicle track
                    case 505: return Path(1.25f);                                         // footpath
                    case 506: return Path(1.2f);                                          // small path
                    case 507: return Path(1.1f);                                          // less distinct path
                    case 508: return Path(1.15f);                                         // narrow ride
                    case 509: return sprint ? TerrainRule.None : Path(1.3f);             // railway
                    case 512: return new TerrainRule(TerrainRole.Bridge, 1.4f);           // bridge / tunnel
                    case 513:
                        // 513.1 Wall (passable, ISOM 2017-2 and ISSprOM 2019-2), 513.2 retaining wall (seen from one side)
                        if (ImpassableName(name)) return new TerrainRule(TerrainRole.BlockingLine, 0f);
                        if (RuinedName(name)) return new TerrainRule(TerrainRole.SlowLine, 0.8f);
                        if (RetainingName(name)) return new TerrainRule(TerrainRole.SlowLine, 0.6f);
                        return new TerrainRule(TerrainRole.SlowLine, 0.5f);               // wall: over it, but slower
                    case 514: return new TerrainRule(TerrainRole.SlowLine, 0.8f);         // ruined wall
                    case 515: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // impassable wall
                    case 516:
                        if (ImpassableName(name)) return new TerrainRule(TerrainRole.BlockingLine, 0f);
                        if (RuinedName(name)) return TerrainRule.None;
                        return new TerrainRule(TerrainRole.SlowLine, 0.8f);               // fence or railing
                    case 517: return TerrainRule.None;                                    // ruined fence
                    case 518: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // impassable fence or railing
                    case 520:
                        // The edge of an area that shall not be entered: the area closes the ground itself
                        return TerrainRule.None;
                    case 521:
                        if (CanopyName(name)) return TerrainRule.None;                    // canopy outline: run under it
                        return new TerrainRule(TerrainRole.BlockingLine, 0f);             // building outline
                    case 522: return TerrainRule.None;                                    // canopy outline
                    case 528: return new TerrainRule(TerrainRole.SlowLine, 0.8f);         // prominent line feature (a low pipeline): step over
                    case 529: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // prominent impassable line feature
                    case 532: return Stairs();                                            // stairway
                    case 534: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // impassable pipeline
                    case 707: return TerrainRule.None;                                    // marked route (ISOM 2017-2): part of the course
                    case 708: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // out-of-bounds boundary: shall not be crossed
                    case 710: return new TerrainRule(TerrainRole.Bridge, 1.2f);           // crossing point (through a wall, fence, out-of-bounds area)
                }
                // Paved paths drawn as lines (ISSprOM "footprint" lines) and road edge lines
                if (m == 501 && (name.Contains("footprint") || name.Contains("sidlinje") || name.Contains("road edge"))) return Path(1.45f);
                if (StairsName(name)) return Stairs();
                return TerrainRule.None;
            }

            if (point)
            {
                // crossing point, footbridge, the course setter's crossing point (ISSprOM 710.1; 708 in ISOM 2000 sets)
                if (m == 519 || m == 512 || m == 708 || m == 710) return new TerrainRule(TerrainRole.Crossing, 1.2f);
                if (m == 206) return Blocked();
                return TerrainRule.None;
            }
            return TerrainRule.None;
        }

        // Multi-level tags (2026-11-25): ISSprOM 2019-2 has no symbol for the entrance to a lower level, so maps that
        // show several levels draw their own line of small triangles: "Ingresso multilivello", "Multi-level entrance" ...
        public static bool MultiLevelTag(string lowerName) =>
            lowerName != null && (lowerName.Contains("multiliv") || lowerName.Contains("multi-liv") || lowerName.Contains("multi liv")
            || lowerName.Contains("multi-level") || lowerName.Contains("multilevel") || lowerName.Contains("multi level")
            || lowerName.Contains("flernivå") || lowerName.Contains("fleirnivå") || lowerName.Contains("flere nivå")
            || lowerName.Contains("level entrance") || lowerName.Contains("ingresso") || lowerName.Contains("nivåinngang")
            || lowerName.Contains("nivåingång"));

        // "Impassable stone wall", "Oöverstigligt staket", "Opasserbar mur", "Ogenomtränglig vegetation" ...
        static bool ImpassableName(string name) =>
            name.Contains("opasserb") || name.Contains("impassable") || name.Contains("uncrossable")
            || name.Contains("oöverstig") || name.Contains("ooverstig") || name.Contains("not crossable")
            || name.Contains("ogenomträng") || name.Contains("ogenomtrang") || name.Contains("ej passerb")
            || name.Contains("inte passerb") || name.Contains("not passable");

        static bool RuinedName(string name) => name.Contains("raser") || name.Contains("ruin");

        static bool RetainingName(string name) => name.Contains("stödmur") || name.Contains("stodmur") || name.Contains("retain");

        // Canopy: "Skärmtak", "Canopy", a roof on posts or a passage under a house
        public static bool CanopyName(string name) =>
            name != null && (name.Contains("skärmtak") || name.Contains("skarmtak") || name.Contains("canopy") || name.Contains("överbyggnad")
            || name.Contains("roof") || name.Contains("tak (") || name.EndsWith(" tak"));

        // "Trappa", "Stairway", "Stairs" (not the outline of a paved area drawn together with the stairs)
        static bool StairsName(string name) =>
            (name.Contains("trapp") || name.Contains("stair")) && !name.Contains("kontur") && !name.Contains("edge") && !name.Contains("outline");

        static bool OneWayName(string name) => name.Contains("one direction") || name.Contains("en riktning") || name.Contains("ena riktning");

        static bool WindfallName(string name) => name.Contains("vindfäll") || name.Contains("vindfall") || name.Contains("windfall") || name.Contains("fallen tree");

        static bool HedgeName(string name) => name.Contains("häck") || name.Contains("hack ") || name.Contains("hedge");

        // Green drawn as a line (minimum width): as slow to cross as the area. Windfalls and open lanes are lines in the
        // green numbers too.
        static TerrainRule GreenLine(string name, bool sprint, float speed)
        {
            if (WindfallName(name)) return new TerrainRule(TerrainRole.SlowLine, 0.6f);
            if (name.Contains("öppen gata") || name.Contains("open lane") || name.Contains("runnable lane")) return Path(1.0f);
            return new TerrainRule(TerrainRole.SlowLine, speed);
        }

        // Vegetation 410/411 drawn as a line: a hedge (ISSprOM 410.1 "Impassable vegetation, minimum width (hedge)"),
        // impassable on sprint maps and whenever the name says so; "Svårpasserbar häck" on a forest map is a fight
        // through it. 411 is the impassable one in the Swedish sets. A windfall or an open lane is runnable.
        static TerrainRule HedgeLine(string name, bool sprint, bool is411)
        {
            if (WindfallName(name)) return new TerrainRule(TerrainRole.SlowLine, 0.6f);
            if (name.Contains("öppen gata") || name.Contains("open lane") || name.Contains("runnable lane")) return Path(1.0f);
            if (is411 || sprint || ImpassableName(name)) return new TerrainRule(TerrainRole.BlockingLine, 0f);
            if (HedgeName(name) || name.Contains("svårpasserb") || name.Contains("fight") || name.Contains("mycket svår")) return new TerrainRule(TerrainRole.SlowLine, 0.35f);
            return new TerrainRule(TerrainRole.SlowLine, 0.35f);
        }

        // The ISOM 2017 number of a symbol. Maps drawn with the old numbering (ISOM 2000, and the line and point
        // symbols ISSOM 2007 shares with it) are translated, so the game's map reading sees every map the same way.
        public static int Isom2017Major(MapSymbol s)
        {
            if (s == null) return 0;
            if (!s.oldNumbers) return s.major;
            string name = (s.name ?? "").ToLowerInvariant();
            switch (s.major)
            {
                case 104: return 101;   // slope line
                case 106: return 104;   // earth bank
                case 107: return 105;   // earth wall
                case 108: return 106;   // small earth wall
                case 109: return 107;   // erosion gully
                case 110: return 108;   // small erosion gully
                case 111: return 101;   // knoll drawn with a contour
                case 112: return 109;   // small knoll
                case 113: return 110;   // elongated knoll
                case 114: return 101;   // depression drawn with a contour
                case 115: return 111;   // small depression
                case 116: return 112;   // pit
                case 117: return 113;   // broken ground
                case 118: return 115;   // special land form feature
                case 202: return 206;   // rock pillars / cliffs
                case 203: return 202;   // passable rock face
                case 204: case 205: return 203;   // rocky pit, cave
                case 206: return 204;   // boulder
                case 207: return 205;   // large boulder
                case 208: return 208;   // boulder field
                case 209: return 207;   // boulder cluster
                case 210: return 210;   // stony ground
                case 211: return 213;   // open sandy ground
                case 212: return 214;   // bare rock
                case 302: return 301;   // pond
                case 304: return name.Contains("stream") || name.Contains("bäck") || name.Contains("watercourse") && !name.Contains("uncrossable") ? 304 : 301;
                case 305: return 304;   // crossable watercourse
                case 306: return 305;   // crossable small watercourse
                case 307: return 306;   // minor water channel
                case 308: return 309;   // narrow marsh
                case 309: return name.Contains("opasserb") || name.Contains("uncrossable") || name.Contains("impassable") ? 307 : 308;
                case 310: return 308;   // marsh
                case 311: return 310;   // indistinct marsh
                case 312: return 311;   // well
                case 313: return 312;   // spring
                case 314: return 313;   // special water feature
                case 414: return 415;   // distinct cultivation boundary
                case 415: return 412;   // cultivated land
                case 418: return 417;   // special vegetation features
                case 419: return 418;
                case 420: return 419;
                case 421: return 410;   // impassable vegetation (ISSOM)
                case 501: return 502;   // motorway
                case 504: return 503;   // road
                case 505: return 504;   // vehicle track
                case 506: return 505;   // footpath
                case 507: return 506;   // small path
                case 508: return 507;   // less distinct small path
                case 509: return 508;   // narrow ride
                case 513: case 514: return 519;   // crossing points
                case 515: return name.Contains("mur") || name.Contains("wall") ? 515 : 509;   // railway (high wall on some maps)
                case 516: return 510;   // power line
                case 517: return 511;   // major power line
                case 518: return name.Contains("tunnel") || name.Contains("genomgång") ? 512 : 518;
                case 519: return 513;   // stone wall
                case 520: return 514;   // ruined stone wall
                case 521: return 515;   // high stone wall
                case 522: return 516;   // fence
                case 523: return 517;   // ruined fence
                case 524: return 518;   // high fence
                case 525: return 519;   // crossing point
                case 526: return 521;   // building
                case 527: case 528: return 520;   // settlement, permanently out of bounds
                case 529: return 501;   // paved area
                case 530: return 523;   // ruin
                case 531: return 520;   // firing range
                case 532: return 531;   // grave
                case 533: return 528;   // crossable pipeline
                case 534: return 529;   // uncrossable pipeline
                case 535: return 524;   // high tower
                case 536: return 525;   // small tower
                case 537: return 526;   // cairn
                case 538: return 527;   // fodder rack
                case 539: return 530;   // special man-made features
                case 540: return 531;
            }
            return s.major;
        }

        public static bool IsBuilding(MapSymbol s) => s != null && s.kind == SymbolKind.Area && (s.oldNumbers ? s.major == 526 : s.major == 521)
            && !CanopyName((s.name ?? "").ToLowerInvariant());

        // Areas nobody may enter: out of bounds, forbidden, course-setting overprint
        static bool ForbiddenName(string name) =>
            name.Contains("shall not be entered") || name.Contains("out of bounds") || name.Contains("out-of-bounds")
            || name.Contains("forbidden") || name.Contains("förbjud") || name.Contains("beträdas") || name.Contains("not be entered")
            || name.Contains("tomtmark") || name.Contains("private") || name.Contains("closed area") || name.Contains("avspärr");

        // An area symbol of the map maker's own whose name says nobody may enter it
        public static bool IsForbiddenArea(MapSymbol s) => s != null && ForbiddenName((s.name ?? "").ToLowerInvariant());

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
            bool impassable = ImpassableName(name) || name.Contains("not be entered");
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
                    case 208: return Stony(0.8f);                                         // boulder field
                    case 210: return Stony(0.8f) ;                                        // stony ground
                    case 211: return Ground(0.85f, sprint);                               // open sandy ground
                    case 212: return Ground(1.2f, sprint);                                // bare rock
                    case 401: return Ground(sprint ? 1.3f : 1.15f, sprint);
                    case 402: return Ground(sprint ? 1.2f : 1.1f, sprint);
                    case 403: return Ground(0.8f, sprint);
                    case 404: return Ground(0.75f, sprint);
                    case 405: return Ground(1.0f, sprint);
                    case 406: return Ground(0.7f, sprint);
                    case 407: return Ground(0.7f, sprint);
                    case 408: return Ground(0.55f, sprint);
                    case 409: return Ground(0.55f, sprint);
                    case 410: return impassable ? Blocked() : Ground(0.4f, sprint);       // vegetation, very difficult
                    case 411: return Ground(0.8f, sprint);
                    case 412: case 415: return Ground(sprint ? 1.3f : 1.15f, sprint);                    // cultivated land
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
                if (ForbiddenName(name)) return Blocked();
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
                    case 501: return Path(1.5f);                                          // motorway (ISOM 2000)
                    case 502: return Path(1.5f);                                          // major road
                    case 503: return Path(1.4f);                                          // minor road
                    case 504: return Path(1.4f);                                          // road
                    case 505: return Path(1.35f);                                         // vehicle track
                    case 506: return Path(1.25f);                                         // footpath
                    case 507: return Path(1.2f);                                          // small path
                    case 508: return Path(1.1f);                                          // less distinct path
                    case 509: return Path(1.15f);                                         // narrow ride
                    case 512: return new TerrainRule(TerrainRole.Bridge, 1.4f);           // bridge
                    case 513: case 519:                                                   // stone wall (ISOM 2000 513, ISSOM 519)
                        if (impassable) return new TerrainRule(TerrainRole.BlockingLine, 0f);
                        return new TerrainRule(TerrainRole.SlowLine, 0.5f);
                    case 514: return new TerrainRule(TerrainRole.SlowLine, 0.8f);         // ruined wall
                    case 515:                                                             // high wall (ISOM 2000) / railway (ISSOM)
                        return impassable || name.Contains("mur") || name.Contains("wall") ? new TerrainRule(TerrainRole.BlockingLine, 0f) : TerrainRule.None;
                    case 516: case 522:                                                   // fence, railing
                        if (impassable) return new TerrainRule(TerrainRole.BlockingLine, 0f);
                        return new TerrainRule(TerrainRole.SlowLine, 0.8f);
                    case 518:                                                             // high fence (ISOM 2000) / tunnel (ISSOM)
                        return name.Contains("tunnel") || name.Contains("genomgång") ? TerrainRule.None : new TerrainRule(TerrainRole.BlockingLine, 0f);
                    case 521: case 524: return new TerrainRule(TerrainRole.BlockingLine, 0f); // impassable wall, impassable fence
                    case 526: return canopy ? TerrainRule.None : new TerrainRule(TerrainRole.BlockingLine, 0f); // building outline
                    case 527: case 528: return new TerrainRule(TerrainRole.BlockingLine, 0f);   // out of bounds edge
                    case 529:                                                             // paved paths; paved area edges and stairs
                        if (StairsName(name)) return Stairs();
                        if (name.Contains("stig") || name.Contains("path") || name.Contains("väg") || name.Contains("road")) return Path(1.45f);
                        return TerrainRule.None;
                    case 533: case 534: return impassable ? new TerrainRule(TerrainRole.BlockingLine, 0f) : TerrainRule.None; // pipeline
                    case 707: return new TerrainRule(TerrainRole.BlockingLine, 0f);
                }
                if (m == 410 || m == 411) return HedgeLine(name, false, false);   // vegetation lines: impassable by name, else a fight
                if (StairsName(name)) return Stairs();
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

        static TerrainRule Stony(float factor) => new TerrainRule(TerrainRole.SlowArea, factor);

        static TerrainRule Path(float speed) => new TerrainRule(TerrainRole.Path, speed);

        static TerrainRule Stairs() => new TerrainRule(TerrainRole.Stairs, StairsSpeed);

        // Contours and slope lines used for heights
        public static bool IsContour(MapSymbol s) => s != null && s.kind == SymbolKind.Line && (s.major == 101 || s.major == 102);
        public static bool IsIndexContour(MapSymbol s) => s != null && s.major == 102 && s.kind == SymbolKind.Line;
        // A point symbol of a contour is its slope tick, also when the map draws it as 101.0 itself (Stöeryd, Ängaryd)
        public static bool IsSlopeTick(MapSymbol s) => s != null && s.kind == SymbolKind.Point && (s.major == 101 || s.major == 102);
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
    // What the ground of a grid cell is, for the runners' map reading (Mistakes/MapFeatureIndex.cs): one byte per
    // cell in the package file kinds.bytes. Not the speed: a marsh and slow forest can be equally slow to run in.
    public static class GroundKind
    {
        public const byte Forest = 0, Open = 1, Cultivated = 2, RoughOpen = 3, OpenTrees = 4, LightGreen = 5, Green = 6,
            DarkGreen = 7, Marsh = 8, IndistinctMarsh = 9, UncrossableMarsh = 10, Water = 11, ShallowWater = 12, Plot = 13,
            Building = 14, Paved = 15, Stony = 16, BoulderField = 17, BareRock = 18, Sand = 19, BrokenGround = 20,
            Orchard = 21, OneWayForest = 22, Canopy = 23, OutOfBounds = 24, Stairs = 25;

        public static byte Of(MapSymbol s)
        {
            if (s == null || s.kind != SymbolKind.Area) return Forest;
            if (SymbolRules.IsBuilding(s)) return Building;
            string name = (s.name ?? "").ToLowerInvariant();
            if ((s.oldNumbers ? s.major == 526 : s.major == 521) && SymbolRules.CanopyName(name)) return Canopy;
            switch (SymbolRules.Isom2017Major(s))
            {
                case 113: case 114: return BrokenGround;
                case 206: return Building;          // a gigantic boulder: as plain to see as a house
                case 208: case 209: return BoulderField;
                case 210: case 211: case 212: return Stony;
                case 213: return Sand;
                case 214: return BareRock;
                case 301: return Water;
                case 302: return ShallowWater;
                case 307: return UncrossableMarsh;
                case 308: return Marsh;
                case 310: return IndistinctMarsh;
                case 401: return Open;
                case 402: case 404: return OpenTrees;
                case 403: return RoughOpen;
                case 406: case 407: return LightGreen;
                case 408: case 409: return Green;
                case 410: return DarkGreen;
                case 411: return name.Contains("riktning") || name.Contains("direction") ? OneWayForest : DarkGreen;
                case 412: return Cultivated;
                case 413: case 414: return Orchard;
                case 501: return Paved;
                case 520: return Plot;
                case 522: return Canopy;
                case 709: case 714: return OutOfBounds;
            }
            return Forest;
        }
    }
}
