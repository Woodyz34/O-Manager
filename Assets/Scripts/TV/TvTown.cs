using System;
using System.Collections.Generic;
using System.Globalization;
using MapImport;

// The town of a sprint map in the run-yourself 3D world (2026-11-20, Anders: the sprint maps as a real town, a
// Swedish one or an old stone town of southern Europe, chosen with the map's terrain look). Worked out once for the
// whole map when the race loads, so every piece of the world agrees:
//  - which house is the church and which the town hall (Anders left it to the game: the church is a long house with
//    green or open land round it, the town hall a big house (at most 1500 m²) on the largest paved square);
//  - the bridges, tunnels, stairs and canopies, read from the map's lines (lines.json), which the world layer does
//    not keep.
// Plain C#, made on a background thread.
public class TvTown
{
    public TvCityShapes.Style style;
    // A building cell of the church and of the town hall (game coordinates); NaN when the map has none
    public double churchE = double.NaN, churchN = double.NaN, hallE = double.NaN, hallN = double.NaN;
    // Lines as e0, n0, e1, n1, ... in the game's coordinates; width: metres across (0 = not given)
    public readonly List<float[]> bridges = new List<float[]>(), tunnels = new List<float[]>(), stairs = new List<float[]>();
    public readonly List<float> stairWidths = new List<float>();
    // Metres between the two side lines of each bridge and tunnel (0 = drawn as one line)
    public readonly List<float> bridgeWidths = new List<float>(), tunnelWidths = new List<float>();
    // Outlines of canopies (roofs on posts, and passages under houses), closed
    public readonly List<float[]> canopies = new List<float[]>();

    // The same town with the style of another terrain look (the look can change in Edit map)
    public TvTown WithLook(TerrainLook look)
    {
        var t = (TvTown)MemberwiseClone();
        t.style = look != null && look.town == TerrainLook.OldStone ? TvCityShapes.Style.OldStone : TvCityShapes.Style.Swedish;
        return t;
    }

    public static TvTown Make(WorldLayer layer, byte[] linesJson, TerrainLook look)
    {
        var town = new TvTown { style = look != null && look.town == TerrainLook.OldStone ? TvCityShapes.Style.OldStone : TvCityShapes.Style.Swedish };
        if (layer == null) return town;
        try { town.ReadLines(linesJson); }
        catch (Exception) { }
        try { town.StitchCanopies(); }
        catch (Exception) { }
        try
        {
            PairSides(town.bridges, town.bridgeWidths);
            PairSides(town.tunnels, town.tunnelWidths);
        }
        catch (Exception) { }
        try { town.Landmarks(layer); }
        catch (Exception) { }
        return town;
    }

    // ---- The map's lines ----

    void ReadLines(byte[] json)
    {
        if (json == null || json.Length == 0) return;
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
            int kind = Kind(name);
            if (kind == 0) { at = next < 0 ? s.Length : next; continue; }
            List<List<float>> parts = Parse(s, open, out int end);
            at = end;
            foreach (List<float> part in parts)
            {
                if (part.Count < 4) continue;
                float[] xy = part.ToArray();
                switch (kind)
                {
                    case 1: bridges.Add(xy); break;
                    case 2: tunnels.Add(xy); break;
                    case 3: stairs.Add(xy); stairWidths.Add(Footprint(name)); break;
                    case 4:
                        if (xy.Length >= 8 && Math.Abs(xy[0] - xy[xy.Length - 2]) < 0.6f && Math.Abs(xy[1] - xy[xy.Length - 1]) < 0.6f) canopies.Add(xy);
                        else canopyPieces.Add(xy);
                        break;
                }
            }
        }
    }

    // Pieces of canopy outlines that are not closed on their own (2026-11-20: a map often draws one canopy as several
    // lines, and those canopies had no roof)
    readonly List<float[]> canopyPieces = new List<float[]>();

    // Joins the pieces end to end (ends within 0.6 m) into closed outlines
    void StitchCanopies()
    {
        var left = new List<List<float>>();
        foreach (float[] p in canopyPieces) left.Add(new List<float>(p));
        canopyPieces.Clear();
        bool Near(float ax, float az, float bx, float bz) => Math.Abs(ax - bx) < 0.6f && Math.Abs(az - bz) < 0.6f;
        while (left.Count > 0)
        {
            List<float> ring = left[left.Count - 1];
            left.RemoveAt(left.Count - 1);
            bool grew = true;
            while (grew && !(ring.Count >= 8 && Near(ring[0], ring[1], ring[ring.Count - 2], ring[ring.Count - 1])))
            {
                grew = false;
                float ex = ring[ring.Count - 2], ez = ring[ring.Count - 1];
                for (int k = 0; k < left.Count; k++)
                {
                    List<float> q = left[k];
                    bool head = Near(ex, ez, q[0], q[1]), tail = Near(ex, ez, q[q.Count - 2], q[q.Count - 1]);
                    if (!head && !tail) continue;
                    if (tail)
                    {
                        // Turn it round
                        var r = new List<float>(q.Count);
                        for (int i = q.Count - 2; i >= 0; i -= 2) { r.Add(q[i]); r.Add(q[i + 1]); }
                        q = r;
                    }
                    ring.AddRange(q.GetRange(2, q.Count - 2));
                    left.RemoveAt(k);
                    grew = true;
                    break;
                }
            }
            if (ring.Count >= 8 && Near(ring[0], ring[1], ring[ring.Count - 2], ring[ring.Count - 1])) canopies.Add(ring.ToArray());
        }
    }

    // A bridge or a tunnel is drawn as its two side lines (ISSprOM 512.1/512.2). Each pair becomes one line down the
    // middle, with the metres between the sides as its width (2026-11-20, Anders: a bridge was two narrow decks with
    // water between them, and nobody got over it).
    static void PairSides(List<float[]> lines, List<float> widths)
    {
        var src = new List<float[]>(lines);
        lines.Clear();
        widths.Clear();
        var used = new bool[src.Count];
        for (int i = 0; i < src.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            float[] a = src[i];
            float ax1 = a[0], az1 = a[1], ax2 = a[a.Length - 2], az2 = a[a.Length - 1];
            float alen = (float)Math.Sqrt((ax2 - ax1) * (ax2 - ax1) + (az2 - az1) * (az2 - az1));
            if (alen < 0.5f) continue;
            float ux = (ax2 - ax1) / alen, uz = (az2 - az1) / alen;
            float amx = (ax1 + ax2) / 2f, amz = (az1 + az2) / 2f;
            int best = -1;
            float bestGap = 12f;
            for (int j = 0; j < src.Count; j++)
            {
                if (used[j]) continue;
                float[] b = src[j];
                float bx1 = b[0], bz1 = b[1], bx2 = b[b.Length - 2], bz2 = b[b.Length - 1];
                float blen = (float)Math.Sqrt((bx2 - bx1) * (bx2 - bx1) + (bz2 - bz1) * (bz2 - bz1));
                if (blen < 0.5f) continue;
                // Running the same way, side by side: across, not along
                if (Math.Abs(ux * (bx2 - bx1) / blen + uz * (bz2 - bz1) / blen) < 0.8f) continue;
                float dx = (bx1 + bx2) / 2f - amx, dz = (bz1 + bz2) / 2f - amz;
                float alongGap = Math.Abs(dx * ux + dz * uz), across = Math.Abs(-dx * uz + dz * ux);
                if (alongGap > Math.Max(alen, blen) * 0.6f + 1f) continue;
                if (across < 1.2f || across > Math.Min(12f, Math.Max(4f, Math.Max(alen, blen) * 1.2f))) continue;
                if (across < bestGap) { bestGap = across; best = j; }
            }
            if (best < 0)
            {
                lines.Add(a);
                widths.Add(0f);
                continue;
            }
            used[best] = true;
            float[] o = src[best];
            float ox1 = o[0], oz1 = o[1], ox2 = o[o.Length - 2], oz2 = o[o.Length - 1];
            if ((ox2 - ox1) * ux + (oz2 - oz1) * uz < 0f) { float t = ox1; ox1 = ox2; ox2 = t; t = oz1; oz1 = oz2; oz2 = t; }
            // The ends: where the shorter side ends, so the way does not run out over the water past the bridge
            lines.Add(new[] { (ax1 + ox1) / 2f, (az1 + oz1) / 2f, (ax2 + ox2) / 2f, (az2 + oz2) / 2f });
            widths.Add(bestGap);
        }
    }

    // 1 bridge, 2 tunnel or underpass, 3 stairs, 4 canopy outline, 0 anything else
    static int Kind(string name)
    {
        string n = name.ToLowerInvariant();
        int code = 0;
        if (n.Length >= 3) int.TryParse(n.Substring(0, 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
        if (n.Contains("tunnel") || n.Contains("underpass") || n.Contains("genomgång")) return 2;
        if (code == 512 || n.Contains("bridge") || n.Contains("spång") || n.StartsWith("bro ") || n.Contains(" bro ") || n.Contains(" bro/")) return 1;
        if (n.Contains("skärmtak") || n.Contains("canopy")) return 4;
        if ((code == 532 && (n.Contains("stair") || n.Contains("trapp"))) || (n.Contains("trappa") && !n.Contains("kontur")) || (n.Contains("stairway") && !n.Contains("edge")))
            return 3;
        return 0;
    }

    // "Stairway, footprint: 1.6 m" -> 1.6
    static float Footprint(string name)
    {
        int colon = name.LastIndexOf(':');
        if (colon < 0) return 0f;
        string rest = name.Substring(colon + 1).Replace("m", "").Trim().Replace(',', '.');
        return float.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out float w) && w > 0.5f && w < 20f ? w : 0f;
    }

    // The arrays of [x, y] pairs from an opening bracket: a LineString's points, or each line of a MultiLineString
    static List<List<float>> Parse(string s, int open, out int end)
    {
        var parts = new List<List<float>>();
        List<float> current = null;
        int depth = 0, i = open;
        var number = new System.Text.StringBuilder();
        var pair = new List<float>(2);
        int pointDepth = -1;
        for (; i < s.Length; i++)
        {
            char ch = s[i];
            if (ch == '[')
            {
                depth++;
                pair.Clear();
            }
            else if (ch == ']' || ch == ',')
            {
                if (number.Length > 0)
                {
                    if (float.TryParse(number.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) pair.Add(v);
                    number.Clear();
                    pointDepth = depth;
                }
                if (ch == ']')
                {
                    if (depth == pointDepth && pair.Count >= 2)
                    {
                        if (current == null) { current = new List<float>(); parts.Add(current); }
                        current.Add(pair[0]);
                        current.Add(pair[1]);
                        pair.Clear();
                    }
                    else if (depth == pointDepth - 1) current = null;   // a line of points ended
                    depth--;
                    if (depth == 0) { i++; break; }
                }
            }
            else if ((ch >= '0' && ch <= '9') || ch == '-' || ch == '.' || ch == 'e' || ch == 'E' || ch == '+') number.Append(ch);
        }
        end = i;
        return parts;
    }

    // ---- The church and the town hall ----

    void Landmarks(WorldLayer layer)
    {
        int cols = layer.cols, rows = layer.rows;
        byte[] cells = layer.cells;
        float cell = layer.cell;
        var seen = new bool[cells.Length];
        var stack = new Stack<int>();
        var groups = new List<(int first, int count, float cx, float cz, float len, float wid, float fill)>();
        var members = new List<int>();
        for (int start = 0; start < cells.Length; start++)
        {
            if (seen[start] || (cells[start] & WorldCell.BaseMask) != WorldCell.Building) continue;
            members.Clear();
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                members.Add(k);
                int c = k % cols, r = k / cols;
                for (int d = 0; d < 4; d++)
                {
                    int nc = c + (d == 0 ? 1 : d == 1 ? -1 : 0), nr = r + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (nc < 0 || nr < 0 || nc >= cols || nr >= rows) continue;
                    int nk = nr * cols + nc;
                    if (!seen[nk] && (cells[nk] & WorldCell.BaseMask) == WorldCell.Building) { seen[nk] = true; stack.Push(nk); }
                }
            }
            if (members.Count < 8) continue;
            Shape(members, cols, cell, out float cx, out float cz, out float len, out float wid, out float fill);
            groups.Add((start, members.Count, cx, cz, len, wid, fill));
        }
        if (groups.Count < 6) return;

        // What lies round each house: paved squares and streets, or green and open land
        float Around(float cx, float cz, float reach, Func<int, bool> what)
        {
            int hits = 0, all = 0;
            for (float dz = -reach; dz <= reach; dz += 3f)
                for (float dx = -reach; dx <= reach; dx += 3f)
                {
                    if (dx * dx + dz * dz > reach * reach) continue;
                    int c = (int)((cx + dx) / cell), r = (int)((cz + dz) / cell);
                    if (c < 0 || r < 0 || c >= cols || r >= rows) continue;
                    all++;
                    if (what(cells[r * cols + c] & WorldCell.BaseMask)) hits++;
                }
            return all == 0 ? 0f : hits / (float)all;
        }
        int hall = -1, church = -1;
        float hallScore = 0f, churchScore = 0f;
        for (int g = 0; g < groups.Count; g++)
        {
            var h = groups[g];
            float area = h.count * cell * cell;
            if (area >= 300f && area <= 1500f && h.fill >= 0.72f)
            {
                float paved = Around(h.cx, h.cz, 22f + (float)Math.Sqrt(area) * 0.5f, k => k == WorldCell.Paved);
                float score = paved * (float)Math.Sqrt(area);
                if (score > hallScore) { hallScore = score; hall = g; }
            }
        }
        for (int g = 0; g < groups.Count; g++)
        {
            if (g == hall) continue;
            var h = groups[g];
            float area = h.count * cell * cell, ratio = h.len / Math.Max(1f, h.wid);
            if (area < 150f || area > 1600f || h.fill < 0.72f || ratio < 1.4f || ratio > 3.8f) continue;
            float green = Around(h.cx, h.cz, 25f, k => k == WorldCell.Open || k == WorldCell.OpenTrees || k == WorldCell.Plot || k == WorldCell.Forest || k == WorldCell.Cultivated);
            float paved = Around(h.cx, h.cz, 25f, k => k == WorldCell.Paved);
            float score = (green + 0.5f * paved) * (float)Math.Pow(area, 0.3);
            if (score > churchScore) { churchScore = score; church = g; }
        }
        if (hall >= 0)
        {
            int k = groups[hall].first;
            hallE = layer.minE + (k % cols + 0.5) * cell;
            hallN = layer.minN + (k / cols + 0.5) * cell;
        }
        if (church >= 0)
        {
            int k = groups[church].first;
            churchE = layer.minE + (k % cols + 0.5) * cell;
            churchN = layer.minN + (k / cols + 0.5) * cell;
        }
    }

    // The long axis of a group of cells, and how much of the rectangle along it the cells fill
    static void Shape(List<int> members, int cols, float cell, out float cx, out float cz, out float len, out float wid, out float fill)
    {
        double sx = 0, sz = 0;
        foreach (int k in members) { sx += k % cols; sz += k / cols; }
        double mx = sx / members.Count, mz = sz / members.Count;
        double xx = 0, zz = 0, xz = 0;
        foreach (int k in members)
        {
            double dx = k % cols - mx, dz = k / cols - mz;
            xx += dx * dx; zz += dz * dz; xz += dx * dz;
        }
        double a = 0.5 * Math.Atan2(2 * xz, xx - zz), ux = Math.Cos(a), uz = Math.Sin(a);
        double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
        foreach (int k in members)
        {
            double dx = k % cols - mx, dz = k / cols - mz, u = dx * ux + dz * uz, v = -dx * uz + dz * ux;
            u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
        }
        len = (float)((u1 - u0 + 1) * cell);
        wid = (float)((v1 - v0 + 1) * cell);
        if (wid > len) { float t = len; len = wid; wid = t; }
        fill = members.Count * cell * cell / Math.Max(1f, len * wid);
        cx = (float)((mx + 0.5) * cell);
        cz = (float)((mz + 0.5) * cell);
    }
}
