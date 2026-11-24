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
    string joinLabel;

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
        // A lobby that is out on a competition can be joined to watch the race (2026-11-24); only one closed by an
        // older version of the game can not
        bool racing = SessionRacing.IsRacing(sessionInfo);
        if (racing)
        {
            playerCountText.text += " · racing";
        }
        if (!SessionRacing.CanJoin(sessionInfo))
        {
            isJoinButtonActive = false;
        }

        joinButton.gameObject.SetActive(isJoinButtonActive);
        TextMeshProUGUI joinText = joinButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (joinText != null)
        {
            if (joinLabel == null) joinLabel = joinText.text;
            joinText.text = racing && isJoinButtonActive ? "Watch" : joinLabel;
        }
        HoverTip.Add(joinButton, racing ? "Join this lobby and watch the race. You race with them from the next race on." : "Join this lobby.");
        HoverTip.Add(transform, !SessionRacing.CanJoin(sessionInfo) ? "This lobby is out racing. You can join it when it is back in the lobby."
            : sessionInfo.PlayerCount >= sessionInfo.MaxPlayers ? "This lobby is full."
            : racing ? "This lobby is out racing. Click Watch to see the race; you race with them from the next race on."
            : "Click Join to enter this lobby. The numbers show how many players are in it out of the most it can hold.");
    }

    public void OnClick()
    {
        OnJoinSession?.Invoke(sessionInfo);
    }

}
