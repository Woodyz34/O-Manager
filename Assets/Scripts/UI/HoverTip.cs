using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Hover text: rest the mouse on something for about half a second and a small card explains it.
// Use HoverTip.Add(someButton, "What it does") on any UI object. Texts that change can be given as a function.
public class HoverTip : MonoBehaviour
{
    const float Delay = 0.5f;
    const float MaxTextWidth = 520f;
    const float Margin = 18f;

    static HoverTip instance;

    // Adds (or replaces) the hover text of a UI object. An object that is no raycast target gets an invisible one.
    public static void Add(Component target, string text)
    {
        if (target != null) Add(target, () => text);
    }

    public static void Add(Component target, Func<string> text)
    {
        if (target == null) return;
        GameObject go = target.gameObject;
        HoverTipTrigger trigger = go.GetComponent<HoverTipTrigger>();
        if (trigger == null) trigger = go.AddComponent<HoverTipTrigger>();
        trigger.text = text;
        if (go.GetComponent<Graphic>() == null && go.GetComponent<RectTransform>() != null)
        {
            Image catcher = go.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;
        }
    }

    // Finds an object by name below a parent and gives it a hover text. Does nothing when it is not there.
    public static void AddTo(Transform root, string childName, string text)
    {
        if (root == null) return;
        Transform child = FindDeep(root, childName);
        if (child != null) Add(child, text);
    }

    static Transform FindDeep(Transform root, string childName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == childName) return t;
        }
        return null;
    }

    internal static void Hover(HoverTipTrigger trigger)
    {
        Ensure();
        instance.current = trigger;
        instance.since = Time.unscaledTime;
        instance.HideCard();
    }

    internal static void Leave(HoverTipTrigger trigger)
    {
        if (instance != null && instance.current == trigger)
        {
            instance.current = null;
            instance.HideCard();
        }
    }

    static void Ensure()
    {
        if (instance != null) return;
        GameObject go = new GameObject("HoverTip", typeof(Canvas), typeof(CanvasScaler));
        DontDestroyOnLoad(go);
        instance = go.AddComponent<HoverTip>();
        instance.Build();
    }

    HoverTipTrigger current;
    float since;
    RectTransform canvasRect;
    RectTransform card;
    TextMeshProUGUI label;
    Canvas canvas;
    string shownText;

    void Build()
    {
        canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        ScreenSettings.Fit(GetComponent<CanvasScaler>());
        canvasRect = (RectTransform)transform;

        Image back = CourseUI.Panel("HoverTip card", transform, CourseUI.Hex("1F3D2B", 0.96f));
        back.raycastTarget = false;
        card = back.rectTransform;
        card.anchorMin = Vector2.zero;
        card.anchorMax = Vector2.zero;
        card.pivot = new Vector2(0f, 1f);
        Outline edge = back.gameObject.AddComponent<Outline>();
        edge.effectColor = CourseUI.Hex("E8742A", 0.9f);
        edge.effectDistance = new Vector2(2f, -2f);

        label = CourseUI.Text("Text", card, "", 24, CourseUI.Hex("F6F0DC"));
        label.raycastTarget = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = TextAlignmentOptions.TopLeft;
        RectTransform lr = label.rectTransform;
        lr.anchorMin = Vector2.zero;
        lr.anchorMax = Vector2.one;
        lr.pivot = new Vector2(0.5f, 0.5f);
        lr.offsetMin = new Vector2(16f, 12f);
        lr.offsetMax = new Vector2(-16f, -12f);
        card.gameObject.SetActive(false);
    }

    void HideCard()
    {
        if (card != null) card.gameObject.SetActive(false);
        shownText = null;
    }

    void Update()
    {
        if (current == null || !current.isActiveAndEnabled)
        {
            current = null;
            if (card.gameObject.activeSelf) HideCard();
            return;
        }
        // A click ends the hint, it is not needed once the player acts
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
        {
            since = float.MaxValue;
            HideCard();
            return;
        }
        if (Time.unscaledTime - since < Delay) return;

        string text = current.text != null ? current.text() : null;
        if (string.IsNullOrEmpty(text))
        {
            HideCard();
            return;
        }
        if (text != shownText)
        {
            shownText = text;
            label.text = text;
            Vector2 size = label.GetPreferredValues(text, MaxTextWidth, 0f);
            card.sizeDelta = new Vector2(size.x + 32f, size.y + 24f);
        }
        card.gameObject.SetActive(true);
        Place();
    }

    void Place()
    {
        float scale = Mathf.Max(0.01f, canvas.scaleFactor);
        Vector2 mouse = (Vector2)Input.mousePosition / scale;
        Vector2 area = canvasRect.rect.size;
        Vector2 size = card.sizeDelta;
        // Below and to the right of the pointer; flipped to the other side near the edges
        float x = mouse.x + Margin;
        float y = mouse.y - Margin;
        if (x + size.x > area.x - 8f) x = mouse.x - Margin - size.x;
        if (y - size.y < 8f) y = mouse.y + Margin + size.y + 14f;
        x = Mathf.Clamp(x, 8f, Mathf.Max(8f, area.x - size.x - 8f));
        y = Mathf.Clamp(y, size.y + 8f, Mathf.Max(size.y + 8f, area.y - 8f));
        card.anchoredPosition = new Vector2(x, y);
    }
}

public class HoverTipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
{
    public Func<string> text;

    public void OnPointerEnter(PointerEventData eventData) { HoverTip.Hover(this); }
    public void OnPointerExit(PointerEventData eventData) { HoverTip.Leave(this); }
    public void OnPointerDown(PointerEventData eventData) { HoverTip.Leave(this); }
    void OnDisable() { HoverTip.Leave(this); }
}
