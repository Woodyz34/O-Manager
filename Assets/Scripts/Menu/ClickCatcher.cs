using UnityEngine;
using UnityEngine.EventSystems;

// Stops clicks (and the end of drags) on a panel from reaching a "click anywhere to close" button
// behind it. Makes no sound: UISounds skips objects that have this.
public class ClickCatcher : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
    }
}
