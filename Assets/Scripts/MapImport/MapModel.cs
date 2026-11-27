using System;
using System.Collections.Generic;

// Everything read from an orienteering map file (OCAD .ocd or OpenOrienteering Mapper .omap/.xmap), in one shape
// that the rest of the import works on. Plain C# without Unity, so it can run on a background thread.
namespace MapImport
{
    public enum SymbolKind { Point, Line, Area, Text, Other }

    public class MapColor
    {
        public string name;
        public int number;          // the file's own colour number (OCAD), or the index (OOM)
        public byte r, g, b;
        public int priority;        // 0 = drawn on top of everything else
        // Spot colours (OOM): the printing ink the colour is made of, as the ink colour's priority (-1 = none known).
        // A "0 % of an ink" colour prints nothing: with knockout it erases that ink only (white over green on a
        // tree takes the green away but leaves the yellow under it), without knockout it is not seen at all.
        public int spot = -1;
        public int maskSpot = -1;
        public bool invisible;
    }

    // One drawing element of a point symbol, a line's mid symbol or an area pattern, in mm around the symbol's origin
    public class SymbolElement
    {
        public enum ElementType { Line = 1, Area = 2, Circle = 3, Dot = 4 }
        public ElementType type;
        public int color = -1;      // priority index into MapFile.colors
        public double lineWidth;    // mm
        public double diameter;     // mm, circles and dots
        public double[] x, y;       // mm
    }

    public class LineStyle
    {
        public int color = -1;      // main line
        public double width;        // mm
        public bool roundCaps;
        // Dashes: mainLength drawn, mainGap blank. 0 gap = solid.
        public double mainLength, mainGap, endLength;
        // Double line (roads): borders and the fill between them
        public int fillColor = -1, leftColor = -1, rightColor = -1;
        public double doubleWidth, leftWidth, rightWidth;
        // Symbols along the line (tags of cliffs, dots of a boundary)
        public List<SymbolElement> midElements;
        public int midCount = 1;
        public double midDistance;  // mm between symbols in a group
        public double midSpacing;   // mm between groups (0 = use mainLength)
        // OpenOrienteering Mapper places symbols along a solid line its own way: from midEnd at each end, evenly,
        // about midSpacing apart (stair steps, fence ticks). A line too short for that gets none, or one in the
        // middle when midAtLeastOne is set.
        public bool oomMid;
        public double midEnd;
        public bool midAtLeastOne;
        public int midMinCount;
        // Dashed borders (unpaved paths): dash and gap in mm, 0 = solid
        public double borderDash, borderGap;
    }

    public class AreaStyle
    {
        public int fillColor = -1;
        public int hatchColor = -1;
        public double hatchWidth, hatchDistance;
        public double hatchAngle1, hatchAngle2;   // degrees
        public int hatchCount;                    // 0, 1 or 2 directions
        public List<SymbolElement> pattern;       // repeated symbol
        public double patternWidth, patternHeight, patternAngle;
        public bool patternShifted;
        public MapSymbol border;                  // a line symbol drawn round the area
        public int borderNumber;                  // OCAD: the border symbol's number, resolved after reading
    }

    public class MapSymbol
    {
        public int key;             // the number objects use to point at it
        public string code = "";    // as printed, e.g. "506" or "506.1"
        public int major;           // 506
        public int minor;           // 1 (from 506.1 or 506001)
        public string name = "";
        public SymbolKind kind = SymbolKind.Other;
        public bool hidden;
        public LineStyle line;
        public AreaStyle area;
        public List<SymbolElement> point;
        public List<MapSymbol> parts;   // OOM combined symbols
        public bool oldNumbers;         // the map uses the old numbering (ISOM 2000 / ISSOM 2007), see SymbolRules

        // The colour the symbol shows most of, used for priority and as a simple fallback
        public int mainColor = -1;

        // "506000 Small path", the same style of name as the layers in the old GeoJSON files
        public string LayerName => (major * 1000 + minor).ToString("000000") + " " + name;
    }

    public class MapObject
    {
        public MapSymbol symbol;
        public SymbolKind kind;
        // Corner points in mm on the paper, y up. Curves already flattened.
        // An area can have several rings: the first is the outline, the rest are holes.
        public List<double[]> parts = new List<double[]>();   // x0, y0, x1, y1, ...
        public double angle;        // degrees, counter-clockwise, for points and patterns
        public string text;
    }

    public class MapFile
    {
        public string format = "";          // "OCAD 12", "OpenOrienteering Mapper"
        public string version = "";
        public double scale = 10000;        // 1:scale
        public bool georeferenced;
        public double refEast, refNorth;    // real-world coordinates of the paper origin
        public double gridAngle;            // degrees between map north and grid north
        public int crsCode;
        public List<MapColor> colors = new List<MapColor>();          // index = priority
        public Dictionary<int, MapSymbol> symbols = new Dictionary<int, MapSymbol>();
        public List<MapObject> objects = new List<MapObject>();
        public List<string> warnings = new List<string>();
        public double contourInterval;      // metres between contours, given by the player (0 = guess from the scale)
        // The edge of the finished map (x,y pairs, paper mm), or null for all of it. See MapBoundary.
        public double[] boundary;
        // How quick the kinds of ground are on this map (the sliders at import), or null for the game's normal speeds
        public TerrainSpeeds terrain;
        // What the terrain looks like in 3D (the choices at import), or null for the normal look
        public TerrainLook look;
        // The player's answers about contours the game was unsure of (Edit map: higher, lower or as high as the one
        // next to it), five numbers each: see RunContourField.Answer. Null when none.
        public double[] heightAnswers;

        public double MetersPerMm => scale / 1000.0;

        // Paper mm to metres on the ground, measured from the paper origin with map north up.
        // The game uses these as the map's coordinates, so the map is shown the right way up.
        public void PaperToGround(double x, double y, out double e, out double n)
        {
            e = x * MetersPerMm;
            n = y * MetersPerMm;
        }

        // Paper mm to the national grid (e.g. SWEREF 99 TM), when the file says where it lies
        public void PaperToWorld(double x, double y, out double e, out double n)
        {
            double a = gridAngle * Math.PI / 180.0;
            double gx = x * MetersPerMm, gy = y * MetersPerMm;
            e = refEast + gx * Math.Cos(a) + gy * Math.Sin(a);
            n = refNorth - gx * Math.Sin(a) + gy * Math.Cos(a);
        }
    }

    // Small geometry helpers shared by the readers
    public static class MapGeometry
    {
        // Adds a cubic Bezier from (x0,y0) to (x3,y3) to a list, without the start point
        public static void AddBezier(List<double> pts, double x0, double y0, double x1, double y1,
                                     double x2, double y2, double x3, double y3)
        {
            double len = Dist(x0, y0, x1, y1) + Dist(x1, y1, x2, y2) + Dist(x2, y2, x3, y3);
            int steps = Math.Max(2, Math.Min(32, (int)(len / 0.25)));   // a point every 0.25 mm of paper
            for (int k = 1; k <= steps; k++)
            {
                double t = k / (double)steps, u = 1 - t;
                double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
                pts.Add(a * x0 + b * x1 + c * x2 + d * x3);
                pts.Add(a * y0 + b * y1 + c * y2 + d * y3);
            }
        }

        public static double Dist(double x0, double y0, double x1, double y1)
        {
            double dx = x1 - x0, dy = y1 - y0;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // Signed area of a ring, positive when it runs counter-clockwise (y up)
        public static double SignedArea(double[] p)
        {
            double s = 0;
            int n = p.Length / 2;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                s += p[2 * i] * p[2 * j + 1] - p[2 * j] * p[2 * i + 1];
            }
            return s / 2;
        }

        public static bool IsClosed(double[] p, double tolerance = 0.01)
        {
            int n = p.Length / 2;
            return n > 3 && Dist(p[0], p[1], p[2 * n - 2], p[2 * n - 1]) <= tolerance;
        }
    }
}
