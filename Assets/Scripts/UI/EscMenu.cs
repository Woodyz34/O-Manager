using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The menu that opens with Esc on every page: resume, volume, back to the main menu and quit the game.
// The host of a running competition can also end it for everyone and take them back to the lobby.
// Online the game keeps running behind the menu (other players are still racing). Alone (single player and the
// weekly/daily contests) the race stands still while the menu is open (GamePause, 2026-11-24).
// "Key bindings" opens a second page with every key that can be changed, one group per game mode.
public class EscMenu : MonoBehaviour
{
    const string VolumeKey = "Settings.MasterVolume";

    // Pages that are menus, not a competition
    static readonly string[] MenuScenes = { "MainMenu", "MultiPlayer", "SingelPlayer", "NewCompetition", "Lobby", CourseScenes.Builder };

    public static bool IsOpen => instance != null && instance.root != null && instance.root.activeSelf;

    static EscMenu instance;

    GameObject root;
    TextMeshProUGUI subtitle;
    TextMeshProUGUI volumeValue;
    Slider volumeSlider;
    Button fullscreenButton;
    Button statsButton;
    Button windowButton;
    Button endButton;
    Button mainMenuButton;
    TextMeshProUGUI hostNote;
    Button settingsButton;
    float endConfirmUntil = -1f;

    // The key bindings page
    GameObject mainCard;
    GameObject keysCard;
    // 2026-11-19, the user: one "Settings" button; it leads to Lobby settings or Your settings
    GameObject settingsCard;   // the choice
    GameObject personalCard;   // volume, display, statistics, key bindings
    readonly System.Collections.Generic.List<KeyRow> keyRows = new System.Collections.Generic.List<KeyRow>();
    KeyRow rebinding; // the row waiting for a new key, or null

    // One changeable key: what it does, the key now and how to change it
    class KeyRow
    {
        public string label;
        public Color? colour;
        public System.Func<KeyCode> get;
        public System.Action<KeyCode> set;
        public bool allowMouse; // a mouse button other than the left one may be chosen too
        public Button button;
    }

    // The groups on the Key bindings page. A new game mode with its own keys gets a group here.
    static System.Collections.Generic.List<(string title, System.Collections.Generic.List<KeyRow> rows)> KeyGroups()
    {
        var groups = new System.Collections.Generic.List<(string, System.Collections.Generic.List<KeyRow>)>();
        var routeChoice = new System.Collections.Generic.List<KeyRow>();
        foreach (int colour in RouteChoiceGame.LeftToRight)
        {
            int c = colour;
            routeChoice.Add(new KeyRow
            {
                label = RouteChoiceGame.ColourNames[c] + " route",
                colour = RouteChoiceGame.Colours[c],
                get = () => RouteChoiceGame.Key(c),
                set = key => RouteChoiceGame.SetKey(c, key),
            });
        }
        for (int which = 0; which < 2; which++)
        {
            int w = which;
            routeChoice.Add(new KeyRow
            {
                label = which == 0 ? "Hide routes (hold)" : "Hide routes, other key",
                get = () => RouteChoiceGame.HideKey(w),
                set = key => RouteChoiceGame.SetHideKey(w, key),
                allowMouse = true,
            });
        }
        groups.Add(("Route choice game", routeChoice));
        return groups;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Create()
    {
        AudioListener.volume = SavedVolume();
        if (instance != null)
        {
            return;
        }
        GameObject go = new GameObject("Esc menu");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<EscMenu>();
    }

    static float SavedVolume()
    {
        return Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        ScreenSettings.Changed += ShowDisplayMode;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        ScreenSettings.Changed -= ShowDisplayMode;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Close();
    }

    void Update()
    {
        // Alone in a race the game stands still while this menu, or the 3D settings opened from it, is open
        // (2026-11-24, the user)
        GamePause.Check(IsOpen || RunSettings.IsOpen);
        if (IsOpen && rebinding != null)
        {
            ListenForKey();
        }
        if (!Input.GetKeyDown(KeyCode.Escape))
        {
            return;
        }
        // Escape skips the intro film; the menu is not opened on top of it
        if (ActiveScene.Name == "Intro")
        {
            return;
        }
        // Escape closes the full-screen TV view first
        if (!IsOpen && TvViewer.CloseOnEscape())
        {
            return;
        }
        if (IsOpen)
        {
            // Escape first stops waiting for a new key, then leaves the key bindings page, then closes the menu
            if (rebinding != null)
            {
                rebinding = null;
                ShowKeys();
            }
            else if (keysCard != null && keysCard.activeSelf)
            {
                ShowKeysPage(false);
            }
            else if (personalCard != null && personalCard.activeSelf)
            {
                ShowPage(settingsCard);
            }
            else if (settingsCard != null && settingsCard.activeSelf)
            {
                ShowPage(mainCard);
            }
            else
            {
                Close();
            }
            return;
        }
        // Esc skips the pack opening show, so leave it to that while it plays
        if (FindAnyObjectByType<PackOpening>() != null)
        {
            return;
        }
        Open();
    }

    // Also kept up to date while open: the host can change, or the competition can end behind the menu
    void LateUpdate()
    {
        if (IsOpen)
        {
            Refresh();
        }
    }

    public void Open()
    {
        if (root == null)
        {
            Build();
        }
        endConfirmUntil = -1f;
        rebinding = null;
        ShowPage(mainCard);
        volumeSlider.SetValueWithoutNotify(Mathf.Round(AudioListener.volume * 100f));
        ShowVolume(volumeSlider.value);
        root.SetActive(true);
        Cursor.visible = true;
        ShowDisplayMode();
        Refresh();
    }

    public void Close()
    {
        rebinding = null;
        if (root != null)
        {
            root.SetActive(false);
        }
    }

    // ---- What the menu shows ----

    static NetworkRunner ActiveRunner()
    {
        foreach (NetworkRunner runner in FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None))
        {
            if (runner != null && runner.IsRunning)
            {
                return runner;
            }
        }
        return null;
    }

    static bool InCompetition(NetworkRunner runner)
    {
        if (runner == null || NetworkPlayer.Local == null)
        {
            return false;
        }
        string scene = ActiveScene.Name;
        foreach (string menu in MenuScenes)
        {
            if (scene == menu)
            {
                return false;
            }
        }
        return true;
    }

    void Refresh()
    {
        NetworkRunner runner = ActiveRunner();
        bool inCompetition = InCompetition(runner);
        bool single = runner != null && runner.GameMode == GameMode.Single;
        bool host = inCompetition && CompetitionFlow.IsHostRunner(runner);

        endButton.gameObject.SetActive(host);
        // Lobby settings only in a multiplayer game (2026-11-19, Anders). The 3D settings are always under Settings
        // (2026-11-20, Anders: not on the single player page)
        if (settingsButton != null)
            settingsButton.gameObject.SetActive(runner != null && ActiveScene.Name != "MainMenu" && !single);
        if (host)
        {
            bool confirming = Time.unscaledTime < endConfirmUntil;
            string label = single ? "End the race" : "End competition for everyone";
            CourseUI.SetButtonText(endButton, confirming ? "Sure? Click again" : label);
        }
        hostNote.gameObject.SetActive(inCompetition && !host);

        mainMenuButton.gameObject.SetActive(ActiveScene.Name != "MainMenu");

        if (inCompetition && !single)
        {
            subtitle.text = "The race keeps running while the menu is open";
        }
        else if (runner != null && !single && ActiveScene.Name == "Lobby")
        {
            subtitle.text = "Going to the main menu leaves the lobby";
        }
        else
        {
            subtitle.text = "";
        }
        subtitle.gameObject.SetActive(subtitle.text.Length > 0);
    }

    void ShowVolume(float percent)
    {
        volumeValue.text = Mathf.RoundToInt(percent) + "%";
    }

    // The chosen display mode is the orange one
    void ShowDisplayMode()
    {
        if (fullscreenButton == null)
        {
            return;
        }
        bool full = ScreenSettings.Fullscreen;
        CourseUI.SetButtonColor(fullscreenButton, full ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        CourseUI.SetButtonColor(windowButton, full ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Primary);
    }

    // ---- Buttons ----

    void ShowStatsButton()
    {
        TextMeshProUGUI label = statsButton != null ? statsButton.GetComponentInChildren<TextMeshProUGUI>() : null;
        if (label != null)
        {
            label.text = "Usage statistics: " + (GameAnalytics.Enabled ? "on" : "off");
        }
    }

    void OnStatsToggle()
    {
        GameAnalytics.SetEnabled(!GameAnalytics.Enabled);
        ShowStatsButton();
    }

    void OnFullscreen()
    {
        ScreenSettings.SetFullscreen(true);
        ShowDisplayMode();
    }

    void OnWindow()
    {
        ScreenSettings.SetFullscreen(false);
        ShowDisplayMode();
    }

    void OnVolumeChanged(float percent)
    {
        float volume = Mathf.Clamp01(percent / 100f);
        AudioListener.volume = volume;
        PlayerPrefs.SetFloat(VolumeKey, volume);
        PlayerPrefs.Save();
        ShowVolume(percent);
    }

    void OnEndCompetition()
    {
        NetworkRunner runner = ActiveRunner();
        if (!InCompetition(runner) || !CompetitionFlow.IsHostRunner(runner))
        {
            return;
        }
        // Ending it for everyone is a big step, so the button asks once more
        if (Time.unscaledTime >= endConfirmUntil)
        {
            endConfirmUntil = Time.unscaledTime + 5f;
            return;
        }
        Close();
        // Takes every player back to the lobby (single player: back to the single-player page)
        ReturnToLobby.LoadLobby(runner);
    }

    void OnMainMenu()
    {
        Close();
        NetworkRunner runner = ActiveRunner();
        if (runner != null)
        {
            // Leaves the session; in a shared session another player becomes host
            SceneLoader.LeaveSession(runner, "MainMenu");
            return;
        }
        // No network game (course builder, single-player menu): show the loading page on the way
        SceneLoader.Load("MainMenu", "Back to the menu");
    }

    void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---- Key bindings ----

    void ShowKeysPage(bool show)
    {
        if (mainCard == null)
        {
            return;
        }
        rebinding = null;
        ShowPage(show ? keysCard : personalCard);
        if (show)
        {
            ShowKeys();
        }
    }

    void ShowKeys()
    {
        foreach (KeyRow row in keyRows)
        {
            CourseUI.SetButtonText(row.button, row == rebinding ? "Press a key..." : RouteChoiceGame.KeyName(row.get()));
            CourseUI.SetButtonColor(row.button, row == rebinding ? CourseUI.ButtonKind.Primary : CourseUI.ButtonKind.Light);
        }
    }

    void StartRebind(KeyRow row)
    {
        // A clicked button stays selected, and Enter or Space would press it again
        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }
        rebinding = row;
        ShowKeys();
    }

    // Waiting for the new key: the first key pressed is it (Escape keeps the old one, see Update)
    void ListenForKey()
    {
        if (!Input.anyKeyDown || Input.GetKeyDown(KeyCode.Escape))
        {
            return;
        }
        foreach (KeyCode key in System.Enum.GetValues(typeof(KeyCode)))
        {
            // No joystick buttons; the left mouse button clicks the menu, other mouse buttons only where allowed
            bool mouse = key >= KeyCode.Mouse1 && key <= KeyCode.Mouse6 && rebinding.allowMouse;
            if (key == KeyCode.None || key == KeyCode.Escape || (key >= KeyCode.Mouse0 && !mouse))
            {
                continue;
            }
            if (!Input.GetKeyDown(key))
            {
                continue;
            }
            rebinding.set(key);
            rebinding = null;
            ShowKeys();
            return;
        }
    }

    void ShowPage(GameObject page)
    {
        if (page == null) return;
        foreach (GameObject card in new[] { mainCard, keysCard, settingsCard, personalCard })
        {
            if (card != null) card.SetActive(card == page);
        }
    }

    static Image NewCard(string name, Transform parent, string heading)
    {
        Image card = CourseUI.CardPanel(name, parent);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 0));
        CourseUI.Column(card.gameObject, 18, new RectOffset(48, 48, 40, 40));
        ContentSizeFitter fitter = card.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, heading, 44, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Size(title, 56);
        return card;
    }

    void BuildKeysCard()
    {
        Image card = CourseUI.CardPanel("Key bindings", root.transform);
        keysCard = card.gameObject;
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 0));
        CourseUI.Column(card.gameObject, 12, new RectOffset(48, 48, 40, 40));
        ContentSizeFitter fitter = card.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Key bindings", 44, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Size(title, 56);
        TextMeshProUGUI help = CourseUI.Text("Help", card.transform, "Click a key, then press the new one. Esc keeps the old key.", 20, CourseUI.InkMuted, FontStyles.Italic, TextAlignmentOptions.Center);
        CourseUI.Size(help, 28);

        keyRows.Clear();
        foreach (var group in KeyGroups())
        {
            TextMeshProUGUI heading = CourseUI.Text("Group " + group.title, card.transform, group.title, 28, CourseUI.Ink, FontStyles.Bold);
            CourseUI.Size(heading, 48);
            foreach (KeyRow row in group.rows)
            {
                KeyRow r = row;
                RectTransform line = CourseUI.Rect("Key " + row.label, card.transform);
                CourseUI.Row(line.gameObject, 14, new RectOffset(4, 0, 0, 0)).childAlignment = TextAnchor.MiddleLeft;
                CourseUI.Size(line, 52);
                if (row.colour.HasValue)
                {
                    // The colour swatch sits in a holder, so the row's layout does not stretch it to full height
                    RectTransform holder = CourseUI.Rect("Swatch holder", line);
                    CourseUI.Size(holder, -1, 44);
                    Image swatch = CourseUI.Panel("Colour", holder, row.colour.Value);
                    CourseUI.Place(swatch.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44, 20));
                }
                TextMeshProUGUI name = CourseUI.Text("Name", line, row.label, 24, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
                CourseUI.Size(name, -1, -1, 1);
                row.button = CourseUI.Button("Key", line, "", CourseUI.ButtonKind.Light, () => StartRebind(r), 22);
                CourseUI.Size(row.button, -1, 200);
                keyRows.Add(row);
            }
        }

        RectTransform gap = CourseUI.Rect("Gap", card.transform);
        CourseUI.Size(gap, 8);
        Button back = CourseUI.Button("Back", card.transform, "Back", CourseUI.ButtonKind.Primary, () => ShowKeysPage(false), 26);
        CourseUI.Size(back, 64);

        keysCard.SetActive(false);
    }

    // ---- Building the UI ----

    void Build()
    {
        root = new GameObject("Esc menu canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250; // above every page, below the short messages at the bottom
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        ScreenSettings.Fit(scaler);

        // A dark veil that also catches clicks, so nothing behind the menu is pressed. Clicking it closes the menu.
        Image veil = CourseUI.Panel("Veil", root.transform, CourseUI.Hex("10241A", 0.6f), false);
        CourseUI.Stretch(veil.rectTransform);
        Button veilButton = veil.gameObject.AddComponent<Button>();
        veilButton.transition = Selectable.Transition.None;
        veilButton.onClick.AddListener(Close);

        Image card = CourseUI.CardPanel("Card", root.transform);
        mainCard = card.gameObject;
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 0));
        CourseUI.Column(card.gameObject, 18, new RectOffset(48, 48, 40, 40));
        ContentSizeFitter fitter = card.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Menu", 48, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Size(title, 60);

        subtitle = CourseUI.Text("Subtitle", card.transform, "", 22, CourseUI.InkMuted, FontStyles.Italic, TextAlignmentOptions.Center);
        CourseUI.Size(subtitle, 30);

        Button resume = CourseUI.Button("Resume", card.transform, "Resume", CourseUI.ButtonKind.Primary, Close, 28);
        CourseUI.Size(resume, 68);

        Button settingsMenu = CourseUI.Button("Settings", card.transform, "Settings", CourseUI.ButtonKind.Light, () => ShowPage(settingsCard), 26);
        CourseUI.Size(settingsMenu, 68);
        HoverTip.Add(settingsMenu, "The lobby's settings (race speed, TV, bears, control moves), the 3D settings and your own (sound, screen, keys).");

        endButton = CourseUI.Button("End competition", card.transform, "End competition for everyone", CourseUI.ButtonKind.Danger, OnEndCompetition, 26);
        CourseUI.Size(endButton, 68);

        hostNote = CourseUI.Text("Host note", card.transform, "Only the host can end the competition", 20, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        CourseUI.Size(hostNote, 26);

        mainMenuButton = CourseUI.Button("Main menu", card.transform, "Back to main menu", CourseUI.ButtonKind.Secondary, OnMainMenu, 26);
        CourseUI.Size(mainMenuButton, 68);

        Button quit = CourseUI.Button("Quit", card.transform, "Quit game", CourseUI.ButtonKind.Light, OnQuit, 26);
        CourseUI.Size(quit, 68);

        // Your own settings: a page of their own behind Settings (2026-11-19)
        Image personal = NewCard("Your settings", root.transform, "Your settings");
        personalCard = personal.gameObject;
        Transform mine = personal.transform;

        // Volume of every sound in the game: a label and the value on one line, the slider under it
        RectTransform volumeRow = CourseUI.Rect("Volume row", mine);
        CourseUI.Row(volumeRow.gameObject, 12, new RectOffset(4, 4, 6, 0));
        CourseUI.Size(volumeRow, 40);
        TextMeshProUGUI volumeLabel = CourseUI.Text("Label", volumeRow, "Volume", 28, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(volumeLabel, -1, -1, 1);
        volumeValue = CourseUI.Text("Value", volumeRow, "100%", 28, CourseUI.Accent, FontStyles.Bold, TextAlignmentOptions.Right);
        CourseUI.Size(volumeValue, -1, 110);

        volumeSlider = CourseUI.SliderControl("Volume", mine, 0, 100, 100, true, OnVolumeChanged);
        CourseUI.Size(volumeSlider, 40);

        // The sounds of "Run it yourself" one by one, and how quickly the mouse turns the head there (2026-11-20,
        // Anders: one sound section here for everything, not in the 3D settings)
        RunSettings.SoundRows(mine);
        RunSettings.MouseRow(mine);

        // Display: fullscreen or a window (F11 / Alt+Enter switch too)
        TextMeshProUGUI displayLabel = CourseUI.Text("Display label", mine, "Display", 28, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(displayLabel, 40);
        RectTransform displayRow = CourseUI.Rect("Display row", mine);
        CourseUI.Row(displayRow.gameObject, 12, new RectOffset(0, 0, 0, 0));
        CourseUI.Size(displayRow, 56);
        fullscreenButton = CourseUI.Button("Fullscreen", displayRow, "Fullscreen", CourseUI.ButtonKind.Primary, OnFullscreen, 24);
        CourseUI.Size(fullscreenButton, -1, -1, 1);
        windowButton = CourseUI.Button("Window", displayRow, "Window", CourseUI.ButtonKind.Light, OnWindow, 24);
        CourseUI.Size(windowButton, -1, -1, 1);

        RectTransform gap = CourseUI.Rect("Gap", mine);
        CourseUI.Size(gap, 6);

        statsButton = CourseUI.Button("Usage statistics", mine, "", CourseUI.ButtonKind.Light, OnStatsToggle, 22);
        CourseUI.Size(statsButton, 52);
        ShowStatsButton();
        HoverTip.Add(statsButton, "Anonymous statistics (how many play and what is played, no names) help make the game better. Click to turn them on or off.");

        Button keys = CourseUI.Button("Key bindings", mine, "Key bindings", CourseUI.ButtonKind.Light, () => ShowKeysPage(true), 26);
        CourseUI.Size(keys, 68);
        Button personalBack = CourseUI.Button("Back", mine, "Back", CourseUI.ButtonKind.Primary, () => ShowPage(settingsCard), 26);
        CourseUI.Size(personalBack, 68);

        // Settings: the choice between the lobby's settings and your own
        Image choice = NewCard("Settings", root.transform, "Settings");
        settingsCard = choice.gameObject;
        // The host's lobby settings (speeds, TV, bears and moves), also during the race. The host changes them; everyone
        // else sees what the lobby uses, without it changing their own saved values.
        settingsButton = CourseUI.Button("Lobby settings", choice.transform, "Lobby settings", CourseUI.ButtonKind.Light, () =>
        {
            Close();
            HostSettingsPopup.Open();
        }, 28);
        CourseUI.Size(settingsButton, 76);
        HoverTip.Add(settingsButton, "Race speed, TV controls, bears and control moves. Only the host can change them, also during the race; everyone else can look.");
        Button threeD = CourseUI.Button("3D settings", choice.transform, "3D settings", CourseUI.ButtonKind.Light, () =>
        {
            Close();
            RunSettings.Open();
        }, 28);
        CourseUI.Size(threeD, 76);
        HoverTip.Add(threeD, "For \"Run it yourself\" (3D): graphics, game speed, man or woman, and the hand that holds the map.");
        Button yours = CourseUI.Button("Your settings", choice.transform, "Your settings", CourseUI.ButtonKind.Light, () => ShowPage(personalCard), 28);
        CourseUI.Size(yours, 76);
        HoverTip.Add(yours, "Sound, mouse look, fullscreen or window, usage statistics and key bindings.");
        Button choiceBack = CourseUI.Button("Back", choice.transform, "Back", CourseUI.ButtonKind.Primary, () => ShowPage(mainCard), 26);
        CourseUI.Size(choiceBack, 68);

        HoverTip.Add(volumeSlider, "The volume of all sounds in the game.");
        HoverTip.Add(fullscreenButton, "Fill the whole screen. F11 also switches.");
        HoverTip.Add(windowButton, "Play in a window you can resize. F11 also switches.");
        HoverTip.Add(resume, "Close this menu and go on playing.");
        HoverTip.Add(keys, "See which keys do what, and change them.");
        HoverTip.Add(endButton, "Stops the competition for all players and takes everyone back to the lobby. Only the host can do it, and you have to confirm.");
        HoverTip.Add(mainMenuButton, "Leave the competition and go back to the main menu. The others carry on without you.");
        HoverTip.Add(quit, "Close the game.");

        TextMeshProUGUI hint = CourseUI.Text("Hint", card.transform, "Esc closes the menu  ·  F11 switches fullscreen", 18, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        CourseUI.Size(hint, 24);

        BuildKeysCard();
        root.SetActive(false);
    }
}
