using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Draws one runner card: tier frame, photo, rating, flag, club, name and stats.
// Used by the collection, the pack opening and the cards you pick from in a competition.
public class RunnerCardView : MonoBehaviour
{
    [Header("Frame")]
    public Image shade;                 // the colour behind the photo, also the card's shape
    public Image front;
    public Sprite[] tierFronts = new Sprite[4];   // indexed by RunnerTier
    public Image glow;

    [Header("Runner")]
    public Image photo;
    public Image flag;
    public Image club;
    public TextMeshProUGUI overallText;
    public TextMeshProUGUI klassText;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI techniqueText;
    public TextMeshProUGUI tierText;

    [Header("Shown in the flag and club spots when there is no picture (made when needed)")]
    public TextMeshProUGUI countryNameText;
    public TextMeshProUGUI clubNameText;

    [Header("Tinted with the tier's text colour")]
    public Graphic[] inked;
    public Graphic[] inkedLabels;       // small captions, a little lighter
    public Graphic[] inkedSoft;         // thin dividing lines, lighter still

    [Header("Shine")]
    public RectTransform shine;
    public float shineEvery = 0f;       // seconds between sweeps, 0 = only when asked
    public float photoWidth = 292f;

    public CardData Card { get; private set; }

    float shineTimer;
    float shineProgress = -1f;

    public void Show(CardData card)
    {
        Card = card;
        RunnerTier tier = RunnerCards.Tier(card);
        Color ink = RunnerCards.TierInk(tier);

        if (front != null && tierFronts != null && (int)tier < tierFronts.Length && tierFronts[(int)tier] != null)
        {
            front.sprite = tierFronts[(int)tier];
        }
        if (shade != null) shade.color = RunnerCards.TierShade(tier);
        if (glow != null) glow.color = UIAnim.WithAlpha(RunnerCards.TierColor(tier), glow.color.a);

        SetSprite(photo, card.RunnerPicture);
        if (photo != null && card.RunnerPicture != null)
        {
            // Keep the photo's own shape and let the card crop what does not fit
            Rect rect = card.RunnerPicture.rect;
            photo.rectTransform.sizeDelta = new Vector2(photoWidth, photoWidth * rect.height / rect.width);
        }
        SetSprite(flag, card.country);
        SetSprite(club, card.clubb);
        // No flag or club badge picture yet: write the name in its place instead
        SetSlotText(ref countryNameText, flag, "Country name", 22f, 52f, card.country == null ? RunnerCards.CountryName(card) : "");
        SetSlotText(ref clubNameText, club, "Club name", 26f, 92f, card.clubb == null ? Emblems.NoBreakAtDash(card.clubName) : "");

        if (overallText != null) overallText.text = RunnerCards.Overall(card).ToString();
        if (klassText != null) klassText.text = card.klass;
        if (nameText != null) nameText.text = card.RunnerName != null ? card.RunnerName.ToUpperInvariant() : "";
        if (speedText != null) speedText.text = Mathf.RoundToInt(card.speed).ToString();
        if (techniqueText != null) techniqueText.text = Mathf.RoundToInt(card.technique).ToString();
        if (tierText != null) tierText.text = RunnerCards.TierName(tier);

        Tint(inked, ink, 1f);
        Tint(inkedLabels, ink, 0.8f);
        Tint(inkedSoft, ink, 0.4f);
        if (countryNameText != null) countryNameText.color = ink;
        if (clubNameText != null) clubNameText.color = ink;
    }

    void SetSlotText(ref TextMeshProUGUI text, Image slot, string objectName, float maxSize, float height, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            if (text != null) text.gameObject.SetActive(false);
            return;
        }
        if (text == null)
        {
            if (slot == null) return;
            text = MakeSlotText(slot, objectName, maxSize, height);
        }
        text.gameObject.SetActive(true);
        text.text = value;
    }

    // A small text the size of the card's left column, placed where the picture would be
    TextMeshProUGUI MakeSlotText(Image slot, string objectName, float maxSize, float height)
    {
        GameObject holder = new GameObject(objectName, typeof(RectTransform));
        holder.layer = slot.gameObject.layer;
        RectTransform rect = (RectTransform)holder.transform;
        rect.SetParent(slot.transform.parent, false);
        rect.SetSiblingIndex(slot.transform.GetSiblingIndex() + 1);
        RectTransform spot = slot.rectTransform;
        rect.anchorMin = spot.anchorMin;
        rect.anchorMax = spot.anchorMax;
        rect.pivot = spot.pivot;
        rect.anchoredPosition = spot.anchoredPosition;
        rect.sizeDelta = new Vector2(156f, height);

        TextMeshProUGUI text = holder.AddComponent<TextMeshProUGUI>();
        TextMeshProUGUI style = klassText != null ? klassText : nameText;
        if (style != null)
        {
            text.font = style.font;
            text.fontSharedMaterial = style.fontSharedMaterial;
        }
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.fontSizeMin = 17f;
        text.fontSizeMax = maxSize;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.lineSpacing = -14f;
        text.raycastTarget = false;
        return text;
    }

    static void Tint(Graphic[] graphics, Color ink, float alpha)
    {
        if (graphics == null) return;
        foreach (Graphic graphic in graphics)
        {
            if (graphic != null) graphic.color = UIAnim.WithAlpha(ink, alpha);
        }
    }

    static void SetSprite(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.enabled = sprite != null;
    }

    // Sweeps a highlight across the card once
    public void PlayShine()
    {
        shineProgress = 0f;
    }

    public void SetGlow(float alpha)
    {
        if (glow != null) glow.color = UIAnim.WithAlpha(glow.color, alpha);
    }

    void OnEnable()
    {
        shineTimer = Random.Range(0.3f, Mathf.Max(0.4f, shineEvery));
        PlaceShine(-1f);
    }

    void Update()
    {
        if (shine == null) return;
        float dt = Time.unscaledDeltaTime;
        if (shineProgress < 0f)
        {
            if (shineEvery <= 0f) return;
            shineTimer -= dt;
            if (shineTimer > 0f) return;
            shineTimer = shineEvery;
            shineProgress = 0f;
        }
        shineProgress += dt / 0.85f;
        if (shineProgress >= 1f)
        {
            shineProgress = -1f;
            PlaceShine(-1f);
            return;
        }
        PlaceShine(shineProgress);
    }

    void PlaceShine(float progress)
    {
        if (shine == null) return;
        bool visible = progress >= 0f;
        if (shine.gameObject.activeSelf != visible) shine.gameObject.SetActive(visible);
        if (visible)
        {
            shine.anchoredPosition = new Vector2(Mathf.Lerp(-420f, 420f, progress), 0f);
        }
    }
}
