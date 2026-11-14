using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The hover texts of the pages that are built in the scenes (not in code). They are put on the objects by name
// when a scene has loaded, so the saved scenes need no change. Pages built in code add their own with HoverTip.Add;
// the texts used on more than one page (lobby and single player) are kept here so they say the same thing.
public class UIHints : MonoBehaviour
{
    public const string RunnerPool = "Which runners you race with. 'Your runners' means only the cards you own. 'Any runner' means you pick from all runners in the game.";
    public const string GameMode = "Extra rules for the race, for example Imposter, Bears, Move a control or Route analysis. Several can be on at once. Click to read what each one does.";
    public const string Course = "The map and course the race is run on.";
    public const string ChangeColor = "Pick the colour of your runner. It is the colour you and the other players see on the map.";
    public const string ChangeCourse = "Choose which map and course to race on. You can pick a ready-made course, a community course or let the game make one.";
    public const string Money = "Your money. You earn it by finishing races and spend it on runner packs in the shop.";

    struct Hint
    {
        public string scene;
        public string path;
        public string text;
        public Hint(string scene, string path, string text) { this.scene = scene; this.path = path; this.text = text; }
    }

    static readonly Hint[] All =
    {
        new Hint("MainMenu", "Home screen/Tile compete", "Start a race: play on your own or with friends online."),
        new Hint("MainMenu", "Home screen/Tile shop", "Open runner packs to get new runner cards. The first pack is free, the next ones cost money you have earned."),
        new Hint("MainMenu", "Home screen/Tile runners", "Your collection: every runner card you own, with their speed and technique."),
        new Hint("MainMenu", "Home screen/Course builder", "Build your own courses, import new maps and share them with the other players."),
        new Hint("MainMenu", "Home screen/Quit", "Close the game."),
        new Hint("MainMenu", "Shop screen/Back", "Back to the main menu."),
        new Hint("MainMenu", "Runners screen/Back", "Back to the main menu."),

        new Hint("NewCompetition", "Tile multiplayer", "Race against other players online. You create a lobby or join one that is already open."),
        new Hint("NewCompetition", "Tile single player", "Race on your own against the clock and the computer runners."),
        new Hint("NewCompetition", "Back", "Back to the main menu."),

        new Hint("MultiPlayer", "Player details panel/Input name", "The name the other players see in the lobby and in the results."),
        new Hint("MultiPlayer", "Player details panel/FindCompetition", "Search for open lobbies that you can join."),
        new Hint("MultiPlayer", "Session List Panel/Create  New Lobby", "Start a new lobby that others can join. You become the host and choose course, runners and game modes."),
        new Hint("MultiPlayer", "Create Game Panel/Session name", "The name of your lobby as it shows in the lobby list."),
        new Hint("MultiPlayer", "Create Game Panel/Create session", "Create the lobby and wait for the others to join."),
        new Hint("MultiPlayer", "Create Game Panel/Return", "Go back without creating a lobby."),

        new Hint("Lobby", "Game setup card/Game mode", RunnerPool + " The host decides."),
        new Hint("Lobby", "Game setup card/Imposter", "The game modes the host has switched on for this race. Click 'See game modes' on the left to read what each one means."),
        new Hint("Lobby", "Game setup card/Map", Course + " The host decides."),
        new Hint("Lobby", "Player setup panel/InnerPanel/Change player outfit", ChangeColor),
        new Hint("Lobby", "Player setup panel/InnerPanel/Change Map", "The host picks the map and course. The others can open this to see what has been picked."),
        new Hint("Lobby", "Player setup panel/InnerPanel/Change GameMode", "The host chooses whether everyone races with their own runners or any runner. The others can look at the choice."),
        new Hint("Lobby", "Player setup panel/InnerPanel/Imposter toggle", "The host picks the game modes. The others can open it to see which ones are on and what they mean."),
        new Hint("Lobby", "StartGame", "Start the race for everyone in the lobby. Only the host can start it."),
        new Hint("Lobby", "LeaveLobby", "Leave this lobby and go back."),
        new Hint("Lobby", "Players panel/Header row", "Everyone who is in the lobby right now."),
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Start()
    {
        GameObject go = new GameObject("UI hints");
        DontDestroyOnLoad(go);
        UIHints hints = go.AddComponent<UIHints>();
        SceneManager.sceneLoaded += hints.OnSceneLoaded;
        hints.OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartCoroutine(Apply(scene));
    }

    // Some pages are rebuilt a moment after the scene has loaded, so the texts are put on a few times
    IEnumerator Apply(Scene scene)
    {
        foreach (float wait in new[] { 0f, 0.5f, 1.5f, 4f })
        {
            if (wait > 0f) yield return new WaitForSecondsRealtime(wait);
            if (!scene.IsValid() || !scene.isLoaded) yield break;
            ApplyTo(scene);
        }
    }

    static void ApplyTo(Scene scene)
    {
        List<Hint> mine = null;
        foreach (Hint hint in All)
        {
            if (hint.scene != scene.name) continue;
            if (mine == null) mine = new List<Hint>();
            mine.Add(hint);
        }
        if (mine == null) return;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!(t is RectTransform)) continue;
                string path = null;
                foreach (Hint hint in mine)
                {
                    if (!t.name.EndsWith(LastPart(hint.path))) continue;
                    if (path == null) path = PathOf(t);
                    if (path == hint.path || path.EndsWith("/" + hint.path)) HoverTip.Add(t, hint.text);
                }
            }
        }
    }

    static string LastPart(string path)
    {
        int i = path.LastIndexOf('/');
        return i < 0 ? path : path.Substring(i + 1);
    }

    static string PathOf(Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }
}
