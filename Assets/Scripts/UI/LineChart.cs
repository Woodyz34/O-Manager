using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A line chart made in code, in the game's style (2026-11-24, the user: "ett linjediagram ... se vilken peak man
// hade"). Used for a player's world ranking over time and for a runner card's physics, speed and technique over the
// rating updates. Several lines, a value per point in time (NaN = no value then, the line breaks), grid lines with
// the values on the left, dates under it, a legend on top, and the values of the point under the mouse.
//
//   LineChart chart = LineChart.Build(parent, "Points");
//   chart.Set(labels, series, invert: false);
public class LineChart : MonoBehaviour
{
    public class Series
    {
        public string name;
        public Color color;
        public List<float> values = new List<float>();
        public System.Func<float, string> format;   // how a value is written (the tooltip and the left side)
    }

    const float Left = 78f, Right = 24f, Top = 54f, Bottom = 46f;

    RectTransform plotRect;
    ChartLines lines;
    RectTransform labelLayer;
    RectTransform legend;
    Image marker;
    Image tipBack;
    TextMeshProUGUI tip;
    TextMeshProUGUI emptyText;
    List<string> xLabels = new List<string>();
    List<Series> series = new List<Series>();
    float min, max;
    float setMin = float.NaN, setMax = float.NaN;
    bool invert;
    int lastHover = -2;

    public static LineChart Build(Transform parent, string emptyMessage = "No data yet")
    {
        RectTransform root = CourseUI.Rect("Line chart", parent);
        LineChart chart = root.gameObject.AddComponent<LineChart>();
        Image back = root.gameObject.AddComponent<Image>();
        back.color = CourseUI.Hex("FFFFFF", 0.55f);
        if (CourseUI.Skin != null && CourseUI.Skin.rounded != null)
        {
            back.sprite = CourseUI.Skin.rounded;
            back.type = Image.Type.Sliced;
        }

        chart.plotRect = CourseUI.Rect("Plot", root);
        CourseUI.Stretch(chart.plotRect, Left, Right, Top, Bottom);
        chart.plotRect.gameObject.AddComponent<CanvasRenderer>();   // before the Graphic, or nothing is drawn
        chart.lines = chart.plotRect.gameObject.AddComponent<ChartLines>();
        chart.lines.raycastTarget = false;

        chart.labelLayer = CourseUI.Rect("Labels", root);
        CourseUI.Stretch(chart.labelLayer);

        chart.legend = CourseUI.Rect("Legend", root);
        CourseUI.Place(chart.legend, new Vector2(0, 1), new Vector2(0, 1), new Vector2(Left, -10), new Vector2(900, 34));
        HorizontalLayoutGroup row = CourseUI.Row(chart.legend.gameObject, 26, new RectOffset(0, 0, 0, 0));
        row.childForceExpandHeight = true;

        chart.marker = CourseUI.Panel("Marker", chart.plotRect, CourseUI.Hex("1F3D2B", 0.35f), false);
        chart.marker.raycastTarget = false;
        chart.marker.rectTransform.anchorMin = new Vector2(0, 0);
        chart.marker.rectTransform.anchorMax = new Vector2(0, 1);
        chart.marker.rectTransform.sizeDelta = new Vector2(2, 0);
        chart.marker.gameObject.SetActive(false);

        chart.tipBack = CourseUI.Panel("Tip", root, CourseUI.Hex("1F3D2B", 0.92f));
        chart.tipBack.raycastTarget = false;
        CourseUI.Place(chart.tipBack.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(260, 100));
        chart.tip = CourseUI.Text("Text", chart.tipBack.transform, "", 19, CourseUI.Hex("FBF4DA"));
        chart.tip.richText = true;
        chart.tip.enableWordWrapping = false;
        chart.tip.overflowMode = TextOverflowModes.Overflow;
        CourseUI.Stretch(chart.tip.rectTransform, 12, 12, 8, 8);
        chart.tipBack.gameObject.SetActive(false);

        chart.emptyText = CourseUI.Text("Empty", root, emptyMessage, 24, CourseUI.InkMuted, FontStyles.Italic, TextAlignmentOptions.Center);
        CourseUI.Stretch(chart.emptyText.rectTransform, 40, 40, 40, 40);
        chart.emptyText.gameObject.SetActive(false);
        return chart;
    }

    public RectTransform Rect => (RectTransform)transform;

    // What the chart says when it has no values
    public string EmptyMessage { set { if (emptyText != null) emptyText.text = value; } }

    // labels: one per point in time (dates). invert: small values at the top (places: 1st at the top).
    // fixedMin/fixedMax: the value range; NaN = fitted to the values.
    public void Set(List<string> labels, List<Series> newSeries, bool invertAxis = false, float fixedMin = float.NaN, float fixedMax = float.NaN)
    {
        xLabels = labels ?? new List<string>();
        series = newSeries ?? new List<Series>();
        invert = invertAxis;
        setMin = fixedMin;
        setMax = fixedMax;
        float lo = float.MaxValue, hi = float.MinValue;
        int points = 0;
        foreach (Series s in series)
        {
            foreach (float v in s.values)
            {
                if (float.IsNaN(v)) continue;
                lo = Mathf.Min(lo, v);
                hi = Mathf.Max(hi, v);
                points++;
            }
        }
        if (points == 0) { lo = 0; hi = 1; }
        if (!float.IsNaN(fixedMin)) lo = fixedMin;
        if (!float.IsNaN(fixedMax)) hi = fixedMax;
        // Some room around the lines, and round numbers on the grid
        float span = Mathf.Max(hi - lo, 1f);
        float step = NiceStep(span / 4f);
        min = float.IsNaN(fixedMin) ? Mathf.Floor((lo - span * 0.08f) / step) * step : lo;
        max = float.IsNaN(fixedMax) ? Mathf.Ceil((hi + span * 0.08f) / step) * step : hi;
        if (max - min < step) max = min + step;
        emptyText.gameObject.SetActive(points == 0);
        drawnSize = Rect.rect.size;
        Redraw(step);
        lastHover = -2;
    }

    static float NiceStep(float raw)
    {
        if (raw <= 0) return 1;
        float pow = Mathf.Pow(10, Mathf.Floor(Mathf.Log10(raw)));
        float n = raw / pow;
        float nice = n < 1.5f ? 1 : n < 3 ? 2 : n < 7 ? 5 : 10;
        return Mathf.Max(1f, nice * pow);
    }

    float YOf(float v)   // 0..1 up the plot
    {
        float t = (v - min) / Mathf.Max(0.0001f, max - min);
        return invert ? 1f - t : t;
    }

    float XOf(int i) => xLabels.Count <= 1 ? 0.5f : i / (float)(xLabels.Count - 1);

    void Redraw(float step)
    {
        foreach (Transform child in labelLayer) Destroy(child.gameObject);
        foreach (Transform child in legend) Destroy(child.gameObject);

        // Grid lines and their values on the left
        List<float> grid = new List<float>();
        for (float v = min; v <= max + step * 0.01f; v += step) grid.Add(v);
        foreach (float v in grid)
        {
            float y = YOf(v);
            TextMeshProUGUI label = CourseUI.Text("Y", labelLayer, Format(v), 18, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0, 0);
            label.rectTransform.pivot = new Vector2(1, 0.5f);
            label.rectTransform.sizeDelta = new Vector2(Left - 12, 26);
            label.rectTransform.anchoredPosition = new Vector2(Left - 10, Bottom + y * PlotHeight());
        }

        // A few dates under the chart: the first, the last and some between, never on top of each other
        int count = xLabels.Count;
        int shown = Mathf.Min(count, Mathf.Max(2, Mathf.FloorToInt(PlotWidth() / 150f)));
        HashSet<int> picked = new HashSet<int>();
        for (int k = 0; k < shown; k++) picked.Add(shown <= 1 ? 0 : Mathf.RoundToInt(k * (count - 1) / (float)(shown - 1)));
        foreach (int i in picked)
        {
            if (i < 0 || i >= count) continue;
            TextMeshProUGUI label = CourseUI.Text("X", labelLayer, xLabels[i], 17, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
            label.enableWordWrapping = false;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0, 0);
            label.rectTransform.pivot = new Vector2(0.5f, 1);
            label.rectTransform.sizeDelta = new Vector2(140, 26);
            label.rectTransform.anchoredPosition = new Vector2(Left + XOf(i) * PlotWidth(), Bottom - 8);
        }

        // The legend: a coloured bar and the name of each line (left out with one line, the heading says it)
        if (series.Count > 1)
        {
            foreach (Series s in series)
            {
                RectTransform item = CourseUI.Rect(s.name, legend);
                HorizontalLayoutGroup row = CourseUI.Row(item.gameObject, 8, new RectOffset(0, 0, 0, 0));
                row.childAlignment = TextAnchor.MiddleLeft;
                row.childForceExpandHeight = false;
                Image swatch = CourseUI.Panel("Swatch", item, s.color);
                CourseUI.Size(swatch, 8, 26);
                TextMeshProUGUI name = CourseUI.Text("Name", item, s.name, 20, CourseUI.Ink, FontStyles.Bold);
                name.enableWordWrapping = false;
                name.overflowMode = TextOverflowModes.Overflow;
                CourseUI.Size(name, 30, name.preferredWidth + 4);
            }
        }

        List<ChartLines.Line> drawn = new List<ChartLines.Line>();
        List<float> gridY = new List<float>();
        foreach (float v in grid) gridY.Add(YOf(v));
        foreach (Series s in series)
        {
            List<Vector2> pts = new List<Vector2>();
            for (int i = 0; i < s.values.Count && i < count; i++)
                pts.Add(float.IsNaN(s.values[i]) ? new Vector2(float.NaN, float.NaN) : new Vector2(XOf(i), YOf(s.values[i])));
            drawn.Add(new ChartLines.Line { color = s.color, points = pts });
        }
        lines.Set(drawn, gridY);
    }

    float PlotWidth() => Mathf.Max(10f, Rect.rect.width - Left - Right);
    float PlotHeight() => Mathf.Max(10f, Rect.rect.height - Top - Bottom);

    string Format(float v)
    {
        Series first = series.Count > 0 ? series[0] : null;
        if (first != null && first.format != null) return first.format(v);
        return Mathf.Abs(v - Mathf.Round(v)) < 0.05f ? Mathf.RoundToInt(v).ToString() : v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    }

    // The values of the point in time under the mouse
    void Update()
    {
        int count = xLabels.Count;
        int hover = -1;
        if (count > 0)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(plotRect, Input.mousePosition, cam, out Vector2 local)
                && plotRect.rect.Contains(local))
            {
                float t = (local.x - plotRect.rect.xMin) / Mathf.Max(1f, plotRect.rect.width);
                hover = count <= 1 ? 0 : Mathf.Clamp(Mathf.RoundToInt(t * (count - 1)), 0, count - 1);
            }
        }
        if (hover == lastHover) return;
        lastHover = hover;
        marker.gameObject.SetActive(hover >= 0);
        tipBack.gameObject.SetActive(hover >= 0);
        if (hover < 0) return;

        float x = XOf(hover) * PlotWidth();
        marker.rectTransform.anchoredPosition = new Vector2(x, 0);
        string text = "<b>" + xLabels[hover] + "</b>";
        int rows = 1;
        foreach (Series s in series)
        {
            if (hover >= s.values.Count) continue;
            float v = s.values[hover];
            string value = float.IsNaN(v) ? "-" : s.format != null ? s.format(v) : Format(v);
            text += "\n<color=#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(s.color, Color.white, 0.35f)) + ">" + s.name + "</color>  <b>" + value + "</b>";
            rows++;
        }
        tip.text = text;
        float width = Mathf.Max(150f, tip.preferredWidth + 26f);
        float height = 16f + rows * 25f;
        RectTransform tr = tipBack.rectTransform;
        tr.sizeDelta = new Vector2(width, height);
        float px = Left + x + 14f;
        if (px + width > Rect.rect.width - 4f) px = Left + x - 14f - width;
        tr.anchoredPosition = new Vector2(px, Rect.rect.height - Top - height);
        tipBack.transform.SetAsLastSibling();
    }

    // Drawn again at the end of the frame when the size changed (the labels sit at sizes in pixels), not inside the
    // layout pass that told about it
    bool sizeChanged;
    Vector2 drawnSize;

    void OnRectTransformDimensionsChange() { sizeChanged = true; }

    void LateUpdate()
    {
        if (!sizeChanged) return;
        sizeChanged = false;
        if (Rect.rect.size == drawnSize || lines == null) return;
        Set(xLabels, series, invert, setMin, setMax);
    }
}

// The grid lines and the lines of a LineChart, drawn as one mesh. Points are 0..1 across the plot; NaN breaks a line.
public class ChartLines : MaskableGraphic
{
    public class Line
    {
        public Color color;
        public List<Vector2> points;
    }

    List<Line> lineList = new List<Line>();
    List<float> grid = new List<float>();
    const float Width = 4f;
    const float Dot = 5f;

    public void Set(List<Line> newLines, List<float> gridY)
    {
        lineList = newLines ?? new List<Line>();
        grid = gridY ?? new List<float>();
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        Vector2 At(Vector2 p) => new Vector2(r.xMin + p.x * r.width, r.yMin + p.y * r.height);
        Color gridColor = CourseUI.Hex("1F3D2B", 0.12f);
        foreach (float y in grid)
        {
            float py = r.yMin + y * r.height;
            Quad(vh, new Vector2(r.xMin, py), new Vector2(r.xMax, py), 1.5f, gridColor);
        }
        foreach (Line line in lineList)
        {
            if (line.points == null) continue;
            int count = line.points.Count;
            for (int i = 0; i + 1 < count; i++)
            {
                Vector2 a = line.points[i], b = line.points[i + 1];
                if (float.IsNaN(a.x) || float.IsNaN(b.x)) continue;
                Quad(vh, At(a), At(b), Width, line.color);
            }
            // A dot on every point (a lone point between two gaps still shows); smaller when there are many
            float dot = count > 60 ? Width * 0.6f : Dot;
            for (int i = 0; i < count; i++)
            {
                if (float.IsNaN(line.points[i].x)) continue;
                Disc(vh, At(line.points[i]), dot, line.color);
            }
        }
    }

    static void Quad(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 d = b - a;
        if (d.sqrMagnitude < 1e-6f) return;
        Vector2 n = new Vector2(-d.y, d.x).normalized * (width / 2f);
        UIVertex v = UIVertex.simpleVert;
        v.color = color;
        int start = vh.currentVertCount;
        v.position = a - n; vh.AddVert(v);
        v.position = a + n; vh.AddVert(v);
        v.position = b + n; vh.AddVert(v);
        v.position = b - n; vh.AddVert(v);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start + 2, start + 3, start);
    }

    static void Disc(VertexHelper vh, Vector2 c, float radius, Color color)
    {
        const int Steps = 12;
        UIVertex v = UIVertex.simpleVert;
        v.color = color;
        int centre = vh.currentVertCount;
        v.position = c; vh.AddVert(v);
        for (int k = 0; k <= Steps; k++)
        {
            float angle = k * Mathf.PI * 2f / Steps;
            v.position = c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            vh.AddVert(v);
            if (k > 0) vh.AddTriangle(centre, centre + k, centre + k + 1);
        }
    }
}
