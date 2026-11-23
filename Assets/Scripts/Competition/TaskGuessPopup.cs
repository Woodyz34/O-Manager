using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Secret task mode: a caught imposter gets one guess at the others' task (2026-11-22, the user's idea: the
// imposter had too hard a time, so the others must show each other they know the task without making it obvious).
// Everyone sees the same list of tasks the host could have drawn. The imposter can press one; the others watch
// and see the guess light up. Built in code, on top of the end screen.
public class TaskGuessPopup : MonoBehaviour
{
    static readonly Color Right = CourseUI.Hex("3F7D54");
    static readonly Color Wrong = CourseUI.Hex("C0392B");

    public static TaskGuessPopup Current;

    readonly Dictionary<SecretTasks.Task, Image> tiles = new Dictionary<SecretTasks.Task, Image>();
    readonly List<Button> buttons = new List<Button>();
    TextMeshProUGUI hint;
    TextMeshProUGUI status;
    Action<SecretTasks.Task> picked;
    bool locked;

    public static TaskGuessPopup Open(List<SecretTasks.Task> tasks, bool canPick, string imposterName, Action<SecretTasks.Task> onPick)
    {
        if (Current != null) Current.Close();
        GameObject go = new GameObject("Task guess", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 210;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        Current = go.AddComponent<TaskGuessPopup>();
        Current.Build(tasks, canPick, imposterName, onPick);
        return Current;
    }

    void Build(List<SecretTasks.Task> tasks, bool canPick, string imposterName, Action<SecretTasks.Task> onPick)
    {
        picked = onPick;
        Image dim = CourseUI.Panel("Dim", transform, new Color(0.04f, 0f, 0.02f, 0.72f), false);
        CourseUI.Stretch(dim.rectTransform);

        int rowsCount = Mathf.CeilToInt(tasks.Count / 2f);
        float height = 60f + 56f + 70f + rowsCount * 98f + 60f + 40f;
        Image card = CourseUI.CardPanel("Card", transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980, height));
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Column(card.gameObject, 12, new RectOffset(36, 36, 28, 28));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform,
            canPick ? "Caught! Guess their task" : imposterName + " is guessing your task",
            40, CourseUI.Danger, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Size(title, 52);
        hint = CourseUI.Text("Hint", card.transform,
            canPick ? "Pick the task you think the others had. Guess right and you still win."
                    : "If " + imposterName + " picks your task, the imposter wins after all.",
            22, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        CourseUI.Size(hint, 60);

        RectTransform grid = CourseUI.Rect("Tasks", card.transform);
        GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(446, 86);
        layout.spacing = new Vector2(16, 12);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 2;
        layout.childAlignment = TextAnchor.UpperCenter;
        CourseUI.Size(grid, rowsCount * 98f);

        foreach (SecretTasks.Task task in tasks)
        {
            SecretTasks.Task captured = task;
            Image tile = CourseUI.Panel(task.ToString(), grid, CourseUI.Soft);
            tiles[task] = tile;
            TextMeshProUGUI text = CourseUI.Text("Text", tile.transform, SecretTasks.Label(task), 24, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
            CourseUI.Stretch(text.rectTransform, 14, 14, 6, 6);
            text.enableAutoSizing = true;
            text.fontSizeMax = 24;
            text.fontSizeMin = 16;
            if (!canPick) continue;
            Button button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = tile;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.92f, 0.92f, 0.92f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.disabledColor = Color.white;
            button.colors = colors;
            button.onClick.AddListener(() => Pick(captured));
            buttons.Add(button);
        }

        status = CourseUI.Text("Status", card.transform, "", 22, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Size(status, 40);
    }

    void Pick(SecretTasks.Task task)
    {
        if (locked) return;
        Lock();
        if (tiles.TryGetValue(task, out Image tile))
        {
            tile.color = CourseUI.Accent;
            tile.GetComponentInChildren<TextMeshProUGUI>().color = Color.white;
        }
        hint.text = "You guessed: " + SecretTasks.Label(task);
        picked?.Invoke(task);
    }

    // No more guessing (picked, or the time ran out)
    public void Lock()
    {
        locked = true;
        foreach (Button button in buttons) button.interactable = false;
    }

    public void SetStatus(string text)
    {
        if (status != null) status.text = text;
    }

    // The answer: the guess goes red or green, and the real task is marked green
    public void ShowAnswer(SecretTasks.Task guess, SecretTasks.Task real)
    {
        Lock();
        foreach (KeyValuePair<SecretTasks.Task, Image> pair in tiles)
        {
            TextMeshProUGUI text = pair.Value.GetComponentInChildren<TextMeshProUGUI>();
            if (pair.Key == real)
            {
                pair.Value.color = Right;
                text.color = Color.white;
                text.text = SecretTasks.Label(pair.Key) + (pair.Key == guess ? "\n<size=70%>GUESSED RIGHT</size>" : "\n<size=70%>THE TASK</size>");
            }
            else if (pair.Key == guess)
            {
                pair.Value.color = Wrong;
                text.color = Color.white;
                text.text = SecretTasks.Label(pair.Key) + "\n<size=70%>THE GUESS</size>";
            }
            else
            {
                pair.Value.color = new Color(1f, 1f, 1f, 0.35f);
                text.color = CourseUI.InkMuted;
            }
        }
    }

    public void Close()
    {
        if (Current == this) Current = null;
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
    }
}
