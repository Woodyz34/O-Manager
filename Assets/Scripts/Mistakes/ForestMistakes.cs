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

    const float H0 = 1f / 4000f;           // direction errors per metre at technique 0 in average ground
    const float RealSpeed100 = 3.6f;       // m/s on speed-1 ground for a speed-100 runner in the real world

    readonly AgentMovement agent;
    readonly System.Random rng = new System.Random();
    Mode mode = Mode.Route;
    float skill;                           // 0..1, grows with every surprise
    Vector2 aim, lostBelief;               // metres
    float U = 8f;                          // how unsure the runner is of its place, metres
    float sinceFix, runSinceAim, expect, travel, lostMetres, doubt, doubtNeeded, circleSimilarity;
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
        mode = Mode.Route; U = 8f; sinceFix = 0f; travel = 0f; misses = 0;
        skill = Mathf.Clamp01(1f - agent.runnerStatTec);
        seen.Clear(); lostTargets.Clear();
    }

    void Log(string what) { Debug.Log($"[Bom] {agent.runnerName}: {what} (teknik nu {skill * 100f:F0})"); }
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
    float FlagSight() => 10f + 15f * Mathf.Min(1f, GroundSpeed(Pos));

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
                    float nearControl = left < 70f ? 2.5f : 1f;
                    float perMetre = H0 * Mathf.Pow(1f - skill, 0.8f) * diff * nearControl + 1f / 250000f;
                    if (Rand() < perMetre * d) DirectionError();
                }
                return;
            case Mode.Drift:
                U = 12f + 0.18f * sinceFix;
                if (runSinceAim > (1.15f + 0.15f * (1f - skill)) * expect + 20f)
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
                lostMetres += d; U += 0.15f * d / 2f;
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
            case Mode.Circle: GoLost(); break;
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
        float a = (6f + Mathf.Abs(Gauss()) * (5f + 12f * (1f - skill))) * (Rand() < 0.5f ? -1f : 1f);
        float k = 1f + Gauss() * (0.04f + 0.08f * (1f - skill));
        Vector2 toControl = Control - Pos;
        float rad = a * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
        Vector2 wrong = new Vector2(toControl.x * cos - toControl.y * sin, toControl.x * sin + toControl.y * cos) * k;
        Log($"riktningsfel {a:F0}°");
        mode = Mode.Drift; sinceFix = 0f;
        SetAim(Pos + wrong);
    }

    void Observe(float boost)
    {
        Vector2 pos = Pos;
        float g = Mathf.Min(1f, GroundSpeed(pos));
        MapFeatureIndex.QueryPoints(pos, 25f, near);
        var pts = MapFeatureIndex.points;
        foreach (int j in near.ToArray())
        {
            float c = pts[j].clarity;
            if (Vector2.Distance(pts[j].p, pos) > 6f + 18f * c * g) continue;
            long key = j;
            if (seen.TryGetValue(key, out float at) && at > travel - 200f) continue;
            seen[key] = travel;
            if (Rand() >= c * (0.55f + 0.45f * skill) * boost) continue;
            var cands = new List<Vector2>();
            MapFeatureIndex.QueryPoints(Belief, U + 25f, near);
            foreach (int k in near) if (pts[k].group == pts[j].group) cands.Add(pts[k].p);
            if (Resolve(cands, c)) return;
        }
        MapFeatureIndex.QuerySamples(pos, 4f, near);
        var smp = MapFeatureIndex.samples;
        foreach (int j in near.ToArray())
        {
            long key = (1L << 40) | (uint)smp[j].line;
            if (seen.TryGetValue(key, out float at) && at > travel - 120f) continue;
            seen[key] = travel;
            float c = smp[j].clarity;
            if (Rand() >= Mathf.Min(1f, c * (0.6f + 0.4f * skill) * boost)) continue;
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
            var cands = new List<Vector2>();
            foreach (var b in best.Values) cands.Add(b.Item2);
            if (Resolve(cands, c)) return;
        }
    }

    // Seen something: which one on the map is it? Returns true when the runner changed its mind
    bool Resolve(List<Vector2> cands, float clarity)
    {
        float frac = runSinceAim / Mathf.Max(expect, 1f);
        if (cands.Count == 0)
        {
            if (mode == Mode.Drift && clarity >= 0.7f && frac > 0.5f && Rand() < 0.5f + 0.5f * skill)
            {
                Surprise("det här stämmer inte");
                GoLost();
                return true;
            }
            U += 10f;
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
            if (Vector2.Distance(cands[i], pos) < 10f) w[i] *= 1f + 8f * skill;
            sum += w[i];
        }
        for (int i = 0; i < w.Length; i++) { w[i] /= sum; max = Mathf.Max(max, w[i]); }
        float r = Rand(); int pick = w.Length - 1;
        for (int i = 0; i < w.Length; i++) { r -= w[i]; if (r <= 0f) { pick = i; break; } }
        if (max < 0.75f && Rand() < skill * (1f - max) * 1.5f)
        {
            U += 5f;   // not sure which one: keep going the same way
            return false;
        }
        Vector2 chosen = cands[pick];
        if (Vector2.Distance(chosen, pos) < 10f)
        {
            Fix();
            return true;
        }
        // Thinks it is at the chosen one: aims at the control from there (a parallel error)
        Surprise("tror sig vara på fel ställe (parallellfel)");
        U = 15f; sinceFix = 0f; mode = Mode.Drift;
        SetAim(pos + (Control - chosen));
        return true;
    }

    void AtEmptySite()
    {
        misses++;
        circleSimilarity = MapFeatureIndex.Similarity(MapFeatureIndex.Signature(Pos), MapFeatureIndex.Signature(Control));
        Surprise($"ingen skärm här (likhet med kontrollområdet {circleSimilarity:F2})");
        if (circleSimilarity <= 0.3f) { Run(PauseThen(2f + 5f * (1f - skill), GoLost)); return; }
        // Looks like the control's ground: circle here while what you see still fits
        doubt = 0f; doubtNeeded = 1f + 3f * (1f - skill);
        lostBelief = Belief;
        mode = Mode.Circle;
        Vector2 centre = Pos;
        float ang = Rand() * 6.28f, rad = 15f;
        var cells = new List<Vector2Int>();
        while (rad < 60f)
        {
            ang += 2f / rad; rad += 0.25f;
            Vector2Int c = agent.WorldToGridPositionCentered((centre + rad * new Vector2(Mathf.Cos(ang), Mathf.Sin(ang))) / Mpu);
            if (agent.pathfindingManager.speedGrid2D[c.y, c.x] <= 0.05f) continue;
            if (cells.Count == 0 || cells[cells.Count - 1] != c) cells.Add(c);
        }
        if (cells.Count < 2) { GoLost(); return; }
        Run(PauseThen(2f + 5f * (1f - skill), () => agent.SetMistakePath(cells)));
    }

    void CircleDoubt()
    {
        var pts = MapFeatureIndex.points;
        MapFeatureIndex.QueryPoints(Pos, 12f, near);
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
        if (mode != Mode.Circle) lostBelief = Belief;
        mode = Mode.Lost; U = Mathf.Max(U, 50f); lostMetres = 0f; lostTargets.Clear();
        Run(PauseThen(3f + 10f * (1f - skill), NextLostTarget));
    }

    // Go to the clearest thing near and read the map there
    void NextLostTarget()
    {
        mode = Mode.Lost;
        if (lostTargets.Count > 8 || lostMetres > 300f + 200f * skill) { Fix(); return; }
        Vector2 pos = Pos; float bestScore = 0f; long bestKey = 0; Vector2 target = pos; bool found = false;
        var pts = MapFeatureIndex.points; var smp = MapFeatureIndex.samples;
        if (misses >= 2)
        {
            // Two empty sites: go for something big and certain
            MapFeatureIndex.QuerySamples(pos, 400f, near);
            for (int n = 0; n < near.Count; n += 2)
            {
                int j = near[n];
                if (smp[j].clarity < 0.6f) continue;
                float sc = smp[j].clarity * (smp[j].distinct ? 1.5f : 1f) / (Vector2.Distance(smp[j].p, pos) + 30f);
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
            float sc = pts[j].clarity * pts[j].unique / (Vector2.Distance(pts[j].p, pos) + 30f);
            if (sc > bestScore) { bestScore = sc; bestKey = j; target = pts[j].p; found = true; }
        }
        MapFeatureIndex.QuerySamples(pos, 150f, near);
        for (int n = 0; n < near.Count; n += 3)
        {
            int j = near[n];
            long key = (1L << 40) | (uint)smp[j].line;
            if (smp[j].clarity < 0.5f || lostTargets.Contains(key)) continue;
            float sc = smp[j].clarity * (smp[j].distinct ? 1.3f : 0.7f) / (Vector2.Distance(smp[j].p, pos) + 30f);
            if (sc > bestScore) { bestScore = sc; bestKey = key; target = smp[j].p; found = true; }
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

    void Fix()
    {
        Log("vet var den är");
        U = 8f; sinceFix = 0f;
        ToControl();
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
            if (result == null || result.Count == 0) result = new List<Vector2Int> { start, goal };
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
