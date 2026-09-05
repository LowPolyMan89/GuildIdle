using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GuildIdle.Activities;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Crafting;
using GuildIdle.Player;
using GuildIdle.Settlement;
using GuildIdle.UI.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RuntimeConfigs = GuildIdle.Configs.Configs;
using RuntimePlayer = GuildIdle.Player.Player;

public sealed class ObjectActivitiesWindowView : UIWindow,
    IUIOpenArgsReceiver<ObjectActivitiesWindowOpenArgs>,
    IUIStateView<ObjectActivitiesWindowState>
{
    [SerializeField] private ActivityCardView _activityCardViewPrefab;
    [SerializeField] private List<ActivityCardView> _activityCards = new List<ActivityCardView>();
    [SerializeField] private Button _closeButton;
    [SerializeField] private RectTransform _activitiesContainer;
    [SerializeField] private ObjectActivitiesWindowInfo _windowInfo;
    [SerializeField] private SelectedActivityInfo _selectedActivityInfo;

    private ObjectActivitiesWindowState _state = ObjectActivitiesWindowState.Empty;
    private Action _closeRequested;
    private Action _closed;
    private Action<string> _activitySelected;
    private Action<int> _cyclesChanged;
    private Action _heroRequested;
    private Action _primaryActionRequested;

    public void ApplyOpenArgs(ObjectActivitiesWindowOpenArgs args)
    {
        if (args == null)
            throw new ArgumentNullException(nameof(args));

        _state = args.State ?? ObjectActivitiesWindowState.Empty;
        _closeRequested = args.CloseRequested;
        _closed = args.Closed;
        _activitySelected = args.ActivitySelected;
        _cyclesChanged = args.CyclesChanged;
        _heroRequested = args.HeroRequested;
        _primaryActionRequested = args.PrimaryActionRequested;
    }

    public void Render(ObjectActivitiesWindowState state)
    {
        _state = state ?? ObjectActivitiesWindowState.Empty;
        _windowInfo?.Render(_state);
        _selectedActivityInfo?.Render(_state.SelectedActivity);

        var activities = _state.Activities ?? Array.Empty<ActivityCardState>();
        EnsureCardCapacity(activities.Count);
        for (var index = 0; index < _activityCards.Count; index++)
        {
            var card = _activityCards[index];
            if (card == null)
                continue;

            var isUsed = index < activities.Count;
            card.gameObject.SetActive(isUsed);
            if (isUsed)
                card.Render(activities[index], _activitySelected);
        }
    }

    protected override void OnBind()
    {
        BindButton(_closeButton, HandleCloseRequested);
        BindButton(_selectedActivityInfo?.HeroButton, HandleHeroRequested);
        BindButton(_selectedActivityInfo?.PrimaryButton, HandlePrimaryActionRequested);
        if (_selectedActivityInfo?.CyclesSlider != null)
        {
            _selectedActivityInfo.CyclesSlider.onValueChanged.AddListener(HandleCyclesChanged);
            RegisterCleanup(() =>
            {
                if (_selectedActivityInfo?.CyclesSlider != null)
                    _selectedActivityInfo.CyclesSlider.onValueChanged.RemoveListener(HandleCyclesChanged);
            });
        }
    }

    protected override void OnShow() => Render(_state);

    protected override void OnHide()
    {
        var closed = _closed;
        _closed = null;
        _closeRequested = null;
        _activitySelected = null;
        _cyclesChanged = null;
        _heroRequested = null;
        _primaryActionRequested = null;
        closed?.Invoke();
    }

    private void BindButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;
        button.onClick.AddListener(action);
        RegisterCleanup(() =>
        {
            if (button != null)
                button.onClick.RemoveListener(action);
        });
    }

    private void EnsureCardCapacity(int requiredCount)
    {
        _activityCards ??= new List<ActivityCardView>();
        _activityCards.RemoveAll(card => card == null);
        if (requiredCount <= _activityCards.Count)
            return;

        if (_activityCardViewPrefab == null || _activitiesContainer == null)
        {
            Debug.LogError($"[{nameof(ObjectActivitiesWindowView)}] Activity card prefab and container must be assigned.", this);
            return;
        }

        while (_activityCards.Count < requiredCount)
        {
            var card = Instantiate(_activityCardViewPrefab, _activitiesContainer, false);
            card.gameObject.SetActive(false);
            _activityCards.Add(card);
        }
    }

    private void HandleCloseRequested() => _closeRequested?.Invoke();
    private void HandleCyclesChanged(float value) => _cyclesChanged?.Invoke(Mathf.Max(1, Mathf.RoundToInt(value)));
    private void HandleHeroRequested() => _heroRequested?.Invoke();
    private void HandlePrimaryActionRequested() => _primaryActionRequested?.Invoke();

    [Serializable]
    public sealed class ObjectActivitiesWindowInfo
    {
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _objectName;
        [SerializeField] private TMP_Text _objectLevel;
        [SerializeField] private TMP_Text _objectDescription;
        [SerializeField] private Image _objectImage;
        [SerializeField] private TMP_Text _availableCount;

        public void Render(ObjectActivitiesWindowState state)
        {
            SetText(_title, ActivityUiText.Get(ActivityUiText.WindowTitle));
            SetText(_objectName, state.ObjectName);
            SetText(_objectLevel, state.BuildingLevel > 0
                ? ActivityUiText.Format(ActivityUiText.Level, state.BuildingLevel)
                : string.Empty);
            SetText(_objectDescription, state.ObjectDescription);
            SetText(_availableCount, ActivityUiText.Format(
                ActivityUiText.AvailableCount,
                state.Activities.Count(value => value.VisualState == ActivityCardVisualState.Idle)));
            ActivityCardProductionInfo.SetIcon(
                _objectImage,
                ActivityCardProductionInfo.IconResolver.ResolveCard(state.IconId));
        }
    }

    [Serializable]
    public sealed class SelectedActivityInfo
    {
        [SerializeField] private GameObject _panel;
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _category;
        [SerializeField] private TMP_Text _description;
        [SerializeField] private TMP_Text _duration;
        [SerializeField] private TMP_Text _emptyHint;
        [SerializeField] private GameObject _cyclesPanel;
        [SerializeField] private TMP_Text _cyclesLabel;
        [SerializeField] private Slider _cyclesSlider;
        [SerializeField] private TMP_Text _cyclesValue;
        [SerializeField] private Button _heroButton;
        [SerializeField] private TMP_Text _heroButtonText;
        [SerializeField] private Image _heroIcon;
        [SerializeField] private TMP_Text _heroInitial;
        [SerializeField] private GameObject _progressPanel;
        [SerializeField] private Slider _progress;
        [SerializeField] private TMP_Text _progressText;
        [SerializeField] private TMP_Text _notice;
        [SerializeField] private Button _primaryButton;
        [SerializeField] private TMP_Text _primaryButtonText;
        [SerializeField] private ActivityCardProductionInfo _productionInfo;

        public Slider CyclesSlider => _cyclesSlider;
        public Button HeroButton => _heroButton;
        public Button PrimaryButton => _primaryButton;

        public void Render(SelectedActivityState state)
        {
            state ??= SelectedActivityState.Empty;
            var hasActivity = !string.IsNullOrWhiteSpace(state.ActivityId);
            if (_panel != null)
                _panel.SetActive(hasActivity);
            if (_emptyHint != null)
            {
                _emptyHint.gameObject.SetActive(!hasActivity);
                SetText(_emptyHint, ActivityUiText.Get(ActivityUiText.SelectAction));
            }
            ActivityCardProductionInfo.SetIcon(_icon, ActivityCardProductionInfo.IconResolver.ResolveCard(state.IconId));
            SetText(_name, state.Name);
            SetText(_category, state.Category);
            SetText(_description, state.Description);
            SetText(_duration, state.Duration);
            SetText(_cyclesValue, state.PlannedCycles.ToString(CultureInfo.InvariantCulture));
            SetText(_cyclesLabel, ActivityUiText.Get(ActivityUiText.Cycles));
            SetText(_heroButtonText, string.IsNullOrWhiteSpace(state.HeroName)
                ? ActivityUiText.Get(ActivityUiText.AssignHero)
                : state.HeroName);
            ActivityCardProductionInfo.SetIcon(_heroIcon, ActivityCardProductionInfo.IconResolver.ResolveHero(state.HeroIconId));
            SetText(_heroInitial, Initial(state.HeroName));
            SetText(_progressText, state.ProgressText);
            SetText(_notice, state.Notice);
            SetText(_primaryButtonText, state.PrimaryActionText);

            if (_cyclesPanel != null)
                _cyclesPanel.SetActive(state.ShowCycles);
            if (_cyclesSlider != null)
            {
                _cyclesSlider.wholeNumbers = true;
                _cyclesSlider.minValue = 1f;
                _cyclesSlider.maxValue = Mathf.Max(1, state.MaxCycles);
                _cyclesSlider.SetValueWithoutNotify(Mathf.Clamp(state.PlannedCycles, 1, state.MaxCycles));
                _cyclesSlider.interactable = state.CanConfigure;
            }
            if (_heroButton != null)
                _heroButton.interactable = state.CanConfigure && state.HasHeroes;
            if (_progressPanel != null)
                _progressPanel.SetActive(state.VisualState == ActivityCardVisualState.InProgress);
            if (_progress != null)
                _progress.normalizedValue = Mathf.Clamp01(state.Progress);
            if (_primaryButton != null)
                _primaryButton.interactable = state.PrimaryActionEnabled;

            _productionInfo?.Render(state.ProductionInfo);
        }
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
            target.text = value ?? string.Empty;
    }

    private static string Initial(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Substring(0, 1).ToUpperInvariant();
}

public sealed class ObjectActivitiesWindowOpenArgs : IUIOpenArgs
{
    public ObjectActivitiesWindowOpenArgs(
        ObjectActivitiesWindowState state,
        Action closeRequested,
        Action closed,
        Action<string> activitySelected,
        Action<int> cyclesChanged,
        Action heroRequested,
        Action primaryActionRequested)
    {
        State = state ?? ObjectActivitiesWindowState.Empty;
        CloseRequested = closeRequested;
        Closed = closed;
        ActivitySelected = activitySelected;
        CyclesChanged = cyclesChanged;
        HeroRequested = heroRequested;
        PrimaryActionRequested = primaryActionRequested;
    }

    public ObjectActivitiesWindowState State { get; }
    public Action CloseRequested { get; }
    public Action Closed { get; }
    public Action<string> ActivitySelected { get; }
    public Action<int> CyclesChanged { get; }
    public Action HeroRequested { get; }
    public Action PrimaryActionRequested { get; }
}

public sealed class ObjectActivitiesAdaptiveLayout : MonoBehaviour
{
    [SerializeField] private RectTransform _body;
    [SerializeField] private RectTransform _listPanel;
    [SerializeField] private RectTransform _detailPanel;
    [SerializeField] private float _portraitBreakpoint = 1.15f;

    private bool? _portrait;

    private void OnEnable() => Apply();
    private void OnRectTransformDimensionsChange() => Apply();

    private void Apply()
    {
        if (_body == null || _listPanel == null || _detailPanel == null || _body.rect.height <= 0f)
            return;
        var portrait = _body.rect.width / _body.rect.height < _portraitBreakpoint;
        if (_portrait == portrait)
            return;
        _portrait = portrait;

        if (portrait)
        {
            SetAnchors(_listPanel, new Vector2(0f, 0.56f), Vector2.one, Vector2.zero, Vector2.zero);
            SetAnchors(_detailPanel, Vector2.zero, new Vector2(1f, 0.54f), Vector2.zero, Vector2.zero);
        }
        else
        {
            SetAnchors(_listPanel, Vector2.zero, new Vector2(0.41f, 1f), Vector2.zero, Vector2.zero);
            SetAnchors(_detailPanel, new Vector2(0.43f, 0f), Vector2.one, Vector2.zero, Vector2.zero);
        }
    }

    private static void SetAnchors(
        RectTransform target,
        Vector2 min,
        Vector2 max,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        target.anchorMin = min;
        target.anchorMax = max;
        target.offsetMin = offsetMin;
        target.offsetMax = offsetMax;
    }
}

public sealed class ObjectActivitiesWindowState : IUIState
{
    public static readonly ObjectActivitiesWindowState Empty = new ObjectActivitiesWindowState(
        string.Empty, 0, string.Empty, string.Empty, string.Empty,
        Array.Empty<ActivityCardState>(), SelectedActivityState.Empty);

    public ObjectActivitiesWindowState(
        string buildingId,
        int buildingLevel,
        string objectName,
        string objectDescription,
        string iconId,
        IReadOnlyList<ActivityCardState> activities,
        SelectedActivityState selectedActivity)
    {
        BuildingId = buildingId ?? string.Empty;
        BuildingLevel = buildingLevel;
        ObjectName = objectName ?? string.Empty;
        ObjectDescription = objectDescription ?? string.Empty;
        IconId = iconId ?? string.Empty;
        Activities = activities ?? Array.Empty<ActivityCardState>();
        SelectedActivity = selectedActivity ?? SelectedActivityState.Empty;
    }

    public string BuildingId { get; }
    public int BuildingLevel { get; }
    public string ObjectName { get; }
    public string ObjectDescription { get; }
    public string IconId { get; }
    public IReadOnlyList<ActivityCardState> Activities { get; }
    public SelectedActivityState SelectedActivity { get; }
}

public sealed class SelectedActivityState
{
    public static readonly SelectedActivityState Empty = new SelectedActivityState(
        string.Empty, ObjectActivityKind.Activity, string.Empty, string.Empty, string.Empty, string.Empty,
        ActivityCardVisualState.Unavailable, false, false, 1, 1, string.Empty, string.Empty, string.Empty,
        false, Array.Empty<HeroSelectionOptionState>(), 0f, string.Empty, ActivityCardProductionState.Empty,
        false, string.Empty, false, string.Empty);

    public SelectedActivityState(
        string activityId,
        ObjectActivityKind kind,
        string name,
        string description,
        string category,
        string iconId,
        ActivityCardVisualState visualState,
        bool showCycles,
        bool canConfigure,
        int plannedCycles,
        int maxCycles,
        string heroId,
        string heroName,
        string heroIconId,
        bool hasHeroes,
        IReadOnlyList<HeroSelectionOptionState> heroChoices,
        float progress,
        string progressText,
        ActivityCardProductionState productionInfo,
        bool primaryActionEnabled,
        string primaryActionText,
        bool isAvailable,
        string notice,
        string executionId = "",
        string pendingResultId = "")
    {
        ActivityId = activityId ?? string.Empty;
        Kind = kind;
        Name = name ?? string.Empty;
        Description = description ?? string.Empty;
        Category = category ?? string.Empty;
        IconId = iconId ?? string.Empty;
        VisualState = visualState;
        ShowCycles = showCycles;
        CanConfigure = canConfigure;
        PlannedCycles = Math.Max(1, plannedCycles);
        MaxCycles = Math.Max(1, maxCycles);
        HeroId = heroId ?? string.Empty;
        HeroName = heroName ?? string.Empty;
        HeroIconId = heroIconId ?? string.Empty;
        HasHeroes = hasHeroes;
        HeroChoices = heroChoices ?? Array.Empty<HeroSelectionOptionState>();
        Progress = Mathf.Clamp01(progress);
        ProgressText = progressText ?? string.Empty;
        ProductionInfo = productionInfo ?? ActivityCardProductionState.Empty;
        PrimaryActionEnabled = primaryActionEnabled;
        PrimaryActionText = primaryActionText ?? string.Empty;
        IsAvailable = isAvailable;
        Notice = notice ?? string.Empty;
        ExecutionId = executionId ?? string.Empty;
        PendingResultId = pendingResultId ?? string.Empty;
        Duration = string.Empty;
    }

    public string ActivityId { get; }
    public ObjectActivityKind Kind { get; }
    public string Name { get; }
    public string Description { get; }
    public string Category { get; }
    public string IconId { get; }
    public ActivityCardVisualState VisualState { get; }
    public bool ShowCycles { get; }
    public bool CanConfigure { get; }
    public int PlannedCycles { get; }
    public int MaxCycles { get; }
    public string HeroId { get; }
    public string HeroName { get; }
    public string HeroIconId { get; }
    public bool HasHeroes { get; }
    public IReadOnlyList<HeroSelectionOptionState> HeroChoices { get; }
    public float Progress { get; }
    public string ProgressText { get; }
    public string Duration { get; internal set; }
    public ActivityCardProductionState ProductionInfo { get; }
    public bool PrimaryActionEnabled { get; }
    public string PrimaryActionText { get; }
    public bool IsAvailable { get; }
    public string Notice { get; }
    public string ExecutionId { get; }
    public string PendingResultId { get; }
}

public sealed class ObjectActivitiesSelection
{
    public string ActivityId { get; set; }
    public string HeroId { get; set; }
    public int PlannedCycles { get; set; } = 1;
    public string Notice { get; set; }
    public string NoticeId { get; set; }
}

public interface IObjectActivitiesRuntimeSource
{
    bool IsReady { get; }
    bool CanOpen(string buildingId, int buildingLevel);
    ObjectActivitiesWindowState BuildState(string buildingId, int buildingLevel, ObjectActivitiesSelection selection);
}

public sealed class RuntimeObjectActivitiesSource : IObjectActivitiesRuntimeSource
{
    public const int DefaultCycleLimit = 10;
    private readonly HashSet<string> _reportedMissingActions = new HashSet<string>(StringComparer.Ordinal);

    public bool IsReady => RuntimeConfigs.IsLoaded && RuntimePlayer.IsLoaded && RuntimePlayer.State != null;

    public bool CanOpen(string buildingId, int buildingLevel)
    {
        var state = RuntimePlayer.State;
        return IsReady && state.CanClickBuilding(buildingId) &&
               state.TryGetBuildingLevelState(buildingId, out var currentLevel) && currentLevel == buildingLevel;
    }

    public ObjectActivitiesWindowState BuildState(
        string buildingId,
        int buildingLevel,
        ObjectActivitiesSelection selection)
    {
        if (!CanOpen(buildingId, buildingLevel))
            return ObjectActivitiesWindowState.Empty;

        selection ??= new ObjectActivitiesSelection();
        RuntimeConfigs.Buildings.TryGet(buildingId, out var building);
        var executions = OnlineActivityRuntime.GetPresentationSnapshot()?.executions ?? Array.Empty<ActivityExecutionSnapshot>();
        var craftExecutions = OnlineActivityRuntime.GetPresentationCraftSnapshots();
        var cards = new List<OrderedCardState>();
        var addedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mapping in RuntimeConfigs.Buildings.BuildingActivities
                     .Where(value => value != null && string.Equals(value.buildingId, buildingId, StringComparison.Ordinal))
                     .OrderBy(value => value.sortOrder))
        {
            var execution = FindExecution(executions, mapping.activityId);
            var resultPending = execution?.status == ActivityRuntimeStatus.ResultPending;
            if ((!resultPending && (mapping.buildingLevel != buildingLevel ||
                                    !ActivityAvailabilityResolver.IsExposedByBuilding(mapping, RuntimePlayer.State))) ||
                !addedIds.Add(mapping.activityId) || !TryResolveDescriptor(mapping.activityId, out var descriptor))
                continue;

            var available = EvaluateClickableRequirement(mapping.clickableRequirement, out var blockReason);
            var kind = ResolveKind(mapping.activityId);
            var visualState = ResolveVisualState(execution, available);
            cards.Add(BuildOrderedCard(
                mapping.sortOrder, mapping.activityId, kind, descriptor, available, blockReason,
                execution?.heroId, execution?.progress ?? 0f,
                execution == null ? string.Empty : FormatRemainingTime(execution.remainingSeconds),
                execution == null ? string.Empty : FormatCycle(execution.completedCycles, execution.plannedCycles),
                GetCurrentDropItemsCount(execution?.pendingResultId), execution?.executionId, execution?.pendingResultId,
                visualState, selection.ActivityId));
        }

        foreach (var craft in RuntimeConfigs.Crafts.GetAvailableCrafts(buildingId, buildingLevel))
        {
            var definition = craft?.Definition;
            if (definition == null || !RuntimeConfigs.Items.TryGet(definition.TargetItemId, out var product))
                continue;
            var execution = FindCraftExecution(craftExecutions, craft.CraftId, buildingId);
            var descriptor = new ActionDescriptor(product.NameId, product.DescriptionId, product.IconId);
            cards.Add(BuildOrderedCard(
                craft.SortOrder, craft.CraftId, ObjectActivityKind.Craft, descriptor, true, string.Empty,
                execution?.heroId, GetCraftProgress(execution),
                execution == null ? string.Empty : FormatRemainingTime(execution.durationSeconds - execution.progressSeconds),
                FormatCraftCycle(execution), GetCurrentDropItemsCount(execution?.pendingResultId),
                execution?.executionId, execution?.pendingResultId, ResolveCraftVisualState(execution), selection.ActivityId));
        }

        var ordered = cards.OrderBy(value => StatePriority(value.State.VisualState))
            .ThenBy(value => value.SortOrder).ThenBy(value => value.Id, StringComparer.Ordinal).ToList();
        var selectedId = ordered.Any(value => string.Equals(value.Id, selection.ActivityId, StringComparison.Ordinal))
            ? selection.ActivityId
            : ordered.FirstOrDefault().Id;

        var renderedCards = ordered.Select(value => CloneSelection(value.State, value.Id == selectedId)).ToArray();
        var selectedCard = renderedCards.FirstOrDefault(value => value.ActivityId == selectedId);
        var selectedState = BuildSelectedState(buildingId, buildingLevel, selectedCard, selection);

        return new ObjectActivitiesWindowState(
            buildingId, buildingLevel, LocaliseOrFallback(building?.nameId, buildingId),
            LocaliseOrFallback(building?.descriptionId, string.Empty), building?.smallIconId,
            renderedCards, selectedState);
    }

    private OrderedCardState BuildOrderedCard(
        int sortOrder,
        string id,
        ObjectActivityKind kind,
        ActionDescriptor descriptor,
        bool available,
        string blockReason,
        string heroId,
        float progress,
        string remaining,
        string cycle,
        long drops,
        string executionId,
        string pendingResultId,
        ActivityCardVisualState visualState,
        string selectedId)
    {
        ResolveSkillInfo(id, kind, out var requiredSkillValue, out var skillIcon);
        ResolveMetrics(id, kind, out var duration, out var energy);
        var dangerChance = ResolveDangerChance(id);
        ResolveHero(heroId, out var heroName, out var heroIcon);
        return new OrderedCardState(sortOrder, id, new ActivityCardState(
            id, kind, LocaliseOrFallback(descriptor.NameId, id), descriptor.IconId, true, available,
            string.Equals(id, selectedId, StringComparison.Ordinal), blockReason, duration, energy,
            requiredSkillValue, skillIcon, dangerChance, heroId, heroName, heroIcon, progress, remaining, cycle, drops,
            executionId, pendingResultId, visualState));
    }

    private SelectedActivityState BuildSelectedState(
        string buildingId,
        int buildingLevel,
        ActivityCardState card,
        ObjectActivitiesSelection selection)
    {
        if (card == null)
            return SelectedActivityState.Empty;

        ResolveDescriptor(card.ActivityId, card.Kind, out var descriptor);
        var showCycles = IsCyclic(card.ActivityId, card.Kind);
        var heroChoices = BuildHeroChoices(card.ActivityId, card.Kind, selection.HeroId);
        var heroId = card.VisualState == ActivityCardVisualState.InProgress || card.VisualState == ActivityCardVisualState.Finished
            ? card.HeroId
            : ResolveSelectedHero(selection.HeroId, heroChoices);
        if (!string.IsNullOrWhiteSpace(heroId) && heroChoices.All(value => !value.IsSelected))
            heroChoices = BuildHeroChoices(card.ActivityId, card.Kind, heroId);
        ResolveHero(heroId, out var heroName, out var heroIcon);
        var canConfigure = card.VisualState == ActivityCardVisualState.Idle || card.VisualState == ActivityCardVisualState.Unavailable;
        var maxCycles = showCycles && canConfigure
            ? ResolveAvailableCycleLimit(card.ActivityId, card.Kind, heroId, DefaultCycleLimit)
            : DefaultCycleLimit;
        var plannedCycles = showCycles ? Mathf.Clamp(selection.PlannedCycles, 1, maxCycles) : 1;
        var production = BuildProductionInfo(card.ActivityId, card.Kind, plannedCycles, heroId);
        var notice = string.IsNullOrWhiteSpace(selection.NoticeId)
            ? selection.Notice
            : ActivityUiText.Get(selection.NoticeId);
        if (string.IsNullOrWhiteSpace(notice) && !card.IsAvailable)
            notice = card.BlockReason;
        if (string.IsNullOrWhiteSpace(notice) && canConfigure && string.IsNullOrWhiteSpace(heroId))
            notice = ActivityUiText.Get(ActivityUiText.NoFreeHero);

        var heroLimitReached = card.VisualState == ActivityCardVisualState.Idle && IsActiveHeroLimitReached();
        var canCancel = card.VisualState == ActivityCardVisualState.InProgress &&
                        card.Kind == ObjectActivityKind.Work &&
                        !string.IsNullOrWhiteSpace(card.ExecutionId);
        var primaryText = card.VisualState == ActivityCardVisualState.Finished
            ? ActivityUiText.Get(ActivityUiText.Claim)
            : canCancel
                ? ActivityUiText.Get(ActivityUiText.Cancel)
                : card.VisualState == ActivityCardVisualState.InProgress
                    ? ActivityUiText.Get(ActivityUiText.InProgress)
                : heroLimitReached
                    ? ActivityUiText.Get(ActivityUiText.HeroLimit)
                    : ActivityUiText.Get(ActivityUiText.Start);
        var primaryEnabled = card.VisualState == ActivityCardVisualState.Finished || canCancel ||
                             (card.VisualState == ActivityCardVisualState.Idle && !heroLimitReached &&
                              !string.IsNullOrWhiteSpace(heroId));
        var state = new SelectedActivityState(
            card.ActivityId, card.Kind, card.Name, LocaliseOrFallback(descriptor.DescriptionId, string.Empty),
            CategoryName(card.Kind), card.IconId, card.VisualState, showCycles, canConfigure,
            plannedCycles, maxCycles, heroId, heroName, heroIcon, heroChoices.Any(value => value.CanSelect), heroChoices,
            card.Progress, BuildProgressText(card), production, primaryEnabled, primaryText,
            card.IsAvailable, notice, card.ExecutionId, card.PendingResultId);
        state.Duration = ResolveTotalDuration(card.ActivityId, card.Kind, plannedCycles);
        return state;
    }

    private bool TryResolveDescriptor(string activityId, out ActionDescriptor descriptor)
    {
        if (ResolveDescriptor(activityId, ResolveKind(activityId), out descriptor))
            return true;
        if (_reportedMissingActions.Add(activityId ?? string.Empty))
            Debug.LogError($"[ObjectActivities] Unknown activity/build action id '{activityId}'.");
        return false;
    }

    private static bool ResolveDescriptor(string id, ObjectActivityKind kind, out ActionDescriptor descriptor)
    {
        descriptor = default;
        if (kind == ObjectActivityKind.Craft && RuntimeConfigs.Crafts.TryGetDefinition(id, out var craft) &&
            RuntimeConfigs.Items.TryGet(craft.TargetItemId, out var product))
        {
            descriptor = new ActionDescriptor(product.NameId, product.DescriptionId, product.IconId);
            return true;
        }
        if (RuntimeConfigs.Activities.TryGet(id, out var activity))
        {
            descriptor = new ActionDescriptor(activity.nameId, activity.descriptionId, activity.iconId);
            return true;
        }
        if (RuntimeConfigs.Buildings.TryGetBuildAction(id, out var buildAction))
        {
            RuntimeConfigs.Buildings.TryGet(buildAction.targetBuildingId, out var target);
            descriptor = new ActionDescriptor($"{buildAction.id}_name_id", target?.descriptionId, target?.smallIconId);
            return true;
        }
        return false;
    }

    private static ObjectActivityKind ResolveKind(string id)
    {
        if (RuntimeConfigs.Buildings.TryGetBuildAction(id, out _))
            return ObjectActivityKind.Construction;
        if (RuntimeConfigs.Crafts.TryGetDefinition(id, out _))
            return ObjectActivityKind.Craft;
        if (RuntimeConfigs.Activities.TryGetCombatDetails(id, out var combat) && combat != null)
            return ObjectActivityKind.Combat;
        if (RuntimeConfigs.Activities.TryGet(id, out var activity) && activity.isRepeatable)
            return ObjectActivityKind.Work;
        return ObjectActivityKind.Activity;
    }

    private static bool IsCyclic(string id, ObjectActivityKind kind)
    {
        if (kind == ObjectActivityKind.Craft)
            return true;
        return RuntimeConfigs.Activities.TryGet(id, out var activity) && activity.isRepeatable;
    }

    private static void ResolveMetrics(string id, ObjectActivityKind kind, out string duration, out string energy)
    {
        duration = string.Empty;
        energy = string.Empty;
        if (kind == ObjectActivityKind.Craft && RuntimeConfigs.Crafts.TryGetDefinition(id, out var craft))
        {
            duration = Math.Max(0, craft.CraftDurationSec).ToString(CultureInfo.InvariantCulture);
            energy = Math.Max(0, craft.FatigueCost).ToString(CultureInfo.InvariantCulture);
            return;
        }
        if (kind == ObjectActivityKind.Construction && RuntimeConfigs.Buildings.TryGetBuildAction(id, out var build))
        {
            energy = Math.Max(0, build.fatigueCost).ToString(CultureInfo.InvariantCulture);
            return;
        }
        if (RuntimeConfigs.Activities.TryGet(id, out var activity))
        {
            var seconds = activity.cycleSec > 0 ? activity.cycleSec : activity.durationSec;
            duration = Math.Max(0, seconds).ToString(CultureInfo.InvariantCulture);
            energy = Math.Max(0, activity.fatigueCost).ToString(CultureInfo.InvariantCulture);
        }
    }

    private static string ResolveTotalDuration(string id, ObjectActivityKind kind, int cycles)
    {
        ResolveMetrics(id, kind, out var duration, out _);
        if (!long.TryParse(duration, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return string.Empty;
        return FormatRemainingTime(seconds * Math.Max(1, cycles));
    }

    private static void ResolveSkillInfo(string id, ObjectActivityKind kind, out string requiredValue, out string iconId)
    {
        requiredValue = string.Empty;
        iconId = string.Empty;
        var skillId = string.Empty;
        if (kind == ObjectActivityKind.Craft && RuntimeConfigs.Crafts.TryGetDefinition(id, out var craft))
            skillId = craft.CraftSkillId;
        else if (kind == ObjectActivityKind.Construction && RuntimeConfigs.Buildings.TryGetBuildAction(id, out var build))
        {
            skillId = build.skillId;
            var requirement = (build.requirementsSkills ?? Array.Empty<RequiredSkillDto>()).FirstOrDefault(
                value => value != null && string.Equals(value.skillId, skillId, StringComparison.Ordinal));
            if (requirement != null && requirement.level > 0)
                requiredValue = requirement.level.ToString(CultureInfo.InvariantCulture);
        }
        else if (RuntimeConfigs.Activities.TryGet(id, out var activity))
        {
            skillId = activity.mainSkillId;
            var requirement = RuntimeConfigs.Activities.GetRequirements(id).FirstOrDefault(value =>
                value != null && !value.hidden &&
                string.Equals(value.targetId, skillId, StringComparison.Ordinal) &&
                ActivityTypeParser.TryParseRequirementType(value.reqType, out var type) &&
                type == RequirementTypeEnum.SkillLevel);
            if (requirement != null && requirement.value > 0)
                requiredValue = requirement.value.ToString(CultureInfo.InvariantCulture);
        }
        var skill = FindSkill(skillId);
        if (skill == null)
            return;
        iconId = skill.skillIconId;
    }

    private static string ResolveDangerChance(string activityId)
    {
        var encounter = RuntimeConfigs.Activities.GetDangerEncounters(activityId).FirstOrDefault();
        if (encounter == null || encounter.riskPercent <= 0f || float.IsNaN(encounter.riskPercent) ||
            float.IsInfinity(encounter.riskPercent))
        {
            return string.Empty;
        }

        return $"{encounter.riskPercent.ToString("0.#", CultureInfo.InvariantCulture)}%";
    }

    private static ActivityCardProductionState BuildProductionInfo(
        string id,
        ObjectActivityKind kind,
        int cycles,
        string heroId)
    {
        if (kind == ObjectActivityKind.Craft && RuntimeConfigs.Crafts.TryGetDefinition(id, out var craft))
            return BuildCraftProductionInfo(craft, cycles, heroId);
        if (kind == ObjectActivityKind.Construction && RuntimeConfigs.Buildings.TryGetBuildAction(id, out var build))
            return BuildConstructionProductionInfo(build, heroId);

        var skills = new List<ActivityCardProductionEntryState>();
        var inputs = new List<ActivityCardProductionEntryState>();
        var products = new List<ActivityCardProductionEntryState>();
        if (RuntimeConfigs.Activities.TryGet(id, out var activity) && activity.fatigueCost > 0)
            inputs.Add(EnergyEntry(Scale(activity.fatigueCost, cycles), heroId));

        foreach (var requirement in RuntimeConfigs.Activities.GetRequirements(id))
        {
            if (requirement == null || requirement.hidden ||
                !ActivityTypeParser.TryParseRequirementType(requirement.reqType, out var type))
                continue;
            var amount = Math.Max(1, requirement.value);
            if (type == RequirementTypeEnum.SkillLevel)
                AddSkillEntry(skills, requirement.targetId, amount.ToString(CultureInfo.InvariantCulture));
            else if (type == RequirementTypeEnum.Resource || type == RequirementTypeEnum.Item ||
                     type == RequirementTypeEnum.ItemCount)
                AddRequiredItemEntry(inputs, requirement.targetId, Scale(amount, cycles));
            else if (type == RequirementTypeEnum.Currency)
                AddRequiredCurrencyEntry(inputs, requirement.targetId, Scale(amount, cycles));
        }

        foreach (var reward in RuntimeConfigs.Activities.GetRewards(id))
        {
            if (reward == null || reward.chance <= 0f ||
                !ActivityTypeParser.TryParseRewardType(reward.rewardType, out var type))
                continue;
            var amount = FormatRange(Scale(reward.min, cycles), Scale(reward.max, cycles));
            if (type == RewardTypeEnum.SkillExp)
                AddSkillEntry(products, reward.targetId, ActivityUiText.Format(ActivityUiText.SkillXp, amount));
            else if (IsItemReward(type))
                AddItemEntry(products, reward.targetId, amount);
            else if (type == RewardTypeEnum.Gold)
                AddCurrencyEntry(products, ActivityResolverUtilities.GoldCurrencyId, amount);
            else if (type == RewardTypeEnum.Currency)
                AddCurrencyEntry(products, reward.targetId, amount);
        }
        return new ActivityCardProductionState(skills, inputs, products, false);
    }

    private static ActivityCardProductionState BuildConstructionProductionInfo(BuildActionConfigDto action, string heroId)
    {
        var skills = new List<ActivityCardProductionEntryState>();
        var inputs = new List<ActivityCardProductionEntryState>();
        if (action.fatigueCost > 0)
            inputs.Add(EnergyEntry(action.fatigueCost, heroId));
        foreach (var requirement in action.requirementsSkills ?? Array.Empty<RequiredSkillDto>())
            if (requirement != null)
                AddSkillEntry(skills, requirement.skillId, Math.Max(1, requirement.level).ToString(CultureInfo.InvariantCulture));
        foreach (var material in action.materials ?? Array.Empty<MaterialCostDto>())
            if (material != null)
                AddRequiredItemEntry(inputs, material.id, Math.Max(1, material.count));
        return new ActivityCardProductionState(skills, inputs, Array.Empty<ActivityCardProductionEntryState>(), true);
    }

    private static ActivityCardProductionState BuildCraftProductionInfo(
        CraftDefinitionDescriptor craft,
        int cycles,
        string heroId)
    {
        var skills = new List<ActivityCardProductionEntryState>();
        var inputs = new List<ActivityCardProductionEntryState>();
        var products = new List<ActivityCardProductionEntryState>();
        AddSkillEntry(skills, craft.CraftSkillId, string.Empty);
        if (craft.FatigueCost > 0)
            inputs.Add(EnergyEntry(Scale(craft.FatigueCost, cycles), heroId));
        foreach (var material in craft.Materials)
            AddRequiredItemEntry(inputs, material.ItemId, Scale(material.Count, cycles));
        if (!string.IsNullOrWhiteSpace(craft.RequiredRecipeItemId) && craft.RequiredRecipeItemCount > 0)
            AddRequiredItemEntry(inputs, craft.RequiredRecipeItemId, Scale(craft.RequiredRecipeItemCount, cycles));
        AddItemEntry(products, craft.TargetItemId, Scale(Math.Max(1, craft.OutputCount), cycles));
        if (craft.SkillExp > 0)
            AddSkillEntry(products, craft.CraftSkillId,
                ActivityUiText.Format(ActivityUiText.SkillXp, Scale(craft.SkillExp, cycles)));
        return new ActivityCardProductionState(skills, inputs, products, false);
    }

    private static ActivityCardProductionEntryState EnergyEntry(int amount, string heroId) =>
        new ActivityCardProductionEntryState(
            "fatigue_icon",
            FormatRequirementAmount(
                amount,
                !string.IsNullOrWhiteSpace(heroId) && RuntimePlayer.State != null
                    ? RuntimePlayer.State.GetHeroFatigue(heroId)
                    : (int?)null),
            ActivityProductionIconKind.Energy,
            RuntimeConfigs.Localisation.TryGet(ActivityUiText.Energy, out var energyName)
                ? energyName
                : string.Empty);

    private static void AddSkillEntry(ICollection<ActivityCardProductionEntryState> target, string skillId, string value)
    {
        var skill = FindSkill(skillId);
        if (skill != null)
            target.Add(new ActivityCardProductionEntryState(
                skill.skillIconId,
                value,
                ActivityProductionIconKind.Skill,
                LocaliseOrFallback(skill.skillNameId, skill.skillId)));
    }

    private static void AddRequiredItemEntry(
        ICollection<ActivityCardProductionEntryState> target,
        string id,
        int requiredAmount)
    {
        var availableAmount = RuntimePlayer.State != null ? RuntimePlayer.State.GetItem(id) : 0;
        AddItemEntry(target, id, FormatRequirementAmount(requiredAmount, availableAmount));
    }

    private static void AddRequiredCurrencyEntry(
        ICollection<ActivityCardProductionEntryState> target,
        string id,
        int requiredAmount)
    {
        var availableAmount = RuntimePlayer.State != null ? RuntimePlayer.State.GetCurrency(id) : 0L;
        AddCurrencyEntry(target, id, FormatRequirementAmount(requiredAmount, availableAmount));
    }

    private static string FormatRequirementAmount(long requiredAmount, long? availableAmount)
    {
        var required = requiredAmount.ToString(CultureInfo.InvariantCulture);
        return availableAmount.HasValue
            ? $"{required} ({availableAmount.Value.ToString(CultureInfo.InvariantCulture)})"
            : required;
    }

    private static SkillConfigDto FindSkill(string skillId) => RuntimeConfigs.Activities.Skills.FirstOrDefault(
        value => value != null && string.Equals(value.skillId, skillId, StringComparison.Ordinal));

    private static void AddItemEntry(ICollection<ActivityCardProductionEntryState> target, string id, int amount) =>
        AddItemEntry(target, id, amount.ToString(CultureInfo.InvariantCulture));

    private static void AddItemEntry(ICollection<ActivityCardProductionEntryState> target, string id, string amount)
    {
        if (RuntimeConfigs.Items.TryGet(id, out var item) && item != null)
            target.Add(new ActivityCardProductionEntryState(
                item.IconId,
                amount,
                ActivityProductionIconKind.Item,
                LocaliseOrFallback(item.NameId, id)));
    }

    private static void AddCurrencyEntry(ICollection<ActivityCardProductionEntryState> target, string id, int amount) =>
        AddCurrencyEntry(target, id, amount.ToString(CultureInfo.InvariantCulture));

    private static void AddCurrencyEntry(ICollection<ActivityCardProductionEntryState> target, string id, string amount)
    {
        if (RuntimeConfigs.Items.TryGetCurrency(id, out var currency) && currency != null)
            target.Add(new ActivityCardProductionEntryState(
                currency.iconId,
                amount,
                ActivityProductionIconKind.Item,
                LocaliseOrFallback(currency.nameId, id)));
    }

    private static ActivityCardState CloneSelection(ActivityCardState source, bool selected) => new ActivityCardState(
        source.ActivityId, source.Kind, source.Name, source.IconId, source.CanSelect, source.IsAvailable, selected,
        source.BlockReason, source.DurationValue, source.EnergyValue, source.RequiredSkillValue, source.SkillIconId,
        source.DangerChanceValue, source.HeroId, source.HeroName, source.HeroIconId, source.Progress, source.RemainingTime, source.Cycle,
        source.CurrentDropItemsCount, source.ExecutionId, source.PendingResultId, source.VisualState);

    private static ActivityExecutionSnapshot FindExecution(IEnumerable<ActivityExecutionSnapshot> values, string id)
    {
        ActivityExecutionSnapshot pending = null;
        foreach (var value in values ?? Array.Empty<ActivityExecutionSnapshot>())
        {
            if (value == null || !string.Equals(value.activityId, id, StringComparison.Ordinal))
                continue;
            if (value.status == ActivityRuntimeStatus.Running || value.status == ActivityRuntimeStatus.Paused)
                return value;
            if (value.status == ActivityRuntimeStatus.ResultPending || value.status == ActivityRuntimeStatus.Completed)
                pending = value;
        }
        return pending;
    }

    private static OnlineCraftSnapshot FindCraftExecution(IEnumerable<OnlineCraftSnapshot> values, string craftId, string buildingId)
    {
        OnlineCraftSnapshot pending = null;
        foreach (var value in values ?? Array.Empty<OnlineCraftSnapshot>())
        {
            if (value == null || !string.Equals(value.craftId, craftId, StringComparison.Ordinal) ||
                !string.Equals(value.stationBuildingId, buildingId, StringComparison.Ordinal))
                continue;
            if (value.status == CraftExecutionStatus.Running)
                return value;
            if (value.status == CraftExecutionStatus.ResultPending)
                pending = value;
        }
        return pending;
    }

    private static bool EvaluateClickableRequirement(string requirement, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(requirement))
            return true;
        var parts = requirement.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var requiredLevel) || requiredLevel < 0)
        {
            reason = ActivityUiText.Get(ActivityUiText.CardUnavailable);
            return false;
        }
        var level = RuntimePlayer.State.GetBuildingLevel(parts[0]);
        if (level >= requiredLevel)
            return true;
        RuntimeConfigs.Buildings.TryGet(parts[0], out var building);
        reason = ActivityUiText.Format(
            ActivityUiText.RequirementLevel,
            LocaliseOrFallback(building?.nameId, parts[0]),
            level,
            requiredLevel);
        return false;
    }

    private static ActivityCardVisualState ResolveVisualState(ActivityExecutionSnapshot execution, bool available)
    {
        if (execution?.status == ActivityRuntimeStatus.Completed || execution?.status == ActivityRuntimeStatus.ResultPending)
            return ActivityCardVisualState.Finished;
        if (execution?.status == ActivityRuntimeStatus.Running || execution?.status == ActivityRuntimeStatus.Paused)
            return ActivityCardVisualState.InProgress;
        return available ? ActivityCardVisualState.Idle : ActivityCardVisualState.Unavailable;
    }

    private static ActivityCardVisualState ResolveCraftVisualState(OnlineCraftSnapshot execution)
    {
        if (execution?.status == CraftExecutionStatus.ResultPending)
            return ActivityCardVisualState.Finished;
        return execution?.status == CraftExecutionStatus.Running
            ? ActivityCardVisualState.InProgress
            : ActivityCardVisualState.Idle;
    }

    private static IReadOnlyList<HeroSelectionOptionState> BuildHeroChoices(
        string activityId,
        ObjectActivityKind kind,
        string selectedHeroId)
    {
        ResolveActivitySkill(activityId, kind, out var skillId, out var skillIconId);
        var requiredEnergy = ResolveEnergyCost(activityId, kind);
        return RuntimeConfigs.Heroes.Heroes
            .Where(hero => hero != null && hero.enabled && RuntimePlayer.State.HasHero(hero.heroId))
            .Select(hero =>
            {
                var busy = RuntimePlayer.State.IsHeroBusy(hero.heroId);
                var energy = RuntimePlayer.State.GetHeroFatigue(hero.heroId);
                var availability = busy
                    ? HeroSelectionAvailability.Busy
                    : energy < requiredEnergy
                        ? HeroSelectionAvailability.NoEnergy
                        : HeroSelectionAvailability.Available;
                return new
                {
                    Hero = hero,
                    Choice = new HeroSelectionOptionState(
                        hero.heroId,
                        LocaliseOrFallback(hero.nameId, hero.heroId),
                        ActivityCardProductionInfo.IconResolver.ResolveHero(hero.iconSpriteId),
                        availability,
                        ActivityCardProductionInfo.IconResolver.ResolveSkill(skillIconId),
                        string.IsNullOrWhiteSpace(skillId) ? 0 : RuntimePlayer.State.GetHeroSkillLevel(hero.heroId, skillId),
                        energy,
                        RuntimePlayer.State.GetHeroMaxFatigue(hero.heroId),
                        string.Equals(hero.heroId, selectedHeroId, StringComparison.Ordinal),
                        hero.sortOrder)
                };
            })
            .OrderBy(value => value.Choice.CanSelect ? 0 : 1)
            .ThenByDescending(value => value.Choice.SkillLevel)
            .ThenBy(value => value.Hero.sortOrder)
            .ThenBy(value => value.Hero.heroId, StringComparer.Ordinal)
            .Select(value => value.Choice)
            .ToArray();
    }

    private static string ResolveSelectedHero(
        string requestedId,
        IReadOnlyList<HeroSelectionOptionState> choices)
    {
        choices ??= Array.Empty<HeroSelectionOptionState>();
        if (choices.Any(value => value.CanSelect &&
                                 string.Equals(value.HeroId, requestedId, StringComparison.Ordinal)))
            return requestedId;
        return choices.FirstOrDefault(value => value.CanSelect)?.HeroId ?? string.Empty;
    }

    private static int ResolveEnergyCost(string id, ObjectActivityKind kind)
    {
        if (kind == ObjectActivityKind.Craft && RuntimeConfigs.Crafts.TryGetDefinition(id, out var craft))
            return Math.Max(0, craft.FatigueCost);
        if (kind == ObjectActivityKind.Construction && RuntimeConfigs.Buildings.TryGetBuildAction(id, out var build))
            return Math.Max(0, build.fatigueCost);
        return RuntimeConfigs.Activities.TryGet(id, out var activity) ? Math.Max(0, activity.fatigueCost) : 0;
    }

    private static int ResolveAvailableCycleLimit(
        string id,
        ObjectActivityKind kind,
        string heroId,
        int configuredLimit)
    {
        var limit = Math.Max(1, configuredLimit);
        var playerState = RuntimePlayer.State;
        if (playerState == null)
            return limit;

        var energyPerCycle = ResolveEnergyCost(id, kind);
        if (energyPerCycle > 0 && !string.IsNullOrWhiteSpace(heroId))
            limit = LimitCycles(limit, playerState.GetHeroFatigue(heroId), energyPerCycle);

        return kind == ObjectActivityKind.Craft && RuntimeConfigs.Crafts.TryGetDefinition(id, out var craft)
            ? ResolveCraftIngredientCycleLimit(craft, limit)
            : ResolveActivityIngredientCycleLimit(id, limit);
    }

    private static int ResolveCraftIngredientCycleLimit(CraftDefinitionDescriptor craft, int currentLimit)
    {
        var itemCosts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var material in craft.Materials ?? Array.Empty<CraftMaterialDescriptor>())
            if (material != null && material.Count > 0)
                AddCycleCost(itemCosts, material.ItemId, material.Count);

        if (craft.ConsumeRecipeItem && !string.IsNullOrWhiteSpace(craft.RequiredRecipeItemId) &&
            craft.RequiredRecipeItemCount > 0)
        {
            AddCycleCost(itemCosts, craft.RequiredRecipeItemId, craft.RequiredRecipeItemCount);
        }

        var limit = currentLimit;
        foreach (var cost in itemCosts)
        {
            var available = (long)RuntimePlayer.State.GetAvailableForActionCount(cost.Key, null);
            if (!craft.ConsumeRecipeItem &&
                string.Equals(cost.Key, craft.RequiredRecipeItemId, StringComparison.Ordinal))
            {
                available = Math.Max(0L, available - Math.Max(0, craft.RequiredRecipeItemCount));
            }
            limit = LimitCycles(limit, available, cost.Value);
        }

        if (!craft.ConsumeRecipeItem && !string.IsNullOrWhiteSpace(craft.RequiredRecipeItemId) &&
            craft.RequiredRecipeItemCount > 0 &&
            RuntimePlayer.State.GetAvailableForActionCount(craft.RequiredRecipeItemId, null) < craft.RequiredRecipeItemCount)
        {
            limit = 0;
        }

        return Math.Max(1, limit);
    }

    private static int ResolveActivityIngredientCycleLimit(string activityId, int currentLimit)
    {
        var itemCosts = new Dictionary<string, long>(StringComparer.Ordinal);
        var currencyCosts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var requirement in RuntimeConfigs.Activities.GetRequirements(activityId))
        {
            if (requirement == null || !requirement.consume || requirement.value <= 0 ||
                !ActivityTypeParser.TryParseRequirementType(requirement.reqType, out var type))
            {
                continue;
            }

            if (type == RequirementTypeEnum.Resource || type == RequirementTypeEnum.Item ||
                type == RequirementTypeEnum.ItemCount)
            {
                AddCycleCost(itemCosts, requirement.targetId, requirement.value);
            }
            else if (type == RequirementTypeEnum.Currency)
            {
                AddCycleCost(currencyCosts, requirement.targetId, requirement.value);
            }
        }

        var limit = currentLimit;
        foreach (var cost in itemCosts)
            limit = LimitCycles(
                limit,
                RuntimePlayer.State.GetAvailableForActionCount(cost.Key, null),
                cost.Value);
        foreach (var cost in currencyCosts)
            limit = LimitCycles(limit, RuntimePlayer.State.GetCurrency(cost.Key), cost.Value);
        return Math.Max(1, limit);
    }

    private static void AddCycleCost(IDictionary<string, long> costs, string id, long amount)
    {
        if (string.IsNullOrWhiteSpace(id) || amount <= 0)
            return;
        costs.TryGetValue(id, out var current);
        costs[id] = current > long.MaxValue - amount ? long.MaxValue : current + amount;
    }

    private static int LimitCycles(int currentLimit, long available, long costPerCycle)
    {
        if (currentLimit <= 0 || costPerCycle <= 0)
            return currentLimit;
        var affordable = Math.Max(0L, available) / costPerCycle;
        return affordable >= currentLimit ? currentLimit : (int)affordable;
    }

    private static void ResolveActivitySkill(
        string id,
        ObjectActivityKind kind,
        out string skillId,
        out string skillIconId)
    {
        skillId = string.Empty;
        skillIconId = string.Empty;
        if (kind == ObjectActivityKind.Craft && RuntimeConfigs.Crafts.TryGetDefinition(id, out var craft))
            skillId = craft.CraftSkillId;
        else if (kind == ObjectActivityKind.Construction && RuntimeConfigs.Buildings.TryGetBuildAction(id, out var build))
            skillId = build.skillId;
        else if (RuntimeConfigs.Activities.TryGet(id, out var activity))
            skillId = activity.mainSkillId;

        skillIconId = FindSkill(skillId)?.skillIconId ?? string.Empty;
    }

    private static void ResolveHero(string heroId, out string name, out string iconId)
    {
        name = string.Empty;
        iconId = string.Empty;
        if (!RuntimeConfigs.Heroes.TryGet(heroId, out var hero) || hero == null)
            return;
        name = LocaliseOrFallback(hero.nameId, heroId);
        iconId = hero.iconSpriteId;
    }

    private static long GetCurrentDropItemsCount(string resultId)
    {
        if (string.IsNullOrWhiteSpace(resultId))
            return 0L;
        long total = 0L;
        foreach (var entry in OnlineActivityRuntime.GetPendingReward(resultId)?.entries ??
                 Array.Empty<ActivityPendingRewardEntrySnapshot>())
        {
            if (entry == null || entry.quantity <= 0 ||
                !ActivityTypeParser.TryParseRewardType(entry.rewardType, out var type) || !IsItemReward(type))
                continue;
            total = long.MaxValue - total < entry.quantity ? long.MaxValue : total + entry.quantity;
        }
        return total;
    }

    private static int StatePriority(ActivityCardVisualState state) =>
        state == ActivityCardVisualState.Finished ? 0 : state == ActivityCardVisualState.InProgress ? 1 : 2;

    private static float GetCraftProgress(OnlineCraftSnapshot value) => value == null || value.durationSeconds <= 0
        ? 0f : Mathf.Clamp01(value.progressSeconds / value.durationSeconds);

    private static string BuildProgressText(ActivityCardState card)
    {
        if (card.VisualState != ActivityCardVisualState.InProgress)
            return string.Empty;
        var parts = new[] { card.RemainingTime, card.Cycle, $"{Mathf.RoundToInt(card.Progress * 100f)}%" }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        return string.Join("  ·  ", parts);
    }

    private static string CategoryName(ObjectActivityKind kind)
    {
        switch (kind)
        {
            case ObjectActivityKind.Work: return ActivityUiText.Get(ActivityUiText.CategoryWork);
            case ObjectActivityKind.Combat: return ActivityUiText.Get(ActivityUiText.CategoryHunting);
            case ObjectActivityKind.Construction: return ActivityUiText.Get(ActivityUiText.CategoryConstruction);
            case ObjectActivityKind.Craft: return ActivityUiText.Get(ActivityUiText.CategoryCraft);
            default: return ActivityUiText.Get(ActivityUiText.CategoryAction);
        }
    }

    private static bool IsActiveHeroLimitReached()
    {
        var playerState = RuntimePlayer.State;
        if (playerState == null)
            return true;

        var limit = ActiveHeroLimitResolver.GetCurrentLimit(new PlayerStateActivityAdapter(playerState));
        return playerState.GetActiveHeroCount() >= limit;
    }

    private static string FormatCycle(int current, int total) => total > 0 ? $"{Mathf.Clamp(current, 0, total)}/{total}" : string.Empty;
    private static string FormatCraftCycle(OnlineCraftSnapshot value) => value == null || value.plannedCycles <= 0
        ? string.Empty : FormatCycle(value.status == CraftExecutionStatus.ResultPending ? value.plannedCycles : 0, value.plannedCycles);

    private static string FormatRemainingTime(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            return string.Empty;
        var total = (long)Math.Ceiling(seconds);
        var hours = total / 3600;
        return hours > 0 ? $"{hours:00}:{total % 3600 / 60:00}:{total % 60:00}" : $"{total / 60:00}:{total % 60:00}";
    }

    private static string FormatRange(int min, int max)
    {
        min = Math.Max(1, min);
        max = Math.Max(min, max);
        return min == max ? min.ToString(CultureInfo.InvariantCulture) : $"{min}-{max}";
    }

    private static int Scale(int value, int cycles)
    {
        var result = (long)Math.Max(0, value) * Math.Max(1, cycles);
        return result > int.MaxValue ? int.MaxValue : (int)result;
    }

    private static bool IsItemReward(RewardTypeEnum type) => type == RewardTypeEnum.Resource ||
        type == RewardTypeEnum.Item || type == RewardTypeEnum.Equipment ||
        type == RewardTypeEnum.Consumable || type == RewardTypeEnum.Recipe;

    private static string LocaliseOrFallback(string id, string fallback)
    {
        var value = RuntimeConfigs.Localisation.Get(id);
        return string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value;
    }

    private readonly struct ActionDescriptor
    {
        public ActionDescriptor(string nameId, string descriptionId, string iconId)
        {
            NameId = nameId;
            DescriptionId = descriptionId;
            IconId = iconId;
        }
        public string NameId { get; }
        public string DescriptionId { get; }
        public string IconId { get; }
    }

    private readonly struct OrderedCardState
    {
        public OrderedCardState(int sortOrder, string id, ActivityCardState state)
        {
            SortOrder = sortOrder;
            Id = id ?? string.Empty;
            State = state;
        }
        public int SortOrder { get; }
        public string Id { get; }
        public ActivityCardState State { get; }
    }
}

public sealed class ObjectActivitiesController : IDisposable
{
    private const float RefreshIntervalSeconds = 0.25f;
    private readonly SettlementSceneView _sceneView;
    private readonly IObjectActivitiesRuntimeSource _runtimeSource;
    private readonly ObjectActivitiesSelection _selection = new ObjectActivitiesSelection();
    private UIService _uiService;
    private ObjectActivitiesWindowView _window;
    private HeroSelectionPopupView _heroPopup;
    private ObjectActivitiesWindowState _state = ObjectActivitiesWindowState.Empty;
    private string _buildingId;
    private int _buildingLevel;
    private float _nextRefreshTime;
    private bool _bound;
    private bool _reportedMissingUiRoot;

    public ObjectActivitiesController(SettlementSceneView sceneView, IObjectActivitiesRuntimeSource runtimeSource = null)
    {
        _sceneView = sceneView != null ? sceneView : throw new ArgumentNullException(nameof(sceneView));
        _runtimeSource = runtimeSource ?? new RuntimeObjectActivitiesSource();
    }

    public event Action<string> ActivitySelected;

    public void Bind()
    {
        if (_bound) return;
        _bound = true;
        _sceneView.BuildingSelected += HandleBuildingSelected;
    }

    public void Tick()
    {
        if (_window == null || Time.unscaledTime < _nextRefreshTime) return;
        _nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
        RefreshWindow();
    }

    public void Dispose()
    {
        if (_bound) _sceneView.BuildingSelected -= HandleBuildingSelected;
        _bound = false;
        CloseWindow();
    }

    private void HandleBuildingSelected(BuildingSelectionContext selection)
    {
        if (!_runtimeSource.IsReady || !_runtimeSource.CanOpen(selection.BuildingId, selection.BuildingLevel)) return;
        var roots = UnityEngine.Object.FindObjectsByType<UIRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (roots.Length != 1 || roots[0].Service == null)
        {
            if (!_reportedMissingUiRoot)
            {
                _reportedMissingUiRoot = true;
                Debug.LogError($"[ObjectActivities] Expected one initialized UIRoot, found {roots.Length}.", _sceneView);
            }
            return;
        }

        _reportedMissingUiRoot = false;
        _uiService = roots[0].Service;
        _buildingId = selection.BuildingId;
        _buildingLevel = selection.BuildingLevel;
        ResetSelection();
        _state = _runtimeSource.BuildState(_buildingId, _buildingLevel, _selection);
        AdoptRenderedSelection();

        _sceneView.SetSelectionBlocked(true);
        try
        {
            _window = _uiService.OpenWindow<ObjectActivitiesWindowView, ObjectActivitiesWindowOpenArgs>(
                new ObjectActivitiesWindowOpenArgs(_state, CloseWindow, HandleWindowClosed,
                    HandleActivitySelected, HandleCyclesChanged, HandleHeroPickerRequested, HandlePrimaryActionRequested));
            _nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
        }
        catch
        {
            _sceneView.SetSelectionBlocked(false);
            _uiService = null;
            _buildingId = null;
            throw;
        }
    }

    private void RefreshWindow()
    {
        if (!_runtimeSource.IsReady || !_runtimeSource.CanOpen(_buildingId, _buildingLevel))
        {
            CloseWindow();
            return;
        }
        _state = _runtimeSource.BuildState(_buildingId, _buildingLevel, _selection);
        AdoptRenderedSelection();
        _window?.Render(_state);
        _heroPopup?.Render(BuildHeroSelectionState());
    }

    private void HandleActivitySelected(string activityId)
    {
        _selection.ActivityId = activityId;
        _selection.HeroId = string.Empty;
        _selection.PlannedCycles = 1;
        ClearNotice();
        ActivitySelected?.Invoke(activityId);
        RefreshWindow();
    }

    private void HandleCyclesChanged(int value)
    {
        _selection.PlannedCycles = Mathf.Clamp(value, 1, RuntimeObjectActivitiesSource.DefaultCycleLimit);
        ClearNotice();
        RefreshWindow();
    }

    private void HandleHeroPickerRequested()
    {
        if (_uiService == null || _state.SelectedActivity == null || !_state.SelectedActivity.CanConfigure)
            return;

        _heroPopup = _uiService.OpenWindow<HeroSelectionPopupView, HeroSelectionPopupOpenArgs>(
            new HeroSelectionPopupOpenArgs(
                BuildHeroSelectionState(),
                CloseHeroPopup,
                HandleHeroPopupClosed,
                HandleHeroSelected));
    }

    private void HandleHeroSelected(string heroId)
    {
        var choice = _state.SelectedActivity?.HeroChoices.FirstOrDefault(value =>
            value.CanSelect && string.Equals(value.HeroId, heroId, StringComparison.Ordinal));
        if (choice == null)
        {
            SetNotice(ActivityUiText.HeroUnavailable);
            RefreshWindow();
            return;
        }
        _selection.HeroId = choice.HeroId;
        ClearNotice();
        CloseHeroPopup();
        RefreshWindow();
    }

    private HeroSelectionPopupState BuildHeroSelectionState()
    {
        var selected = _state.SelectedActivity ?? SelectedActivityState.Empty;
        return new HeroSelectionPopupState(
            ActivityUiText.Get(ActivityUiText.HeroSelectionTitle),
            string.IsNullOrWhiteSpace(selected.Name)
                ? string.Empty
                : ActivityUiText.Format(ActivityUiText.HeroSelectionContext, selected.Name),
            ActivityCardProductionInfo.IconResolver.ResolveItem("fatigue_icon"),
            selected.HeroChoices);
    }

    private void HandlePrimaryActionRequested()
    {
        var selected = _state.SelectedActivity;
        if (selected == null || string.IsNullOrWhiteSpace(selected.ActivityId)) return;
        if (selected.VisualState == ActivityCardVisualState.Finished)
        {
            var result = OnlineActivityRuntime.Claim(selected.PendingResultId);
            if (result.Success)
                ClearNotice();
            else
                SetNotice(ActivityUiText.ClaimFailed);
            RefreshWindow();
            return;
        }
        if (selected.VisualState == ActivityCardVisualState.InProgress &&
            selected.Kind == ObjectActivityKind.Work &&
            !string.IsNullOrWhiteSpace(selected.ExecutionId))
        {
            var result = OnlineActivityRuntime.Cancel(selected.ExecutionId);
            if (result.success)
                ClearNotice();
            else
                SetNotice(ActivityUiText.CancelFailed);
            RefreshWindow();
            return;
        }
        if (!selected.PrimaryActionEnabled) return;

        var success = false;
        if (selected.Kind == ObjectActivityKind.Craft)
        {
            var result = OnlineActivityRuntime.StartCraft(selected.ActivityId, selected.HeroId,
                _buildingId, _buildingLevel, selected.PlannedCycles);
            success = result.success;
        }
        else if (selected.Kind == ObjectActivityKind.Combat)
        {
            var result = OnlineActivityRuntime.StartCombat(selected.ActivityId, selected.HeroId);
            success = result.success;
        }
        else
        {
            var result = OnlineActivityRuntime.Start(new ActivityStartRequest
            {
                activityId = selected.ActivityId,
                heroId = selected.HeroId,
                plannedCycleCount = selected.ShowCycles ? selected.PlannedCycles : null
            });
            success = result.success;
        }
        if (success)
            ClearNotice();
        else
            SetNotice(ActivityUiText.StartFailed);
        RefreshWindow();
    }

    private void AdoptRenderedSelection()
    {
        if (_state?.SelectedActivity == null) return;
        _selection.ActivityId = _state.SelectedActivity.ActivityId;
        _selection.HeroId = _state.SelectedActivity.HeroId;
        _selection.PlannedCycles = _state.SelectedActivity.PlannedCycles;
    }

    private void ResetSelection()
    {
        _selection.ActivityId = string.Empty;
        _selection.HeroId = string.Empty;
        _selection.PlannedCycles = 1;
        _selection.Notice = string.Empty;
        _selection.NoticeId = string.Empty;
    }

    private void SetNotice(string localisationId)
    {
        _selection.Notice = string.Empty;
        _selection.NoticeId = localisationId ?? string.Empty;
    }

    private void ClearNotice()
    {
        _selection.Notice = string.Empty;
        _selection.NoticeId = string.Empty;
    }

    private void CloseWindow()
    {
        var service = _uiService;
        if (service == null) { HandleWindowClosed(); return; }
        try
        {
            CloseHeroPopup();
            if (service.IsWindowOpen<ObjectActivitiesWindowView>()) service.CloseWindow<ObjectActivitiesWindowView>();
            else HandleWindowClosed();
        }
        catch (ObjectDisposedException) { HandleWindowClosed(); }
    }

    private void HandleWindowClosed()
    {
        _window = null;
        _heroPopup = null;
        _uiService = null;
        _state = ObjectActivitiesWindowState.Empty;
        _buildingId = null;
        _buildingLevel = 0;
        ResetSelection();
        _sceneView.SetSelectionBlocked(false);
    }

    private void CloseHeroPopup()
    {
        var service = _uiService;
        if (service == null)
        {
            HandleHeroPopupClosed();
            return;
        }

        try
        {
            if (service.IsWindowOpen<HeroSelectionPopupView>())
                service.CloseWindow<HeroSelectionPopupView>();
            else
                HandleHeroPopupClosed();
        }
        catch (ObjectDisposedException)
        {
            HandleHeroPopupClosed();
        }
    }

    private void HandleHeroPopupClosed() => _heroPopup = null;
}
