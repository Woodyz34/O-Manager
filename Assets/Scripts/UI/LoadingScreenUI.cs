using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The moving parts of the loading page: the control flag that bobs, the dots after the message,
// the percentage next to the bar, the drifting contour lines and a tip at the bottom.
// Whoever shows the page still drives the Slider, exactly as before.
public class LoadingScreenUI : MonoBehaviour
{
    public Slider slider;
    public RectTransform emblem;
    public TextMeshProUGUI messageText;
    public TextMeshProUGUI percentText;
    public TextMeshProUGUI tipText;
    public RawImage contours;
    public string message = "Getting things ready";

    // Only things that are true in the game today
    static readonly string[] Tips =
    {
        "A runner with high technique makes fewer mistakes.",
        "Your runner does not lose the way while following a path.",
        "Scroll to zoom the map. Hold the right mouse button to move it.",
        "The more runners you beat, the more money you earn.",
        "Open packs in the shop to collect new runners.",
    };

    Vector2 emblemHome;
    bool emblemHomeKnown;
    int shownPercent = -1;
    int shownDots = -1;

    void OnEnable()
    {
        if (tipText != null)
        {
            tipText.text = Tips[Random.Range(0, Tips.Length)];
        }
        shownPercent = -1;
        shownDots = -1;
    }

    // Says what is being waited for, for example "Creating lobby"
    public void SetMessage(string text)
    {
        message = text;
        shownDots = -1;
    }

    void Update()
    {
        float time = Time.unscaledTime;

        if (emblem != null)
        {
            if (!emblemHomeKnown)
            {
                emblemHome = emblem.anchoredPosition;
                emblemHomeKnown = true;
            }
            emblem.anchoredPosition = emblemHome + new Vector2(0, Mathf.Sin(time * 2.2f) * 10f);
            emblem.localRotation = Quaternion.Euler(0, 0, 8f + Mathf.Sin(time * 1.3f) * 4f);
        }

        if (messageText != null)
        {
            // The dots that are not lit yet are still there, only invisible, so the text does not jump sideways
            int dots = Mathf.FloorToInt(time * 2.5f) % 4;
            if (dots != shownDots)
            {
                shownDots = dots;
                messageText.text = message + new string('.', dots) + "<alpha=#00>" + new string('.', 3 - dots);
            }
        }

        if (percentText != null && slider != null)
        {
            int percent = Mathf.RoundToInt(slider.normalizedValue * 100f);
            if (percent != shownPercent)
            {
                shownPercent = percent;
                percentText.text = percent > 0 ? percent + "%" : "";
            }
        }

        if (contours != null)
        {
            Rect uv = contours.uvRect;
            uv.x = time * 0.006f;
            uv.y = time * 0.004f;
            contours.uvRect = uv;
        }
    }
}
