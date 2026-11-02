using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// A round colour wheel like in most games: the angle is the hue, the distance from the middle is how
// strong the colour is (white in the middle). Click or drag on it to pick. Brightness is set elsewhere;
// the wheel darkens with it so it always shows the colours you can get.
public class ColorWheel : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    const int Size = 512;

    public Action<float, float> Picked;    // hue, saturation (0..1)

    RawImage wheel;
    RectTransform marker;
    Image markerFill;
    static Texture2D texture;

    public static ColorWheel Create(Transform parent, float size)
    {
        RectTransform root = CourseUI.Rect("Colour wheel", parent);
        root.sizeDelta = new Vector2(size, size);
        ColorWheel colorWheel = root.gameObject.AddComponent<ColorWheel>();
        colorWheel.wheel = root.gameObject.AddComponent<RawImage>();
        colorWheel.wheel.texture = Texture();

        // A ring that shows the picked colour, with a dark and a light edge so it is seen on any colour
        Image outer = CourseUI.Panel("Marker", root, Color.white);
        outer.raycastTarget = false;
        if (CourseUI.Skin != null && CourseUI.Skin.pill != null) outer.sprite = CourseUI.Skin.pill;
        colorWheel.marker = outer.rectTransform;
        colorWheel.marker.sizeDelta = new Vector2(40, 40);
        Outline edge = outer.gameObject.AddComponent<Outline>();
        edge.effectColor = new Color(0f, 0f, 0f, 0.6f);
        edge.effectDistance = new Vector2(2, -2);
        colorWheel.markerFill = CourseUI.Panel("Fill", outer.transform, Color.white);
        colorWheel.markerFill.raycastTarget = false;
        if (CourseUI.Skin != null && CourseUI.Skin.pill != null) colorWheel.markerFill.sprite = CourseUI.Skin.pill;
        CourseUI.Stretch(colorWheel.markerFill.rectTransform, 6, 6, 6, 6);
        return colorWheel;
    }

    // Moves the ring to the colour and darkens the wheel to the brightness
    public void Show(float h, float s, float v, Color color)
    {
        float radius = ((RectTransform)transform).rect.width / 2f;
        float angle = h * Mathf.PI * 2f;
        marker.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * s * radius;
        markerFill.color = color;
        wheel.color = new Color(v, v, v, 1f);
    }

    public void OnPointerDown(PointerEventData eventData) { Pick(eventData); }
    public void OnDrag(PointerEventData eventData) { Pick(eventData); }

    void Pick(PointerEventData eventData)
    {
        RectTransform rect = (RectTransform)transform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out Vector2 local)) return;
        local -= rect.rect.center;
        float radius = rect.rect.width / 2f;
        float saturation = Mathf.Clamp01(local.magnitude / radius);
        float hue = Mathf.Atan2(local.y, local.x) / (Mathf.PI * 2f);
        if (hue < 0f) hue += 1f;
        Picked?.Invoke(hue, saturation);
    }

    // Made once: hue around, white in the middle, a soft edge outside the circle
    static Texture2D Texture()
    {
        if (texture != null) return texture;
        texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[Size * Size];
        float half = Size / 2f;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float dx = x + 0.5f - half;
                float dy = y + 0.5f - half;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(half - distance);   // one pixel of smoothing at the edge
                float hue = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f);
                if (hue < 0f) hue += 1f;
                Color color = Color.HSVToRGB(hue, Mathf.Clamp01(distance / half), 1f);
                color.a = alpha;
                pixels[y * Size + x] = color;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
}
