using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// What the statistics screen (RelayStatsUI) needs from the race it shows: a relay, or an individual competition
public interface IRaceStats
{
    CourseData Course { get; }
    bool IsRelay { get; }
    int LegCount { get; }
    RelayHistory History { get; }
    string LegDescription(int leg);
    void HostContinue();
}

// An individual competition in the competition scene. Created by CourseLoader for courses that are not relays.
// Records every runner's punches (on NetworkPlayer, the same way relays do as "leg 0") and their routes, and when
// everyone is in shows the statistics screen: results, split times with places and estimated mistakes, and a replay
// with all players. The host then shows the results, so the competition never ends by itself.
public class IndividualRace : MonoBehaviour, IRaceStats
{
    public static IndividualRace Active { get; private set; }

    CourseData course;
    RelayReplayRecorder recorder;
    RelayStatsUI statsUI;
    readonly RelayHistory history = new RelayHistory();
    bool started;
    bool statsShown;
    TextMeshProUGUI pickInfo;
    List<RelayTrack> ownTracks;     // routes recorded elsewhere (run it yourself: the 3D run), shown instead of the 2D runners'

    // Run it yourself (2026-11-19): the runner ran in the 3D world, so the replay shows that GPS track
    public void UseTracks(List<RelayTrack> tracks) => ownTracks = tracks;

    public bool FinalResultsAllowed { get; private set; }
    public CourseData Course => course;
    public bool IsRelay => false;
    public int LegCount => 1;
    public RelayHistory History => history;

    public static IndividualRace Create(CourseData course)
    {
        IndividualRace race = new GameObject("Individual race").AddComponent<IndividualRace>();
        race.course = course;
        race.recorder = race.gameObject.AddComponent<RelayReplayRecorder>();
        Active = race;
        return race;
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
        if (statsUI != null) Destroy(statsUI.gameObject);
    }

    // "Course: 12 controls, 4.3 km"
    public string LegDescription(int leg)
    {
        return course.name + ": " + course.controls.Count + " controls, " + course.LengthText();
    }

    // The choose-runner screen tells how long the course is
    public void ShowPickInfo(GameObject panel)
    {
        if (panel == null) return;
        if (pickInfo == null)
        {
            Image back = CourseUI.Panel("Course info", panel.transform, CourseUI.Card);
            CourseUI.Place(back.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 20), new Vector2(900, 60));
            back.raycastTarget = false;
            pickInfo = CourseUI.Text("Text", back.transform, "", 26, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
            CourseUI.Stretch(pickInfo.rectTransform, 16, 16, 4, 4);

            // The runner list ends above the bar, so the bar does not cover its last row
            RunnerPickerUI picker = panel.GetComponentInChildren<RunnerPickerUI>(true);
            if (picker != null && picker.scroll != null)
            {
                RectTransform list = (RectTransform)picker.scroll.transform;
                list.offsetMin = new Vector2(list.offsetMin.x, Mathf.Max(list.offsetMin.y, 96f));
            }
        }
        string cap = RunnerRules.CapText(new System.Collections.Generic.List<CardData>(), 1);
        pickInfo.text = LegDescription(0) + (cap.Length > 0 ? "   <color=#E8742A>" + cap + "</color>" : "");
    }

    // Every runner has drawn the first stretch: the race starts (later stretches are ignored here)
    public void OnRaceStart(AgentMovement agent)
    {
        if (started) return;
        started = true;
        recorder.Begin(0, agent, true);
        OnPunch(agent, 0);
    }

    // The team clock at the start (index 0), at every control and at the finish
    public void OnPunch(AgentMovement agent, int index)
    {
        NetworkPlayer np = agent.GetComponent<NetworkPlayer>();
        if (np == null || np.Object == null || !np.Object.HasStateAuthority || index < 0 || index >= np.relaySplits.Length) return;
        int clock = RelayRace.ClockSeconds(agent);
        np.relaySplits.Set(index, clock);
        np.relaySplitVariants.Set(index, 0);
        np.relaySplitLeg = 0;
        np.relaySplitCount = Mathf.Max(np.relaySplitCount, index + 1);
        if (index == course.controls.Count + 1)
        {
            np.relayLegTimes.Set(0, Mathf.Max(0, clock - np.relaySplits[0]));
            np.relayTotals.Set(0, Mathf.Max(1, clock));
        }
    }

    void Update()
    {
        if (!statsShown && started && AllFinished())
        {
            statsShown = true;
            recorder.Stop();
            statsUI = RelayStatsUI.Create(this);
            statsUI.Open(0, true, ownTracks ?? recorder.Tracks);
        }
        if (CompetitionFlow.ResultsShown && statsUI != null && statsUI.IsOpen)
        {
            statsUI.Close();
        }
    }

    static bool AllFinished()
    {
        int count = 0;
        foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player.Object == null || !player.Object.IsValid) continue;
            count++;
            if (!player.raceFinished || player.relayTotals[0] <= 0) return false;
        }
        return count > 0;
    }

    // The host pressed "Show the results"
    public void HostContinue()
    {
        if (!CompetitionFlow.LocalIsHost()) return;
        FinalResultsAllowed = true;
        CompetitionFlow.HostShowResults();
    }
}
