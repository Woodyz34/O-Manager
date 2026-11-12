// Runs the game's forest mistakes on a map package outside Unity.
//   mono harness.exe <package folder> <legs.json> calib <runs per leg> [techniques]
//   mono harness.exe <package folder> <legs.json> tracks <leg,leg,...> <techniques> <out.json> [tries]
// Times are real seconds (the game runs 10 x faster; the harness converts back).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

static class Program
{
    const float Mpu = 100f, GameSpeed = 36f, RealSpeed = 3.6f;
    static float[,] speed, height;
    static int rows, cols; static float cellM;
    static MapFeatureIndex.Mapping map;
    static List<string> events = new List<string>();

    static int Main(string[] a)
    {
        var ci = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = ci;
        string pkg = a[0];
        JObject info = JObject.Parse(File.ReadAllText(Path.Combine(pkg, "map.json")));
        byte[] b = File.ReadAllBytes(Path.Combine(pkg, "grid.bytes"));
        rows = BitConverter.ToInt32(b, 28); cols = BitConverter.ToInt32(b, 32); cellM = BitConverter.ToSingle(b, 24);
        speed = new float[rows, cols]; height = new float[rows, cols];
        Buffer.BlockCopy(b, 36, speed, 0, rows * cols * 4);
        bool hasHeights = b[36 + rows * cols * 4] != 0;
        Buffer.BlockCopy(b, 37 + rows * cols * 4, height, 0, rows * cols * 4);
        map = new MapFeatureIndex.Mapping
        {
            rows = rows, cols = cols, cellSize = cellM / Mpu,
            minx = (float)info["minx"], miny = (float)info["miny"], maxx = (float)info["maxx"], maxy = (float)info["maxy"]
        };
        byte[] kinds = File.Exists(Path.Combine(pkg, "kinds.bytes")) ? File.ReadAllBytes(Path.Combine(pkg, "kinds.bytes")) : null;
        var files = new List<byte[]> { File.ReadAllBytes(Path.Combine(pkg, "points.json")), File.ReadAllBytes(Path.Combine(pkg, "lines.json")) };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        MapFeatureIndex.Build(files, map, Mpu, speed, hasHeights ? height : null, kinds);
        Console.Error.WriteLine($"index: {MapFeatureIndex.points.Count} points, {MapFeatureIndex.samples.Count} line samples, {MapFeatureIndex.areas.Count} closed edges, " +
                                $"{MapFeatureIndex.zones.Count - 1} zones ({(kinds != null ? "kinds.bytes" : "from speeds")}), {MapFeatureIndex.hills.Count} hills, {sw.ElapsedMilliseconds} ms");
        if (a.Length > 2 && a[2] == "zones")
        {
            foreach (var g in MapFeatureIndex.zones.Skip(1).GroupBy(z => z.cls).OrderBy(g => g.Key))
                Console.WriteLine($"class {g.Key}: {g.Count()} zones, {g.Sum(z => z.size)} cells");
            foreach (var g in MapFeatureIndex.samples.GroupBy(s => s.group).OrderBy(g => g.Key))
                Console.WriteLine($"line group {g.Key}: {g.Select(s => s.line).Distinct().Count()} lines, {g.Count(s => s.distinct)} distinct samples of {g.Count()}");
            foreach (var g in MapFeatureIndex.points.GroupBy(s => s.group).OrderBy(g => g.Key))
                Console.WriteLine($"point group {g.Key}: {g.Count()}");
            return 0;
        }
        JArray legs = JArray.Parse(File.ReadAllText(a[1]));
        Debug.sink = s => events.Add(s);

        if (a[2] == "calib")
        {
            int n = int.Parse(a[3]);
            int[] techs = a.Length > 4 ? a[4].Split(',').Select(int.Parse).ToArray() : new[] { 0, 40, 60, 80, 90, 99 };
            foreach (int t in techs)
            {
                var losses = new List<float>(); int mistakes = 0, timeouts = 0; float mistakeTime = 0f;
                var kinds2 = new Dictionary<string, int>();
                for (int l = 0; l < legs.Count; l++)
                    for (int k = 0; k < n; k++)
                    {
                        var r = RunLeg(legs[l], t, null);
                        if (r.Item2 <= 0f) continue;
                        if (r.Item3) timeouts++;
                        float loss = r.Item1 - r.Item2;
                        losses.Add(loss);
                        if (loss > 15f && loss > 0.25f * r.Item2) { mistakes++; mistakeTime += loss; }
                        foreach (string e in events)
                        {
                            int i = e.IndexOf(": "), j = e.LastIndexOf(" (teknik");
                            string what = e.Substring(i + 2, j - i - 2);
                            what = System.Text.RegularExpressions.Regex.Replace(what, "[0-9.,-]+°?", "#");
                            kinds2.TryGetValue(what, out int c); kinds2[what] = c + 1;
                        }
                    }
                losses.Sort();
                Console.WriteLine($"technique {t}: mean loss {losses.Average():F1} s/control, mistake time {mistakeTime / losses.Count:F1} s/control, " +
                                  $"{100f * mistakes / losses.Count:F0} % of legs a mistake, median loss {losses[losses.Count / 2]:F0} s, 90 % {losses[losses.Count * 9 / 10]:F0} s, max {losses[losses.Count - 1]:F0} s, timeouts {timeouts}");
                foreach (var kv in kinds2.OrderByDescending(x => x.Value)) Console.WriteLine($"     {kv.Value / (float)losses.Count:F2} per leg  {kv.Key}");
            }
            return 0;
        }
        if (a[2] == "worst")
        {
            // the run that lost the most time, with everything that happened in it
            int t = int.Parse(a[3]), n = int.Parse(a[4]); float worst = -1f; List<string> log = null; int worstLeg = 0;
            for (int l = 0; l < legs.Count; l++)
                for (int k = 0; k < n; k++)
                {
                    var r = RunLeg(legs[l], t, null);
                    if (r.Item1 - r.Item2 > worst) { worst = r.Item1 - r.Item2; log = new List<string>(events); worstLeg = l; }
                }
            Console.WriteLine($"leg {worstLeg} {legs[worstLeg]}: lost {worst:F0} s");
            foreach (string e in log) Console.WriteLine(e);
            return 0;
        }
        if (a[2] == "tracks")
        {
            int[] which = a[3].Split(',').Select(int.Parse).ToArray();
            int[] techs = a[4].Split(',').Select(int.Parse).ToArray();
            int tries = a.Length > 6 ? int.Parse(a[6]) : 1;
            var output = new JArray();
            foreach (int l in which)
                foreach (int t in techs)
                {
                    // of several tries, keep the one with the median loss, so the picture shows a typical run
                    var runs = new List<(float, JObject)>();
                    for (int k = 0; k < tries; k++)
                    {
                        var track = new List<float[]>();
                        var r = RunLeg(legs[l], t, track);
                        var o = new JObject { ["leg"] = l, ["technique"] = t, ["time"] = r.Item1, ["best"] = r.Item2, ["loss"] = r.Item1 - r.Item2,
                            ["from"] = legs[l][0], ["to"] = legs[l][1],
                            ["track"] = new JArray(track.Select(p => new JArray(Math.Round(p[0], 1), Math.Round(p[1], 1)))),
                            ["events"] = new JArray(events.ToArray()) };
                        runs.Add((r.Item1 - r.Item2, o));
                    }
                    runs.Sort((x, y) => x.Item1.CompareTo(y.Item1));
                    output.Add(runs[runs.Count * 2 / 3].Item2);
                }
            File.WriteAllText(a[5], output.ToString(Newtonsoft.Json.Formatting.None));
            return 0;
        }
        return 1;
    }

    static Vector2 PackageToMetres(JToken p) => map.ToMetres((float)p[0], (float)p[1], Mpu);
    // metres (centred) -> package coordinates
    static float[] ToPackage(Vector2 m)
    {
        float u = (m.x / cellM + cols / 2f - 0.5f) / (cols - 1), v = (m.y / cellM + rows / 2f - 0.5f) / (rows - 1);
        return new[] { map.minx + u * (map.maxx - map.minx), map.miny + v * (map.maxy - map.miny) };
    }

    // Returns (time, best time, timed out), real seconds
    static (float, float, bool) RunLeg(JToken leg, int technique, List<float[]> track)
    {
        events.Clear();
        var agent = new AgentMovement { pathfindingManager = new PathfindingManager { speedGrid2D = speed, cellSize = cellM / Mpu } };
        agent.runnerStatTec = 1f - technique / 100f;
        Vector2 start = PackageToMetres(leg[0]), control = PackageToMetres(leg[1]);
        agent.transform.position = (Vector3)(start / Mpu);
        Vector2Int sc = agent.WorldToGridPositionCentered(start / Mpu), cc = agent.WorldToGridPositionCentered(control / Mpu);
        agent.CurrentControlPosition = agent.GridToWorld(cc);
        var plan = agent.AStar(sc, cc);
        if (plan == null) return (0f, 0f, false);
        float best = 0f;
        for (int i = 1; i < plan.Count; i++)
            best += Vector2Int.Distance(plan[i], plan[i - 1]) * cellM * 2f / (speed[plan[i].y, plan[i].x] + speed[plan[i - 1].y, plan[i - 1].x]) / GameSpeed;
        agent.path = plan; agent.pathIndex = 0;
        var fm = new ForestMistakes(agent);
        fm.StartLeg();
        int n0 = 0;
        bool timedOut = false;
        while (true)
        {
            // the event list gets the place of each new event
            for (; n0 < events.Count; n0++) { float[] q = ToPackage((Vector2)agent.transform.position * Mpu); events[n0] = $"{q[0]:F0},{q[1]:F0} {agent.time * GameSpeed / RealSpeed:F0}s {events[n0]}"; }
            if (agent.time * GameSpeed / RealSpeed > best * GameSpeed / RealSpeed + 2400f) { timedOut = true; break; }
            float next = agent.Pump();
            if (agent.isWaiting)
            {
                if (next < 0f) { timedOut = true; break; }
                agent.time = Math.Max(agent.time, next);
                continue;
            }
            if (agent.pathIndex >= agent.path.Count)
            {
                if (fm.Blocking) { fm.OnPathEnd(); continue; }
                break;   // at the control
            }
            Vector3 from = agent.transform.position, target = agent.GridToWorld(agent.path[agent.pathIndex]);
            Vector2 d = (Vector2)(target - from);
            float dist = d.magnitude * Mpu;
            if (dist < 0.01f) { agent.pathIndex++; continue; }
            float step = Math.Min(dist, 2f);
            Vector2 to = (Vector2)from + d.normalized * (step / Mpu);
            Vector2Int cell = agent.WorldToGridPositionCentered(to);
            float v = Math.Max(0.3f, speed[cell.y, cell.x]);
            agent.time += step / (GameSpeed * v);
            agent.transform.position = to;
            if (step >= dist - 1e-4f) agent.pathIndex++;
            if (track != null) track.Add(ToPackage(to * Mpu));
            fm.Tick(from, to);
        }
        for (; n0 < events.Count; n0++) events[n0] = $"0,0 0s {events[n0]}";
        agent.ClearCoroutines();
        return (agent.time * GameSpeed / RealSpeed, best * GameSpeed / RealSpeed, timedOut);
    }
}
