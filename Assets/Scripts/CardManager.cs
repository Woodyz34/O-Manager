using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CardManager : MonoBehaviour
{
    private static readonly Lazy<CardManager> instance = new Lazy<CardManager>(() => new CardManager());
    public GameObject CardPrefab;
    public GameObject parent;
    //MyCards MyCards = MyCards.getInstance();
    CardDisplay cardDisplay = CardDisplay.getInstance();
    public List<CardDisplay> SelectedCards = new List<CardDisplay>();

    // The runners offered on the Choose a runner screen. RunnerPickerUI shows them, and makes cards only for the ones
    // that can be seen (a long list used to make hundreds of cards at once, which froze the screen for a moment).
    public readonly List<CardData> Offered = new List<CardData>();
    public int OfferedVersion { get; private set; }

    public void Offer(CardData card)
    {
        if (card == null) return;
        Offered.Add(card);
        OfferedVersion++;
    }

    public void ClearOffered()
    {
        Offered.Clear();
        OfferedVersion++;
    }
    Dictionary<int, CardData> LocalCardDatabase;
    PathfindingManager pathfindingManager;

    private CardManager()
    {
        // Konstruktorlogik här
    }

    public static CardManager getInstance()
    {
        return instance.Value;
    }

    void Awake()
    {
        pathfindingManager = FindObjectOfType<PathfindingManager>();
    }
    private void Start()
    {
        
    }

    public void ShowAllCards()
    {
        if(AllCards.getInstance().GetAllCardsList().Count == 0)
        {
            AllCards.getInstance().SetAllCards();
        }
        foreach (CardData card in GameModes.WithSameStats(UnderCap(RunnerCards.InPacks())))
        {

            Offer(card);
        }

    }

    // "Your runners": the runners the player owns, plus runners on loan up to 10 (RunnerCollection.CardsForRace)
    public void ShowOwnedCards()
    {
        // With a max overall the cheapest missing runner is lent when none of the player's own is under it
        List<CardData> owned = RunnerRules.Cap > 0 && RelayRace.Active == null ? UnderCap(RunnerRules.YourRunnersPool(new List<LegSex> { LegSex.Any }, RunnerRules.Cap))
            : RunnerCollection.CardsForRace();
        foreach (CardData card in owned)
        {
            Offer(card);
        }
    }

    // "Open packs" in an individual race: the runner that came out of the pack
    public void ShowCards(List<CardData> cards)
    {
        foreach (CardData card in cards)
        {
            Offer(card);
        }
    }

    // An individual race with a max overall (RunnerRules.Cap): only the runners at or under it
    static List<CardData> UnderCap(List<CardData> cards)
    {
        if (RunnerRules.Cap <= 0 || RelayRace.Active != null) return cards;   // relays: RelayRace checks the whole team
        return cards.FindAll(c => RunnerRules.BudgetCost(c) <= RunnerRules.Cap);
    }

    // The ON LOAN ribbon on runners the player only has for this round (lent, or out of an "Open packs" pack).
    // The cards are used again for other runners as the list scrolls, so the ribbon is also taken off.
    public void MarkCard(Transform card, CardData data)
    {
        bool onLoan = data != null && (RunnerCollection.IsOnLoan(data.RunnerName) || RunnerRules.IsOpened(data.RunnerName));
        Transform ribbon = card.Find("On loan");
        if (ribbon != null) ribbon.gameObject.SetActive(onLoan);
        else if (onLoan) MarkOnLoan(card);
    }

    // A small orange tag on a runner the player only borrows for this round. It sits like a ribbon across the
    // bottom tip of the card (the card picture is 301 high round the card's centre): at the top it stuck out above
    // the card and the top row of the Choose a runner grid cut it off.
    static void MarkOnLoan(Transform card)
    {
        Image tag = CourseUI.Panel("On loan", card, CourseUI.Accent);
        tag.raycastTarget = false;
        CourseUI.Place(tag.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -146), new Vector2(150, 34));
        tag.transform.SetAsLastSibling();
        TMPro.TextMeshProUGUI text = CourseUI.Text("Text", tag.transform, "ON LOAN", 20, Color.white, TMPro.FontStyles.Bold, TMPro.TextAlignmentOptions.Center);
        text.raycastTarget = false;
        CourseUI.Stretch(text.rectTransform);
    }

    public void ShowRandomCards(int amount)
    {
        // Se till att listan är fylld
        if (AllCards.getInstance().GetAllCardsList().Count == 0)
        {
            AllCards.getInstance().SetAllCards();
        }

        List<CardData> allCards = RunnerCards.InPacks();

        // Säkerhetskontroll om amount är större än antal kort
        if (amount > allCards.Count)
            amount = allCards.Count;

        // Skapa en lista med slumpmässigt valda kort
        List<CardData> randomCards = new List<CardData>();

        // Skapa en kopia av listan för att undvika duplicering
        List<CardData> tempList = new List<CardData>(allCards);

        for (int i = 0; i < amount; i++)
        {
            int randIndex = UnityEngine.Random.Range(0, tempList.Count);
            randomCards.Add(tempList[randIndex]);
            tempList.RemoveAt(randIndex); // ta bort så inga duplicates uppstår
        }

        // Instansiera korten
        foreach (CardData card in randomCards)
        {
            Offer(card);
        }
    }



    public void AddCardToSelected(CardDisplay card)
    {

        cardDisplay = card;
        SelectedCards.Add(cardDisplay);

        // Gör vad du vill göra med det valda kortet här
        // T.ex. skriv ut informationen om det valda kortet

        Debug.Log(SelectedCards.Count);
        Debug.Log("Selected Card: " + cardDisplay.runnerName);
        Debug.Log("tec: " + cardDisplay.technique);


    }
    
    public float GetRunnerTec(int z)
    {

        return SelectedCards[z].technique;
    }
    public string GetCardName(int z)
    {
        return SelectedCards[z].runnerName;
    }

}

