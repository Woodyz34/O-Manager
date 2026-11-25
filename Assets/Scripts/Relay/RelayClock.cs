using Fusion;
using UnityEngine;

// One race clock for the whole relay (2026-11-19). Every team's clock used to tick on its own computer, so after a
// few TV shots (the host slows the race down a moment before the others see it) the teams' clocks were a second or
// two apart although they had started together. Now the host keeps the race clock of the leg, from the moment the
// first team sets off until every team is in, and publishes it with the network tick it was read at
// (NetworkPlayer.relayRaceClock / relayRaceClockTick). A team whose runner is out running sets its clock to it.
// Before the team's own start (waiting for the changeover) its clock stands still as before.
public static class RelayClock
{
    static bool on;
    static double seconds;
    static int leg = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        on = false;
        seconds = 0;
        leg = -1;
    }

    // Every team has drawn the leg; the first team sets off with its time (the same on every computer)
    public static void LegStarts(int legIndex, int firstTeamSeconds)
    {
        if (on && leg == legIndex) return;
        on = true;
        leg = legIndex;
        seconds = firstTeamSeconds;
    }

    public static void LegOver()
    {
        on = false;
    }

    // RelayRace.Update, every frame
    public static void Tick(float deltaTime)
    {
        if (on) seconds += deltaTime * PackRunning.GameSecondsPerSecond;
    }

    // What the local player publishes: the race clock on the host, -1 on the others or between the legs
    public static float ToPublish => on && RelayRace.Active != null && CompetitionFlow.LocalIsHost() ? (float)seconds : -1f;

    // The race clock now, in game seconds, or false when no leg is under way
    public static bool TryNow(out double now)
    {
        now = 0;
        if (RelayRace.Active == null) return false;
        if (CompetitionFlow.LocalIsHost())
        {
            now = seconds;
            return on;
        }
        foreach (NetworkPlayer player in NetworkPlayer.All())
        {
            if (player == null || player.Object == null || !player.Object.IsValid) continue;
            float clock = player.relayRaceClock;
            if (clock < 0f || string.IsNullOrEmpty(player.hostOptions.ToString())) continue;
            now = clock;
            // The value is a little old when it gets here: add the time since the host read it
            NetworkRunner runner = player.Runner;
            int stamped = player.relayRaceClockTick;
            if (runner != null && stamped > 0)
            {
                int ticks = Mathf.Clamp((int)runner.Tick - stamped, 0, 200);
                now += ticks * runner.DeltaTime * Time.timeScale * PackRunning.GameSecondsPerSecond;
            }
            return true;
        }
        return false;
    }
}
