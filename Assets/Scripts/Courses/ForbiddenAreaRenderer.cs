using System.Collections.Generic;
using UnityEngine;

// Draws out-of-bounds areas the way ISOM / ISSprOM symbol 709 looks: purple vertical stripes (north-south on the map)
// with a purple border line, and uncrossable boundaries (707) as a thick purple line. In the course builder it also
// draws the corner handles and the area or line being drawn.
public class ForbiddenAreaRenderer : MonoBehaviour
{
    public int sortingOrder = 3;    // under the course lines (CourseRenderer uses 4)

    // Stripe and border sizes on the ground: 0.25 mm lines 0.8 mm apart, at 1:15 000 in the forest and 1:4 000 in sprint
    float stripeSpacingMeters = 12f;
    float stripeShare = 0.32f;
    float borderMeters = 3.5f;
    float boundaryMeters = 7f;      // 707: 0.7 mm, a little thinner than that at 1:15 000 so it does not hide the map

    readonly List<GameObject> parts = new List<GameObject>();
    readonly List<LineRenderer> lines = new List<LineRenderer>();
    readonly List<float> lineMeters = new List<float>();     // each line's real width on the ground
    readonly List<Transform> handles = new List<Transform>();
    readonly List<float> handleScale = new List<float>();
    readonly List<Mesh> meshes = new List<Mesh>();
    LineRenderer rubber;      // from the last corner of the area being drawn to the mouse
    Vector3? rubberEnd;
    List<Vector3> openCorners;

    static Material material;
    static Texture2D stripes;
    static Mesh quad;

    public static readonly Color Purple = CourseRenderer.CoursePurple;

    float Scale => MapDefinition.WorldUnitsPerMeter;

    public void SetMapType(bool sprint)
    {
        stripeSpacingMeters = sprint ? 3.2f : 12f;
        borderMeters = sprint ? 1f : 3.5f;
        boundaryMeters = sprint ? 2.4f : 7f;
    }

    public void Clear()
    {
        foreach (GameObject part in parts) if (part != null) Destroy(part);
        parts.Clear();
        foreach (Mesh mesh in meshes) if (mesh != null) Destroy(mesh);
        meshes.Clear();
        lines.Clear();
        lineMeters.Clear();
        handles.Clear();
        handleScale.Clear();
        rubber = null;
        openCorners = null;
    }

    // The areas of a course, in the competition
    public void Draw(MapDefinition map, List<ForbiddenArea> areas)
    {
        Clear();
        if (map == null || areas == null) return;
        SetMapType(map.IsSprint);
        foreach (ForbiddenArea area in areas)
        {
            if (area == null || !area.IsValid) continue;
            List<Vector3> corners = area.corners.ConvertAll(c => map.ToWorld(c));
            if (area.line) AddBoundary(corners, false, Purple);
            else AddArea(corners, false, Purple);
        }
    }

    // The course builder: finished areas and boundary lines, the one being drawn (open), and handles on every corner
    // when editing. badArea counts the areas first, then the lines.
    public void DrawEditing(List<List<Vector3>> areas, List<List<Vector3>> boundaries, List<Vector3> open, bool openIsLine, bool showHandles, int badArea = -1)
    {
        Clear();
        for (int a = 0; a < areas.Count; a++)
        {
            AddArea(areas[a], showHandles, a == badArea ? CourseRenderer.ErrorRed : Purple);
        }
        for (int b = 0; b < boundaries.Count; b++)
        {
            AddBoundary(boundaries[b], showHandles, areas.Count + b == badArea ? CourseRenderer.ErrorRed : Purple);
        }
        if (open != null && open.Count > 0)
        {
            openCorners = open;
            if (open.Count >= 2)
            {
                LineRenderer line = NewLine(openIsLine ? "Line being drawn" : "Area being drawn", false, openIsLine ? boundaryMeters : borderMeters);
                line.positionCount = open.Count;
                for (int k = 0; k < open.Count; k++) line.SetPosition(k, Flat(open[k], -1.15f));
            }
            rubber = NewLine("To the mouse", false, openIsLine ? boundaryMeters : borderMeters);
            rubber.startColor = rubber.endColor = new Color(Purple.r, Purple.g, Purple.b, 0.55f);
            rubber.positionCount = 2;
            UpdateRubber();
            // An area is closed by clicking its first corner, so that one is bigger; a line has no such corner
            for (int k = 0; k < open.Count; k++) AddHandle(open[k], !openIsLine && k == 0 && open.Count >= 3);
        }
    }

    public void SetRubberEnd(Vector3? end)
    {
        rubberEnd = end;
        UpdateRubber();
    }

    void UpdateRubber()
    {
        if (rubber == null || openCorners == null || openCorners.Count == 0) return;
        bool show = rubberEnd.HasValue;
        rubber.enabled = show;
        if (!show) return;
        rubber.SetPosition(0, Flat(openCorners[openCorners.Count - 1], -1.15f));
        rubber.SetPosition(1, Flat(rubberEnd.Value, -1.15f));
    }

    void AddArea(List<Vector3> corners, bool showHandles, Color color)
    {
        if (corners.Count < 3) return;
        // The stripes: a mesh of the area whose texture repeats along x, so the stripes run north-south
        GameObject fill = new GameObject("Out-of-bounds area");
        fill.transform.SetParent(transform, false);
        Mesh mesh = new Mesh { name = "Out-of-bounds area" };
        Vector3[] vertices = new Vector3[corners.Count];
        Vector2[] uv = new Vector2[corners.Count];
        float spacing = stripeSpacingMeters * Scale;
        for (int k = 0; k < corners.Count; k++)
        {
            vertices[k] = Flat(corners[k], -1.1f);
            uv[k] = new Vector2(corners[k].x / spacing, 0.5f);
        }
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.SetTriangles(ForbiddenAreas.Triangulate(corners), 0);
        mesh.RecalculateBounds();
        fill.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer meshRenderer = fill.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = StripeMaterial(stripeShare);
        meshRenderer.sortingOrder = sortingOrder;
        // Sprites/Default tints with the vertex colour
        Color[] colors = new Color[corners.Count];
        for (int k = 0; k < colors.Length; k++) colors[k] = color;
        mesh.colors = colors;
        meshes.Add(mesh);
        parts.Add(fill);

        LineRenderer border = NewLine("Out-of-bounds border", true, borderMeters);
        border.startColor = border.endColor = color;
        border.positionCount = corners.Count;
        for (int k = 0; k < corners.Count; k++) border.SetPosition(k, Flat(corners[k], -1.12f));

        if (showHandles) foreach (Vector3 corner in corners) AddHandle(corner, false);
    }

    // An uncrossable boundary: one thick purple line through the corners, its ends not joined
    void AddBoundary(List<Vector3> corners, bool showHandles, Color color)
    {
        if (corners.Count < 2) return;
        LineRenderer line = NewLine("Uncrossable boundary", false, boundaryMeters);
        line.startColor = line.endColor = color;
        line.numCapVertices = 0;
        line.positionCount = corners.Count;
        for (int k = 0; k < corners.Count; k++) line.SetPosition(k, Flat(corners[k], -1.12f));
        if (showHandles) foreach (Vector3 corner in corners) AddHandle(corner, false);
    }

    // A small square on a corner that can be dragged; the first corner of an area being drawn is larger,
    // since clicking it closes the area
    void AddHandle(Vector3 position, bool closeHere)
    {
        GameObject outer = NewQuad("Corner", Purple, sortingOrder + 2);
        outer.transform.position = Flat(position, -1.3f);
        GameObject inner = NewQuad("Inside", closeHere ? new Color(1f, 0.85f, 0.3f) : Color.white, sortingOrder + 3);
        inner.transform.SetParent(outer.transform, false);
        inner.transform.localScale = new Vector3(0.55f, 0.55f, 1f);
        inner.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        handles.Add(outer.transform);
        handleScale.Add(closeHere ? 1.5f : 1f);
        outer.transform.localScale = Vector3.one * HandleSize * handleScale[handleScale.Count - 1];
    }

    // Handles and lines keep the same size on the screen when zooming, but never get thinner than the real symbol
    public float HandleSize
    {
        get
        {
            Camera camera = Camera.main;
            return camera != null ? camera.orthographicSize * 0.022f : 0.1f;
        }
    }

    void LateUpdate()
    {
        Camera camera = Camera.main;
        if (camera == null) return;
        // A border never gets thinner on the screen than a hairline; a boundary keeps its thickness over the border's
        float hairline = camera.orthographicSize * 0.004f;
        for (int k = 0; k < lines.Count; k++)
        {
            if (lines[k] == null) continue;
            float meters = lineMeters[k];
            lines[k].widthMultiplier = Mathf.Max(meters * Scale, hairline * (meters > borderMeters ? 2f : 1f));
        }
        float size = HandleSize;
        for (int k = 0; k < handles.Count; k++)
        {
            if (handles[k] != null) handles[k].localScale = new Vector3(size * handleScale[k], size * handleScale[k], 1f);
        }
    }

    static Vector3 Flat(Vector3 p, float z) => new Vector3(p.x, p.y, z);

    LineRenderer NewLine(string label, bool loop, float meters)
    {
        GameObject go = new GameObject(label);
        go.transform.SetParent(transform, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = PlainMaterial();
        line.useWorldSpace = true;
        line.loop = loop;
        line.widthMultiplier = meters * Scale;
        line.startColor = line.endColor = Purple;
        line.numCornerVertices = 2;
        line.numCapVertices = 2;
        line.sortingOrder = sortingOrder + 1;
        parts.Add(go);
        lines.Add(line);
        lineMeters.Add(meters);
        return line;
    }

    GameObject NewQuad(string label, Color color, int order)
    {
        if (quad == null)
        {
            quad = new Mesh { name = "Handle" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
            quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateBounds();
        }
        GameObject go = new GameObject(label);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = PlainMaterial();
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        block.SetColor("_Color", color);
        meshRenderer.SetPropertyBlock(block);
        meshRenderer.sortingOrder = order;
        parts.Add(go);
        return go;
    }

    static Material plain;

    static Material PlainMaterial()
    {
        if (plain == null) plain = new Material(Shader.Find("Sprites/Default"));
        return plain;
    }

    // One row of pixels, purple for the stripe and clear between, repeated across the area. Mipmaps turn the
    // stripes into an even purple tint when zoomed far out, instead of flickering.
    static Material StripeMaterial(float share)
    {
        if (material != null) return material;
        const int width = 64;
        stripes = new Texture2D(width, 4, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, name = "Out-of-bounds stripes" };
        stripes.anisoLevel = 4;
        Color[] pixels = new Color[width * 4];
        int on = Mathf.Max(1, Mathf.RoundToInt(width * share));
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = x < on ? Color.white : new Color(1f, 1f, 1f, 0f);
        stripes.SetPixels(pixels);
        stripes.Apply(true);
        material = new Material(Shader.Find("Sprites/Default")) { mainTexture = stripes, name = "Out-of-bounds stripes" };
        return material;
    }
}
