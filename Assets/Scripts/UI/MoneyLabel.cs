using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows the player's saved money and keeps it up to date.
public class MoneyLabel : MonoBehaviour
{
    public TextMeshProUGUI text;

    // The one way money is written everywhere in the game: "95 700 kr" (a space between the thousands)
    public static string Format(int amount)
    {
        return amount.ToString("N0", Spaced) + " kr";
    }

    static readonly NumberFormatInfo Spaced = new NumberFormatInfo { NumberGroupSeparator = " ", NumberDecimalDigits = 0 };

    // The one money pill: a cream pill with soft corners and an orange dot in front of the amount. Pages built in code
    // use this; the main menu and in-game pages get the same look from the editor builders.
    public static Image CreatePill(Transform parent, Vector2 anchoredPosition, out TextMeshProUGUI amount, float fontSize = 28f)
    {
        Image pill = CourseUI.Panel("Money pill", parent, CourseUI.Card);
        if (CourseUI.Skin != null && CourseUI.Skin.pill != null)
        {
            pill.sprite = CourseUI.Skin.pill;
            pill.type = Image.Type.Sliced;
            // The same soft, round ends as the main menu's and the lobby's pill (2026-11-19, the user: without this the
            // ends came out pointed). Every money pill uses this look.
            pill.pixelsPerUnitMultiplier = 128f / 60f;
        }
        pill.raycastTarget = true;
        CourseUI.Place(pill.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), anchoredPosition, new Vector2(220, 60));
        Shadow shadow = pill.gameObject.AddComponent<Shadow>();
        shadow.effectColor = CourseUI.Hex("1F3D2B", 0.25f);
        shadow.effectDistance = new Vector2(0, -4);
        HorizontalLayoutGroup row = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(20, 26, 0, 0);
        row.spacing = 12;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        ContentSizeFitter fit = pill.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        Image dot = CourseUI.Panel("Coin", pill.transform, CourseUI.Accent);
        if (CourseUI.Skin != null && CourseUI.Skin.pill != null) { dot.sprite = CourseUI.Skin.pill; dot.type = Image.Type.Simple; }
        dot.raycastTarget = false;
        LayoutElement dotSize = dot.gameObject.AddComponent<LayoutElement>();
        dotSize.minWidth = dotSize.preferredWidth = 22;
        dotSize.minHeight = dotSize.preferredHeight = 22;

        amount = CourseUI.Text("Amount", pill.transform, "0 kr", fontSize, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        amount.enableWordWrapping = false;
        amount.overflowMode = TextOverflowModes.Overflow;
        return pill;
    }

    void OnEnable()
    {
        Money.Changed += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        Money.Changed -= Refresh;
    }

    void Start()
    {
        HoverTip.Add(transform, UIHints.Money);
        Refresh();
    }

    void Refresh()
    {
        if (text != null && Money.Instance != null)
        {
            text.text = Format(Money.Instance.GetMoney());
        }
    }
}
