using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RuntimeConfigs = GuildIdle.Configs.Configs;

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
    [SerializeField] private GameObject _dangerMetric;
    [SerializeField] private TMP_Text _dangerValue;

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
        SetMetric(_skillMetric, _skillName, state.RequiredSkillValue, !string.IsNullOrWhiteSpace(state.SkillIconId));
        ActivityCardProductionInfo.SetIcon(
            _skillIcon,
            ActivityCardProductionInfo.IconResolver.ResolveSkill(state.SkillIconId));
        SetMetric(_dangerMetric, _dangerValue, state.DangerChanceValue);

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
        SetText(_finishText, ActivityUiText.Get(ActivityUiText.CardReady));
        SetText(_idleText, ActivityUiText.Get(ActivityUiText.CardAvailable));
        SetText(_unavailableText, string.IsNullOrWhiteSpace(state.BlockReason)
            ? ActivityUiText.Get(ActivityUiText.CardUnavailable)
            : state.BlockReason);

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
        SetMetric(container, text, value, !string.IsNullOrWhiteSpace(value));
    }

    private static void SetMetric(GameObject container, TMP_Text text, string value, bool visible)
    {
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
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, string.Empty, 0f, string.Empty, string.Empty, 0L, string.Empty,
        string.Empty, ActivityCardVisualState.Unavailable);

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
        string requiredSkillValue,
        string skillIconId,
        string dangerChanceValue,
        string heroId,
        string heroName,
        string heroIconId,
        float progress,
        string remainingTime,
        string cycle,
        long currentDropItemsCount,
        string executionId,
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
        RequiredSkillValue = requiredSkillValue ?? string.Empty;
        SkillIconId = skillIconId ?? string.Empty;
        DangerChanceValue = dangerChanceValue ?? string.Empty;
        HeroId = heroId ?? string.Empty;
        HeroName = heroName ?? string.Empty;
        HeroIconId = heroIconId ?? string.Empty;
        Progress = Mathf.Clamp01(progress);
        RemainingTime = remainingTime ?? string.Empty;
        Cycle = cycle ?? string.Empty;
        CurrentDropItemsCount = Math.Max(0L, currentDropItemsCount);
        ExecutionId = executionId ?? string.Empty;
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
    public string RequiredSkillValue { get; }
    public string SkillIconId { get; }
    public string DangerChanceValue { get; }
    public string HeroId { get; }
    public string HeroName { get; }
    public string HeroIconId { get; }
    public float Progress { get; }
    public string RemainingTime { get; }
    public string Cycle { get; }
    public long CurrentDropItemsCount { get; }
    public string ExecutionId { get; }
    public string PendingResultId { get; }
    public ActivityCardVisualState VisualState { get; }
}

[Serializable]
public sealed class ActivityCardProductionInfo
{
    public GameObject Panel;
    public List<RequiredSkillsInfo> RequiredSkills = new List<RequiredSkillsInfo>();
    public GameObject RequiredSkillsPanel;
    public TMP_Text RequiredSkillsTitle;
    public List<RequiredItemInfo> RequiredItems = new List<RequiredItemInfo>();
    public GameObject RequiredItemsPanel;
    public TMP_Text RequiredItemsTitle;
    public List<RequiredItemInfo> ProductItems = new List<RequiredItemInfo>();
    public GameObject ProductItemsPanel;
    public TMP_Text ProductItemsTitle;

    public void Render(ActivityCardProductionState state)
    {
        state ??= ActivityCardProductionState.Empty;
        EnsureScrollableList(RequiredSkills != null && RequiredSkills.Count > 0 ? RequiredSkills[0]?.SkillContainer : null);
        EnsureScrollableList(RequiredItems != null && RequiredItems.Count > 0 ? RequiredItems[0]?.ItemContainer : null);
        EnsureScrollableList(ProductItems != null && ProductItems.Count > 0 ? ProductItems[0]?.ItemContainer : null);
        EnsureSkillsCapacity(RequiredSkills, state.RequiredSkills.Count);
        EnsureItemsCapacity(RequiredItems, state.RequiredItems.Count);
        EnsureItemsCapacity(ProductItems, state.IsConstruction ? 0 : state.ProductItems.Count);

        var skillsVisible = RenderSkills(RequiredSkills, state.RequiredSkills);
        var requiredItemsVisible = RenderItems(RequiredItems, state.RequiredItems);
        var productItemsVisible = RenderItems(
            ProductItems,
            state.IsConstruction ? Array.Empty<ActivityCardProductionEntryState>() : state.ProductItems);
        LayoutSkillRows(RequiredSkills, state.RequiredSkills.Count);
        LayoutItemRows(RequiredItems, state.RequiredItems.Count);
        LayoutItemRows(ProductItems, state.IsConstruction ? 0 : state.ProductItems.Count);

        SetActive(RequiredSkillsPanel, skillsVisible);
        SetActive(RequiredItemsPanel, requiredItemsVisible);
        SetActive(ProductItemsPanel, productItemsVisible);
        SetActive(Panel, skillsVisible || requiredItemsVisible || productItemsVisible);
        SetText(RequiredSkillsTitle, ActivityUiText.Get(ActivityUiText.Skills));
        SetText(RequiredItemsTitle, ActivityUiText.Get(ActivityUiText.Ingredients));
        SetText(ProductItemsTitle, ActivityUiText.Get(ActivityUiText.Result));
    }

    private static void EnsureScrollableList(GameObject firstRow)
    {
        if (firstRow == null)
            return;
        var rowParent = firstRow.transform.parent as RectTransform;
        if (rowParent == null)
            return;

        if (rowParent.name == "Content" && rowParent.parent != null &&
            rowParent.parent.TryGetComponent<ScrollRect>(out _))
        {
            DisableAutomaticLayout(rowParent);
            return;
        }
        if (rowParent.TryGetComponent<ScrollRect>(out _))
            return;

        var viewport = rowParent;
        var grid = viewport.GetComponent<GridLayoutGroup>();
        if (grid != null)
            grid.enabled = false;

        var existingRows = new Transform[viewport.childCount];
        for (var index = 0; index < existingRows.Length; index++)
            existingRows[index] = viewport.GetChild(index);

        var contentObject = new GameObject("Content", typeof(RectTransform));
        contentObject.layer = viewport.gameObject.layer;
        var content = (RectTransform)contentObject.transform;
        content.SetParent(viewport, false);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        foreach (var row in existingRows)
            row.SetParent(content, false);

        if (viewport.GetComponent<RectMask2D>() == null)
            viewport.gameObject.AddComponent<RectMask2D>();
        var raycastSurface = viewport.GetComponent<Image>() ?? viewport.gameObject.AddComponent<Image>();
        raycastSurface.color = Color.clear;
        raycastSurface.raycastTarget = true;

        var scrollRect = viewport.gameObject.AddComponent<ScrollRect>();
        scrollRect.viewport = viewport;
        scrollRect.content = content;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 24f;
    }

    private static void DisableAutomaticLayout(RectTransform content)
    {
        var layout = content.GetComponent<LayoutGroup>();
        if (layout != null)
            layout.enabled = false;
        var fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter != null)
            fitter.enabled = false;
    }

    private static void LayoutSkillRows(IReadOnlyList<RequiredSkillsInfo> views, int visibleCount)
    {
        if (views == null)
            return;
        var count = Math.Min(views.Count, Math.Max(0, visibleCount));
        RectTransform content = null;
        for (var index = 0; index < count; index++)
            PositionRow(views[index]?.SkillContainer, index, ref content);
        if (content == null && views.Count > 0 && views[0]?.SkillContainer != null)
            content = views[0].SkillContainer.transform.parent as RectTransform;
        SetContentHeight(content, count);
    }

    private static void LayoutItemRows(IReadOnlyList<RequiredItemInfo> views, int visibleCount)
    {
        if (views == null)
            return;
        var count = Math.Min(views.Count, Math.Max(0, visibleCount));
        RectTransform content = null;
        for (var index = 0; index < count; index++)
            PositionRow(views[index]?.ItemContainer, index, ref content);
        if (content == null && views.Count > 0 && views[0]?.ItemContainer != null)
            content = views[0].ItemContainer.transform.parent as RectTransform;
        SetContentHeight(content, count);
    }

    private static void PositionRow(GameObject row, int index, ref RectTransform content)
    {
        if (row == null)
            return;
        var rowRect = row.transform as RectTransform;
        if (rowRect == null)
            return;
        content ??= rowRect.parent as RectTransform;
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, -index * 50f);
        rowRect.sizeDelta = new Vector2(0f, 44f);
    }

    private static void SetContentHeight(RectTransform content, int rowCount)
    {
        if (content == null)
            return;
        var height = rowCount > 0 ? rowCount * 44f + (rowCount - 1) * 6f : 0f;
        content.sizeDelta = new Vector2(0f, height);
        var viewport = content.parent as RectTransform;
        var maxOffset = viewport != null ? Mathf.Max(0f, height - viewport.rect.height) : 0f;
        content.anchoredPosition = new Vector2(0f, Mathf.Clamp(content.anchoredPosition.y, 0f, maxOffset));
        content.ForceUpdateRectTransforms();
    }

    private static void EnsureSkillsCapacity(List<RequiredSkillsInfo> views, int requiredCount)
    {
        if (views == null || views.Count == 0)
            return;

        var template = views[0];
        while (views.Count < requiredCount)
        {
            var clone = template?.Clone(views.Count + 1);
            if (clone == null)
                break;
            views.Add(clone);
        }
    }

    private static void EnsureItemsCapacity(List<RequiredItemInfo> views, int requiredCount)
    {
        if (views == null || views.Count == 0)
            return;

        var template = views[0];
        while (views.Count < requiredCount)
        {
            var clone = template?.Clone(views.Count + 1);
            if (clone == null)
                break;
            views.Add(clone);
        }
    }

    private static T FindChild<T>(GameObject root, string childName) where T : Component
    {
        if (root == null)
            return null;
        var child = root.transform.Find(childName);
        return child != null ? child.GetComponent<T>() : null;
    }

    private static void ConfigureEntryLayout(
        GameObject container,
        Image icon,
        TMP_Text name,
        TMP_Text value,
        float valueWidth = 54f,
        float nameRightInset = 66f)
    {
        if (container == null)
            return;

        if (icon != null)
        {
            var iconRect = icon.rectTransform;
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = new Vector2(25f, 0f);
            iconRect.sizeDelta = new Vector2(40f, 40f);
        }

        if (value != null)
        {
            var valueRect = value.rectTransform;
            valueRect.anchorMin = new Vector2(1f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.anchoredPosition = new Vector2(-8f, 0f);
            valueRect.sizeDelta = new Vector2(valueWidth, -8f);
            value.horizontalAlignment = HorizontalAlignmentOptions.Right;
            value.verticalAlignment = VerticalAlignmentOptions.Middle;
            value.fontSize = 18f;
            value.fontStyle = FontStyles.Bold;
            value.enableAutoSizing = true;
            value.fontSizeMin = 11f;
            value.fontSizeMax = 18f;
        }

        if (name != null)
        {
            var nameRect = name.rectTransform;
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = Vector2.one;
            nameRect.pivot = new Vector2(0.5f, 0.5f);
            nameRect.offsetMin = new Vector2(52f, 5f);
            nameRect.offsetMax = new Vector2(-nameRightInset, -5f);
            name.horizontalAlignment = HorizontalAlignmentOptions.Left;
            name.verticalAlignment = VerticalAlignmentOptions.Middle;
            name.fontSize = 15f;
            name.fontStyle = FontStyles.Normal;
            name.enableAutoSizing = true;
            name.fontSizeMin = 10f;
            name.fontSizeMax = 15f;
            name.textWrappingMode = TextWrappingModes.Normal;
            name.overflowMode = TextOverflowModes.Ellipsis;
        }
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

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
            target.text = value ?? string.Empty;
    }

    [Serializable]
    public sealed class RequiredSkillsInfo
    {
        public GameObject SkillContainer;
        public Image SkillIcon;
        public TMP_Text SkillName;
        public TMP_Text SkillLevel;

        public void Render(ActivityCardProductionEntryState state)
        {
            SetActive(SkillContainer, state != null);
            if (state == null)
                return;

            EnsureNameLabel();
            ConfigureEntryLayout(SkillContainer, SkillIcon, SkillName, SkillLevel);
            if (SkillName != null)
                SkillName.text = state.Name;
            if (SkillLevel != null)
                SkillLevel.text = state.Value;
            SetIcon(SkillIcon, IconResolver.Resolve(state));
        }

        public RequiredSkillsInfo Clone(int index)
        {
            if (SkillContainer == null || SkillContainer.transform.parent == null)
                return null;
            var root = UnityEngine.Object.Instantiate(SkillContainer, SkillContainer.transform.parent, false);
            root.name = $"Slot{index}";
            root.SetActive(false);
            return new RequiredSkillsInfo
            {
                SkillContainer = root,
                SkillIcon = FindChild<Image>(root, "Icon"),
                SkillName = FindChild<TMP_Text>(root, "Name"),
                SkillLevel = FindChild<TMP_Text>(root, "Value")
            };
        }

        private void EnsureNameLabel()
        {
            if (SkillName != null || SkillLevel == null || SkillContainer == null)
                return;
            SkillName = UnityEngine.Object.Instantiate(SkillLevel, SkillContainer.transform, false);
            SkillName.name = "Name";
        }
    }

    [Serializable]
    public sealed class RequiredItemInfo
    {
        public GameObject ItemContainer;
        public Image itemIcon;
        public TMP_Text ItemName;
        public TMP_Text ItemCount;

        public void Render(ActivityCardProductionEntryState state)
        {
            SetActive(ItemContainer, state != null);
            if (state == null)
                return;

            EnsureNameLabel();
            ConfigureRowLayout();
            if (ItemName != null)
                ItemName.text = state.Name;
            if (ItemCount != null)
                ItemCount.text = state.Value;
            SetIcon(itemIcon, IconResolver.Resolve(state));
        }

        public RequiredItemInfo Clone(int index)
        {
            if (ItemContainer == null || ItemContainer.transform.parent == null)
                return null;
            var root = UnityEngine.Object.Instantiate(ItemContainer, ItemContainer.transform.parent, false);
            root.name = $"Slot{index}";
            root.SetActive(false);
            return new RequiredItemInfo
            {
                ItemContainer = root,
                itemIcon = FindChild<Image>(root, "Icon"),
                ItemName = FindChild<TMP_Text>(root, "Name"),
                ItemCount = FindChild<TMP_Text>(root, "Value")
            };
        }

        private void EnsureNameLabel()
        {
            if (ItemName != null || ItemCount == null || ItemContainer == null)
                return;

            ItemName = UnityEngine.Object.Instantiate(ItemCount, ItemContainer.transform, false);
            ItemName.name = "Name";
        }

        private void ConfigureRowLayout()
        {
            ConfigureEntryLayout(ItemContainer, itemIcon, ItemName, ItemCount, 84f, 96f);
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

internal static class ActivityUiText
{
    public const string WindowTitle = "ui.object_activities.title";
    public const string Level = "ui.object_activities.level";
    public const string AvailableCount = "ui.object_activities.available_count";
    public const string SelectAction = "ui.object_activities.select_action";
    public const string Cycles = "ui.object_activities.cycles";
    public const string Skills = "ui.object_activities.skills";
    public const string Ingredients = "ui.object_activities.ingredients";
    public const string Result = "ui.object_activities.result";
    public const string Energy = "ui.object_activities.energy";
    public const string AssignHero = "ui.object_activities.assign_hero";
    public const string Start = "ui.object_activities.start";
    public const string Claim = "ui.object_activities.claim";
    public const string Cancel = "ui.object_activities.cancel";
    public const string InProgress = "ui.object_activities.in_progress";
    public const string HeroLimit = "ui.object_activities.hero_limit";
    public const string NoFreeHero = "ui.object_activities.no_free_hero";
    public const string HeroUnavailable = "ui.object_activities.hero_unavailable";
    public const string StartFailed = "ui.object_activities.start_failed";
    public const string ClaimFailed = "ui.object_activities.claim_failed";
    public const string CancelFailed = "ui.object_activities.cancel_failed";
    public const string CategoryAction = "ui.object_activities.category.action";
    public const string CategoryWork = "ui.object_activities.category.work";
    public const string CategoryHunting = "ui.object_activities.category.hunting";
    public const string CategoryConstruction = "ui.object_activities.category.construction";
    public const string CategoryCraft = "ui.object_activities.category.craft";
    public const string RequirementLevel = "ui.object_activities.requirement_level";
    public const string SkillXp = "ui.object_activities.skill_xp";
    public const string HeroSelectionTitle = "ui.hero_selection.title";
    public const string HeroSelectionContext = "ui.hero_selection.context";
    public const string HeroSelectionHint = "ui.hero_selection.hint";
    public const string HeroAvailable = "ui.hero_selection.status.available";
    public const string HeroBusy = "ui.hero_selection.status.busy";
    public const string HeroNoEnergy = "ui.hero_selection.status.no_energy";
    public const string CardReady = "ui.activity_card.ready";
    public const string CardAvailable = "ui.activity_card.available";
    public const string CardUnavailable = "ui.activity_card.unavailable";

    public static string Get(string id) => RuntimeConfigs.Localisation.Get(id);

    public static string Format(string id, params object[] args)
    {
        var template = Get(id);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args ?? Array.Empty<object>());
        }
        catch (FormatException exception)
        {
            Debug.LogError($"[ActivityUiText] Invalid localisation format for '{id}': {exception.Message}");
            return template;
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
        ActivityProductionIconKind iconKind = ActivityProductionIconKind.Item,
        string name = "")
    {
        IconId = iconId ?? string.Empty;
        Value = value ?? string.Empty;
        IconKind = iconKind;
        Name = name ?? string.Empty;
    }

    public string IconId { get; }
    public string Value { get; }
    public ActivityProductionIconKind IconKind { get; }
    public string Name { get; }
}
