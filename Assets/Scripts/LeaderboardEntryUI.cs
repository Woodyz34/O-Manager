using TMPro;
using UnityEngine;

public class LeaderboardEntryUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI positionText;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI timeText;

    public void SetData(int position, string name, string time)
    {
        positionText.text = position.ToString();
        nameText.text = name;
        timeText.text = time;
    }

    public void UpdateTime(string time)
    {
        timeText.text = time;
    }
}
