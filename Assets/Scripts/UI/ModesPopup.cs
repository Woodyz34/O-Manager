using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The lobby's "Game mode" window: the host ticks the modes to play with. Several can be on at once
// (Imposter, Bears, Move a control); Route analysis is played on its own, so ticking it clears the others.
// Nothing ticked is a normal race. Looks like ChoicePopup.
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

        int tiles = 1;
        foreach (ExtraMode mode in ExtraModes.All) if ((offered & mode) != 0) tiles++;
        float height = 150f + tiles * 112f + 100f;
        Image card = CourseUI.CardPanel("Card", transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, height));
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Column(card.gameObject, 12, new RectOffset(36, 36, 30, 30));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Game mode", 40, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 52);
        TextMeshProUGUI hint = CourseUI.Text("Hint", card.transform, hintText, 21, CourseUI.InkMuted);
        CourseUI.Size(hint, 30);

        list = CourseUI.Rect("Modes", card.transform);
        CourseUI.Column(list.gameObject, 10, new RectOffset(0, 0, 0, 0));
        CourseUI.Size(list, tiles * 112f - 10f);
        Fill();

        Button done = CourseUI.Button("Done", card.transform, "Done", CourseUI.ButtonKind.Primary, Done, 24);
        CourseUI.Size(done, 58);
    }

    void Fill()
    {
        foreach (Transform child in list) Destroy(child.gameObject);
        AddTile("Normal", "No extra modes: everyone races to finish first.", modes == ExtraMode.None, () => { if (!readOnly) modes = ExtraMode.None; });
        int players = ExtraModes.PlayersInLobby();
        foreach (ExtraMode mode in ExtraModes.All)
        {
            if ((offered & mode) == 0) continue;
            ExtraMode captured = mode;
            string description = ExtraModes.Description(mode);
            // Bears and Move a control cannot be ticked on a sprint course (nor the modes for individual courses on a relay).
            // They are shown greyed out; clicking one anyway says why.
            string notHere = ExtraModes.NotOn(mode, CourseLibrary.Current);
            // The imposter modes need at least 3 players in the lobby (a host alone in single player never sees them)
            if (notHere == null && !readOnly && (offered & ExtraModes.ImposterKinds) == ExtraModes.ImposterKinds)
                notHere = ExtraModes.NotWithPlayers(mode, players);
            if (notHere != null)
            {
                if (!readOnly) modes &= ~mode;
                AddTile(ExtraModes.Title(mode), description, false,
                    () => UIToast.Show(notHere, 3f, CourseUI.Ink), true);
                continue;
            }
            bool secret = mode == ExtraMode.SecretTask;
            Image tile = AddTile(ExtraModes.Title(mode), description, (modes & mode) != 0, () =>
            {
                if (readOnly) return;
                bool wasOn = (modes & captured) != 0;
                modes = ExtraModes.Toggle(modes, captured);
                // Switching Secret task on asks which tasks to play with
                if (captured == ExtraMode.SecretTask && !wasOn && (modes & captured) != 0) OpenTasks();
            });
            if (secret && (modes & mode) != 0)
            {
                // "Tasks" button: the host edits the list, the guests only look at it
                Button tasks = CourseUI.Button("Tasks", tile.transform, readOnly ? "See tasks" : "Tasks", CourseUI.ButtonKind.Primary, OpenTasks, 22);
                CourseUI.Place(tasks.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24, 0), new Vector2(150, 56));
                Transform label = tile.transform.Find("Text");
                if (label != null) CourseUI.Stretch((RectTransform)label, 86, 190, 10, 10);
            }
        }
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
        CourseUI.Size(tile, 102);
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
        CourseUI.Stretch(text.rectTransform, 86, 22, 10, 10);
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
