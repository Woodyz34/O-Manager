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

        // A player who has just left is still found for a moment, but its networked values can not be read any more
        var players = FindObjectsOfType<NetworkPlayer>()
            .Where(p => p.Object != null && p.Object.IsValid)
            .OrderBy(p => p.hourCount * 3600 + p.minuterCount * 60 + p.secoundCount)
            .ToList();

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

            // Alternerande bakgrundsfärg
            if (rank % 2 == 0)
                entry.GetComponent<Image>().color = new Color(1, 1, 1, 0.55f);
            else
                entry.GetComponent<Image>().color = new Color(1, 1, 1, 0.8f);

            // Markera egen rad
            if (p.Object.HasInputAuthority)
                entry.GetComponent<Image>().color = new Color(0.91f, 0.45f, 0.16f, 0.32f);

            string timeString = $"{p.hourCount:00}:{p.minuterCount:00}:{p.secoundCount:00}";
            entry.SetData(rank, p.nickName.ToString(), timeString);

            entries[p.Object.InputAuthority] = entry;
            nicknameToRank[p.nickName.ToString()] = rank;
            rank++;
        }

        ResizePanel(players.Count);
    }

    // Row and header sizes of the leaderboard card, used to let the card hug its rows
    private const float panelHeaderHeight = 62f;
    private const float panelRowHeight = 44f;
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

        foreach (var p in FindObjectsOfType<NetworkPlayer>())
        {
            if (p.Object == null || !p.Object.IsValid)
            {
                continue;
            }
            if (!entries.TryGetValue(p.Object.InputAuthority, out LeaderboardEntryUI entry) || entry == null)
            {
                continue;
            }

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
