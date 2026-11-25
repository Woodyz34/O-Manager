using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The Monday relay (MondayRelay) on the main menu: a card in the top left corner with this week's name and the time
// left. From 20:55 its button joins the lobby; while the relay is on, it lets you watch.
public class MondayRelayCard : MonoBehaviour
{
    TextMeshProUGUI nameText;
    TextMeshProUGUI whenText;
    Button button;
    float nextRefresh;

    public static void Add(Transform home)
    {
        if (home == null || home.Find("Monday relay") != null) return;
        Image card = CourseUI.CardPanel("Monday relay", home);
        CourseUI.Place(card.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48, -48), new Vector2(440, 176));
        MondayRelayCard view = card.gameObject.AddComponent<MondayRelayCard>();

        TextMeshProUGUI caption = CourseUI.Text("Caption", card.transform, "MONDAY RELAY", 16, CourseUI.InkMuted, FontStyles.Bold);
        caption.characterSpacing = 6f;
        CourseUI.Place(caption.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24, -16), new Vector2(392, 24));

        view.nameText = CourseUI.Text("Name", card.transform, "", 24, CourseUI.Ink, FontStyles.Bold);
        view.nameText.enableWordWrapping = false;
        view.nameText.enableAutoSizing = true;
        view.nameText.fontSizeMin = 16;
        view.nameText.fontSizeMax = 24;
        CourseUI.Place(view.nameText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24, -40), new Vector2(392, 34));

        view.whenText = CourseUI.Text("When", card.transform, "", 18, CourseUI.InkMuted);
        CourseUI.Place(view.whenText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24, -74), new Vector2(392, 28));

        view.button = CourseUI.Button("Join", card.transform, "Join", CourseUI.ButtonKind.Primary, view.Join, 22);
        CourseUI.Place((RectTransform)view.button.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24, 18), new Vector2(392, 50));

        HoverTip.Add(card, "Every Monday at 21:00 (Swedish time) a big relay for everyone. The lobby opens 5 minutes before. "
            + "Vote for one of three relays made by the players; every leg has a class (D18, H20 ...) that only runners of that class may run.");
        view.Refresh();
    }

    void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.5f;
        Refresh();
    }

    void Refresh()
    {
        DateTime now = MondayRelay.NowUtc;
        MondayRelay.Event e = MondayRelay.Current(now);
        nameText.text = e.FunName;
        DateTime local = MondayRelay.SwedishTime(e.startUtc);
        string day = local.ToString("ddd d MMM", CultureInfo.InvariantCulture) + " at 21:00";
        bool canJoin = false;
        string label;
        if (now < e.OpensUtc)
        {
            whenText.text = day + "  ·  in " + MondayRelay.Countdown(e.startUtc - now);
            label = "The lobby opens at 20:55";
        }
        else if (now < e.startUtc)
        {
            whenText.text = "Starts in " + MondayRelay.Countdown(e.startUtc - now) + "  ·  vote for the relay now";
            label = "Join the relay";
            canJoin = true;
        }
        else if (now < e.LateStartUtc)
        {
            whenText.text = "Starting now";
            label = "Join";
            canJoin = true;
        }
        else
        {
            whenText.text = "On now. Next: " + MondayRelay.Next(now).FunName;
            label = "Watch";
            canJoin = true;
        }
        CourseUI.SetButtonText(button, label);
        button.interactable = canJoin;
    }

    void Join()
    {
        DateTime now = MondayRelay.NowUtc;
        MondayRelay.Event e = MondayRelay.Current(now);
        if (now < e.OpensUtc) return;
        MondayRelay.AutoJoin = true;
        SceneLoader.Load("MultiPlayer", "Joining " + e.FunName);
    }
}
