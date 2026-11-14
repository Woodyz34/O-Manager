using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// A scrolling grid of cards that only makes the cards that can be seen (and a row more above and below). With a
// thousand runners the grid used to make a thousand cards with pictures and texts at once: a long wait when the
// screen opened, and slow scrolling because every card was moved and drawn again. Now about thirty cards are made and
// filled with other runners as the list scrolls, so it looks and works as before, only lighter.
// The grid keeps the size, spacing, padding and alignment set on its GridLayoutGroup in the scene; the layout group
// itself (and the content size fitter) is switched off, since the cards are placed here.
public class VirtualGrid
{
    readonly ScrollRect scroll;
    readonly RectTransform content;
    readonly Func<RectTransform> make;          // a new, empty card in the grid
    readonly Action<RectTransform, int> fill;   // shows item number i on a card

    Vector2 cellSize, spacing;
    RectOffset padding;
    TextAnchor alignment;
    int fixedColumns;
    float alignmentShift;

    int count;
    int columns = 1;
    float width = -1f;
    int firstShown = -1, lastShown = -1;
    readonly List<RectTransform> pool = new List<RectTransform>();
    readonly Dictionary<int, RectTransform> shown = new Dictionary<int, RectTransform>();
    readonly List<int> leaving = new List<int>();

    public VirtualGrid(ScrollRect scroll, RectTransform content, Func<RectTransform> make, Action<RectTransform, int> fill)
    {
        this.scroll = scroll;
        this.content = content;
        this.make = make;
        this.fill = fill;

        GridLayoutGroup layout = content.GetComponent<GridLayoutGroup>();
        if (layout != null)
        {
            cellSize = layout.cellSize;
            spacing = layout.spacing;
            padding = layout.padding;
            alignment = layout.childAlignment;
            fixedColumns = layout.constraint == GridLayoutGroup.Constraint.FixedColumnCount ? layout.constraintCount : 0;
            layout.enabled = false;
        }
        else
        {
            cellSize = new Vector2(258, 400);
            spacing = new Vector2(20, 20);
            padding = new RectOffset(20, 20, 16, 16);
            alignment = TextAnchor.UpperCenter;
        }
        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter != null) fitter.enabled = false;
        if (scroll != null) scroll.onValueChanged.AddListener(_ => Refresh());
    }

    public int Count => count;

    // Every card made so far (shown or waiting to be used again)
    public IEnumerable<RectTransform> Cards
    {
        get
        {
            foreach (RectTransform card in shown.Values) yield return card;
            foreach (RectTransform card in pool) yield return card;
        }
    }

    // A new list: every card is filled again
    public void SetCount(int newCount, bool toTop)
    {
        count = Mathf.Max(0, newCount);
        foreach (var pair in shown)
        {
            pair.Value.gameObject.SetActive(false);
            pool.Add(pair.Value);
        }
        shown.Clear();
        firstShown = lastShown = -1;
        Layout();
        if (toTop && scroll != null) scroll.verticalNormalizedPosition = 1f;
        Refresh();
    }

    // The width of the grid can change (the window was resized): the columns are worked out again
    void Layout()
    {
        width = content.rect.width;
        float inner = width - padding.horizontal;
        columns = fixedColumns > 0 ? fixedColumns : Mathf.Max(1, Mathf.FloorToInt((inner + spacing.x) / (cellSize.x + spacing.x)));
        int rows = (count + columns - 1) / columns;
        float height = padding.vertical + rows * cellSize.y + Mathf.Max(0, rows - 1) * spacing.y;
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        float used = columns * cellSize.x + (columns - 1) * spacing.x;
        float free = inner - used;
        switch (alignment)
        {
            case TextAnchor.UpperLeft: case TextAnchor.MiddleLeft: case TextAnchor.LowerLeft: alignmentShift = 0f; break;
            case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: alignmentShift = free; break;
            default: alignmentShift = free / 2f; break;
        }
    }

    // Which cards can be seen now; called when the list scrolls (and each frame by the screen, which costs nearly nothing)
    public void Refresh()
    {
        if (content == null) return;
        if (!Mathf.Approximately(content.rect.width, width))
        {
            // Resized: every card in its new place
            foreach (var pair in shown)
            {
                pair.Value.gameObject.SetActive(false);
                pool.Add(pair.Value);
            }
            shown.Clear();
            firstShown = lastShown = -1;
            Layout();
        }
        RectTransform viewport = scroll != null && scroll.viewport != null ? scroll.viewport : content.parent as RectTransform;
        float top = content.anchoredPosition.y;   // how far the list is scrolled down
        float seen = viewport != null ? viewport.rect.height : 1080f;
        float rowHeight = cellSize.y + spacing.y;
        int firstRow = Mathf.Max(0, Mathf.FloorToInt((top - padding.top) / rowHeight) - 1);
        int lastRow = Mathf.FloorToInt((top + seen - padding.top) / rowHeight) + 1;
        int first = firstRow * columns;
        int last = Mathf.Min(count - 1, (lastRow + 1) * columns - 1);
        if (first == firstShown && last == lastShown) return;
        firstShown = first;
        lastShown = last;

        leaving.Clear();
        foreach (int index in shown.Keys) if (index < first || index > last) leaving.Add(index);
        foreach (int index in leaving)
        {
            RectTransform card = shown[index];
            shown.Remove(index);
            card.gameObject.SetActive(false);
            pool.Add(card);
        }
        for (int i = first; i <= last; i++)
        {
            if (shown.ContainsKey(i)) continue;
            RectTransform card;
            if (pool.Count > 0)
            {
                card = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
            }
            else
            {
                card = make();
            }
            Place(card, i);
            card.gameObject.SetActive(true);
            fill(card, i);
            shown[i] = card;
        }
    }

    void Place(RectTransform card, int index)
    {
        int row = index / columns, column = index % columns;
        card.anchorMin = card.anchorMax = new Vector2(0f, 1f);
        card.sizeDelta = cellSize;
        Vector2 pivot = card.pivot;
        float x = padding.left + alignmentShift + column * (cellSize.x + spacing.x) + cellSize.x * pivot.x;
        float y = padding.top + row * (cellSize.y + spacing.y) + cellSize.y * (1f - pivot.y);
        card.anchoredPosition = new Vector2(x, -y);
    }

    // The card showing item i, if it is made now
    public RectTransform CardAt(int index)
    {
        return shown.TryGetValue(index, out RectTransform card) ? card : null;
    }
}
