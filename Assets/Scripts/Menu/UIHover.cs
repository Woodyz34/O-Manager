using UnityEngine;
using UnityEngine.EventSystems;

// Makes a button or tile grow a little under the mouse and dip when pressed.
public class UIHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public RectTransform target;
    public float hoverScale = 1.05f;
    public float pressScale = 0.97f;
    public float lift = 0f;

    Vector3 baseScale;
    Vector2 basePosition;
    bool hovering;
    bool pressing;
    bool ready;

    void Awake()
    {
        Remember();
    }

    void Remember()
    {
        if (ready) return;
        if (target == null) target = (RectTransform)transform;
        baseScale = target.localScale;
        basePosition = target.anchoredPosition;
        ready = true;
    }

    void OnDisable()
    {
        if (!ready) return;
        hovering = false;
        pressing = false;
        target.localScale = baseScale;
        target.anchoredPosition = basePosition;
    }

    public void OnPointerEnter(PointerEventData eventData) { hovering = true; }
    public void OnPointerExit(PointerEventData eventData) { hovering = false; pressing = false; }
    public void OnPointerDown(PointerEventData eventData) { pressing = true; }
    public void OnPointerUp(PointerEventData eventData) { pressing = false; }

    void Update()
    {
        float wanted = pressing ? pressScale : hovering ? hoverScale : 1f;
        float k = 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime);
        target.localScale = Vector3.Lerp(target.localScale, baseScale * wanted, k);
        if (lift != 0f)
        {
            Vector2 wantedPosition = basePosition + new Vector2(0, hovering ? lift : 0f);
            target.anchoredPosition = Vector2.Lerp(target.anchoredPosition, wantedPosition, k);
        }
    }
}
