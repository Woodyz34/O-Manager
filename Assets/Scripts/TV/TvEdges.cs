using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using MapImport;

// The map's own edge lines of areas: lake and river banks, vegetation and other area boundaries, the edges of paved
// areas (2026-11-23, Anders: "follow the real lines, as for the fences and the garden outlines, for every kind of
// ground and object, so it all fits up close"). The world layer has only 2 m cells for areas; near one of these lines
// the 3D ground takes the kind of the line's side it is on, and the ground, the paving and the water are cut along
// the line. Read from the map's lines.json once per map (kept with its world layer).
public class TvEdges
{
    public readonly List<float[]> lines = new List<float[]>();     // e0, n0, e1, n1, ... in the game's map metres
    public readonly List<float[]> bounds = new List<float[]>();    // minE, minN, maxE, maxN of each line

    static readonly ConditionalWeakTable<WorldLayer, TvEdges> all = new ConditionalWeakTable<WorldLayer, TvEdges>();

    public static TvEdges Of(WorldLayer layer)
    {
        if (layer == null) return null;
        return all.TryGetValue(layer, out TvEdges e) ? e : null;
    }

    // Reads the edge lines of a map's lines.json and keeps them with its world layer (once)
    public static void Attach(WorldLayer layer, byte[] linesJson)
    {
        if (layer == null || linesJson == null || linesJson.Length == 0 || Of(layer) != null) return;
        TvEdges e;
        try { e = Read(linesJson); }
        catch (Exception) { return; }
        try { all.Add(layer, e); }
        catch (ArgumentException) { }
    }

    public static bool IsEdge(string name)
    {
        string n = name.ToLowerInvariant();
        int code = 0;
        if (n.Length >= 3) int.TryParse(n.Substring(0, 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
        bool edgeWord = n.Contains("kontur") || n.Contains("edge") || n.Contains("kant") || n.Contains("bank") || n.Contains("sidlinje")
                        || n.Contains("outline") || n.Contains("shore") || n.Contains("strand");
        // Water: the bank of a lake, pond or wide river (not a stream drawn as one line)
        if (code == 301 || code == 302)
            return edgeWord || n.Contains("sjö") || n.Contains("lake") || n.Contains("pond") || n.Contains("damm")
                   || !(n.Contains("vattendrag") || n.Contains("watercourse") || n.Contains("stream"));
        // Area boundaries: exact boundary, vegetation boundary, cultivated land boundary
        if (code == 415 || code == 416 || code == 404) return true;
        // Edges of paved areas
        if ((code == 501 || code == 523) && edgeWord && !n.Contains("footprint")) return true;
        return false;
    }

    static TvEdges Read(byte[] json)
    {
        var e = new TvEdges();
        string s = System.Text.Encoding.UTF8.GetString(json);
        int at = 0;
        while (true)
        {
            int layerAt = s.IndexOf("\"Layer\"", at, StringComparison.Ordinal);
            if (layerAt < 0) break;
            int q0 = s.IndexOf('"', s.IndexOf(':', layerAt) + 1);
            int q1 = q0 < 0 ? -1 : s.IndexOf('"', q0 + 1);
            if (q1 < 0) break;
            string name = s.Substring(q0 + 1, q1 - q0 - 1);
            int coords = s.IndexOf("\"coordinates\"", q1, StringComparison.Ordinal);
            if (coords < 0) break;
            int open = s.IndexOf('[', coords);
            int next = s.IndexOf("\"Layer\"", q1, StringComparison.Ordinal);
            at = open + 1;
            if (open < 0) break;
            if (next >= 0 && open > next) continue;
            if (!IsEdge(name)) { at = next < 0 ? s.Length : next; continue; }
            List<List<float>> parts = TvTown.Parse(s, open, out int end);
            at = end;
            foreach (List<float> part in parts)
            {
                if (part.Count < 4) continue;
                float[] xy = part.ToArray();
                float a = float.MaxValue, b = float.MaxValue, c = float.MinValue, d = float.MinValue;
                for (int i = 0; i + 1 < xy.Length; i += 2)
                {
                    a = Math.Min(a, xy[i]); b = Math.Min(b, xy[i + 1]); c = Math.Max(c, xy[i]); d = Math.Max(d, xy[i + 1]);
                }
                e.lines.Add(xy);
                e.bounds.Add(new[] { a, b, c, d });
            }
        }
        return e;
    }
}
