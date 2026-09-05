using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GuildIdle.UI.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class HeroSelectionPopupView : UIWindow,
    IUIOpenArgsReceiver<HeroSelectionPopupOpenArgs>,
    IUIStateView<HeroSelectionPopupState>
{
    [SerializeField] private Button _backdropButton;
    [SerializeField] private Button _closeButton;
    [SerializeField] private TMP_Text _title;
    [SerializeField] private TMP_Text _context;
    [SerializeField] private TMP_Text _hint;
    [SerializeField] private RectTransform _dialog;
    [SerializeField] private ScrollRect _scroll;
    [SerializeField] private RectTransform _content;
    [SerializeField] private GameObject _heroTemplate;

    private readonly List<HeroRow> _rows = new List<HeroRow>();
    private HeroSelectionPopupState _state = HeroSelectionPopupState.Empty;
    private Action _closeRequested;
    private Action _closed;
    private Action<string> _heroSelected;

    public void ApplyOpenArgs(HeroSelectionPopupOpenArgs args)
    {
        if (args == null)
            throw new ArgumentNullException(nameof(args));

        _state = args.State ?? HeroSelectionPopupState.Empty;
        _closeRequested = args.CloseRequested;
        _closed = args.Closed;
        _heroSelected = args.HeroSelected;
    }

    public void Render(HeroSelectionPopupState state)
    {
        _state = state ?? HeroSelectionPopupState.Empty;
        SetText(_title, _state.Title);
        SetText(_context, _state.Context);
        SetText(_hint, ActivityUiText.Get(ActivityUiText.HeroSelectionHint));

        var heroes = (_state.Heroes ?? Array.Empty<HeroSelectionOptionState>())
            .OrderBy(value => value.CanSelect ? 0 : 1)
            .ThenByDescending(value => value.SkillLevel)
            .ThenBy(value => value.SortOrder)
            .ThenBy(value => value.Name, StringComparer.CurrentCulture)
            .ToArray();
        EnsureRowCapacity(heroes.Length);
        for (var index = 0; index < _rows.Count; index++)
        {
            var used = index < heroes.Length;
            _rows[index].SetActive(used);
            if (used)
                _rows[index].Render(heroes[index], _state.EnergyIcon, HandleHeroSelected);
        }

        if (_content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
    }

    protected override void OnBind()
    {
        BindButton(_backdropButton, HandleCloseRequested);
        BindButton(_closeButton, HandleCloseRequested);
    }

    protected override void OnShow()
    {
        ApplyAdaptiveLayout();
        Render(_state);
        if (_scroll != null)
            _scroll.verticalNormalizedPosition = 1f;
    }

    protected override void OnHide()
    {
        var closed = _closed;
        _closeRequested = null;
        _closed = null;
        _heroSelected = null;
        closed?.Invoke();
    }

    private void EnsureRowCapacity(int count)
    {
        if (_heroTemplate == null || _content == null)
        {
            if (count > 0)
                Debug.LogError($"[{nameof(HeroSelectionPopupView)}] Hero template and content must be assigned.", this);
            return;
        }

        while (_rows.Count < count)
        {
            var instance = Instantiate(_heroTemplate, _content, false);
            instance.name = "Hero";
            instance.SetActive(false);
            _rows.Add(new HeroRow(instance));
        }
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

    private void HandleCloseRequested() => _closeRequested?.Invoke();
    private void HandleHeroSelected(string heroId) => _heroSelected?.Invoke(heroId);

    private void OnRectTransformDimensionsChange()
    {
        if (IsShown)
            ApplyAdaptiveLayout();
    }

    private void ApplyAdaptiveLayout()
    {
        var root = transform as RectTransform;
        if (_dialog == null || root == null || root.rect.height <= 0f)
            return;
        var portrait = root.rect.width / root.rect.height < 1.15f;
        _dialog.anchorMin = portrait ? new Vector2(0.04f, 0.08f) : new Vector2(0.22f, 0.1f);
        _dialog.anchorMax = portrait ? new Vector2(0.96f, 0.92f) : new Vector2(0.78f, 0.9f);
        _dialog.offsetMin = Vector2.zero;
        _dialog.offsetMax = Vector2.zero;
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
            target.text = value ?? string.Empty;
    }

    private sealed class HeroRow
    {
        private static readonly Color AvailableColor = new Color32(47, 116, 67, 255);
        private static readonly Color BusyColor = new Color32(171, 102, 31, 255);
        private static readonly Color NoEnergyColor = new Color32(166, 58, 51, 255);
        private static readonly Color RowColor = new Color32(255, 249, 235, 255);
        private static readonly Color SelectedColor = new Color32(224, 241, 213, 255);
        private static readonly Color DisabledColor = new Color32(226, 219, 205, 255);

        private readonly GameObject _root;
        private readonly Button _button;
        private readonly Image _background;
        private readonly Image _avatar;
        private readonly TMP_Text _initial;
        private readonly TMP_Text _name;
        private readonly TMP_Text _status;
        private readonly Image _skillIcon;
        private readonly TMP_Text _skillLevel;
        private readonly Image _energyIcon;
        private readonly TMP_Text _energy;
        private readonly TMP_Text _selected;
        private string _heroId;
        private Action<string> _selectionRequested;

        public HeroRow(GameObject root)
        {
            _root = root;
            _button = root.GetComponent<Button>();
            _background = root.GetComponent<Image>();
            _avatar = Find<Image>(root.transform, "AvatarFrame/Avatar");
            _initial = Find<TMP_Text>(root.transform, "AvatarFrame/Initial");
            _name = Find<TMP_Text>(root.transform, "Name");
            _status = Find<TMP_Text>(root.transform, "Status");
            _skillIcon = Find<Image>(root.transform, "SkillIcon");
            _skillLevel = Find<TMP_Text>(root.transform, "SkillLevel");
            _energyIcon = Find<Image>(root.transform, "EnergyIcon");
            _energy = Find<TMP_Text>(root.transform, "Energy");
            _selected = Find<TMP_Text>(root.transform, "Selected");
        }

        public void SetActive(bool active) => _root.SetActive(active);

        public void Render(HeroSelectionOptionState state, Sprite energyIcon, Action<string> selectionRequested)
        {
            _heroId = state.HeroId;
            _selectionRequested = selectionRequested;
            SetText(_name, state.Name);
            SetText(_initial, Initial(state.Name));
            SetIcon(_avatar, state.Avatar);
            if (_initial != null)
                _initial.gameObject.SetActive(state.Avatar == null);

            SetText(_status, StatusText(state.Availability));
            if (_status != null)
                _status.color = StatusColor(state.Availability);
            SetIcon(_skillIcon, state.SkillIcon);
            SetText(_skillLevel, state.SkillIcon == null
                ? "—"
                : state.SkillLevel.ToString(CultureInfo.InvariantCulture));
            SetIcon(_energyIcon, energyIcon);
            SetText(_energy, $"{state.Energy}/{state.MaxEnergy}");
            if (_selected != null)
                _selected.gameObject.SetActive(state.IsSelected);
            if (_button != null)
            {
                _button.onClick.RemoveListener(HandleClick);
                _button.onClick.AddListener(HandleClick);
                _button.interactable = state.CanSelect;
            }
            if (_background != null)
                _background.color = state.IsSelected ? SelectedColor : state.CanSelect ? RowColor : DisabledColor;
        }

        private void HandleClick()
        {
            if (!string.IsNullOrWhiteSpace(_heroId))
                _selectionRequested?.Invoke(_heroId);
        }

        private static T Find<T>(Transform root, string path) where T : Component =>
            root.Find(path)?.GetComponent<T>();

        private static void SetIcon(Image target, Sprite sprite)
        {
            if (target == null)
                return;
            target.sprite = sprite;
            target.enabled = sprite != null;
        }

        private static string Initial(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Substring(0, 1).ToUpperInvariant();

        private static string StatusText(HeroSelectionAvailability availability)
        {
            switch (availability)
            {
                case HeroSelectionAvailability.Busy: return ActivityUiText.Get(ActivityUiText.HeroBusy);
                case HeroSelectionAvailability.NoEnergy: return ActivityUiText.Get(ActivityUiText.HeroNoEnergy);
                default: return ActivityUiText.Get(ActivityUiText.HeroAvailable);
            }
        }

        private static Color StatusColor(HeroSelectionAvailability availability)
        {
            switch (availability)
            {
                case HeroSelectionAvailability.Busy: return BusyColor;
                case HeroSelectionAvailability.NoEnergy: return NoEnergyColor;
                default: return AvailableColor;
            }
        }
    }
}

public sealed class HeroSelectionPopupOpenArgs : IUIOpenArgs
{
    public HeroSelectionPopupOpenArgs(
        HeroSelectionPopupState state,
        Action closeRequested,
        Action closed,
        Action<string> heroSelected)
    {
        State = state ?? HeroSelectionPopupState.Empty;
        CloseRequested = closeRequested;
        Closed = closed;
        HeroSelected = heroSelected;
    }

    public HeroSelectionPopupState State { get; }
    public Action CloseRequested { get; }
    public Action Closed { get; }
    public Action<string> HeroSelected { get; }
}

public sealed class HeroSelectionPopupState : IUIState
{
    public static readonly HeroSelectionPopupState Empty = new HeroSelectionPopupState(
        string.Empty, string.Empty, null, Array.Empty<HeroSelectionOptionState>());

    public HeroSelectionPopupState(
        string title,
        string context,
        Sprite energyIcon,
        IReadOnlyList<HeroSelectionOptionState> heroes)
    {
        Title = title ?? string.Empty;
        Context = context ?? string.Empty;
        EnergyIcon = energyIcon;
        Heroes = heroes ?? Array.Empty<HeroSelectionOptionState>();
    }

    public string Title { get; }
    public string Context { get; }
    public Sprite EnergyIcon { get; }
    public IReadOnlyList<HeroSelectionOptionState> Heroes { get; }
}

public enum HeroSelectionAvailability
{
    Available = 0,
    Busy = 1,
    NoEnergy = 2
}

public sealed class HeroSelectionOptionState
{
    public HeroSelectionOptionState(
        string heroId,
        string name,
        Sprite avatar,
        HeroSelectionAvailability availability,
        Sprite skillIcon,
        int skillLevel,
        int energy,
        int maxEnergy,
        bool isSelected,
        int sortOrder = 0)
    {
        HeroId = heroId ?? string.Empty;
        Name = name ?? string.Empty;
        Avatar = avatar;
        Availability = availability;
        SkillIcon = skillIcon;
        SkillLevel = Math.Max(0, skillLevel);
        Energy = Math.Max(0, energy);
        MaxEnergy = Math.Max(Energy, maxEnergy);
        IsSelected = isSelected;
        SortOrder = sortOrder;
    }

    public string HeroId { get; }
    public string Name { get; }
    public Sprite Avatar { get; }
    public HeroSelectionAvailability Availability { get; }
    public Sprite SkillIcon { get; }
    public int SkillLevel { get; }
    public int Energy { get; }
    public int MaxEnergy { get; }
    public bool IsSelected { get; }
    public int SortOrder { get; }
    public bool CanSelect => Availability == HeroSelectionAvailability.Available;
}
