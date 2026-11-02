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

    // Each runner can only be owned once, so the copies badge stays hidden
    public void Show(CardData card)
    {
        view.Show(card);
        if (copiesBadge != null) copiesBadge.SetActive(false);
    }
}
