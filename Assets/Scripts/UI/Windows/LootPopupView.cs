using System;
using System.Collections.Generic;
using System.Globalization;
using GuildIdle.UI.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LootPopupView : UIWindow, IUIOpenArgsReceiver<LootPopupOpenArgs>, IUIStateView<LootPopupState>
{
    [SerializeField] private TMP_Text _title;
    [SerializeField] private TMP_Text _context;
    [SerializeField] private TMP_Text _capacity;
    [SerializeField] private TMP_Text _notice;
    [SerializeField] private TMP_Text _itemsTitle;
    [SerializeField] private Button _close;
    [SerializeField] private Button _claimAll;
    [SerializeField] private Button _discardAll;
    [SerializeField] private Button _discardMode;
    [SerializeField] private ScrollRect _scroll;
    [SerializeField] private RectTransform _items;
    [SerializeField] private RectTransform _rewards;
    [SerializeField] private GameObject _itemTemplate;
    [SerializeField] private GameObject _rewardTemplate;
    [SerializeField] private GameObject _confirmation;
    [SerializeField] private TMP_Text _confirmationTitle;
    [SerializeField] private TMP_Text _confirmationText;
    [SerializeField] private Button _confirm;
    [SerializeField] private Button _cancel;

    private static readonly Color Ink = new Color32(57, 44, 28, 255);
    private static readonly Color Danger = new Color32(166, 58, 51, 255);
    private static readonly Color Paper = new Color32(255, 249, 235, 255);
    private static readonly Color DiscardPaper = new Color32(251, 220, 212, 255);
    private readonly List<EntryView> _itemViews = new List<EntryView>();
    private readonly List<EntryView> _rewardViews = new List<EntryView>();
    private LootPopupOpenArgs _args;
    private float _nextTick;

    public void ApplyOpenArgs(LootPopupOpenArgs args) => _args = args ?? throw new ArgumentNullException(nameof(args));

    protected override void OnBind()
    {
        Bind(_close, () => _args?.CloseRequested());
        Bind(_claimAll, () => _args?.ClaimAll());
        Bind(_discardAll, () => _args?.DiscardAll());
        Bind(_discardMode, () => _args?.ToggleDiscard());
        Bind(_confirm, () => _args?.ConfirmDiscard());
        Bind(_cancel, () => _args?.CancelDiscard());
    }

    protected override void OnShow()
    {
        _confirmation.SetActive(false);
        _scroll.verticalNormalizedPosition = 1f;
        _nextTick = Time.unscaledTime + 0.25f;
    }

    protected override void OnHide()
    {
        var closed = _args?.Closed;
        _args = null;
        closed?.Invoke();
    }

    private void Update()
    {
        if (!IsShown || Time.unscaledTime < _nextTick) return;
        _nextTick = Time.unscaledTime + 0.25f;
        _args?.RefreshRequested();
    }

    public void Render(LootPopupState state)
    {
        _title.text = LootUiText.Get("title", "Полученная добыча");
        _context.text = state.Context;
        _capacity.text = LootUiText.Get("storage", "Склад") + $"  {state.OccupiedSlots} / {state.Capacity}";
        _itemsTitle.text = LootUiText.Get("items", "Предметы");
        Label(_close, "close", "Закрыть");
        Label(_claimAll, "claim_all", "Забрать всё");
        Label(_discardAll, "discard_all", "Выбросить всё");
        Label(_discardMode, state.DiscardMode ? "discard_on" : "discard", state.DiscardMode ? "Выбросить · Вкл." : "Выбросить");
        _claimAll.interactable = state.CanClaimAll && !state.ConfirmDiscard;
        _discardAll.interactable = state.HasItems && !state.ConfirmDiscard;
        _discardMode.interactable = state.HasItems && !state.ConfirmDiscard;
        _close.interactable = !state.ConfirmDiscard;
        _discardMode.GetComponent<Image>().color = state.DiscardMode ? DiscardPaper : Paper;
        _notice.text = state.Error ?? (state.DiscardMode
            ? LootUiText.Get("discard_hint", "Режим выбрасывания: клик удаляет всю ячейку. Повторное нажатие «Выбросить» выключает режим.")
            : state.NoSpace ? LootUiText.Get("no_space", "Нет места")
            : state.HasItems ? LootUiText.Get("claim_hint", "Нажмите на предмет, чтобы забрать его на склад.")
            : state.CanClaimAll ? LootUiText.Get("retry", "Нажмите «Забрать всё», чтобы повторить получение награды.")
            : LootUiText.Get("resolved", "Награда получена. Окно можно закрыть."));
        _notice.color = state.DiscardMode || state.NoSpace || state.Error != null ? Danger : Ink;
        RenderEntries(_itemViews, state.Items, _itemTemplate, _items, false, state);
        RenderEntries(_rewardViews, state.Rewards, _rewardTemplate, _rewards, true, state);
        _rewards.gameObject.SetActive(state.Rewards.Count > 0);
        _itemsTitle.gameObject.SetActive(state.Items.Count > 0);
        var wasConfirming = _confirmation.activeSelf;
        _confirmation.SetActive(state.ConfirmDiscard);
        _confirmationTitle.text = LootUiText.Get("confirm_title", "Выбросить все предметы?");
        _confirmationText.text = LootUiText.Get("confirm_text",
            "Все оставшиеся предметы будут потеряны безвозвратно. Окно закроется. Полученные валюта и опыт сохранятся.");
        Label(_confirm, "confirm", "Да, выбросить всё");
        Label(_cancel, "cancel", "Отмена");
        if (state.ConfirmDiscard && !wasConfirming) _cancel.Select();
        if (!state.ConfirmDiscard && wasConfirming) _discardAll.Select();
    }

    private void RenderEntries(List<EntryView> views, IReadOnlyList<LootEntryState> states,
        GameObject template, RectTransform parent, bool reward, LootPopupState popup)
    {
        while (views.Count < states.Count)
        {
            var instance = Instantiate(template, parent, false);
            instance.name = reward ? "Reward" : "Item";
            views.Add(new EntryView(instance, id => _args?.EntryClicked(id)));
        }
        for (var i = 0; i < views.Count; i++)
        {
            views[i].Root.SetActive(i < states.Count);
            if (i < states.Count) views[i].Render(states[i], reward, popup.DiscardMode, popup.ConfirmDiscard);
        }
    }

    private void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        button.onClick.AddListener(action);
        RegisterCleanup(() => { if (button != null) button.onClick.RemoveListener(action); });
    }

    private static void Label(Button button, string key, string fallback) =>
        button.GetComponentInChildren<TMP_Text>(true).text = LootUiText.Get(key, fallback);

    private sealed class EntryView
    {
        public readonly GameObject Root;
        private readonly Button _button;
        private readonly Image _background;
        private readonly Image _icon;
        private readonly TMP_Text _name;
        private readonly TMP_Text _quantity;
        private readonly TMP_Text _status;
        private string _id;

        public EntryView(GameObject root, Action<string> clicked)
        {
            Root = root;
            _button = root.GetComponent<Button>();
            _background = _button.targetGraphic as Image;
            _icon = root.transform.Find("Icon").GetComponent<Image>();
            _name = root.transform.Find("Name").GetComponent<TMP_Text>();
            _quantity = root.transform.Find("Quantity").GetComponent<TMP_Text>();
            _status = root.transform.Find("Status").GetComponent<TMP_Text>();
            _button.onClick.AddListener(() => clicked(_id));
        }

        public void Render(LootEntryState state, bool reward, bool discardMode, bool confirming)
        {
            _id = state.Id;
            var pending = state.Status == LootEntryStatus.Pending;
            _button.interactable = !reward && pending && !confirming;
            _background.color = !reward && discardMode && pending ? DiscardPaper : Paper;
            _icon.sprite = state.Icon;
            _icon.enabled = state.Icon != null && (reward || pending);
            _name.text = state.Name;
            _name.gameObject.SetActive(!reward);
            _status.gameObject.SetActive(!reward);
            _quantity.text = (reward ? "+" : "×") + state.Quantity.ToString(CultureInfo.CurrentCulture);
            _quantity.gameObject.SetActive(reward || pending);
            _status.text = !pending ? state.Status == LootEntryStatus.Discarded
                ? LootUiText.Get("discarded", "Выброшено") : LootUiText.Get("claimed", "Получено")
                : reward ? LootUiText.Get("pending", "Ожидает получения")
                : discardMode ? LootUiText.Get("discard", "Выбросить")
                : state.Quality > 0 ? LootUiText.Get("quality", "Качество") + " " + state.Quality : string.Empty;
            _status.color = !reward && discardMode || state.Status == LootEntryStatus.Discarded ? Danger : Ink;
        }
    }
}

public sealed class LootPopupOpenArgs : IUIOpenArgs
{
    public LootPopupOpenArgs(Action closeRequested, Action closed, Action claimAll, Action discardAll,
        Action toggleDiscard, Action<string> entryClicked, Action confirmDiscard, Action cancelDiscard, Action refreshRequested)
    {
        CloseRequested = closeRequested; Closed = closed; ClaimAll = claimAll; DiscardAll = discardAll;
        ToggleDiscard = toggleDiscard; EntryClicked = entryClicked; ConfirmDiscard = confirmDiscard;
        CancelDiscard = cancelDiscard; RefreshRequested = refreshRequested;
    }
    public Action CloseRequested { get; }
    public Action Closed { get; }
    public Action ClaimAll { get; }
    public Action DiscardAll { get; }
    public Action ToggleDiscard { get; }
    public Action<string> EntryClicked { get; }
    public Action ConfirmDiscard { get; }
    public Action CancelDiscard { get; }
    public Action RefreshRequested { get; }
}

public enum LootEntryStatus { Pending, Claimed, Discarded }

public sealed class LootEntryState
{
    public LootEntryState(string id, string name, Sprite icon, long quantity, int quality, LootEntryStatus status)
    { Id = id; Name = name; Icon = icon; Quantity = quantity; Quality = quality; Status = status; }
    public string Id { get; }
    public string Name { get; }
    public Sprite Icon { get; }
    public long Quantity { get; }
    public int Quality { get; }
    public LootEntryStatus Status { get; }
}

public sealed class LootPopupState : IUIState
{
    public LootPopupState(string context, IReadOnlyList<LootEntryState> items, IReadOnlyList<LootEntryState> rewards,
        int occupiedSlots, int capacity, bool canClaimAll, bool hasItems, bool noSpace, bool discardMode,
        bool confirmDiscard, string error)
    {
        Context = context; Items = items; Rewards = rewards; OccupiedSlots = occupiedSlots; Capacity = capacity;
        CanClaimAll = canClaimAll; HasItems = hasItems; NoSpace = noSpace; DiscardMode = discardMode;
        ConfirmDiscard = confirmDiscard; Error = error;
    }
    public string Context { get; }
    public IReadOnlyList<LootEntryState> Items { get; }
    public IReadOnlyList<LootEntryState> Rewards { get; }
    public int OccupiedSlots { get; }
    public int Capacity { get; }
    public bool CanClaimAll { get; }
    public bool HasItems { get; }
    public bool NoSpace { get; }
    public bool DiscardMode { get; }
    public bool ConfirmDiscard { get; }
    public string Error { get; }
}
