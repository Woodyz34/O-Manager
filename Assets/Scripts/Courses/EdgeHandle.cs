using UnityEngine;
using UnityEngine.EventSystems;

// A corner or side-middle dot in the Map edge pop-up (MapEdgeEditor)
public class EdgeHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    internal MapEdgeEditor editor;
    internal int index;
    internal bool middle;

    internal RectTransform Rect => (RectTransform)transform;

    public void OnBeginDrag(PointerEventData data) => editor.BeginDrag(this);
    public void OnDrag(PointerEventData data) => editor.Drag(this, data);
    public void OnEndDrag(PointerEventData data) => editor.EndDrag();

    public void OnPointerClick(PointerEventData data)
    {
        if (data.button == PointerEventData.InputButton.Right) editor.Remove(this);
    }
}
