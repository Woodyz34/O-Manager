using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Secret task: the list of tasks that can turn up in the race. The host ticks the ones that are fun; the other
// players open the same list read-only to see what is in the game (the host's choice is shared in the lobby).
// Looks like ModesPopup. The choice is a bit mask of SecretTasks.Bit, with a set bit meaning "switched off",
// so an empty mask (the default) means every task is in.
public class TaskFilterPopup : MonoBehaviour
{
    static TaskFilterPopup open;

    int off;
    bool readOnly;
    Action<int> chosen;
    RectTransform list;

    public static void Open(int tasksOff, bool readOnly, Action<int> onChosen)
    {
        if (open != null) open.Close();
        GameObject go = new GameObject("Choice: Secret tasks", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        open = go.AddComponent<TaskFilterPopup>();
        open.off = tasksOff;
        open.readOnly = readOnly;
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

        int rows = SecretTasks.Pool.Length;
        float rowHeight = 64f;
        float height = 150f + rows * (rowHeight + 8f) + 110f;
        Image card = CourseUI.CardPanel("Card", transform);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, height));
        card.gameObject.AddComponent<ClickCatcher>();
        CourseUI.Column(card.gameObject, 12, new RectOffset(36, 36, 30, 30));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Secret tasks", 40, CourseUI.Ink, FontStyles.Bold);
        CourseUI.Size(title, 52);
        string hintText = readOnly
            ? "The tasks the host has put in the game. Don't like one? Ask the host to turn it off."
            : "Tick the tasks that can turn up. One of them is picked at random.";
        TextMeshProUGUI hint = CourseUI.Text("Hint", card.transform, hintText, 21, CourseUI.InkMuted);
        CourseUI.Size(hint, 30);

        list = CourseUI.Rect("Tasks", card.transform);
        CourseUI.Column(list.gameObject, 8, new RectOffset(0, 0, 0, 0));
        CourseUI.Size(list, rows * (rowHeight + 8f) - 8f);
        Fill();

        Button done = CourseUI.Button("Done", card.transform, "Done", CourseUI.ButtonKind.Primary, Done, 24);
        CourseUI.Size(done, 58);
    }

    int TasksOn()
    {
        int n = 0;
        foreach (SecretTasks.Task t in SecretTasks.Pool) if ((off & SecretTasks.Bit(t)) == 0) n++;
        return n;
    }

    void Fill()
    {
        foreach (Transform child in list) Destroy(child.gameObject);
        foreach (SecretTasks.Task task in SecretTasks.Pool)
        {
            SecretTasks.Task captured = task;
            bool on = (off & SecretTasks.Bit(task)) == 0;
            AddRow(SecretTasks.Label(task), on, () =>
            {
                if (readOnly) return;
                if (on && TasksOn() <= 1)
                {
                    UIToast.Show("At least one task has to stay on", 3f, CourseUI.Ink);
                    return;
                }
                off ^= SecretTasks.Bit(captured);
            });
        }
    }

    void AddRow(string name, bool on, Action click)
    {
        Image tile = CourseUI.Panel(name, list, on ? CourseUI.Forest : CourseUI.Soft);
        CourseUI.Size(tile, 64);
        Button button = tile.gameObject.AddComponent<Button>();
        button.targetGraphic = tile;
        if (readOnly) button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() =>
        {
            click();
            Fill();
        });

        Image box = CourseUI.Panel("Tick", tile.transform, on ? Color.white : new Color(1f, 1f, 1f, 0.9f));
        CourseUI.Place(box.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20, 0), new Vector2(34, 34));
        box.raycastTarget = false;
        Outline outline = box.gameObject.AddComponent<Outline>();
        outline.effectColor = on ? new Color(1f, 1f, 1f, 0.6f) : CourseUI.FieldBorder;
        outline.effectDistance = new Vector2(2, -2);
        TextMeshProUGUI tick = CourseUI.Text("Mark", box.transform, on ? "X" : "", 24, CourseUI.Forest, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Stretch(tick.rectTransform);

        TextMeshProUGUI text = CourseUI.Text("Text", tile.transform, name, 28, on ? Color.white : CourseUI.Ink);
        CourseUI.Stretch(text.rectTransform, 74, 16, 6, 6);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
    }

    void Done()
    {
        if (!readOnly) chosen?.Invoke(off);
        Close();
    }

    public void Close()
    {
        if (open == this) open = null;
        Destroy(gameObject);
    }
}
