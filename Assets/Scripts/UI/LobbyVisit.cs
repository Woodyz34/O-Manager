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
    static readonly List<Behaviour> offBehaviours = new List<Behaviour>();
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

        Image card = CourseUI.CardPanel("While you wait", go.transform);
        CourseUI.Place(card.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(48, 48), new Vector2(420, 292));
        CourseUI.Column(card.gameObject, 10, new RectOffset(26, 26, 20, 24));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "While you wait", 30, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 44);
        Button shop = CourseUI.Button("Pack shop", card.transform, "Pack shop", CourseUI.ButtonKind.Light, () => Open(Place.Shop), 24);
        CourseUI.Size(shop, 60);
        Button runners = CourseUI.Button("My Runners", card.transform, "My Runners", CourseUI.ButtonKind.Light, () => Open(Place.Runners), 24);
        CourseUI.Size(runners, 60);
        Button builder = CourseUI.Button("Course builder", card.transform, "Course builder", CourseUI.ButtonKind.Light, () => Open(Place.CourseBuilder), 24);
        CourseUI.Size(builder, 60);
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

    static void HideAll<T>(GameObject root) where T : Behaviour
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
        foreach (Behaviour b in offBehaviours)
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
