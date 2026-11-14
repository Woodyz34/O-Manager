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
        MakeScrollable();
        ClearList();
    }

    // The lobby list used to sit straight in its panel, so with many lobbies the ones at the bottom ended up
    // outside the screen. The list is put into a scroll view: a clipped viewport around it that the mouse wheel
    // and a drag both move.
    void MakeScrollable()
    {
        RectTransform list = verticalLayoutGroup.GetComponent<RectTransform>();
        if (list.parent != null && list.parent.GetComponentInParent<ScrollRect>() != null) return;

        GameObject viewObject = new GameObject("Lobby scroll", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        RectTransform view = (RectTransform)viewObject.transform;
        view.SetParent(list.parent, false);
        view.SetSiblingIndex(list.GetSiblingIndex());
        view.anchorMin = list.anchorMin;
        view.anchorMax = list.anchorMax;
        view.pivot = list.pivot;
        view.anchoredPosition = list.anchoredPosition;
        view.sizeDelta = list.sizeDelta;
        Image catcher = viewObject.GetComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0f);

        list.SetParent(view, false);
        list.anchorMin = new Vector2(0f, 1f);
        list.anchorMax = new Vector2(1f, 1f);
        list.pivot = new Vector2(0.5f, 1f);
        list.anchoredPosition = Vector2.zero;
        list.sizeDelta = Vector2.zero;
        ContentSizeFitter fitter = list.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = list.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewObject.GetComponent<ScrollRect>();
        scroll.viewport = view;
        scroll.content = list;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;
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
