using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The run-yourself game mode (2026-11-19): the player is the runner, in first person, in the 3D world made from the
// map (the same world as the TV controls, RunWorld), with the map in the hand (RunMap). Nobody draws a route and no
// runner card is used: everyone runs with the same body. The controls must be punched in the right order and the race
// ends at the finish; a control missed or taken in the wrong order is only told at the finish (disqualified).
// This first version is single player (a record per course). Mass start and individual start online come next.
//
// The competition scene is still loaded underneath: its 2D map and course are what the map in the hand films, and
// the GPS replay afterwards is the usual relay replay on that map.
public partial class RunSession : MonoBehaviour
{
    public const string RunnerName = "You";
    const float PunchReach = 0.6f;      // metres from the punching unit (the flag) that punch it
    const float SampleEvery = 0.25f;    // seconds between GPS points

    public static bool On => (ExtraModes.Is(ExtraMode.RunYourself) || ExtraModes.Is(ExtraMode.RunMass)) && RelayRace.Active == null;
    public static bool Mass => ExtraModes.Is(ExtraMode.RunMass);
    public static RunSession Active { get; private set; }

    // Instead of the choose-a-runner screen: everyone is ready at once
    public static void Begin(NetworkPlayer local)
    {
        AgentMovement agent = local.agentMovement;
        agent.SetRunner(new CardData(RunnerName, 100f, 100f, null, null, "", true, null));
        local.relayRunnerName = RunnerName;
        if (local.manager != null && local.manager.ChooseRunnerPanel != null) local.manager.ChooseRunnerPanel.SetActive(false);
        local.SetHasChoosenRunner();
        GameObject board = GameObject.Find("LeaderboardPanel");
        if (board != null) board.SetActive(false);
    }

    // Everyone is ready: nobody draws, the 2D runner stands still and its clock is stopped; the 3D race takes over
    public static void StartRace(AgentMovement agent)
    {
        agent.manualPathDrawer.timeToDraw = false;
        agent.canRunNextSegment = false;
        agent.WaitingForAllPlayersToDraw = true;
        if (Active == null) new GameObject("Run it yourself").AddComponent<RunSession>();
    }

    // The best time on a course on this computer (real-life seconds), or 0
    public static float Best(string courseId) => PlayerPrefs.GetFloat(LocalPrefs.Key("RunYourself.Best." + courseId), 0f);

    static void SetBest(string courseId, float seconds) => PlayerPrefs.SetFloat(LocalPrefs.Key("RunYourself.Best." + courseId), seconds);

    // ---- The race ----

    enum Phase { Loading, Waiting, Countdown, Running, Finished, Failed }

    Phase phase = Phase.Loading;
    CourseData course;
    MapDefinition mapDef;
    List<CoursePoint> points;           // start, controls, finish
    RunTerrain terrain;
    RunWorld world;
    RunSounds sounds;
    RunPlayer player;
    RunMap handMap;

    readonly List<int> punches = new List<int>();          // flag index (0 = control 1 .. last = finish), in order
    readonly List<float> punchTimes = new List<float>();
    int lastPunched = -1;
    float countdown;
    int lastBeep;
    float gameSeconds;                  // seconds played since the start (not real-life race time)
    float nextSample;
    RelayTrack track;
    bool ok;
    float resultSeconds;
    float[] finalSplits;
    bool atResults;

    // What is hidden of the 2D competition while running, to be shown again
    Camera mainCam;
    int mainMask;
    CameraController cameraController;
    readonly List<Canvas> hiddenCanvases = new List<Canvas>();
    readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    readonly HashSet<Canvas> own = new HashSet<Canvas>();
    float nextHide;
    bool twoDHidden;

    Canvas hud;
    TextMeshProUGUI clockText, bigText, hintText, factsText;
    GameObject backdrop;
    UIProgressCard loadingCard;
    GameObject results;

    void Awake()
    {
        Active = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive) return;
        if (this != null) Destroy(gameObject);
    }

    void Start()
    {
        if (IndividualRace.Active != null) IndividualRace.Active.ManualStats = true;
        RouteAnalysis.RealTimer = TimeRoute;
        Load();
    }

    // The computer's route in the statistics, timed as this 3D runner would run it (2026-11-19, Anders: the computer
    // was far quicker than a good run, because it was timed with the 2D runner's speeds): the same ground speeds,
    // slopes and top speed as the runner, every 2 m along the line, at full speed (the computer doesn't read the map)
    float TimeRoute(List<Vector3> line)
    {
        if (terrain == null || mapDef == null || line == null || line.Count < 2) return 0f;
        double seconds = 0;
        CoursePoint a = mapDef.FromWorld(line[0]);
        for (int i = 1; i < line.Count; i++)
        {
            CoursePoint b = mapDef.FromWorld(line[i]);
            double de = b.e - a.e, dn = b.n - a.n;
            double length = Math.Sqrt(de * de + dn * dn);
            int steps = Mathf.Max(1, (int)Math.Ceiling(length / 2.0));
            double step = length / steps;
            double ux = de / Math.Max(1e-6, length), uz = dn / Math.Max(1e-6, length);
            for (int s = 0; s < steps; s++)
            {
                double f = (s + 0.5) / steps;
                double e = a.e + de * f, n = a.n + dn * f;
                RunTerrain.Ground g = terrain.At(e, n);
                float rise = terrain.MapHeight(e + ux * 3.0, n + uz * 3.0) - terrain.MapHeight(e - ux * 3.0, n - uz * 3.0);
                float gradient = Mathf.Clamp(rise / 6f / RunSettings.Hills, -1.2f, 1.2f);
                float speed = RunPlayer.ForestSpeed * (g.blocked ? 0.05f : Mathf.Max(0.05f, g.speed)) * RunPlayer.SlopeFactor(gradient, g.rough);
                seconds += step / Mathf.Max(0.05f, speed);
            }
            a = b;
        }
        return (float)seconds;
    }

    async void Load()
    {
        course = CourseLoader.Loaded;
        mapDef = CourseLoader.LoadedMap;
        if (course == null || mapDef == null)
        {
            Fail("The course could not be found.");
            return;
        }
        points = course.AllPoints();

        HideTwoD();
        BuildHud();
        loadingCard = UIProgressCard.Show((mapDef.IsSprint ? "Building the town of " : "Building the forest of ") + mapDef.name + "...");
        loadingCard.Set("Reading the map", 0.02f);

        TvWorldData.Data data = null;
        try { data = await TvWorldData.LoadAsync(mapDef, true); }
        catch (Exception e) { Debug.LogWarning("RunSession: " + e); }
        if (this == null) return;
        if (data == null || data.layer == null)
        {
            Fail("This map has no 3D world. Import it again (OCAD or OOM) to run it yourself.");
            return;
        }
        data = WithHills(data, RunSettings.Hills);
        // The knolls and hollows the mapper drew with contours and form lines, which the 10 m height grid rounds away
        loadingCard.Set("Shaping the ground from the contours (the first time on a map takes a little while)", 0.06f);
        byte[] linesJson = TvWorldData.LinesJson(mapDef), pointsJson = TvWorldData.PointsJson(mapDef);
        float interval = TvWorldData.ContourInterval(mapDef);
        string mapId = mapDef.id;
        // The ground from the contours is kept on disk; a new version of the map's lines (an import) gets a new file
        string cacheFile = System.IO.Path.Combine(Application.persistentDataPath, "RunHeights",
            Safe(mapId) + "-" + (linesJson != null ? linesJson.Length : 0) + "-" + (pointsJson != null ? pointsJson.Length : 0) + "-v1.bytes");
        TvWorldData.Data plain = data;
        TvHeights detailed = plain.heights;
        try { detailed = await Task.Run(() => RunContours.Detailed(mapId, plain.heights, linesJson, pointsJson, interval, m => Debug.Log("RunContours: " + m), cacheFile)); }
        catch (Exception e) { Debug.LogWarning("RunSession: contours: " + e); }
        if (this == null) return;
        data = new TvWorldData.Data { mapId = plain.mapId, layer = plain.layer, heights = detailed, look = plain.look, speeds = plain.speeds, town = plain.town };
        loadingCard.Set("Reading the terrain", 0.08f);
        try { terrain = await Task.Run(() => new RunTerrain(data.layer, data.heights, data.speeds, data.town)); }
        catch (Exception e)
        {
            Debug.LogWarning("RunSession: " + e);
            if (this != null) Fail("The terrain could not be read.");
            return;
        }
        if (this == null) return;

        List<CoursePoint> flags = new List<CoursePoint>(points);
        flags.RemoveAt(0);
        world = RunWorld.Create(data, terrain, points[0], flags);
        world.transform.SetParent(transform, true);
        world.Loading = true;

        double se = points[0].e, sn = points[0].n;
        StartSpot(ref se, ref sn);
        FreeSpot(ref se, ref sn);
        world.SetPlayer(se, sn);

        // Build the forest round the start before anything is shown
        float waited = 0f;
        while (this != null && waited < 30f)
        {
            float share = world.Ready(se, sn, Mathf.Min(world.ViewDistance, 120f));
            loadingCard.Set("Building the forest", 0.1f + 0.9f * share);
            if (share >= 1f && waited > 0.3f) break;
            await Task.Yield();
            waited += Time.unscaledDeltaTime;
        }
        if (this == null) return;
        world.Loading = false;
        loadingCard.Close();
        loadingCard = null;
        if (backdrop != null) Destroy(backdrop);

        float yaw = Bearing(points[0], points[1]);
        sounds = RunSounds.Create(transform, RunSettings.Woman, UnityEngine.Random.Range(0, int.MaxValue));
        player = RunPlayer.Create(world, terrain, sounds, se, sn, yaw, UnityEngine.Random.Range(0, int.MaxValue));
        player.transform.SetParent(transform, true);
        player.Moved = CheckPunch;
        // Online everyone runs at the same speed, so the mass start is fair and the replays line up
        if (Online) player.gameSpeed = 4f;
        handMap = RunMap.Create(player, mapDef, points, mainMask);
        handMap.transform.SetParent(transform, true);
        EnsureListener();

        track = new RelayTrack { label = RunnerName, isLocal = true, color = TrackColour() };
        phase = Online ? Phase.Waiting : Phase.Countdown;
        waitingSince = Time.unscaledTime;
        countdown = 5.2f;
        lastBeep = 99;
        bigText.text = "";
        hintText.text = Hint();
    }

    // The player's colour for the GPS line, never white or near white (2026-11-19, Anders: white cannot be seen on the map)
    static Color TrackColour()
    {
        Color c = PlayerColor.HasSaved ? PlayerColor.Saved : CourseUI.Accent;
        float light = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        Color.RGBToHSV(c, out _, out float saturation, out _);
        return light > 0.8f || (light > 0.6f && saturation < 0.25f) ? CourseUI.Accent : c;
    }

    static string Safe(string name)
    {
        var b = new System.Text.StringBuilder();
        foreach (char ch in name ?? "map") b.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
        return b.ToString();
    }

    static string Hint() => "W A S D: run    Mouse: look    Space: jump    Left mouse: map up/down    F4: speed and ground\n"
                            + "With the map up: scroll to zoom, hold right mouse to move it, Q / E to turn it, Shift / Ctrl for the next / previous leg";

    // The world with the hills made higher (a copy: the TV controls keep the map's own heights). Heights are stretched
    // round the mean, so the lowest and highest ground move apart and every slope gets steeper by the same share.
    static TvWorldData.Data WithHills(TvWorldData.Data data, float scale)
    {
        TvHeights source = data.heights;
        if (source == null || source.h == null || source.h.Length == 0 || Mathf.Approximately(scale, 1f)) return data;
        double sum = 0;
        foreach (float v in source.h) sum += v;
        float mean = (float)(sum / source.h.Length);
        TvHeights higher = new TvHeights { cell = source.cell, minx = source.minx, miny = source.miny, rows = source.rows, cols = source.cols, h = new float[source.h.Length] };
        for (int i = 0; i < source.h.Length; i++) higher.h[i] = mean + (source.h[i] - mean) * scale;
        return new TvWorldData.Data { mapId = data.mapId, layer = data.layer, heights = higher, look = data.look, speeds = data.speeds, town = data.town };
    }

    // A spot near the start where the runner can stand
    void FreeSpot(ref double e, ref double n)
    {
        if (!terrain.Blocked(e, n, 0.4f)) return;
        for (int ring = 1; ring < 40; ring++)
        {
            for (int k = 0; k < 16; k++)
            {
                double a = k * Math.PI / 8.0, r = ring * 0.75;
                double te = e + Math.Sin(a) * r, tn = n + Math.Cos(a) * r;
                if (terrain.Blocked(te, tn, 0.4f)) continue;
                e = te;
                n = tn;
                return;
            }
        }
    }

    static float Bearing(CoursePoint a, CoursePoint b) => Mathf.Repeat(Mathf.Atan2((float)(b.e - a.e), (float)(b.n - a.n)) * Mathf.Rad2Deg, 360f);

    void Update()
    {
        if (Time.unscaledTime >= nextHide && twoDHidden)
        {
            nextHide = Time.unscaledTime + 0.5f;
            HideCanvases();
        }
        if (player == null || atResults) return;
        float dt = Time.deltaTime;
        bool playing = phase == Phase.Waiting || phase == Phase.Countdown || phase == Phase.Running;
        NetTick(dt);

        // The mouse turns the head while running; any menu or window (Esc menu, settings, a pop-up) frees it and the
        // runner stops taking keys until it is closed
        if (Time.unscaledTime >= nextMenuCheck)
        {
            nextMenuCheck = Time.unscaledTime + 0.15f;
            menuOpen = MenuOpen();
        }
        player.inputOff = menuOpen;
        bool locked = playing && !menuOpen;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;

        if (phase == Phase.Waiting)
        {
            // Online: the countdown starts when everyone has built the forest
            int ready = ReadyCount(out int all);
            bigText.text = "<size=45%>Waiting for the others to load the forest (" + ready + " of " + all + " ready)</size>";
            if (ready >= all || Time.unscaledTime - waitingSince > 90f)
            {
                phase = Phase.Countdown;
                bigText.text = "";
            }
        }
        else if (phase == Phase.Countdown)
        {
            countdown -= dt;
            int second = Mathf.CeilToInt(countdown);
            if (second != lastBeep && second <= 4)
            {
                lastBeep = second;
                if (second >= 1) sounds.StartBeep(false);
                else sounds.StartBeep(true);
            }
            bigText.text = second >= 1 && second <= 4 ? second.ToString() : countdown > 4f ? "Get ready" : "";
            if (countdown <= 0f)
            {
                phase = Phase.Running;
                player.controlsOn = true;
                bigText.text = "GO!";
                handMap.SetUp(true);
                handMap.FrameLeg(0);
                Invoke(nameof(ClearBig), 1.2f);
            }
        }
        else if (phase == Phase.Running)
        {
            gameSeconds += dt;
            // Standing still reading the map: the clock goes as in real life (1 s is 1 s), so stopping to read costs
            // what it would in a real race. Standing still without the map, and running, go at the game speed
            // (2026-11-20, Anders). Alone only: online everyone shares one world and one clock.
            bool readingStill = !Online && player.mapUp && player.speed < 0.05f && !player.Fallen;
            player.raceSeconds += dt * (readingStill ? 1f : player.gameSpeed);
            if (gameSeconds >= nextSample)
            {
                nextSample = gameSeconds + SampleEvery;
                Sample();
            }
        }
        else if (phase == Phase.Finished)
        {
            // In, but the others still run: their GPS tracks go on (the race clock keeps going for them)
            gameSeconds += dt;
            if (gameSeconds >= nextSample)
            {
                nextSample = gameSeconds + SampleEvery;
                SampleOthers(gameSeconds * player.gameSpeed);
            }
            WatchTick(dt);
        }

        player.Tick(dt);
        handMap.Tick(dt, phase == Phase.Running && !menuOpen);
        clockText.text = phase == Phase.Countdown || phase == Phase.Waiting ? "0:00"
            : Watching ? (watching.raceFinished ? RaceTime(watching.relayTotals[0]) : RaceTime(gameSeconds * player.gameSpeed))
            : RaceTime(player.raceSeconds);
        if (Input.GetKeyDown(KeyCode.F4)) showFacts = !showFacts;
        factsText.text = showFacts ? Facts() : "";
    }

    bool showFacts;
    bool menuOpen;
    float nextMenuCheck;

    // True while a window that wants the mouse is open: the Esc menu, the settings, or any other pop-up with buttons
    // above the race (they all sit at sorting order 200 and up)
    bool MenuOpen()
    {
        if (EscMenu.IsOpen || HostSettingsPopup.IsOpen || RunSettings.IsOpen) return true;
        foreach (GraphicRaycaster r in FindObjectsByType<GraphicRaycaster>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            Canvas c = r.GetComponent<Canvas>();
            if (c == null || !c.enabled || own.Contains(c) || c.renderMode == RenderMode.WorldSpace) continue;
            if (c.name == "Toast") continue;   // a short message in the corner, not a window
            // The canvas of a closed window (the Esc menu keeps its canvas and hides only its card) does not count:
            // something must be shown on it
            if (c.sortingOrder >= 200 && c.GetComponentInChildren<Graphic>(false) != null) return true;
        }
        return false;
    }

    // F4: what the runner is doing, for trying out the speeds
    string Facts()
    {
        float pace = player.speed > 0.2f ? 1000f / player.speed / 60f : 0f;
        string paceText = pace > 0f ? Mathf.FloorToInt(pace) + ":" + Mathf.FloorToInt(pace % 1f * 60f).ToString("00") + " min/km" : "standing";
        return paceText + "   (real life)\n"
               + "Ground: " + player.ground.surface + ", speed " + player.ground.speed.ToString("0.00") + ", rough " + player.ground.rough.ToString("0.0") + "\n"
               + "Slope: " + (player.gradient * 100f).ToString("0") + " %, slope factor " + RunPlayer.SlopeFactor(player.gradient, player.ground.rough).ToString("0.00") + "\n"
               + "Run so far: " + (player.distance / 1000f).ToString("0.00") + " km   Tired: " + (player.fatigue * 100f).ToString("0") + " %";
    }

    void ClearBig()
    {
        if (bigText != null && phase == Phase.Running) bigText.text = "";
    }

    // GPS points stamped with the race clock in replay seconds, like every other competition's replay
    void Sample()
    {
        SampleOthers(player.raceSeconds);
        track.times.Add(player.raceSeconds / PackRunning.GameSecondsPerSecond);
        track.positions.Add(mapDef.ToWorld(new CoursePoint(player.e, player.n)));
    }

    static string RaceTime(float seconds)
    {
        int s = Mathf.FloorToInt(seconds);
        return s >= 3600 ? (s / 3600) + ":" + (s / 60 % 60).ToString("00") + ":" + (s % 60).ToString("00")
                         : (s / 60) + ":" + (s % 60).ToString("00");
    }

    // ---- Punching ----

    void CheckPunch(double ae, double an, double be, double bn)
    {
        if (phase != Phase.Running) return;
        for (int i = 0; i < world.FlagCount; i++)
        {
            world.Flag(i, out double fe, out double fn);
            if (SegmentDistance(fe, fn, ae, an, be, bn) > PunchReach) continue;
            if (i == lastPunched) continue;
            lastPunched = i;
            punches.Add(i);
            if (i == inOrder) inOrder++;
            punchTimes.Add(player.raceSeconds);
            if (i == world.FlagCount - 1)
            {
                Finish();
                return;
            }
            sounds.Punch();
            // The map shows the next leg from the control just punched (if it is the next one on the course)
            if (handMap != null && i + 1 > handMap.Leg) handMap.FrameLeg(i + 1);
        }
    }

    static double SegmentDistance(double pe, double pn, double ae, double an, double be, double bn)
    {
        double de = be - ae, dn = bn - an;
        double len = de * de + dn * dn;
        double t = len < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((pe - ae) * de + (pn - an) * dn) / len));
        double x = ae + de * t - pe, y = an + dn * t - pn;
        return Math.Sqrt(x * x + y * y);
    }

    // The controls, punched in order (other punches in between are allowed): the time each was punched, or null
    // when one is missing or came in the wrong order
    float[] Splits()
    {
        int controls = world.FlagCount - 1;
        float[] splits = new float[controls + 1];
        int next = 0;
        for (int k = 0; k < punches.Count && next <= controls; k++)
        {
            if (punches[k] != next) continue;
            splits[next] = punchTimes[k];
            next++;
        }
        return next == controls + 1 ? splits : null;
    }

    // The finish: the time and the split times go to the race like a 2D runner's, and the usual statistics screen
    // (results, split times, replay) and the usual results with prize money follow (2026-11-19, Anders: the same
    // results as every other competition). Missing a control is told at once; the result list has no
    // disqualification yet, so the time is still listed (comes with multiplayer).
    void Finish()
    {
        phase = Phase.Finished;
        player.controlsOn = false;
        sounds.Finish();
        Sample();
        resultSeconds = player.raceSeconds;
        float[] splits = Splits();
        ok = splits != null;
        float best = Best(course.id);
        bool record = ok && (best <= 0f || resultSeconds < best);
        if (record) SetBest(course.id, resultSeconds);
        PlayerPrefs.Save();
        finalSplits = splits;
        Report(splits);   // at once, so the others know this runner is in

        float km = player.distance / 1000f;
        string pace = km > 0.05f ? Pace(resultSeconds / km) + " min/km" : "";
        string missed = ok ? "" : "\n<size=45%><color=#FF8A6A>Disqualified: " + MissedText() + "</color></size>";
        bigText.text = (ok ? "Finish!" : "Finish") + "  " + RaceTime(resultSeconds)
                       + "\n<size=45%>You ran " + km.ToString("0.00") + " km   " + pace
                       + (record && best > 0f ? "   New record!" : best > 0f && ok ? "   Record " + RaceTime(best) : "") + "</size>" + missed;
        Invoke(nameof(AfterFinish), ok ? 4.5f : 6.5f);
    }

    static string Pace(float secondsPerKm)
    {
        int s = Mathf.RoundToInt(secondsPerKm);
        return (s / 60) + ":" + (s % 60).ToString("00");
    }

    // "control 3" or "controls 3, 4" not punched in order
    string MissedText()
    {
        List<int> missing = new List<int>();
        int next = 0, controls = world.FlagCount - 1;
        foreach (int p in punches)
        {
            if (p == next) next++;
        }
        for (int i = next; i < controls; i++) missing.Add(i + 1);
        if (missing.Count == 0) return "the controls were not punched in order";
        if (missing.Count > 4) return missing.Count + " controls not punched in order (from control " + missing[0] + ")";
        return (missing.Count == 1 ? "control " : "controls ") + string.Join(", ", missing) + " not punched in order";
    }

    // The race clock at every control and the finish, written on the player as the 2D race does it
    void Report(float[] splits)
    {
        NetworkPlayer np = NetworkPlayer.Local;
        if (np == null || np.Object == null || !np.Object.HasStateAuthority) return;
        int controls = world.FlagCount - 1;
        int total = Mathf.Max(1, Mathf.RoundToInt(resultSeconds));
        np.relaySplits.Set(0, 0);
        np.relaySplitVariants.Set(0, 0);
        for (int i = 0; i < controls && i + 1 < np.relaySplits.Length; i++)
        {
            int at = splits != null ? Mathf.RoundToInt(splits[i]) : PunchedAt(i);
            np.relaySplits.Set(i + 1, at);
            np.relaySplitVariants.Set(i + 1, 0);
        }
        if (controls + 1 < np.relaySplits.Length)
        {
            np.relaySplits.Set(controls + 1, total);
            np.relaySplitVariants.Set(controls + 1, 0);
        }
        np.relaySplitLeg = 0;
        np.relaySplitCount = Mathf.Min(np.relaySplits.Length, controls + 2);
        np.relayLegTimes.Set(0, total);
        np.relayTotals.Set(0, total);
        np.hourCount = total / 3600;
        np.minuterCount = total / 60 % 60;
        np.secoundCount = total % 60;
        np.raceFinished = true;
    }

    // The first time a control was punched, or 0 when it never was
    int PunchedAt(int control)
    {
        for (int k = 0; k < punches.Count; k++)
            if (punches[k] == control) return Mathf.RoundToInt(punchTimes[k]);
        return 0;
    }

    // From the 3D finish to the 2D map, where the statistics screen opens over it as in every competition
    void ToResults()
    {
        if (this == null) return;
        CloseWatchList();
        bigText.text = "";
        atResults = true;
        twoDHidden = false;
        foreach (Canvas c in hiddenCanvases) if (c != null) c.enabled = true;
        hiddenCanvases.Clear();
        foreach (Renderer r in hiddenRenderers) if (r != null) r.enabled = true;
        hiddenRenderers.Clear();
        if (cameraController != null) cameraController.enabled = true;
        ShowTwoDMap(true);
        // The usual statistics screen: results, split times, replay; then "Show the results" (prize money) and back
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (IndividualRace.Active != null) IndividualRace.Active.ShowStats(AllTracks());
        else CompetitionFlow.HostShowResults();
        Debug.Log("RunSession: finished, statistics shown (" + (IndividualRace.Active != null) + ")");
    }

    // The replay is drawn on the 2D map, so the 3D view is switched off while it plays
    void ShowTwoDMap(bool show)
    {
        if (player != null) player.cam.enabled = !show;
        if (handMap != null) handMap.SetVisible(!show);
        if (world != null) world.gameObject.SetActive(!show);
        if (mainCam != null) mainCam.cullingMask = show ? mainMask : 0;
        if (hud != null) hud.enabled = !show;
    }

    // ---- Hiding the 2D game underneath ----

    void HideTwoD()
    {
        twoDHidden = true;
        NetworkPlayer local = NetworkPlayer.Local;
        cameraController = local != null ? local.GetComponent<CameraController>() : null;
        mainCam = cameraController != null && cameraController.cam != null ? cameraController.cam : Camera.main;
        if (cameraController != null) cameraController.enabled = false;
        if (mainCam != null)
        {
            mainMask = mainCam.cullingMask;
            mainCam.cullingMask = 0;
        }
        else
        {
            mainMask = ~0;
        }
        // The 2D runners and their route lines would show on the map in the hand
        foreach (NetworkPlayer p in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            foreach (Renderer r in p.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                r.enabled = false;
                hiddenRenderers.Add(r);
            }
            // and so would their name tags
            foreach (Canvas c in p.GetComponentsInChildren<Canvas>(true))
            {
                if (!c.enabled) continue;
                c.enabled = false;
                hiddenCanvases.Add(c);
            }
        }
        HideCanvases();
    }

    // The 2D race's own screens (clock, leaderboard, buttons). Messages, menus and pop-ups stay.
    void HideCanvases()
    {
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c == null || !c.enabled || !c.isRootCanvas || own.Contains(c)) continue;
            if (c.sortingOrder >= 200 || c.gameObject.scene.name == "DontDestroyOnLoad") continue;
            if (c.renderMode == RenderMode.WorldSpace) continue;
            c.enabled = false;
            hiddenCanvases.Add(c);
        }
    }

    void BuildHud()
    {
        hud = new GameObject("Run HUD", typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
        hud.transform.SetParent(transform, false);
        hud.renderMode = RenderMode.ScreenSpaceOverlay;
        hud.sortingOrder = 50;
        own.Add(hud);
        ScreenSettings.Fit(hud.GetComponent<CanvasScaler>());

        backdrop = CourseUI.Panel("Loading", hud.transform, new Color(0.11f, 0.16f, 0.13f), false).gameObject;
        CourseUI.Stretch((RectTransform)backdrop.transform);
        TextMeshProUGUI loadingTitle = CourseUI.Text("Title", backdrop.transform, "Run it yourself\n<size=60%>" + course.name + "</size>",
            64, new Color(0.96f, 0.94f, 0.86f), FontStyles.Bold, TextAlignmentOptions.Center);
        loadingTitle.richText = true;
        CourseUI.Place(loadingTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), new Vector2(1400f, 240f));

        Image clockBack = CourseUI.Panel("Clock", hud.transform, new Color(0.1f, 0.14f, 0.12f, 0.7f));
        CourseUI.Place(clockBack.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(220f, 64f));
        clockText = CourseUI.Text("Time", clockBack.transform, "0:00", 40, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Stretch(clockText.rectTransform);
        HoverTip.Add(clockBack, "Your race time, as in real life.");

        bigText = CourseUI.Text("Big", hud.transform, "", 120, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Place(bigText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1500f, 420f));
        bigText.richText = true;
        bigText.outlineWidth = 0.2f;
        bigText.outlineColor = new Color32(0, 0, 0, 200);
        factsText = CourseUI.Text("Facts", hud.transform, "", 20, Color.white, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        CourseUI.Place(factsText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(700f, 140f));
        factsText.outlineWidth = 0.2f;
        factsText.outlineColor = new Color32(0, 0, 0, 200);
        hintText = CourseUI.Text("Hint", hud.transform, "", 18, new Color(1f, 1f, 1f, 0.85f), FontStyles.Normal, TextAlignmentOptions.Center);
        CourseUI.Place(hintText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(1600f, 56f));
        foreach (Graphic g in hud.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
    }

    void EnsureListener()
    {
        foreach (AudioListener l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            if (l.enabled && l.gameObject.activeInHierarchy) return;
        }
        player.cam.gameObject.AddComponent<AudioListener>();
    }

    void Fail(string message)
    {
        phase = Phase.Failed;
        if (loadingCard != null) loadingCard.Close();
        loadingCard = null;
        if (hud == null) BuildHud();
        if (backdrop != null) Destroy(backdrop);
        results = new GameObject("Run problem", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        results.transform.SetParent(transform, false);
        Canvas canvas = results.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;
        own.Add(canvas);
        ScreenSettings.Fit(results.GetComponent<CanvasScaler>());
        Image card = CourseUI.CardPanel("Card", results.transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 340));
        CourseUI.Column(card.gameObject, 16, new RectOffset(40, 40, 34, 34));
        TextMeshProUGUI text = CourseUI.Text("Text", card.transform, message, 28, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Size(text, 160);
        Button back = CourseUI.Button("Back", card.transform, CompetitionFlow.BackButtonText, CourseUI.ButtonKind.Primary, CompetitionFlow.HostReturnToLobby, 26);
        CourseUI.Size(back, 64);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Active == this) Active = null;
        RouteAnalysis.RealTimer = null;
        if (loadingCard != null) loadingCard.Close();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (mainCam != null) mainCam.cullingMask = mainMask;
        if (cameraController != null) cameraController.enabled = true;
        foreach (Canvas c in hiddenCanvases) if (c != null) c.enabled = true;
        foreach (Renderer r in hiddenRenderers) if (r != null) r.enabled = true;
    }
}
