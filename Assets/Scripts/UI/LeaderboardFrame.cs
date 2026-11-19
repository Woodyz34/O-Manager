using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The leaderboard during the race (2026-11-19, test night): it was too big on ordinary screens. Now it sits right at
// the left edge, a little smaller, and a tab beside it hides it into the edge (and back) and makes it bigger or
// smaller. The player's choice is remembered.
public class LeaderboardFrame : MonoBehaviour
{
    const string ScaleKey = "Leaderboard.Scale";
    const string HiddenKey = "Leaderboard.Hidden";
    const float DefaultScale = 0.8f, MinScale = 0.5f, MaxScale = 1.3f, Step = 0.1f;

    RectTransform panel;
    float width;
    float scale;
    bool hidden;
    Button toggle;

    public static void Attach(RectTransform panel)
    {
        if (panel == null || panel.GetComponent<LeaderboardFrame>() != null) return;
        panel.gameObject.AddComponent<LeaderboardFrame>().Setup(panel);
    }

    void Setup(RectTransform target)
    {
        panel = target;
        width = panel.rect.width;
        // Flush with the left edge of the screen, at the same height as before
        Vector2 min = panel.anchorMin, max = panel.anchorMax;
        panel.anchorMin = new Vector2(0f, min.y);
        panel.anchorMax = new Vector2(0f, max.y);
        panel.pivot = new Vector2(0f, panel.pivot.y);
        panel.sizeDelta = new Vector2(width, panel.sizeDelta.y);
        scale = Mathf.Clamp(PlayerPrefs.GetFloat(ScaleKey, DefaultScale), MinScale, MaxScale);
        hidden = PlayerPrefs.GetInt(HiddenKey, 0) == 1;

        // The tab on the right side of the card
        Image tab = CourseUI.Panel("Leaderboard tab", panel, CourseUI.Card);
        CourseUI.Place(tab.rectTransform, new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(2f, 0f), new Vector2(46f, 150f));
        tab.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;   // the card may have a layout of its own
        CourseUI.Column(tab.gameObject, 4, new RectOffset(4, 4, 4, 4));
        toggle = CourseUI.Button("Hide", tab.transform, "<", CourseUI.ButtonKind.Secondary, () =>
        {
            hidden = !hidden;
            Save();
            Apply();
        }, 24);
        CourseUI.Size(toggle, 44);
        Button bigger = CourseUI.Button("Bigger", tab.transform, "+", CourseUI.ButtonKind.Light, () => Resize(Step), 24);
        CourseUI.Size(bigger, 44);
        Button smaller = CourseUI.Button("Smaller", tab.transform, "-", CourseUI.ButtonKind.Light, () => Resize(-Step), 24);
        CourseUI.Size(smaller, 44);
        HoverTip.Add(toggle, "Hide the leaderboard in the edge of the screen, or show it again.");
        HoverTip.Add(bigger, "Make the leaderboard bigger.");
        HoverTip.Add(smaller, "Make the leaderboard smaller.");
        Apply();
    }

    void Resize(float change)
    {
        scale = Mathf.Clamp(scale + change, MinScale, MaxScale);
        Save();
        Apply();
    }

    void Save()
    {
        PlayerPrefs.SetFloat(ScaleKey, scale);
        PlayerPrefs.SetInt(HiddenKey, hidden ? 1 : 0);
        PlayerPrefs.Save();
    }

    void Apply()
    {
        if (panel == null) return;
        panel.localScale = new Vector3(scale, scale, 1f);
        panel.anchoredPosition = new Vector2(hidden ? -width * scale : 0f, panel.anchoredPosition.y);
        if (toggle != null) CourseUI.SetButtonText(toggle, hidden ? ">" : "<");
    }
}

// A row of the leaderboard: click to watch that player's runner (your own row: back to your own). The host can
// right-click someone else's row to take them out of the game (someone away from the keyboard).
public class LeaderboardRow : MonoBehaviour, IPointerClickHandler
{
    public NetworkPlayer player;
    bool tipAdded;

    public void Set(NetworkPlayer who)
    {
        player = who;
        if (tipAdded) return;
        tipAdded = true;
        HoverTip.Add(this, () =>
        {
            string tip = "Click to watch this runner. Click your own row to come back to your own.";
            if (CompetitionFlow.LocalIsHost() && player != null && player.Object != null && player.Object.IsValid && !player.Object.HasStateAuthority)
            {
                tip += " Right-click to take the player out of the game.";
            }
            return tip;
        });
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (player == null || player.Object == null || !player.Object.IsValid) return;
        NetworkPlayer local = NetworkPlayer.Local;
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            CameraController camera = local != null && local.agentMovement != null ? local.agentMovement.CamController : null;
            if (camera != null) camera.ShowRunner(player.transform);
            return;
        }
        if (eventData.button != PointerEventData.InputButton.Right) return;
        if (local == null || !CompetitionFlow.LocalIsHost() || player.Object.HasStateAuthority) return;
        NetworkPlayer target = player;
        string name = BearMode.Name(target);
        ChoicePopup.Open("Kick " + name + "?", new[]
        {
            new ChoicePopup.Option
            {
                id = "kick",
                title = "Kick " + name,
                description = "They leave the game and go back to the main menu. The race goes on without them."
            }
        }, null, id =>
        {
            if (id != "kick" || target == null || target.Object == null || !target.Object.IsValid) return;
            NetworkPlayer host = NetworkPlayer.Local;
            if (host != null) host.RPC_Kick(target.Object.StateAuthority);
        }, "Only the host can do this, for example when someone is away from the keyboard.");
    }
}
