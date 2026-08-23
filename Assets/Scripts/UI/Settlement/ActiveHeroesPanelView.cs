using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class ActiveHeroesPanelView : MonoBehaviour
{
    [SerializeField] private ActiveHeroCardView _activeHeroCardViewPrefab;
    [SerializeField] private RectTransform _activeHeroesContainer;
    [SerializeField] private TMP_Text _activeHeroesCountText; // current/limit

    private readonly List<ActiveHeroCardView> _activeHeroCardViews = new List<ActiveHeroCardView>();

    public void Render(ActiveHeroesPanelState state)
    {
        state ??= ActiveHeroesPanelState.Empty;

        if (_activeHeroesCountText != null)
            _activeHeroesCountText.text = $"{state.CurrentCount}/{state.Limit}";

        EnsureCardCount(state.Heroes.Count);
        var renderedCount = Mathf.Min(state.Heroes.Count, _activeHeroCardViews.Count);
        for (var index = 0; index < renderedCount; index++)
            _activeHeroCardViews[index]?.Render(state.Heroes[index]);
    }

    private void EnsureCardCount(int requiredCount)
    {
        if (_activeHeroCardViewPrefab == null)
        {
            if (requiredCount > 0)
                Debug.LogError("[ActiveHeroesPanelView] Active hero card prefab is not assigned.", this);
            return;
        }

        var parent = _activeHeroesContainer != null ? _activeHeroesContainer : transform;
        while (_activeHeroCardViews.Count < requiredCount)
            _activeHeroCardViews.Add(Instantiate(_activeHeroCardViewPrefab, parent));

        for (var index = _activeHeroCardViews.Count - 1; index >= requiredCount; index--)
        {
            var card = _activeHeroCardViews[index];
            _activeHeroCardViews.RemoveAt(index);
            if (card != null)
                Destroy(card.gameObject);
        }
    }
}
