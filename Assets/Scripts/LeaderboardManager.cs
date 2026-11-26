using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using Fusion;
using UnityEngine.UI;

public class LeaderboardManager : NetworkBehaviour
{
    public static LeaderboardManager Instance;

    [SerializeField] private Transform contentParent;
    [SerializeField] private GameObject leaderboardEntryPrefab;

    private Dictionary<string, int> nicknameToRank = new();

    private Dictionary<PlayerRef, LeaderboardEntryUI> entries = new();
    private Dictionary<PlayerRef, int> shownSeconds = new();

    void Awake()
    {
        Instance = this;
    }

    public override void Spawned()
    {
        BuildLeaderboardUI();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void BuildLeaderboardUI()
    {
        if (contentParent == null)
        {
            return;
        }

        var players = Standings();

        // Rensa gamla entries
        foreach (Transform child in contentParent)
        {
            Destroy(child.gameObject);
        }

        entries.Clear();
        shownSeconds.Clear();
        nicknameToRank.Clear();

        int rank = 1;
        foreach (var p in players)
        {
            GameObject entryObj = Instantiate(leaderboardEntryPrefab, contentParent);
            LeaderboardEntryUI entry = entryObj.GetComponent<LeaderboardEntryUI>();

            ColorRow(entry, rank, p);
            LeaderboardRow row = entryObj.GetComponent<LeaderboardRow>();
            if (row == null) row = entryObj.AddComponent<LeaderboardRow>();
            row.Set(p);

            string timeString = $"{p.hourCount:00}:{p.minuterCount:00}:{p.secoundCount:00}";
            entry.SetData(rank, p.nickName.ToString(), p.relayRunnerName.ToString(), p.RunnerStatsText, timeString);

            entries[p.Object.InputAuthority] = entry;
            nicknameToRank[p.nickName.ToString()] = rank;
            rank++;
        }

        ResizePanel(players.Count);
        ScrollRect scroll = contentParent.GetComponentInParent<ScrollRect>();
        if (scroll != null && scroll.transform.parent is RectTransform card) LeaderboardFrame.Attach(card);
        shownOrder.Clear();
        foreach (var p in players) shownOrder.Add(p.Object.InputAuthority);
    }

    // Alternating rows, your own in orange, and red for the others while they are still drawing (2026-11-19), so you
    // can see who you are waiting for
    static void ColorRow(LeaderboardEntryUI entry, int rank, NetworkPlayer p)
    {
        Image image = entry.GetComponent<Image>();
        if (image == null) return;
        Color color;
        if (p.Object.HasInputAuthority) color = new Color(0.91f, 0.45f, 0.16f, 0.32f);
        else if (p.drawingNow) color = new Color(0.86f, 0.24f, 0.2f, 0.55f);
        else color = rank % 2 == 0 ? new Color(1, 1, 1, 0.55f) : new Color(1, 1, 1, 0.8f);
        if (image.color != color) image.color = color;
    }

    // The standings as the race goes (2026-11-15): furthest on first (relay leg, then control; on a fork by NetworkPlayer.relayProgress, 2026-11-16), and at the same
    // control the one with the least time. Before, the list was only put in order of time when someone punched (in
    // relays only at the end of each leg). Everything it needs is already networked on the players, so each client
    // works it out itself once a second; nothing is sent.
    public static List<NetworkPlayer> Standings()
    {
        // A player who has just left is still found for a moment, but its networked values can not be read any more.
        // A player who only watches (joined in the middle of the race) has no row.
        return NetworkPlayer.All()
            .Where(p => NetworkPlayer.Races(p))
            .OrderByDescending(p => Mathf.Max(0, p.relayLeg))
            .ThenByDescending(p => p.relayLeg < 0 ? 0f : p.relayProgress)
            .ThenBy(p => p.hourCount * 3600 + p.minuterCount * 60 + p.secoundCount)
            .ToList();
    }

    private readonly List<PlayerRef> shownOrder = new List<PlayerRef>();
    private const float orderRefreshInterval = 1f;
    private float nextOrderRefresh;

    // Puts the rows in the order of the standings, when it has changed; a new or departed player builds the list again.
    // Also when the list is still empty (2026-11-24): the board is made when the scene starts, often before the
    // players are there, and then it stayed empty until someone punched a control. Now everyone in the game shows
    // up within a second, before anyone has drawn or run.
    void RefreshOrder()
    {
        if (contentParent == null) return;
        var players = Standings();
        bool same = players.Count == shownOrder.Count;
        for (int i = 0; same && i < players.Count; i++) same = players[i].Object.InputAuthority == shownOrder[i];
        if (same) return;
        foreach (var p in players)
        {
            if (!entries.TryGetValue(p.Object.InputAuthority, out LeaderboardEntryUI e) || e == null)
            {
                BuildLeaderboardUI();
                return;
            }
        }
        if (players.Count != entries.Count)
        {
            BuildLeaderboardUI();
            return;
        }
        shownOrder.Clear();
        nicknameToRank.Clear();
        for (int i = 0; i < players.Count; i++)
        {
            NetworkPlayer p = players[i];
            LeaderboardEntryUI entry = entries[p.Object.InputAuthority];
            entry.transform.SetSiblingIndex(i);
            string timeString = $"{p.hourCount:00}:{p.minuterCount:00}:{p.secoundCount:00}";
            entry.SetData(i + 1, p.nickName.ToString(), p.relayRunnerName.ToString(), p.RunnerStatsText, timeString);
            ColorRow(entry, i + 1, p);
            shownOrder.Add(p.Object.InputAuthority);
            nicknameToRank[p.nickName.ToString()] = i + 1;
        }
    }

    // Row and header sizes of the leaderboard card, used to let the card hug its rows
    private const float panelHeaderHeight = 62f;
    private const float panelRowHeight = LeaderboardEntryUI.RowHeight;
    private const float panelRowSpacing = 6f;
    private const float panelBottomPadding = 14f;
    private const float panelMaxHeight = 560f;

    private void ResizePanel(int rowCount)
    {
        ScrollRect scroll = contentParent.GetComponentInParent<ScrollRect>();
        if (scroll == null || !(scroll.transform.parent is RectTransform panel))
        {
            return;
        }
        float rowsHeight = rowCount * panelRowHeight + Mathf.Max(0, rowCount - 1) * panelRowSpacing;
        float height = Mathf.Min(panelMaxHeight, panelHeaderHeight + rowsHeight + panelBottomPadding);
        panel.sizeDelta = new Vector2(panel.sizeDelta.x, height);
    }

    // Tiderna ligger redan som [Networked] värden på varje spelare, så varje klient läser dem själv
    // några gånger per sekund i stället för att alla spelare skickar en RPC för varje sekund på klockan.
    private const float timeRefreshInterval = 0.25f;
    private float nextTimeRefresh;

    void Update()
    {
        if (Time.unscaledTime >= nextOrderRefresh)
        {
            nextOrderRefresh = Time.unscaledTime + orderRefreshInterval;
            RefreshOrder();
        }
        if (Time.unscaledTime < nextTimeRefresh)
        {
            return;
        }
        nextTimeRefresh = Time.unscaledTime + timeRefreshInterval;
        UpdateLeaderboardTimesOnly();
    }

    public void UpdateLeaderboardTimesOnly()
    {
        if (entries.Count == 0)
        {
            return;
        }

        foreach (var p in NetworkPlayer.All())
        {
            if (p.Object == null || !p.Object.IsValid)
            {
                continue;
            }
            if (!entries.TryGetValue(p.Object.InputAuthority, out LeaderboardEntryUI entry) || entry == null)
            {
                continue;
            }

            // The runner is picked (or changed on a new relay leg) after the list was built
            entry.SetNames(p.nickName.ToString(), p.relayRunnerName.ToString(), p.RunnerStatsText);
            int place = shownOrder.IndexOf(p.Object.InputAuthority);
            if (place >= 0) ColorRow(entry, place + 1, p);

            int seconds = p.hourCount * 3600 + p.minuterCount * 60 + p.secoundCount;
            if (shownSeconds.TryGetValue(p.Object.InputAuthority, out int shown) && shown == seconds)
            {
                continue;
            }
            shownSeconds[p.Object.InputAuthority] = seconds;

            string timeString = $"{p.hourCount:00}:{p.minuterCount:00}:{p.secoundCount:00}";
            entry.UpdateTime(timeString);
        }
    }

    public int GetPlayerRankByNickname(string nickName)
    {
        if (nicknameToRank.TryGetValue(nickName, out int rank))
            return rank;

        Debug.LogWarning($"Ingen spelare hittades med nickname: {nickName}");
        return -1;
    }

}
