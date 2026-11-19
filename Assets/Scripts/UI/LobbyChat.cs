using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// A chat in the multiplayer lobby (2026-11-19, test night). A speech-bubble button in the bottom left corner opens a
// small chat window; a red dot on the button tells that someone wrote while the window was closed. The messages go
// to everyone in the session through NetworkPlayer.RPC_Chat and are kept until the player leaves the session, so the
// chat is still there when the players come back to the lobby after a race.
public class LobbyChat : MonoBehaviour
{
    public const int MaxLength = 150;   // short, so a message always fits one network message
    const int MaxMessages = 60;

    struct Line
    {
        public string name;
        public Color color;
        public string text;
    }

    static readonly List<Line> lines = new List<Line>();
    static LobbyChat instance;
    static bool unread;

    GameObject root;
    GameObject window;
    Image dot;
    TextMeshProUGUI log;
    ScrollRect scroll;
    TMP_InputField input;
    bool dirty = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        GameObject go = new GameObject("Lobby chat");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<LobbyChat>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        lines.Clear();
        unread = false;
        instance = null;
    }

    // A message from a player (NetworkPlayer.RPC_Chat, on every computer)
    public static void Receive(NetworkPlayer from, string text)
    {
        text = Clean(text);
        if (string.IsNullOrEmpty(text)) return;
        string name = from != null ? from.nickName.ToString() : "";
        if (string.IsNullOrEmpty(name)) name = "Player";
        Color color = CourseUI.Forest;
        if (from != null && from.playerColor > 0) color = PlayerColor.Unpack(from.playerColor);
        lines.Add(new Line { name = name, color = color, text = text });
        if (lines.Count > MaxMessages) lines.RemoveAt(0);
        bool mine = from != null && from == NetworkPlayer.Local;
        if (instance != null)
        {
            instance.dirty = true;
            if (!mine && (instance.window == null || !instance.window.activeInHierarchy)) unread = true;
        }
    }

    static string Clean(string text)
    {
        if (text == null) return "";
        text = text.Replace("\n", " ").Replace("\r", " ").Trim();
        if (text.Length > MaxLength) text = text.Substring(0, MaxLength);
        // No rich text from others: < and > are shown as they are
        return text.Replace("<", "‹").Replace(">", "›");
    }

    void Start()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // A new game starts from the main menu: the old lobby's chat goes
        if (mode == LoadSceneMode.Single && scene.name == "MainMenu")
        {
            lines.Clear();
            unread = false;
            dirty = true;
        }
    }

    bool ShouldShow()
    {
        if (NetworkPlayer.Local == null) return false;
        if (SceneManager.GetActiveScene().name != "Lobby") return false;
        if (LobbyVisit.Active || EscMenu.IsOpen) return false;
        return true;
    }

    void Update()
    {
        bool show = ShouldShow();
        if (show && root == null) Build();
        if (root == null) return;
        if (root.activeSelf != show) root.SetActive(show);
        if (!show) return;

        if (dot != null && dot.gameObject.activeSelf != unread) dot.gameObject.SetActive(unread);
        if (window.activeSelf)
        {
            unread = false;
            if (dirty) Refresh();
            // Enter sends, also when the field lost focus after the last message
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (!string.IsNullOrWhiteSpace(input.text)) Send();
                else Focus();
            }
        }
    }

    void Build()
    {
        root = new GameObject("Lobby chat canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        try { root.tag = "Canvas"; } catch (UnityException) { }   // clicks here do not reach the map under it
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;   // above the lobby, under pop-ups (200+) and the Esc menu
        ScreenSettings.Fit(root.GetComponent<CanvasScaler>());
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            events.transform.SetParent(transform, false);
        }

        // The button in the bottom left corner
        Button open = CourseUI.Button("Chat button", root.transform, "Chat", CourseUI.ButtonKind.Secondary, Toggle, 24);
        CourseUI.Place((RectTransform)open.transform, Vector2.zero, Vector2.zero, new Vector2(24, 24), new Vector2(110, 56));
        HoverTip.Add(open, "Chat with the others in the lobby.");
        dot = CourseUI.Panel("Unread", open.transform, CourseUI.Danger);
        CourseUI.Place(dot.rectTransform, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-6, -6), new Vector2(20, 20));
        dot.raycastTarget = false;
        dot.gameObject.SetActive(false);

        // The window above it
        Image card = CourseUI.CardPanel("Chat window", root.transform);
        window = card.gameObject;
        CourseUI.Place(card.rectTransform, Vector2.zero, Vector2.zero, new Vector2(24, 92), new Vector2(480, 420));

        TextMeshProUGUI heading = CourseUI.Text("Heading", card.transform, "Chat", 28, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Place(heading.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -12), new Vector2(300, 40));
        Button close = CourseUI.Button("Close", card.transform, "X", CourseUI.ButtonKind.Light, Toggle, 22);
        CourseUI.Place((RectTransform)close.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -10), new Vector2(44, 44));

        RectTransform content = CourseUI.ScrollList("Messages", card.transform, out scroll);
        RectTransform viewport = (RectTransform)scroll.transform;
        CourseUI.Stretch(viewport, 16, 16, 60, 76);
        CourseUI.Column(content.gameObject, 0, new RectOffset(4, 4, 4, 4));
        log = CourseUI.Text("Log", content, "", 22, CourseUI.Ink);
        log.richText = true;
        log.overflowMode = TextOverflowModes.Overflow;
        log.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        input = CourseUI.Input("Message", card.transform, "Write a message...", MaxLength, null);
        CourseUI.Place((RectTransform)input.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(16, 14), new Vector2(340, 52));
        Button send = CourseUI.Button("Send", card.transform, "Send", CourseUI.ButtonKind.Primary, Send, 22);
        CourseUI.Place((RectTransform)send.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 14), new Vector2(100, 52));

        window.SetActive(false);
        dirty = true;
    }

    void Toggle()
    {
        if (window == null) return;
        window.SetActive(!window.activeSelf);
        if (window.activeSelf)
        {
            unread = false;
            dirty = true;
            Focus();
        }
    }

    void Focus()
    {
        if (input == null) return;
        input.ActivateInputField();
        input.Select();
    }

    void Send()
    {
        NetworkPlayer local = NetworkPlayer.Local;
        string text = Clean(input.text);
        input.text = "";
        Focus();
        if (local == null || string.IsNullOrEmpty(text) || local.Object == null || !local.Object.IsValid) return;
        local.RPC_Chat(text);
    }

    void Refresh()
    {
        dirty = false;
        StringBuilder sb = new StringBuilder();
        if (lines.Count == 0) sb.Append("<color=#5B7563><i>No messages yet. Say hi!</i></color>");
        foreach (Line line in lines)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("<b><color=#").Append(ColorUtility.ToHtmlStringRGB(Darker(line.color))).Append('>')
              .Append(line.name).Append(":</color></b> ").Append(line.text);
        }
        log.text = sb.ToString();
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 0f;   // the newest at the bottom, in view
    }

    // Player colours can be light; names are drawn a bit darker so they can be read on the cream card
    static Color Darker(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        return Color.HSVToRGB(h, Mathf.Max(s, 0.45f), Mathf.Min(v, 0.6f));
    }
}
