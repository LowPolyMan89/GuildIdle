using UnityEngine;

public sealed class SettlementHudView : MonoBehaviour
{
    [SerializeField] private CurrencyPanelView _currencyPanelView;
    [SerializeField] private QuestPanelView _questPanelView;
    [SerializeField] private ActiveHeroesPanelView _activeHeroesPanelView;
    [SerializeField] private BottomNavigationPanelView _bottomNavigationPanelView;

    public void Render(SettlementHudState state)
    {
        state ??= SettlementHudState.Empty;
        _currencyPanelView?.Render(state.Currencies);
        _questPanelView?.Render(state.Stage, state.Quests);
        _activeHeroesPanelView?.Render(state.ActiveHeroes);
    }
}
