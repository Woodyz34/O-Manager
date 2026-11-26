using System.Collections.Generic;
using System.Text;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows who is in the lobby. Reads the spawned NetworkPlayer objects, so it needs no extra networking.
public class LobbyPlayerListUI : MonoBehaviour
{
    public TextMeshProUGUI countText;
    public TextMeshProUGUI emptyText;
    public Transform listParent;
    public GameObject rowTemplate;
    public float refreshInterval = 0.5f;

    public Color localDotColor = new Color(0.91f, 0.45f, 0.16f);
    public Color otherDotColor = new Color(0.25f, 0.49f, 0.33f);

    readonly List<GameObject> rows = new List<GameObject>();
    string lastSignature;
    float nextRefresh;

    void Start()
    {
        rowTemplate.SetActive(false);
        MakeScrollable();
        Refresh();
    }

    // With up to 100 players the list is taller than the screen, so it sits in a box that scrolls
    const float MaxListHeight = 560f;
    LayoutElement viewportSize;
    RectTransform content;

    void MakeScrollable()
    {
        content = listParent as RectTransform;
        if (content == null || content.parent == null) return;
        Transform panel = content.parent;

        GameObject viewport = new GameObject("Players viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.SetParent(panel, false);
        viewportRect.SetSiblingIndex(content.GetSiblingIndex());
        viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0);   // invisible, but catches the mouse wheel
        viewportSize = viewport.GetComponent<LayoutElement>();

        content.SetParent(viewportRect, false);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewport.GetComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
    }

    void FitViewport()
    {
        if (viewportSize == null || content == null) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        viewportSize.preferredHeight = Mathf.Min(LayoutUtility.GetPreferredHeight(content), MaxListHeight);
    }

    void Update()
    {
        if (Time.unscaledTime < nextRefresh)
        {
            return;
        }
        nextRefresh = Time.unscaledTime + refreshInterval;
        Refresh();
    }

    void Refresh()
    {
        List<NetworkPlayer> players = new List<NetworkPlayer>();
        foreach (NetworkPlayer player in NetworkPlayer.All())
        {
            if (player.Object != null && player.Object.IsValid)
            {
                players.Add(player);
            }
        }
        players.Sort((a, b) => a.Object.StateAuthority.PlayerId.CompareTo(b.Object.StateAuthority.PlayerId));

        NetworkRunner runner = players.Count > 0 ? players[0].Runner : null;
        bool localIsHost = runner != null && (runner.IsServer || runner.IsSharedModeMasterClient);
        int maxPlayers = runner != null && runner.SessionInfo != null && runner.SessionInfo.IsValid ? runner.SessionInfo.MaxPlayers : 0;

        StringBuilder signature = new StringBuilder();
        signature.Append(maxPlayers).Append('|').Append(localIsHost);
        foreach (NetworkPlayer player in players)
        {
            signature.Append('|').Append(player.Object.Id).Append(':').Append(player.nickName.ToString()).Append(':').Append(player.Object.HasStateAuthority);
        }
        if (signature.ToString() == lastSignature)
        {
            return;
        }
        lastSignature = signature.ToString();

        foreach (GameObject row in rows)
        {
            Destroy(row);
        }
        rows.Clear();

        foreach (NetworkPlayer player in players)
        {
            bool isLocal = player.Object.HasStateAuthority;
            string playerName = player.nickName.ToString();

            GameObject row = Instantiate(rowTemplate, listParent);
            row.SetActive(true);
            rows.Add(row);

            TextMeshProUGUI nameText = row.transform.Find("Name").GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI tagText = row.transform.Find("Tag").GetComponent<TextMeshProUGUI>();
            Image dot = row.transform.Find("Dot").GetComponent<Image>();

            nameText.text = string.IsNullOrWhiteSpace(playerName) ? "Player " + player.Object.StateAuthority.PlayerId : playerName;
            tagText.text = isLocal ? (localIsHost ? "You (host)" : "You") : "";
            dot.color = isLocal ? localDotColor : otherDotColor;
        }

        FitViewport();

        if (countText != null)
        {
            countText.text = maxPlayers > 0 ? players.Count + " / " + maxPlayers : players.Count.ToString();
        }
        if (emptyText != null)
        {
            emptyText.gameObject.SetActive(players.Count == 0);
        }
    }
}
