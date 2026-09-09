using System;
using System.Collections.Generic;
using GuildIdle.Localisation;
using GuildIdle.Player;
using GuildIdle.UI.Core;
using RuntimeConfigs = GuildIdle.Configs.Configs;

public sealed class StorageWindowController : IDisposable
{
    private readonly UIService _ui;
    private readonly IStorageService _storage;
    private StorageWindowView _view;
    private int _category;
    private bool _disposed;

    private StorageWindowController(UIService ui, IStorageService storage)
    {
        _ui = ui;
        _storage = storage;
    }

    public static StorageWindowController Open(UIService ui, IStorageService storage)
    {
        if (ui == null) throw new ArgumentNullException(nameof(ui));
        if (storage == null) throw new ArgumentNullException(nameof(storage));
        var controller = new StorageWindowController(ui, storage);
        try
        {
            controller._view = ui.OpenWindow<StorageWindowView, StorageWindowOpenArgs>(
                new StorageWindowOpenArgs(controller.Dispose, controller.HandleClosed, controller.SelectCategory));
            storage.Changed += controller.HandleStorageChanged;
            RuntimeConfigs.OnLoaded += controller.Refresh;
            LocalisationService.LanguageChanged += controller.HandleLanguageChanged;
            controller.Refresh();
            return controller;
        }
        catch { controller.Dispose(); throw; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        try { _ui.CloseWindow<StorageWindowView>(); }
        catch (ObjectDisposedException) { }
        finally { HandleClosed(); }
    }

    private void HandleClosed()
    {
        if (_disposed) return;
        _disposed = true;
        _storage.Changed -= HandleStorageChanged;
        RuntimeConfigs.OnLoaded -= Refresh;
        LocalisationService.LanguageChanged -= HandleLanguageChanged;
        _view = null;
    }

    private void SelectCategory(int category) { _category = category; Refresh(); }
    private void HandleStorageChanged(StorageSnapshot snapshot) => Refresh();
    private void HandleLanguageChanged(string language) => Refresh();

    private void Refresh()
    {
        if (_disposed || _view == null) return;
        var snapshot = _storage.GetSnapshot();
        var entries = new List<StorageSlotState>();
        foreach (var stack in snapshot.Stacks)
            Add(entries, stack.stackId, stack.itemId, stack.quantity, stack.stateId);
        foreach (var instance in snapshot.Instances)
            Add(entries, instance.instanceId, instance.itemId, 1, instance.stateId);
        _view.Render(new StorageWindowState(entries, snapshot.FreeSlots, snapshot.OccupiedSlots,
            snapshot.Capacity, _category, new[] { StorageUiText.Get("all", "Все"),
                StorageUiText.Get("resources", "Ресурсы"), StorageUiText.Get("items", "Предметы") }));
    }

    private void Add(List<StorageSlotState> entries, string id, string itemId, int quantity, string stateId)
    {
        var found = RuntimeConfigs.Items.TryGet(itemId, out var item);
        var resource = found && string.Equals(item.Kind, "resource", StringComparison.OrdinalIgnoreCase);
        if ((_category == 1 && !resource) || (_category == 2 && resource)) return;
        var name = found ? LootUiText.Localise(item.NameId, itemId) : itemId;
        if (RuntimeConfigs.Storage.TryGetItemState(stateId, out var state))
            name += " · " + LootUiText.Localise(state.storageItemStateNameId, stateId);
        entries.Add(new StorageSlotState(id, name, quantity,
            found ? ActivityCardProductionInfo.IconResolver.ResolveItem(item.IconId) : null));
    }
}

internal static class StorageUiText
{
    public static string Get(string key, string fallback) =>
        RuntimeConfigs.Localisation.TryGet("ui.storage." + key, out var text) ? text : fallback;
}
