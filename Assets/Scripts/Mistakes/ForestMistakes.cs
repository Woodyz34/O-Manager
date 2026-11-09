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
//  * Every surprise makes the runner more careful for the rest of the leg.
// Tuned so that the time lost per control matches the technique scale of the runner cards
// (technique = 100 - 2 points per mistake minute per 100 controls): about 30 s per control at technique 0,
// 12 s at 60, 6 s at 80, 3 s at 90 and 0.3 s at 99.
public class ForestMistakes
{
    enum Mode { Route, Drift, Circle, Lost, Safe }

    const float H0 = 1f / 2300f;           // direction errors per metre at technique 0 in average ground
    const float RealSpeed100 = 3.6f;       // m/s on speed-1 ground for a speed-100 runner in the real world

    readonly AgentMovement agent;
    readonly System.Random rng = new System.Random();
    Mode mode = Mode.Route;
    float skill;                           // 0..1, grows with every surprise
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
        mode = Mode.Route; U = 8f; sinceFix = 0f; travel = 0f; misses = 0; nextContour = 25f;
        skill = baseSkill = Mathf.Clamp01(1f - agent.runnerStatTec);
        seen.Clear(); lostTargets.Clear();
    }

    float baseSkill;
    // After each surprise the runner gets more careful for the rest of the leg; the card's technique comes back next leg
    void Log(string what) { Debug.Log($"[Bom] {agent.runnerName}: {what} (teknik {baseSkill * 100f:F0}, försiktig nu som {skill * 100f:F0})"); }
    void Surprise(string what)
    {
        Log(what);
        skill = 1f - (1f - skill) * 0.4f;
    }

    float Rand() => (float)rng.NextDouble();
    float Gauss() { float u1 = 1f - Rand(), u2 = Rand(); return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2); }

    float GroundSpeed(Vector2 m)
    {
        Vector2Int c = agent.WorldToGridPositionCentered(m / Mpu);
        return agent.pathfindingManager.speedGrid2D[c.y, c.x];
    }
    float FlagSight() => (10f + 15f * Mathf.Min(1f, GroundSpeed(Pos))) * (MapFeatureIndex.Open(Pos) ? 1.6f : 1f);

    // Called every frame after the runner has moved
    public void Tick(Vector3 fromWorld, Vector3 toWorld)
    {
        if (!Enabled() || waitingForPath || busy != null) return;
        float d = Vector2.Distance(fromWorld, toWorld) * Mpu;
        if (d <= 0f) return;
        travel += d; sinceFix += d; runSinceAim += d;
        Vector2 pos = Pos;

        if (mode != Mode.Route && Vector2.Distance(pos, Control) < FlagSight())
        {
            Log("ser skärmen");
            ToControl();
            return;
        }
        switch (mode)
        {
            case Mode.Route:
                if (!MapFeatureIndex.OnHandrail(pos))
                {
                    float left = Vector2.Distance(pos, Control);
                    float diff = 0.6f + Mathf.Max(0f, 1.2f - MapFeatureIndex.Readability(pos) / 3f);
                    float nearControl = left < 70f ? 1.3f : 1f;   // drifting off happens all along the leg
                    float perMetre = H0 * Mathf.Pow(1f - skill, 0.15f) * diff * nearControl + 1f / 250000f;
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
        SetAim(Pos + wrong);
    }

    bool Noticed(float clarity, float boost) => Rand() < Mathf.Min(1f, clarity * (0.55f + 0.45f * skill) * boost);

    void Observe(float boost)
    {
        Vector2 pos = Pos;
        float g = GroundSpeed(pos);
        var pts = MapFeatureIndex.points;
        // Stones, knolls, pits ... seen further in open ground and from high ground
        MapFeatureIndex.QueryPoints(pos, 70f, near);
        foreach (int j in near.ToArray())
        {
            float c = pts[j].clarity;
            if (Vector2.Distance(pts[j].p, pos) > MapFeatureIndex.Sight(pos, pts[j].p, c, g)) continue;
            long key = j;
            if (seen.TryGetValue(key, out float at) && at > travel - 200f) continue;
            seen[key] = travel;
            if (!Noticed(c, boost)) continue;
            var cands = new List<Vector2>(); var ids = new List<long>();
            MapFeatureIndex.QueryPoints(Belief, U + 25f, near);
            foreach (int k in near) if (pts[k].group == pts[j].group) { cands.Add(pts[k].p); ids.Add(k); }
            if (Resolve(cands, ids, j, pts[j].p, c, 10f, false)) return;
        }
        // Paths, streams, fences ... when you cross or run along them
        var smp = MapFeatureIndex.samples;
        MapFeatureIndex.QuerySamples(pos, 4f, near);
        foreach (int j in near.ToArray())
        {
            long key = (1L << 40) | (uint)smp[j].line;
            if (seen.TryGetValue(key, out float at) && at > travel - 120f) continue;
            seen[key] = travel;
            float c = smp[j].clarity;
            // A road by houses and gardens, or a junction that is the only one round here or has something clear by it:
            // you know exactly where you are
            if (smp[j].roadSure && mode != Mode.Route) { Fix(); return; }
            // Another junction: which one of the junctions round here is it?
            if (smp[j].junction && mode != Mode.Route)
            {
                var jc = new List<Vector2>(); var jid = new List<long>(); var have = new HashSet<long>();
                MapFeatureIndex.QuerySamples(Belief, U + 60f, near);
                foreach (int k in near.ToArray())
                {
                    if (!smp[k].junction) continue;
                    long cl = JunctionKey(smp[k].p);
                    if (have.Add(cl)) { jc.Add(smp[k].p); jid.Add(cl); }
                }
                long trueCl = JunctionKey(smp[j].p);
                if (!have.Contains(trueCl)) { jc.Add(smp[j].p); jid.Add(trueCl); }
                if (Resolve(jc, jid, trueCl, smp[j].p, 0.9f, 25f, false)) return;
                continue;
            }
            if (!Noticed(c, boost * 1.1f)) continue;
            bool distinct = smp[j].distinct && Rand() < 0.4f + 0.5f * skill;
            var best = new Dictionary<int, (float, Vector2)>();
            Vector2 bel = Belief;
            MapFeatureIndex.QuerySamples(bel, U + 25f, near);
            foreach (int k in near)
            {
                if (smp[k].group != smp[j].group || (distinct && !smp[k].distinct)) continue;
                float dd = (smp[k].p - bel).sqrMagnitude;
                if (!best.TryGetValue(smp[k].line, out var b) || dd < b.Item1) best[smp[k].line] = (dd, smp[k].p);
            }
            var cands = new List<Vector2>(); var ids = new List<long>();
            foreach (var b in best) { cands.Add(b.Value.Item2); ids.Add(b.Key); }
            if (Resolve(cands, ids, smp[j].line, smp[j].p, c, 10f, false)) return;
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
            var best = new Dictionary<int, (float, Vector2)>();
            Vector2 bel = Belief; var tmpList = new List<int>();
            MapFeatureIndex.QueryAreas(bel, U + 40f, tmpList);
            foreach (int k in tmpList)
            {
                if (areas[k].small != areas[j].small) continue;
                float dd = (areas[k].p - bel).sqrMagnitude;
                if (!best.TryGetValue(areas[k].area, out var b) || dd < b.Item1) best[areas[k].area] = (dd, areas[k].p);
            }
            var cands = new List<Vector2>(); var ids = new List<long>();
            foreach (var b in best) { cands.Add(b.Value.Item2); ids.Add(b.Key); }
            // Matching it to the right house or plot puts you within about 25 m
            if (Resolve(cands, ids, areas[j].area, areas[j].p, 0.9f, 25f, false)) return;
            break;
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
            var cands = new List<Vector2>(); var ids = new List<long>();
            Vector2 bel = Belief;
            for (int h = 0; h < hills.Count; h++) if (Vector2.Distance(hills[h].p, bel) < U + 80f) { cands.Add(hills[h].p); ids.Add(h); }
            if (Resolve(cands, ids, j, hills[j].p, hills[j].clarity, hills[j].size + 10f, true)) return;
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

    // Seen something at truePos: which one on the map is it? Returns true when the runner changed its mind.
    // tol: how close the right match puts you (a stone: 10 m, a house: 25 m). hill: the right hill only says
    // roughly where you are, so you aim from its top.
    bool Resolve(List<Vector2> cands, List<long> ids, long trueId, Vector2 truePos, float clarity, float tol, bool hill)
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
        float u = Mathf.Max(U, 10f);
        float[] w = new float[cands.Count];
        float sum = 0f, max = 0f;
        for (int i = 0; i < w.Length; i++)
        {
            w[i] = Mathf.Exp(-(cands[i] - bel).sqrMagnitude / (2f * u * u)) + 1e-12f;
            // Reading the detail round it: the right one gets a bonus that grows with technique
            if (ids[i] == trueId) w[i] *= 1f + 8f * skill;
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
            // The right hill, road or area, but not exactly where on it (you know which side of the road you are):
            // aim from the place on it you think you are
            Log("vet ungefär var den är");
            U = 20f; sinceFix = 0f; mode = Mode.Drift;
            SetAim(pos + (Control - chosen));
            return true;
        }
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
                if (g <= 0.05f) continue;
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
        Run(PauseThen(3f + 10f * (1f - skill), NextLostTarget));
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
        if (!found) { Fix(); return; }
        lostTargets.Add(bestKey); seen.Remove(bestKey);
        PathTo(target);
    }

    IEnumerator ReadAndGoOn()
    {
        yield return Pause(2f + 4f * (1f - skill));
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
        U = 8f; sinceFix = 0f;
        // Stop for a few seconds of the runner's time ("now I see, I'm over here"), then run the right way
        if (mode != Mode.Route) Run(PauseThen(3.5f, ToControl));
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
    // Standing reading the map. The game runs faster than real time, so a real pause is scaled to race time.
    IEnumerator Pause(float realSeconds)
    {
        agent.isWaiting = true;
        float gameMetresPerSecond = agent.UnscaledBaseSpeed * TerrainSpeedModel.RaceSpeedScale() * Mpu;
        float seconds = gameMetresPerSecond > 0f ? realSeconds * RealSpeed100 / gameMetresPerSecond : 0.5f;
        yield return new WaitForSeconds(Mathf.Clamp(seconds, 0.05f, 3f));
        agent.isWaiting = false;
    }
}
