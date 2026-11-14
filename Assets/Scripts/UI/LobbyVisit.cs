using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// From the multiplayer lobby you can pop into the pack shop, My Runners and the course builder without
// leaving the lobby. The scene for it (MainMenu or CourseBuilder) is loaded on top of the lobby, and the
// lobby's cameras, canvases, sounds and pictures are switched off while you are away and on again when you
// come back. The network session keeps running all the time, so the others still see you in the lobby.
// If the host starts the race while you are away, the race scene replaces everything as usual.
//
// Other code that listens to SceneManager.sceneLoaded ignores additive loads, so a visit is not taken for a
// new race.
public static class LobbyVisit
{
    public enum Place { Shop, Runners, CourseBuilder }

    const string LobbyScene = "Lobby";
    const string MenuScene = "MainMenu";

    // True from the moment a visit starts loading until you are back in the lobby
    public static bool Active { get; private set; }
    public static Place Current { get; private set; }

    static Scene lobby;
    static string visitScene;
    static bool loading;
    static readonly List<UnityEngine.Behaviour> offBehaviours = new List<UnityEngine.Behaviour>();
    static readonly List<Renderer> offRenderers = new List<Renderer>();
    static readonly HashSet<GameObject> lobbyRoots = new HashSet<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        Forget();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // A whole new scene (the race starting, or leaving the lobby) replaces the lobby and the visit with it
        if (mode == LoadSceneMode.Single) Forget();
    }

    static void Forget()
    {
        Active = false;
        loading = false;
        visitScene = null;
        offBehaviours.Clear();
        offRenderers.Clear();
        lobbyRoots.Clear();
    }

    // ---- The buttons in the lobby ----

    // A small card under "Runner setup" with the three ways out. Built in code so the lobby scene needs no rebuild.
    public static void AddButtons()
    {
        GameObject go = new GameObject("Lobby extras", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;   // above the lobby, under its pop-ups
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        go.AddComponent<LobbyVisitCard>();

        Image card = CourseUI.CardPanel("While you wait", go.transform);
        CourseUI.Place(card.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(48, 48), new Vector2(420, 292));
        CourseUI.Column(card.gameObject, 10, new RectOffset(26, 26, 20, 24));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "While you wait", 30, CourseUI.Ink, FontStyles.Bold);
        title.enableWordWrapping = false;
        CourseUI.Size(title, 44);
        MoveCornersAround();
        Button shop = CourseUI.Button("Pack shop", card.transform, "Pack shop", CourseUI.ButtonKind.Light, () => Open(Place.Shop), 24);
        CourseUI.Size(shop, 60);
        Button runners = CourseUI.Button("My Runners", card.transform, "My Runners", CourseUI.ButtonKind.Light, () => Open(Place.Runners), 24);
        CourseUI.Size(runners, 60);
        Button builder = CourseUI.Button("Course builder", card.transform, "Course builder", CourseUI.ButtonKind.Light, () => Open(Place.CourseBuilder), 24);
        CourseUI.Size(builder, 60);
        HoverTip.Add(shop, "Open runner packs while you wait. You come straight back to the lobby when you leave.");
        HoverTip.Add(runners, "Look at the runner cards you own. You come straight back to the lobby when you leave.");
        HoverTip.Add(builder, "Build your own courses or import maps while you wait. You come straight back to the lobby when you leave.");
    }

    // Money always sits in the top right corner, as in the main menu, so "Leave lobby" moves to the top left
    static void MoveCornersAround()
    {
        foreach (MoneyLabel label in UnityEngine.Object.FindObjectsByType<MoneyLabel>(FindObjectsSortMode.None))
        {
            if (label.gameObject.name != "Money pill" || label.gameObject.scene.name != LobbyScene) continue;
            RectTransform pill = (RectTransform)label.transform;
            pill.anchorMin = pill.anchorMax = new Vector2(1f, 1f);
            pill.pivot = new Vector2(1f, 1f);
            pill.anchoredPosition = new Vector2(-48f, -48f);
        }
        foreach (Button b in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            if (b.gameObject.name != "LeaveLobby" || b.gameObject.scene.name != LobbyScene) continue;
            RectTransform r = (RectTransform)b.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(48f, -48f);
        }
    }

    // ---- Going and coming back ----

    public static void Open(Place place)
    {
        if (Active || loading) return;
        Scene active = SceneManager.GetActiveScene();
        if (active.name != LobbyScene) return;

        lobby = active;
        Current = place;
        visitScene = place == Place.CourseBuilder ? CourseScenes.Builder : MenuScene;
        Active = true;
        loading = true;

        lobbyRoots.Clear();
        foreach (GameObject root in lobby.GetRootGameObjects()) lobbyRoots.Add(root);
        // Switched off before loading, so the visit's own camera is the only one (Camera.main) when it starts
        HideLobby();

        string message = place == Place.CourseBuilder ? "Opening the course builder"
                       : place == Place.Shop ? "Opening the pack shop" : "Opening My Runners";
        SceneLoader.LoadAdditive(visitScene, message, scene =>
        {
            loading = false;
            if (!Active) return;   // the race started while loading
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogWarning("Could not open " + visitScene + " from the lobby");
                Close();
            }
        });
    }

    // Back to the lobby: the visit's scene goes away and the lobby is shown again
    public static void Close()
    {
        if (!Active || loading) return;
        Active = false;

        // Things the visit made at runtime (the course builder's map, pop-ups...) ended up in the lobby scene,
        // because that is the active scene; they go too. Network objects are left alone.
        if (lobby.IsValid() && lobby.isLoaded)
        {
            foreach (GameObject root in lobby.GetRootGameObjects())
            {
                if (lobbyRoots.Contains(root)) continue;
                if (root.GetComponentInChildren<NetworkObject>(true) != null || root.GetComponentInChildren<NetworkRunner>(true) != null) continue;
                UnityEngine.Object.Destroy(root);
            }
        }
        Scene visit = SceneManager.GetSceneByName(visitScene);
        if (visit.IsValid() && visit.isLoaded) SceneManager.UnloadSceneAsync(visit);

        ShowLobby();
        lobbyRoots.Clear();
        visitScene = null;
    }

    static void HideLobby()
    {
        offBehaviours.Clear();
        offRenderers.Clear();
        foreach (GameObject root in lobby.GetRootGameObjects())
        {
            Hide(root);
        }
        // The players' runner dots and name tags live outside the lobby scene (they follow you into the race)
        foreach (NetworkPlayer player in UnityEngine.Object.FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            foreach (Renderer r in player.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                r.enabled = false;
                offRenderers.Add(r);
            }
            foreach (Canvas c in player.GetComponentsInChildren<Canvas>())
            {
                if (!c.enabled) continue;
                c.enabled = false;
                offBehaviours.Add(c);
            }
        }
    }

    static void Hide(GameObject root)
    {
        HideAll<Camera>(root);
        HideAll<AudioListener>(root);
        HideAll<BaseInputModule>(root);
        HideAll<EventSystem>(root);
        HideAll<Canvas>(root);
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            r.enabled = false;
            offRenderers.Add(r);
        }
    }

    static void HideAll<T>(GameObject root) where T : UnityEngine.Behaviour
    {
        foreach (T b in root.GetComponentsInChildren<T>())
        {
            if (!b.enabled) continue;
            b.enabled = false;
            offBehaviours.Add(b);
        }
    }

    static void ShowLobby()
    {
        foreach (UnityEngine.Behaviour b in offBehaviours)
        {
            if (b != null) b.enabled = true;
        }
        foreach (Renderer r in offRenderers)
        {
            if (r != null) r.enabled = true;
        }
        offBehaviours.Clear();
        offRenderers.Clear();
    }
}

// Hides the "While you wait" card while something else is open in the lobby (choose course, AI course,
// runner pool, game mode, colour picker...), so it stays in the background and never covers a pop-up.
public class LobbyVisitCard : MonoBehaviour
{
    Canvas canvas;
    MapSelectUI maps;
    GameModeSelect pools;
    float nextCheck;

    void Start()
    {
        canvas = GetComponent<Canvas>();
        maps = FindObjectOfType<MapSelectUI>(true);
        pools = FindObjectOfType<GameModeSelect>(true);
    }

    void Update()
    {
        if (Time.unscaledTime < nextCheck || canvas == null) return;
        nextCheck = Time.unscaledTime + 0.1f;
        // While visiting the shop or the course builder LobbyVisit has switched the canvas off; leave it alone
        if (LobbyVisit.Active) return;
        bool show = !SomethingOpen();
        if (canvas.enabled != show) canvas.enabled = show;
    }

    bool SomethingOpen()
    {
        if (maps != null && maps.mapSelectPanel != null && maps.mapSelectPanel.activeInHierarchy) return true;
        if (pools != null && pools.gameModeSelectPanel != null && pools.gameModeSelectPanel.activeInHierarchy) return true;
        if (FindObjectOfType<AICoursePanel>() != null) return true;
        // Pop-ups made in code have their own canvas above this one (the short toast messages do not count)
        foreach (Canvas other in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (other == canvas || !other.isRootCanvas || !other.isActiveAndEnabled) continue;
            if (other.renderMode != RenderMode.ScreenSpaceOverlay || other.sortingOrder <= canvas.sortingOrder) continue;
            if (other.name == "Toast" || other.name == "Esc menu") continue;
            if (other.GetComponent<LoadingScreenUI>() != null) continue;
            if (other.GetComponent<HoverTip>() != null) continue;   // the hover text's canvas is always there
            // Canvases that live for the whole game (the updater's version label, the F3 FPS meter...) are not
            // lobby pop-ups. The updater's canvas (2026-11-14) was always there and hid this card for good.
            if (other.gameObject.scene.name == "DontDestroyOnLoad") continue;
            return true;
        }
        return false;
    }
}
