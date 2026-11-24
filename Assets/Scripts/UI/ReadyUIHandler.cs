using Fusion;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ReadyUIHandler : NetworkBehaviour
{
    [Header("UI)")]
    public TextMeshProUGUI buttonReadyText;
    public GameObject buttonReady;
    public TextMeshProUGUI countDownText;

    public MapHandler mapHandler;
    public GameModeHandler gameModeHandler;


    bool isStarting = false;

    // Knappar som bara värden ska se: de som öppnar kart- och spelläges-väljaren
    private List<GameObject> hostOnlyButtons = new List<GameObject>();
    private void Awake()
    {
        buttonReady.SetActive(false);
    }
    // Start is called before the first frame update
    void Start()
    {
        
        countDownText.text = "";
        FindHostOnlyButtons();

        
    }

    bool remaking;

    // After a race on an AI course, Start game lays a new AI course with the same kind, length and
    // number of controls (unless the host picked another course in the meantime), shows it to everyone
    // in the lobby for a moment, and then starts.
    async void RemakeAICourseThenStart()
    {
        remaking = true;
        // A card with a loading bar and the time left, as in the AI course window: laying a sprint course with
        // barriers can take up to about 50 s, and a plain message that went away after 4 s looked like nothing happened
        CourseGenerator.Progress progress = new CourseGenerator.Progress();
        UIProgressCard card = UIProgressCard.Show("Laying a new AI course with the same length and controls...");
        Task<CourseGenerator.Result> making = AICoursePanel.Remake(progress);
        while (!making.IsCompleted)
        {
            await Task.Yield();
            if (this == null) { card.Close(); return; }
            card.Set(AICoursePanel.DescribeProgress(progress, out float share), share);
        }
        CourseGenerator.Result result;
        try { result = await making; }
        catch (System.Exception e) { result = new CourseGenerator.Result { error = e.Message }; }
        card.Close();
        if (this == null) return;
        if (result.course != null) UIToast.Show("New AI course laid. Starting...", 3f, CourseUI.Forest);
        if (result.course != null)
        {
            mapHandler.Select(result.course);
            // Gives the lobby time to send the new course to the other players
            float until = Time.realtimeSinceStartup + 1.2f;
            while (Time.realtimeSinceStartup < until)
            {
                await Task.Yield();
                if (this == null) return;
            }
        }
        else
        {
            UIToast.Show("Could not lay a new AI course (" + result.error + "). Running the same course again.");
            AICoursePanel.RacedCourseId = null;
        }
        remaking = false;
        StartGame();
    }

    public void StartGame()
    {
        // Bara värden (master client i Shared Mode) får starta spelet
        if (!IsHost()) return;
        if (remaking) return;
        // No course chosen: tell the host instead of starting a race that can't be set up
        string courseProblem = CourseLibrary.StartProblem(CourseLibrary.Current);
        if (courseProblem != null)
        {
            UIToast.Show(courseProblem);
            return;
        }
        if (AICoursePanel.ShouldRemake(CourseLibrary.Current))
        {
            RemakeAICourseThenStart();
            return;
        }

        // "Your runners": every player needs a runner of their own for every leg of a relay
        string chosenMode = gameModeHandler != null && !string.IsNullOrWhiteSpace(gameModeHandler.gameMode) ? gameModeHandler.gameMode : GameModes.YourRunners;
        LobbySettingsUI lobbySettings = FindObjectOfType<LobbySettingsUI>();
        ExtraMode extraModes = lobbySettings != null ? lobbySettings.modes : ExtraMode.None;
        // Imposter and Secret task are always raced with Same stats
        if ((extraModes & ExtraModes.ImposterKinds) != 0 && !GameModes.IsSameStats(chosenMode))
        {
            chosenMode = SameStatsPopup.LastValuesMode();
            if (gameModeHandler != null) gameModeHandler.gameMode = chosenMode;
        }
        // Route analysis and the route choice game give everyone the same runner, so nobody needs runners of their own
        string tooFew = (extraModes & ExtraModes.NoRunners) != 0 ? null : GameModes.RelayNeedsMoreRunners(CourseLibrary.Current, chosenMode);
        if (tooFew != null)
        {
            UIToast.Show(tooFew);
            return;
        }
        string modeProblem = ExtraModes.CourseProblem(extraModes, CourseLibrary.Current);
        if (modeProblem != null)
        {
            UIToast.Show(modeProblem);
            return;
        }
        // The imposter modes (Imposter, Secret task) need at least 3 players
        if ((extraModes & ExtraModes.ImposterKinds) != 0 || CompetitionFlow.IsImposterMode(chosenMode))
        {
            string fewPlayers = ExtraModes.NotWithPlayers(ExtraModes.ImposterKinds, ExtraModes.PlayersInLobby());
            if (fewPlayers != null)
            {
                UIToast.Show("Imposter and Secret task need at least " + ExtraModes.MinImposterPlayers + " players. Wait for more players or choose another game mode.");
                return;
            }
        }
        if ((extraModes & ExtraMode.SecretTask) != 0)
        {
            string noTask = SecretTasks.StartProblem(CourseLibrary.Current, SecretTasks.HostOff);
            if (noTask != null)
            {
                UIToast.Show(noTask);
                return;
            }
        }
        // A course on the host's private map: everyone needs their copy of the map before the race can start
        string mapProblem = PrivateMaps.StartProblem(CourseLibrary.Current);
        if (mapProblem != null)
        {
            UIToast.Show(mapProblem);
            return;
        }

        // The lobby stays open while it races: a player who joins now watches the race (SessionRacing, Spectator)
        SessionRacing.Set(Runner, true);

        // If no game mode was selected, default to "Your runners"
        string selectedMode = GameModes.YourRunners;
        if (gameModeHandler != null && !string.IsNullOrWhiteSpace(gameModeHandler.gameMode))
        {
            selectedMode = gameModeHandler.gameMode;
        }

        // Ensure the server/state-authority broadcasts the chosen mode to all peers
        var allPlayers = FindObjectsOfType<NetworkPlayer>();
        NetworkPlayer serverPlayer = null;
        foreach (var p in allPlayers)
        {
            if (p.Object != null && p.Object.HasStateAuthority)
            {
                serverPlayer = p;
                break;
            }
        }

        if (serverPlayer != null)
        {
            // set networked field on server and broadcast via RPC
            serverPlayer.gameMode = selectedMode;
            // The extra game modes, for everyone to read when the competition starts
            serverPlayer.competitionModes = (int)extraModes | ExtraModes.KnownBit;
            // Imposter mode: one random player gets the secret job of finishing last.
            // Secret task mode: one random player is the imposter, everyone else gets the same secret task.
            bool secretTaskOn = (extraModes & ExtraMode.SecretTask) != 0;
            bool imposterOn = secretTaskOn || (extraModes & ExtraMode.Imposter) != 0 || CompetitionFlow.IsImposterMode(selectedMode);
            serverPlayer.secretTask = secretTaskOn ? SecretTasks.Pick(CourseLibrary.Current, SecretTasks.HostOff) : 0;
            serverPlayer.imposter = imposterOn
                ? CompetitionFlow.PickRandomPlayer(Runner)
                : PlayerRef.None;
            try { serverPlayer.RPC_BroadcastGameMode(selectedMode); } catch { }
        }
        else
        {
            // fallback: call locally
            if (NetworkPlayer.Local != null)
                NetworkPlayer.Local.setGameMode(selectedMode);
        }

        // Remember the AI course, so the next Start game in this lobby gets a fresh one
        if (MapSelectUI.IsAICourse(CourseLibrary.Current)) AICoursePanel.RacedCourseId = CourseLibrary.Current.id;

        // Every course is run in the same scene; CourseLoader sets it up for the course picked in the lobby
        Runner.LoadScene(CourseScenes.Competition);

    }

    // Update is called once per frame
    void Update()
    {
        // I Shared Mode har varje spelare state authority över sin egen spelare, så det går inte att använda
        // för att avgöra vem som är värd. Kollas varje bildruta så att knappen även följer med om värden byts.
        bool show = IsHost();
        if (buttonReady.activeSelf != show)
        {
            buttonReady.SetActive(show);
        }
        foreach (GameObject button in hostOnlyButtons)
        {
            if (button != null && button.activeSelf != show)
            {
                button.SetActive(show);
            }
        }
    }

    // Hittar knapparna via deras OnClick i scenen (OpenPanel på MapSelectUI respektive GameModeSelect),
    // så att scenen inte behöver kopplas om.
    private void FindHostOnlyButtons()
    {
        foreach (Button button in FindObjectsOfType<Button>(true))
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                UnityEngine.Object target = button.onClick.GetPersistentTarget(i);
                if (button.onClick.GetPersistentMethodName(i) == "OpenPanel" && (target is MapSelectUI || target is GameModeSelect))
                {
                    hostOnlyButtons.Add(button.gameObject);
                    button.gameObject.SetActive(false); // visas först när vi vet att vi är värd
                    break;
                }
            }
        }
    }

    private bool IsHost()
    {
        return Runner != null && (Runner.IsServer || Runner.IsSharedModeMasterClient);
    }

    public void LeaveLobby()
    {
        if (Runner == null)
            return;

        // Kolla om vi är host (server) och om det finns fler än 1 spelare
        if (Runner.IsServer && Runner.ActivePlayers.Count() > 1)
        {
            Debug.Log("Du kan inte lämna servern eftersom andra spelare är anslutna.");
            return; // Stoppa hosten från att lämna
        }

        // Stäng ner nätverks-sessionen och ladda MainMenu när den är stängd
        SceneLoader.LeaveSession(Runner, "MainMenu");
    }

    // "Change color": a colour picker; the colour is saved and used in every game from now on
    public void ChangePlayerOutfit()
    {
        PlayerColorPicker.Open();
    }

}
