using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using Fusion;
using UnityEngine;
using System;

public class SessionInfoListUIItem : MonoBehaviour
{
    public TextMeshProUGUI sessionNameText;
    public TextMeshProUGUI playerCountText;
    public Button joinButton;

    SessionInfo sessionInfo;

    public event Action<SessionInfo> OnJoinSession;

    public void SetInformation(SessionInfo sessionInfo)
    {
        this.sessionInfo = sessionInfo;

        sessionNameText.text = sessionInfo.Name;
        playerCountText.text = $"{sessionInfo.PlayerCount.ToString()}/{sessionInfo.MaxPlayers.ToString()}";

        bool isJoinButtonActive = true;

        if(sessionInfo.PlayerCount >= sessionInfo.MaxPlayers)
        {
            isJoinButtonActive = false;
        }
        // A lobby that is out on a competition can not be joined until it is back in the lobby
        if (!sessionInfo.IsOpen)
        {
            isJoinButtonActive = false;
            playerCountText.text += " · racing";
        }

        joinButton.gameObject.SetActive(isJoinButtonActive);
        HoverTip.Add(joinButton, "Join this lobby.");
        HoverTip.Add(transform, !sessionInfo.IsOpen ? "This lobby is out racing. You can join it when it is back in the lobby."
            : sessionInfo.PlayerCount >= sessionInfo.MaxPlayers ? "This lobby is full."
            : "Click Join to enter this lobby. The numbers show how many players are in it out of the most it can hold.");
    }

    public void OnClick()
    {
        OnJoinSession?.Invoke(sessionInfo);
    }

}
