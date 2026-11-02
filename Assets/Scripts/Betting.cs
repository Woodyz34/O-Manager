using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Betting : MonoBehaviour
{
    public GameObject CardPrefab;
    public GameObject BettingCardUIPrefab;
    public Transform parent;
    CardDisplay cardDisplay = CardDisplay.getInstance();
    public Dictionary<string, CardData> SelectedCards = new Dictionary<string, CardData>();
    public static Betting Instance;

    private void Awake()
    {
        Instance = this;
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void ShowAllBettingCards()
    {
        foreach (var entry in SelectedCards)
        {
            string playerName = entry.Key;
            CardData cardData = entry.Value;

            GameObject uiInstance = Instantiate(BettingCardUIPrefab, parent);
            BettingCardUI bettingUI = uiInstance.GetComponent<BettingCardUI>();

            bettingUI.Setup(playerName, cardData);
        }
    }

    public void AddSelectedCard(string playerName, CardData cardData)
    {
        SelectedCards[playerName] = cardData;
        Debug.Log($"{playerName} valde {cardData.RunnerName}");
    }

    public CardData GetSelectedCard(string playerName)
    {
        if (SelectedCards.TryGetValue(playerName, out CardData cardData))
        {
            return cardData;
        }

        Debug.LogWarning($"Ingen valt kort hittades för spelare: {playerName}");
        return null;
    }

}
