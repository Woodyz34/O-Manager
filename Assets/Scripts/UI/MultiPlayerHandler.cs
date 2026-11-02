using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        PlayerPrefs.SetString("LobbyName", sessionNameinputField.text);
        PlayerPrefs.Save();
        networkRunnerHandler.CreateGame(sessionNameinputField.text, "Lobby");

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
