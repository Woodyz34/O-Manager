using UnityEngine;

// "Done!" at a control when the line has been drawn all the way to it (2026-11-15: it was hard to tell that a
// stretch was finished; the line just disappeared). A green ring pops out of the control and fades, and a green
// tick stays a moment over the control.
public class DrawnMark : MonoBehaviour
{
    static readonly Color Green = new Color(0.16f, 0.66f, 0.28f, 1f);
    const float Seconds = 1.1f;

    static Material plain;
    LineRenderer ring, tick;
    float radius;
    float born;

    // circleRadius: the control circle's radius in world units. The mark is sized by the circle, not by the zoom
    // (2026-11-16: in a relay the camera jumps to the next stretch at another zoom, and the tick came out tiny or huge).
    public static void Show(Vector3 at, Material material, int sortingOrder, float circleRadius)
    {
        DrawnMark mark = new GameObject("Drawn mark").AddComponent<DrawnMark>();
        mark.transform.position = new Vector3(at.x, at.y, -1.6f);
        mark.radius = circleRadius;
        mark.born = Time.unscaledTime;
        mark.ring = Line(mark.transform, "Ring", material, sortingOrder, circleRadius * 0.14f, 41, true);
        mark.tick = Line(mark.transform, "Tick", material, sortingOrder + 1, circleRadius * 0.24f, 3, false);
        // About as wide as the circle
        float r = mark.radius * 0.95f;
        mark.tick.SetPosition(0, new Vector3(-r * 0.55f, 0f, 0f));
        mark.tick.SetPosition(1, new Vector3(-r * 0.1f, -r * 0.45f, 0f));
        mark.tick.SetPosition(2, new Vector3(r * 0.65f, r * 0.55f, 0f));
        mark.FaceCamera();
        mark.Animate(0f);
        Destroy(mark.gameObject, Seconds + 0.1f);
    }

    static LineRenderer Line(Transform parent, string name, Material material, int order, float width, int points, bool loop)
    {
        LineRenderer line = new GameObject(name).AddComponent<LineRenderer>();
        line.transform.SetParent(parent, false);
        line.useWorldSpace = false;
        // Its own plain material: the colour comes from the line's vertex colours
        if (plain == null) plain = new Material(Shader.Find("Sprites/Default"));
        line.sharedMaterial = plain;
        line.widthMultiplier = width;
        line.positionCount = points;
        line.loop = loop;
        line.numCapVertices = 4;
        line.numCornerVertices = 4;
        line.sortingOrder = order;
        line.startColor = line.endColor = Green;
        return line;
    }

    // The map can be turned: the tick turns with the camera so it always stands the right way up (2026-11-16)
    void FaceCamera()
    {
        Camera cam = Camera.main;
        if (cam != null) transform.rotation = Quaternion.Euler(0f, 0f, cam.transform.eulerAngles.z);
    }

    void Update()
    {
        FaceCamera();
        Animate((Time.unscaledTime - born) / Seconds);
    }

    void Animate(float t)
    {
        t = Mathf.Clamp01(t);
        // The ring grows out of the control and fades
        float grow = 1f + 0.9f * (1f - (1f - t) * (1f - t));
        float r = radius * grow;
        for (int i = 0; i < ring.positionCount; i++)
        {
            float a = i / (float)ring.positionCount * Mathf.PI * 2f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
        }
        Color ringColor = Green;
        ringColor.a = 1f - t;
        ring.startColor = ring.endColor = ringColor;
        // The tick pops in, stays, then fades over the last third
        float pop = t < 0.15f ? Mathf.Lerp(0.4f, 1.15f, t / 0.15f) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((t - 0.15f) / 0.15f));
        tick.transform.localScale = Vector3.one * pop;
        Color tickColor = Green;
        tickColor.a = t < 0.66f ? 1f : 1f - (t - 0.66f) / 0.34f;
        tick.startColor = tick.endColor = tickColor;
    }
}
