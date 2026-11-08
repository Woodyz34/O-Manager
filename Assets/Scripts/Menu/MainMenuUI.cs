using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Runs the main menu: the home screen, the pack shop and the runner collection.
public class MainMenuUI : MonoBehaviour
{
    [Header("Screens")]
    public Canvas canvas;
    public GameObject homePanel;
    public GameObject shopPanel;
    public GameObject runnersPanel;

    [Header("Home")]
    public TextMeshProUGUI shopCaption;
    public TextMeshProUGUI runnersCaption;

    [Header("Shop")]
    public RectTransform packImage;
    public RectTransform packShadow;
    public Button buyButton;
    public TextMeshProUGUI buyLabel;
    public TextMeshProUGUI noteText;
    public TextMeshProUGUI oddsText;
    public TextMeshProUGUI ownedText;

    [Header("Sound")]
    public AudioSource audioSource;
    public AudioClip clickSound;   // not used any more: UISounds clicks every button

    PackOpening opening;
    Vector2 packHome;
    bool packHomeKnown;

    void OnEnable()
    {
        Money.Changed += Refresh;
        RunnerCollection.Changed += Refresh;
    }

    void OnDisable()
    {
        Money.Changed -= Refresh;
        RunnerCollection.Changed -= Refresh;
    }

    void Start()
    {
        // Opened from the multiplayer lobby: straight to the shop or My Runners, and "Back" goes to the lobby
        if (LobbyVisit.Active && LobbyVisit.Current != LobbyVisit.Place.CourseBuilder)
        {
            BackGoesToLobby(shopPanel);
            BackGoesToLobby(runnersPanel);
            Show(LobbyVisit.Current == LobbyVisit.Place.Runners ? runnersPanel : shopPanel);
            return;
        }
        Show(homePanel);
        PrivateMaps.ForgetBorrowed();   // copies of other players' private maps are only for the lobby they were in
        SharedMaps.CheckOnce();   // maps other players have shared, downloaded in the background
    }

    static void BackGoesToLobby(GameObject panel)
    {
        if (panel == null) return;
        Transform back = panel.transform.Find("Back");
        if (back == null) return;
        TextMeshProUGUI label = back.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) label.text = "Back to lobby";
        RectTransform rect = back as RectTransform;
        if (rect != null) rect.sizeDelta = new Vector2(Mathf.Max(rect.sizeDelta.x, 260f), rect.sizeDelta.y);
    }

    // ---------- buttons ----------

    public void ShowHome()
    {
        if (LobbyVisit.Active)
        {
            LobbyVisit.Close();
            return;
        }
        Show(homePanel);
    }

    public void ShowShop()
    {
        Show(shopPanel);
    }

    public void ShowRunners()
    {
        Show(runnersPanel);
    }

    public void BuyPack()
    {
        if (opening != null)
        {
            return;
        }
        PackResult result = PackShop.Buy();
        if (result == null)
        {
            Refresh();
            return;
        }
        PackOpening prefab = Resources.Load<PackOpening>("UI/PackOpening");
        if (prefab == null)
        {
            Debug.LogWarning("PackOpening prefab is missing. Run Tools > O-Manager > Apply Menu UI.");
            Refresh();
            return;
        }
        // The show has its own camera and canvas, so the menu steps aside while it plays
        opening = Instantiate(prefab);
        canvas.enabled = false;
        opening.Play(result, OnOpeningClosed);
    }

    public void Quit()
    {
        Application.Quit();
    }

    void OnOpeningClosed(PackOpening.Exit exit)
    {
        Destroy(opening.gameObject);
        opening = null;
        canvas.enabled = true;
        Refresh();
        switch (exit)
        {
            case PackOpening.Exit.OpenAnother:
                BuyPack();
                break;
            case PackOpening.Exit.Runners:
                Show(runnersPanel);
                break;
            default:
                Show(shopPanel);
                break;
        }
    }

    // ---------- screens ----------

    void Show(GameObject panel)
    {
        homePanel.SetActive(panel == homePanel);
        shopPanel.SetActive(panel == shopPanel);
        runnersPanel.SetActive(panel == runnersPanel);
        Refresh();
    }


    void Refresh()
    {
        int total = RunnerCards.InPacks().Count;
        int owned = RunnerCollection.OwnedCount();
        int price = PackShop.CurrentPrice();
        int money = Money.Instance != null ? Money.Instance.GetMoney() : 0;
        bool canBuy = PackShop.CanBuy();

        if (runnersCaption != null) runnersCaption.text = owned + " of " + total + " runners collected";
        if (shopCaption != null) shopCaption.text = price == 0 ? "Your first pack is free" : "Runner packs for " + price + " kr";

        if (buyButton != null) buyButton.interactable = canBuy;
        if (buyLabel != null) buyLabel.text = price == 0 ? "Open free pack" : "Open pack   " + price + " kr";
        if (noteText != null)
        {
            if (PackShop.AllCollected())
            {
                noteText.text = "You have every runner. New ones arrive when the rankings update.";
            }
            else if (!canBuy)
            {
                noteText.text = "You need " + (price - money) + " kr more. Compete to earn money.";
            }
            else if (price == 0)
            {
                noteText.text = "Your first pack is on the house.";
            }
            else
            {
                noteText.text = "Every pack holds a runner you do not have yet.";
            }
        }
        if (ownedText != null) ownedText.text = "You have " + owned + " of " + total + " runners";
        if (oddsText != null)
        {
            // Four tiers do not fit on one line at full size, so the line shrinks to the box
            oddsText.enableAutoSizing = true;
            oddsText.fontSizeMin = 18;
            oddsText.fontSizeMax = 30;
            oddsText.textWrappingMode = TextWrappingModes.NoWrap;
            oddsText.text = OddsText();
        }
    }

    static string OddsText()
    {
        StringBuilder text = new StringBuilder();
        for (int i = (int)RunnerTier.Elite; i >= 0; i--)
        {
            RunnerTier tier = (RunnerTier)i;
            float chance = PackShop.Chance(tier);
            if (chance <= 0f)
            {
                continue;
            }
            if (text.Length > 0) text.Append("   ");
            string name = RunnerCards.TierName(tier);
            text.Append("<b>").Append(name[0]).Append(name.Substring(1).ToLowerInvariant()).Append("</b> ");
            text.Append(Mathf.RoundToInt(chance * 100f)).Append("%");
        }
        return text.ToString();
    }

    void Update()
    {
        // The pack in the shop floats gently
        if (packImage != null && shopPanel.activeSelf)
        {
            if (!packHomeKnown)
            {
                packHome = packImage.anchoredPosition;
                packHomeKnown = true;
            }
            float bob = Mathf.Sin(Time.unscaledTime * 1.7f);
            packImage.anchoredPosition = packHome + new Vector2(0, bob * 10f);
            packImage.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.unscaledTime * 1.1f) * 2.2f);
            if (packShadow != null)
            {
                float s = 1f - bob * 0.06f;
                packShadow.localScale = new Vector3(s, s, 1f);
            }
        }
    }
}
