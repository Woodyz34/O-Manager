using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The lobby's "Game mode" window, in two parts (2026-11-19): the game mode (Normal, Imposter, Secret task, Route
// analysis, Route choice game; exactly one), and the extras on top of it (Bears, Move a control; any number, but only
// with Normal, Imposter or Secret task). Looks like ChoicePopup.
public class ModesPopup : MonoBehaviour
{
    static ModesPopup open;

    ExtraMode modes;
    Action<ExtraMode> chosen;
    RectTransform list;
    ExtraMode offered = ExtraModes.LobbyMask;
    string hintText;
    bool readOnly;
    int tasksOff;
    Action<int> tasksChosen;

    // offered: the modes to show (single player only offers the ones that work alone); hint: the line under the heading
    // readOnly: the guests of a lobby look at what the host picked; nothing can be changed.
    // tasksOff / onTasksChosen: the Secret task tasks the host has switched off (see TaskFilterPopup).
    public static void Open(ExtraMode current, Action<ExtraMode> onChosen, ExtraMode? offered = null, string hint = null,
        bool readOnly = false, int tasksOff = 0, Action<int> onTasksChosen = null)
    {
        if (open != null) open.Close();
        GameObject go = new GameObject("Choice: Game mode", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        open = go.AddComponent<ModesPopup>();
        open.offered = offered ?? ExtraModes.LobbyMask;
        open.modes = current & open.offered;
        open.chosen = onChosen;
        open.readOnly = readOnly;
        open.tasksOff = tasksOff;
        open.tasksChosen = onTasksChosen;
        open.hintText = hint ?? "Tick as many as you like. Everyone in the lobby plays with them.";
        open.Build();
    }

    void Build()
    {
        Image dim = CourseUI.Panel("Dim", transform, new Color(0f, 0f, 0f, 0.45f), false);
        CourseUI.Stretch(dim.rectTransform);
        Button outside = dim.gameObject.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.onClick.AddListener(Done);

        // Two parts (2026-11-19): the game mode itself (one of them), and the extras that go on top of it
        int tiles = 1, extras = 0;
        foreach (ExtraMode mode in ExtraModes.All)
        {
            if ((offered & mode) == 0 || mode == ExtraMode.RunMass) continue;
            if ((mode & ExtraModes.AddOns) != 0) extras++;
            else tiles++;
        }
        float listHeight = (tiles + extras) * TileStep + HeadingHeight * (extras > 0 ? 2 : 1);
        // Never taller than the screen (2026-11-19: the Done button fell off the bottom): the list scrolls instead
        float screenHeight = Screen.height / ScreenSettings.FitScale();
        float shown = Mathf.Min(listHeight, Mathf.Max(260f, screenHeight - 60f - 250f));
        float height = 150f + shown + 100f;
        Image card = CourseUI.CardPanel("Card", transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, height));
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Column(card.gameObject, 12, new RectOffset(36, 36, 30, 30));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Game mode", 40, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 52);
        TextMeshProUGUI hint = CourseUI.Text("Hint", card.transform, hintText, 21, CourseUI.InkMuted);
        CourseUI.Size(hint, 30);

        list = CourseUI.ScrollList("Modes", card.transform, out ScrollRect scroll);
        CourseUI.Column(list.gameObject, TileGap, new RectOffset(0, shown < listHeight ? 22 : 0, 0, 0));
        CourseUI.Size(scroll.GetComponent<RectTransform>(), shown - TileGap);
        if (shown < listHeight) FastScroll.Add(scroll)?.OnLightCard();
        Fill();

        Button done = CourseUI.Button("Done", card.transform, "Done", CourseUI.ButtonKind.Primary, Done, 24);
        CourseUI.Size(done, 58);
    }

    const float TileHeight = 86f, TileGap = 8f, TileStep = TileHeight + TileGap, HeadingHeight = 34f;

    void Fill()
    {
        foreach (Transform child in list) Destroy(child.gameObject);
        int players = ExtraModes.PlayersInLobby();
        ExtraMode bases = ExtraModes.ImposterKinds | ExtraModes.Alone;
        ExtraMode baseMode = modes & bases;

        // ---- The game mode: exactly one ----
        AddHeading("Game mode");
        AddTile("Normal", "Everyone races to finish first.", baseMode == ExtraMode.None, () =>
        {
            if (!readOnly) modes &= ~bases;
        });
        foreach (ExtraMode mode in ExtraModes.All)
        {
            if ((offered & mode) == 0 || (mode & ExtraModes.AddOns) != 0) continue;
            if (mode == ExtraMode.RunMass) continue;   // shown as the start choice on "Run it yourself"
            if (mode == ExtraMode.RunYourself)
            {
                AddRunTile(players);
                continue;
            }
            AddModeTile(mode, players, () =>
            {
                bool wasOn = (modes & mode) != 0;
                // Picking another game mode keeps the extras, unless it is played on its own; picking the one that is on
                // goes back to Normal
                if (wasOn) modes &= ~mode;
                else
                {
                    modes = (modes & ~bases) | mode;
                    if ((mode & ExtraModes.Alone) != 0) modes &= ~ExtraModes.AddOns;
                }
                if (mode == ExtraMode.SecretTask && !wasOn && (modes & mode) != 0) OpenTasks();
            });
        }

        // ---- The extras on top: any number, with Normal, Imposter or Secret task ----
        bool anyExtra = false;
        foreach (ExtraMode mode in ExtraModes.All) if ((offered & mode) != 0 && (mode & ExtraModes.AddOns) != 0) anyExtra = true;
        if (!anyExtra) return;
        AddHeading("Extras on top of the game mode");
        bool alone = (modes & ExtraModes.Alone) != 0;
        foreach (ExtraMode mode in ExtraModes.All)
        {
            if ((offered & mode) == 0 || (mode & ExtraModes.AddOns) == 0) continue;
            if (alone)
            {
                string why = ExtraModes.Title(modes & ExtraModes.Alone) + " is played without extras";
                if (!readOnly) modes &= ~mode;
                AddTile(ExtraModes.Title(mode), ExtraModes.Description(mode), false, () => UIToast.Show(why, 3f, CourseUI.Ink), true);
                continue;
            }
            AddModeTile(mode, players, () => modes ^= mode);
        }
    }

    // A tile for one mode: greyed out when it can not be played here (sprint, relay, too few players)
    void AddModeTile(ExtraMode mode, int players, Action toggle)
    {
        string description = ExtraModes.Description(mode);
        string notHere = ExtraModes.NotOn(mode, CourseLibrary.Current);
        // The imposter modes need at least 3 players in the lobby (a host alone in single player never sees them)
        if (notHere == null && !readOnly && (offered & ExtraModes.ImposterKinds) == ExtraModes.ImposterKinds)
            notHere = ExtraModes.NotWithPlayers(mode, players);
        if (notHere != null)
        {
            if (!readOnly) modes &= ~mode;
            AddTile(ExtraModes.Title(mode), description, false, () => UIToast.Show(notHere, 3f, CourseUI.Ink), true);
            return;
        }
        Image tile = AddTile(ExtraModes.Title(mode), description, (modes & mode) != 0, () =>
        {
            if (!readOnly) toggle();
        });
        if (mode == ExtraMode.SecretTask && (modes & mode) != 0)
        {
            // "Tasks" button: the host edits the list, the guests only look at it
            Button tasks = CourseUI.Button("Tasks", tile.transform, readOnly ? "See tasks" : "Tasks", CourseUI.ButtonKind.Primary, OpenTasks, 22);
            CourseUI.Place(tasks.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24, 0), new Vector2(150, 56));
            Transform label = tile.transform.Find("Text");
            if (label != null) CourseUI.Stretch((RectTransform)label, 86, 190, 8, 8);
        }
    }

    // "Run it yourself (3D)": one game mode (2026-11-19, Anders), with a button on it for the start: everyone in their
    // own forest (individual) or all together (mass start)
    void AddRunTile(int players)
    {
        ExtraMode bases = ExtraModes.ImposterKinds | ExtraModes.Alone;
        bool on = (modes & ExtraModes.RunModes) != 0;
        bool mass = (modes & ExtraMode.RunMass) != 0;
        bool massOffered = (offered & ExtraMode.RunMass) != 0;
        string notHere = ExtraModes.NotOn(ExtraMode.RunYourself, CourseLibrary.Current);
        string description = massOffered
            ? "Be the runner: run the course yourself in 3D with the map in your hand. Choose individual start (everyone in their own forest) or mass start (all together)."
            : ExtraModes.Description(ExtraMode.RunYourself);
        if (notHere != null)
        {
            if (!readOnly) modes &= ~ExtraModes.RunModes;
            AddTile(ExtraModes.Title(ExtraMode.RunYourself), description, false, () => UIToast.Show(notHere, 3f, CourseUI.Ink), true);
            return;
        }
        Image tile = AddTile(ExtraModes.Title(ExtraMode.RunYourself), description, on, () =>
        {
            if (readOnly) return;
            if (on) modes &= ~ExtraModes.RunModes;
            else modes = (modes & ~bases & ~ExtraModes.AddOns) | ExtraMode.RunYourself;
        });
        if (!on || !massOffered) return;
        // The start: a button that flips between the two
        Button start = CourseUI.Button("Start", tile.transform, mass ? "Mass start" : "Individual", CourseUI.ButtonKind.Primary, () =>
        {
            if (readOnly) return;
            modes = (modes & ~ExtraModes.RunModes) | (mass ? ExtraMode.RunYourself : ExtraMode.RunMass);
            Fill();
        }, 22);
        CourseUI.Place(start.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-20, 0), new Vector2(170, 56));
        HoverTip.Add(start, "Individual: everyone runs in their own forest; compare times and routes afterwards.\nMass start: all start together in the same forest, see each other and can bump into each other.\nClick to change.");
        Transform label = tile.transform.Find("Text");
        if (label != null) CourseUI.Stretch((RectTransform)label, 86, 205, 8, 8);
    }

    void AddHeading(string text)
    {
        TextMeshProUGUI heading = CourseUI.Text("Heading", list, text, 24, CourseUI.Forest, FontStyles.Bold);
        heading.alignment = TextAlignmentOptions.BottomLeft;
        CourseUI.Size(heading, HeadingHeight - TileGap);
    }

    static readonly Color DisabledTile = new Color(0.55f, 0.55f, 0.52f, 0.55f);
    static readonly Color DisabledInk = new Color(0.25f, 0.27f, 0.25f, 0.55f);

    void OpenTasks()
    {
        TaskFilterPopup.Open(tasksOff, readOnly, off =>
        {
            tasksOff = off;
            tasksChosen?.Invoke(off);
        });
    }

    Image AddTile(string name, string description, bool on, Action click, bool disabled = false)
    {
        Image tile = CourseUI.Panel(name, list, disabled ? DisabledTile : on ? CourseUI.Forest : CourseUI.Soft);
        CourseUI.Size(tile, TileHeight);
        Button button = tile.gameObject.AddComponent<Button>();
        button.targetGraphic = tile;
        ColorBlock colors = button.colors;
        // A greyed-out tile does not light up under the mouse, so it does not look clickable
        colors.highlightedColor = disabled ? Color.white : new Color(0.92f, 0.92f, 0.92f);
        colors.pressedColor = disabled ? Color.white : new Color(0.8f, 0.8f, 0.8f);
        button.colors = colors;
        button.onClick.AddListener(() =>
        {
            click();
            Fill();
        });

        // A tick box on the left
        Image box = CourseUI.Panel("Tick", tile.transform, disabled ? new Color(1f, 1f, 1f, 0.35f) : on ? Color.white : new Color(1f, 1f, 1f, 0.9f));
        CourseUI.Place(box.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24, 0), new Vector2(40, 40));
        box.raycastTarget = false;
        Outline outline = box.gameObject.AddComponent<Outline>();
        outline.effectColor = on ? new Color(1f, 1f, 1f, 0.6f) : CourseUI.FieldBorder;
        outline.effectDistance = new Vector2(2, -2);
        TextMeshProUGUI tick = CourseUI.Text("Mark", box.transform, on ? "X" : "", 28, CourseUI.Forest, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Stretch(tick.rectTransform);

        TextMeshProUGUI text = CourseUI.Text("Text", tile.transform, "", 29, disabled ? DisabledInk : on ? Color.white : CourseUI.Ink);
        CourseUI.Stretch(text.rectTransform, 86, 22, 8, 8);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.richText = true;
        text.raycastTarget = false;
        string muted = disabled ? "<color=#3F443F88>" : on ? "<color=#FFFFFFCC>" : "<color=#5B7563>";
        text.text = "<b>" + name + "</b>\n<size=20>" + muted + description + "</color></size>";
        return tile;
    }

    void Done()
    {
        if (!readOnly) chosen?.Invoke(modes);
        Close();
    }

    public void Close()
    {
        if (open == this) open = null;
        Destroy(gameObject);
    }
}
