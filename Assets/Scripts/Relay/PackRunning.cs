using System.Collections.Generic;
using UnityEngine;

// Another runner out on a relay course, as seen by the local runner: the other players' runners, and the test ghosts.
public interface IPackRunner
{
    bool IsValidRunner { get; }
    bool IsLocalRunner { get; }
    Vector3 RunnerPosition { get; }
    int RelayLeg { get; }          // the leg the runner is on, -1 when not in a relay
    int RelayControl { get; }      // the control the runner is heading for (1 = first control)
    bool RelayRunning { get; }     // out on the course and moving, not drawing, waiting or done
    string RunnerLabel { get; }
}

public static class PackRegistry
{
    static readonly List<IPackRunner> runners = new List<IPackRunner>();

    public static IReadOnlyList<IPackRunner> All => runners;

    public static void Add(IPackRunner runner)
    {
        if (runner != null && !runners.Contains(runner)) runners.Add(runner);
    }

    public static void Remove(IPackRunner runner)
    {
        runners.Remove(runner);
    }
}

// Running in a pack on a relay leg, for the local player's runner: the boost from having someone just ahead ("rygg"),
// hanging on to a runner on straight legs, and the tiredness that comes from it. The runner's speed card is all that
// decides it: a fast leader means a big effort for a slow follower.
//
// Own pace: v = base speed (from the speed card) x ground x slope, the speed the runner would run here alone.
// Drafting: b = 4 % with a runner up to 20 m ahead, fading to 0 at 50 m. Pugh (1971) and Hoogkamer et al. (2018,
//   Sports Med 48:1859) put the air-resistance saving of running right behind someone at a few percent of the energy at
//   race speeds; in a forest most of the gain is that the runner behind does not have to read the map as carefully.
// Effort: A = (pace held / own pace) / (1 + b). Running alone at your own pace is A = 1.
// Critical speed model (Monod & Scherrer 1965; Jones et al. 2010, Med Sci Sports Exerc 42:1876): a runner can hold their
//   critical speed CS for a long time, and here CS is the runner's own pace (A = 1), since a 30-90 minute race is run
//   close to it. Above it they use up a reserve D' of metres run above CS:
//     A > 1:   D' -= (A - 1) x v x dt                      (metres)
//   Below it the reserve fills up again, as in Skiba's W' balance (Skiba et al. 2012, Med Sci Sports Exerc 44:1526):
//     A <= 1:  D' = D'max - (D'max - D') x e^(-t / tau),  tau = 546 x e^(-0.01 x DCP) + 316 game seconds,
//              DCP = 350 W x (1 - A), how far below critical power the runner is (350 W for a trained runner)
//   D'max = 50 m. Real runners have about 150-250 m, but then a runner on speed 60 could hang on to one on speed 90
//   for about 700 m; scaled to 50 m it is about 170 m, and a runner on 85 can hang on to one on 90 for about 2.9 km.
// Letting go: at D' = 0 the runner can't keep up, lets go, finds the control the leader was heading for on their own and
//   goes on with their own drawn line. They can hang on again when the reserve is back at 40 %.
// Tired: until the reserve has filled up, the runner's own pace is lower: v x (1 - 0.10 x (1 - D' / D'max)),
//   up to 10 % slower right after letting go.
// Hanging pace: the leader's speed, plus a little to close the gap to 6 m (or minus to open it), at most 35 % above own pace.
public class PackRunning : MonoBehaviour
{
    public const float MetersPerUnit = 1f / MapDefinition.WorldUnitsPerMeter;
    public const float GameSecondsPerSecond = 20f;   // the competition clock runs 20 game seconds per real second (Clock)

    public const float DraftBoost = 0.04f;
    public const float FullDraftMeters = 20f;
    public const float DraftRangeMeters = 50f;
    public const float HangGapMeters = 6f;
    public const float LoseBackMeters = 50f;
    public const float MaxHangRatio = 1.35f;         // nobody can sprint more than 35 % above their own pace
    public const float TiredSlowdown = 0.10f;
    public const float RehangReserve = 0.4f;         // after letting go, the reserve must be back to 40 % to hang again
    public const float ReserveMeters = 50f;          // D'max
    public const float CriticalPower = 350f;         // watts, for Skiba's recovery time

    AgentMovement agent;
    NetworkPlayer self;

    // The reserve D' of the runner on this leg, in metres
    public float DPrime => ReserveMeters;
    public float Reserve { get; private set; } = ReserveMeters;
    public float ReserveShare => DPrime > 0f ? Reserve / DPrime : 1f;

    public bool HangAllowed { get; set; }            // straight relay leg and the player has hanging switched on
    public bool IsHanging => leader != null;
    public string LeaderName => leader != null ? leader.RunnerLabel : "";
    public float CurrentDraft { get; private set; }  // the boost from the runner ahead right now (0 - 0.04)
    public string Status { get; private set; } = "";

    IPackRunner leader;
    IPackRunner draftLeader;
    float leaderStoppedFor;
    Vector2Int lastHangCell;
    float lastNatural;
    float lastMultiplier = 1f;     // the pace asked for last frame, compared with the runner's own pace here
    float statusUntil;
    bool active;

    class Trail
    {
        public readonly List<Vector3> points = new List<Vector3>();
        public Vector3 lastPosition;
        public float speed;          // units per second, smoothed
        public bool seen;
    }

    readonly Dictionary<IPackRunner, Trail> trails = new Dictionary<IPackRunner, Trail>();

    void Awake()
    {
        agent = GetComponent<AgentMovement>();
        self = GetComponent<NetworkPlayer>();
    }

    // ---- Leg ----

    public void BeginLeg(bool hangAllowed)
    {
        Reserve = DPrime;
        HangAllowed = hangAllowed;
        leader = null;
        draftLeader = null;
        CurrentDraft = 0f;
        trails.Clear();
        Status = "";
        active = true;
    }

    public void EndLeg()
    {
        leader = null;
        draftLeader = null;
        CurrentDraft = 0f;
        active = false;
    }

    // ---- Speed, asked by AgentMovement every frame it moves ----

    // naturalSpeed is the runner's own speed here (base speed, ground and slope), in units per second
    public float SpeedMultiplier(float naturalSpeed)
    {
        lastNatural = naturalSpeed;
        lastMultiplier = ComputeMultiplier(naturalSpeed);
        return lastMultiplier;
    }

    float ComputeMultiplier(float naturalSpeed)
    {
        if (!active || naturalSpeed <= 0.0001f)
        {
            return 1f;
        }
        if (leader != null && trails.TryGetValue(leader, out Trail trail))
        {
            float gapMeters = Vector2.Distance(transform.position, leader.RunnerPosition) * MetersPerUnit;
            // Keep a few metres behind: close the gap a little faster than the leader runs, or ease off
            float wanted = trail.speed + (gapMeters - HangGapMeters) / MetersPerUnit * 0.8f;
            wanted = Mathf.Clamp(wanted, naturalSpeed * 0.3f, naturalSpeed * MaxHangRatio);
            return wanted / naturalSpeed;
        }
        return (1f + CurrentDraft) * TiredFactor();
    }

    public float TiredFactor() => 1f - TiredSlowdown * (1f - ReserveShare);

    // The leader has punched the control we are heading for
    public bool LeaderPassed(int control)
    {
        return leader != null && leader.RelayControl > control;
    }

    // ---- Every frame ----

    void Update()
    {
        if (!active || agent == null)
        {
            return;
        }
        UpdateTrails();
        bool running = IsRunning();

        // Who is right ahead of us, for the drafting boost
        draftLeader = running ? FindRunnerAhead(DraftRangeMeters, out float aheadMeters) : null;
        if (draftLeader != null)
        {
            float d = Vector2.Distance(transform.position, draftLeader.RunnerPosition) * MetersPerUnit;
            CurrentDraft = DraftBoost * Mathf.Clamp01((DraftRangeMeters - d) / (DraftRangeMeters - FullDraftMeters));
        }
        else
        {
            CurrentDraft = 0f;
        }
        if (leader != null)
        {
            // Hanging on: the leader is close, so the full boost
            CurrentDraft = DraftBoost * Mathf.Clamp01((DraftRangeMeters - Vector2.Distance(transform.position, leader.RunnerPosition) * MetersPerUnit) / (DraftRangeMeters - FullDraftMeters));
        }

        if (running)
        {
            UpdateReserve(Time.deltaTime);
            if (leader != null)
            {
                KeepHanging();
            }
            else
            {
                TryStartHanging();
            }
        }

        if (leader == null && Time.time > statusUntil)
        {
            Status = CurrentDraft > 0.001f && draftLeader != null
                ? "Running behind " + draftLeader.RunnerLabel + "  (+" + Mathf.RoundToInt(CurrentDraft * 100f) + " %)"
                : ReserveShare < 0.95f ? "Tired: " + Mathf.RoundToInt((1f - TiredFactor()) * 100f) + " % slower until rested" : "";
        }
    }

    bool IsRunning()
    {
        return agent.canRunNextSegment && !agent.hasFinished && !agent.WaitingForAllPlayersToReachKontroll
            && !agent.WaitingForAllPlayersToDraw && !agent.relayHold && !agent.isWaiting
            && agent.manualPathDrawer != null && !agent.manualPathDrawer.timeToDraw;
    }

    void UpdateTrails()
    {
        foreach (IPackRunner runner in PackRegistry.All)
        {
            if (!Usable(runner)) continue;
            if (!trails.TryGetValue(runner, out Trail trail))
            {
                trail = new Trail();
                trails[runner] = trail;
            }
            Vector3 position = runner.RunnerPosition;
            if (!trail.seen)
            {
                trail.seen = true;
                trail.lastPosition = position;
                trail.points.Add(position);
                continue;
            }
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float moved = Vector2.Distance(trail.lastPosition, position);
            if (moved > 0.5f)
            {
                // A jump, not running (new leg, start of the course)
                trail.points.Clear();
                trail.speed = 0f;
            }
            else
            {
                trail.speed = trail.speed <= 0f ? moved / dt : Mathf.Lerp(trail.speed, moved / dt, 1f - Mathf.Exp(-dt / 0.25f));
            }
            trail.lastPosition = position;
            if (trail.points.Count == 0 || Vector2.Distance(trail.points[trail.points.Count - 1], position) > 0.03f)
            {
                trail.points.Add(position);
                if (trail.points.Count > 60) trail.points.RemoveAt(0); // about the last 180 m
            }
        }
    }

    bool Usable(IPackRunner runner)
    {
        return runner != null && runner.IsValidRunner && !runner.IsLocalRunner && runner.RelayLeg == RelayRace.CurrentLegIndex;
    }

    // The nearest runner within range in front of us (within 60 degrees of the way we are running)
    IPackRunner FindRunnerAhead(float rangeMeters, out float distanceMeters)
    {
        IPackRunner best = null;
        distanceMeters = float.MaxValue;
        Vector2 forward = transform.up;
        foreach (IPackRunner runner in PackRegistry.All)
        {
            if (!Usable(runner) || !runner.RelayRunning) continue;
            Vector2 offset = runner.RunnerPosition - transform.position;
            float meters = offset.magnitude * MetersPerUnit;
            if (meters > rangeMeters || meters < 0.5f) continue;
            if (Vector2.Dot(offset.normalized, forward) < 0.5f) continue;
            if (meters < distanceMeters)
            {
                distanceMeters = meters;
                best = runner;
            }
        }
        return best;
    }

    // ---- Tiredness ----

    void UpdateReserve(float dt)
    {
        if (lastNatural <= 0.0001f) return;
        // The pace the runner is holding, compared with their own pace on this ground; running behind someone makes it cheaper
        float effort = lastMultiplier / (1f + CurrentDraft);
        if (effort > 1f)
        {
            // Metres run above critical speed (the runner's own pace) come out of the reserve
            Reserve = Mathf.Max(0f, Reserve - (effort - 1f) * lastNatural * MetersPerUnit * dt);
        }
        else
        {
            // Skiba: the further below critical power, the faster the reserve comes back
            float wattsBelow = CriticalPower * (1f - effort);
            float tau = 546f * Mathf.Exp(-0.01f * wattsBelow) + 316f;
            float gameSeconds = dt * GameSecondsPerSecond;
            Reserve = DPrime - (DPrime - Reserve) * Mathf.Exp(-gameSeconds / tau);
        }
    }

    // ---- Hanging on ----

    void TryStartHanging()
    {
        if (!HangAllowed || ReserveShare < RehangReserve || agent.IsMissing || draftLeader == null)
        {
            return;
        }
        IPackRunner candidate = draftLeader;
        if (candidate.RelayControl != agent.currentKontroll) return;      // going somewhere else
        if (!trails.TryGetValue(candidate, out Trail trail) || trail.points.Count == 0) return;
        if (lastNatural <= 0.0001f || trail.speed < lastNatural * 0.97f) return; // slower than us: run past instead

        // Join the leader's line at the point of it nearest to us
        int nearest = 0;
        float bestDistance = float.MaxValue;
        for (int k = 0; k < trail.points.Count; k++)
        {
            float distance = Vector2.Distance(trail.points[k], transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = k;
            }
        }
        List<Vector2Int> cells = new List<Vector2Int>();
        Vector2Int here = agent.WorldToGridPositionCentered(transform.position);
        cells.Add(here);
        for (int k = nearest; k < trail.points.Count; k++)
        {
            if (!AppendLine(cells, agent.WorldToGridPositionCentered(trail.points[k])))
            {
                return; // something we can't cross between us and the leader's line
            }
        }
        if (!AppendLine(cells, agent.WorldToGridPositionCentered(candidate.RunnerPosition))) return;

        leader = candidate;
        leaderStoppedFor = 0f;
        lastHangCell = cells[cells.Count - 1];
        agent.path = cells;
        agent.currentTargetIndex = cells.Count > 1 ? 1 : 0;
        Status = "Hanging on to " + leader.RunnerLabel;
        Debug.Log("PackRunning: hanging on to " + leader.RunnerLabel);
    }

    void KeepHanging()
    {
        if (!leader.IsValidRunner || leader.RelayLeg != RelayRace.CurrentLegIndex)
        {
            LetGo(leader != null ? leader.RunnerLabel + " is gone" : "", false);
            return;
        }
        float gapMeters = Vector2.Distance(transform.position, leader.RunnerPosition) * MetersPerUnit;
        if (gapMeters > LoseBackMeters)
        {
            LetGo("Lost " + leader.RunnerLabel + "'s back", false);
            return;
        }
        if (Reserve <= 0.01f)
        {
            LetGo("Too tired to keep up with " + leader.RunnerLabel, true);
            return;
        }
        leaderStoppedFor = leader.RelayRunning ? 0f : leaderStoppedFor + Time.deltaTime;
        if (leaderStoppedFor > 1.5f)
        {
            LetGo(leader.RunnerLabel + " stopped", false);
            return;
        }

        // Follow the leader's line as it grows
        Vector2Int cell = agent.WorldToGridPositionCentered(leader.RunnerPosition);
        if (cell != lastHangCell && agent.path != null)
        {
            if (AppendLine(agent.path, cell))
            {
                lastHangCell = cell;
            }
        }
        Status = "Hanging on to " + leader.RunnerLabel + "  (reserve " + Mathf.RoundToInt(ReserveShare * 100f) + " %)";
    }

    // Stops hanging on. The runner then finds the control the leader was heading for on their own,
    // and runs their own drawn line from there.
    public void LetGo(string reason, bool tired)
    {
        if (leader == null) return;
        Debug.Log("PackRunning: let go: " + reason);
        leader = null;
        Status = reason;
        statusUntil = Time.time + 4f;
        if (agent != null && !agent.hasFinished)
        {
            agent.FindControlAlone();
        }
    }

    // Stops hanging without looking for the control (the leg ended, or we punched the finish)
    public void StopHanging()
    {
        leader = null;
    }

    // Adds the grid cells on a straight line from the last cell to the target. False if the line crosses a cell nobody can run in.
    bool AppendLine(List<Vector2Int> cells, Vector2Int target)
    {
        float[,] grid = agent.pathfindingManager.speedGrid2D;
        Vector2Int from = cells[cells.Count - 1];
        int steps = Mathf.Max(Mathf.Abs(target.x - from.x), Mathf.Abs(target.y - from.y));
        for (int step = 1; step <= steps; step++)
        {
            float t = (float)step / steps;
            Vector2Int cell = new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(from.x, target.x, t)), Mathf.RoundToInt(Mathf.Lerp(from.y, target.y, t)));
            if (grid[cell.y, cell.x] <= 0.01f) return false;
            if (cell != cells[cells.Count - 1]) cells.Add(cell);
        }
        return true;
    }
}
