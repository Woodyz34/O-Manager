using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameModeSelect : MonoBehaviour
{
    public GameObject gameModeButtonPrefab;
    public Transform gameModeListParent;
    public GameModeHandler gameModeHandler;
    public GameObject gameModeSelectPanel;

    private void Start()
    {
        PopulateGameModeList();
        // A pop-up card over the lobby, so its Close button doesn't cover "Leave lobby"
        CourseUI.MakePopup(gameModeSelectPanel, gameModeListParent.parent as RectTransform, 184f);
        gameModeSelectPanel.SetActive(false);
    }

    void PopulateGameModeList()
    {
        foreach (Transform child in gameModeListParent)
            Destroy(child.gameObject);

        foreach (Texture2D texture in gameModeHandler.gameModes)
        {
            Debug.Log("GameMode name: " + texture.name);

            GameObject btnObj = Instantiate(gameModeButtonPrefab, gameModeListParent);
            TMP_Text txt = btnObj.GetComponentInChildren<TMP_Text>();
            Image image = btnObj.GetComponentInChildren<Image>();
            Debug.Log("image finns " + image);
            Debug.Log("txt finns " + txt);

            txt.text = texture.name;
            Debug.Log(texture.height + " height and this is width " + texture.width + " taxture name " + texture.name);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f)
            );

            image.rectTransform.sizeDelta = new Vector2(texture.width, texture.height);

            image.sprite = sprite;

            Button btn = btnObj.GetComponent<Button>();
            string gameModeNameCopy = texture.name;
            btn.onClick.AddListener(() => SelectGameMode(gameModeNameCopy));
        }

        // All the modes side by side, shrunk to fit the pop-up, so none is hidden below the edge
        GridLayoutGroup grid = gameModeListParent.GetComponent<GridLayoutGroup>();
        RectTransform content = gameModeListParent as RectTransform;
        int count = gameModeHandler.gameModes.Count;
        if (grid != null && content != null && count > 0)
        {
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = count;
            float width = count * grid.cellSize.x + (count - 1) * grid.spacing.x + grid.padding.left + grid.padding.right;
            content.sizeDelta = new Vector2(width, content.sizeDelta.y);
            float scale = Mathf.Min(1f, 1700f / width);
            content.localScale = new Vector3(scale, scale, 1f);
        }
    }

    public void SelectGameMode(string gameModeName)
    {
        Choose(gameModeName, gameModeHandler.gameMode, mode => gameModeHandler.gameMode = mode);
        Debug.Log($"GameMode selected: {gameModeName}");
        gameModeSelectPanel.SetActive(false);
    }

    // A runner pool picked in the pop-up. Shared with the single player page, so both offer the same pools and
    // the same follow-up windows. set gets the new mode string (maybe later, when a follow-up window is closed).
    public static void Choose(string pool, string current, System.Action<string> set)
    {
        // "Same stats": the values are set first (the mode only changes when they press "Use these values")
        if (pool == GameModes.SameStatsMode)
        {
            SameStatsPopup.Open(current, set);
            return;
        }
        // "Random runners": how many each player gets
        if (pool == GameModes.RandomMode)
        {
            RandomCountPopup.Open(current, set);
            return;
        }
        // Your runners and Any runner keep the max average overall the host has set
        set(GameModes.WithCap(pool, GameModes.Cap(current)));
    }

    // "Runner pool" in the lobby: your own runners, any runner in the game, or any runner with the host's values.
    // A small pop-up with the choices replaced the old panel of pictures (kept in the scene, unused).
    public void OpenPanel()
    {
        // Imposter and Secret task are always raced with Same stats: only its values can be changed
        LobbySettingsUI settings = FindObjectOfType<LobbySettingsUI>();
        if (settings != null && (settings.modes & ExtraModes.ImposterKinds) != 0)
        {
            UIToast.Show("Imposter and Secret task are raced with Same stats. Turn them off to pick another runner pool.", 4f);
            SameStatsPopup.Open(gameModeHandler.gameMode, mode =>
            {
                if (GameModes.IsSameStats(mode)) gameModeHandler.gameMode = mode;
            });
            return;
        }
        // Your runners and Any runner can have a max average overall (CapPopup; lobby only)
        string current = gameModeHandler.gameMode;
        string extra = null;
        System.Action openCap = null;
        // Only for relays (2026-11-16): in an individual race it only shut out the good runners
        CourseData lobbyCourse = CourseLibrary.Current;
        if (GameModes.CanHaveCap(current) && lobbyCourse != null && lobbyCourse.IsRelay())
        {
            int cap = GameModes.Cap(current);
            extra = "Max average overall: " + (cap > 0 ? cap.ToString() : "off");
            openCap = () => CapPopup.Open(gameModeHandler.gameMode, mode => gameModeHandler.gameMode = mode);
        }
        ChoicePopup.Open("Runner pool", PoolOptions(current), GameModes.PoolId(current), SelectGameMode, null, extra, openCap);
    }

    // The runner pools to pick from (the lobby and single player offer the same ones)
    public static ChoicePopup.Option[] PoolOptions(string current)
    {
        string[] pools = GameModes.LobbyPools;
        ChoicePopup.Option[] options = new ChoicePopup.Option[pools.Length];
        for (int i = 0; i < pools.Length; i++)
        {
            string pool = pools[i];
            // The chosen "Same stats" shows its values
            // The chosen pool shows its values or its max average
            string title = (pool == GameModes.SameStatsMode && GameModes.IsSameStats(current)) || pool == GameModes.PoolId(current)
                ? GameModes.PoolName(current) : GameModes.PoolName(pool);
            options[i] = new ChoicePopup.Option { id = pool, title = title, description = GameModes.PoolDescription(pool) };
        }
        return options;
    }

    public void ClosePanel()
    {
        gameModeSelectPanel.SetActive(false);
    }
}
