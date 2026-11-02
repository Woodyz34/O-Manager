using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CardDisplay : MonoBehaviour
{
    private static readonly Lazy<CardDisplay> instance = new Lazy<CardDisplay>(() => new CardDisplay());

    public CardData CardData { get; private set; }

    private CardDisplay()
    {
        // Konstruktorlogik här
    }

    public static CardDisplay getInstance()
    {
        return instance.Value;
    }

    public string runnerName;
    public float speed;
    public float technique;
    public Sprite country;
    public Sprite clubb;
    public string klass;
    public bool man = true;
    public Sprite RunnerPicture;


    public TextMeshProUGUI nameText;
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI techniqueText;
    public Image countryImage;
    public TextMeshProUGUI klassText;
    public Image clubbImage;
    public Image runnerImage;

    // The new card look. When it is set, it draws the card and the old texts above stay hidden.
    public RunnerCardView view;


    // Funktion för att sätta värden för det här kortet
    public void SetValues(CardData card)
    {
        CardData = card;

        runnerName = card.RunnerName;
        speed = card.speed;
        technique = card.technique;
        klass = card.klass;
        clubb = card.clubb;
        RunnerPicture = card.RunnerPicture;
        country = card.country;

        // Uppdatera text och bilder för detta kort
        if (nameText != null) nameText.text = runnerName;
        if (speedText != null) speedText.text = Mathf.RoundToInt(speed).ToString();
        if (techniqueText != null) techniqueText.text = Mathf.RoundToInt(technique).ToString();
        if (klassText != null) klassText.text = klass;
        if (clubbImage != null) clubbImage.sprite = clubb;
        if (countryImage != null) countryImage.sprite = country;
        if (runnerImage != null) runnerImage.sprite = RunnerPicture;
        if (view != null) view.Show(card);
    }
    public float getTechnique(CardData card)
    {
        return card.technique;
    }
    public float getSpeed(CardData card)
    {
        return card.speed;
    }
    public void OnCardClick()
    {
        Debug.Log("a card got clicked");
        if (NetworkPlayer.Local == null)
        {
            Debug.LogWarning("Ingen lokal NetworkPlayer ännu!");
            return;
        }
        PathfindingManager pathfindingManager = FindObjectOfType<PathfindingManager>();
        if (NetworkPlayer.Local.BetMode == true)
        {
            if (NetworkPlayer.Local.IsInBetMode)
            {
                pathfindingManager.BetOnRunnerPanel.SetActive(false);
                NetworkPlayer.Local.RPC_SetHasMadeBet(NetworkPlayer.Local.nickName.ToString());
            }
            else
            {
                string playerName = NetworkPlayer.Local.nickName.ToString();

                Betting.Instance.AddSelectedCard(playerName, CardData);

                NetworkPlayer.Local.GetComponent<AgentMovement>().SetRunner(this);


                pathfindingManager.ChooseRunnerPanel.SetActive(false);
                NetworkPlayer.Local.SetHasChoosenRunner();
            }
        }
        else if (RelayRace.Active != null)
        {
            // Relays: one runner per leg. The panel stays open until every leg has a runner.
            if (RelayRace.Active.PickRunner(this))
            {
                pathfindingManager.ChooseRunnerPanel.SetActive(false);
                NetworkPlayer.Local.SetHasChoosenRunner();
            }
        }
        else
        {
            NetworkPlayer.Local.GetComponent<AgentMovement>().SetRunner(this);

            pathfindingManager.ChooseRunnerPanel.SetActive(false);
            NetworkPlayer.Local.SetHasChoosenRunner();
        }   
    }
}
