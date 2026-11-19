using UnityEngine;
using UnityEngine.UI;

// Makes a long list of cards quick to get through: a scrollbar next to the list that can be dragged
// (or clicked) to jump anywhere, a mouse wheel that speeds up when it is spun fast, and the keys
// Page Up, Page Down, Home and End. Added from code, so the saved scenes need no rebuild.
[DefaultExecutionOrder(1000)]   // after ScrollRect, which sets the handle size in its LateUpdate
public class FastScroll : MonoBehaviour
{
    const float MinHandle = 40f;      // the handle is never shorter than this, so it can be grabbed
    const float WheelStep = 180f;     // pixels per wheel notch, about half a row of cards
    const float MaxBoost = 5f;        // a fast spin moves up to this many times further per notch
    const float SpinGap = 0.14f;      // notches closer than this count as one fast spin

    ScrollRect scroll;
    RectTransform bar;
    bool besideList;
    float boost = 1f;
    float lastNotch = -10f;
    Image trackImage, handleImage;

    // For a list on a cream card (Lobby settings): a green handle on a light track, so the bar is easy to see
    // (2026-11-19, the user: the cream handle was the same colour as the card)
    public FastScroll OnLightCard()
    {
        if (trackImage != null) trackImage.color = new Color(0.12f, 0.24f, 0.17f, 0.14f);
        if (handleImage != null) handleImage.color = new Color(0.16f, 0.42f, 0.27f, 1f);
        return this;
    }

    public static FastScroll Add(ScrollRect scroll)
    {
        if (scroll == null) return null;
        FastScroll fast = scroll.GetComponent<FastScroll>();
        if (fast == null)
        {
            fast = scroll.gameObject.AddComponent<FastScroll>();
            fast.scroll = scroll;
            fast.MakeScrollbar();
        }
        return fast;
    }

    // A thin bar just to the right of the list. It is put next to the list, not inside it,
    // because the list clips everything inside it, and the cards fill it from edge to edge.
    void MakeScrollbar()
    {
        RectTransform list = (RectTransform)scroll.transform;
        bool stretched = Mathf.Approximately(list.anchorMax.x, 1f);
        Transform parent = stretched ? list.parent : list;

        GameObject barObject = new GameObject("Fast scrollbar", typeof(RectTransform));
        bar = (RectTransform)barObject.transform;
        besideList = stretched;
        bar.SetParent(parent, false);
        bar.SetSiblingIndex(stretched ? list.GetSiblingIndex() + 1 : list.childCount - 1);
        if (stretched)
        {
            FollowList();
        }
        else
        {
            bar.anchorMin = new Vector2(1, 0);
            bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(1, 0.5f);
            bar.offsetMin = new Vector2(-16f, 12f);
            bar.offsetMax = new Vector2(-4f, -12f);
        }

        Image listImage = scroll.GetComponent<Image>();
        Sprite round = listImage != null ? listImage.sprite : null;

        Image track = barObject.AddComponent<Image>();
        trackImage = track;
        Style(track, round, new Color(0.12f, 0.24f, 0.17f, 0.42f));

        RectTransform area = NewRect("Sliding area", bar);
        area.anchorMin = Vector2.zero;
        area.anchorMax = Vector2.one;
        area.offsetMin = Vector2.zero;
        area.offsetMax = Vector2.zero;
        RectTransform handle = NewRect("Handle", area);
        handle.offsetMin = Vector2.zero;
        handle.offsetMax = Vector2.zero;
        Image handleImage = handle.gameObject.AddComponent<Image>();
        Style(handleImage, round, new Color(0.984f, 0.957f, 0.855f, 0.9f));
        this.handleImage = handleImage;

        Scrollbar scrollbar = barObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        ColorBlock colors = scrollbar.colors;
        colors.highlightedColor = new Color(0.91f, 0.455f, 0.165f);   // the orange accent while hovered or dragged
        colors.pressedColor = new Color(0.91f, 0.455f, 0.165f);
        colors.selectedColor = Color.white;
        scrollbar.colors = colors;

        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.scrollSensitivity = WheelStep;
    }

    // The list can move or change height after the bar is made (Choose a runner raises its bottom
    // edge for the course info), so the bar follows it
    void FollowList()
    {
        RectTransform list = (RectTransform)scroll.transform;
        bar.anchorMin = new Vector2(1, list.anchorMin.y);
        bar.anchorMax = new Vector2(1, list.anchorMax.y);
        bar.pivot = new Vector2(0, 0.5f);
        bar.offsetMin = new Vector2(list.offsetMax.x + 16f, list.offsetMin.y + 8f);
        bar.offsetMax = new Vector2(list.offsetMax.x + 34f, list.offsetMax.y - 8f);
    }

    void LateUpdate()
    {
        if (scroll == null || bar == null) return;
        if (besideList) FollowList();
        Scrollbar scrollbar = scroll.verticalScrollbar;
        float height = bar.rect.height;
        if (scrollbar != null && height > 0f && scrollbar.size < MinHandle / height)
        {
            scrollbar.size = Mathf.Min(1f, MinHandle / height);
        }
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    static void Style(Image image, Sprite round, Color color)
    {
        image.color = color;
        if (round != null)
        {
            image.sprite = round;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 6f;   // a small radius on the thin bar
        }
    }

    void Update()
    {
        if (scroll == null || !scroll.isActiveAndEnabled) return;

        // The wheel: notches that come quickly after each other move further and further
        if (Input.mouseScrollDelta.y != 0f)
        {
            boost = Time.unscaledTime - lastNotch < SpinGap ? Mathf.Min(boost * 1.4f, MaxBoost) : 1f;
            lastNotch = Time.unscaledTime;
            scroll.scrollSensitivity = WheelStep * boost;
        }

        // Keys, unless the player is typing in the search field
        if (TypingInField()) return;
        if (Input.GetKeyDown(KeyCode.Home)) Jump(1f);
        else if (Input.GetKeyDown(KeyCode.End)) Jump(0f);
        else if (Input.GetKeyDown(KeyCode.PageDown)) Page(-1);
        else if (Input.GetKeyDown(KeyCode.PageUp)) Page(1);
    }

    static bool TypingInField()
    {
        UnityEngine.EventSystems.EventSystem events = UnityEngine.EventSystems.EventSystem.current;
        GameObject selected = events != null ? events.currentSelectedGameObject : null;
        return selected != null && selected.GetComponent<TMPro.TMP_InputField>() != null;
    }

    void Jump(float position)
    {
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = position;
    }

    // One screen of cards up or down
    void Page(int direction)
    {
        RectTransform view = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
        float hidden = scroll.content.rect.height - view.rect.height;
        if (hidden <= 0f) return;
        float step = view.rect.height * 0.9f / hidden;
        Jump(Mathf.Clamp01(scroll.verticalNormalizedPosition + direction * step));
    }
}
