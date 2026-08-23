using TMPro;
using UnityEngine;

public class QuestItemView : MonoBehaviour
{
    [SerializeField] private TMP_Text _questName;
    [SerializeField] private TMP_Text _questDescription;

    public void Render(QuestItemState state)
    {
        if (_questName != null)
            _questName.text = state?.Name ?? string.Empty;
        if (_questDescription != null)
            _questDescription.text = state?.Description ?? string.Empty;
    }
}
