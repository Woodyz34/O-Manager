using System.Collections.Generic;
using UnityEngine;

// The TV production of a relay with TV controls (CourseData.tvControls). A relay with TV controls is a broadcast: the
// map (the "GPS") runs at the race's usual fast pace while nothing happens on TV, and when runners come to a TV control
// the whole race slows down to real running pace and the control is shown live in 3D (TvWorldView) beside the map
// (TvViewer). After the runners have left, the race speeds up again. The start of every leg gets a short shot too.
//
// The host is the producer: it decides what is on TV and sends it to everyone (NetworkPlayer.tvShot), so every player
// slows down and speeds up together. What a shot shows:
//  - a runner coming to a TV control: the last metres in, the punch, and a few metres out; then straight back to the
//    map. Nothing is shown before a runner is there.
//  - the start: the first seconds of every runner that sets off
// The 3D worlds are built ahead, while the runners are on their way, and only a few are kept at a time.
// Lives on the RelayRace's object, so it goes when the race does.
public class TvDirector : MonoBehaviour
{
    public static TvDirector Active { get; private set; }

    // The race clock runs 20 game seconds per real second (Clock); on TV the runners run in real time
    public const float TvPace = 1f / 20f;
    const int StartShot = 99;
    const float In = 24f, Out = 7f;                 // metres from the control: on TV from here in, until this far out again
    const float StartOut = 12f;                     // metres from the start
    const float LongestShot = 25f, Linger = 0.1f;   // real seconds
    const float ShownWithin = 100f;                                             // runners this near are in the 3D world
    const int WorldsKept = 3;

    enum Pass { None, In, Done }

    // A place the TV can show: a TV control or the start of the leg
    class Spot
    {
        public CoursePoint point;
        public string name;
        public bool start;
        public int number;                     // what is sent as NetworkPlayer.tvShot
        public TvWorldView view;
        public bool building, failed;
        public float lastUsed = -999f;
        public int ordinal = -1;               // its number among the leg's common points, for the split times
        // The producer's notes for the leg
        internal readonly Dictionary<IPackRunner, Pass> passes = new Dictionary<IPackRunner, Pass>();
        internal readonly HashSet<IPackRunner> punched = new HashSet<IPackRunner>();
        internal readonly Dictionary<IPackRunner, float> closest = new Dictionary<IPackRunner, float>();
        internal readonly Dictionary<IPackRunner, float> since = new Dictionary<IPackRunner, float>();
        internal IPackRunner firstRunner;      // the first runner to the spot on this leg
    }

    public struct Split
    {
        public string name;
        public Color color;
        public int seconds;
    }

    class Track
    {
        public double e, n;
        public float dirE, dirN;               // the way the runner moves, length 1 (0 before it has moved)
    }

    MapDefinition map;
    CourseData course;
    readonly List<Spot> spots = new List<Spot>();      // the TV controls, then the start
    Spot startSpot;
    Spot current;                                      // on TV now
    int producedLeg = -1;
    int produced;                                      // the producer's choice (host)
    float shotStart, lastWanted;
    bool building;
    float nextPlan;
    bool slowed;
    readonly Dictionary<IPackRunner, Track> tracks = new Dictionary<IPackRunner, Track>();
    readonly List<IPackRunner> running = new List<IPackRunner>();
    readonly List<TvWorldView.RunnerAt> runners = new List<TvWorldView.RunnerAt>();
    readonly Dictionary<IPackRunner, Color> colors = new Dictionary<IPackRunner, Color>();

    // ---- What the viewer shows ----

    public bool OnAir => current != null;
    public TvWorldView View => current != null ? current.view : null;
    public bool Failed => current != null && current.failed;
    public bool AtStart => current != null && current.start;
    public string Title => current != null ? current.name : "TV";
    public Vector3? Subject { get; private set; }
    public bool RunnerInPicture { get; private set; }
    public float ShotSeconds => current != null ? Time.unscaledTime - shotStart : 0f;
    // What the local player sends to the others: the producer's choice from the host, nothing from the others
    public int ShotToSend => IsProducer ? produced : 0;

    static bool IsProducer => NetworkPlayer.Local == null || CompetitionFlow.LocalIsHost();

    static readonly Color[] Palette =
    {
        new Color32(0xd4, 0x35, 0x2b, 255), new Color32(0x2a, 0x6f, 0xd6, 255), new Color32(0xe8, 0xc2, 0x1f, 255),
        new Color32(0xee, 0xee, 0xea, 255), new Color32(0x3f, 0x9d, 0x54, 255), new Color32(0x9b, 0x4d, 0xca, 255)
    };

    // Starts the TV production of a relay, or returns null when the course has no TV controls or the map no 3D layer
    public static TvDirector Create(GameObject host, CourseData course, MapDefinition map)
    {
        if (course == null || course.tvControls == null || course.tvControls.Count == 0 || map == null) return null;
        if (course.IsSprint() || !TvWorldData.HasWorld(map))
        {
            Debug.Log("TvDirector: the course has TV controls, but " + (course.IsSprint() ? "it is a sprint" : "the map has no 3D layer") + "; no TV");
            return null;
        }
        TvDirector director = host.AddComponent<TvDirector>();
        director.map = map;
        director.course = course;
        for (int i = 0; i < course.tvControls.Count; i++)
        {
            director.spots.Add(new Spot { point = course.tvControls[i], name = "TV " + (i + 1), number = i + 1 });
        }
        director.startSpot = new Spot { name = "Start", start = true, number = StartShot };
        director.spots.Add(director.startSpot);
        Active = director;
        TvViewer.ForRace(director);
        Debug.Log("TvDirector: a TV production with " + course.tvControls.Count + " TV controls");
        return director;
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
        SetPace(false);
        foreach (Spot spot in spots) if (spot.view != null) Destroy(spot.view.gameObject);
    }

    void OnDisable()
    {
        SetPace(false);
    }

    // The whole race at TV pace (real running speed) or at its usual pace
    void SetPace(bool slow)
    {
        if (slow == slowed) return;
        slowed = slow;
        Time.timeScale = slow ? TvPace : 1f;
    }

    static bool Same(CoursePoint a, CoursePoint b)
    {
        return System.Math.Abs(a.e - b.e) < 1.5 && System.Math.Abs(a.n - b.n) < 1.5;
    }

    // A new leg: where it starts, and which of its controls the TV controls are
    void BeginLeg(RelayRace race)
    {
        LegRoute route = race.Route;
        List<CoursePoint> points = route != null && route.course != null ? route.course.AllPoints() : null;
        if (points == null || points.Count == 0) return;
        producedLeg = race.Leg;
        foreach (Spot spot in spots)
        {
            spot.passes.Clear();
            spot.punched.Clear();
            spot.closest.Clear();
            spot.since.Clear();
            spot.firstRunner = null;
            spot.ordinal = -1;
        }
        tracks.Clear();
        if (startSpot.view != null && !Same(startSpot.point, points[0]))
        {
            Destroy(startSpot.view.gameObject);
            startSpot.view = null;
        }
        if (startSpot.view == null) startSpot.failed = false;
        startSpot.point = points[0];
        startSpot.name = "Start · Leg " + (race.Leg + 1);
        for (int s = 0; s < spots.Count - 1; s++)
        {
            Spot spot = spots[s];
            spot.name = "TV " + (s + 1);
            for (int i = 1; i < points.Count && i < route.commonOrdinal.Count; i++)
            {
                if (!Same(points[i], spot.point)) continue;
                spot.ordinal = route.commonOrdinal[i];
                spot.name += i == points.Count - 1 ? " · Finish" : " · Control " + i;
                break;
            }
        }
    }

    Spot SpotOf(int number)
    {
        foreach (Spot spot in spots) if (spot.number == number) return spot;
        return null;
    }

    Color ColorOf(IPackRunner runner)
    {
        if (colors.TryGetValue(runner, out Color color)) return color;
        color = Palette[colors.Count % Palette.Length];
        if (runner is Component component && component != null)
        {
            BodyHandler body = component.GetComponent<BodyHandler>();
            if (body != null && body.cirkel != null)
            {
                color = body.cirkel.sharedMaterial != null ? body.cirkel.sharedMaterial.color : body.cirkel.color;
                color.a = 1f;
            }
        }
        colors[runner] = color;
        return color;
    }

    static float Distance(Track t, CoursePoint p)
    {
        double de = t.e - p.e, dn = t.n - p.n;
        return (float)System.Math.Sqrt(de * de + dn * dn);
    }

    void Update()
    {
        RelayRace race = RelayRace.Active;
        if (race == null || race.Leg < 0)
        {
            current = null;
            produced = 0;
            SetPace(false);
            return;
        }
        if (race.Leg != producedLeg) BeginLeg(race);
        if (race.Leg != producedLeg)
        {
            current = null;
            produced = 0;
            SetPace(false);
            return;
        }
        float now = Time.unscaledTime;

        // Everyone who is out running on this leg, and the way they are heading
        running.Clear();
        foreach (IPackRunner runner in PackRegistry.All)
        {
            if (runner == null || !runner.IsValidRunner || !runner.RelayRunning || runner.RelayLeg != race.Leg)
            {
                if (runner != null) tracks.Remove(runner);
                continue;
            }
            CoursePoint p = map.FromWorld(runner.RunnerPosition);
            if (!tracks.TryGetValue(runner, out Track track))
            {
                track = new Track { e = p.e, n = p.n };
                tracks[runner] = track;
            }
            else
            {
                double de = p.e - track.e, dn = p.n - track.n;
                double moved = System.Math.Sqrt(de * de + dn * dn);
                if (moved > 0.05)
                {
                    float k = moved > 40.0 ? 1f : 0.35f;   // a jump: forget the old heading
                    track.dirE = Mathf.Lerp(track.dirE, (float)(de / moved), k);
                    track.dirN = Mathf.Lerp(track.dirN, (float)(dn / moved), k);
                    float length = Mathf.Sqrt(track.dirE * track.dirE + track.dirN * track.dirN);
                    if (length > 1e-4f) { track.dirE /= length; track.dirN /= length; }
                    track.e = p.e;
                    track.n = p.n;
                }
            }
            running.Add(runner);
        }

        // What is on TV: the producer decides, the others follow the producer
        int shot;
        if (IsProducer)
        {
            Produce(now);
            shot = produced;
        }
        else
        {
            produced = 0;
            shot = 0;
            foreach (Spot spot in spots) Follow(spot, now);   // who has passed what, for building the worlds ahead
            foreach (IPackRunner runner in PackRegistry.All)
            {
                if (runner is NetworkPlayer player && player.IsValidRunner && player.tvShot != 0)
                {
                    shot = player.tvShot;
                    break;
                }
            }
        }
        Spot on = shot != 0 ? SpotOf(shot) : null;
        if (on != current)
        {
            current = on;
            shotStart = now;
            Subject = null;
        }
        SetPace(shot != 0);

        Plan(now);

        // The runners in the picture
        TvWorldView view = View;
        if (current == null || view == null)
        {
            Subject = null;
            RunnerInPicture = false;
            return;
        }
        current.lastUsed = now;
        runners.Clear();
        float nearest = float.MaxValue;
        foreach (IPackRunner runner in running)
        {
            Track t = tracks[runner];
            float d = Distance(t, current.point);
            if (d > ShownWithin) continue;
            nearest = Mathf.Min(nearest, d);
            runners.Add(new TvWorldView.RunnerAt
            {
                key = runner, label = runner.RunnerLabel, color = ColorOf(runner),
                x = (float)(t.e - current.point.e), z = (float)(t.n - current.point.n)
            });
        }
        Vector3? subject = view.ShowRunners(runners);
        // The camera stays on the control until the runner is near enough to be seen coming
        RunnerInPicture = nearest < 40f;
        Subject = RunnerInPicture ? subject : null;
    }

    // ---- The producer (host) ----

    void Produce(float now)
    {
        Spot wanted = null;
        foreach (Spot spot in spots)
        {
            bool wants = Follow(spot, now);
            if (wants && (wanted == null || spot == current)) wanted = spot;
        }
        if (current != null && IsProducer)
        {
            bool stay = wanted == current || Wants(current);
            if (stay) lastWanted = now;
            bool tooLong = now - shotStart > LongestShot;
            if (tooLong)
            {
                // Someone stands still at the control, or is lost near it: enough of that
                foreach (IPackRunner runner in running)
                {
                    if (current.passes.TryGetValue(runner, out Pass pass) && pass == Pass.In) current.passes[runner] = Pass.Done;
                }
            }
            if (tooLong || now - lastWanted > Linger)
            {
                produced = 0;
                if (wanted == current) wanted = null;
            }
            else
            {
                produced = current.number;
                return;
            }
        }
        if (wanted != null)
        {
            produced = wanted.number;
            lastWanted = now;
        }
        else produced = 0;
    }

    bool Wants(Spot spot)
    {
        foreach (var pair in spot.passes) if (pair.Value == Pass.In) return true;
        return false;
    }

    // Follows every runner past the spot. Returns true while a runner is there to be shown.
    bool Follow(Spot spot, float now)
    {
        bool any = false;
        foreach (IPackRunner runner in running)
        {
            Track t = tracks[runner];
            float d = Distance(t, spot.point);
            spot.passes.TryGetValue(runner, out Pass pass);
            bool first = spot.firstRunner == runner;
            if (spot.start)
            {
                // The runner sets off: its first metres
                if (pass == Pass.None)
                {
                    if (d < 8f)
                    {
                        pass = Pass.In;
                        spot.since[runner] = now;
                        if (spot.firstRunner == null) spot.firstRunner = runner;
                    }
                    else if (d > 40f) pass = Pass.Done;   // was never seen at the start; not on the way back either
                }
                else if (pass == Pass.In && (d > StartOut || now - spot.since[runner] > 6f)) pass = Pass.Done;
            }
            else if (pass == Pass.None)
            {
                // Coming to the control: near it and heading for it.
                if (d < In)
                {
                    float toE = (float)(spot.point.e - t.e), toN = (float)(spot.point.n - t.n);
                    float heading = d > 0.01f ? (toE * t.dirE + toN * t.dirN) / d : 1f;
                    if (d < 10f || heading > 0.6f)
                    {
                        pass = Pass.In;
                        spot.since[runner] = now;
                        spot.closest[runner] = d;
                        if (spot.firstRunner == null) spot.firstRunner = runner;
                    }
                }
            }
            else if (pass == Pass.In)
            {
                // Punched: at the control, or on the way out again after passing it (the drawn route may cut the corner)
                spot.closest.TryGetValue(runner, out float least);
                if (d < least) spot.closest[runner] = least = d;
                if (d < 3f || (least < 15f && d > least + 2f)) spot.punched.Add(runner);
                bool punched = spot.punched.Contains(runner);
                if (punched && d > Mathf.Min(In - 2f, least + Out)) pass = Pass.Done;
                else if (!punched && d > In + 15f)
                {
                    // Ran past without punching: it may come back
                    pass = Pass.None;
                    if (first) spot.firstRunner = null;
                }
            }
            spot.passes[runner] = pass;
            if (pass == Pass.In) any = true;
        }
        return any;
    }

    // ---- The 3D worlds: built ahead, a few kept ----

    void Plan(float now)
    {
        if (building) return;
        // What is on TV comes first
        if (current != null && current.view == null && !current.failed)
        {
            Build(current);
            return;
        }
        if (now < nextPlan) return;
        nextPlan = now + 0.25f;
        // The start of the leg is always needed; then the TV control the runners reach next
        Spot next = null;
        if (startSpot.view == null && !startSpot.failed && producedLeg >= 0 && running.Count == 0) next = startSpot;
        if (next == null)
        {
            float best = 700f;
            foreach (Spot spot in spots)
            {
                if (spot.view != null || spot.failed || spot.start) continue;
                foreach (IPackRunner runner in running)
                {
                    // Not the ones already past it
                    if (spot.passes.TryGetValue(runner, out Pass pass) && pass == Pass.Done) continue;
                    float d = Distance(tracks[runner], spot.point);
                    if (d < best)
                    {
                        best = d;
                        next = spot;
                    }
                }
            }
            // Nobody is out yet: the first TV control of the leg can be made while the teams draw
            if (next == null && running.Count == 0)
            {
                int kept = 0;
                foreach (Spot spot in spots) if (spot.view != null) kept++;
                if (kept < 2)
                {
                    int lowest = int.MaxValue;
                    foreach (Spot spot in spots)
                    {
                        if (spot.view != null || spot.failed || spot.start || spot.ordinal < 0 || spot.ordinal >= lowest) continue;
                        lowest = spot.ordinal;
                        next = spot;
                    }
                }
            }
        }
        if (next != null) Build(next);
    }

    async void Build(Spot spot)
    {
        building = true;
        spot.building = true;
        // Room for the new one: the world shown longest ago goes
        int kept = 0;
        Spot oldest = null;
        foreach (Spot other in spots)
        {
            if (other.view == null) continue;
            kept++;
            if (other != current && (oldest == null || other.lastUsed < oldest.lastUsed)) oldest = other;
        }
        if (kept >= WorldsKept && oldest != null)
        {
            Destroy(oldest.view.gameObject);
            oldest.view = null;
        }
        TvWorldView made = null;
        try
        {
            made = await TvWorldView.Create(map, spot.point, spot.start);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("TvDirector: could not build the 3D world of " + spot.name + ": " + e);
        }
        if (this == null)
        {
            if (made != null) Destroy(made.gameObject);
            return;
        }
        building = false;
        spot.building = false;
        if (made == null)
        {
            spot.failed = true;
            return;
        }
        made.SetDrawing(false);
        spot.view = made;
        spot.lastUsed = Time.unscaledTime;
    }

    // ---- Split times at the TV control on air: the teams that have punched it on this leg, the first one first ----

    public void Splits(List<Split> into)
    {
        into.Clear();
        if (current == null || current.start || current.ordinal < 0) return;
        foreach (IPackRunner runner in PackRegistry.All)
        {
            if (!(runner is NetworkPlayer player) || !player.IsValidRunner) continue;
            if (player.relaySplitLeg != producedLeg || player.relaySplitCount <= current.ordinal || current.ordinal >= player.relaySplits.Length) continue;
            int seconds = player.relaySplits[current.ordinal];
            if (seconds <= 0) continue;
            into.Add(new Split { name = player.RunnerLabel, color = ColorOf(runner), seconds = seconds });
        }
        into.Sort((a, b) => a.seconds.CompareTo(b.seconds));
    }

    // ---- Mistakes, for the map: the runners making a mistake right now ----

    public void Mistakes(List<(Vector3 position, string name)> into)
    {
        into.Clear();
        foreach (IPackRunner runner in PackRegistry.All)
        {
            if (runner is NetworkPlayer player && player.IsValidRunner && player.relayRunning && player.relayMistake)
            {
                into.Add((player.RunnerPosition, player.RunnerLabel));
            }
        }
    }
}
