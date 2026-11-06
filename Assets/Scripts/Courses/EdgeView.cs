using UnityEngine;
using UnityEngine.EventSystems;

// The empty part of the Map edge pop-up's picture: the mouse wheel zooms, dragging moves the picture (MapEdgeEditor)
public class EdgeView : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler
{
    internal MapEdgeEditor editor;

    public void OnScroll(PointerEventData data) => editor.ZoomAt(data.position, data.enterEventCamera, data.scrollDelta.y > 0 ? 1.25f : 0.8f);
    public void OnBeginDrag(PointerEventData data) { }
    public void OnDrag(PointerEventData data) => editor.PanBy(data.position, data.delta, data.pressEventCamera);
}
