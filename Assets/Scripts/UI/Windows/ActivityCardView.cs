using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ActivityCardView : MonoBehaviour
{
    [Header("Card")]
    [SerializeField] private Button _selectButton;
    [SerializeField] private Image _background;
    [SerializeField] private GameObject _selectionMarker;
    [SerializeField] private TMP_Text _activityName;
    [SerializeField] private Image _activityIcon;

    [Header("Compact information")]
    [SerializeField] private GameObject _durationMetric;
    [SerializeField] private TMP_Text _durationValue;
    [SerializeField] private GameObject _energyMetric;
    [SerializeField] private TMP_Text _energyValue;
    [SerializeField] private GameObject _skillMetric;
    [SerializeField] private Image _skillIcon;
    [SerializeField] private TMP_Text _skillName;

    [Header("States")]
    [SerializeField] private GameObject _finishState;
    [SerializeField] private TMP_Text _finishText;
    [SerializeField] private TMP_Text _currentDropItemsCount;
    [SerializeField] private GameObject _processState;
    [SerializeField] private Slider _activityProgress;
    [SerializeField] private Image _heroInActivity;
    [SerializeField] private TMP_Text _heroInitial;
    [SerializeField] private TMP_Text _heroName;
    [SerializeField] private TMP_Text _timeRemainingText;
    [SerializeField] private TMP_Text _cycleText;
    [SerializeField] private TMP_Text _progressText;
    [SerializeField] private GameObject _idleState;
    [SerializeField] private TMP_Text _idleText;
    [SerializeField] private GameObject _unavailableState;
    [SerializeField] private TMP_Text _unavailableText;

    [Header("Palette")]
    [SerializeField] private Color _normalColor = new Color32(255, 248, 233, 255);
    [SerializeField] private Color _selectedColor = new Color32(255, 242, 207, 255);
    [SerializeField] private Color _finishedColor = new Color32(222, 244, 219, 255);
    [SerializeField] private Color _unavailableColor = new Color32(232, 226, 214, 255);

    private string _activityId;
    private Action<string> _selectRequested;

    public void Render(ActivityCardState state, Action<string> selectRequested)
    {
        state ??= ActivityCardState.Empty;
        _activityId = state.ActivityId;
        _selectRequested = selectRequested;

        SetText(_activityName, state.Name);
        ActivityCardProductionInfo.SetIcon(
            _activityIcon,
            ActivityCardProductionInfo.IconResolver.ResolveCard(state.IconId));

        SetMetric(_durationMetric, _durationValue, state.DurationValue);
        SetMetric(_energyMetric, _energyValue, state.EnergyValue);
        SetMetric(_skillMetric, _skillName, state.SkillName);
        ActivityCardProductionInfo.SetIcon(
            _skillIcon,
            ActivityCardProductionInfo.IconResolver.ResolveSkill(state.SkillIconId));

        if (_activityProgress != null)
            _activityProgress.normalizedValue = Mathf.Clamp01(state.Progress);

        ActivityCardProductionInfo.SetIcon(
            _heroInActivity,
            ActivityCardProductionInfo.IconResolver.ResolveHero(state.HeroIconId));
        SetText(_heroInitial, Initial(state.HeroName));
        SetText(_heroName, state.HeroName);
        SetText(_timeRemainingText, state.RemainingTime);
        SetText(_cycleText, state.Cycle);
        SetText(_progressText, $"{Mathf.RoundToInt(Mathf.Clamp01(state.Progress) * 100f)}%");
        SetText(_currentDropItemsCount, state.CurrentDropItemsCount > 0 ? state.CurrentDropItemsCount.ToString() : string.Empty);
        SetText(_finishText, "Готово");
        SetText(_idleText, "Доступно");
        SetText(_unavailableText, state.BlockReason);

        SetVisualState(state.VisualState);
        if (_selectionMarker != null)
            _selectionMarker.SetActive(state.IsSelected);
        if (_background != null)
            _background.color = ResolveBackground(state);

        if (_selectButton != null)
        {
            _selectButton.onClick.RemoveListener(HandleSelectRequested);
            _selectButton.onClick.AddListener(HandleSelectRequested);
            _selectButton.interactable = state.CanSelect;
        }
    }

    private void OnDestroy()
    {
        if (_selectButton != null)
            _selectButton.onClick.RemoveListener(HandleSelectRequested);
    }

    private void HandleSelectRequested()
    {
        if (!string.IsNullOrWhiteSpace(_activityId))
            _selectRequested?.Invoke(_activityId);
    }

    private void SetVisualState(ActivityCardVisualState state)
    {
        SetActive(_finishState, state == ActivityCardVisualState.Finished);
        SetActive(_processState, state == ActivityCardVisualState.InProgress);
        SetActive(_idleState, state == ActivityCardVisualState.Idle);
        SetActive(_unavailableState, state == ActivityCardVisualState.Unavailable);
    }

    private Color ResolveBackground(ActivityCardState state)
    {
        if (state.VisualState == ActivityCardVisualState.Finished)
            return _finishedColor;
        if (state.VisualState == ActivityCardVisualState.Unavailable)
            return _unavailableColor;
        return state.IsSelected ? _selectedColor : _normalColor;
    }

    private static void SetMetric(GameObject container, TMP_Text text, string value)
    {
        var visible = !string.IsNullOrWhiteSpace(value);
        SetActive(container, visible);
        SetText(text, value);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null)
            target.SetActive(active);
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
            target.text = value ?? string.Empty;
    }

    private static string Initial(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Substring(0, 1).ToUpperInvariant();
    }
}

public enum ActivityCardVisualState
{
    Unavailable = 0,
    Idle = 1,
    InProgress = 2,
    Finished = 3
}

public enum ObjectActivityKind
{
    Activity = 0,
    Work = 1,
    Combat = 2,
    Construction = 3,
    Craft = 4
}

public sealed class ActivityCardState
{
    public static readonly ActivityCardState Empty = new ActivityCardState(
        string.Empty, ObjectActivityKind.Activity, string.Empty, string.Empty, false, false, false,
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, string.Empty, 0f, string.Empty, string.Empty, 0L, string.Empty,
        ActivityCardVisualState.Unavailable);

    public ActivityCardState(
        string activityId,
        ObjectActivityKind kind,
        string name,
        string iconId,
        bool canSelect,
        bool isAvailable,
        bool isSelected,
        string blockReason,
        string durationValue,
        string energyValue,
        string skillName,
        string skillIconId,
        string heroId,
        string heroName,
        string heroIconId,
        float progress,
        string remainingTime,
        string cycle,
        long currentDropItemsCount,
        string pendingResultId,
        ActivityCardVisualState visualState)
    {
        ActivityId = activityId ?? string.Empty;
        Kind = kind;
        Name = name ?? string.Empty;
        IconId = iconId ?? string.Empty;
        CanSelect = canSelect;
        IsAvailable = isAvailable;
        IsSelected = isSelected;
        BlockReason = blockReason ?? string.Empty;
        DurationValue = durationValue ?? string.Empty;
        EnergyValue = energyValue ?? string.Empty;
        SkillName = skillName ?? string.Empty;
        SkillIconId = skillIconId ?? string.Empty;
        HeroId = heroId ?? string.Empty;
        HeroName = heroName ?? string.Empty;
        HeroIconId = heroIconId ?? string.Empty;
        Progress = Mathf.Clamp01(progress);
        RemainingTime = remainingTime ?? string.Empty;
        Cycle = cycle ?? string.Empty;
        CurrentDropItemsCount = Math.Max(0L, currentDropItemsCount);
        PendingResultId = pendingResultId ?? string.Empty;
        VisualState = visualState;
    }

    public string ActivityId { get; }
    public ObjectActivityKind Kind { get; }
    public string Name { get; }
    public string IconId { get; }
    public bool CanSelect { get; }
    public bool IsAvailable { get; }
    public bool IsSelected { get; }
    public string BlockReason { get; }
    public string DurationValue { get; }
    public string EnergyValue { get; }
    public string SkillName { get; }
    public string SkillIconId { get; }
    public string HeroId { get; }
    public string HeroName { get; }
    public string HeroIconId { get; }
    public float Progress { get; }
    public string RemainingTime { get; }
    public string Cycle { get; }
    public long CurrentDropItemsCount { get; }
    public string PendingResultId { get; }
    public ActivityCardVisualState VisualState { get; }
}

[Serializable]
public sealed class ActivityCardProductionInfo
{
    public GameObject Panel;
    public List<RequiredSkillsInfo> RequiredSkills = new List<RequiredSkillsInfo>();
    public GameObject RequiredSkillsPanel;
    public List<RequiredItemInfo> RequiredItems = new List<RequiredItemInfo>();
    public GameObject RequiredItemsPanel;
    public List<RequiredItemInfo> ProductItems = new List<RequiredItemInfo>();
    public GameObject ProductItemsPanel;

    public void Render(ActivityCardProductionState state)
    {
        state ??= ActivityCardProductionState.Empty;
        var skillsVisible = RenderSkills(RequiredSkills, state.RequiredSkills);
        var requiredItemsVisible = RenderItems(RequiredItems, state.RequiredItems);
        var productItemsVisible = RenderItems(
            ProductItems,
            state.IsConstruction ? Array.Empty<ActivityCardProductionEntryState>() : state.ProductItems);

        SetActive(RequiredSkillsPanel, skillsVisible);
        SetActive(RequiredItemsPanel, requiredItemsVisible);
        SetActive(ProductItemsPanel, productItemsVisible);
        SetActive(Panel, skillsVisible || requiredItemsVisible || productItemsVisible);
    }

    private static bool RenderSkills(
        IReadOnlyList<RequiredSkillsInfo> views,
        IReadOnlyList<ActivityCardProductionEntryState> states)
    {
        views ??= Array.Empty<RequiredSkillsInfo>();
        states ??= Array.Empty<ActivityCardProductionEntryState>();
        var count = Math.Min(views.Count, states.Count);
        for (var index = 0; index < views.Count; index++)
            views[index]?.Render(index < count ? states[index] : null);
        return count > 0;
    }

    private static bool RenderItems(
        IReadOnlyList<RequiredItemInfo> views,
        IReadOnlyList<ActivityCardProductionEntryState> states)
    {
        views ??= Array.Empty<RequiredItemInfo>();
        states ??= Array.Empty<ActivityCardProductionEntryState>();
        var count = Math.Min(views.Count, states.Count);
        for (var index = 0; index < views.Count; index++)
            views[index]?.Render(index < count ? states[index] : null);
        return count > 0;
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null)
            target.SetActive(active);
    }

    [Serializable]
    public sealed class RequiredSkillsInfo
    {
        public GameObject SkillContainer;
        public Image SkillIcon;
        public TMP_Text SkillLevel;

        public void Render(ActivityCardProductionEntryState state)
        {
            SetActive(SkillContainer, state != null);
            if (SkillLevel != null)
                SkillLevel.text = state?.Value ?? string.Empty;
            SetIcon(SkillIcon, state == null ? null : IconResolver.Resolve(state));
        }
    }

    [Serializable]
    public sealed class RequiredItemInfo
    {
        public GameObject ItemContainer;
        public Image itemIcon;
        public TMP_Text ItemCount;

        public void Render(ActivityCardProductionEntryState state)
        {
            SetActive(ItemContainer, state != null);
            if (ItemCount != null)
                ItemCount.text = state?.Value ?? string.Empty;
            SetIcon(itemIcon, state == null ? null : IconResolver.Resolve(state));
        }
    }

    internal static void SetIcon(Image target, Sprite sprite)
    {
        if (target == null)
            return;
        target.sprite = sprite;
        target.enabled = sprite != null;
    }

    internal static class IconResolver
    {
        private static readonly string[] SkillPaths = { "Sprites/Icons/Skills" };
        private static readonly string[] CardPaths =
        {
            "Sprites/Icons/Activity", "Sprites/Icons/Goods", "Sprites/Icons/Weapons", "Sprites/Icons/Misc"
        };
        private static readonly string[] ItemPaths =
        {
            "Sprites/Icons/Goods", "Sprites/Icons/Weapons", "Sprites/Icons/Misc"
        };
        private static readonly string[] HeroPaths =
        {
            "Sprites/Heroes/Icons", "Sprites/Heroes", "Sprites/Icons/Heroes"
        };
        private static readonly Dictionary<string, Sprite> Cache =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);

        public static Sprite ResolveSkill(string iconId) => Resolve(iconId, SkillPaths);
        public static Sprite ResolveCard(string iconId) => Resolve(iconId, CardPaths);
        public static Sprite ResolveItem(string iconId) => Resolve(iconId, ItemPaths);
        public static Sprite ResolveHero(string iconId) => Resolve(iconId, HeroPaths);

        public static Sprite Resolve(ActivityCardProductionEntryState state)
        {
            if (state == null)
                return null;
            switch (state.IconKind)
            {
                case ActivityProductionIconKind.Skill:
                    return ResolveSkill(state.IconId);
                case ActivityProductionIconKind.Energy:
                    return ResolveItem("fatigue_icon");
                default:
                    return ResolveItem(state.IconId);
            }
        }

        private static Sprite Resolve(string iconId, IReadOnlyList<string> paths)
        {
            if (string.IsNullOrWhiteSpace(iconId))
                return null;
            foreach (var path in paths)
            {
                var resourcePath = $"{path}/{iconId}";
                if (!Cache.TryGetValue(resourcePath, out var sprite))
                {
                    var sprites = Resources.LoadAll<Sprite>(resourcePath);
                    sprite = Array.Find(sprites, value => string.Equals(value.name, iconId, StringComparison.Ordinal)) ??
                             (sprites.Length > 0 ? sprites[0] : null);
                    Cache[resourcePath] = sprite;
                }
                if (sprite != null)
                    return sprite;
            }
            return null;
        }
    }
}

public enum ActivityProductionIconKind
{
    Item = 0,
    Skill = 1,
    Energy = 2
}

public sealed class ActivityCardProductionState
{
    public static readonly ActivityCardProductionState Empty = new ActivityCardProductionState(
        Array.Empty<ActivityCardProductionEntryState>(),
        Array.Empty<ActivityCardProductionEntryState>(),
        Array.Empty<ActivityCardProductionEntryState>(),
        false);

    public ActivityCardProductionState(
        IReadOnlyList<ActivityCardProductionEntryState> requiredSkills,
        IReadOnlyList<ActivityCardProductionEntryState> requiredItems,
        IReadOnlyList<ActivityCardProductionEntryState> productItems,
        bool isConstruction)
    {
        RequiredSkills = requiredSkills ?? Array.Empty<ActivityCardProductionEntryState>();
        RequiredItems = requiredItems ?? Array.Empty<ActivityCardProductionEntryState>();
        ProductItems = productItems ?? Array.Empty<ActivityCardProductionEntryState>();
        IsConstruction = isConstruction;
    }

    public IReadOnlyList<ActivityCardProductionEntryState> RequiredSkills { get; }
    public IReadOnlyList<ActivityCardProductionEntryState> RequiredItems { get; }
    public IReadOnlyList<ActivityCardProductionEntryState> ProductItems { get; }
    public bool IsConstruction { get; }
}

public sealed class ActivityCardProductionEntryState
{
    public ActivityCardProductionEntryState(
        string iconId,
        string value,
        ActivityProductionIconKind iconKind = ActivityProductionIconKind.Item)
    {
        IconId = iconId ?? string.Empty;
        Value = value ?? string.Empty;
        IconKind = iconKind;
    }

    public string IconId { get; }
    public string Value { get; }
    public ActivityProductionIconKind IconKind { get; }
}
