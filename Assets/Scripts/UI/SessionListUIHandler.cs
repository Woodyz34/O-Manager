using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using Fusion;
using UnityEngine;
using System;

public class SessionListUIHandler : MonoBehaviour
{
    public TextMeshProUGUI statusText;
    public GameObject sessionItemListPrefab;
    public VerticalLayoutGroup verticalLayoutGroup;

    public bool dontCreateGame = true;

    private void Awake()
    {
        ClearList();
    }
    public void ClearList()
    {
        foreach (Transform child in verticalLayoutGroup.transform)
        {
            Destroy(child.gameObject);
        }

        statusText.gameObject.SetActive(false);
    }

    public void AddToList(SessionInfo sessionInfo)
    {
        SessionInfoListUIItem addedSessionInfoListUIItem  = Instantiate(sessionItemListPrefab, verticalLayoutGroup.transform).GetComponent<SessionInfoListUIItem>();

        addedSessionInfoListUIItem.SetInformation(sessionInfo);

        addedSessionInfoListUIItem.OnJoinSession += AddedSessionInfoListUIItem_OnJoinSession;

    }
    private void AddedSessionInfoListUIItem_OnJoinSession(SessionInfo sessionInfo)
    {
        NetworkRunnerHandler networkRunnerHandler = FindObjectOfType<NetworkRunnerHandler>();

        networkRunnerHandler.JoinGame(sessionInfo);

        MultiPlayerHandler multiPlayerHandler = FindObjectOfType<MultiPlayerHandler>();

        multiPlayerHandler.OnJoiningServer();
    }

    public void OnNoSessionsFound()
    {
        ClearList();
        statusText.text = "No game lobbys found";
        statusText.gameObject.SetActive(true);
        dontCreateGame = false;
    }
    public void OnLookingForGameLobby()
    {
        ClearList();
        statusText.text = "Looking for a Lobby";
        statusText.gameObject.SetActive(true);
    }
}
