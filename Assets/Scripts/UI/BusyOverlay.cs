using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A dark sheet over a card while the game works in the background: a spinning ring, what it is doing and how long it
// has been at it, so nobody thinks the game has frozen (2026-11-27, Anders: "Uncertain contours" stood still for
// minutes with nothing moving). Clicks on the card are stopped while it shows.
public class BusyOverlay : MonoBehaviour
{
    TextMeshProUGUI text;
    string message = "";
    float since;
    int shownSeconds = -1;
    string shownMessage;

    public static BusyOverlay Add(Transform parent)
    {
        Image shade = CourseUI.Panel("Busy", parent, new Color(0.08f, 0.14f, 0.1f, 0.55f), false);
        CourseUI.Stretch(shade.rectTransform);
        shade.raycastTarget = true;
        BusyOverlay overlay = shade.gameObject.AddComponent<BusyOverlay>();
        Image box = CourseUI.CardPanel("Box", shade.transform);
        box.raycastTarget = true;
        CourseUI.Place(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 250));
        LoadingSpinner.Add(box.transform, new Vector2(0.5f, 1f), new Vector2(0f, -64f), 30f, 10f);
        overlay.text = CourseUI.Text("Text", box.transform, "", 22, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        overlay.text.textWrappingMode = TextWrappingModes.Normal;
        overlay.text.raycastTarget = false;
        CourseUI.Place(overlay.text.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(590f, 120f));
        shade.gameObject.SetActive(false);
        return overlay;
    }

    public bool Showing => gameObject.activeSelf;

    public void Show(string what)
    {
        if (!gameObject.activeSelf)
        {
            since = Time.unscaledTime;
            shownSeconds = -1;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }
        message = string.IsNullOrEmpty(what) ? "Working ..." : what;
    }

    public void Hide()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    void Update()
    {
        int seconds = Mathf.FloorToInt(Time.unscaledTime - since);
        if (seconds == shownSeconds && message == shownMessage) return;
        shownSeconds = seconds;
        shownMessage = message;
        string time = seconds < 60 ? seconds + " s" : seconds / 60 + " min " + (seconds % 60).ToString("00") + " s";
        text.text = message + "\n<size=75%><color=#6B7B70>The game is working, please wait.   " + time + "</color></size>";
    }
}
