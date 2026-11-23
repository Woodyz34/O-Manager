using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static Unity.Collections.Unicode;

public class Clock : MonoBehaviour
{
    public AgentMovement agentMovement;
    private bool isClockRunning = true;
    private float sekunderPerSekund = 20f;
    public float sekundercount;
    public int minuterCount;
    public int hourCount;

    [Header("Prefabs")]
    [Tooltip("Dra in huvud-prefabet här (GameDashBoardPrefab) som innehåller Clock, EndScreen, osv.")]
    public GameObject GameDashBoard;

    private GameObject instantiatedDashboard;

    private Text secoundBox;
    private Text minuterBox;
    private Text hourBox;
    private float elapsedTime;

    public GameObject EndScreen;
    // Relays: the statistics screen is up, so the finish texts stay hidden
    [HideInInspector] public bool HideFinishTexts;
    public GameObject multiplayerEndScreen;
    private bool endScreenRequested = false;

    [Header("Test")]
    [Tooltip("Endast för utveckling: ger värden en knapp som när som helst avslutar tävlingen och tar alla tillbaka till lobbyn.")]
    [SerializeField] private bool testEndCompetitionButton = false;
    private GameObject testEndButton;
    public Text finishTime;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // The lobby's shop, My Runners and course builder are loaded on top of the lobby (LobbyVisit); not a new race
        if (mode == LoadSceneMode.Additive) return;
        // The object can be destroyed by another scene-loaded handler earlier in the same call (e.g. when the
        // network session shuts down as the scene changes); Unity then still calls this handler
        if (this == null)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            return;
        }
        if (scene.name != "Lobby")
        {
            Start();
        }
        else
        {
            // Klockan ska inte ticka vidare i lobbyn om tävlingen avbröts mitt i ett lopp
            StopTicking();
            endScreenRequested = false;
            // The race's clock and finish texts never show in the lobby (2026-11-19: the time stayed in the corner)
            if (instantiatedDashboard != null && instantiatedDashboard.scene.name != scene.name) Destroy(instantiatedDashboard);
            instantiatedDashboard = null;
        }
    }

    public void Start()
    {
        endScreenRequested = false;
        HideFinishTexts = false;
        StopTicking();
        sekundercount = 0;
        minuterCount = 0;
        hourCount = 0;
        sekunderPerSekund = 20f;
        elapsedTime = 0f;
        isClockRunning = true; 
        if (agentMovement == null)
        {
            return;
        }
        if (ActiveScene.Name == "Lobby" || !agentMovement.HasStateAuthority)
        {
            return;
        }
        Canvas canvas = GameObject.FindGameObjectWithTag("Canvas")?.GetComponent<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("Ingen Canvas hittades i scenen! Lägg till en Canvas först.");
            return;
        }
        GameObject DashBoard = GameObject.FindGameObjectWithTag("GameDashboard");
        if(DashBoard == null && GameDashBoard != null)
        {
            instantiatedDashboard = Instantiate(GameDashBoard, canvas.transform);
            instantiatedDashboard.name = "GameDashboard";
            instantiatedDashboard.tag = "GameDashboard";
        }
        else if(DashBoard != null)
        {
            instantiatedDashboard = DashBoard.GetComponent<GameObject>();
        }
        else
        {
            return;
        }
        StartTicking();
        // Hitta alla nödvändiga komponenter i prefab
        if (instantiatedDashboard != null)
        {
            // Clock-delen (sekunder, minuter, timmar)
            Transform clockSection = instantiatedDashboard.transform.Find("Clock");
            if (clockSection != null)
            {
                hourBox = clockSection.Find("Timmar")?.GetComponent<Text>();
                minuterBox = clockSection.Find("Minuter")?.GetComponent<Text>();
                secoundBox = clockSection.Find("Sekunder")?.GetComponent<Text>();
            }
            else
            {
                Debug.LogError("Clock-delen saknas i prefaben!");
            }

            // EndScreen och YourTime
            EndScreen = instantiatedDashboard.transform.Find("EndScreen")?.gameObject;
           
            multiplayerEndScreen = instantiatedDashboard.transform.Find("Retrurn To Lobby")?.gameObject;
            multiplayerEndScreen.GetComponent<Button>().onClick.AddListener(() => FindObjectOfType<ReturnToLobby>().GoToLobby());

            finishTime = instantiatedDashboard.transform.Find("Your time")?.GetComponent<Text>();

            Debug.Log(" Endscreen null" +  EndScreen + " mulitplayer null " + multiplayerEndScreen + " finishTime " + finishTime);
            finishTime.gameObject.SetActive(false);
            EndScreen.SetActive(false);
            multiplayerEndScreen.SetActive(false);

        }
    }
    // A replay is playing (RelayReplayView): the clock shows the replay's time instead (2026-11-20, Anders: it stood at 00:00:00)
    public void ShowReplayTime(int seconds)
    {
        if (hourBox == null || minuterBox == null || secoundBox == null) return;
        int h = seconds / 3600, m = seconds / 60 % 60, s = seconds % 60;
        hourBox.text = h.ToString("00") + ":";
        minuterBox.text = m.ToString("00") + ":";
        secoundBox.text = s.ToString("00");
    }

    // Back from a replay: the race's own time again
    public void ShowOwnTime()
    {
        if (hourBox == null || minuterBox == null || secoundBox == null) return;
        hourBox.text = hourCount.ToString("00") + ":";
        minuterBox.text = minuterCount.ToString("00") + ":";
        secoundBox.text = Mathf.FloorToInt(sekundercount).ToString("00");
    }

    public void UpdateTimer()
    {
        if(agentMovement.manualPathDrawer == null)
        {
            return;
        }
       
        if (!agentMovement.HasStateAuthority)
        {
            return;
        }
        
        if (agentMovement.WaitingForAllPlayersToReachKontroll == true)
        {
            return;
        }

        if(agentMovement.WaitingForAllPlayersToDraw == true)
        {
            return;
        }

        if(agentMovement.WaitingForAllPlayersToChooseRunner == true)
        {
            return;
        }

        // Relays: the team's clock stands still while its runner waits for the changeover
        if (agentMovement.relayHold)
        {
            return;
        }

        if (!agentMovement.Object.HasStateAuthority)
        {
            return;
        }

       
        // Uppdatera klockan med 20 sekunder varje sekund
        if (isClockRunning)
        {
            var player = GetComponent<NetworkPlayer>();
            float adjustedSecondsPerUpdate;
            adjustedSecondsPerUpdate = 1;
            if (agentMovement.manualPathDrawer.timeToDraw)
                { adjustedSecondsPerUpdate = 0.05f; }

            sekundercount += adjustedSecondsPerUpdate;
            FollowRelayClock(player);
            while (sekundercount >= 60)
            {
                sekundercount -= 60;
                minuterCount += 1;
            }
            while (minuterCount >= 60)
            {
                minuterCount -= 60;
                hourCount += 1;
            }
            // The networked time is set after the minute and hour have rolled over. It used to be set before, so
            // for a moment the others saw e.g. 12:60 (a minute too much) on the leaderboard.
            int wholeSeconds = (int)sekundercount;
            if (player.secoundCount != wholeSeconds) player.secoundCount = wholeSeconds;
            if (player.minuterCount != minuterCount) player.minuterCount = minuterCount;
            if (player.hourCount != hourCount) player.hourCount = hourCount;

            // Whole seconds only. While a route is being drawn the clock moves in small steps,
            // and the decimals used to show.
            secoundBox.text = Mathf.FloorToInt(sekundercount).ToString("00");

            if (minuterCount <= 9)
            {
                minuterBox.text = "0" + minuterCount + ":";
            }
            else
            {
                minuterBox.text = "" + minuterCount + ":";
            }

            if (hourCount <= 9)
            {
                hourBox.text = "0" + hourCount + ":";
            }
            else
            {
                hourBox.text = "" + hourCount + ":";
            }

            
            if (player.Object.HasInputAuthority)
            {
               
                
               
            }
        }
    }
    // Relays: once the team's runner is off, its clock shows the race clock (RelayClock), so every team has exactly
    // the same time (2026-11-19: they were a second or so apart). Small differences are taken up over a few steps,
    // so the clock never jumps back and forth.
    void FollowRelayClock(NetworkPlayer player)
    {
        if (RelayRace.Active == null || player == null || !player.relayRunning) return;
        if (agentMovement.manualPathDrawer.timeToDraw) return;
        if (!RelayClock.TryNow(out double race)) return;
        double mine = hourCount * 3600 + minuterCount * 60 + sekundercount;
        double off = race - mine;
        if (System.Math.Abs(off) < 0.35) return;
        double now = System.Math.Abs(off) > 6 ? race : mine + off * 0.3;
        if (now < 0) now = 0;
        hourCount = (int)(now / 3600);
        now -= hourCount * 3600;
        minuterCount = (int)(now / 60);
        sekundercount = (float)(now - minuterCount * 60);
    }

    public void ChangeClockRunning()
    {
        if (isClockRunning == true)
        {
            isClockRunning = false;
        }
        else
        {
            isClockRunning = true;
        }
    }
    public void ChangeSekunderPerSekund(int x)
    {
        sekunderPerSekund = x;
    }

    public string GetTime()
    {
        // Whole seconds, cut off the same way as the time the other players see
        int seconds = Mathf.FloorToInt(sekundercount);
        string time = minuterCount.ToString("00") + "." + seconds.ToString("00");
        return hourCount == 0 ? time : hourCount + ":" + time;
    }
    public void PlaySingelPlayerEndScreen()
    {
        EndScreen.SetActive(true);
    }
    public void PlayMultiPlayerEndScreen()
    {
        if (!agentMovement.HasStateAuthority)
        {
            Debug.Log("hasNotStateAuthority");
            return;
        }
        if (!NetworkPlayer.Local.Object.HasStateAuthority)
        {
            Debug.Log("hasNotStateAuthority Networked");
            return;
        }
        Debug.Log( "multiplayer screen " + multiplayerEndScreen);
        if(multiplayerEndScreen == null)
        {
            Debug.Log("multiplayerEnd is null");
            
        }
        endScreenRequested = true;
        multiplayerEndScreen.SetActive(IsHost());
    }

    // Test-knappen visas bara för värden, under en pågående tävling, och bara om testEndCompetitionButton är ikryssad
    private void UpdateTestEndButton()
    {
        if (!testEndCompetitionButton || agentMovement == null)
        {
            return;
        }

        bool show = !endScreenRequested
            && !CompetitionFlow.ResultsShown
            && agentMovement.Object != null && agentMovement.Object.IsValid
            && agentMovement.HasStateAuthority
            && IsHost()
            && ActiveScene.Name != "Lobby";

        if (show && testEndButton == null)
        {
            CreateTestEndButton();
        }
        if (testEndButton != null && testEndButton.activeSelf != show)
        {
            testEndButton.SetActive(show);
        }
    }

    private void CreateTestEndButton()
    {
        GameObject canvasObject = GameObject.FindGameObjectWithTag("Canvas");
        if (canvasObject == null)
        {
            return;
        }

        testEndButton = new GameObject("TestEndCompetitionButton", typeof(RectTransform), typeof(Image), typeof(Button));
        testEndButton.transform.SetParent(canvasObject.transform, false);

        RectTransform rect = testEndButton.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(-770f, -480f); // nere till vänster
        rect.sizeDelta = new Vector2(340f, 60f);
        testEndButton.GetComponent<Image>().color = new Color(1f, 0.6f, 0.6f, 1f);

        GameObject textObject = new GameObject("Text (TMP)", typeof(RectTransform));
        textObject.transform.SetParent(testEndButton.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = "TEST: End competition";
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 26f;
        text.color = Color.black;
        text.raycastTarget = false;

        testEndButton.GetComponent<Button>().onClick.AddListener(() =>
        {
            ReturnToLobby returnToLobby = FindObjectOfType<ReturnToLobby>();
            if (returnToLobby != null)
            {
                returnToLobby.GoToLobby();
            }
        });
    }

    public void HideMultiPlayerEndScreen()
    {
        endScreenRequested = false;
        multiplayerEndScreen.SetActive(false);
    }

    // The results screen is taking over: stop the clock and hide the finish texts and buttons
    public void StopForResults()
    {
        isClockRunning = false;
        StopTicking();
        endScreenRequested = false;
        if (multiplayerEndScreen != null)
        {
            multiplayerEndScreen.SetActive(false);
        }
        if (finishTime != null)
        {
            finishTime.gameObject.SetActive(false);
        }
        if (testEndButton != null)
        {
            testEndButton.SetActive(false);
        }
    }

    // I Shared Mode är värden den som är master client, och bara den kan ladda lobbyn åt alla
    private bool IsHost()
    {
        return agentMovement != null && CompetitionFlow.IsHostRunner(agentMovement.Runner);
    }

    // The clock moves in steps of 0.05 real seconds (20 game seconds per real second while running). It used to be
    // driven by InvokeRepeating, which on a slow computer (under 20 frames per second) ran fewer steps than real time,
    // so that player's clock ran slow and their time came out better than it was (2026-11-14 relay: first over the
    // finish line but second on time). Now the steps follow the real time that has passed, whatever the frame rate.
    const float TickSeconds = 0.05f;
    bool ticking;
    float tickTime;

    void StartTicking()
    {
        ticking = true;
        tickTime = 0f;
    }

    void StopTicking()
    {
        ticking = false;
    }

    private void Update()
    {
        if (ticking)
        {
            tickTime += Time.deltaTime;
            int steps = 0;
            // A long hitch (loading, a dragged window) is not made up for more than a few seconds at a time
            while (tickTime >= TickSeconds && steps < 100)
            {
                tickTime -= TickSeconds;
                steps++;
                UpdateTimer();
            }
            if (steps >= 100) tickTime = 0f;
        }

        UpdateTestEndButton();

        // Håller knappen rätt även om värdskapet går över till en annan spelare efter målgång
        if (!endScreenRequested || multiplayerEndScreen == null)
        {
            return;
        }
        bool show = IsHost() && !HideFinishTexts;
        if (multiplayerEndScreen.activeSelf != show)
        {
            multiplayerEndScreen.SetActive(show);
        }
    }
    public void setEndTimeText()
    {
        finishTime.gameObject.SetActive(true);
        PlaceFinishTimeAtTop();
        finishTime.text = "Time: " + GetTime();
        if (agentMovement != null && agentMovement.pathfindingManager != null && agentMovement.pathfindingManager.multiplayer)
        {
            finishTime.text += "\n<size=20>Waiting for the other runners to finish...</size>";
        }
    }

    // The time card after the finish sat in the middle of the screen and hid the others still racing.
    // Now it is a small card at the top edge, so the race can be followed under it.
    void PlaceFinishTimeAtTop()
    {
        RectTransform rect = finishTime.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -16f);
        rect.sizeDelta = new Vector2(460f, 96f);
        finishTime.fontSize = 40;
    }
}
