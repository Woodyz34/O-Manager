using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MapImport;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// "Uncertain contours" in Edit map (2026-11-27, Anders: "Ska den vara högre eller lägre än den här höjdkurvan? ...
// det ska vara extremt simpelt"). The game shows one contour it is unsure of (orange) and the contour next to it
// (blue) on the map, and the player answers Higher, Lower or Same height; then the next one comes. Nothing is drawn by
// hand. Skip asks first whether the player knows the map may become a little uncertain there. When the list is done
// the map is checked again with the answers (they can show more places to check), until nothing is left. The answers
// go back to Edit map, which keeps them with the map when it is saved (MapPackageInfo.heightAnswers).
public class HeightQuestionPanel : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    MapDefinition map;
    Func<double[], List<RunContourField.HeightQuestion>> find;   // the questions left with these answers (background thread)
    Action<double[]> onDone;
    double[] answers;
    List<RunContourField.HeightQuestion> queue = new List<RunContourField.HeightQuestion>();
    int index;
    int answeredNow;
    readonly List<RunContourField.HeightQuestion> skipped = new List<RunContourField.HeightQuestion>();
    readonly Stack<(double[] answers, int index, bool skip)> undo = new Stack<(double[], int, bool)>();
    bool busy;
    double viewWidth;                  // metres across the picture

    TextMeshProUGUI question, progress, hint, confirmText;
    RectTransform frameRect;
    RawImage picture;
    ContourLinesGraphic orangeLines, blueLines;
    Button higher, lower, same, skip, back, stop;
    GameObject confirm, answerRow;
    Sprite sprite;
    MapBackground background;

    static readonly Color Orange = CourseUI.Hex("E8742A");
    static readonly Color Blue = CourseUI.Hex("1E6FD9");
    static readonly Vector2 Area = new Vector2(1100, 600);

    public static void Open(Transform canvas, MapDefinition map, List<RunContourField.HeightQuestion> questions, double[] answers,
                            Func<double[], List<RunContourField.HeightQuestion>> find, Action<double[]> onDone)
    {
        if (IsOpen) return;
        var dim = CourseUI.Panel("Uncertain contours", canvas, new Color(0.08f, 0.14f, 0.1f, 0.7f), false);
        CourseUI.Stretch(dim.rectTransform);
        HeightQuestionPanel panel = dim.gameObject.AddComponent<HeightQuestionPanel>();
        panel.map = map;
        panel.find = find;
        panel.onDone = onDone;
        panel.answers = answers;
        panel.queue = questions ?? new List<RunContourField.HeightQuestion>();
        panel.Build(dim.transform);
        IsOpen = true;
        panel.Show();
    }

    void OnDestroy()
    {
        IsOpen = false;
    }

    // ---- Layout ----

    void Build(Transform root)
    {
        Image card = CourseUI.CardPanel("Card", root);
        CourseUI.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180, 1000));

        TextMeshProUGUI title = CourseUI.Text("Title", card.transform, "Uncertain contours", 34, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -22), new Vector2(1100, 44));
        question = CourseUI.Text("Question", card.transform, "", 27, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Place(question.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -70), new Vector2(1100, 40));
        progress = CourseUI.Text("Progress", card.transform, "", 19, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        CourseUI.Place(progress.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -112), new Vector2(1100, 28));

        // The map round the two contours
        Image frame = CourseUI.Panel("Frame", card.transform, Color.white, false);
        CourseUI.Place(frame.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -146), Area);
        frame.gameObject.AddComponent<RectMask2D>();
        frameRect = frame.rectTransform;
        picture = CourseUI.Rect("Picture", frame.transform).gameObject.AddComponent<RawImage>();
        picture.raycastTarget = false;
        CourseUI.Stretch(picture.rectTransform);
        blueLines = Lines("Blue", frame.transform, Blue);
        orangeLines = Lines("Orange", frame.transform, Orange);
        background = map.GetBackground(map.defaultBackground);
        try { sprite = MapPackages.LoadSprite(background); }
        catch (Exception e) { Debug.LogWarning("HeightQuestionPanel: no map picture: " + e.Message); }
        if (sprite != null) picture.texture = sprite.texture;
        else picture.color = new Color(0.97f, 0.97f, 0.95f);

        // Zoom, in the frame's top right corner
        RectTransform zoom = CourseUI.Rect("Zoom", frame.transform);
        CourseUI.Place(zoom, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-10, -10), new Vector2(130, 56));
        CourseUI.Row(zoom.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        Button zoomOut = CourseUI.Button("Zoom out", zoom, "-", CourseUI.ButtonKind.Light, () => { viewWidth = Math.Min(2000, viewWidth * 1.5); Draw(); }, 28);
        Button zoomIn = CourseUI.Button("Zoom in", zoom, "+", CourseUI.ButtonKind.Light, () => { viewWidth = Math.Max(30, viewWidth / 1.5); Draw(); }, 28);
        HoverTip.Add(zoomOut, "See more of the map round the two contours.");
        HoverTip.Add(zoomIn, "See the two contours closer.");

        hint = CourseUI.Text("Hint", card.transform,
            "<color=#E8742A><b>Orange</b></color>: the contour the game is unsure of.   <color=#1E6FD9><b>Blue</b></color>: the contour next to it.\n"
            + "Look at the slope ticks, knolls, hollows, streams and marshes round them.",
            18, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        hint.textWrappingMode = TextWrappingModes.Normal;
        CourseUI.Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -754), new Vector2(1100, 54));

        // Higher, lower, same height
        RectTransform answerRect = CourseUI.Rect("Answers", card.transform);
        answerRow = answerRect.gameObject;
        CourseUI.Place(answerRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 96), new Vector2(1100, 66));
        CourseUI.Row(answerRow, 14, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        higher = CourseUI.Button("Higher", answerRect, "Higher", CourseUI.ButtonKind.Primary, () => Answer(1), 28);
        lower = CourseUI.Button("Lower", answerRect, "Lower", CourseUI.ButtonKind.Primary, () => Answer(-1), 28);
        same = CourseUI.Button("Same", answerRect, "Same height", CourseUI.ButtonKind.Secondary, () => Answer(0), 24);
        HoverTip.Add(higher, "The orange contour is one contour higher than the blue one.");
        HoverTip.Add(lower, "The orange contour is one contour lower than the blue one.");
        HoverTip.Add(same, "They are at the same height: a ridge or a valley lies between them.");

        RectTransform more = CourseUI.Rect("More", card.transform);
        CourseUI.Place(more, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 26), new Vector2(1100, 54));
        CourseUI.Row(more.gameObject, 14, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        back = CourseUI.Button("Back", more, "Back", CourseUI.ButtonKind.Light, Back, 22);
        skip = CourseUI.Button("Skip", more, "Skip", CourseUI.ButtonKind.Light, () => confirm.SetActive(true), 22);
        stop = CourseUI.Button("Stop", more, "Stop for now", CourseUI.ButtonKind.Light, Finish, 22);
        HoverTip.Add(back, "Go back to the last question and answer it again.");
        HoverTip.Add(skip, "Leave this one as the game guessed it.");
        HoverTip.Add(stop, "Keep the answers you have given and go back to Edit map. You can go on later.");

        // "Are you aware that the map may become a little uncertain?" before a skip
        Image shade = CourseUI.Panel("Confirm", card.transform, new Color(0.08f, 0.14f, 0.1f, 0.6f), false);
        CourseUI.Stretch(shade.rectTransform);
        confirm = shade.gameObject;
        Image box = CourseUI.CardPanel("Box", shade.transform);
        CourseUI.Place(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720, 260));
        confirmText = CourseUI.Text("Text", box.transform,
            "Are you aware that the map can become a little uncertain here if you skip this one?",
            24, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        confirmText.textWrappingMode = TextWrappingModes.Normal;
        CourseUI.Place(confirmText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(640, 110));
        RectTransform yesNo = CourseUI.Rect("Buttons", box.transform);
        CourseUI.Place(yesNo, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 30), new Vector2(560, 60));
        CourseUI.Row(yesNo.gameObject, 14, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        CourseUI.Button("Yes", yesNo, "Yes, skip", CourseUI.ButtonKind.Primary, () => { confirm.SetActive(false); Skip(); }, 24);
        CourseUI.Button("No", yesNo, "No", CourseUI.ButtonKind.Light, () => confirm.SetActive(false), 24);
        confirm.SetActive(false);
    }

    static ContourLinesGraphic Lines(string name, Transform parent, Color color)
    {
        RectTransform rect = CourseUI.Rect(name, parent);
        rect.gameObject.AddComponent<CanvasRenderer>();     // before the Graphic, or it caches a missing renderer and draws nothing
        ContourLinesGraphic g = rect.gameObject.AddComponent<ContourLinesGraphic>();
        CourseUI.Stretch(g.rectTransform);
        g.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        g.color = new Color(color.r, color.g, color.b, 0.8f);
        g.thickness = 6f;
        g.raycastTarget = false;
        return g;
    }

    // ---- One question ----

    RunContourField.HeightQuestion Current => index >= 0 && index < queue.Count ? queue[index] : null;

    void Show()
    {
        RunContourField.HeightQuestion q = Current;
        bool asking = q != null && !busy;
        foreach (Button b in new[] { higher, lower, same, skip }) b.interactable = asking;
        back.interactable = !busy && undo.Count > 0;
        stop.interactable = !busy;
        if (busy) return;
        if (q == null)
        {
            question.text = "Every uncertain contour is answered.";
            progress.text = Summary();
            orangeLines.SetRuns(null);
            blueLines.SetRuns(null);
            CourseUI.SetButtonText(stop, "Done");
            return;
        }
        CourseUI.SetButtonText(stop, "Stop for now");
        question.text = "Is the <color=#E8742A>orange</color> contour higher or lower than the <color=#1E6FD9>blue</color> one?";
        progress.text = $"Question {index + 1} of {queue.Count}" + (answeredNow > 0 || skipped.Count > 0 ? "   ·   " + Summary() : "");
        double d = MapGeometry.Dist(q.ax, q.ay, q.bx, q.by);
        viewWidth = Math.Max(90, d * 6);
        Draw();
    }

    string Summary()
    {
        string s = answeredNow == 1 ? "1 answer" : answeredNow + " answers";
        if (skipped.Count > 0) s += ", " + skipped.Count + " skipped";
        return s + ". They are kept when you press Save in Edit map.";
    }

    void Answer(int relation)
    {
        RunContourField.HeightQuestion q = Current;
        if (q == null || busy) return;
        undo.Push((answers, index, false));
        answers = HeightAnswers.With(answers, RunContourField.Answer(q, relation));
        answeredNow++;
        Next();
    }

    void Skip()
    {
        RunContourField.HeightQuestion q = Current;
        if (q == null || busy) return;
        undo.Push((answers, index, true));
        skipped.Add(q);
        Next();
    }

    void Back()
    {
        if (busy || undo.Count == 0) return;
        var last = undo.Pop();
        if (last.skip) skipped.RemoveAt(skipped.Count - 1);
        else answeredNow--;
        answers = last.answers;
        index = last.index;
        Show();
    }

    void Next()
    {
        index++;
        if (index < queue.Count) Show();
        else Recheck();
    }

    // The list is done: the map is checked again with the answers, which can settle more or show new places to check
    async void Recheck()
    {
        busy = true;
        question.text = "Checking the map again with your answers ...";
        progress.text = Summary();
        Show();
        List<RunContourField.HeightQuestion> found = null;
        double[] now = answers;
        try { found = await Task.Run(() => find(now)); }
        catch (Exception e) { Debug.LogWarning("HeightQuestionPanel: " + e); }
        if (this == null) return;
        busy = false;
        var left = new List<RunContourField.HeightQuestion>();
        if (found != null)
            foreach (RunContourField.HeightQuestion q in found)
                if (!WasSkipped(q)) left.Add(q);
        queue = left;
        index = 0;
        undo.Clear();   // the old questions are gone
        Show();
        if (left.Count > 0) progress.text = $"Your answers showed {left.Count} more {(left.Count == 1 ? "place" : "places")} to check.   ·   " + Summary();
    }

    bool WasSkipped(RunContourField.HeightQuestion q)
    {
        foreach (RunContourField.HeightQuestion s in skipped)
            if (MapGeometry.Dist(s.ax, s.ay, q.ax, q.ay) < 15 || MapGeometry.Dist(s.ax, s.ay, q.bx, q.by) < 15
                || MapGeometry.Dist(s.bx, s.by, q.ax, q.ay) < 15) return true;
        return false;
    }

    void Finish()
    {
        if (busy) return;
        onDone?.Invoke(answers);
        Destroy(gameObject);
    }

    // ---- The map and the two lines ----

    void Draw()
    {
        RunContourField.HeightQuestion q = Current;
        if (q == null) return;
        double cx = (q.ax + q.bx) / 2, cy = (q.ay + q.by) / 2;
        double w = viewWidth, h = viewWidth * Area.y / Area.x;
        if (sprite != null && background != null)
        {
            Vector2 lo = TextureUV(cx - w / 2, cy - h / 2), hi = TextureUV(cx + w / 2, cy + h / 2);
            picture.uvRect = new Rect(lo.x, lo.y, hi.x - lo.x, hi.y - lo.y);
        }
        orangeLines.SetRuns(Runs(q.a, cx, cy, w, h));
        blueLines.SetRuns(Runs(q.b, cx, cy, w, h));
    }

    // Where a place (game metres) is in the map picture's texture (0..1, from the bottom left)
    Vector2 TextureUV(double e, double n)
    {
        Vector2 uv = background.WorldToUV(map.ToWorld(new CoursePoint(e, n)));
        Vector2 pixel = new Vector2(uv.x * background.pixelSize.x, uv.y * background.pixelSize.y);
        Rect r = sprite.textureRect;
        return new Vector2((r.x + pixel.x) / sprite.texture.width, (r.y + pixel.y) / sprite.texture.height);
    }

    // The parts of a line inside the picture (and a little round it), in the frame's own units
    static List<List<Vector2>> Runs(double[] xy, double cx, double cy, double w, double h)
    {
        var runs = new List<List<Vector2>>();
        if (xy == null) return runs;
        List<Vector2> run = null;
        double mx = w * 0.75, my = h * 0.75;
        for (int i = 0; i + 1 < xy.Length; i += 2)
        {
            double dx = xy[i] - cx, dy = xy[i + 1] - cy;
            if (Math.Abs(dx) > mx || Math.Abs(dy) > my)
            {
                run = null;
                continue;
            }
            if (run == null) runs.Add(run = new List<Vector2>());
            run.Add(new Vector2((float)(dx / w * Area.x), (float)(dy / h * Area.y)));
        }
        return runs;
    }
}
