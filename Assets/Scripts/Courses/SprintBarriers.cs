using System;
using System.Collections.Generic;
using UnityEngine;

// Sprint AI courses: out-of-bounds barriers (purple stripes, ISSprOM 709) that close the obvious way straight on,
// as a course setter closes a passage between two houses, so the runners must choose between going round on the
// left or on the right.
//
// Never at random. Only legs with one clearly fastest way get a barrier, and only across a narrow passage that this
// way runs through (between two houses, a gap in a fence, a narrow street): from wall to wall, so it looks like a
// real closed passage. Each place is tried with the same route choice search as the route choice game, and a barrier
// is only kept when afterwards there are two route choices close in time (CloseChoice), one passing the barrier on
// the left and one on the right, and the leg has not become a long detour. When one barrier gives a new way that is
// still clearly fastest, a second barrier can close that one too. A barrier that spoils another leg is left out.
// Plain C# with no Unity objects, so it runs on a background thread.
public class SprintBarriers
{
    public const float MaxGapMeters = 40f;     // only passages this narrow are closed
    const float MinGapMeters = 1.5f;
    const float DepthMeters = 4f;              // how thick a barrier is along the way it closes
    const float IntoWallMeters = 3f;           // a barrier reaches this far into the house or fence on each side, so no gap is seen or left
    const float ControlRoomMeters = 25f;       // never this close to a control, the start or the finish
    const float GapSpacingMeters = 10f;        // places tried on one leg at least this far apart
    const float MaxSlowdown = 1.6f;            // the leg at most this much slower than its straight way was
    const float MaxMiddleSlowdown = 1.6f;      // a little more when an obvious middle or fastest way is closed too
    const float SkirtMeters = 5f;              // the route choices keep this far from a barrier: they go round the houses, not round its end
    const int MinWallCells = 12;               // a barrier ends on something big (a house, a plot, a fence), not a tree or a boulder
    const float OtherLegSlack = 1.10f;         // a leg the barrier was not for may get at most this much slower (unless it gets a close choice)
    const int MaxPerLeg = 4;                   // barriers that close the fastest way, one after the other, on one leg
    const int TriesPerRound = 8;
    const float BarriersPerControl = 1.5f;     // the course gets about this many barriers per control
    const int MaxPlainSteps = 2;               // barriers in a row that leave a leg with no close choice, on the way to one
    const int MaxFillTries = 40;               // passages tried for barriers that only make the map new
    const float MinBetweenMeters = 15f;        // two barriers at least this far apart
    const int MaxMiddle = 2;                   // extra barriers per leg that close routes between the left and the right choice
    public const int MaxBarriers = 60;         // per course
    const float TargetSlack = 1.05f;           // a barrier leg counts as even when its two best choices are within close x this

    public class Barrier
    {
        public List<Vector2> corners;   // grid coordinates (x = column, y = row, cell j spans j to j + 1)
        public int leg;
    }

    class LegState
    {
        public bool blocked;              // no way through at all
        public float ratio = float.MaxValue;  // second fastest choice / fastest (max: one clear way)
        public float fastest;             // seconds of the fastest way
        public List<RouteChoice> choices = new List<RouteChoice>();
        public List<Vector2Int> fastestCells;
        public bool leftRight;            // the two fastest choices go on different sides of the straight line
        public bool Fun(float close) => !blocked && ratio <= close;
        public bool Balanced(float close) => Fun(close) && leftRight;
    }

    class Gap
    {
        public Vector2 centre, normal, along, a, b;   // a, b: where the passage meets the walls
        public float width;               // cells
        public List<Vector2> corners;
        public int leg;
    }

    readonly float[,] speed;
    readonly float[,] original;       // the grid before any barrier, to open one again
    readonly RouteChoiceGenerator generator;
    readonly float cellMeters;
    readonly float close;
    readonly Func<Vector2, Vector2> snap;
    readonly DateTime started = DateTime.UtcNow;
    readonly double budgetSeconds;

    public int FunBefore { get; private set; }
    public int FunAfter { get; private set; }
    public List<string> Log { get; } = new List<string>();
    public bool Verbose;

    // speed: a copy of the grid the generator searches; the kept barriers are left closed in it.
    // snap: moves a corner to where it ends up once saved (whole metres), so the barriers tried are the ones raced.
    public SprintBarriers(float[,] speed, RouteChoiceGenerator generator, float cellMeters, float closeChoice,
                          double budgetSeconds, Func<Vector2, Vector2> snap = null)
    {
        this.speed = speed;
        original = (float[,])speed.Clone();
        this.generator = generator;
        this.cellMeters = cellMeters;
        close = closeChoice;
        this.budgetSeconds = budgetSeconds;
        this.snap = snap;
    }

    bool OutOfTime => (DateTime.UtcNow - started).TotalSeconds > budgetSeconds;

    // The barriers for a course: points = start, controls and finish as grid cells
    public List<Barrier> Plan(List<Vector2Int> points)
    {
        List<Barrier> kept = new List<Barrier>();
        int legs = points.Count - 1;
        int controls = Mathf.Max(1, points.Count - 2);
        goal = Wanted(controls);
        LegState[] state = new LegState[legs + 1];
        for (int k = 1; k <= legs; k++) state[k] = Measure(points, k);
        for (int k = 1; k <= legs; k++) if (state[k].Fun(close)) FunBefore++;

        // Every leg long enough: first the ones with one clear way, then the ones that already have a close choice
        // (they get barriers too, so the choice is not just round the nearest house); longest first (on a short leg
        // a detour is soon too much)
        List<int> order = new List<int>();
        for (int k = 1; k <= legs; k++)
        {
            if (state[k].blocked) continue;
            if (Vector2.Distance(points[k - 1], points[k]) * cellMeters < 60f) continue;
            order.Add(k);
        }
        order.Sort((a, b) =>
        {
            int funA = state[a].Fun(close) ? 1 : 0, funB = state[b].Fun(close) ? 1 : 0;
            if (funA != funB) return funA.CompareTo(funB);
            return Vector2.Distance(points[b - 1], points[b]).CompareTo(Vector2.Distance(points[a - 1], points[a]));
        });

        foreach (int k in order)
        {
            if (OutOfTime || kept.Count >= MaxBarriers) break;
            TryLeg(points, k, state, kept);
        }
        // Still short of the number wanted: more barriers that change the map without spoiling any leg
        if (kept.Count < goal) Fill(points, state, kept);
        // Last: every barrier must still change a way. One placed early can be left doing nothing once later ones
        // closed more (the runners no longer come near it), so each is opened again on its own and taken away
        // when no leg's choices change.
        Prune(points, state, kept);
        FunAfter = 0;
        for (int k = 1; k <= legs; k++) if (state[k].Fun(close)) FunAfter++;
        return kept;
    }

    int goal;

    // How many barriers a course with this many controls should get
    public static int Wanted(int controls) => Mathf.Min(MaxBarriers, Mathf.RoundToInt(Mathf.Max(1, controls) * BarriersPerControl));

    // One leg: closes the way that is fastest now, again and again, while the leg keeps (or gets) a close choice.
    // Each barrier sends the runners further out, so the choice is no longer just left or right round the nearest
    // house but a longer way that is harder to judge. Stops when the leg has LegGoal barriers, or the next one would
    // leave it with one clear way or too long a detour.
    void TryLeg(List<Vector2Int> points, int k, LegState[] state, List<Barrier> kept)
    {
        float target = close * TargetSlack;
        LegState original = state[k];
        LegState current = original;
        List<KeyValuePair<int, float>> changed = new List<KeyValuePair<int, float>>();
        List<Gap> placed = new List<Gap>();
        // The last state where the leg was even, and how much had been closed then (to go back to it)
        LegState lastFun = null;
        int lastFunChanged = 0, lastFunPlaced = 0;
        int legGoal = LegGoal(points, k);
        int plainSteps = 0;
        for (int round = 0; round < MaxPerLeg + MaxPlainSteps && kept.Count + placed.Count < MaxBarriers && !OutOfTime; round++)
        {
            if (lastFun != null && lastFunPlaced >= legGoal) break;
            if (placed.Count >= MaxPerLeg) break;
            List<Gap> gaps = FindGaps(current.fastestCells, points);
            if (Verbose) Log.Add($"   leg {k} round {round}: {gaps.Count} passages");
            Gap best = null;
            LegState bestState = null;
            float bestScore = float.MinValue;
            int tried = 0;
            foreach (Gap gap in gaps)
            {
                if (tried++ >= TriesPerRound || OutOfTime) break;
                if (TooClose(gap, kept, placed)) continue;
                List<KeyValuePair<int, float>> trial = new List<KeyValuePair<int, float>>();
                ForbiddenRaster.Close(speed, gap.corners, false, trial);
                // A quick look first (one search, not the whole route choice search): a barrier the fastest way just
                // slips past, or one that makes the leg far too long, is no use
                RouteChoice quick = generator.Fastest(points[k - 1], points[k]);
                if (quick == null || quick.seconds < current.fastest * 1.01f || quick.seconds > original.fastest * MaxSlowdown)
                {
                    ForbiddenRaster.Undo(speed, trial);
                    continue;
                }
                LegState after = Measure(points, k);
                ForbiddenRaster.Undo(speed, trial);
                if (after.blocked || after.fastest > original.fastest * MaxSlowdown) continue;
                bool away = KeepsAway(after, gap, placed);
                bool fun = after.Fun(target) && away;
                if (Verbose) Log.Add($"   leg {k} round {round} gap {gap.width * cellMeters:0.0} m at ({gap.centre.x:0},{gap.centre.y:0}): "
                                     + $"{after.fastest / original.fastest:0.00}x slower, {Describe(after)}, away {away}");
                // Even choices first; of those the most even, a little in favour of a longer way round (harder to see)
                // (a step with no close choice: the one that brings the choices closest, else the shortest detour)
                float slower = after.fastest / original.fastest - 1f;
                float score = fun ? 10f - (after.ratio - 1f) * 20f + slower * 2f
                                  : after.ratio < 100f ? -(after.ratio - 1f) * 10f - slower : -5f - slower;
                if (score > bestScore) { best = gap; bestState = after; bestScore = score; }
            }
            if (best == null) break;
            bool bestFun = bestState.Fun(target) && KeepsAway(bestState, best, placed);
            // One that leaves no close choice is a step on the way: the next barrier may give one (the way that is
            // fastest now is closed too). At most MaxPlainSteps such steps in a row; the leg goes back to its last
            // even state at the end.
            plainSteps = bestFun ? 0 : plainSteps + 1;
            if (plainSteps > MaxPlainSteps) break;
            ForbiddenRaster.Close(speed, best.corners, false, changed);
            placed.Add(best);
            current = bestState;
            if (bestFun) { lastFun = current; lastFunChanged = changed.Count; lastFunPlaced = placed.Count; }
        }
        // Back to the last even state (later steps that did not end even are opened again)
        if (lastFun != null && placed.Count > lastFunPlaced)
        {
            ForbiddenRaster.Undo(speed, changed.GetRange(lastFunChanged, changed.Count - lastFunChanged));
            changed.RemoveRange(lastFunChanged, changed.Count - lastFunChanged);
            placed.RemoveRange(lastFunPlaced, placed.Count - lastFunPlaced);
            current = lastFun;
        }
        bool ok = lastFun != null && placed.Count > 0;
        // Tidy up: when the leg has more choices and the slowest is much slower, close the fastest again so they
        // are all about as good
        if (ok) current = CloseMiddle(points, k, current, placed, changed, target, kept.Count, original);

        string why = placed.Count == 0 ? "no passage that keeps it even" : "no even choice";
        List<KeyValuePair<int, LegState>> others = ok ? OthersStillGood(points, k, state, placed, ref ok, ref why) : null;
        if (!ok)
        {
            ForbiddenRaster.Undo(speed, changed);
            Log.Add($"leg {k}: no barrier ({why})");
            return;
        }
        state[k] = current;
        foreach (var pair in others) state[pair.Key] = pair.Value;
        foreach (Gap gap in placed) kept.Add(new Barrier { corners = gap.corners, leg = k });
        Log.Add($"leg {k}: {placed.Count} barrier(s), {original.fastest:0}s straight -> choices {Describe(current)}");
    }

    // How many barriers a leg should get: more on long legs (one per 100 m, at least 1, at most MaxPerLeg)
    int LegGoal(List<Vector2Int> points, int k)
    {
        float meters = Vector2.Distance(points[k - 1], points[k]) * cellMeters;
        return Mathf.Clamp(Mathf.RoundToInt(meters / 100f) + 1, 1, MaxPerLeg);
    }

    // The other legs must not get worse: a leg with close choices keeps them, and every leg can still be run.
    // Null (and ok false) when one does.
    List<KeyValuePair<int, LegState>> OthersStillGood(List<Vector2Int> points, int k, LegState[] state, List<Gap> placed,
                                                      ref bool ok, ref string why)
    {
        List<KeyValuePair<int, LegState>> others = new List<KeyValuePair<int, LegState>>();
        for (int j = 1; j < state.Length && ok; j++)
        {
            if (j == k || !Near(state[j], placed)) continue;
            LegState again = Measure(points, j);
            // Another leg may get close choices from it, but must not lose them, and a leg that stays plain must
            // keep its way (a barrier for one leg should not quietly close the best way of the next)
            bool wasFun = state[j].Fun(close), nowFun = again.Fun(close);
            if (again.blocked || (wasFun && !nowFun) || (!nowFun && again.fastest > state[j].fastest * OtherLegSlack)
                || again.fastest > state[j].fastest * MaxSlowdown)
            {
                ok = false;
                why = "it would spoil leg " + j;
            }
            else others.Add(new KeyValuePair<int, LegState>(j, again));
        }
        return ok ? others : null;
    }

    // More barriers that make the map new without making any leg worse: passages on the ways the legs take now
    // (any of their choices), each kept only when every leg near it keeps a close choice (or, when it had none,
    // is hardly slower). They take away a slower choice, or send a choice round another block.
    void Fill(List<Vector2Int> points, LegState[] state, List<Barrier> kept)
    {
        float target = close * TargetSlack;
        List<Gap> candidates = new List<Gap>();
        for (int k = 1; k < state.Length; k++)
        {
            if (state[k].blocked) continue;
            foreach (RouteChoice c in state[k].choices)
            {
                foreach (Gap g in FindGaps(c.cells, points))
                {
                    bool dup = false;
                    foreach (Gap o in candidates) if (Vector2.Distance(o.centre, g.centre) < GapSpacingMeters / cellMeters) { dup = true; break; }
                    if (!dup) { g.leg = k; candidates.Add(g); }
                }
            }
        }
        // Narrow ones first (they look most like a real closed passage), mixed so they spread over the course
        candidates.Sort((a, b) => a.width.CompareTo(b.width));
        int tries = 0;
        foreach (Gap gap in candidates)
        {
            if (kept.Count >= goal || OutOfTime || tries >= MaxFillTries) break;
            if (TooClose(gap, kept, null)) continue;
            tries++;
            List<KeyValuePair<int, float>> trial = new List<KeyValuePair<int, float>>();
            ForbiddenRaster.Close(speed, gap.corners, false, trial);
            List<Gap> one = new List<Gap> { gap };
            bool ok = true;
            List<KeyValuePair<int, LegState>> again = new List<KeyValuePair<int, LegState>>();
            bool changes = false;
            for (int j = 1; j < state.Length && ok; j++)
            {
                if (!Near(state[j], one)) continue;
                LegState now = Measure(points, j);
                bool wasFun = state[j].Fun(close), nowFun = now.Fun(close);
                if (now.blocked || (wasFun && !nowFun) || (!nowFun && now.fastest > state[j].fastest * OtherLegSlack)
                    || now.fastest > state[j].fastest * MaxSlowdown || (nowFun && !KeepsAway(now, gap, one)))
                {
                    ok = false;
                }
                else
                {
                    again.Add(new KeyValuePair<int, LegState>(j, now));
                    if (!Same(now, state[j])) changes = true;
                }
            }
            // It must change some leg's choices; one that changes nothing is only clutter
            if (!changes) ok = false;
            if (Verbose) Log.Add($"   fill gap {gap.width * cellMeters:0.0} m at ({gap.centre.x:0},{gap.centre.y:0}) near leg {gap.leg}: {(ok ? "KEPT" : "no")}");
            if (!ok || again.Count == 0)
            {
                ForbiddenRaster.Undo(speed, trial);
                continue;
            }
            foreach (var pair in again) state[pair.Key] = pair.Value;
            kept.Add(new Barrier { corners = gap.corners, leg = gap.leg });
            Log.Add($"fill: barrier by leg {gap.leg} ({gap.width * cellMeters:0} m)");
        }
    }

    void Prune(List<Vector2Int> points, LegState[] state, List<Barrier> kept)
    {
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        float[,] mark = null;
        DateTime pruneStart = DateTime.UtcNow;
        for (int b = kept.Count - 1; b >= 0; b--)
        {
            if ((DateTime.UtcNow - pruneStart).TotalSeconds > budgetSeconds * 0.5) break;
            Barrier barrier = kept[b];
            // The cells this barrier closed, opened again (to the speed they had before any barrier)
            if (mark == null)
            {
                mark = new float[rows, cols];
                for (int i = 0; i < rows; i++) for (int j = 0; j < cols; j++) mark[i, j] = 1f;
            }
            List<KeyValuePair<int, float>> cells = new List<KeyValuePair<int, float>>();
            ForbiddenRaster.Close(mark, barrier.corners, false, cells);
            ForbiddenRaster.Undo(mark, new List<KeyValuePair<int, float>>(cells));
            List<KeyValuePair<int, float>> reopened = new List<KeyValuePair<int, float>>();
            foreach (var c in cells)
            {
                int i = c.Key / cols, j = c.Key % cols;
                reopened.Add(new KeyValuePair<int, float>(c.Key, speed[i, j]));
                speed[i, j] = original[i, j];
            }
            // Another barrier over the same cells stays closed
            foreach (Barrier other in kept) if (other != barrier) ForbiddenRaster.Close(speed, other.corners, false);

            Vector2 centre = Vector2.zero;
            foreach (Vector2 c in barrier.corners) centre += c;
            centre /= barrier.corners.Count;
            float reach = 0f;
            foreach (Vector2 c in barrier.corners) reach = Mathf.Max(reach, Vector2.Distance(c, centre));
            bool changes = false;
            List<KeyValuePair<int, LegState>> without = new List<KeyValuePair<int, LegState>>();
            for (int k = 1; k < state.Length && !changes; k++)
            {
                // Opening can give a leg back a way it had before, which its present choices may not pass near, so every
                // leg whose search window (as the route choice search) holds the barrier is looked at
                if (!InWindow(points, k, centre, reach)) continue;
                LegState now = Measure(points, k);
                if (!Same(now, state[k])) changes = true;
                else without.Add(new KeyValuePair<int, LegState>(k, now));
            }
            if (changes)
            {
                ForbiddenRaster.Undo(speed, reopened);   // closed again
                continue;
            }
            foreach (var pair in without) state[pair.Key] = pair.Value;
            kept.RemoveAt(b);
            Log.Add($"pruned a barrier by leg {barrier.leg}: no leg's choices change without it");
        }
    }

    // Is a point inside the window the route choice search looks at for a leg (its straight distance round it, at least 80 m)?
    bool InWindow(List<Vector2Int> points, int k, Vector2 p, float reach)
    {
        Vector2Int a = points[k - 1], b = points[k];
        float margin = Mathf.Max(0.6f * Vector2.Distance(a, b) * cellMeters, 80f) / cellMeters + reach;
        return p.x >= Mathf.Min(a.x, b.x) - margin && p.x <= Mathf.Max(a.x, b.x) + margin
            && p.y >= Mathf.Min(a.y, b.y) - margin && p.y <= Mathf.Max(a.y, b.y) + margin;
    }

    // The same choices: as many, and each about as fast
    static bool Same(LegState a, LegState b)
    {
        if (a.blocked != b.blocked || a.choices.Count != b.choices.Count) return false;
        List<float> x = new List<float>(), y = new List<float>();
        foreach (RouteChoice c in a.choices) x.Add(c.seconds);
        foreach (RouteChoice c in b.choices) y.Add(c.seconds);
        x.Sort(); y.Sort();
        for (int i = 0; i < x.Count; i++) if (Mathf.Abs(x[i] - y[i]) > Mathf.Max(1f, 0.01f * y[i])) return false;
        return true;
    }

    // Does any choice of a leg pass within reach (cells) of a point?
    static bool NearPoint(LegState s, Vector2 p, float reach)
    {
        if (s.blocked) return false;
        foreach (RouteChoice c in s.choices)
            foreach (Vector2Int cell in c.cells)
                if (Vector2.Distance(new Vector2(cell.x + 0.5f, cell.y + 0.5f), p) < reach) return true;
        return false;
    }

    // Two barriers never right next to each other (it would look like one odd shape)
    bool TooClose(Gap gap, List<Barrier> kept, List<Gap> placed)
    {
        float min = MinBetweenMeters / cellMeters;
        if (placed != null) foreach (Gap g in placed) if (Vector2.Distance(g.centre, gap.centre) < min) return true;
        foreach (Barrier b in kept)
        {
            Vector2 c = Vector2.zero;
            foreach (Vector2 p in b.corners) c += p;
            c /= b.corners.Count;
            if (Vector2.Distance(c, gap.centre) < min) return true;
        }
        return false;
    }

    // Evens out a leg with several choices: while the slowest choice is more than 'target' slower than the fastest,
    // the fastest (the obvious one) is closed, kept only when the rest are then more even. Every other choice
    // stays, so a leg can keep three or four ways as long as they are all about as good.
    LegState CloseMiddle(List<Vector2Int> points, int k, LegState current, List<Gap> placed,
                         List<KeyValuePair<int, float>> changed, float target, int keptCount, LegState start)
    {
        for (int round = 0; round < MaxMiddle && placed.Count < MaxPerLeg + MaxMiddle && keptCount + placed.Count < MaxBarriers && !OutOfTime; round++)
        {
            if (current.choices.Count < 3 || Spread(current) <= target) break;
            List<RouteChoice> byTime = new List<RouteChoice>(current.choices);
            byTime.Sort((x, y) => x.seconds.CompareTo(y.seconds));
            List<RouteChoice[]> tries = new List<RouteChoice[]> { new[] { byTime[0], null, null } };
            bool closed = false;
            foreach (RouteChoice[] t in tries)
            {
                if (closed || OutOfTime) break;
                int tried = 0;
                foreach (Gap gap in FindGaps(t[0].cells, points))
                {
                    if (tried++ >= 4 || OutOfTime) break;
                    if (t[1] != null && (Crosses(t[1].cells, gap) || Crosses(t[2].cells, gap))) continue;
                    List<KeyValuePair<int, float>> trial = new List<KeyValuePair<int, float>>();
                    ForbiddenRaster.Close(speed, gap.corners, false, trial);
                    LegState after = Measure(points, k);
                    List<Gap> with = new List<Gap>(placed) { gap };
                    bool good = !after.blocked && after.Fun(target) && after.fastest <= start.fastest * MaxMiddleSlowdown
                                && after.choices.Count >= 2 && Spread(after) < Spread(current)
                                && KeepsAway(after, gap, with)
                                && (t[1] == null || after.leftRight || BothSides(after, gap, with));
                    if (Verbose) Log.Add($"   leg {k} middle {t[0].seconds:0}s gap {gap.width * cellMeters:0.0} m at ({gap.centre.x:0},{gap.centre.y:0}): "
                                         + (after.blocked ? "blocked" : Describe(after) + (after.leftRight ? " L/R" : "") + (KeepsAway(after, gap, with) ? "" : " near")) + (good ? " KEPT" : ""));
                    if (!good)
                    {
                        ForbiddenRaster.Undo(speed, trial);
                        continue;
                    }
                    changed.AddRange(trial);
                    placed.Add(gap);
                    current = after;
                    closed = true;
                    break;
                }
            }
            if (!closed) break;
        }
        return current;
    }

    // How much slower the slowest choice is than the fastest
    static float Spread(LegState s)
    {
        float min = float.MaxValue, max = 0f;
        foreach (RouteChoice c in s.choices) { min = Mathf.Min(min, (float)c.seconds); max = Mathf.Max(max, (float)c.seconds); }
        return min > 0f && s.choices.Count > 0 ? max / min : float.MaxValue;
    }

    // Does a route run through a barrier's passage?
    bool Crosses(List<Vector2Int> route, Gap gap)
    {
        foreach (Vector2Int c in route)
        {
            Vector2 p = new Vector2(c.x + 0.5f, c.y + 0.5f);
            Vector2 ab = gap.b - gap.a;
            float t = Mathf.Clamp01(Vector2.Dot(p - gap.a, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
            if (Vector2.Distance(p, gap.a + ab * t) < 3f / cellMeters) return true;
        }
        return false;
    }

    static string Describe(LegState s)
    {
        List<string> parts = new List<string>();
        foreach (RouteChoice c in s.choices) parts.Add(c.seconds.ToString("0") + "s");
        return string.Join(" / ", parts) + " (" + ((s.ratio - 1f) * 100f).ToString("0") + " %)";
    }

    LegState Measure(List<Vector2Int> points, int k)
    {
        LegState s = new LegState();
        RouteChoiceLeg made = null;
        try { made = generator.Make(k, points[k - 1], points[k], k * 7919 + points[k - 1].x * 31 + points[k].y); }
        catch (Exception) { made = null; }
        if (made != null && made.choices.Count >= 2)
        {
            s.choices = made.choices;
            float fastest = float.MaxValue, second = float.MaxValue;
            foreach (RouteChoice c in made.choices)
            {
                if (c.seconds < fastest) { second = fastest; fastest = c.seconds; s.fastestCells = c.cells; }
                else if (c.seconds < second) second = c.seconds;
            }
            s.fastest = fastest;
            s.ratio = second / Mathf.Max(0.001f, fastest);
            List<RouteChoice> sorted = new List<RouteChoice>(made.choices);
            sorted.Sort((x, y) => x.seconds.CompareTo(y.seconds));
            s.leftRight = sorted[0].side * sorted[1].side < 0f;
            return s;
        }
        RouteChoice only = generator.Fastest(points[k - 1], points[k]);
        if (only == null) { s.blocked = true; return s; }
        s.fastest = only.seconds;
        s.fastestCells = only.cells;
        s.choices.Add(only);
        return s;
    }

    // The two fastest choices pass the barrier on different sides (or round different barriers)
    bool BothSides(LegState s, Gap gap, List<Gap> placed)
    {
        List<RouteChoice> sorted = new List<RouteChoice>(s.choices);
        sorted.Sort((a, b) => a.seconds.CompareTo(b.seconds));
        if (sorted.Count < 2) return false;
        List<Gap> all = new List<Gap>(placed);
        if (!all.Contains(gap)) all.Add(gap);
        foreach (Gap g in all)
        {
            int a = SideOf(sorted[0].cells, g), b = SideOf(sorted[1].cells, g);
            if (a != 0 && b != 0 && a != b) return true;
        }
        return false;
    }

    // The two fastest choices go properly round (the houses beside the barrier), not just round the end of it
    bool KeepsAway(LegState s, Gap gap, List<Gap> placed)
    {
        List<RouteChoice> sorted = new List<RouteChoice>(s.choices);
        sorted.Sort((x, y) => x.seconds.CompareTo(y.seconds));
        float skirt = SkirtMeters / cellMeters;
        List<Gap> all = new List<Gap>(placed);
        if (!all.Contains(gap)) all.Add(gap);
        for (int i = 0; i < Mathf.Min(2, sorted.Count); i++)
        {
            foreach (Vector2Int c in sorted[i].cells)
            {
                Vector2 p = new Vector2(c.x + 0.5f, c.y + 0.5f);
                foreach (Gap g in all)
                {
                    Vector2 ab = g.b - g.a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - g.a, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
                    if (Vector2.Distance(p, g.a + ab * t) < skirt) return false;
                }
            }
        }
        return true;
    }

    // Is the closed ground at a cell part of something big (at least MinWallCells closed cells joined together)?
    bool BigWall(Vector2 at)
    {
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        int x0 = Mathf.FloorToInt(at.x), y0 = Mathf.FloorToInt(at.y);
        if (x0 < 0 || y0 < 0 || x0 >= cols || y0 >= rows) return true;   // the map edge
        if (speed[y0, x0] > 0.01f) return false;
        HashSet<int> seen = new HashSet<int> { y0 * cols + x0 };
        Queue<int> queue = new Queue<int>();
        queue.Enqueue(y0 * cols + x0);
        while (queue.Count > 0 && seen.Count < MinWallCells)
        {
            int i = queue.Dequeue(), y = i / cols, x = i % cols;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) return true;
                    int n = ny * cols + nx;
                    if (speed[ny, nx] > 0.01f || !seen.Add(n)) continue;
                    queue.Enqueue(n);
                }
        }
        return seen.Count >= MinWallCells;
    }

    // Which side of a barrier a route passes, where it comes closest to it (0 when it is not near)
    int SideOf(List<Vector2Int> route, Gap gap)
    {
        float best = float.MaxValue;
        int side = 0;
        float reach = (gap.width / 2f + 40f / cellMeters);
        foreach (Vector2Int c in route)
        {
            Vector2 v = new Vector2(c.x + 0.5f, c.y + 0.5f) - gap.centre;
            float d = v.magnitude;
            if (d < best && d < reach)
            {
                best = d;
                side = Vector2.Dot(v, gap.normal) >= 0f ? 1 : -1;
            }
        }
        return side;
    }

    // Does any choice of a leg run near one of these barriers?
    bool Near(LegState s, List<Gap> gaps)
    {
        if (s.blocked) return false;
        foreach (RouteChoice c in s.choices)
        {
            foreach (Vector2Int cell in c.cells)
            {
                Vector2 p = new Vector2(cell.x + 0.5f, cell.y + 0.5f);
                foreach (Gap g in gaps)
                {
                    if (Vector2.Distance(p, g.centre) < g.width / 2f + 4f) return true;
                }
            }
        }
        return false;
    }

    // Narrow passages along a route (in its middle part, away from the controls), narrowest first
    List<Gap> FindGaps(List<Vector2Int> route, List<Vector2Int> points)
    {
        List<Gap> found = new List<Gap>();
        if (route == null || route.Count < 8) return found;
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        int look = Mathf.Max(1, Mathf.RoundToInt(4f / cellMeters));
        int every = Mathf.Max(1, Mathf.RoundToInt(2f / cellMeters));
        float maxCells = MaxGapMeters / cellMeters;
        float controlRoom = ControlRoomMeters / cellMeters;
        int first = Mathf.RoundToInt(route.Count * 0.15f), last = Mathf.RoundToInt(route.Count * 0.85f);
        for (int i = Mathf.Max(first, look); i <= Mathf.Min(last, route.Count - 1 - look); i += every)
        {
            Vector2 p = new Vector2(route[i].x + 0.5f, route[i].y + 0.5f);
            bool nearControl = false;
            foreach (Vector2Int c in points)
            {
                if (Vector2.Distance(p, new Vector2(c.x + 0.5f, c.y + 0.5f)) < controlRoom) { nearControl = true; break; }
            }
            if (nearControl) continue;
            Vector2 along = ((Vector2)(route[i + look] - route[i - look])).normalized;
            if (along.sqrMagnitude < 0.5f) continue;
            Vector2 normal = new Vector2(-along.y, along.x);
            float right = Wall(p, normal, maxCells), left = Wall(p, -normal, maxCells);
            if (right < 0f || left < 0f) continue;
            float width = right + left;
            if (width > maxCells || width * cellMeters < MinGapMeters) continue;
            // Centred in the passage
            Vector2 a = p + normal * right, b = p - normal * left;
            if (!BigWall(p + normal * right) || !BigWall(p - normal * left)) continue;
            Vector2 centre = (a + b) / 2f;
            float into = IntoWallMeters / cellMeters, half = DepthMeters / cellMeters / 2f;
            Vector2 a2 = a + normal * into, b2 = b - normal * into;
            List<Vector2> corners = new List<Vector2> { a2 + along * half, b2 + along * half, b2 - along * half, a2 - along * half };
            if (snap != null) for (int c = 0; c < corners.Count; c++) corners[c] = snap(corners[c]);
            found.Add(new Gap { centre = centre, normal = normal, along = along, width = width, corners = corners, a = a, b = b });
        }
        found.Sort((x, y) => x.width.CompareTo(y.width));
        // Places at least GapSpacingMeters apart: the narrowest of each stretch
        List<Gap> spread = new List<Gap>();
        float spacing = GapSpacingMeters / cellMeters;
        foreach (Gap g in found)
        {
            bool closeBy = false;
            foreach (Gap s in spread) if (Vector2.Distance(s.centre, g.centre) < spacing) { closeBy = true; break; }
            if (!closeBy) spread.Add(g);
        }
        return spread;
    }

    // How far (in cells) from p along dir the first closed cell is, or -1 when there is none within max
    float Wall(Vector2 p, Vector2 dir, float max)
    {
        int rows = speed.GetLength(0), cols = speed.GetLength(1);
        for (float t = 0.25f; t <= max; t += 0.25f)
        {
            Vector2 q = p + dir * t;
            int x = Mathf.FloorToInt(q.x), y = Mathf.FloorToInt(q.y);
            if (x < 0 || y < 0 || x >= cols || y >= rows) return t;
            if (speed[y, x] <= 0.01f) return t;
        }
        return -1f;
    }
}
