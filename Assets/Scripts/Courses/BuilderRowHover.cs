using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// A row of the course builder's control list (2026-11-20): its buttons (Move, Remove...) only show while the mouse is
// on the row, so the list stays short and calm. A click on the row itself shows that control on the map.
public class BuilderRowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    static readonly Color Rest = new Color(1f, 1f, 1f, 0f);
    static readonly Color Lit = new Color(1f, 1f, 1f, 0.55f);

    public readonly List<GameObject> hoverOnly = new List<GameObject>();
    public Action clicked;
    Image background;

    public static BuilderRowHover Attach(GameObject row)
    {
        BuilderRowHover hover = row.AddComponent<BuilderRowHover>();
        hover.background = row.GetComponent<Image>();
        if (hover.background == null) hover.background = row.AddComponent<Image>();
        hover.background.color = Rest;
        hover.background.raycastTarget = true;
        if (CourseUI.Skin != null && CourseUI.Skin.rounded != null)
        {
            hover.background.sprite = CourseUI.Skin.rounded;
            hover.background.type = Image.Type.Sliced;
        }
        return hover;
    }

    void Start()
    {
        Show(false);
    }

    void Show(bool on)
    {
        foreach (GameObject go in hoverOnly) if (go != null) go.SetActive(on);
        if (background != null) background.color = on ? Lit : Rest;
    }

    public void OnPointerEnter(PointerEventData eventData) => Show(true);

    public void OnPointerExit(PointerEventData eventData)
    {
        // Moving onto one of the row's own buttons is still on the row
        if (eventData.pointerCurrentRaycast.gameObject != null && eventData.pointerCurrentRaycast.gameObject.transform.IsChildOf(transform)) return;
        Show(false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) clicked?.Invoke();
    }
}
