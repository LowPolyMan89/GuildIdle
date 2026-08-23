using System;
using GuildIdle.Activities;
using GuildIdle.Combat;
using GuildIdle.Crafting;
using GuildIdle.Localisation;
using GuildIdle.Player;
using GuildIdle.Progression;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;
using RuntimePlayer = GuildIdle.Player.Player;

[DisallowMultipleComponent]
[RequireComponent(typeof(SettlementHudView))]
public sealed class SettlementHudController : MonoBehaviour
{
    private const float RefreshIntervalSeconds = 0.5f;

    private SettlementHudView _view;
    private BottomNavigationPanelView _bottomNavigationPanelView;
    private ISettlementHudRuntimeSource _runtimeSource;
    private SettlementHudPresenter _presenter;
    private ProgressionRuntimeService _subscribedProgression;
    private float _nextRefreshTime;

    public event Action InventoryRequested;

    private void Awake()
    {
        Compose();
    }

    private void OnEnable()
    {
        Compose();

        RuntimeConfigs.OnLoaded += HandleRefreshRequested;
        LocalisationService.LanguageChanged += HandleLanguageChanged;
        OnlineActivityRuntime.Updated += HandleActivityUpdated;
        OnlineActivityRuntime.CombatAdvanced += HandleCombatAdvanced;
        PlayerRuntimeComposition.CraftStarted += HandleCraftStarted;
        PlayerRuntimeComposition.CombatStarted += HandleCombatStarted;
        PlayerRuntimeComposition.OfflineProcessed += HandleOfflineProcessed;

        if (_bottomNavigationPanelView != null)
            _bottomNavigationPanelView.InventoryRequested += HandleInventoryRequested;

        BindProgressionSubscription();
        _nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
        RefreshNow(true);
    }

    private void OnDisable()
    {
        RuntimeConfigs.OnLoaded -= HandleRefreshRequested;
        LocalisationService.LanguageChanged -= HandleLanguageChanged;
        OnlineActivityRuntime.Updated -= HandleActivityUpdated;
        OnlineActivityRuntime.CombatAdvanced -= HandleCombatAdvanced;
        PlayerRuntimeComposition.CraftStarted -= HandleCraftStarted;
        PlayerRuntimeComposition.CombatStarted -= HandleCombatStarted;
        PlayerRuntimeComposition.OfflineProcessed -= HandleOfflineProcessed;

        if (_bottomNavigationPanelView != null)
            _bottomNavigationPanelView.InventoryRequested -= HandleInventoryRequested;

        UnbindProgressionSubscription();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextRefreshTime)
            return;

        _nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
        RefreshNow();
    }

    public void RefreshNow(bool forceRender = false)
    {
        Compose();
        BindProgressionSubscription();
        _presenter.Refresh(forceRender);
    }

    private void Compose()
    {
        _view ??= GetComponent<SettlementHudView>();
        _bottomNavigationPanelView ??= _view.BottomNavigationPanelView;
        _runtimeSource ??= new RuntimeSettlementHudSource();
        _presenter ??= new SettlementHudPresenter(_view, _runtimeSource);
    }

    private void BindProgressionSubscription()
    {
        var progression = RuntimePlayer.Progression;
        if (ReferenceEquals(_subscribedProgression, progression))
            return;

        UnbindProgressionSubscription();
        _subscribedProgression = progression;
        if (_subscribedProgression != null)
            _subscribedProgression.Updated += HandleProgressionUpdated;
    }

    private void UnbindProgressionSubscription()
    {
        if (_subscribedProgression != null)
            _subscribedProgression.Updated -= HandleProgressionUpdated;
        _subscribedProgression = null;
    }

    private void HandleRefreshRequested()
    {
        RefreshNow(true);
    }

    private void HandleLanguageChanged(string language)
    {
        RefreshNow(true);
    }

    private void HandleActivityUpdated(ActivityRuntimeSnapshot snapshot)
    {
        RefreshNow();
    }

    private void HandleCombatAdvanced(OnlineCombatAdvanceResult result)
    {
        RefreshNow();
    }

    private void HandleCraftStarted(CraftStartedEvent startedEvent)
    {
        RefreshNow();
    }

    private void HandleCombatStarted(CombatStartedEvent startedEvent)
    {
        RefreshNow();
    }

    private void HandleOfflineProcessed(OfflineCoordinatorReport report)
    {
        RefreshNow();
    }

    private void HandleProgressionUpdated(ProgressionRuntimeUpdate update)
    {
        RefreshNow();
    }

    private void HandleInventoryRequested()
    {
        InventoryRequested?.Invoke();
    }
}
