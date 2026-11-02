using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// Finds which race in the merits text the mouse is over (TextMeshPro <link> tags) and reports
// hover and clicks. A click beside the races closes the close-up, like a click anywhere else.
public class MeritLinks : MonoBehaviour, IPointerClickHandler
{
    public TextMeshProUGUI text;
    public Action<int> Hovered;
    public Action<int> Clicked;
    public Action Missed;

    int hovered = -1;

    void OnDisable()
    {
        hovered = -1;
    }

    void Update()
    {
        int link = LinkAt(Input.mousePosition);
        if (link != hovered)
        {
            hovered = link;
            Hovered?.Invoke(link);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        int link = LinkAt(eventData.position);
        if (link >= 0) Clicked?.Invoke(link);
        else Missed?.Invoke();
    }

    // The number in <link="n">, or -1
    int LinkAt(Vector2 screen)
    {
        if (text == null) return -1;
        Canvas canvas = text.canvas;
        Camera cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        int index = TMP_TextUtilities.FindIntersectingLink(text, screen, cam);
        if (index < 0 || index >= text.textInfo.linkCount) return -1;
        int id;
        return int.TryParse(text.textInfo.linkInfo[index].GetLinkID(), out id) ? id : -1;
    }
}
