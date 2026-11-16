using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The runner pool "Random runners" (2026-11-16): the host picks how many random runners everyone gets on loan for
// the race. The last number is remembered between games.
public class RandomCountPopup : MonoBehaviour
{
    const string CountKey = "Lobby.RandomRunners";

    static RandomCountPopup open;

    int count;
    TextMeshProUGUI label;

    public static void Open(string current, Action<string> chosen)
    {
        if (open != null) open.Close();
        GameObject go = new GameObject("Random runners", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 201;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        open = go.AddComponent<RandomCountPopup>();
        int now = GameModes.RandomCount(current);
        open.count = now > 0 ? now : Mathf.Clamp(PlayerPrefs.GetInt(CountKey, GameModes.DefaultRandom), GameModes.MinRandom, GameModes.MaxRandom);
        open.Build(chosen);
    }

    void Build(Action<string> chosen)
    {
        Image dim = CourseUI.Panel("Dim", transform, new Color(0f, 0f, 0f, 0.45f), false);
        CourseUI.Stretch(dim.rectTransform);
        Button outside = dim.gameObject.AddComponent<Button>();
        outside.transition = Selectable.Transition.None;
        outside.onClick.AddListener(Close);

        Image card = CourseUI.CardPanel("Card", transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 420));
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Column(card.gameObject, 14, new RectOffset(36, 36, 30, 30));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Random runners", 40, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 52);
        TextMeshProUGUI hint = CourseUI.Text("Hint", card.transform,
            "How many random runners everyone gets on loan for the race. They pick their runners among these.", 22, CourseUI.InkMuted);
        hint.enableWordWrapping = true;
        CourseUI.Size(hint, 60);

        label = CourseUI.Text("Value", card.transform, "", 56, CourseUI.Accent, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Size(label, 72);
        Slider slider = CourseUI.SliderControl("Count", card.transform, GameModes.MinRandom, GameModes.MaxRandom, count, true, v =>
        {
            count = Mathf.RoundToInt(v);
            Refresh();
        });
        CourseUI.Size(slider, 44);

        RectTransform buttons = CourseUI.Rect("Buttons", card.transform);
        CourseUI.Row(buttons.gameObject, 16, new RectOffset(0, 0, 18, 0)).childForceExpandWidth = true;
        CourseUI.Size(buttons, 76);
        Button cancel = CourseUI.Button("Cancel", buttons, "Cancel", CourseUI.ButtonKind.Light, Close, 24);
        CourseUI.Size(cancel, -1, -1, 1);
        Button use = CourseUI.Button("Use", buttons, "Use", CourseUI.ButtonKind.Primary, () =>
        {
            PlayerPrefs.SetInt(CountKey, count);
            PlayerPrefs.Save();
            chosen?.Invoke(GameModes.MakeRandom(count));
            Close();
        }, 24);
        CourseUI.Size(use, -1, -1, 1);

        Refresh();
    }

    void Refresh()
    {
        label.text = count.ToString();
    }

    public void Close()
    {
        if (open == this) open = null;
        Destroy(gameObject);
    }
}
