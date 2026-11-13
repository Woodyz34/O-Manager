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
        // "Same stats": the host sets the values first (the mode only changes when they press "Use these values")
        if (gameModeName == GameModes.SameStatsMode)
        {
            SameStatsPopup.Open(gameModeHandler.gameMode, mode => gameModeHandler.gameMode = mode);
            return;
        }
        // Your runners and Any runner keep the max average overall the host has set
        gameModeHandler.gameMode = GameModes.WithCap(gameModeName, GameModes.Cap(gameModeHandler.gameMode));
        Debug.Log($"GameMode selected: {gameModeHandler.gameMode}");
        gameModeSelectPanel.SetActive(false);
    }

    // "Runner pool" in the lobby: your own runners, any runner in the game, or any runner with the host's values.
    // A small pop-up with the choices replaced the old panel of pictures (kept in the scene, unused).
    public void OpenPanel()
    {
        string[] pools = GameModes.LobbyPools;
        ChoicePopup.Option[] options = new ChoicePopup.Option[pools.Length];
        for (int i = 0; i < pools.Length; i++)
        {
            string pool = pools[i];
            // The chosen "Same stats" shows its values
            string title = (pool == GameModes.SameStatsMode && GameModes.IsSameStats(gameModeHandler.gameMode))
                || (pool == GameModes.PoolId(gameModeHandler.gameMode))
                ? GameModes.PoolName(gameModeHandler.gameMode) : GameModes.PoolName(pool);
            options[i] = new ChoicePopup.Option { id = pool, title = title, description = GameModes.PoolDescription(pool) };
        }
        // Your runners and Any runner can have a max average overall (CapPopup)
        string extra = null;
        System.Action openCap = null;
        if (GameModes.CanHaveCap(gameModeHandler.gameMode))
        {
            int cap = GameModes.Cap(gameModeHandler.gameMode);
            extra = "Max average overall: " + (cap > 0 ? cap.ToString() : "off");
            openCap = () => CapPopup.Open(gameModeHandler.gameMode, mode => gameModeHandler.gameMode = mode);
        }
        ChoicePopup.Open("Runner pool", options, GameModes.PoolId(gameModeHandler.gameMode), SelectGameMode, extra, openCap);
    }

    public void ClosePanel()
    {
        gameModeSelectPanel.SetActive(false);
    }
}
