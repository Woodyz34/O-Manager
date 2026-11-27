using UnityEngine;
using UnityEngine.UI;

// A control punched in the 3D race: the edges of the screen glow softly orange for a moment, like a pulse, and the
// middle of the view stays free (2026-11-27, Anders chose idea D over a pop-up). Also for the runner watched.
public class RunPunchGlow : MonoBehaviour
{
    const float Rise = 0.1f;      // seconds up to the brightest
    const float Fade = 0.9f;      // seconds back to nothing
    const float Peak = 0.75f;     // the brightest alpha at the very edge
    static readonly Color Orange = new Color(0.95f, 0.42f, 0.11f);
    static Texture2D texture;

    RawImage image;
    float since = -1f;

    public static RunPunchGlow Create(Transform parent)
    {
        var go = new GameObject("Punch glow", typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        RunPunchGlow glow = go.AddComponent<RunPunchGlow>();
        glow.image = go.GetComponent<RawImage>();
        glow.image.texture = Texture();
        glow.image.raycastTarget = false;
        glow.image.color = new Color(Orange.r, Orange.g, Orange.b, 0f);
        glow.image.enabled = false;
        return glow;
    }

    public void Pulse()
    {
        since = 0f;
        image.enabled = true;
    }

    void Update()
    {
        if (since < 0f) return;
        since += Time.unscaledDeltaTime;
        float a = since < Rise ? since / Rise : 1f - (since - Rise) / Fade;
        if (a <= 0f)
        {
            since = -1f;
            image.enabled = false;
            return;
        }
        a = a * a * (3f - 2f * a);
        image.color = new Color(Orange.r, Orange.g, Orange.b, Peak * a);
    }

    // White, see-through in the middle and thicker towards the edges (the colour comes from the image)
    static Texture2D Texture()
    {
        if (texture != null) return texture;
        const int w = 128, h = 72;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Distance to the nearest edge, as a share of the short side
                float dx = Mathf.Min(x + 0.5f, w - x - 0.5f) / h, dy = Mathf.Min(y + 0.5f, h - y - 0.5f) / h;
                float d = Mathf.Min(dx, dy);
                float t = Mathf.Clamp01(1f - d / 0.22f);
                // Rounded corners: a little more glow where two edges meet
                float corner = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / 0.3f);
                float alpha = Mathf.Clamp01(t * t + 0.4f * corner * corner);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Punch glow", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        texture.SetPixels32(px);
        texture.Apply();
        return texture;
    }
}
