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
        gameModeHandler.gameMode = gameModeName;
        Debug.Log($"GameMode selected: {gameModeName}");
        gameModeSelectPanel.SetActive(false);
    }

    public void OpenPanel()
    {
        gameModeSelectPanel.SetActive(true);
    }

    public void ClosePanel()
    {
        gameModeSelectPanel.SetActive(false);
    }
}
