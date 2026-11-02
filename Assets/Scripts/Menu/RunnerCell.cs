using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One card in the My Runners grid.
public class RunnerCell : MonoBehaviour
{
    public RunnerCardView view;
    public Button button;
    public GameObject copiesBadge;
    public TextMeshProUGUI copiesText;

    public void Show(CardData card, int copies)
    {
        view.Show(card);
        if (copiesBadge != null) copiesBadge.SetActive(copies > 1);
        if (copiesText != null) copiesText.text = "x" + copies;
    }
}
