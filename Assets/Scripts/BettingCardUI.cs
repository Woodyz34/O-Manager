using TMPro;
using UnityEngine;

public class BettingCardUI : MonoBehaviour
{
    public TextMeshProUGUI playerNameText;
    public CardDisplay cardDisplay;

    public void Setup(string playerName, CardData cardData)
    {
        playerNameText.text = playerName;
        cardDisplay.SetValues(cardData);
    }
}
