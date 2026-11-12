using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Mistakes in the forest (not sprint), worked out 2026-11-09 and tested outside Unity on Illern 2025 first
// (pictures and the Python model in the project files, skogsbommar/).
//
// The runner always runs a path over the ground (A*) towards where it THINKS the control is:
//  * Direction error: off a path or a stream, now and then the runner aims a few degrees wrong (and misjudges the
//    distance a little). More often with low technique, in ground with few clear things, and close to the control.
//  * Reading in: on the way the runner notices things (the clearer, the more likely) and looks for them on the map
//    round where it thinks it is. If another thing of the same kind lies closer to that place, it may well pick that
//    one: a parallel error, and it then aims at the control from the wrong place. A straight path without a junction
//    only says you are on the path, not where on it. Something that does not fit at all (a big road behind the
//    control) late in the leg makes you stop and think.
//  * Distance sense: you know roughly how far it is, so you only start looking once you have run about as far as the
//    leg (or clearly too far). Unsure in the middle of the leg, you just keep going.
//  * No flag where you thought: if the ground round you looks like the control's (same kinds of things), you circle
//    there until enough things do not fit (low technique needs more of them). Then you go to the clearest thing near
//    and read yourself in. After two empty sites you go for something big and certain (a road, a big path).
//  * The technique is the card's the whole leg: a runner does not read the map better because it has gone wrong.
//  * Anything on the map can tell you where you are, if it is the only one of its kind round there: a path, a
//    railway, a power line (and its pylons), a wall, a fence, a big cliff, a house, a marsh, a field, a thicket.
//    Two of them near each other: you guess, helped by what you passed on the way and how the ground slopes.
//    Clear things are looked for wider on the map than small ones ("there is only one field round here").
//  * A line or an edge only tells which line you are on, not where on it, unless you are at a corner or a junction.
//  * Along a ridge or a valley bottom the ground shows the way, like a path: no direction errors there.
//  * The flag is not always seen: off the plan the runner can pass it 15 m away and run on.
// Tuned roughly to the technique scale of the runner cards (technique = 100 - 2 points per mistake minute per
// 100 controls), on purpose with more mistakes than that: see Docs/forest_mistakes/README.md.
public class ForestMistakes
{
    enum Mode { Route, Drift, Circle, Lost, Safe }

    const float H0 = 1f / 1500f;           // direction errors per metre at technique 0 in average ground
    const float RealSpeed100 = 3.6f;       // m/s on speed-1 ground for a speed-100 runner in the real world

    readonly AgentMovement agent;
    readonly System.Random rng = new System.Random();
    Mode mode = Mode.Route;
    float skill;                           // 0..1, the card's technique
    Vector2 aim, lostBelief;               // metres
    float U = 8f;                          // how unsure the runner is of its place, metres
    float sinceFix, runSinceAim, expect, travel, lostMetres, doubt, doubtNeeded, circleSimilarity, nextContour, lostTurn = 1f;
    Vector2 lostCentre;
    Dictionary<int, int> controlSignature = new Dictionary<int, int>();
    readonly List<Vector2> visited = new List<Vector2>();
    int misses;
    readonly List<long> lostTargets = new List<long>();
    readonly Dictionary<long, float> seen = new Dictionary<long, float>();
    readonly List<int> near = new List<int>();
    Coroutine busy;
    bool waitingForPath;

    public ForestMistakes(AgentMovement agent) { this.agent = agent; }

    float Mpu => MapFeatureIndex.metresPerUnit;
    Vector2 Pos => (Vector2)agent.transform.position * Mpu;
    Vector2 Control => (Vector2)agent.CurrentControlPosition * Mpu;
    Vector2 Belief => mode == Mode.Route ? Pos : (mode == Mode.Drift ? Control - (aim - Pos) : lostBelief);

    // Off the planned route: the runner must not punch a control it has not seen, and owns its path
    public bool Active => mode != Mode.Route || waitingForPath;
    public bool BlocksPunch => mode == Mode.Drift || mode == Mode.Circle || mode == Mode.Lost || mode == Mode.Safe;
    // The path belongs to the mistake logic for now (off the plan, or a new path is being worked out)
    public bool Blocking => BlocksPunch || waitingForPath;

    static bool Enabled()
    {
        if (!MapFeatureIndex.Ready) return false;
        if (CourseLoader.Loaded != null && CourseLoader.Loaded.IsSprint()) return false;
        return true;
    }

    // A new leg (or a new drawn route): back on the plan
    public void StartLeg()
    {
        if (busy != null) { agent.StopCoroutine(busy); busy = null; agent.isWaiting = false; }
        waitingForPath = false;
        mode = Mode.Route; U = 8f; sinceFix = 0f; travel = 0f; quietUntil = 0f; smallDone = false; flagBlindUntil = 0f; nextObserve = 0f; parallels = 0; sureTravel = 0f; trail.Clear(); lastSide = 0; nextSideCheck = 0f; misses = 0; nextContour = 25f;
        skill = baseSkill = Mathf.Clamp01(1f - agent.runnerStatTec);
        seen.Clear(); lostTargets.Clear();
    }

    float baseSkill;
    void Log(string what) { Debug.Log($"[Bom] {agent.runnerName}: {what} (teknik {baseSkill * 100f:F0})"); }
    // Something did not fit. The runner is no better at reading the map for it (if anything it gets stressed), but it
    // holds on to its new idea for a while
    void Surprise(string what)
    {
        Log(what);
        quietUntil = travel + 40f;
    }

    float Rand() => (float)rng.NextDouble();
    float Gauss() { float u1 = 1f - Rand(), u2 = Rand(); return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2); }

    float GroundSpeed(Vector2 m)
    {
        Vector2Int c = agent.WorldToGridPositionCentered(m / Mpu);
        return agent.pathfindingManager.speedGrid2D[c.y, c.x];
    }
    float FlagSight() => (10f + 15f * Mathf.Min(1f, GroundSpeed(Pos))) * (MapFeatureIndex.Open(Pos) ? 1.6f : 1f);

    // The flag is easy to miss: behind a tree or a knoll, down in a pit. Right by it (8 m) you always see it; further
    // off, within sight, you get one look every 25 m you run. Low technique looks up from the map less.
    float flagBlindUntil;
    bool SeesFlag(Vector2 pos)
    {
        float d = Vector2.Distance(pos, Control);
        if (d >= FlagSight()) return false;
        if (d < 8f) return true;
        if (travel < flagBlindUntil) return false;
        if (Rand() < 0.45f + 0.5f * skill) return true;
        flagBlindUntil = travel + 25f;
        return false;
    }

    // Called every frame after the runner has moved
    public void Tick(Vector3 fromWorld, Vector3 toWorld)
    {
        if (!Enabled() || waitingForPath || busy != null) return;
        float d = Vector2.Distance(fromWorld, toWorld) * Mpu;
        if (d <= 0f) return;
        travel += d; sinceFix += d; runSinceAim += d;
        Vector2 pos = Pos;
        if (CrossedToWrongSide(pos)) return;

        if (mode != Mode.Route && SeesFlag(pos))
        {
            Log("ser skärmen");
            ToControl();
            return;
        }
        switch (mode)
        {
            case Mode.Route:
                if (!smallDone && Vector2.Distance(pos, Control) < 60f)
                {
                    // A small mistake at the control: aim a bit beside it, then look round there
                    smallDone = true;
                    float dc = 0.6f + Mathf.Max(0f, 1.2f - MapFeatureIndex.Readability(Control) / 3f);
                    if (Rand() < 0.7f * Mathf.Pow(1f - skill, 0.9f) * dc)
                    {
                        Vector2 v = (Control - pos).normalized, n = new Vector2(-v.y, v.x) * (Rand() < 0.5f ? -1f : 1f);
                        Vector2 off = Control + n * (25f + 20f * Rand()) + v * (-10f + 25f * Rand());
                        if (GroundSpeed(off) > 0.05f)
                        {
                            Log("liten miss vid kontrollen");
                            mode = Mode.Drift; sinceFix = 0f; U = 15f;
                            SetAim(off);
                            return;
                        }
                    }
                }
                if (!MapFeatureIndex.OnHandrail(pos) && !MapFeatureIndex.OnTerrainLine(pos, (Vector2)(toWorld - fromWorld)))
                {
                    float left = Vector2.Distance(pos, Control);
                    // Unsure on the leg (few details to read, or a mess of them): direction errors are more common
                    float diff = 0.4f + Mathf.Max(0f, 2f - MapFeatureIndex.Readability(pos) / 2f);
                    float nearControl = left < 70f ? 1.3f : 1f;   // drifting off happens all along the leg
                    float perMetre = H0 * Mathf.Pow(1f - skill, 0.45f) * diff * nearControl + 1f / 250000f;
                    if (Rand() < perMetre * d) DirectionError();
                }
                return;
            case Mode.Drift:
                U = Mathf.Min(60f, 12f + 0.18f * sinceFix);
                if (runSinceAim > (1.15f + 0.35f * (1f - skill)) * expect + 20f)
                {
                    Surprise("har sprungit för långt");
                    GoLost();
                    return;
                }
                Observe(1f);
                return;
            case Mode.Circle:
                CircleDoubt();
                return;
            case Mode.Lost:
            case Mode.Safe:
                lostMetres += d; U = Mathf.Min(80f, U + 0.075f * d);
                if (visited.Count == 0 || Vector2.Distance(visited[visited.Count - 1], pos) > 25f) visited.Add(pos);
                Observe(1.5f);
                return;
        }
    }

    // The path the runner was on has run out (not on the plan)
    public void OnPathEnd()
    {
        if (!Enabled()) { ToControl(); return; }
        switch (mode)
        {
            case Mode.Drift: AtEmptySite(); break;
            case Mode.Circle: NextLookStop(); break;
            case Mode.Lost:
                Run(ReadAndGoOn());
                break;
            case Mode.Safe:
                misses = 0; Fix(); break;
            default: ToControl(); break;
        }
    }

    // ---------------- the kinds of mistakes
    void DirectionError()
    {
        // Good runners make small direction errors, poor ones can be far off
        float bad = 1f - skill;
        // Technique 0: 20-40 degrees off; the elite 8-16 degrees, so you can see they run a bit off
        float a = Mathf.Min(8f + 12f * bad + Mathf.Abs(Gauss()) * (8f + 8f * bad), 16f + 24f * bad) * (Rand() < 0.5f ? -1f : 1f);
        float k = 1f + Gauss() * (0.03f + 0.09f * bad);
        Vector2 toControl = Control - Pos;
        float rad = a * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
        Vector2 wrong = new Vector2(toControl.x * cos - toControl.y * sin, toControl.x * sin + toControl.y * cos) * k;
        Log($"riktningsfel {a:F0}°");
        mode = Mode.Drift; sinceFix = 0f;
        quietUntil = travel + 80f;   // you only start to wonder once you have run a bit the wrong way
        SetAim(Pos + wrong);
    }

    bool Noticed(float clarity, float boost) => Rand() < Mathf.Min(1f, clarity * (0.55f + 0.45f * skill) * boost);

    int lastSide; float nextSideCheck; Vector2 lastRightSidePos;
    // Crossing a road or a big path you didn't have to cross: "this side is wrong, back over it".
    // You may still not know where on it you are, but you never search on the wrong side of it.
    bool CrossedToWrongSide(Vector2 pos)
    {
        if (travel < nextSideCheck) return false;
        nextSideCheck = travel + 4f;
        int side = MapFeatureIndex.SideOf(pos), controlSide = MapFeatureIndex.SideOf(Control);
        if (side == 0 || controlSide == 0) return false;
        bool wrong = lastSide == controlSide && side != controlSide;
        lastSide = side;
        if (side == controlSide) { lastRightSidePos = pos; return false; }
        if (!wrong || mode == Mode.Lost || mode == Mode.Safe || mode == Mode.Route) return false;
        Log("korsade stigen/vägen – fel sida, vänder");
        Vector2 target = MapFeatureIndex.SameSide(aim, Control) ? aim : lastRightSidePos;
        mode = Mode.Drift;
        SetAim(target);
        return true;
    }
    float quietUntil;
    bool smallDone;
    // What the runner has noticed lately (kind and place): "a lake, then a boulder, then a cliff" only fits one place
    struct Passed { public int kind; public Vector2 p; public float at; }
    readonly List<Passed> trail = new List<Passed>();
    float nextObserve;
    int parallels;                         // wrong identifications on this leg
    float sureTravel;                      // how far the runner had run when it last knew where it was

    void Observe(float boost)
    {
        // Right after a direction error or a surprise you run on with your idea: nothing makes you change it yet
        if (travel < quietUntil || travel < nextObserve) return;
        nextObserve = travel + 2f;
        Vector2 pos = Pos;
        float g = GroundSpeed(pos);
        var pts = MapFeatureIndex.points;
        // Stones, knolls, pits, pylons ... seen further in open ground and from high ground
        MapFeatureIndex.QueryPoints(pos, 70f, near);
        foreach (int j in near.ToArray())
        {
            float c = pts[j].clarity;
            if (Vector2.Distance(pts[j].p, pos) > MapFeatureIndex.Sight(pos, pts[j].p, c, g)) continue;
            long key = j;
            if (seen.TryGetValue(key, out float at) && at > travel - 200f) continue;
            seen[key] = travel;
            if (!Noticed(c, boost)) continue;
            // One stone among many: often you just run on without thinking about it
            if (pts[j].unique < 0.5f && Rand() < 0.5f) continue;
            int group = pts[j].group;
            if (Consider(group, j, pts[j].p, c, 10f, false, (centre, radius, cands, ids) =>
                {
                    MapFeatureIndex.QueryPoints(centre, radius, near);
                    foreach (int k in near) if (pts[k].group == group) { cands.Add(pts[k].p); ids.Add(k); }
                })) return;
        }
        // Paths, streams, fences ... when you cross or run along them; roads, railways, power lines and lake shores
        // from a bit away
        var smp = MapFeatureIndex.samples;
        MapFeatureIndex.QuerySamples(pos, 40f, near);
        foreach (int j in near.ToArray())
        {
            if ((smp[j].p - pos).sqrMagnitude > smp[j].see * smp[j].see) continue;
            long key = (1L << 40) | (uint)smp[j].line;
            if (seen.TryGetValue(key, out float at) && at > travel - 120f) continue;
            seen[key] = travel;
            float c = smp[j].clarity;
            // A road by houses and gardens, or a junction that is the only one round here or has something clear by it:
            // you know exactly where you are
            if (smp[j].roadSure && mode != Mode.Route && (smp[j].p - pos).sqrMagnitude < 36f) { Fix(); return; }
            // Another junction: which one of the junctions round here is it?
            if (smp[j].junction && mode != Mode.Route && (smp[j].p - pos).sqrMagnitude < 36f)
            {
                long trueCl = JunctionKey(smp[j].p);
                Vector2 at0 = smp[j].p;
                if (Consider(0, trueCl, at0, 0.9f, 25f, false, (centre, radius, cands, ids) =>
                    {
                        var have = new HashSet<long>();
                        MapFeatureIndex.QuerySamples(centre, radius + 35f, near);
                        foreach (int k in near)
                        {
                            if (!smp[k].junction) continue;
                            long cl = JunctionKey(smp[k].p);
                            if (have.Add(cl)) { cands.Add(smp[k].p); ids.Add(cl); }
                        }
                        if (!have.Contains(trueCl) && Vector2.Distance(at0, centre) < radius + 35f) { cands.Add(at0); ids.Add(trueCl); }
                    })) return;
                continue;
            }
            if (!Noticed(c, boost * 1.1f)) continue;
            // At a corner or a junction of it (good runners look for that): only places like that on the map fit
            bool distinct = smp[j].distinct && Rand() < 0.4f + 0.5f * skill;
            int group = smp[j].group;
            if (Consider(group + MapFeatureIndex.LineGroupOffset, smp[j].line, smp[j].p, c, 10f, false, (centre, radius, cands, ids) =>
                {
                    var best = new Dictionary<int, (float, Vector2)>();
                    MapFeatureIndex.QuerySamples(centre, radius, near);
                    foreach (int k in near)
                    {
                        if (smp[k].group != group || (distinct && !smp[k].distinct)) continue;
                        float dd = (smp[k].p - centre).sqrMagnitude;
                        if (!best.TryGetValue(smp[k].line, out var b) || dd < b.Item1) best[smp[k].line] = (dd, smp[k].p);
                    }
                    foreach (var b in best) { cands.Add(b.Value.Item2); ids.Add(b.Key); }
                })) return;
        }
        // Houses, gardens, olive green, lakes: hard to miss
        var areas = MapFeatureIndex.areas;
        MapFeatureIndex.QueryAreas(pos, 60f, near);
        for (int n = 0; n < near.Count; n += 2)
        {
            int j = near[n];
            if (Vector2.Distance(areas[j].p, pos) > MapFeatureIndex.Sight(pos, areas[j].p, 0.9f, g)) continue;
            long key = (3L << 40) | (uint)areas[j].area;
            if (seen.TryGetValue(key, out float at) && at > travel - 150f) continue;
            seen[key] = travel;
            if (!Noticed(0.9f, boost * 1.2f)) continue;
            bool small = areas[j].small;
            // Matching it to the right house or plot puts you within about 25 m
            if (Consider(0, areas[j].area, areas[j].p, 0.9f, 25f, false, (centre, radius, cands, ids) =>
                {
                    var best = new Dictionary<int, (float, Vector2)>();
                    var list = new List<int>();
                    MapFeatureIndex.QueryAreas(centre, radius, list);
                    foreach (int k in list)
                    {
                        if (areas[k].small != small) continue;
                        float dd = (areas[k].p - centre).sqrMagnitude;
                        if (!best.TryGetValue(areas[k].area, out var b) || dd < b.Item1) best[areas[k].area] = (dd, areas[k].p);
                    }
                    foreach (var b in best) { cands.Add(b.Value.Item2); ids.Add(b.Key); }
                })) return;
            break;
        }
        // A marsh, a field, a thicket, a boulder field: you notice it when you come to it (a field from far away, a
        // marsh often only when you stand in it). Which one on the map is it?
        var edges = MapFeatureIndex.zoneEdges; var zones = MapFeatureIndex.zones;
        if (zones.Count > 1)
        {
            float half = MapFeatureIndex.CellSizeMetres * 0.5f;
            var here = new Dictionary<int, Vector2>();
            int inside = MapFeatureIndex.ZoneAt(pos);
            if (inside != 0) here[inside] = pos;
            MapFeatureIndex.QueryZones(pos, 35f + half, near);
            foreach (int j in near)
            {
                int z = edges[j].zone;
                if (here.ContainsKey(z) || Vector2.Distance(edges[j].p, pos) > zones[z].see + half) continue;
                here[z] = edges[j].p;
            }
            foreach (var kv in here)
            {
                int z = kv.Key;
                long key = (5L << 40) | (uint)z;
                if (seen.TryGetValue(key, out float at) && at > travel - 200f) continue;
                seen[key] = travel;
                float c = zones[z].clarity;
                if (!Noticed(c, boost)) continue;
                int cls = zones[z].cls;
                // A small marsh or thicket is a place of its own; a big one only says you are somewhere along it
                float tol = zones[z].size <= 6 ? 25f : 10f;
                if (Consider(cls + MapFeatureIndex.ZoneKeyOffset, z, kv.Value, c, tol, false, (centre, radius, cands, ids) =>
                    {
                        var best = new Dictionary<int, (float, Vector2)>();
                        var list = new List<int>();
                        MapFeatureIndex.QueryZones(centre, radius, list);
                        foreach (int k in list)
                        {
                            int zk = edges[k].zone;
                            if (zones[zk].cls != cls) continue;
                            float dd = (edges[k].p - centre).sqrMagnitude;
                            if (!best.TryGetValue(zk, out var b) || dd < b.Item1) best[zk] = (dd, edges[k].p);
                        }
                        foreach (var b in best) { cands.Add(b.Value.Item2); ids.Add(b.Key); }
                    })) return;
            }
        }
        // Big hills: you feel when you are on the top of one
        var hills = MapFeatureIndex.hills;
        for (int j = 0; j < hills.Count; j++)
        {
            if (Vector2.Distance(hills[j].p, pos) > hills[j].size || MapFeatureIndex.HeightAt(pos) < hills[j].height - 6f) continue;
            long key = (4L << 40) | (uint)j;
            if (seen.TryGetValue(key, out float at) && at > travel - 300f) continue;
            seen[key] = travel;
            if (!Noticed(hills[j].clarity, boost * 1.3f)) continue;
            if (Consider(0, j, hills[j].p, hills[j].clarity, hills[j].size + 10f, true, (centre, radius, cands, ids) =>
                {
                    for (int h = 0; h < hills.Count; h++) if (Vector2.Distance(hills[h].p, centre) < radius + 55f) { cands.Add(hills[h].p); ids.Add(h); }
                })) return;
        }
        // Reading the contours as you go: works in hilly ground, for good runners
        if (travel >= nextContour)
        {
            nextContour = travel + 25f;
            if (mode != Mode.Route && Rand() < 0.08f * skill * MapFeatureIndex.Relief(pos))
            {
                Log("läser in sig på höjdkurvorna");
                Fix();
            }
        }
    }

    delegate void Gather(Vector2 centre, float radius, List<Vector2> cands, List<long> ids);

    // The runner has noticed something at truePos. It looks on the map for things of that kind round where it thinks
    // it is; the clearer the thing, the wider it looks ("is there another power line round here? No: it is this one").
    // Something clear that is nowhere near where it thought makes it stop and look over a still bigger part of the map.
    bool Consider(int kind, long trueId, Vector2 truePos, float clarity, float tol, bool hill, Gather gather)
    {
        if (mode == Mode.Route) return false;
        var cands = new List<Vector2>(); var ids = new List<long>();
        Vector2 bel = Belief;
        float radius = U + 25f + 250f * clarity * clarity;
        gather(bel, radius, cands, ids);
        float spread = Mathf.Max(U, 10f);
        for (int i = cands.Count - 1; i >= 0; i--)
            if (!MapFeatureIndex.SameSide(cands[i], Pos)) { cands.RemoveAt(i); ids.RemoveAt(i); }
        if (cands.Count == 0 && clarity >= 0.6f)
        {
            radius = 700f * clarity; spread = radius * 0.5f;
            gather(bel, radius, cands, ids);
        }
        // You know roughly how far you have run since you last knew where you were, so a thing far from where you
        // really are is not on the list: the further you have run (and the lower the technique), the wider the list
        float reach = 30f + (0.15f + 0.35f * (1f - skill)) * (travel - sureTravel);
        for (int i = cands.Count - 1; i >= 0; i--)
        {
            Vector2 d = cands[i] - truePos;
            if (d.sqrMagnitude <= reach * reach) continue;
            if (ids[i] == trueId) cands[i] = truePos + d.normalized * reach;   // the right path, about this far along it
            else { cands.RemoveAt(i); ids.RemoveAt(i); }
        }
        bool changed = Resolve(cands, ids, trueId, truePos, clarity, tol, hill, spread);
        if (kind != 0 && clarity >= 0.3f)
        {
            trail.Add(new Passed { kind = kind, p = truePos, at = travel });
            if (trail.Count > 4) trail.RemoveAt(0);
        }
        return changed;
    }

    // How well what the runner passed lately fits if it now stands at cand (seeing the thing that really is at truePos):
    // each earlier thing should lie the same way from here on the map. Only good runners keep count like that.
    float TrailFit(Vector2 cand, Vector2 truePos, bool isTrue)
    {
        float f = 1f;
        foreach (Passed t in trail)
        {
            if (travel - t.at > 300f || (t.p - truePos).sqrMagnitude < 25f) continue;
            Vector2 d = t.p - truePos;
            bool fits = isTrue || MapFeatureIndex.HasKind(t.kind, cand + d, 15f + 0.1f * d.magnitude);
            f *= fits ? 1f + 2f * skill : 1f - 0.6f * skill;
        }
        return f;
    }

    // Does the ground slope the same way at the two places? A stone on a west slope is not the stone on the flat.
    // Same slope and same kind of thing: an easy parallel error.
    float SlopeFit(Vector2 cand, Vector2 truePos)
    {
        Vector2 a = MapFeatureIndex.Slope(truePos), b = MapFeatureIndex.Slope(cand);
        float m = Mathf.Max(a.magnitude, b.magnitude);
        if (m < 0.06f) return 1f;   // flat at both: nothing to tell them apart by
        return 1f - 0.8f * skill * Mathf.Clamp01((a - b).magnitude / m);
    }

    // Seen something at truePos: which one on the map is it? Returns true when the runner changed its mind.
    // tol: how close the right match puts you (a stone: 10 m, a house: 25 m). hill: the right hill only says
    // roughly where you are, so you aim from its top.
    bool Resolve(List<Vector2> cands, List<long> ids, long trueId, Vector2 truePos, float clarity, float tol, bool hill, float spread)
    {
        if (mode == Mode.Route) return false;
        // You always notice crossing a road, so it can't be one on the other side of a road
        for (int i = cands.Count - 1; i >= 0; i--)
            if (!MapFeatureIndex.SameSide(cands[i], Pos)) { cands.RemoveAt(i); ids.RemoveAt(i); }
        float frac = runSinceAim / Mathf.Max(expect, 1f);
        if (cands.Count == 0)
        {
            if (mode == Mode.Drift && clarity >= 0.7f && frac > 0.5f && Rand() < 0.5f + 0.5f * skill)
            {
                Surprise("det här stämmer inte");
                GoLost();
                return true;
            }
            U = Mathf.Min(80f, U + 10f);
            return false;
        }
        Vector2 bel = Belief, pos = Pos;
        // The only one of its kind round here: "then it is this one". Not a full guarantee: you have to trust it.
        if (cands.Count == 1 && ids[0] == trueId && Rand() > 0.6f + 0.4f * skill)
        {
            U = Mathf.Min(80f, U + 5f);
            return false;
        }
        float[] w = new float[cands.Count];
        float sum = 0f, max = 0f;
        for (int i = 0; i < w.Length; i++)
        {
            // Nearest to where you think you are is the first guess, but one further off is not ruled out
            w[i] = 1f / (1f + (cands[i] - bel).sqrMagnitude / (spread * spread));
            bool isTrue = ids[i] == trueId;
            // Reading the detail round it: the right one gets a bonus that grows with technique
            if (isTrue) w[i] *= 1f + 4f * skill;
            else w[i] *= SlopeFit(cands[i], truePos);
            w[i] *= TrailFit(cands[i], truePos, isTrue);
            sum += w[i];
        }
        for (int i = 0; i < w.Length; i++) { w[i] /= sum; max = Mathf.Max(max, w[i]); }
        float r = Rand(); int pick = w.Length - 1;
        for (int i = 0; i < w.Length; i++) { r -= w[i]; if (r <= 0f) { pick = i; break; } }
        if (max < 0.75f && Rand() < skill * (1f - max) * 1.5f)
        {
            U = Mathf.Min(80f, U + 5f);   // not sure which one: keep going the same way
            return false;
        }
        Vector2 chosen = cands[pick];
        bool right = ids[pick] == trueId;
        // A house or a plot you know is the right one: you see its corner and know exactly where you are
        if (right && !hill && (tol >= 25f || Vector2.Distance(truePos, pos) < tol && Vector2.Distance(chosen, truePos) < tol))
        {
            Fix();
            return true;
        }
        if (right)
        {
            // The right hill, path, lake or field, but not exactly where on it (you know which side of the road you
            // are): a look at the map, then aim from the place on it you think you are
            Log("vet vad det är, men inte exakt var på den");
            U = 20f; sinceFix = 0f; mode = Mode.Drift;
            Vector2 target = pos + (Control - chosen);
            Run(SlowThen(2f, () => SetAim(target)));
            return true;
        }
        // Twice fooled on this leg, or on the way to something big and certain: small things are not trusted any more
        if (mode == Mode.Safe || parallels >= 2)
        {
            U = Mathf.Min(80f, U + 5f);
            return false;
        }
        parallels++;
        // Thinks it is at the chosen one: aims at the control from there (a parallel error)
        Surprise("tror sig vara på fel ställe (parallellfel)");
        U = 15f; sinceFix = 0f; mode = Mode.Drift;
        SetAim(pos + (Control - (chosen + (pos - truePos))));
        return true;
    }

    readonly List<Vector2> lookStops = new List<Vector2>();

    void AtEmptySite()
    {
        misses++;
        circleSimilarity = MapFeatureIndex.Similarity(MapFeatureIndex.Signature(Pos), MapFeatureIndex.Signature(Control));
        Surprise($"ingen skärm här (likhet med kontrollområdet {circleSimilarity:F2})");
        lostBelief = Belief;
        if (!MapFeatureIndex.SameSide(Pos, Control)) circleSimilarity = 0f;   // the control is over the road: no doubt
        if (circleSimilarity <= 0.3f) { Run(PauseThen(2f + 5f * (1f - skill), GoLost)); return; }
        // Looks like the control's ground: look round here, a loose loop of 4-5 stops 25-45 m out on ground like
        // the control's (green, open ...), while what you see still fits
        doubt = 0f; doubtNeeded = 1f + 3f * (1f - skill);
        mode = Mode.Circle;
        lookStops.Clear();
        Vector2 centre = Pos;
        float controlSpeed = GroundSpeed(Control), ang = Rand() * 6.28f, turn = Rand() < 0.5f ? -1f : 1f;
        int stops = Rand() < 0.5f ? 4 : 5;
        for (int n = 0; n < stops; n++)
        {
            float best = -1f, bestAng = ang; Vector2 bestPoint = centre;
            for (int t = 0; t < 6; t++)
            {
                float a = ang + turn * (0.9f + 0.7f * Rand()), r = 25f + 20f * Rand();
                Vector2 q = centre + r * new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float g = GroundSpeed(q);
                if (g <= 0.05f || !MapFeatureIndex.SameSide(q, Control)) continue;
                float sc = 1f / (1f + Mathf.Abs(g - controlSpeed) * 3f) + 0.3f * Rand();
                if (sc > best) { best = sc; bestAng = a; bestPoint = q; }
            }
            if (best < 0f) break;
            ang = bestAng; lookStops.Add(bestPoint);
        }
        if (lookStops.Count == 0) { GoLost(); return; }
        Run(PauseThen(2f + 5f * (1f - skill), NextLookStop));
    }

    void NextLookStop()
    {
        if (lookStops.Count == 0 || doubt >= doubtNeeded) { GoLost(); return; }
        Vector2 next = lookStops[0]; lookStops.RemoveAt(0);
        PathTo(next);
    }

    void CircleDoubt()
    {
        var pts = MapFeatureIndex.points;
        MapFeatureIndex.QueryPoints(Pos, 15f, near);
        foreach (int j in near)
        {
            long key = (2L << 40) | (uint)j;
            if (seen.ContainsKey(key) || pts[j].clarity < 0.3f) continue;
            seen[key] = 0f;
            if (Rand() < pts[j].clarity * (0.5f + 0.5f * skill)) doubt += (1f - circleSimilarity) + 0.3f;
        }
        if (doubt >= doubtNeeded) GoLost();
    }

    void GoLost()
    {
        if (mode != Mode.Circle && mode != Mode.Lost) lostBelief = Belief;
        mode = Mode.Lost; U = Mathf.Max(U, 50f); lostMetres = 0f; lostTargets.Clear();
        lostCentre = Pos; lostTurn = Rand() < 0.5f ? -1f : 1f;
        controlSignature = MapFeatureIndex.Signature(Control, 30f);
        visited.Clear(); visited.Add(Pos);
        Run(SlowThen(3f + 10f * (1f - skill), NextLostTarget));
    }

    // Lost: go round the area, to the clearest new thing ahead along a loop round where you got lost
    float LostScore(Vector2 q, float value)
    {
        Vector2 pos = Pos;
        float d = Vector2.Distance(q, pos);
        if (d < 40f || !MapFeatureIndex.SameSide(q, Control)) return 0f;   // search on the control's side of roads
        for (int i = 0; i < visited.Count; i++) if (Vector2.Distance(q, visited[i]) < 70f) return 0f;
        float f = 1f;
        Vector2 r = pos - lostCentre; float rn = r.magnitude;
        if (rn > 5f)
        {
            Vector2 u = (q - pos) / d, out_ = r / rn, tang = new Vector2(-out_.y, out_.x) * lostTurn;
            f = 0.4f + 0.8f * Mathf.Max(0f, Vector2.Dot(u, tang)) + (rn < 80f ? 0.3f * Mathf.Max(0f, Vector2.Dot(u, out_)) : 0f);
        }
        // The control is in green / open / rocky ground: look in ground like that
        float like = 1f / (1f + Mathf.Abs(GroundSpeed(q) - GroundSpeed(Control)) * 3f)
                     + 0.5f * MapFeatureIndex.Similarity(MapFeatureIndex.Signature(q, 30f), controlSignature);
        return value * f * like / (d + 30f);
    }

    void NextLostTarget()
    {
        mode = Mode.Lost;
        if (lostMetres > 300f + 200f * skill) { Fix(); return; }
        Vector2 pos = Pos; float bestScore = 0f; long bestKey = 0; Vector2 target = pos; bool found = false;
        var pts = MapFeatureIndex.points; var smp = MapFeatureIndex.samples; var areas = MapFeatureIndex.areas;
        if (misses >= 2)
        {
            // Two empty sites: go for something big and certain
            MapFeatureIndex.QuerySamples(pos, 400f, near);
            for (int n = 0; n < near.Count; n += 2)
            {
                int j = near[n];
                if (smp[j].clarity < 0.6f) continue;
                float sc = LostScore(smp[j].p, smp[j].clarity * (smp[j].distinct ? 1.5f : 1f));
                if (sc > bestScore) { bestScore = sc; target = smp[j].p; found = true; }
            }
            if (found)
            {
                Log("springer till något säkert");
                mode = Mode.Safe;
                PathTo(target);
                return;
            }
        }
        MapFeatureIndex.QueryPoints(pos, 150f, near);
        foreach (int j in near)
        {
            if (lostTargets.Contains(j)) continue;
            float sc = LostScore(pts[j].p, pts[j].clarity * pts[j].unique);
            if (sc > bestScore) { bestScore = sc; bestKey = j; target = pts[j].p; found = true; }
        }
        MapFeatureIndex.QuerySamples(pos, 150f, near);
        for (int n = 0; n < near.Count; n += 3)
        {
            int j = near[n];
            long key = (1L << 40) | (uint)smp[j].line;
            if (smp[j].clarity < 0.5f || lostTargets.Contains(key)) continue;
            float sc = LostScore(smp[j].p, smp[j].clarity * (smp[j].distinct ? 1.3f : 0.7f));
            if (sc > bestScore) { bestScore = sc; bestKey = key; target = smp[j].p; found = true; }
        }
        MapFeatureIndex.QueryAreas(pos, 200f, near);
        for (int n = 0; n < near.Count; n += 3)
        {
            int j = near[n];
            long key = (3L << 40) | (uint)areas[j].area;
            if (lostTargets.Contains(key)) continue;
            float sc = LostScore(areas[j].p, 1.2f);
            if (sc > bestScore) { bestScore = sc; bestKey = key; target = areas[j].p; found = true; }
        }
        var zoneEdges = MapFeatureIndex.zoneEdges; var zoneList = MapFeatureIndex.zones;
        MapFeatureIndex.QueryZones(pos, 200f, near);
        for (int n = 0; n < near.Count; n += 3)
        {
            int j = near[n];
            float clarity = zoneList[zoneEdges[j].zone].clarity;
            long key = (5L << 40) | (uint)zoneEdges[j].zone;
            if (clarity < 0.5f || lostTargets.Contains(key)) continue;
            float sc = LostScore(zoneEdges[j].p, clarity);
            if (sc > bestScore) { bestScore = sc; bestKey = key; target = zoneEdges[j].p; found = true; }
        }
        if (!found) { Fix(); return; }
        lostTargets.Add(bestKey); seen.Remove(bestKey);
        PathTo(target);
    }

    IEnumerator ReadAndGoOn()
    {
        SlowDown(2f + 4f * (1f - skill));
        yield return null;
        busy = null;
        // Look properly round here
        var keep = new Dictionary<long, float>();
        foreach (var kv in seen) if ((kv.Key >> 40) == 2) keep[kv.Key] = kv.Value;
        seen.Clear(); foreach (var kv in keep) seen[kv.Key] = kv.Value;
        Observe(1.5f);
        if (mode == Mode.Lost) NextLostTarget();
    }

    static long JunctionKey(Vector2 p) => ((long)Mathf.FloorToInt(p.x / 30f) << 32) ^ (uint)Mathf.FloorToInt(p.y / 30f);

    void Fix()
    {
        Log("vet var den är");
        U = 8f; sinceFix = 0f; sureTravel = travel;
        // A few slower seconds ("now I see, I'm over here") while turning the right way
        if (mode != Mode.Route) Run(SlowThen(3.5f, ToControl));
        else ToControl();
    }

    void ToControl()
    {
        mode = Mode.Route;
        PathTo(Control);
    }

    void SetAim(Vector2 target)
    {
        aim = target;
        runSinceAim = 0f;
        expect = Vector2.Distance(Pos, target) * 1.1f;   // replaced by the path length below
        PathTo(target, true);
    }

    // ---------------- paths and pauses
    void PathTo(Vector2 targetMetres, bool setExpect = false)
    {
        Vector2Int start = agent.WorldToGridPositionCentered(agent.transform.position);
        Vector2Int goal = NearestOpen(agent.WorldToGridPositionCentered(targetMetres / Mpu));
        waitingForPath = true;
        agent.isWaiting = true;
        agent.StartCoroutine(agent.FindPathAsync(start, goal, result =>
        {
            waitingForPath = false;
            agent.isWaiting = false;
            if (result == null || result.Count == 0)
            {
                // No way there over open ground: never straight through houses or olive green. Head for the control.
                if (Vector2.Distance(targetMetres, Control) > 1f) { mode = Mode.Route; PathTo(Control); return; }
                result = new List<Vector2Int> { start };
            }
            if (setExpect)
            {
                float len = 0f;
                for (int i = 1; i < result.Count; i++) len += Vector2Int.Distance(result[i], result[i - 1]);
                expect = len * agent.pathfindingManager.cellSize * Mpu;
            }
            agent.SetMistakePath(result);
        }));
    }

    Vector2Int NearestOpen(Vector2Int c)
    {
        float[,] g = agent.pathfindingManager.speedGrid2D;
        int rows = g.GetLength(0), cols = g.GetLength(1);
        if (g[c.y, c.x] > 0.05f) return c;
        for (int r = 1; r <= 8; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int y = c.y + dy, x = c.x + dx;
                    if (y >= 0 && x >= 0 && y < rows && x < cols && g[y, x] > 0.05f) return new Vector2Int(x, y);
                }
        return c;
    }

    void Run(IEnumerator routine)
    {
        if (busy != null) agent.StopCoroutine(busy);
        busy = agent.StartCoroutine(Wrap(routine));
    }
    IEnumerator Wrap(IEnumerator routine)
    {
        yield return routine;
        busy = null;
    }
    IEnumerator PauseThen(float realSeconds, Action then)
    {
        yield return Pause(realSeconds);
        busy = null;
        then();
    }
    // Standing reading the map: only where the runner thinks it is at the control and finds no flag.
    // The game runs faster than real time, so a real pause is scaled to race time.
    IEnumerator Pause(float realSeconds)
    {
        agent.isWaiting = true;
        yield return new WaitForSeconds(GameSeconds(realSeconds));
        agent.isWaiting = false;
    }

    float GameSeconds(float realSeconds)
    {
        float gameMetresPerSecond = agent.UnscaledBaseSpeed * TerrainSpeedModel.RaceSpeedScale() * Mpu;
        float seconds = gameMetresPerSecond > 0f ? realSeconds * RealSpeed100 / gameMetresPerSecond : 0.5f;
        return Mathf.Clamp(seconds, 0.05f, 3f);
    }

    // Unsure on the way (user's wish 2026-11-12): no stop-and-go. The runner reads the map on the run, at a slower pace
    // for a few seconds, losing about the same time as the old pause. Then the next step starts at once.
    const float ReadingPace = 0.45f;

    void SlowDown(float realSeconds)
    {
        agent.readingSlowFactor = ReadingPace;
        agent.readingSlowUntil = Time.time + GameSeconds(realSeconds) / (1f - ReadingPace);
    }

    IEnumerator SlowThen(float realSeconds, Action then)
    {
        SlowDown(realSeconds);
        yield return null;
        busy = null;
        then();
    }
}
