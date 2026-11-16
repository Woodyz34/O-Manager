using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LeaderboardEntryUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI positionText;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI timeText;

    // Two lines since 2026-11-16: the player on top, the runner and its physics/speed/technique under it
    public const float RowHeight = 54f;
    const int MaxPlayerChars = 18;
    const int MaxRunnerChars = 20;

    bool fitted;

    void Fit()
    {
        if (fitted) return;
        fitted = true;
        LayoutElement row = GetComponent<LayoutElement>();
        if (row != null)
        {
            row.minHeight = RowHeight;
            row.preferredHeight = RowHeight;
        }
        if (nameText != null)
        {
            // Long names get smaller, and if still too long end in "..."
            nameText.enableWordWrapping = false;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            nameText.enableAutoSizing = true;
            nameText.fontSizeMax = 20f;
            nameText.fontSizeMin = 12f;
            nameText.lineSpacing = -12f;
            nameText.alignment = TextAlignmentOptions.MidlineLeft;
        }
    }

    public void SetData(int position, string name, string time)
    {
        Fit();
        positionText.text = position.ToString();
        nameText.text = name;
        timeText.text = time;
    }

    // player: the player's nickname; runner: the runner raced with ("" or the same name: not shown twice);
    // stats: "P85  S90  T70" ("" when not known yet)
    public void SetData(int position, string player, string runner, string stats, string time)
    {
        positionText.text = position.ToString();
        SetNames(player, runner, stats);
        timeText.text = time;
    }

    string shownNames;

    public void SetNames(string player, string runner, string stats)
    {
        string key = player + "|" + runner + "|" + stats;
        if (key == shownNames) return;
        shownNames = key;
        Fit();
        stats = stats ?? "";
        string full = string.IsNullOrWhiteSpace(player) ? "Player" : player.Trim();
        // Same stats: the runner has the player's own name, so it is not shown twice
        bool showRunner = !string.IsNullOrWhiteSpace(runner) && runner.Trim() != full;
        player = Short(full, MaxPlayerChars);
        string second = (showRunner ? Short(runner.Trim(), MaxRunnerChars) : "")
                        + (showRunner && stats.Length > 0 ? "  " : "") + stats;
        nameText.text = second.Length > 0
            ? "<b>" + Escape(player) + "</b>\n<size=78%><color=#5B7563>" + Escape(second) + "</color></size>"
            : "<b>" + Escape(player) + "</b>";
    }

    static string Short(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 2).TrimEnd() + "...";

    static string Escape(string s) => s.Replace("<", "<​");

    public void UpdateTime(string time)
    {
        timeText.text = time;
    }
}
