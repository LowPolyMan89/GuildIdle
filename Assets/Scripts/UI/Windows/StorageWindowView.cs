using System;
using System.Collections.Generic;
using System.Globalization;
using GuildIdle.UI.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class StorageWindowView : UIWindow, IUIOpenArgsReceiver<StorageWindowOpenArgs>, IUIStateView<StorageWindowState>
{
    [SerializeField] private Button _close;
    [SerializeField] private Button[] _tabs;
    [SerializeField] private ScrollRect _scroll;
    [SerializeField] private GridLayoutGroup _grid;
    [SerializeField] private GameObject _slotTemplate;
    [SerializeField] private TMP_Text _capacity;
    [SerializeField] private TMP_Text _selection;
    private readonly List<GameObject> _slots = new List<GameObject>();
    private StorageWindowOpenArgs _args;
    private string _selectedId;
    private int _category = -1;

    public void ApplyOpenArgs(StorageWindowOpenArgs args) => _args = args ?? throw new ArgumentNullException(nameof(args));

    protected override void OnBind()
    {
        Bind(_close, () => _args?.Close());
        for (var i = 0; i < _tabs.Length; i++)
        {
            var category = i;
            Bind(_tabs[i], () => _args?.SelectCategory(category));
        }
    }

    private void Bind(Button button, UnityEngine.Events.UnityAction action)
    {
        button.onClick.AddListener(action);
        RegisterCleanup(() => button.onClick.RemoveListener(action));
    }

    protected override void OnHide()
    {
        var closed = _args?.Closed;
        _args = null;
        closed?.Invoke();
    }

    public void Render(StorageWindowState state)
    {
        var changedCategory = _category != state.Category;
        _category = state.Category;
        if (changedCategory) _selectedId = null;
        _selection.text = string.Empty;
        for (var i = 0; i < _tabs.Length; i++)
        {
            _tabs[i].GetComponentInChildren<TMP_Text>().text = state.Tabs[i];
            _tabs[i].GetComponent<Image>().color = i == state.Category
                ? new Color32(249, 239, 215, 255) : new Color32(225, 214, 190, 255);
            _tabs[i].interactable = i != state.Category;
        }
        _capacity.text = state.Occupied.ToString(CultureInfo.InvariantCulture) + " / " + state.Capacity.ToString(CultureInfo.InvariantCulture);
        var count = state.Entries.Count + state.FreeSlots;
        while (_slots.Count < count) _slots.Add(Instantiate(_slotTemplate, _grid.transform, false));
        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            slot.SetActive(i < count);
            var button = slot.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            if (i >= count) continue;
            var entry = i < state.Entries.Count ? state.Entries[i] : null;
            var icon = slot.transform.Find("Icon").GetComponent<Image>();
            icon.sprite = entry?.Icon;
            icon.enabled = icon.sprite != null;
            slot.transform.Find("Quantity").GetComponent<TMP_Text>().text = entry == null ? string.Empty : entry.Quantity.ToString(CultureInfo.InvariantCulture);
            button.interactable = entry != null;
            slot.GetComponent<Image>().color = entry == null ? new Color32(240, 231, 210, 255) : new Color32(255, 249, 235, 255);
            if (entry != null)
            {
                if (_selectedId == entry.Id) _selection.text = entry.Name;
                button.onClick.AddListener(() => { _selectedId = entry.Id; _selection.text = entry.Name; });
            }
        }
        if (changedCategory) _scroll.verticalNormalizedPosition = 1;
    }
}

public sealed class StorageWindowOpenArgs : IUIOpenArgs
{
    public readonly Action Close;
    public readonly Action Closed;
    public readonly Action<int> SelectCategory;
    public StorageWindowOpenArgs(Action close, Action closed, Action<int> selectCategory)
    { Close = close; Closed = closed; SelectCategory = selectCategory; }
}

public sealed class StorageSlotState
{
    public readonly string Id;
    public readonly string Name;
    public readonly int Quantity;
    public readonly Sprite Icon;
    public StorageSlotState(string id, string name, int quantity, Sprite icon)
    { Id = id; Name = name; Quantity = quantity; Icon = icon; }
}

public sealed class StorageWindowState : IUIState
{
    public readonly IReadOnlyList<StorageSlotState> Entries;
    public readonly int FreeSlots, Occupied, Capacity, Category;
    public readonly string[] Tabs;
    public StorageWindowState(IReadOnlyList<StorageSlotState> entries, int freeSlots, int occupied, int capacity, int category, string[] tabs)
    { Entries = entries; FreeSlots = freeSlots; Occupied = occupied; Capacity = capacity; Category = category; Tabs = tabs; }
}
