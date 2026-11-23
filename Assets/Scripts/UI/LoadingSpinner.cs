using UnityEngine;
using UnityEngine.UI;

// A ring of dots that chases round and round, so a waiting page is never completely still and nobody thinks the game
// has frozen (2026-11-23, Anders). Put one on any loading or waiting page with LoadingSpinner.Add.
public class LoadingSpinner : MonoBehaviour
{
    const int Dots = 12;
    Image[] images;
    Color color;

    // anchor is where on the parent the middle of the ring sits, position is the offset from there
    public static LoadingSpinner Add(Transform parent, Vector2 anchor, Vector2 position, float radius = 36f, float dotSize = 12f, Color? colour = null)
    {
        RectTransform root = CourseUI.Rect("Spinner", parent);
        CourseUI.Place(root, anchor, new Vector2(0.5f, 0.5f), position, new Vector2(radius * 2f + dotSize, radius * 2f + dotSize));
        LoadingSpinner spinner = root.gameObject.AddComponent<LoadingSpinner>();
        spinner.color = colour ?? CourseUI.Accent;
        spinner.images = new Image[Dots];
        for (int i = 0; i < Dots; i++)
        {
            Image dot = CourseUI.Panel("Dot", root, spinner.color);
            dot.raycastTarget = false;
            float angle = i * Mathf.PI * 2f / Dots;
            CourseUI.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius), new Vector2(dotSize, dotSize));
            spinner.images[i] = dot;
        }
        return spinner;
    }

    void Update()
    {
        int head = Mathf.FloorToInt(Time.unscaledTime * 12f) % Dots;
        for (int i = 0; i < Dots; i++)
        {
            int behind = (head - i + Dots) % Dots;
            Color c = color;
            c.a = Mathf.Lerp(1f, 0.15f, behind / (float)(Dots - 1));
            images[i].color = c;
        }
    }
}
