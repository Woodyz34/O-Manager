using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Everything on the map a runner can read itself in on (stones, knolls, paths, streams ...), in metres, for the
// forest mistakes (ForestMistakes). Built once per map from the same GeoJSON files GeoJsonSpawner reads.
// Each object gets a group (same kind of thing: "a stone", "a small path") and a clarity: how easy it is to see
// and to be sure of, from 1 (a big road, a lake) down to 0.15 (a vegetation boundary).
public static class MapFeatureIndex
{
    public struct Point { public Vector2 p; public int group; public float clarity; public float unique; }
    public struct Sample { public Vector2 p; public int line; public int group; public float clarity; public bool distinct; }

    public static List<Point> points = new List<Point>();
    public static List<Sample> samples = new List<Sample>();
    public static bool Ready { get; private set; }

    // Spatial hash, 25 m buckets
    const float Bucket = 25f;
    static readonly Dictionary<long, List<int>> pointBuckets = new Dictionary<long, List<int>>();
    static readonly Dictionary<long, List<int>> sampleBuckets = new Dictionary<long, List<int>>();
    public const int LineGroupOffset = 10000;   // signature keys: points by group, lines by group + this

    // World position <-> metres (1 world unit = metresPerUnit metres, the grid's cell size times 100)
    public static float metresPerUnit = 100f;

    // ---- the kinds of objects, by ISOM code (the first three digits of the layer name) ----
    static bool Classify(string layer, bool line, out int group, out float clarity)
    {
        group = 0; clarity = 0f;
        if (string.IsNullOrEmpty(layer) || layer.Length < 3 || !int.TryParse(layer.Substring(0, 3), out int code)) return false;
        int full = 0;
        if (layer.Length >= 6) int.TryParse(layer.Substring(0, 6), out full);
        if (!line)
        {
            switch (code)
            {
                case 204: group = 204; clarity = 0.35f; return true;            // stone
                case 205: group = 205; clarity = 0.6f; return true;             // large stone
                case 206: group = 205; clarity = 0.75f; return true;            // very large stone
                case 207: group = 207; clarity = 0.5f; return true;             // group of stones
                case 109: case 110: group = 109; clarity = 0.45f; return true;  // knoll
                case 111: case 112: group = 111; clarity = 0.3f; return true;   // pit
                case 526: group = 526; clarity = full == 526300 ? 0.2f : 0.3f; return true;   // cairn
                case 312: group = 312; clarity = 0.25f; return true;            // spring
                case 524: group = 524; clarity = 0.9f; return true;             // high tower
                case 525: group = 525; clarity = 0.6f; return true;             // small tower
                case 523: group = 523; clarity = 0.55f; return true;            // ruin
                case 530: case 531: case 527: group = 531; clarity = 0.4f; return true;   // special man-made
                case 115: case 419: group = 115; clarity = 0.35f; return true;  // special feature
                case 417: case 418: group = 417; clarity = 0.35f; return true;  // distinct tree
                case 308: group = 308; clarity = 0.3f; return true;             // small marsh
                case 311: group = 311; clarity = 0.4f; return true;             // well
                case 303: group = 303; clarity = 0.4f; return true;             // waterhole
                case 203: group = 203; clarity = 0.5f; return true;             // stony pit
            }
            return false;
        }
        switch (code)
        {
            case 501: case 502: group = 501; clarity = 1.0f; return true;   // big road
            case 503: group = 503; clarity = 0.9f; return true;             // road
            case 504: group = 504; clarity = 0.9f; return true;             // vehicle track
            case 505: group = 505; clarity = 0.8f; return true;             // footpath
            case 506: group = 506; clarity = 0.6f; return true;             // small path
            case 507: group = 507; clarity = 0.35f; return true;            // less distinct path
            case 508: group = 508; clarity = 0.4f; return true;             // ride / firebreak
            case 510: group = 510; clarity = 0.9f; return true;             // power line
            case 304: group = 304; clarity = 0.8f; return true;             // stream
            case 305: group = 305; clarity = 0.5f; return true;             // small stream
            case 306: group = 306; clarity = 0.4f; return true;             // ditch
            case 309: group = 309; clarity = 0.25f; return true;            // narrow marsh
            case 513: group = 513; clarity = 0.6f; return true;             // stone wall
            case 514: group = 513; clarity = 0.4f; return true;             // ruined wall
            case 516: group = 516; clarity = 0.7f; return true;             // fence
            case 517: group = 516; clarity = 0.5f; return true;             // ruined fence
            case 518: group = 516; clarity = 0.9f; return true;             // high fence
            case 416: group = 416; clarity = 0.15f; return true;            // vegetation boundary
            case 415: group = 415; clarity = 0.45f; return true;            // cultivation boundary
            case 201: group = 201; clarity = 0.75f; return true;            // impassable cliff
            case 202: group = 202; clarity = 0.4f; return true;             // passable cliff
            case 301: group = 301; clarity = 1.0f; return true;             // lake shore
            case 105: group = 105; clarity = 0.4f; return true;             // earth bank
        }
        return false;
    }

    public struct Mapping
    {
        public int rows, cols; public float cellSize, minx, miny, maxx, maxy;
        // Same as GeoJsonSpawner's mapping, without rounding to the cell, in metres
        public Vector2 ToMetres(float x, float y, float mpu)
        {
            float u = (x - minx) / (maxx - minx), v = (y - miny) / (maxy - miny);
            float wx = u * (cols - 1) * cellSize + cellSize / 2f - cols * cellSize / 2f;
            float wy = v * (rows - 1) * cellSize + cellSize / 2f - rows * cellSize / 2f;
            return new Vector2(wx * mpu, wy * mpu);
        }
    }

    // Runs in a background thread
    public static void Build(List<byte[]> files, Mapping map, float mpu)
    {
        Ready = false;
        var pts = new List<Point>(); var smp = new List<Sample>();
        int lineId = 0;
        foreach (byte[] data in files)
        {
            JObject geo;
            using (var reader = new JsonTextReader(new StreamReader(new MemoryStream(data)))) geo = JObject.Load(reader);
            foreach (var f in (JArray)geo["features"])
            {
                string type = f["geometry"]?["type"]?.ToString();
                string layer = f["properties"]?["Layer"]?.ToString();
                JArray c = f["geometry"]?["coordinates"] as JArray;
                if (c == null) continue;
                if (type == "Point" && Classify(layer, false, out int g, out float cl))
                {
                    pts.Add(new Point { p = map.ToMetres((float)c[0], (float)c[1], mpu), group = g, clarity = cl });
                }
                else if (type == "LineString" && c.Count >= 2 && Classify(layer, true, out int lg, out float lc))
                {
                    int id = lineId++;
                    Vector2 prev = map.ToMetres((float)c[0][0], (float)c[0][1], mpu);
                    for (int i = 1; i < c.Count; i++)
                    {
                        Vector2 next = map.ToMetres((float)c[i][0], (float)c[i][1], mpu);
                        float d = Vector2.Distance(prev, next);
                        int k = Mathf.Max(1, (int)(d / 5f));
                        for (int s = 0; s < k; s++)
                            smp.Add(new Sample { p = Vector2.Lerp(prev, next, s / (float)k), line = id, group = lg, clarity = lc });
                        prev = next;
                    }
                    smp.Add(new Sample { p = prev, line = id, group = lg, clarity = lc });
                }
            }
        }
        lock (pointBuckets)
        {
            metresPerUnit = mpu;
            points = pts; samples = smp;
            Fill(pointBuckets, pts.Count, i => pts[i].p);
            Fill(sampleBuckets, smp.Count, i => smp[i].p);
            var near = new List<int>();
            // How many of the same kind within 80 m: one of five stones is hard to tell apart
            for (int i = 0; i < pts.Count; i++)
            {
                QueryPoints(pts[i].p, 80f, near);
                int same = 0;
                foreach (int j in near) if (pts[j].group == pts[i].group) same++;
                Point q = pts[i]; q.unique = 1f / Mathf.Max(1, same); pts[i] = q;
            }
            // A place on a line you can be sure of: a junction, or something clear right by it
            for (int i = 0; i < smp.Count; i++)
            {
                bool distinct = false;
                QuerySamples(smp[i].p, 12f, near);
                foreach (int j in near) if (smp[j].line != smp[i].line && smp[j].clarity >= 0.35f) { distinct = true; break; }
                if (!distinct)
                {
                    QueryPoints(smp[i].p, 12f, near);
                    foreach (int j in near) if (pts[j].clarity >= 0.35f) { distinct = true; break; }
                }
                Sample q = smp[i]; q.distinct = distinct; smp[i] = q;
            }
            readability.Clear();
        }
        Ready = true;
    }

    public static void Clear() { Ready = false; }

    static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
    static void Fill(Dictionary<long, List<int>> buckets, int n, Func<int, Vector2> pos)
    {
        buckets.Clear();
        for (int i = 0; i < n; i++)
        {
            Vector2 p = pos(i);
            long k = Key(Mathf.FloorToInt(p.x / Bucket), Mathf.FloorToInt(p.y / Bucket));
            if (!buckets.TryGetValue(k, out var list)) buckets[k] = list = new List<int>();
            list.Add(i);
        }
    }
    static void Query(Dictionary<long, List<int>> buckets, Func<int, Vector2> pos, Vector2 c, float r, List<int> result)
    {
        result.Clear();
        int x0 = Mathf.FloorToInt((c.x - r) / Bucket), x1 = Mathf.FloorToInt((c.x + r) / Bucket);
        int y0 = Mathf.FloorToInt((c.y - r) / Bucket), y1 = Mathf.FloorToInt((c.y + r) / Bucket);
        float r2 = r * r;
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (buckets.TryGetValue(Key(x, y), out var list))
                    foreach (int i in list)
                        if ((pos(i) - c).sqrMagnitude <= r2) result.Add(i);
    }
    public static void QueryPoints(Vector2 c, float r, List<int> result) => Query(pointBuckets, i => points[i].p, c, r, result);
    public static void QuerySamples(Vector2 c, float r, List<int> result) => Query(sampleBuckets, i => samples[i].p, c, r, result);

    // ---- how easy the ground round a place is to read (clear, unique things within 40 m) ----
    static readonly Dictionary<long, float> readability = new Dictionary<long, float>();
    static readonly List<int> tmp = new List<int>();
    public static float Readability(Vector2 m)
    {
        long k = Key(Mathf.FloorToInt(m.x / 10f), Mathf.FloorToInt(m.y / 10f));
        if (readability.TryGetValue(k, out float r)) return r;
        Vector2 c = new Vector2((Mathf.Floor(m.x / 10f) + 0.5f) * 10f, (Mathf.Floor(m.y / 10f) + 0.5f) * 10f);
        r = 0f;
        QueryPoints(c, 40f, tmp);
        foreach (int i in tmp) r += points[i].clarity * points[i].unique;
        var best = new Dictionary<int, float>();
        QuerySamples(c, 40f, tmp);
        foreach (int i in tmp) if (!best.TryGetValue(samples[i].line, out float b) || samples[i].clarity > b) best[samples[i].line] = samples[i].clarity;
        foreach (float b in best.Values) r += b;
        readability[k] = r;
        return r;
    }

    // A line you can follow without a compass (a path, a stream, a fence ...) within 7 m
    public static bool OnHandrail(Vector2 m)
    {
        QuerySamples(m, 7f, tmp);
        foreach (int i in tmp) if (samples[i].clarity >= 0.5f) return true;
        return false;
    }

    // What you see round a place: kinds of clear things and how many of each
    public static Dictionary<int, int> Signature(Vector2 m, float r = 35f)
    {
        var sig = new Dictionary<int, int>();
        QueryPoints(m, r, tmp);
        foreach (int i in tmp)
        {
            if (points[i].clarity < 0.3f) continue;
            sig.TryGetValue(points[i].group, out int n); sig[points[i].group] = n + 1;
        }
        var lines = new Dictionary<int, int>();
        QuerySamples(m, r, tmp);
        foreach (int i in tmp) if (samples[i].clarity >= 0.35f) lines[samples[i].line] = samples[i].group;
        foreach (int g in lines.Values)
        {
            sig.TryGetValue(g + LineGroupOffset, out int n); sig[g + LineGroupOffset] = n + 1;
        }
        return sig;
    }

    public static float Similarity(Dictionary<int, int> a, Dictionary<int, int> b)
    {
        float min = 0f, max = 0f;
        var keys = new HashSet<int>(a.Keys); keys.UnionWith(b.Keys);
        if (keys.Count == 0) return 1f;
        foreach (int k in keys)
        {
            a.TryGetValue(k, out int x); b.TryGetValue(k, out int y);
            min += Mathf.Min(x, y); max += Mathf.Max(x, y);
        }
        return max > 0f ? min / max : 1f;
    }
}
