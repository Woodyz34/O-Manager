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

    public static void Open(ExtraMode current, Action<ExtraMode> onChosen)
    {
        if (open != null) open.Close();
        GameObject go = new GameObject("Choice: Game mode", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        open = go.AddComponent<ModesPopup>();
        open.modes = current;
        open.chosen = onChosen;
        open.Build();
    }

    void Build()
    {
        Image dim = CourseUI.Panel("Dim", transform, new Color(0f, 0f, 0f, 0.45f), false);
        CourseUI.Stretch(dim.rectTransform);
        Button outside = dim.gameObject.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.onClick.AddListener(Done);

        int tiles = ExtraModes.All.Length + 1;
        float height = 150f + tiles * 112f + 100f;
        Image card = CourseUI.CardPanel("Card", transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, height));
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Column(card.gameObject, 12, new RectOffset(36, 36, 30, 30));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Game mode", 40, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 52);
        TextMeshProUGUI hint = CourseUI.Text("Hint", card.transform, "Tick as many as you like. Everyone in the lobby plays with them.", 21, CourseUI.InkMuted);
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
        AddTile("Normal", "No extra modes: everyone races to finish first.", modes == ExtraMode.None, () => modes = ExtraMode.None);
        foreach (ExtraMode mode in ExtraModes.All)
        {
            ExtraMode captured = mode;
            string description = ExtraModes.Description(mode);
            AddTile(ExtraModes.Title(mode), description, (modes & mode) != 0, () => modes = ExtraModes.Toggle(modes, captured));
        }
    }

    void AddTile(string name, string description, bool on, Action click)
    {
        Image tile = CourseUI.Panel(name, list, on ? CourseUI.Forest : CourseUI.Soft);
        CourseUI.Size(tile, 102);
        Button button = tile.gameObject.AddComponent<Button>();
        button.targetGraphic = tile;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.92f, 0.92f, 0.92f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        button.colors = colors;
        button.onClick.AddListener(() =>
        {
            click();
            Fill();
        });

        // A tick box on the left
        Image box = CourseUI.Panel("Tick", tile.transform, on ? Color.white : new Color(1f, 1f, 1f, 0.9f));
        CourseUI.Place(box.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24, 0), new Vector2(40, 40));
        box.raycastTarget = false;
        Outline outline = box.gameObject.AddComponent<Outline>();
        outline.effectColor = on ? new Color(1f, 1f, 1f, 0.6f) : CourseUI.FieldBorder;
        outline.effectDistance = new Vector2(2, -2);
        TextMeshProUGUI tick = CourseUI.Text("Mark", box.transform, on ? "X" : "", 28, CourseUI.Forest, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Stretch(tick.rectTransform);

        TextMeshProUGUI text = CourseUI.Text("Text", tile.transform, "", 29, on ? Color.white : CourseUI.Ink);
        CourseUI.Stretch(text.rectTransform, 86, 22, 10, 10);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.richText = true;
        text.raycastTarget = false;
        string muted = on ? "<color=#FFFFFFCC>" : "<color=#5B7563>";
        text.text = "<b>" + name + "</b>\n<size=20>" + muted + description + "</color></size>";
    }

    void Done()
    {
        chosen?.Invoke(modes);
        Close();
    }

    public void Close()
    {
        if (open == this) open = null;
        Destroy(gameObject);
    }
}
