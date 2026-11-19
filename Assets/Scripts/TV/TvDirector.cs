using System.Collections.Generic;
using UnityEngine;

// The TV production of a relay with TV controls (CourseData.tvControls). A relay with TV controls is a broadcast: the
// map (the "GPS") runs at the race's usual fast pace while nothing happens on TV, and when runners come to a TV control
// the whole race slows down to real running pace and the control is shown live in 3D (TvWorldView) beside the map
// (TvViewer). After the runners have left, the race speeds up again. The start of every leg gets a short shot too.
//
// The host is the producer: it decides what is on TV and sends it to everyone (NetworkPlayer.tvShot), so every player
// slows down and speeds up together. What a shot shows:
//  - a runner coming to a TV control: from when the TV camera can see it, through the punch, until it is out of
//    sight again; then straight back to the map. Nothing is shown while no runner is in the picture.
//  - the start: the first seconds of every runner that sets off
// The 3D worlds are built ahead, while the runners are on their way, and only a few are kept at a time.
// Lives on the RelayRace's object, so it goes when the race does.
public class TvDirector : MonoBehaviour
{
    public static TvDirector Active { get; private set; }

    // The race clock runs 20 game seconds per real second (Clock); on TV the runners run a little faster than in real
    // life (1.4 times), which looks right on a screen
    public const float TvPace = 1.4f / 20f;
    const int StartShot = 99, FinishShot = 98;
    const float SeenWithin = 60f, Out = 5f;         // metres: how far the camera sees a runner; always shown this far out after the punch
    const float StartOut = 45f;                     // metres from the start: the runners are followed well down the first leg
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
        public bool off;                       // not in use on this leg
        public float camE, camN;               // the side of the spot the TV camera should stand on (0, 0 = anywhere)
        public float legE, legN;               // at the start: the way the first leg goes
        public int ordinal = -1;               // its number among the leg's common points, for the split times
        public int controlIndex = -1;          // its place in this team's route on the leg (0 = start), -1 = not on it
        // The producer's notes for the leg
        internal readonly Dictionary<IPackRunner, Pass> passes = new Dictionary<IPackRunner, Pass>();
        internal readonly HashSet<IPackRunner> punched = new HashSet<IPackRunner>();
        internal readonly Dictionary<IPackRunner, float> closest = new Dictionary<IPackRunner, float>();
        internal readonly Dictionary<IPackRunner, float> seen = new Dictionary<IPackRunner, float>();
        internal readonly Dictionary<IPackRunner, float> since = new Dictionary<IPackRunner, float>();
        internal IPackRunner firstRunner;      // the first runner to the spot on this leg
        internal readonly List<IPackRunner> order = new List<IPackRunner>();   // the runners filmed here on this leg, first one first
    }

    public struct Split
    {
        public string name;
        public Color color;
        public int seconds;
        public bool running;   // not at the control yet: seconds is the team's clock now, ticking until the punch
    }

    // A runner who finished the leg while on its way in to the finish camera: it is still shown running the
    // last metres to the finish unit and punching it, and the shot cuts away half a second after
    class Finishing
    {
        public IPackRunner runner;
        public Track track;
        public string label;
        public Color color;
        public float since;
    }
    readonly Dictionary<IPackRunner, Finishing> finishing = new Dictionary<IPackRunner, Finishing>();
    // After the finish punch the runner gets its breath back and walks off; the shot stays a moment on that
    const float FinishHold = 4.5f, FinishLongest = 12f;

    // The host's "TV shot length" (HostSettings): 0.5 = half as long shots as in 1.0.4, the default since 2026-11-19.
    // The runners are taken up nearer the control and left sooner after it.
    static float ShotScale => Mathf.Clamp(HostSettings.TvShotScale, 0.1f, 2f);
    static float Approach => Mathf.Max(14f, SeenWithin * ShotScale);
    static float OutAfter => Mathf.Max(2f, Out * ShotScale);
    static float Longest => Mathf.Max(6f, LongestShot * ShotScale);

    // How many runners the spot shows on this leg, the first ones there (0 = all): the host's settings
    int Limit(Spot spot)
    {
        HostSettings.Values settings = HostSettings.Current;
        if (spot.start) return settings.tvStartShown;
        bool finish = spot == finishSpot || (finishSpot != null && finishSpot.controlIndex > 0 && spot.controlIndex == finishSpot.controlIndex);
        return finish ? settings.tvFinishShown : settings.tvControlShown;
    }

    // May the runner be filmed at the spot? The first ones there are, until the limit is reached
    bool MayFilm(Spot spot, IPackRunner runner)
    {
        int limit = Limit(spot);
        if (limit <= 0 || spot.order.Contains(runner)) return true;
        if (spot.order.Count >= limit) return false;
        spot.order.Add(runner);
        return true;
    }

    class Track
    {
        public double e, n;
        public float dirE, dirN;               // the way the runner moves, length 1 (0 before it has moved)
    }

    MapDefinition map;
    CourseData course;
    readonly List<Spot> spots = new List<Spot>();      // the TV controls, then the start
    Spot startSpot, finishSpot;
    Spot current;                                      // on TV now
    int producedLeg = -1;
    int produced;                                      // the producer's choice (host)
    float shotStart, lastWanted;
    bool building;
    float nextPlan;
    bool slowed;
    readonly Dictionary<IPackRunner, Track> tracks = new Dictionary<IPackRunner, Track>();
    readonly List<IPackRunner> running = new List<IPackRunner>();
    readonly List<IPackRunner> ended = new List<IPackRunner>();
    readonly List<TvWorldView.RunnerAt> runners = new List<TvWorldView.RunnerAt>();
    readonly Dictionary<IPackRunner, Color> colors = new Dictionary<IPackRunner, Color>();

    // ---- What the viewer shows ----

    public bool OnAir => current != null;
    public TvWorldView View => current != null ? current.view : null;
    public bool Failed => current != null && current.failed;
    public bool AtStart => current != null && current.start;
    // A runner who only passes the TV control on the way to another control (2026-11-15: "TV 2" read as if the
    // runner was at TV 2). Then the title says where the runner is really going.
    public string Title
    {
        get
        {
            if (current == null) return "TV";
            IPackRunner p = passing;
            if (p == null || !p.IsValidRunner || current.start) return current.name;
            RelayRace race = RelayRace.Active;
            int last = race != null && race.Route != null ? race.Route.course.AllPoints().Count - 1 : -1;
            string to = p.RelayControl == last ? "the finish" : "control " + p.RelayControl;
            string camera = current.name.Split('·')[0].Trim();
            return p.RunnerLabel + " on the way to " + to + "  <size=80%><color=#C7D6DA>(" + camera + " camera)</color></size>";
        }
    }
    IPackRunner passing;

    // Is the runner on its way to this spot (or just past it)? Only then does it punch there. A runner who passes
    // a TV control or the finish on the way to another control does not punch it (2026-11-15: a runner near the
    // finish at the start of the leg punched the finish).
    static bool HeadingTo(Spot spot, IPackRunner runner)
    {
        // A runner who is making a mistake does not punch, even near the control on the right leg (2026-11-19: a runner
        // lost near a TV control punched it on TV and then went on making the mistake)
        if (!spot.start && runner is NetworkPlayer lost && lost.relayLost) return false;
        if (spot.start || spot.controlIndex < 0) return true;
        int to = runner.RelayControl;
        return to == spot.controlIndex || to == spot.controlIndex + 1;
    }
    public Vector3? Subject { get; private set; }
    public bool RunnerInPicture { get; private set; }
    public float ShotSeconds => current != null ? Time.unscaledTime - shotStart : 0f;
    // What the local player sends to the others: the producer's choice from the host, nothing from the others
    public int ShotToSend => IsProducer ? produced : 0;

    // True while the finish is on TV and a runner who has just finished is still on the way to the finish unit, or
    // the finish shot is still on after the punch (FinishHold). The relay's statistics wait for it (RelayRace), so they do not cover the
    // runner before it has punched.
    public bool FinishStillOnTheWay()
    {
        if (current == null || current != finishSpot || finishSpot.view == null || finishing.Count == 0) return false;
        float now = Time.unscaledTime;
        foreach (Finishing f in finishing.Values)
        {
            float at = finishSpot.view.PunchedAt(f.runner);
            if (at < 0f)
            {
                if (now - f.since < FinishLongest) return true;
            }
            else if (now - at < FinishHold) return true;
        }
        return false;
    }
    // (2026-11-16: the statistics wait for the whole finish shot, the punch and the walk off, not just a second after
    // the punch; they came up over half the screen while the finish camera was still on)

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
        // The finish and the start of every leg are always on TV
        director.finishSpot = new Spot { name = "Finish", number = FinishShot, off = true };
        director.spots.Add(director.finishSpot);
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

    // The whole race at TV pace (real running speed, times the host's TV speed) or at its usual pace (times the
    // host's race speed). Both can be changed by the host during the race (HostSettings), so it is set every frame.
    void SetPace(bool slow)
    {
        slowed = slow;
        float want;
        if (!slow) want = isActiveAndEnabled && NetworkPlayer.Local != null ? RacePace.MapPace(NetworkPlayer.Local.agentMovement) : 1f;
        else want = HostSettings.TvSpeed / PackRunning.GameSecondsPerSecond;
        if (!Mathf.Approximately(Time.timeScale, want)) Time.timeScale = want;
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
            spot.seen.Clear();
            spot.since.Clear();
            spot.firstRunner = null;
            spot.ordinal = -1;
            spot.order.Clear();
        }
        tracks.Clear();
        finishing.Clear();
        if (startSpot.view != null && !Same(startSpot.point, points[0]))
        {
            Destroy(startSpot.view.gameObject);
            startSpot.view = null;
        }
        if (startSpot.view == null) startSpot.failed = false;
        startSpot.point = points[0];
        CameraSide(startSpot, points, 0);
        startSpot.name = "Start · Leg " + (race.Leg + 1);
        startSpot.controlIndex = 0;
        // The finish, unless it is a TV control already
        CoursePoint finish = points[points.Count - 1];
        if (finishSpot.view != null && !Same(finishSpot.point, finish))
        {
            Destroy(finishSpot.view.gameObject);
            finishSpot.view = null;
        }
        if (finishSpot.view == null) finishSpot.failed = false;
        finishSpot.point = finish;
        finishSpot.name = "Finish · Leg " + (race.Leg + 1);
        finishSpot.controlIndex = points.Count - 1;
        finishSpot.off = points.Count < 2;
        // The host's settings: the start only on the first leg and the finish only on the last, if so chosen
        HostSettings.Values settings = HostSettings.Current;
        bool lastLeg = course == null || race.Leg >= course.legs - 1;
        if (!settings.tvFinishEveryLeg && !lastLeg) finishSpot.off = true;
        startSpot.off = !settings.tvStartEveryLeg && race.Leg > 0;
        if (points.Count - 1 < route.commonOrdinal.Count) finishSpot.ordinal = route.commonOrdinal[points.Count - 1];
        CameraSide(finishSpot, points, points.Count - 1);
        for (int s = 0; s < spots.Count - 2; s++)
        {
            Spot spot = spots[s];
            if (Same(spot.point, finish)) finishSpot.off = true;
            spot.name = "TV " + (s + 1);
            spot.controlIndex = -1;
            for (int i = 1; i < points.Count && i < route.commonOrdinal.Count; i++)
            {
                if (!Same(points[i], spot.point)) continue;
                spot.ordinal = route.commonOrdinal[i];
                spot.controlIndex = i;
                CameraSide(spot, points, i);
                spot.name += i == points.Count - 1 ? " · Finish" : " · Control " + i;
                break;
            }
        }
    }

    // Where the TV camera of a point on the course should stand, so the runners come in and go out in front of it
    // without it having to turn round: on the outside of the turn, looking into it. On a straight line through the
    // control it stands to the side; at the finish behind the control, facing the runners coming in; at the start
    // behind the start, watching them go.
    static void CameraSide(Spot spot, List<CoursePoint> points, int i)
    {
        Vector2 Way(int from, int to)
        {
            var d = new Vector2((float)(points[to].e - points[from].e), (float)(points[to].n - points[from].n));
            return d.sqrMagnitude > 1f ? d.normalized : Vector2.zero;
        }
        Vector2 back = i > 0 ? Way(i, i - 1) : Vector2.zero, on = i < points.Count - 1 ? Way(i, i + 1) : Vector2.zero;
        Vector2 side;
        spot.legE = spot.legN = 0f;
        if (back == Vector2.zero && on != Vector2.zero)
        {
            // The start: a little ahead and to the sunny side, so the camera sees the runners set off and run on
            // down the first leg instead of only their backs
            Vector2 across = new Vector2(-on.y, on.x);
            if (across.x * 0.55f - across.y * 0.5f < 0f) across = -across;
            side = on * 0.75f + across * 0.65f;
            spot.legE = on.x;
            spot.legN = on.y;
        }
        else if (back == Vector2.zero || on == Vector2.zero) side = -(back + on);
        else
        {
            side = -(back + on);
            // Straight on: to the side of the line, the sunny one (the sun stands in the south-east)
            if (side.magnitude < 0.5f)
            {
                side = new Vector2(-on.y, on.x);
                if (side.x * 0.55f - side.y * 0.5f < 0f) side = -side;
            }
        }
        side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector2.zero;
        spot.camE = side.x;
        spot.camN = side.y;
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
                if (runner != null && tracks.TryGetValue(runner, out Track last))
                {
                    // Stopped running near the finish: it finished. Keep it for the finish camera.
                    if (!finishSpot.off && runner.IsValidRunner && runner.RelayLeg == race.Leg && Distance(last, finishSpot.point) < 45f
                        && !finishing.ContainsKey(runner) && HeadingTo(finishSpot, runner) && MayFilm(finishSpot, runner))
                    {
                        finishing[runner] = new Finishing { runner = runner, track = last, label = runner.RunnerLabel, color = ColorOf(runner), since = now };
                        finishSpot.punched.Add(runner);
                    }
                    tracks.Remove(runner);
                }
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
            if (on != null && on.view != null) on.view.NewShot();
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
        float passingDistance = float.MaxValue;
        passing = null;
        foreach (IPackRunner runner in running)
        {
            Track t = tracks[runner];
            float d = Distance(t, current.point);
            if (d > ShownWithin) continue;
            nearest = Mathf.Min(nearest, d);
            bool heading = HeadingTo(current, runner);
            if (d < passingDistance)
            {
                passingDistance = d;
                passing = heading ? null : runner;
            }
            runners.Add(new TvWorldView.RunnerAt
            {
                key = runner, label = runner.RunnerLabel, color = ColorOf(runner),
                x = (float)(t.e - current.point.e), z = (float)(t.n - current.point.n),
                mayPunch = heading
            });
        }
        if (current == finishSpot)
        {
            foreach (var pair in finishing)
            {
                Finishing f = pair.Value;
                if (now - f.since > FinishLongest) continue;
                float d = Distance(f.track, current.point);
                nearest = Mathf.Min(nearest, d);
                runners.Add(new TvWorldView.RunnerAt
                {
                    key = pair.Key, label = f.label, color = f.color, finishing = true,
                    x = (float)(f.track.e - current.point.e), z = (float)(f.track.n - current.point.n)
                });
            }
        }
        Vector3? subject = view.ShowRunners(runners);
        // The camera stays on the control until the runner is near enough to be seen coming
        RunnerInPicture = nearest < 40f;
        Subject = subject;
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
            // Never away from a runner who is on the way to the flag or punching
            bool stay = wanted == current || Wants(current) || (current.view != null && current.view.Punching && now - shotStart < Longest);
            if (stay) lastWanted = now;
            bool tooLong = now - shotStart > Longest;
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

    bool FinishShown(Finishing f, float now)
    {
        if (now - f.since > FinishLongest) return true;
        if (current != finishSpot || finishSpot.view == null) return now - f.since > 1.5f;
        float at = finishSpot.view.PunchedAt(f.runner);
        return at > 0f && now - at > FinishHold;
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
        if (spot.off) return false;
        // A runner who is not out running any more (finished, left) is not waited for
        ended.Clear();
        foreach (var pair in spot.passes)
        {
            if (pair.Value != Pass.In || tracks.ContainsKey(pair.Key)) continue;
            // At the finish: on TV until the runner has punched the finish unit, and a moment after
            if (spot == finishSpot && finishing.TryGetValue(pair.Key, out Finishing f) && !FinishShown(f, now))
            {
                any = true;
                continue;
            }
            ended.Add(pair.Key);
        }
        foreach (IPackRunner runner in ended) spot.passes[runner] = Pass.Done;
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
                    if (d < 8f && !spot.off && MayFilm(spot, runner))
                    {
                        pass = Pass.In;
                        spot.since[runner] = now;
                        if (spot.firstRunner == null) spot.firstRunner = runner;
                    }
                    else if (d > 40f) pass = Pass.Done;   // was never seen at the start; not on the way back either
                }
                else if (pass == Pass.In)
                {
                    // On TV while the camera can still see the runner, up to 12 s
                    if (InSight(spot, t, d, true, 0f)) spot.seen[runner] = now;
                    spot.seen.TryGetValue(runner, out float seenAt);
                    if (d > StartOut * ShotScale || now - spot.since[runner] > 12f * ShotScale || (d > 12f && now - seenAt > 0.6f)) pass = Pass.Done;
                }
            }
            else if (pass == Pass.None)
            {
                // Coming to the control: on TV as soon as the camera can see the runner, within the shot length
                // (only the first runners there, as many as the host has chosen)
                if (d < Approach)
                {
                    float toE = (float)(spot.point.e - t.e), toN = (float)(spot.point.n - t.n);
                    float heading = d > 0.01f ? (toE * t.dirE + toN * t.dirN) / d : 1f;
                    if ((d < 10f || heading > 0.3f) && InSight(spot, t, d, false, 0f) && MayFilm(spot, runner))
                    {
                        pass = Pass.In;
                        spot.since[runner] = now;
                        spot.closest[runner] = d;
                        spot.seen[runner] = now;
                        if (spot.firstRunner == null) spot.firstRunner = runner;
                    }
                }
            }
            else if (pass == Pass.In)
            {
                // Punched: at the control, or on the way out again after passing it (the drawn route may cut the corner)
                spot.closest.TryGetValue(runner, out float least);
                if (d < least) spot.closest[runner] = least = d;
                if ((d < 3f || (least < 15f && d > least + 2f)) && HeadingTo(spot, runner)) spot.punched.Add(runner);
                bool punched = spot.punched.Contains(runner);
                if (InSight(spot, t, d, punched, least)) spot.seen[runner] = now;
                spot.seen.TryGetValue(runner, out float seenAt);
                bool lost = now - seenAt > 0.4f;
                // On the way out: on TV for as long as the runner is seen, at least the first metres
                if (punched && d > least + OutAfter && (lost || d > Approach)) pass = Pass.Done;
                else if (!punched && (d > Approach + 10f || now - seenAt > 1.5f))
                {
                    // Out of sight without punching: it may come back
                    pass = Pass.None;
                    if (first) spot.firstRunner = null;
                }
            }
            spot.passes[runner] = pass;
            if (pass == Pass.In) any = true;
        }
        return any;
    }

    // Can the TV camera of the spot see the runner? From the built 3D world: not too far off, no hill in the way and
    // not too many tree trunks. Before the world is built: the last metres to the control and the first ones out.
    bool InSight(Spot spot, Track t, float d, bool punched, float least)
    {
        TvWorld world = spot.view != null ? spot.view.world : null;
        if (world == null) return punched ? d < least + Out : d < 24f;
        float x = (float)(t.e - spot.point.e), z = (float)(t.n - spot.point.n);
        if (Mathf.Abs(x) > world.radius - 3f || Mathf.Abs(z) > world.radius - 3f) return false;
        float cx = world.camX, cz = world.camZ;
        float dx = x - cx, dz = z - cz, far = Mathf.Sqrt(dx * dx + dz * dz);
        if (far > SeenWithin) return false;
        if (far < 12f) return true;
        float cy = world.HeightAt(cx, cz) + 1.7f, ry = world.HeightAt(x, z) + 1.3f;
        int steps = Mathf.CeilToInt(far / 1.5f);
        for (int i = 1; i < steps; i++)
        {
            float f = i / (float)steps;
            if (world.HeightAt(cx + dx * f, cz + dz * f) > Mathf.Lerp(cy, ry, f) - 0.1f) return false;
        }
        // Trunks and stones on the line
        int inTheWay = 0;
        for (int i = 0; i < world.solids.Count; i++)
        {
            TvSolid solid = world.solids[i];
            float f = ((solid.x - cx) * dx + (solid.z - cz) * dz) / (far * far);
            if (f <= 0.05f || f >= 0.97f) continue;
            float px = cx + dx * f - solid.x, pz = cz + dz * f - solid.z, reach = solid.r + 0.1f;
            if (px * px + pz * pz > reach * reach) continue;
            if (solid.r > 0.8f || ++inTheWay >= 3) return false;
        }
        return true;
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
        if (startSpot.view == null && !startSpot.failed && !startSpot.off && producedLeg >= 0 && running.Count == 0) next = startSpot;
        if (next == null)
        {
            float best = 700f;
            foreach (Spot spot in spots)
            {
                if (spot.view != null || spot.failed || spot.start || spot.off) continue;
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
            made = await TvWorldView.Create(map, spot.point, spot.start, spot.camE, spot.camN, spot.legE, spot.legN);
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

    // A TV control with split times is on air (not the start)
    public bool ShowsSplits => current != null && !current.start && current.ordinal >= 0;

    public void Splits(List<Split> into)
    {
        into.Clear();
        if (current == null || current.start || current.ordinal < 0) return;
        foreach (IPackRunner runner in PackRegistry.All)
        {
            if (!(runner is NetworkPlayer player) || !player.IsValidRunner) continue;
            bool punched = player.relaySplitLeg == producedLeg && player.relaySplitCount > current.ordinal
                           && current.ordinal < player.relaySplits.Length && player.relaySplits[current.ordinal] > 0;
            // The time comes up when the runner is seen punching, not a moment before. Until then the runner is listed
            // as on its way with the clock still going, up to its punch time (2026-11-16: in the last second before the
            // punch on TV the box said "Nobody on the way here yet", since the race itself had already passed the control)
            if (punched && current.view != null && current.view.PunchToCome(runner))
            {
                int split = player.relaySplits[current.ordinal];
                into.Add(new Split { name = player.RunnerLabel, color = ColorOf(runner), seconds = Mathf.Min(RelayRace.TeamSeconds(player), split), running = true });
                continue;
            }
            if (punched)
            {
                into.Add(new Split { name = player.RunnerLabel, color = ColorOf(runner), seconds = player.relaySplits[current.ordinal] });
                continue;
            }
            // Still on the way to the control on this leg: the clock ticks on beside the name until the punch, but only
            // for the runner the camera shows now (2026-11-19: the others, far away, were listed with their clocks too)
            if (player.relayLeg != producedLeg || (current.controlIndex >= 0 && player.relayControl > current.controlIndex)) continue;
            if (!current.passes.TryGetValue(runner, out Pass pass) || pass != Pass.In) continue;
            into.Add(new Split { name = player.RunnerLabel, color = ColorOf(runner), seconds = RelayRace.TeamSeconds(player), running = true });
        }
        // The ones who have punched in order of time, then the ones on the way (the one furthest on first)
        into.Sort((a, b) => a.running != b.running ? a.running.CompareTo(b.running) : a.running ? b.seconds.CompareTo(a.seconds) : a.seconds.CompareTo(b.seconds));
    }

    // ---- Mistakes, for the map: the runners making a mistake right now ----

    public void Mistakes(List<(Vector3 position, string name, bool own)> into)
    {
        into.Clear();
        foreach (IPackRunner runner in PackRegistry.All)
        {
            if (runner is NetworkPlayer player && player.IsValidRunner && player.relayRunning && player.relayMistake)
            {
                into.Add((player.RunnerPosition, player.RunnerLabel, player.IsLocalRunner));
            }
        }
    }
}
