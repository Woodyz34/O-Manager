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
        foreach (CardData card in RunnerCards.InPacks())
        {

            GameObject cardInstance = Instantiate(CardPrefab, parent.transform);
            CardDisplay cardDisplay = cardInstance.GetComponent<CardDisplay>();

            // Anropa SetValues för att sätta värden för varje instans separat
            cardDisplay.SetValues(card);
        }

    }

    // "Your runners": the runners the player owns (all runners if they own none yet)
    public void ShowOwnedCards()
    {
        List<CardData> owned = RunnerCollection.OwnedCards();
        if (owned.Count == 0)
        {
            ShowAllCards();
            return;
        }
        foreach (CardData card in owned)
        {
            GameObject cardInstance = Instantiate(CardPrefab, parent.transform);
            cardInstance.GetComponent<CardDisplay>().SetValues(card);
        }
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
            GameObject cardInstance = Instantiate(CardPrefab, parent.transform);
            CardDisplay cardDisplay = cardInstance.GetComponent<CardDisplay>();
            cardDisplay.SetValues(card);
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

