using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A small window over everything with a few options to pick from, each with a line that explains it.
// Used in the lobby and on the single player page for "Runner pool".
public class ChoicePopup : MonoBehaviour
{
    public struct Option
    {
        public string id;
        public string title;
        public string description;
    }

    static ChoicePopup open;

    // hint: the line under the heading (the lobby's text when left out)
    // extraText/extra: an optional button under the options (the runner pool's "Max average overall")
    public static void Open(string heading, Option[] options, string selected, Action<string> chosen, string hint = null, string extraText = null, Action extra = null)
    {
        if (open != null) open.Close();
        GameObject go = new GameObject("Choice: " + heading, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        ScreenSettings.Fit(scaler);
        open = go.AddComponent<ChoicePopup>();
        open.Build(heading, options, selected, chosen, hint ?? "Everyone in the lobby plays with what you pick.", extraText, extra);
    }

    void Build(string heading, Option[] options, string selected, Action<string> chosen, string hintText, string extraText, Action extra)
    {
        Image dim = CourseUI.Panel("Dim", transform, new Color(0f, 0f, 0f, 0.45f), false);
        CourseUI.Stretch(dim.rectTransform);
        Button outside = dim.gameObject.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.onClick.AddListener(Close);

        float height = 150f + options.Length * 126f + 90f + (extra != null ? 72f : 0f);
        Image card = CourseUI.CardPanel("Card", transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, height));
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Column(card.gameObject, 14, new RectOffset(36, 36, 30, 30));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, heading, 40, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 52);
        TextMeshProUGUI hint = CourseUI.Text("Hint", card.transform, hintText, 21, CourseUI.InkMuted);
        CourseUI.Size(hint, 30);

        foreach (Option option in options)
        {
            Option captured = option;
            bool on = option.id == selected;
            Image tile = CourseUI.Panel(option.title, card.transform, on ? CourseUI.Forest : CourseUI.Soft);
            CourseUI.Size(tile, 112);
            Button button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = tile;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.92f, 0.92f, 0.92f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                chosen?.Invoke(captured.id);
                Close();
            });
            TextMeshProUGUI text = CourseUI.Text("Text", tile.transform, "", 30, on ? Color.white : CourseUI.Ink);
            CourseUI.Stretch(text.rectTransform, 26, 26, 12, 12);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.richText = true;
            string muted = on ? "<color=#FFFFFFCC>" : "<color=#5B7563>";
            text.text = "<b>" + option.title + "</b>" + (on ? "   <size=20>CHOSEN</size>" : "") + "\n<size=21>" + muted + option.description + "</color></size>";
        }

        if (extra != null)
        {
            Button more = CourseUI.Button("Extra", card.transform, extraText ?? "", CourseUI.ButtonKind.Secondary, () =>
            {
                Close();
                extra();
            }, 24);
            CourseUI.Size(more, 58);
        }

        Button close = CourseUI.Button("Close", card.transform, "Close", CourseUI.ButtonKind.Light, Close, 24);
        CourseUI.Size(close, 58);
    }

    public void Close()
    {
        if (open == this) open = null;
        Destroy(gameObject);
    }
}
