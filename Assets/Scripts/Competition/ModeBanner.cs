using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A big, bouncy message over the whole screen for game mode events everyone should see, wherever their camera is:
// a bear that jumps up roaring, or a control that flies off. Takes no clicks, so the race goes on underneath.
public class ModeBanner : MonoBehaviour
{
    static ModeBanner current;
    static Sprite bearSprite;

    RectTransform card;
    RectTransform icon;
    TextMeshProUGUI big;
    TextMeshProUGUI small;
    CanvasGroup group;

    // The bear picture (Resources/Modes/Bear.png, made by Docs/game_mode_assets.py)
    public static Sprite BearSprite
    {
        get
        {
            if (bearSprite == null)
            {
                Texture2D texture = Resources.Load<Texture2D>("Modes/Bear");
                if (texture != null)
                {
                    bearSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
                }
            }
            return bearSprite;
        }
    }

    // icon may be null. shake: the card shakes as if something roared.
    public static void Show(Sprite iconSprite, string bigText, string smallText, Color accent, float seconds, bool shake, AudioClip sound = null)
    {
        if (current != null) Destroy(current.gameObject);
        GameObject go = new GameObject("Mode banner", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;
        ScreenSettings.Fit(go.GetComponent<CanvasScaler>());
        current = go.AddComponent<ModeBanner>();
        current.Build(iconSprite, bigText, smallText, accent);
        current.StartCoroutine(current.Play(seconds, shake, iconSprite != null));
        if (sound != null)
        {
            AudioSource source = go.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            source.PlayOneShot(sound, 0.9f);
        }
    }

    public static AudioClip LoadSound(string name)
    {
        return Resources.Load<AudioClip>("Sounds/" + name);
    }

    void Build(Sprite iconSprite, string bigText, string smallText, Color accent)
    {
        group = gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        Image back = CourseUI.CardPanel("Card", transform);
        back.raycastTarget = false;
        card = back.rectTransform;
        CourseUI.Place(card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -150), new Vector2(iconSprite != null ? 1060 : 900, 200));

        Image stripe = CourseUI.Panel("Stripe", card, accent);
        stripe.raycastTarget = false;
        CourseUI.Place(stripe.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0, 0), new Vector2(18, 200));

        float textLeft = 40f;
        if (iconSprite != null)
        {
            Image picture = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            picture.transform.SetParent(card, false);
            picture.sprite = iconSprite;
            picture.preserveAspect = true;
            picture.raycastTarget = false;
            icon = picture.rectTransform;
            CourseUI.Place(icon, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.15f), new Vector2(150, -70), new Vector2(260, 260));
            textLeft = 290f;
        }
        big = CourseUI.Text("Big", card, bigText, 64, accent, FontStyles.Bold, TextAlignmentOptions.Left);
        CourseUI.Place(big.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(textLeft, -22), new Vector2(card.sizeDelta.x - textLeft - 30, 84));
        big.enableWordWrapping = false;
        big.enableAutoSizing = true;
        big.fontSizeMin = 30;
        big.fontSizeMax = 64;
        small = CourseUI.Text("Small", card, smallText, 30, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        CourseUI.Place(small.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(textLeft + 2, -108), new Vector2(card.sizeDelta.x - textLeft - 30, 80));
    }

    IEnumerator Play(float seconds, bool shake, bool hasIcon)
    {
        Vector2 home = card.anchoredPosition;
        float t = 0f;
        // Drops in from above with a bounce
        while (t < 0.45f)
        {
            t += Time.unscaledDeltaTime;
            float f = Mathf.Clamp01(t / 0.45f);
            float bounce = 1f - Mathf.Pow(1f - f, 3f) * Mathf.Cos(f * Mathf.PI * 2.5f);
            card.anchoredPosition = home + new Vector2(0f, 360f * (1f - bounce));
            if (hasIcon)
            {
                // The bear jumps up out of the card
                float pop = f < 0.6f ? f / 0.6f * 1.25f : 1.25f - (f - 0.6f) / 0.4f * 0.25f;
                icon.localScale = Vector3.one * pop;
            }
            yield return null;
        }
        card.anchoredPosition = home;
        float shown = 0f;
        float hold = Mathf.Max(0.5f, seconds - 0.8f);
        while (shown < hold)
        {
            shown += Time.unscaledDeltaTime;
            if (shake && shown < 1.1f)
            {
                float strength = 14f * (1f - shown / 1.1f);
                card.anchoredPosition = home + new Vector2(Mathf.Sin(shown * 70f) * strength, Mathf.Cos(shown * 53f) * strength * 0.6f);
                big.transform.localScale = Vector3.one * (1f + 0.12f * Mathf.Abs(Mathf.Sin(shown * 18f)) * (1f - shown / 1.1f));
            }
            else
            {
                card.anchoredPosition = home;
                big.transform.localScale = Vector3.one;
            }
            if (hasIcon)
            {
                // A little roaring wobble
                icon.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(shown * 9f) * 7f);
            }
            yield return null;
        }
        // Slides away
        t = 0f;
        while (t < 0.35f)
        {
            t += Time.unscaledDeltaTime;
            float f = Mathf.Clamp01(t / 0.35f);
            card.anchoredPosition = home + new Vector2(0f, 320f * f * f);
            group.alpha = 1f - f;
            yield return null;
        }
        if (current == this) current = null;
        Destroy(gameObject);
    }
}
