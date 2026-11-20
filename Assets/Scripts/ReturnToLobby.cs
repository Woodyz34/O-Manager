using Fusion;
using System;
using TMPro;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Unity.Collections.Unicode;

public class ReturnToLobby : MonoBehaviour
{
    public GameObject SeeBetResultPanel;
    public GameObject SeeResultWithoutBetPanel;
    public Transform CardPanel;
    public TextMeshProUGUI BetWhoYouBetOnText;
    public TextMeshProUGUI BetWhatPositionBetcameText;
    public TextMeshProUGUI BetMoneyGainedText;
    public TextMeshProUGUI BetYourPositionText;
    public TextMeshProUGUI BetTrophyText;
    public TextMeshProUGUI TrophyText;
    public TextMeshProUGUI YourPositionText;

    public void GoToLobby()
    {
        NetworkRunner runner = FindObjectOfType<NetworkRunner>();
        if (runner == null)
        {
            return;
        }

        // Bara värden (master client i Shared Mode) får avsluta tävlingen och ladda lobbyn åt alla
        if (!runner.IsSharedModeMasterClient)
        {
            return;
        }

        // Ending the competition first shows the results to everyone. The results screen has its own
        // button for the host that then loads the lobby.
        if (!CompetitionFlow.ResultsShown)
        {
            CompetitionFlow.HostShowResults();
            return;
        }

        LoadLobby(runner);
    }

    public static void LoadLobby(NetworkRunner runner)
    {
        // Single player has no lobby: back to the single-player page
        if (runner != null && runner.GameMode == GameMode.Single)
        {
            SinglePlayerMenu.EndGame(runner);
            return;
        }
        if (runner == null || !runner.IsSharedModeMasterClient)
        {
            return;
        }

        runner.SessionInfo.IsOpen = true;
        // Se till att alla spelare inte förstörs
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        foreach (var player in players)
        {
            var networkPlayer = player.GetComponent<NetworkPlayer>();
            // A player who has just left can still be found for a moment; it has nothing to reset
            if (networkPlayer == null || networkPlayer.Object == null || !networkPlayer.Object.IsValid)
            {
                continue;
            }
            networkPlayer.RPC_ResetTimer();
            DontDestroyOnLoad(player);
        }

        // Ladda lobby-scenen via Fusion
        runner.LoadScene("Lobby");
    }

    public void SeeResult()
    {
        NetworkPlayer.Local.RPC_SeeResult();
    }

    public void SeeResultWithoutBet()
    {
        SeeResultWithoutBetPanel.SetActive(true);
        LeaderboardManager.Instance.BuildLeaderboardUI();

        int rank = LeaderboardManager.Instance.GetPlayerRankByNickname(NetworkPlayer.Local.nickName.ToString());
        YourPositionText.text = $"You came {rank}th";

        // No trophies any more
        if (TrophyText != null) TrophyText.text = "";
    }

    public void SeeBetResult()
    {
        
        SeeBetResultPanel.SetActive(true);
        BetWhoYouBetOnText.text = $"You betted on {NetworkPlayer.Local.BetPlayer}";

        LeaderboardManager.Instance.BuildLeaderboardUI();

        int rank = LeaderboardManager.Instance.GetPlayerRankByNickname(NetworkPlayer.Local.BetPlayer);

        BetWhatPositionBetcameText.text = $"{NetworkPlayer.Local.BetPlayer} came {rank}th";

        int money = 0;

        if(rank == 1)
        {
            money = 500;
        }

        BetMoneyGainedText.text = "+" + MoneyLabel.Format(money);

        Money.Instance.AddMoney(money);

        int yourRank = LeaderboardManager.Instance.GetPlayerRankByNickname(NetworkPlayer.Local.nickName.ToString());
        BetYourPositionText.text = $"You came {yourRank}th";

        // No trophies any more
        if (BetTrophyText != null) BetTrophyText.text = "";
        
        string playerName = NetworkPlayer.Local.nickName.ToString();
        CardData cardData = Betting.Instance.GetSelectedCard(playerName);

        GameObject uiInstance = Instantiate(Betting.Instance.BettingCardUIPrefab, CardPanel);
        BettingCardUI bettingUI = uiInstance.GetComponent<BettingCardUI>();

        bettingUI.Setup(playerName, cardData);
    }
}
