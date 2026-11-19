using System.Collections.Generic;
using UnityEngine;

// Run it yourself online (2026-11-19). Every runner tells the others where it is in the 3D world through its
// NetworkPlayer (run3dAt, run3dYaw, run3dSpeed, run3dFlags), about 60 times a second from its own computer.
//   - Individual (ExtraMode.RunYourself): everyone runs in their own forest and sees nobody; the GPS tracks of all
//     are recorded, so the statistics screen and replay afterwards show everyone.
//   - Mass start (ExtraMode.RunMass): everyone starts together, side by side in rows at the start, sees the others as
//     runners (reading their maps when the player holds the map up) and cannot run through them.
// The countdown starts when every computer has built its forest. The results come when everyone is in.
public partial class RunSession
{
    const int FlagMapUp = 1, FlagFallen = 2, FlagAir = 4, FlagReady = 8, FlagRightHand = 16;

    class Remote
    {
        public NetworkPlayer player;
        public TvStickFigure figure;
        public Vector2 shown;
        public float yaw;
        public bool placed;
        public RelayTrack track;
        public float finishedFor = -1f;   // seconds since this runner came in (mass start)
        public Vector2 leave;             // the way it walks off after the finish
    }

    readonly Dictionary<NetworkPlayer, Remote> remotes = new Dictionary<NetworkPlayer, Remote>();
    float waitingSince;
    float nextPlayerScan;
    bool waitingForOthers;

    // More than one player in the competition
    static bool Online => Players().Count > 1;

    static List<NetworkPlayer> Players()
    {
        var list = new List<NetworkPlayer>();
        foreach (NetworkPlayer p in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (p != null && p.Object != null && p.Object.IsValid) list.Add(p);
        }
        list.Sort((a, b) => a.Object.StateAuthority.PlayerId.CompareTo(b.Object.StateAuthority.PlayerId));
        return list;
    }

    // Mass start: rows of six, 1.3 m apart side by side and 1.6 m between rows, across the way to control 1
    void StartSpot(ref double e, ref double n)
    {
        if (!Mass) return;
        List<NetworkPlayer> all = Players();
        int index = all.IndexOf(NetworkPlayer.Local);
        if (index <= 0 && all.Count <= 1) return;
        index = Mathf.Max(0, index);
        int inRow = Mathf.Min(6, all.Count), row = index / 6, col = index % 6;
        float across = (col - (inRow - 1) * 0.5f) * 1.3f, back = row * 1.6f;
        float bearing = Bearing(points[0], points[1]) * Mathf.Deg2Rad;
        double fx = Mathf.Sin(bearing), fz = Mathf.Cos(bearing);   // ahead
        double rx = fz, rz = -fx;                                  // to the right
        e += rx * across - fx * back;
        n += rz * across - fz * back;
    }

    int ReadyCount(out int all)
    {
        List<NetworkPlayer> players = Players();
        all = players.Count;
        int ready = 0;
        foreach (NetworkPlayer p in players) if ((p.run3dFlags & FlagReady) != 0) ready++;
        return ready;
    }

    // Every frame: tell the others where this runner is, and move the others' runners
    void NetTick(float dt)
    {
        NetworkPlayer local = NetworkPlayer.Local;
        if (local != null && local.Object != null && local.Object.HasStateAuthority)
        {
            // After the finish the camera may be watching someone else: this runner stays where it came in
            if (phase != Phase.Finished)
            {
                local.run3dAt = new Vector2((float)player.e, (float)player.n);
                local.run3dYaw = player.yaw;
                local.run3dPitch = player.pitch;
                local.run3dSpeed = player.speed;
                if (handMap != null)
                {
                    local.run3dMapLeg = handMap.Leg;
                    local.run3dMapTurn = handMap.Turn;
                }
            }
            local.run3dPunched = inOrder;
            local.run3dFlags = phase == Phase.Finished ? FlagReady | (RunSettings.RightHand ? FlagRightHand : 0) : FlagReady | (player.mapUp ? FlagMapUp : 0) | (player.Fallen ? FlagFallen : 0) | (player.Airborne ? FlagAir : 0) | (RunSettings.RightHand ? FlagRightHand : 0);
        }

        if (Time.unscaledTime >= nextPlayerScan)
        {
            nextPlayerScan = Time.unscaledTime + 1f;
            var seen = new HashSet<NetworkPlayer>();
            foreach (NetworkPlayer p in Players())
            {
                if (p == local) continue;
                seen.Add(p);
                if (!remotes.ContainsKey(p)) remotes[p] = MakeRemote(p);
            }
            foreach (NetworkPlayer gone in new List<NetworkPlayer>(remotes.Keys))
            {
                if (seen.Contains(gone)) continue;
                remotes[gone].figure?.Destroy();
                remotes.Remove(gone);
            }
        }

        player.others.Clear();
        foreach (Remote r in remotes.Values)
        {
            if (r.player == null || r.player.Object == null || !r.player.Object.IsValid) continue;
            if ((r.player.run3dFlags & FlagReady) == 0) continue;
            if (Mass && r.figure != null && r.placed && r.player.raceFinished)
            {
                PoseFinished(r, dt);
                continue;
            }
            Vector2 at = r.player.run3dAt;
            if (!r.placed) { r.shown = at; r.yaw = r.player.run3dYaw; r.placed = true; }
            Vector2 before = r.shown;
            float follow = 1f - Mathf.Exp(-dt * 12f);
            r.shown = (r.shown - at).sqrMagnitude > 400f ? at : Vector2.Lerp(r.shown, at, follow);
            r.yaw = Mathf.LerpAngle(r.yaw, r.player.run3dYaw, follow);
            if (!Mass) continue;
            if (!r.player.raceFinished) player.others.Add(r.shown);
            if (r.figure == null) continue;

            float moved = Vector2.Distance(before, r.shown);
            float ground = world.GroundY(r.shown.x, r.shown.y);
            Vector3 at3 = world.ToScene(r.shown.x, r.shown.y, ground);
            float rad = r.yaw * Mathf.Deg2Rad;
            r.figure.forward = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            float ahead = world.GroundY(r.shown.x + Mathf.Sin(rad) * 2f, r.shown.y + Mathf.Cos(rad) * 2f);
            int flags = r.player.run3dFlags;
            r.figure.readMode = (flags & FlagMapUp) != 0 ? 1 : 0;
            r.figure.mapHand = (flags & FlagRightHand) != 0 ? 1 : -1;
            r.figure.tired = Mathf.MoveTowards(r.figure.tired, (flags & FlagFallen) != 0 ? 1f : 0f, dt * 6f);
            r.figure.Pose(at3.x, at3.y, at3.z, moved, at3 + Vector3.up * 1000f, 0f, dt, (ahead - ground) / 2f, (flags & FlagAir) != 0 ? 0.6f : 0f);
            // Far away runners are not drawn (the forest hides them anyway)
            float far = Vector2.Distance(r.shown, new Vector2((float)player.e, (float)player.n));
            // The runner watched is not drawn: the camera is in its head
            r.figure.SetVisible(far < world.ViewDistance && r.player != watching);
        }
    }

    // Mass start: a runner who is in punches the finish, stands bent over getting the breath back, walks off and is
    // gone after about 9 s, so nobody can see that a runner stands waiting at the finish (2026-11-19, Anders)
    void PoseFinished(Remote r, float dt)
    {
        if (r.finishedFor < 0f)
        {
            r.finishedFor = 0f;
            float away = (r.yaw + (r.player.Object.StateAuthority.PlayerId % 2 == 0 ? 75f : -75f)) * Mathf.Deg2Rad;
            r.leave = new Vector2(Mathf.Sin(away), Mathf.Cos(away));
        }
        r.finishedFor += dt;
        float t = r.finishedFor;
        if (t > 9f)
        {
            r.figure.SetVisible(false);
            return;
        }
        Vector2 before = r.shown;
        bool walking = t > 4f;
        if (walking) r.shown += r.leave * 1.4f * dt;
        float wantYaw = walking ? Mathf.Atan2(r.leave.x, r.leave.y) * Mathf.Rad2Deg : r.yaw;
        r.yaw = Mathf.MoveTowardsAngle(r.yaw, wantYaw, 120f * dt);
        float rad = r.yaw * Mathf.Deg2Rad;
        r.figure.forward = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        float ground = world.GroundY(r.shown.x, r.shown.y);
        Vector3 at3 = world.ToScene(r.shown.x, r.shown.y, ground);
        world.Flag(world.FlagCount - 1, out double fe, out double fn);
        Vector3 unit = world.ToScene(fe, fn, world.GroundY(fe, fn) + 0.9f);
        r.figure.readMode = 0;
        r.figure.tired = Mathf.MoveTowards(r.figure.tired, t > 0.8f && !walking ? 1f : 0f, dt * 2.5f);
        r.figure.walk = Mathf.MoveTowards(r.figure.walk, walking ? 1f : 0f, dt * 2f);
        r.figure.Pose(at3.x, at3.y, at3.z, Vector2.Distance(before, r.shown), unit, t < 0.8f ? 1f : 0f, dt);
        float far = Vector2.Distance(r.shown, new Vector2((float)player.e, (float)player.n));
        r.figure.SetVisible(far < world.ViewDistance);
    }

    Remote MakeRemote(NetworkPlayer p)
    {
        Color color = ColourOf(p, remotes.Count + 1);
        string name = p.nickName.ToString();
        if (string.IsNullOrWhiteSpace(name)) name = "Player " + p.Object.StateAuthority.PlayerId;
        var r = new Remote { player = p, track = new RelayTrack { label = name, isLocal = false, color = color } };
        if (Mass && world != null) r.figure = new TvStickFigure(world.transform, name, color, p.Object.StateAuthority.PlayerId * 7919 + 13);
        return r;
    }

    // The player's colour on the map, never white (it can't be seen on the map); a colour of its own otherwise
    static readonly Color[] Spare =
    {
        new Color(0.91f, 0.45f, 0.16f), new Color(0.13f, 0.47f, 0.85f), new Color(0.20f, 0.62f, 0.33f),
        new Color(0.80f, 0.16f, 0.30f), new Color(0.55f, 0.30f, 0.75f), new Color(0.10f, 0.65f, 0.70f)
    };

    static Color ColourOf(NetworkPlayer p, int index)
    {
        Color c = Spare[index % Spare.Length];
        BodyHandler body = p != null ? p.GetComponent<BodyHandler>() : null;
        if (body != null && body.cirkel != null)
        {
            c = body.cirkel.material != null ? body.cirkel.material.color : body.cirkel.color;
            c.a = 1f;
        }
        float light = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        Color.RGBToHSV(c, out _, out float saturation, out _);
        if (light > 0.8f || (light > 0.6f && saturation < 0.25f)) c = Spare[index % Spare.Length];
        return c;
    }

    // A GPS point for everyone else too, on this runner's race clock (everyone started together)
    void SampleOthers(float raceSeconds)
    {
        float t = raceSeconds / PackRunning.GameSecondsPerSecond;
        foreach (Remote r in remotes.Values)
        {
            if (r.player == null || r.player.Object == null || !r.player.Object.IsValid || !r.placed) continue;
            if (r.player.raceFinished && r.track.positions.Count > 0) continue;
            r.track.times.Add(t);
            r.track.positions.Add(mapDef.ToWorld(new CoursePoint(r.player.run3dAt.x, r.player.run3dAt.y)));
        }
    }

    // The finish was shown: the results when everyone is in, else wait for them
    void AfterFinish()
    {
        if (this == null || atResults) return;
        int inCount = 0, all = 0;
        foreach (NetworkPlayer p in Players())
        {
            all++;
            if (p.raceFinished) inCount++;
        }
        if (inCount >= all)
        {
            ToResults();
            return;
        }
        waitingForOthers = true;
        OpenWatchList();
        bigText.text = Watching ? "" : "<size=40%>Waiting for the others: " + inCount + " of " + all + " in\n<size=80%>Click a runner on the left to see what they see</size></size>";
        Invoke(nameof(AfterFinish), 0.5f);
    }

    // The replay shows everyone: this runner first, with the dot each one has on the map
    List<RelayTrack> AllTracks()
    {
        NetworkPlayer local = NetworkPlayer.Local;
        if (local != null)
        {
            track.color = ColourOf(local, 0);
            track.look = local.transform;
            string name = local.nickName.ToString();
            if (!string.IsNullOrWhiteSpace(name)) track.label = name;
        }
        var list = new List<RelayTrack> { track };
        foreach (Remote r in remotes.Values)
        {
            if (r.track.positions.Count == 0) continue;
            if (r.player != null) r.track.look = r.player.transform;
            list.Add(r.track);
        }
        return list;
    }
}
