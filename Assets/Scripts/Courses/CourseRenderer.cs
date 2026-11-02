using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Draws a course on top of the map picture the way it looks on a printed orienteering map:
// a triangle for the start, numbered circles for the controls, a double circle for the finish
// and purple lines between them. Every mark is its own object, so the same map picture can be used for any course.
public class CourseRenderer : MonoBehaviour
{
    public static readonly Color CoursePurple = new Color(0.50f, 0.04f, 0.58f, 1f);
    public static readonly Color ErrorRed = new Color(0.86f, 0.12f, 0.12f, 1f);

    // Sizes in metres on the ground, matched to the courses printed on the old Illern map pictures
    public float controlRadiusMeters = 37f;
    public float startSideMeters = 90f;
    public float finishInnerRadiusMeters = 28f;
    public float finishOuterRadiusMeters = 40f;
    public float lineWidthMeters = 4.5f;
    public float numberSizeMeters = 45f;
    public int sortingOrder = 4;

    readonly List<GameObject> marks = new List<GameObject>();
    static Material lineMaterial;

    float Scale => MapDefinition.WorldUnitsPerMeter;

    public void Clear()
    {
        foreach (GameObject mark in marks)
        {
            if (mark != null) Destroy(mark);
        }
        marks.Clear();
    }

    // errorIndex counts start = 0, controls 1..n, finish = n + 1. The mark at that index is drawn red.
    public void Draw(MapDefinition map, CourseData course, int errorIndex = -1)
    {
        Clear();
        if (map == null || course == null)
        {
            return;
        }
        List<Vector3> points = new List<Vector3>();
        foreach (CoursePoint point in course.AllPoints())
        {
            Vector3 world = map.ToWorld(point);
            world.z = -1.2f;
            points.Add(world);
        }
        DrawPoints(points, true, true, errorIndex);
    }

    // Draws marks at world positions, in running order. While a course is being laid it may not have
    // a start or a finish yet; then the first or last point is a control.
    public void DrawPoints(List<Vector3> points, bool hasStart, bool hasFinish, int errorIndex = -1)
    {
        Clear();
        if (points.Count == 0)
        {
            return;
        }
        int finishIndex = hasFinish ? points.Count - 1 : -1;

        for (int i = 0; i < points.Count; i++)
        {
            Color color = i == errorIndex ? ErrorRed : CoursePurple;
            if (i == 0 && hasStart)
            {
                // The triangle points towards the first control
                Vector3 toward = points.Count > 1 ? points[1] - points[0] : Vector3.up;
                AddStart(points[0], toward, color);
            }
            else if (i == finishIndex)
            {
                AddCircle(points[i], finishInnerRadiusMeters * Scale, color, "Finish inner");
                AddCircle(points[i], finishOuterRadiusMeters * Scale, color, "Finish outer");
            }
            else
            {
                int number = hasStart ? i : i + 1;
                AddCircle(points[i], controlRadiusMeters * Scale, color, "Control " + number);
                AddNumber(points[i], number.ToString(), color);
            }

            if (i > 0)
            {
                float fromGap = (i - 1 == 0 && hasStart ? startSideMeters * 0.6f : controlRadiusMeters) * Scale;
                float toGap = (i == finishIndex ? finishOuterRadiusMeters : controlRadiusMeters) * Scale;
                AddLeg(points[i - 1], points[i], fromGap, toGap, i == errorIndex ? ErrorRed : CoursePurple);
            }
        }
    }

    // Draws a relay course with its forks. slots[0] is the start, slots[1..n] the controls with all their variants
    // (variant 0 first) and the last slot the finish. A forked control's variants are labelled 3a, 3b, ... and get
    // lines from every variant before them and to every variant after them.
    // errorVariant -1 marks every variant of errorSlot red.
    public void DrawSlots(List<List<Vector3>> slots, bool hasStart, bool hasFinish, int errorSlot = -1, int errorVariant = -1)
    {
        Clear();
        if (slots.Count == 0)
        {
            return;
        }
        int finishSlot = hasFinish ? slots.Count - 1 : -1;
        for (int s = 0; s < slots.Count; s++)
        {
            List<Vector3> variants = slots[s];
            for (int v = 0; v < variants.Count; v++)
            {
                bool bad = s == errorSlot && (errorVariant < 0 || errorVariant == v);
                Color color = bad ? ErrorRed : CoursePurple;
                if (s == 0 && hasStart)
                {
                    Vector3 toward = slots.Count > 1 ? Average(slots[1]) - variants[v] : Vector3.up;
                    AddStart(variants[v], toward, color);
                }
                else if (s == finishSlot)
                {
                    AddCircle(variants[v], finishInnerRadiusMeters * Scale, color, "Finish inner");
                    AddCircle(variants[v], finishOuterRadiusMeters * Scale, color, "Finish outer");
                }
                else
                {
                    int number = hasStart ? s : s + 1;
                    string label = variants.Count > 1 ? number.ToString() + (char)('a' + v) : number.ToString();
                    AddCircle(variants[v], controlRadiusMeters * Scale, color, "Control " + label);
                    AddNumber(variants[v], label, color);
                }

                if (s > 0)
                {
                    foreach (Vector3 from in slots[s - 1])
                    {
                        float fromGap = (s - 1 == 0 && hasStart ? startSideMeters * 0.6f : controlRadiusMeters) * Scale;
                        float toGap = (s == finishSlot ? finishOuterRadiusMeters : controlRadiusMeters) * Scale;
                        AddLeg(from, variants[v], fromGap, toGap, bad ? ErrorRed : CoursePurple);
                    }
                }
            }
        }
    }

    // ---- Any arrangement of marks and lines (relay courses with forks, and the faded forks of other legs) ----

    public enum MarkKind { Start, Control, Finish }

    public struct Mark
    {
        public Vector3 position;
        public MarkKind kind;
        public string label;
        public Color color;
        public Vector3 toward;     // start only: where the triangle points
    }

    public struct Line
    {
        public Vector3 from, to;
        public MarkKind fromKind, toKind;
        public Color color;
    }

    public void DrawMarks(List<Mark> marks, List<Line> lines)
    {
        Clear();
        foreach (Mark mark in marks)
        {
            Vector3 position = mark.position;
            position.z = -1.2f;
            switch (mark.kind)
            {
                case MarkKind.Start:
                    AddStart(position, mark.toward.sqrMagnitude > 0f ? mark.toward : Vector3.up, mark.color);
                    break;
                case MarkKind.Finish:
                    AddCircle(position, finishInnerRadiusMeters * Scale, mark.color, "Finish inner");
                    AddCircle(position, finishOuterRadiusMeters * Scale, mark.color, "Finish outer");
                    break;
                default:
                    AddCircle(position, controlRadiusMeters * Scale, mark.color, "Control " + mark.label);
                    if (!string.IsNullOrEmpty(mark.label)) AddNumber(position, mark.label, mark.color);
                    break;
            }
        }
        foreach (Line line in lines)
        {
            float fromGap = (line.fromKind == MarkKind.Start ? startSideMeters * 0.6f : line.fromKind == MarkKind.Finish ? finishOuterRadiusMeters : controlRadiusMeters) * Scale;
            float toGap = (line.toKind == MarkKind.Finish ? finishOuterRadiusMeters : line.toKind == MarkKind.Start ? startSideMeters * 0.6f : controlRadiusMeters) * Scale;
            Vector3 from = line.from; from.z = -1.2f;
            Vector3 to = line.to; to.z = -1.2f;
            AddLeg(from, to, fromGap, toGap, line.color);
        }
    }

    public static readonly Color ShadowPurple = new Color(0.66f, 0.13f, 0.75f, 0.42f);

    // Faded control circles with a label, on top of what is drawn already: forked controls that are not run now
    // but still have to be run on a later leg
    public void AddShadows(List<Vector3> positions, List<string> labels)
    {
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 position = positions[i];
            position.z = -1.15f;
            AddCircle(position, controlRadiusMeters * Scale, ShadowPurple, "Shadow " + labels[i]);
            AddNumber(position, labels[i], ShadowPurple);
        }
    }

    static Vector3 Average(List<Vector3> points)
    {
        Vector3 sum = Vector3.zero;
        foreach (Vector3 point in points) sum += point;
        return points.Count > 0 ? sum / points.Count : Vector3.zero;
    }

    void AddStart(Vector3 center, Vector3 toward, Color color)
    {
        float side = startSideMeters * Scale;
        float radius = side / Mathf.Sqrt(3f);
        float angle = Mathf.Atan2(toward.y, toward.x);
        Vector3[] corners = new Vector3[3];
        for (int k = 0; k < 3; k++)
        {
            float a = angle + k * Mathf.PI * 2f / 3f;
            corners[k] = center + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
        }
        LineRenderer line = NewLine("Start", color, true);
        line.positionCount = 3;
        line.SetPositions(corners);
    }

    void AddCircle(Vector3 center, float radius, Color color, string label)
    {
        const int segments = 48;
        LineRenderer line = NewLine(label, color, true);
        line.positionCount = segments;
        for (int k = 0; k < segments; k++)
        {
            float a = k * Mathf.PI * 2f / segments;
            line.SetPosition(k, center + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius);
        }
    }

    void AddLeg(Vector3 from, Vector3 to, float fromGap, float toGap, Color color)
    {
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length <= fromGap + toGap)
        {
            return; // the marks overlap, so there is no line to draw between them
        }
        Vector3 direction = delta / length;
        LineRenderer line = NewLine("Leg", color, false);
        line.positionCount = 2;
        line.SetPosition(0, from + direction * fromGap);
        line.SetPosition(1, to - direction * toGap);
    }

    void AddNumber(Vector3 center, string number, Color color)
    {
        GameObject go = new GameObject("Number " + number);
        go.transform.SetParent(transform, false);
        // Place the number up and to the right of the circle, as course setters usually do
        float offset = (controlRadiusMeters + numberSizeMeters * 0.45f) * Scale;
        go.transform.position = center + new Vector3(offset, offset, 0f);
        TextMeshPro text = go.AddComponent<TextMeshPro>();
        text.text = number;
        text.fontSize = numberSizeMeters * Scale * 10f;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.rectTransform.sizeDelta = new Vector2(numberSizeMeters * Scale * 3f, numberSizeMeters * Scale * 1.5f);
        text.sortingOrder = sortingOrder;
        marks.Add(go);
    }

    LineRenderer NewLine(string label, Color color, bool loop)
    {
        GameObject go = new GameObject(label);
        go.transform.SetParent(transform, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        if (lineMaterial == null)
        {
            lineMaterial = new Material(Shader.Find("Sprites/Default"));
        }
        line.sharedMaterial = lineMaterial;
        line.useWorldSpace = true;
        line.loop = loop;
        line.widthMultiplier = lineWidthMeters * Scale;
        line.startColor = color;
        line.endColor = color;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.sortingOrder = sortingOrder;
        marks.Add(go);
        return line;
    }
}
