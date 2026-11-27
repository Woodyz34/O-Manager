using UnityEngine;
using UnityEngine.EventSystems;

// The map in HeightQuestionPanel: dragged with the mouse (left or right button) and zoomed with the wheel round the
// point under the mouse (2026-11-27, Anders wanted to move the map with the mouse, not only + and -)
public class HeightQuestionView : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler
{
    public HeightQuestionPanel panel;

    float Scale
    {
        get
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            return canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        }
    }

    public void OnBeginDrag(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData)
    {
        if (panel != null) panel.Drag(eventData.delta / Scale);
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (panel == null) return;
        RectTransform rect = (RectTransform)transform;
        Vector2 at = Vector2.zero;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera ?? eventData.enterEventCamera, out Vector2 local))
            at = local - rect.rect.center;
        panel.Wheel(eventData.scrollDelta.y, at);
    }
}
