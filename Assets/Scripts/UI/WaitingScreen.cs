using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The page shown in the competition once you have picked your runner and the others are still picking.
// (2026-11-15) It was a fixed 1920x1080 picture, so on a wide screen the map showed beside it, and the text sat in the
// picture's middle. Now it covers the whole screen at any size: the race map dimmed behind a card that says who is
// still choosing, with a button to go back and pick another runner while the race has not started.
public class WaitingScreen : MonoBehaviour
{
    const string Tag = "WaitingForPlayersToConnect";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded += (scene, mode) =>
        {
            if (mode == LoadSceneMode.Additive) return;
            GameObject page;
            try { page = GameObject.FindGameObjectWithTag(Tag); }
            catch (UnityException) { return; }
            if (page != null && page.GetComponent<WaitingScreen>() == null) page.AddComponent<WaitingScreen>();
        };
    }

    TextMeshProUGUI heading;
    TextMeshProUGUI sub;
    TextMeshProUGUI players;
    Button changeButton;
    GameObject card;
    GameObject forest;
    float nextRefresh;

    void Awake()
    {
        RectTransform rect = (RectTransform)transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        // The old forest picture, its shade and its text give way to the dimmed map
        foreach (Transform child in transform) child.gameObject.SetActive(false);
        Image back = GetComponent<Image>();
        if (back != null)
        {
            back.sprite = null;
            back.color = new Color(0.04f, 0.1f, 0.07f, 0.72f);
            back.raycastTarget = true;   // the map under it is not clicked by mistake
        }

        // While waiting for the others, the forest picture covers the map, so nobody sees the course before the start
        // (2026-11-20, Anders)
        Sprite picture = Resources.Load<Sprite>("Sprites/e2bf7ebc-02a1-4b78-b52f-e0211eeac746");   // the lobby's forest
        if (picture != null)
        {
            Image cover = CourseUI.Panel("Forest", transform, Color.white, false);
            CourseUI.Stretch(cover.rectTransform);
            gameObject.AddComponent<RectMask2D>();
            Image art = CourseUI.Panel("Picture", cover.transform, Color.white, false);
            art.sprite = picture;
            art.raycastTarget = false;
            AspectRatioFitter fitter = art.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = picture.rect.width / Mathf.Max(1f, picture.rect.height);
            forest = cover.gameObject;
            forest.SetActive(false);
        }

        Image cardImage = CourseUI.CardPanel("Waiting card", transform);
        card = cardImage.gameObject;
        CourseUI.Place(cardImage.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 420));

        LoadingSpinner.Add(card.transform, new Vector2(1f, 1f), new Vector2(-56f, -56f), 20f, 8f);
        heading = CourseUI.Text("Heading", card.transform, "Waiting for the others", 46, CourseUI.Ink, FontStyles.Bold, TextAlignmentOptions.Center);
        CourseUI.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -36), new Vector2(700, 64));

        sub = CourseUI.Text("Subheading", card.transform, "The race starts when everyone has chosen a runner.", 24, CourseUI.InkMuted, FontStyles.Normal, TextAlignmentOptions.Center);
        CourseUI.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -102), new Vector2(700, 40));

        players = CourseUI.Text("Players", card.transform, "", 26, CourseUI.Ink, FontStyles.Normal, TextAlignmentOptions.Center);
        players.overflowMode = TextOverflowModes.Overflow;
        CourseUI.Place(players.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -156), new Vector2(700, 150));

        changeButton = CourseUI.Button("Change runner", card.transform, "Change runner", CourseUI.ButtonKind.Light, ChangeRunner, 26);
        CourseUI.Place((RectTransform)changeButton.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 32), new Vector2(320, 64));
        card.SetActive(false);
    }

    void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;

        NetworkPlayer local = NetworkPlayer.Local;
        // After End competition / leaving, the runner is shut down and despawned but Local may linger: its networked
        // properties throw when read then, so treat it as gone (2026-11-24)
        if (local != null && (local.Object == null || !local.Object.IsValid)) local = null;
        PathfindingManager manager = local != null ? local.manager : null;
        // Single player has nobody to wait for: only the dimmed map shows for the moment before the race
        bool multiplayer = manager != null && manager.multiplayer;
        AgentMovement agent = local != null ? local.GetComponent<AgentMovement>() : null;
        bool waiting = multiplayer && agent != null && agent.WaitingForAllPlayersToChooseRunner && local.HasChoosenRunner
            && (manager.ChooseRunnerPanel == null || !manager.ChooseRunnerPanel.activeSelf);
        if (card.activeSelf != waiting) card.SetActive(waiting);
        if (forest != null && forest.activeSelf != waiting) forest.SetActive(waiting);
        if (!waiting) return;
        // Same stats (Imposter, Secret task...): nobody picks a runner, so the ones not ready are still loading in, and
        // there is no runner to change to (2026-11-19)
        // Run it yourself, Route analysis and the Route choice game have no runners to pick either
        bool noPicking = GameModes.SameStatsLegs != null || (ExtraModes.Active & ExtraModes.NoRunners) != 0;
        sub.text = noPicking ? "The race starts when everyone is in." : "The race starts when everyone has chosen a runner.";
        if (changeButton.gameObject.activeSelf == noPicking) changeButton.gameObject.SetActive(!noPicking);

        StringBuilder text = new StringBuilder();
        List<string> ready = new List<string>();
        List<string> choosing = new List<string>();
        foreach (NetworkPlayer p in NetworkPlayer.All())
        {
            if (!NetworkPlayer.Races(p)) continue;
            string name = p.nickName.ToString();
            if (string.IsNullOrEmpty(name)) name = "Player";
            (p.HasChoosenRunner ? ready : choosing).Add(name);
        }
        if (choosing.Count > 0) text.Append(noPicking ? "<color=#E8742A><b>Still loading in:</b></color> " : "<color=#E8742A><b>Still choosing:</b></color> ").Append(string.Join(", ", choosing)).Append('\n');
        if (ready.Count > 0) text.Append("<color=#3F7D54><b>Ready:</b></color> ").Append(string.Join(", ", ready));
        players.text = text.ToString();
    }

    // Back to the runner picker. Only while the race has not started (it starts the moment everyone has chosen).
    void ChangeRunner()
    {
        NetworkPlayer local = NetworkPlayer.Local;
        if (local == null || local.manager == null || !local.UnchooseRunner()) return;
        card.SetActive(false);
        local.manager.ChooseRunnerPanel.SetActive(true);
        if (RelayRace.Active != null) RelayRace.Active.BeginPicking();
        if (IndividualRace.Active != null) IndividualRace.Active.ShowPickInfo(local.manager.ChooseRunnerPanel);
    }
}
