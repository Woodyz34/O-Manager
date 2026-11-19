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
        SlowArea        // an area that slows down whatever ground it lies on (stony ground): speed is a factor
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
                        // ISSprOM 410 is impassable vegetation; ISOM 410 is very difficult (fight)
                        if (sprint || name.Contains("impassable") || name.Contains("opasserbar")) return Blocked();
                        return Ground(TerrainSpeeds.NormalDark, sprint);
                    case 411: return Ground(sprint ? 0.6f : 0.8f, sprint);                // runnable in one direction
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
                    case 520: return Blocked();                                           // area that shall not be entered
                    case 521: return Blocked();                                           // building
                    case 522: return Ground(1.4f, sprint);                                // canopy: you can run under it
                    case 709: return Blocked();                                           // out of bounds (course setting)
                    case 714: return Blocked();                                           // temporary out of bounds
                }
                if (m == 206) return Blocked();                                           // gigantic boulder
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
                    case 301: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // lake shore
                    case 307: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // uncrossable marsh edge
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
                        if (ImpassableName(name)) return new TerrainRule(TerrainRole.BlockingLine, 0f);
                        if (minor == 1 || RuinedName(name)) return new TerrainRule(TerrainRole.SlowLine, 0.8f);   // ruined wall (513.1)
                        return new TerrainRule(TerrainRole.SlowLine, 0.5f);               // stone wall: over, but slower
                    case 514: return new TerrainRule(TerrainRole.SlowLine, 0.8f);         // ruined wall
                    case 515: return new TerrainRule(TerrainRole.BlockingLine, 0f);       // impassable wall
                    case 516:
                        if (ImpassableName(name)) return new TerrainRule(TerrainRole.BlockingLine, 0f);
                        if (RuinedName(name)) return TerrainRule.None;
                        return new TerrainRule(TerrainRole.SlowLine, 0.8f);               // fence
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

        // "Impassable stone wall", "Oöverstigligt staket", "Opasserbar mur" ...
        static bool ImpassableName(string name) =>
            name.Contains("opasserb") || name.Contains("impassable") || name.Contains("uncrossable")
            || name.Contains("oöverstig") || name.Contains("ooverstig") || name.Contains("not crossable");

        static bool RuinedName(string name) => name.Contains("raser") || name.Contains("ruin");

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

        public static bool IsBuilding(MapSymbol s) => s != null && s.kind == SymbolKind.Area && (s.oldNumbers ? s.major == 526 : s.major == 521);

        // Areas nobody may enter: out of bounds, forbidden, course-setting overprint
        static bool ForbiddenName(string name) =>
            name.Contains("shall not be entered") || name.Contains("out of bounds") || name.Contains("out-of-bounds")
            || name.Contains("forbidden") || name.Contains("förbjud") || name.Contains("beträdas");

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

        static TerrainRule Stony(float factor) => new TerrainRule(TerrainRole.SlowArea, factor);

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
    // What the ground of a grid cell is, for the runners' map reading (Mistakes/MapFeatureIndex.cs): one byte per
    // cell in the package file kinds.bytes. Not the speed: a marsh and slow forest can be equally slow to run in.
    public static class GroundKind
    {
        public const byte Forest = 0, Open = 1, Cultivated = 2, RoughOpen = 3, OpenTrees = 4, LightGreen = 5, Green = 6,
            DarkGreen = 7, Marsh = 8, IndistinctMarsh = 9, UncrossableMarsh = 10, Water = 11, ShallowWater = 12, Plot = 13,
            Building = 14, Paved = 15, Stony = 16, BoulderField = 17, BareRock = 18, Sand = 19, BrokenGround = 20,
            Orchard = 21, OneWayForest = 22, Canopy = 23, OutOfBounds = 24;

        public static byte Of(MapSymbol s)
        {
            if (s == null || s.kind != SymbolKind.Area) return Forest;
            if (SymbolRules.IsBuilding(s)) return Building;
            string name = (s.name ?? "").ToLowerInvariant();
            if (s.oldNumbers && s.major == 526 && (name.Contains("skärmtak") || name.Contains("canopy"))) return Canopy;
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
                case 411: return OneWayForest;
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
