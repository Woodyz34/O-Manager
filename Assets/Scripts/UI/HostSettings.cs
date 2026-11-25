using System.Text;
using UnityEngine;

// The host's lobby settings (2026-11-19, test night list): how the TV production of a relay runs, how fast the race
// runs and how many bears and control moves every player gets. The host changes them in HostSettingsPopup, in the
// lobby or during the race (Esc menu), and they are saved on the host's computer for the next lobby.
// The host publishes them on its own player (NetworkPlayer.hostOptions) and everyone reads them from there, so all
// players slow down, speed up and show the same thing at the same time.
public static class HostSettings
{
    public struct Values
    {
        public bool tvStartEveryLeg;    // the start of every leg on TV (else only the first leg's start)
        public bool tvFinishEveryLeg;   // the finish of every leg on TV (else only the last leg's finish)
        public int tvFinishShown;       // how many runners the finish camera shows, the first ones in (0 = all)
        public int tvStartShown;        // how many runners the start camera shows, the first ones off (0 = all)
        public int tvControlShown;      // how many runners every TV control shows, the first ones there (0 = all)
        public int tvShotPercent;       // how long every TV shot is, in percent of the 1.0.4 length
        public int tvSpeedTenths;       // how fast the runners run on TV, times real running pace, in tenths
        public int raceSpeedTenths;     // how fast the race runs on the map (the "GPS"), in tenths of the normal pace
        public int bearsPerLeg;         // Bears mode: bears every player can hide on every leg
        public int movesPerLeg;         // Move a control mode: controls every player can move on every leg
    }

    // The defaults the user asked for: start and finish on every leg, all runners at the finish, the first three at
    // the TV controls, half as long TV shots as in 1.0.4 and a little faster running on TV
    public static Values Defaults => new Values
    {
        tvStartEveryLeg = true,
        tvFinishEveryLeg = true,
        tvFinishShown = 0,
        tvStartShown = 0,
        tvControlShown = 3,
        tvShotPercent = 50,
        tvSpeedTenths = 18,
        raceSpeedTenths = 10,
        bearsPerLeg = 1,
        movesPerLeg = 1,
    };

    public const int MaxShown = 10;                     // the sliders go 1..10, and "All" past that
    public const int MinShotPercent = 20, MaxShotPercent = 150;
    public const int MinTvSpeed = 10, MaxTvSpeed = 40;  // 1.0x to 4.0x real running pace
    public const int MinRaceSpeed = 3, MaxRaceSpeed = 30;   // 0.3x to 3.0x the normal pace
    public const int MaxPerLeg = 3;

    const string PrefKey = "Lobby.HostSettings";

    static Values? mine;

    // The host's own values (saved between games)
    public static Values Mine
    {
        get
        {
            if (mine == null) mine = Decode(PlayerPrefs.GetString(PrefKey, ""));
            return mine.Value;
        }
        set
        {
            mine = Clean(value);
            PlayerPrefs.SetString(PrefKey, Encode(mine.Value));
            PlayerPrefs.Save();
            cachedFrame = -1;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        mine = null;
        cachedFrame = -1;
    }

    static int cachedFrame = -1;
    static Values cached;

    // The values in play: the host's, read from the host's player. Before anyone has published (or with no network),
    // the local player's own values.
    public static Values Current
    {
        get
        {
            if (cachedFrame == Time.frameCount) return cached;
            cachedFrame = Time.frameCount;
            // The Monday relay has no real host: the normal values for everyone
            if (MondayRelay.Active)
            {
                cached = Defaults;
                return cached;
            }
            cached = Mine;
            NetworkPlayer local = NetworkPlayer.Local;
            if (local == null || CompetitionFlow.LocalIsHost()) return cached;
            foreach (NetworkPlayer player in UnityEngine.Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
            {
                if (player == null || player.Object == null || !player.Object.IsValid) continue;
                string text = player.hostOptions.ToString();
                if (string.IsNullOrEmpty(text)) continue;
                cached = Decode(text);
                break;
            }
            return cached;
        }
    }

    // What the local player publishes: its values when it is the host, nothing otherwise
    public static string ToPublish => CompetitionFlow.LocalIsHost() ? Encode(Mine) : "";

    // ---- Shortcuts used in the game ----

    public static float TvShotScale => Current.tvShotPercent / 100f;
    public static float TvSpeed => Current.tvSpeedTenths / 10f;
    public static float RaceSpeed => Current.raceSpeedTenths / 10f;
    public static int BearsPerLeg => Mathf.Clamp(Current.bearsPerLeg, 1, MaxPerLeg);
    public static int MovesPerLeg => Mathf.Clamp(Current.movesPerLeg, 1, MaxPerLeg);

    // ---- Text form, "ts1;tf1;fn0;..." (short, fits a NetworkString<_64>) ----

    public static string Encode(Values v)
    {
        var sb = new StringBuilder();
        sb.Append("ts").Append(v.tvStartEveryLeg ? 1 : 0).Append(';');
        sb.Append("tf").Append(v.tvFinishEveryLeg ? 1 : 0).Append(';');
        sb.Append("fn").Append(v.tvFinishShown).Append(';');
        sb.Append("sn").Append(v.tvStartShown).Append(';');
        sb.Append("tn").Append(v.tvControlShown).Append(';');
        sb.Append("tl").Append(v.tvShotPercent).Append(';');
        sb.Append("tv").Append(v.tvSpeedTenths).Append(';');
        sb.Append("rs").Append(v.raceSpeedTenths).Append(';');
        sb.Append("b").Append(v.bearsPerLeg).Append(';');
        sb.Append("m").Append(v.movesPerLeg);
        return sb.ToString();
    }

    public static Values Decode(string text)
    {
        Values v = Defaults;
        if (string.IsNullOrEmpty(text)) return v;
        foreach (string part in text.Split(';'))
        {
            int digits = 0;
            while (digits < part.Length && char.IsLetter(part[digits])) digits++;
            if (digits == 0 || digits == part.Length) continue;
            if (!int.TryParse(part.Substring(digits), out int n)) continue;
            switch (part.Substring(0, digits))
            {
                case "ts": v.tvStartEveryLeg = n != 0; break;
                case "tf": v.tvFinishEveryLeg = n != 0; break;
                case "fn": v.tvFinishShown = n; break;
                case "sn": v.tvStartShown = n; break;
                case "tn": v.tvControlShown = n; break;
                case "tl": v.tvShotPercent = n; break;
                case "tv": v.tvSpeedTenths = n; break;
                case "rs": v.raceSpeedTenths = n; break;
                case "b": v.bearsPerLeg = n; break;
                case "m": v.movesPerLeg = n; break;
            }
        }
        return Clean(v);
    }

    static Values Clean(Values v)
    {
        v.tvFinishShown = Mathf.Clamp(v.tvFinishShown, 0, MaxShown);
        v.tvStartShown = Mathf.Clamp(v.tvStartShown, 0, MaxShown);
        v.tvControlShown = Mathf.Clamp(v.tvControlShown, 0, MaxShown);
        v.tvShotPercent = Mathf.Clamp(v.tvShotPercent, MinShotPercent, MaxShotPercent);
        v.tvSpeedTenths = Mathf.Clamp(v.tvSpeedTenths, MinTvSpeed, MaxTvSpeed);
        v.raceSpeedTenths = Mathf.Clamp(v.raceSpeedTenths, MinRaceSpeed, MaxRaceSpeed);
        v.bearsPerLeg = Mathf.Clamp(v.bearsPerLeg, 1, MaxPerLeg);
        v.movesPerLeg = Mathf.Clamp(v.movesPerLeg, 1, MaxPerLeg);
        return v;
    }
}
