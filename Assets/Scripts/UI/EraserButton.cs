using UnityEngine;
using UnityEngine.UI;

// The button that rubs out the route being drawn (the scenes' "UndoButton"): it gets a picture of an eraser, and is
// only there while the player draws.
public class EraserButton : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        var go = new GameObject("Eraser button");
        DontDestroyOnLoad(go);
        go.AddComponent<EraserButton>();
    }

    static Sprite eraser;
    GameObject button;
    ManualPathDrawer drawer;
    float nextLook;

    void Update()
    {
        if (button == null)
        {
            if (Time.unscaledTime < nextLook) return;
            nextLook = Time.unscaledTime + 0.5f;
            button = GameObject.Find("UndoButton");
            if (button == null) return;
            Dress(button);
        }
        // These modes hide the button themselves
        if (RouteChoiceGame.On || RouteAnalysis.On) return;
        if (drawer == null)
        {
            NetworkPlayer local = NetworkPlayer.Local;
            AgentMovement agent = local != null ? local.GetComponent<AgentMovement>() : null;
            drawer = agent != null ? agent.manualPathDrawer : null;
            if (drawer == null) return;
        }
        bool show = drawer.timeToDraw;
        if (button.activeSelf != show) button.SetActive(show);
    }

    static void Dress(GameObject button)
    {
        Image image = button.GetComponent<Image>();
        if (image == null) return;
        if (eraser == null) eraser = Sprite.Create(Draw(), new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        image.sprite = eraser;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = Color.white;
        // The old arrow or text on the button
        foreach (Transform child in button.transform) child.gameObject.SetActive(false);
    }

    const int Size = 128;

    // An eraser lying at a slant: a pink rubber end, a blue sleeve, a dark edge
    static Texture2D Draw()
    {
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Eraser", filterMode = FilterMode.Bilinear };
        var pixels = new Color32[Size * Size];
        Color32 edge = new Color32(31, 61, 43, 255), rubber = new Color32(242, 150, 150, 255), sleeve = new Color32(63, 111, 181, 255),
                stripe = new Color32(240, 240, 235, 255);
        float angle = 35f * Mathf.Deg2Rad, cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
        const float HalfLong = 50f, HalfShort = 24f, Corner = 8f, Line = 5f;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float px = x - Size / 2f + 0.5f, py = y - Size / 2f + 0.5f;
                float u = px * cos + py * sin, v = -px * sin + py * cos;
                // Distance to a rounded box
                float qx = Mathf.Abs(u) - (HalfLong - Corner), qy = Mathf.Abs(v) - (HalfShort - Corner);
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - Corner;
                Color32 c = u < -12f ? rubber : u < -6f ? stripe : sleeve;
                if (outside > -Line || Mathf.Abs(u + 12f) < Line * 0.4f) c = edge;
                float alpha = Mathf.Clamp01(0.5f - outside);
                c.a = (byte)(alpha * 255f);
                pixels[y * Size + x] = c;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
