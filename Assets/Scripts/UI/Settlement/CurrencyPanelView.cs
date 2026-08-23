using System.Collections.Generic;
using UnityEngine;

public class CurrencyPanelView : MonoBehaviour
{
    [SerializeField] private CurrencyItemView _currencyItemViewPrefab;
    [SerializeField] private List<CurrencyItemView> _currencyItemViews = new List<CurrencyItemView>();

    public void Render(IReadOnlyList<CurrencyItemState> items)
    {
        items ??= System.Array.Empty<CurrencyItemState>();
        if (!EnsureItemCount(items.Count))
            return;

        for (var index = 0; index < items.Count; index++)
            _currencyItemViews[index].Render(items[index]);
    }

    private bool EnsureItemCount(int count)
    {
        _currencyItemViews.RemoveAll(view => view == null);

        if (_currencyItemViewPrefab == null)
        {
            if (count > 0)
                Debug.LogError($"[CurrencyPanelView] Currency item prefab is not assigned on '{name}'.", this);
            return count == 0;
        }

        while (_currencyItemViews.Count < count)
            _currencyItemViews.Add(Instantiate(_currencyItemViewPrefab, transform, false));

        while (_currencyItemViews.Count > count)
        {
            var lastIndex = _currencyItemViews.Count - 1;
            var view = _currencyItemViews[lastIndex];
            _currencyItemViews.RemoveAt(lastIndex);
            if (view != null)
                Destroy(view.gameObject);
        }

        return true;
    }
}
