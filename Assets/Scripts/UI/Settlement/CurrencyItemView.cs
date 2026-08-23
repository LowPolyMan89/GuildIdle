using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CurrencyItemView : MonoBehaviour
{
    [SerializeField] private Image _icon;
    [SerializeField] private TMP_Text _amount;

    public void Render(CurrencyItemState state)
    {
        if (_amount != null)
            _amount.text = state?.Amount ?? string.Empty;
    }
}
