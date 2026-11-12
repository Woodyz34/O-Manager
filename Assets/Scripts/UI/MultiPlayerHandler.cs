using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Fusion;

public class MultiPlayerHandler : MonoBehaviour
{

    [Header("Panels")]
    public GameObject playerDetailPanel;
    public GameObject sessionBrowserPanel;
    public GameObject createSessionPanel;
    public GameObject statusPanel;
    // Shown while a lobby is being created or joined. It replaced the status panel, which was an empty box.
    public GameObject loadingScreen;

    [Header("Player settings")]
    public TMP_InputField playerNameinputField;

    [Header("New game session")]
    public TMP_InputField sessionNameinputField;

    SessionListUIHandler SessionListUIHandler;
    // Start is called before the first frame update
    void Start()
    {
        // The names from last time, so they don't have to be written again ("PlayerNickname" is what older versions saved)
        string savedName = PlayerPrefs.GetString("PlayerNickName", PlayerPrefs.GetString("PlayerNickname", ""));
        if (!string.IsNullOrEmpty(savedName))
        {
            playerNameinputField.text = savedName;
        }
        string savedSession = PlayerPrefs.GetString("LobbyName", "");
        if (!string.IsNullOrEmpty(savedSession) && sessionNameinputField != null)
        {
            sessionNameinputField.text = savedSession;
        }
        MakeBackButton();
    }

    // "Back" in the top left corner on every step of this page: leaves the lobby list and goes back to
    // the page where you pick single player or multiplayer. Made here so the scene needs no rebuild.
    void MakeBackButton()
    {
        Canvas canvas = playerDetailPanel != null ? playerDetailPanel.GetComponentInParent<Canvas>(true) : null;
        if (canvas == null) return;
        Transform root = canvas.rootCanvas.transform;
        Button back = CourseUI.Button("Back", root, "Back", CourseUI.ButtonKind.Light, OnBackClicked, 28);
        CourseUI.Place((RectTransform)back.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(48, -48), new Vector2(170, 60));
        Shadow shadow = back.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.12f, 0.24f, 0.17f, 0.3f);
        shadow.effectDistance = new Vector2(0, -4);
        // Above the panels, below the loading page
        back.transform.SetAsLastSibling();
        if (loadingScreen != null && loadingScreen.transform.parent == root) loadingScreen.transform.SetAsLastSibling();
    }

    public void OnBackClicked()
    {
        // Leave the lobby list (or a lobby being made) before changing page
        NetworkRunner running = null;
        foreach (NetworkRunner runner in FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None))
        {
            if (runner.IsRunning) running = runner;
            else Destroy(runner.gameObject);
        }
        SceneLoader.LeaveSession(running, "NewCompetition");
    }
    void HideAllPanels()
    {
        playerDetailPanel.SetActive(false);
        sessionBrowserPanel.SetActive(false);
        createSessionPanel.SetActive(false);
        statusPanel.SetActive(false);
    }
    public void OnFindGameClicked()
    {
        PlayerPrefs.SetString("PlayerNickName", playerNameinputField.text);
        PlayerPrefs.Save();

        GameManager.Instance.playerNickName = playerNameinputField.text;

        NetworkRunnerHandler networkRunnerHandler = FindObjectOfType<NetworkRunnerHandler>();

        networkRunnerHandler.OnJoinLobby();

        HideAllPanels();
        
        sessionBrowserPanel.SetActive(true);

        FindObjectOfType<SessionListUIHandler>(true).OnLookingForGameLobby();
    }

    public void OnCreateNewGameClicked()
    {
        HideAllPanels();

        createSessionPanel.SetActive(true);
    }

    public void OnReturnToSessionListClicked()
    {
        HideAllPanels();

        sessionBrowserPanel.SetActive(true);
    }

    public void OnStartNewSessionClicked()
    {
        if(FindObjectOfType<SessionListUIHandler>(true).dontCreateGame == true)
        {
            return;
        }
        NetworkRunnerHandler networkRunnerHandler = FindObjectOfType<NetworkRunnerHandler>();

        // A lobby without a name would be matched with any open lobby by the game servers, so it always gets one
        string sessionName = sessionNameinputField.text != null ? sessionNameinputField.text.Trim() : "";
        if (sessionName.Length == 0)
        {
            string host = GameManager.Instance != null ? GameManager.Instance.playerNickName : "";
            sessionName = (string.IsNullOrWhiteSpace(host) ? "Player" : host.Trim()) + "'s lobby";
        }
        PlayerPrefs.SetString("LobbyName", sessionName);
        PlayerPrefs.Save();
        networkRunnerHandler.CreateGame(sessionName, "Lobby");

        HideAllPanels();

        ShowLoading("Creating lobby");
    }

    public void OnJoiningServer()
    {
        HideAllPanels();

        ShowLoading("Joining lobby");
    }

    void ShowLoading(string message)
    {
        if (loadingScreen == null)
        {
            return;
        }
        LoadingScreenUI loading = loadingScreen.GetComponent<LoadingScreenUI>();
        if (loading != null)
        {
            loading.SetMessage(message);
        }
        loadingScreen.SetActive(true);
    }
}
