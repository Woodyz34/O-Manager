using System.Collections;
using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The screen everyone sees when the competition is over: placings and times, the money you earned,
// and in imposter mode the guessing and the reveal. The host has the button back to the lobby.
public class EndScreenUI : MonoBehaviour
{
    public static EndScreenUI Current;

    [Header("General")]
    public CanvasGroup rootGroup;
    public TextMeshProUGUI subtitleText;

    [Header("Standings")]
    public Transform rowParent;
    public GameObject rowTemplate;

    [Header("Reward")]
    public GameObject rewardPanel;
    public TextMeshProUGUI rewardTotalText;
    public Transform rewardLineParent;
    public GameObject rewardLineTemplate;
    public TextMeshProUGUI balanceText;

    [Header("Imposter vote")]
    public GameObject votePanel;
    public TextMeshProUGUI voteTitleText;
    public TextMeshProUGUI voteHintText;
    public Transform voteButtonParent;
    public GameObject voteButtonTemplate;
    public TextMeshProUGUI voteStatusText;

    [Header("Footer")]
    public Button lobbyButton;
    public Button revealNowButton;
    public TextMeshProUGUI waitingText;

    [Header("Colours")]
    public Color rowColor = new Color(1f, 1f, 1f, 0.78f);
    public Color localRowColor = new Color(0.91f, 0.45f, 0.16f, 0.28f);
    public Color imposterRowColor = new Color(0.88f, 0.16f, 0.24f, 0.25f);
    public Color winnerBadgeColor = new Color(0.91f, 0.45f, 0.16f);
    public Color badgeColor = new Color(0.25f, 0.49f, 0.33f);
    public Color unfinishedBadgeColor = new Color(0.6f, 0.6f, 0.55f);
    public Color accentColor = new Color(0.91f, 0.45f, 0.16f);
    public Color dangerColor = new Color(0.78f, 0.13f, 0.2f);

    List<Standing> standings = new List<Standing>();
    readonly Dictionary<PlayerRef, GameObject> rows = new Dictionary<PlayerRef, GameObject>();
    readonly List<Button> voteButtons = new List<Button>();

    bool imposterMode;
    PlayerRef imposter;
    // Secret task mode: an imposter mode where the others had a task (SecretTasks)
    bool secretTaskMode;
    int secretTask;
    bool voting;
    bool rewardsGiven;
    bool revealRunning;
    bool caught;
    int votersCount;

    public static void Show()
    {
        if (Current != null)
        {
            return;
        }
        GameObject prefab = Resources.Load<GameObject>("UI/EndScreen");
        if (prefab == null)
        {
            Debug.LogError("Missing prefab Resources/UI/EndScreen");
            return;
        }
        Instantiate(prefab);
    }

    void Awake()
    {
        Current = this;
        rowTemplate.SetActive(false);
        rewardLineTemplate.SetActive(false);
        voteButtonTemplate.SetActive(false);
        rewardPanel.SetActive(false);
        votePanel.SetActive(false);
        lobbyButton.gameObject.SetActive(false);
        revealNowButton.gameObject.SetActive(false);
        waitingText.text = "";

        lobbyButton.onClick.AddListener(CompetitionFlow.HostReturnToLobby);
        revealNowButton.onClick.AddListener(ConfirmRevealNow);
    }

    // The host's "reveal now" asks first (2026-11-19: the host revealed the imposter before the others had guessed,
    // most likely by pressing this button by mistake)
    void ConfirmRevealNow()
    {
        if (CompetitionFlow.RevealStarted) return;
        int waiting = 0;
        CompetitionFlow.TryGetImposter(out PlayerRef imposter);
        foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player.Object == null || !player.Object.IsValid || player.Object.StateAuthority == imposter) continue;
            if (!player.hasGuessedImposter) waiting++;
        }
        string hint = waiting == 0 ? "Everyone has guessed." : waiting == 1 ? "1 player has not guessed yet." : waiting + " players have not guessed yet.";
        ChoicePopup.Open("Reveal the imposter now?", new[]
        {
            new ChoicePopup.Option
            {
                id = "reveal",
                title = "Yes, reveal now",
                description = "Nobody can guess after this. Without it the imposter is shown when everyone has guessed or the time is up."
            }
        }, null, id =>
        {
            if (id == "reveal") CompetitionFlow.HostRevealImposter();
        }, hint);
    }

    void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    IEnumerator Start()
    {
        subtitleText.text = "COUNTING THE RESULTS";
        rootGroup.alpha = 0f;
        for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
        {
            rootGroup.alpha = t / 0.35f;
            yield return null;
        }
        rootGroup.alpha = 1f;

        // Give the last finish times a moment to arrive from the other players, then lock the standings
        yield return new WaitForSecondsRealtime(0.8f);
        standings = CompetitionFlow.ComputeStandings();
        imposterMode = CompetitionFlow.TryGetImposter(out imposter);
        secretTaskMode = imposterMode && SecretTasks.TryGet(out secretTask);
        subtitleText.text = secretTaskMode ? "SECRET TASK" : imposterMode ? "IMPOSTER MODE" : "COMPETITION FINISHED";
        yield return BuildRows();

        if (imposterMode)
        {
            ShowVote();
            // The reveal may already have been ordered while the rows were appearing
            if (CompetitionFlow.RevealStarted)
            {
                BeginReveal();
            }
        }
        else
        {
            GiveRewards();
        }
    }

    void Update()
    {
        bool host = CompetitionFlow.LocalIsHost();

        if (lobbyButton.gameObject.activeSelf != (host && rewardsGiven))
        {
            lobbyButton.gameObject.SetActive(host && rewardsGiven);
        }
        bool showRevealNow = host && voting && !CompetitionFlow.RevealStarted;
        if (revealNowButton.gameObject.activeSelf != showRevealNow)
        {
            revealNowButton.gameObject.SetActive(showRevealNow);
        }
        waitingText.text = (!host && rewardsGiven) ? "Waiting for the host to return to the lobby..." : "";

        if (voting && !CompetitionFlow.RevealStarted)
        {
            int guessed = 0;
            int voters = 0;
            foreach (Standing standing in standings)
            {
                if (standing.player == imposter)
                {
                    continue;
                }
                voters++;
                NetworkPlayer player = CompetitionFlow.FindPlayer(standing.player);
                if (player != null && player.hasGuessedImposter)
                {
                    guessed++;
                }
            }
            int secondsLeft = Mathf.Max(0, Mathf.CeilToInt(CompetitionFlow.GuessDeadline - Time.unscaledTime));
            voteStatusText.text = voters > 0
                ? guessed + " of " + voters + " have guessed. Reveal in " + secondsLeft + " s."
                : "The reveal is coming...";
        }
    }

    // ---- Standings ----

    IEnumerator BuildRows()
    {
        foreach (Standing standing in standings)
        {
            GameObject row = Instantiate(rowTemplate, rowParent);
            row.SetActive(true);
            rows[standing.player] = row;

            row.GetComponent<Image>().color = standing.isLocal ? localRowColor : rowColor;
            Image badge = row.transform.Find("Badge").GetComponent<Image>();
            badge.color = !standing.finished ? unfinishedBadgeColor : (standing.place == 1 ? winnerBadgeColor : badgeColor);
            Text(row, "Badge/Place").text = standing.finished ? standing.place.ToString() : "-";
            Text(row, "Name").text = standing.name;
            Text(row, "Tag").text = standing.isLocal ? "YOU" : "";
            Text(row, "Time").text = standing.finished ? CompetitionFlow.FormatTime(standing.seconds) : "DNF";

            // Rows drop in one after the other
            CanvasGroup group = row.GetComponent<CanvasGroup>();
            if (group != null)
            {
                for (float t = 0f; t < 0.12f; t += Time.unscaledDeltaTime)
                {
                    group.alpha = t / 0.12f;
                    yield return null;
                }
                group.alpha = 1f;
            }
        }
    }

    static TextMeshProUGUI Text(GameObject parent, string path)
    {
        return parent.transform.Find(path).GetComponent<TextMeshProUGUI>();
    }

    Standing LocalStanding()
    {
        return standings.Find(s => s.isLocal);
    }

    string NameOf(PlayerRef player)
    {
        Standing standing = standings.Find(s => s.player == player);
        return standing != null ? standing.name : "a runner who left";
    }

    // ---- Imposter vote ----

    void ShowVote()
    {
        voting = true;
        votePanel.SetActive(true);

        Standing me = LocalStanding();
        bool amImposter = me != null && me.player == imposter;
        if (amImposter)
        {
            voteTitleText.text = "You were the imposter";
            voteHintText.text = secretTaskMode
                ? "Their task was: " + SecretTasks.Short(secretTask) + ". The others are guessing now. Keep a straight face."
                : "The others are guessing now. Keep a straight face.";
            return;
        }

        voteTitleText.text = "Who was the imposter?";
        voteHintText.text = (secretTaskMode ? "One runner didn't know the task." : "One runner was trying to finish last.")
            + " Guess right for +" + CompetitionRewards.CorrectGuess + " kr.";
        foreach (Standing standing in standings)
        {
            if (standing.isLocal)
            {
                continue;
            }
            GameObject buttonObject = Instantiate(voteButtonTemplate, voteButtonParent);
            buttonObject.SetActive(true);
            Text(buttonObject, "Label").text = standing.name;
            Button button = buttonObject.GetComponent<Button>();
            PlayerRef candidate = standing.player;
            button.onClick.AddListener(() => Vote(candidate, button));
            voteButtons.Add(button);
        }
    }

    void Vote(PlayerRef candidate, Button chosen)
    {
        NetworkPlayer local = NetworkPlayer.Local;
        if (local == null || local.hasGuessedImposter || CompetitionFlow.RevealStarted)
        {
            return;
        }
        local.imposterGuess = candidate;
        local.hasGuessedImposter = true;

        foreach (Button button in voteButtons)
        {
            button.interactable = false;
        }
        chosen.GetComponent<Image>().color = accentColor;
        Text(chosen.gameObject, "Label").color = Color.white;
        voteHintText.text = "You guessed " + NameOf(candidate) + ". Waiting for the others...";
    }

    // ---- Reveal ----

    public void BeginReveal()
    {
        if (revealRunning || !voting)
        {
            return;
        }
        revealRunning = true;
        StartCoroutine(RevealRoutine());
    }

    IEnumerator RevealRoutine()
    {
        foreach (Button button in voteButtons)
        {
            button.interactable = false;
        }

        List<string> names = new List<string>();
        foreach (Standing standing in standings)
        {
            names.Add(standing.name);
        }

        ImposterOverlay overlay = ImposterOverlay.GetOrCreate();
        if (overlay != null)
        {
            yield return overlay.PlayReveal(names, NameOf(imposter), CountVotes);
        }
        else
        {
            CountVotes();
        }

        voting = false;
        votePanel.SetActive(false);
        if (rows.TryGetValue(imposter, out GameObject imposterRow))
        {
            imposterRow.GetComponent<Image>().color = imposterRowColor;
            TextMeshProUGUI tag = Text(imposterRow, "Tag");
            tag.text = "IMPOSTER";
            tag.color = dangerColor;
        }
        GiveRewards();
    }

    ImposterOverlay.RevealResult CountVotes()
    {
        int correct = 0;
        votersCount = 0;
        foreach (Standing standing in standings)
        {
            if (standing.player == imposter)
            {
                continue;
            }
            votersCount++;
            NetworkPlayer player = CompetitionFlow.FindPlayer(standing.player);
            if (player != null && player.hasGuessedImposter && player.imposterGuess == imposter)
            {
                correct++;
            }
        }
        // The imposter is caught when at least half of the other runners point at them
        caught = votersCount > 0 && correct * 2 >= votersCount;

        string detail;
        if (votersCount == 0)
        {
            detail = "Nobody else was there to guess.";
        }
        else if (correct == 1)
        {
            detail = "1 of " + votersCount + " runners guessed right.";
        }
        else
        {
            detail = correct + " of " + votersCount + " runners guessed right.";
        }
        if (secretTaskMode)
        {
            detail += "\nThe task was: " + SecretTasks.Short(secretTask);
        }
        return new ImposterOverlay.RevealResult { caught = caught, detail = detail };
    }

    // ---- Money ----

    void GiveRewards()
    {
        if (rewardsGiven)
        {
            return;
        }
        rewardsGiven = true;
        rewardPanel.SetActive(true);

        Standing me = LocalStanding();
        if (me == null)
        {
            rewardTotalText.text = "+0 kr";
            balanceText.text = Balance();
            return;
        }

        int runners = standings.Count;
        bool amImposter = imposterMode && me.player == imposter;
        string placeLabel = me.finished
            ? CompetitionFlow.Ordinal(me.place) + " place of " + runners
            : "Did not finish";

        int total = 0;
        if (amImposter && secretTaskMode)
        {
            // Without a task, the imposter simply races: a normal place, plus money for getting away
            total += AddLine(placeLabel, CompetitionRewards.ForPlace(me.place, runners, me.finished));
            if (votersCount > 0)
            {
                total += AddLine(caught ? "Caught by the others" : "Got away with it",
                    caught ? 0 : CompetitionRewards.ForImposterEscape(votersCount));
            }
        }
        else if (amImposter)
        {
            total += AddLine(me.finished ? placeLabel + ", counted from the back" : placeLabel,
                CompetitionRewards.ForImposterPlace(me.place, runners, me.finished));
            if (votersCount > 0)
            {
                total += AddLine(caught ? "Caught by the others" : "Got away with it",
                    caught ? 0 : CompetitionRewards.ForImposterEscape(votersCount));
            }
        }
        else
        {
            total += AddLine(placeLabel, CompetitionRewards.ForPlace(me.place, runners, me.finished));
            if (imposterMode)
            {
                NetworkPlayer local = NetworkPlayer.Local;
                bool guessed = local != null && local.hasGuessedImposter;
                bool right = guessed && local.imposterGuess == imposter;
                total += AddLine(right ? "Found the imposter" : (guessed ? "Wrong guess" : "No guess"),
                    right ? CompetitionRewards.CorrectGuess : 0);
            }
        }

        if (Money.Instance != null)
        {
            Money.Instance.AddMoney(total);
        }
        balanceText.text = Balance();
        StartCoroutine(CountUp(total));
    }

    int AddLine(string label, int amount)
    {
        GameObject line = Instantiate(rewardLineTemplate, rewardLineParent);
        line.SetActive(true);
        Text(line, "Label").text = label;
        Text(line, "Amount").text = "+" + amount + " kr";
        return amount;
    }

    static string Balance()
    {
        return Money.Instance != null ? Money.Instance.GetMoney() + " kr" : "";
    }

    IEnumerator CountUp(int total)
    {
        for (float t = 0f; t < 0.9f; t += Time.unscaledDeltaTime)
        {
            rewardTotalText.text = "+" + Mathf.RoundToInt(Mathf.Lerp(0, total, t / 0.9f)) + " kr";
            yield return null;
        }
        rewardTotalText.text = "+" + total + " kr";
    }
}
