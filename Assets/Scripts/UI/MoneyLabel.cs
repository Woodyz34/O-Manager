using TMPro;
using UnityEngine;

// Shows the player's saved money and keeps it up to date.
public class MoneyLabel : MonoBehaviour
{
    public TextMeshProUGUI text;

    void OnEnable()
    {
        Money.Changed += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        Money.Changed -= Refresh;
    }

    void Start()
    {
        Refresh();
    }

    void Refresh()
    {
        if (text != null && Money.Instance != null)
        {
            text.text = Money.Instance.GetMoney() + " kr";
        }
    }
}
