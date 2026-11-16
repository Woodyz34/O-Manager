using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The relay settings in the course builder (2026-11-16): fork mode and how the other legs show.
// Kept out of the relay panel because the defaults are what you want almost always.
public class RelaySettingsPopup : MonoBehaviour
{
    static RelaySettingsPopup open;

    Func<bool> everyBranch, showOther;
    Action toggleFork, toggleOther;
    Button forkButton, otherButton;
    TextMeshProUGUI forkText;

    public static void Open(Func<bool> everyBranch, Action toggleFork, Func<bool> showOther, Action toggleOther)
    {
        if (open != null) open.Close();
        GameObject go = new GameObject("Relay settings", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        open = go.AddComponent<RelaySettingsPopup>();
        open.everyBranch = everyBranch;
        open.toggleFork = toggleFork;
        open.showOther = showOther;
        open.toggleOther = toggleOther;
        open.Build();
    }

    void Build()
    {
        Image dim = CourseUI.Panel("Dim", transform, new Color(0f, 0f, 0f, 0.45f), false);
        CourseUI.Stretch(dim.rectTransform);
        Button outside = dim.gameObject.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.onClick.AddListener(Close);

        Image card = CourseUI.CardPanel("Card", transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720, 640));
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Column(card.gameObject, 12, new RectOffset(36, 36, 30, 30));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Relay settings", 40, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 52);

        TextMeshProUGUI forkHead = CourseUI.Text("Forks", card.transform, "Forks", 24, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(forkHead, 32);
        forkButton = CourseUI.Button("Fork mode", card.transform, "", CourseUI.ButtonKind.Light, () => { toggleFork(); Refresh(); }, 22);
        CourseUI.Size(forkButton, 54);
        forkText = CourseUI.Text("Fork text", card.transform, "", 19, CourseUI.InkMuted);
        CourseUI.Size(forkText, 54);

        TextMeshProUGUI otherHead = CourseUI.Text("Other legs", card.transform, "Other legs on the map", 24, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(otherHead, 32);
        otherButton = CourseUI.Button("Other legs", card.transform, "", CourseUI.ButtonKind.Light, () => { toggleOther(); Refresh(); }, 22);
        CourseUI.Size(otherButton, 54);

        TextMeshProUGUI help = CourseUI.Text("Help", card.transform,
            "Turn a control into a fork with \"Fork\" in the control list; a branch can have several controls, or none. "
            + "Bring a control to a faded mark and it clicks into place: on a plain control it becomes the same control, on a fork's control (F1A...) the whole fork goes on this leg.",
            18, CourseUI.InkMuted);
        CourseUI.Size(help, 120);

        Button close = CourseUI.Button("Close", card.transform, "Close", CourseUI.ButtonKind.Primary, Close, 24);
        CourseUI.Size(close, 58);
        Refresh();
    }

    void Refresh()
    {
        bool every = everyBranch();
        CourseUI.SetButtonText(forkButton, every ? "Every team runs every branch" : "Branches are random");
        CourseUI.SetButtonColor(forkButton, every ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Secondary);
        forkText.text = every
            ? "Every team runs every branch of a fork once, so a fork needs as many legs as it has branches. Click to change."
            : "Each team gets a random branch of every fork. Click to change.";
        bool other = showOther();
        CourseUI.SetButtonText(otherButton, other ? "Shown faded" : "Only their forks");
        CourseUI.SetButtonColor(otherButton, other ? CourseUI.ButtonKind.Light : CourseUI.ButtonKind.Secondary);
    }

    public void Close()
    {
        if (open == this) open = null;
        Destroy(gameObject);
    }
}
