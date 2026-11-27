using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MapImport;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// "Uncertain contours" in Edit map (2026-11-27, Anders: "Ska den vara högre eller lägre än den här höjdkurvan? ...
// det ska vara extremt simpelt"). The game shows one contour it is unsure of (pink) and the contour next to it (blue)
// on the map, and the player answers Higher, Lower or Same height; then the next one comes. Nothing is drawn by hand.
// Skip asks first whether the player knows the map may become a little uncertain there. After every answer the map
// is checked again in the background with all the answers, and the questions the answers have settled are taken away
// (2026-11-27, Anders: "varje svar man ger så ta bort vissa höjdkurvor"). The map is dragged with the mouse and zoomed
// with the wheel. The answers go back to Edit map, which keeps them with the map when it is saved
// (MapPackageInfo.heightAnswers).
public class HeightQuestionPanel : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    MapDefinition map;
    Func<double[], List<RunContourField.HeightQuestion>> find;   // the questions left with these answers (background thread)
    Action<double[]> onDone;
    Action<double[]> onChange;   // after every answer: kept on disk at once, so nothing is lost if the game stops
    double[] answers;
    RunContourField.HeightQuestion current;                       // the question on screen, or null
    List<RunContourField.HeightQuestion> rest = new List<RunContourField.HeightQuestion>();
    int answeredNow;
    readonly List<RunContourField.HeightQuestion> skipped = new List<RunContourField.HeightQuestion>();
    readonly Stack<(double[] answers, RunContourField.HeightQuestion q, bool skip)> undo = new Stack<(double[], RunContourField.HeightQuestion, bool)>();
    bool checking, checkAgain;         // the map is being checked again with the answers (and should be once more after)
    double viewWidth;                  // metres across the picture
    double centreE, centreN;           // the middle of the picture, game metres

    TextMeshProUGUI question, progress, hint, confirmText;
    RectTransform frameRect;
    RawImage picture;
    ContourLinesGraphic pinkLines, blueLines;
    Button higher, lower, same, skip, back, stop;
    GameObject confirm, answerRow;
    Sprite sprite;
    MapBackground background;

    // Not orange: the map's own contours are orange-brown (2026-11-27, Anders could not tell which contour was meant)
    static readonly Color Pink = CourseUI.Hex("E0158F");
    static readonly Color Blue = CourseUI.Hex("1E50E0");
    static readonly Vector2 Area = new Vector2(1100, 600);

    public static void Open(Transform canvas, MapDefinition map, List<RunContourField.HeightQuestion> questions, double[] answers,
                            Func<double[], List<RunContourField.HeightQuestion>> find, Action<double[]> onDone, Action<double[]> onChange = null)
    {
        if (IsOpen) return;
        var dim = CourseUI.Panel("Uncertain contours", canvas, new Color(0.08f, 0.14f, 0.1f, 0.7f), false);
        CourseUI.Stretch(dim.rectTransform);
        HeightQuestionPanel panel = dim.gameObject.AddComponent<HeightQuestionPanel>();
        panel.map = map;
        panel.find = find;
        panel.onDone = onDone;
        panel.onChange = onChange;
        panel.answers = answers;
        if (questions != null) panel.rest.AddRange(questions);
        panel.Build(dim.transform);
        IsOpen = true;
        panel.Next();
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
        // Behind the lines: drag the map (either mouse button) and zoom with the wheel
        Image view = CourseUI.Panel("View", frame.transform, new Color(1, 1, 1, 0), false);
        CourseUI.Stretch(view.rectTransform);
        view.raycastTarget = true;
        view.gameObject.AddComponent<HeightQuestionView>().panel = this;
        blueLines = Lines("Blue", frame.transform, Blue, 7f);
        pinkLines = Lines("Pink", frame.transform, Pink, 9f);
        background = map.GetBackground(map.defaultBackground);
        try { sprite = MapPackages.LoadSprite(background); }
        catch (Exception e) { Debug.LogWarning("HeightQuestionPanel: no map picture: " + e.Message); }
        if (sprite != null) picture.texture = sprite.texture;
        else picture.color = new Color(0.97f, 0.97f, 0.95f);

        // Zoom, in the frame's top right corner
        RectTransform zoom = CourseUI.Rect("Zoom", frame.transform);
        CourseUI.Place(zoom, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-10, -10), new Vector2(130, 56));
        CourseUI.Row(zoom.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        Button zoomOut = CourseUI.Button("Zoom out", zoom, "-", CourseUI.ButtonKind.Light, () => Zoom(1.5), 28);
        Button zoomIn = CourseUI.Button("Zoom in", zoom, "+", CourseUI.ButtonKind.Light, () => Zoom(1 / 1.5), 28);
        HoverTip.Add(zoomOut, "See more of the map round the two contours.");
        HoverTip.Add(zoomIn, "See the two contours closer.");

        // Find the lines again when they are small or out of sight (2026-11-27, Anders)
        RectTransform find = CourseUI.Rect("Find", frame.transform);
        CourseUI.Place(find, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10, -10), new Vector2(330, 48));
        CourseUI.Row(find.gameObject, 8, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        Button findPink = CourseUI.Button("Find pink", find, "Show pink", CourseUI.ButtonKind.Primary, () => GoTo(true), 19);
        findPink.GetComponent<Image>().color = Pink;
        Button findBlue = CourseUI.Button("Find blue", find, "Show blue", CourseUI.ButtonKind.Primary, () => GoTo(false), 19);
        findBlue.GetComponent<Image>().color = Blue;
        HoverTip.Add(findPink, "Move the map to the pink contour and show all of it.");
        HoverTip.Add(findBlue, "Move the map to the blue contour and show all of it.");

        hint = CourseUI.Text("Hint", card.transform,
            "<color=#E0158F><b>Pink</b></color>: the contour the game is unsure of.   <color=#1E50E0><b>Blue</b></color>: the contour next to it.\n"
            + "Drag the map with the mouse and zoom with the wheel. Look at the slope ticks, knolls, hollows, streams and marshes.",
            18, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        hint.textWrappingMode = TextWrappingModes.Normal;
        CourseUI.Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -754), new Vector2(1100, 54));

        // Higher, lower, same height
        RectTransform answerRect = CourseUI.Rect("Answers", card.transform);
        answerRow = answerRect.gameObject;
        CourseUI.Place(answerRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 96), new Vector2(1100, 66));
        CourseUI.Row(answerRow, 14, new RectOffset(0, 0, 0, 0)).childForceExpandWidth = true;
        // Which is higher? The buttons have the lines' own colours (2026-11-27, Anders: just think which one is higher up)
        higher = CourseUI.Button("Pink", answerRect, "Pink", CourseUI.ButtonKind.Primary, () => Answer(1), 28);
        higher.GetComponent<Image>().color = Pink;
        lower = CourseUI.Button("Blue", answerRect, "Blue", CourseUI.ButtonKind.Primary, () => Answer(-1), 28);
        lower.GetComponent<Image>().color = Blue;
        same = CourseUI.Button("Same", answerRect, "Same height", CourseUI.ButtonKind.Secondary, () => Answer(0), 24);
        HoverTip.Add(higher, "The pink contour is the higher one.");
        HoverTip.Add(lower, "The blue contour is the higher one.");
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

        // Never still while the game works (2026-11-27, Anders: it stood still for minutes and looked frozen):
        // a small ring by the count while answers are checked behind the question, a sheet over the map when
        // there is nothing to ask until the check is done
        checkSpinner = LoadingSpinner.Add(card.transform, new Vector2(1f, 1f), new Vector2(-46f, -120f), 13f, 5f).gameObject;
        checkSpinner.SetActive(false);
        busy = BusyOverlay.Add(frame.transform);
        stopColor = stop.GetComponent<Image>().color;
    }

    GameObject checkSpinner;
    BusyOverlay busy;
    Color stopColor;

    void Update()
    {
        if (checkSpinner == null || busy == null) return;
        bool behind = checking && current != null;
        if (checkSpinner.activeSelf != behind) checkSpinner.SetActive(behind);
        if (checking && current == null) busy.Show("Checking the map again with your answers ...");
        else busy.Hide();
    }

    static ContourLinesGraphic Lines(string name, Transform parent, Color color, float thickness)
    {
        RectTransform rect = CourseUI.Rect(name, parent);
        rect.gameObject.AddComponent<CanvasRenderer>();     // before the Graphic, or it caches a missing renderer and draws nothing
        ContourLinesGraphic g = rect.gameObject.AddComponent<ContourLinesGraphic>();
        CourseUI.Stretch(g.rectTransform);
        g.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        g.color = new Color(color.r, color.g, color.b, 0.85f);
        g.thickness = thickness;
        g.raycastTarget = false;
        return g;
    }

    // ---- One question ----

    void Show()
    {
        RunContourField.HeightQuestion q = current;
        bool asking = q != null;
        foreach (Button b in new[] { higher, lower, same, skip }) b.interactable = asking;
        back.interactable = undo.Count > 0;
        if (q == null)
        {
            pinkLines.SetRuns(null);
            blueLines.SetRuns(null);
            if (checking)
            {
                question.text = "Checking the map again with your answers ...";
                progress.text = Summary();
                stop.interactable = false;
                return;
            }
            stop.interactable = true;
            question.text = "Every uncertain contour is answered. Press Done.";
            progress.text = Summary();
            CourseUI.SetButtonText(stop, "Done");
            stop.GetComponent<Image>().color = CourseUI.Accent;   // the one thing left to press
            return;
        }
        stop.interactable = true;
        CourseUI.SetButtonText(stop, "Stop for now");
        stop.GetComponent<Image>().color = stopColor;
        question.text = "Which contour is higher: <color=#E0158F>pink</color> or <color=#1E50E0>blue</color>?";
        ShowProgress();
        // Wide enough to see the hill or the slope round the two (2026-11-27, Anders: it started too close)
        double d = MapGeometry.Dist(q.ax, q.ay, q.bx, q.by);
        viewWidth = Math.Max(260, d * 16);
        centreE = (q.ax + q.bx) / 2;
        centreN = (q.ay + q.by) / 2;
        Draw();
    }

    void ShowProgress()
    {
        int left = rest.Count + (current != null ? 1 : 0);
        progress.text = (left == 1 ? "1 question left" : left + " questions left") + (checking ? " (checking your answers ...)" : "")
                        + (answeredNow > 0 || skipped.Count > 0 ? "   ·   " + Summary() : "");
    }

    string Summary()
    {
        string s = answeredNow == 1 ? "1 answer" : answeredNow + " answers";
        if (skipped.Count > 0) s += ", " + skipped.Count + " skipped";
        return s + ". They are kept when you press Save in Edit map.";
    }

    void Answer(int relation)
    {
        RunContourField.HeightQuestion q = current;
        if (q == null) return;
        undo.Push((answers, q, false));
        answers = HeightAnswers.With(answers, RunContourField.Answer(q, relation));
        answeredNow++;
        onChange?.Invoke(answers);
        current = null;
        Next();
        Recheck();
    }

    void Skip()
    {
        RunContourField.HeightQuestion q = current;
        if (q == null) return;
        undo.Push((answers, q, true));
        skipped.Add(q);
        current = null;
        Next();
    }

    void Back()
    {
        if (undo.Count == 0) return;
        var last = undo.Pop();
        if (last.skip) skipped.Remove(last.q);
        else answeredNow--;
        bool answersChanged = !ReferenceEquals(answers, last.answers);
        answers = last.answers;
        if (answersChanged) onChange?.Invoke(answers);
        if (current != null) rest.Insert(0, current);
        current = last.q;
        Show();
        if (answersChanged) Recheck();
    }

    // The next question in the list; when the list is empty the map is checked again before saying it is done
    void Next()
    {
        current = null;
        while (rest.Count > 0 && current == null)
        {
            RunContourField.HeightQuestion q = rest[0];
            rest.RemoveAt(0);
            if (!WasSkipped(q)) current = q;
        }
        Show();
    }

    // The map is checked again with all the answers, in the background, while the player goes on. The questions the
    // answers have settled go away; ones that came up are added.
    async void Recheck()
    {
        if (checking) { checkAgain = true; return; }
        checking = true;
        if (current != null) ShowProgress(); else Show();
        List<RunContourField.HeightQuestion> found = null;
        while (true)
        {
            checkAgain = false;
            double[] now = answers;
            try { found = await Task.Run(() => find(now)); }
            catch (Exception e) { Debug.LogWarning("HeightQuestionPanel: " + e); found = null; }
            if (this == null) return;
            if (!checkAgain) break;
        }
        checking = false;
        if (found != null)
        {
            rest.Clear();
            foreach (RunContourField.HeightQuestion q in found)
                if (!WasSkipped(q) && (current == null || !Same(q, current))) rest.Add(q);
            // The question on screen was settled by the answers before it: on to the next
            if (current != null && !found.Exists(q => Same(q, current)))
            {
                current = null;
                Next();
                return;
            }
        }
        if (current == null) Next();
        else ShowProgress();
    }

    static bool Same(RunContourField.HeightQuestion a, RunContourField.HeightQuestion b)
    {
        return MapGeometry.Dist(a.ax, a.ay, b.ax, b.ay) < 5 && MapGeometry.Dist(a.bx, a.by, b.bx, b.by) < 5;
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
        if (checking && current == null) return;
        onDone?.Invoke(answers);
        Destroy(gameObject);
    }

    // ---- The map and the two lines ----

    // The whole of one line in view (or the part round the question when the line is very long), a little round it
    void GoTo(bool pinkLine)
    {
        if (current == null) return;
        double[] xy = pinkLine ? current.a : current.b;
        double px = pinkLine ? current.ax : current.bx, py = pinkLine ? current.ay : current.by;
        if (xy == null || xy.Length < 2) return;
        double minX = px, maxX = px, minY = py, maxY = py;
        for (int i = 0; i + 1 < xy.Length; i += 2)
        {
            if (Math.Abs(xy[i] - px) > 600 || Math.Abs(xy[i + 1] - py) > 600) continue;
            minX = Math.Min(minX, xy[i]); maxX = Math.Max(maxX, xy[i]);
            minY = Math.Min(minY, xy[i + 1]); maxY = Math.Max(maxY, xy[i + 1]);
        }
        centreE = (minX + maxX) / 2;
        centreN = (minY + maxY) / 2;
        viewWidth = Math.Max(120, Math.Min(3000, Math.Max(maxX - minX, (maxY - minY) * Area.x / Area.y) * 1.3));
        Draw();
    }

    void Zoom(double factor)
    {
        if (current == null) return;
        viewWidth = Math.Max(30, Math.Min(3000, viewWidth * factor));
        Draw();
    }

    // Called by HeightQuestionView: the map dragged by this many frame units
    public void Drag(Vector2 by)
    {
        if (current == null) return;
        double perUnit = viewWidth / Area.x;
        centreE -= by.x * perUnit;
        centreN -= by.y * perUnit;
        Draw();
    }

    // Called by HeightQuestionView: the wheel, zooming round the point under the mouse (frame units from its middle)
    public void Wheel(float steps, Vector2 at)
    {
        if (current == null || steps == 0) return;
        double perUnit = viewWidth / Area.x;
        double e = centreE + at.x * perUnit, n = centreN + at.y * perUnit;
        double factor = Math.Pow(1.25, -steps);
        viewWidth = Math.Max(30, Math.Min(3000, viewWidth * factor));
        perUnit = viewWidth / Area.x;
        centreE = e - at.x * perUnit;
        centreN = n - at.y * perUnit;
        Draw();
    }

    void Draw()
    {
        if (current == null) return;
        double cx = centreE, cy = centreN;
        double w = viewWidth, h = viewWidth * Area.y / Area.x;
        if (sprite != null && background != null)
        {
            Vector2 lo = TextureUV(cx - w / 2, cy - h / 2), hi = TextureUV(cx + w / 2, cy + h / 2);
            picture.uvRect = new Rect(lo.x, lo.y, hi.x - lo.x, hi.y - lo.y);
        }
        pinkLines.SetRuns(Runs(current.a, cx, cy, w, h));
        blueLines.SetRuns(Runs(current.b, cx, cy, w, h));
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
