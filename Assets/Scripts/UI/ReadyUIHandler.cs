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
        UIToast.Show("Laying a new AI course with the same length and controls...", 4f);
        CourseGenerator.Result result = await AICoursePanel.Remake();
        if (this == null) return;
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
        if (AICoursePanel.ShouldRemake(CourseLibrary.Current))
        {
            RemakeAICourseThenStart();
            return;
        }

        // "Your runners": every player needs a runner of their own for every leg of a relay
        string chosenMode = gameModeHandler != null && !string.IsNullOrWhiteSpace(gameModeHandler.gameMode) ? gameModeHandler.gameMode : GameModes.YourRunners;
        string tooFew = GameModes.RelayNeedsMoreRunners(CourseLibrary.Current, chosenMode);
        if (tooFew != null)
        {
            UIToast.Show(tooFew);
            return;
        }

        Runner.SessionInfo.IsOpen = false;

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
            // Imposter mode: one random player gets the secret job of finishing last
            LobbySettingsUI lobbySettings = FindObjectOfType<LobbySettingsUI>();
            bool imposterOn = (lobbySettings != null && lobbySettings.imposterEnabled) || CompetitionFlow.IsImposterMode(selectedMode);
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
