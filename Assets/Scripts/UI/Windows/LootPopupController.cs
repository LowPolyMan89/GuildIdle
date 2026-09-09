using System;
using System.Collections.Generic;
using System.Linq;
using GuildIdle.Core;
using GuildIdle.Localisation;
using GuildIdle.Player;
using GuildIdle.UI.Core;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

/// <summary>One open PendingResult session; all mutations use the transactional runtime API.</summary>
public sealed class LootPopupController : IDisposable
{
    private readonly UIService _ui;
    private readonly IPendingResultService _results;
    private readonly IStorageService _storage;
    private readonly string _resultId;
    private readonly string _context;
    private readonly string _heroId;
    private readonly Action _closed;
    private readonly PendingResultEntrySaveData[] _initialEntries;
    private readonly HashSet<string> _discarded = new HashSet<string>(StringComparer.Ordinal);
    private LootPopupView _view;
    private bool _busy;
    private bool _disposed;
    private bool _discardMode;
    private bool _confirmDiscard;
    private string _error;
    private long _shownResultRevision = -1;
    private long _shownStorageRevision = -1;

    private LootPopupController(UIService ui, IPendingResultService results, IStorageService storage,
        PendingResultSaveData pending, string context, Action closed)
    {
        _ui = ui;
        _results = results;
        _storage = storage;
        _resultId = pending.resultId;
        _heroId = pending.ownerHeroId;
        _context = context ?? pending.sourceId;
        _closed = closed;
        // Snapshot lifetime is limited to this popup. No extra data is written to SaveData.
        _initialEntries = (pending.entries ?? Array.Empty<PendingResultEntrySaveData>())
            .Where(value => value != null && value.quantity > 0)
            .OrderBy(value => value.sortOrder).ThenBy(value => value.entryId, StringComparer.Ordinal).ToArray();
    }

    public static LootPopupController Open(UIService ui, IPendingResultService results, IStorageService storage,
        string resultId, string context = null, Action closed = null)
    {
        if (ui == null) throw new ArgumentNullException(nameof(ui));
        if (results == null) throw new ArgumentNullException(nameof(results));
        if (storage == null) throw new ArgumentNullException(nameof(storage));
        var pending = results.Get(resultId);
        if (pending == null) throw new InvalidOperationException("PendingResult does not exist: " + resultId);
        if (ui.IsWindowOpen<LootPopupView>()) ui.CloseWindow<LootPopupView>();
        var controller = new LootPopupController(ui, results, storage, pending, context, closed);
        try
        {
            controller._view = ui.OpenWindow<LootPopupView, LootPopupOpenArgs>(new LootPopupOpenArgs(
                controller.Dispose, controller.HandleClosed, controller.ClaimAll, controller.RequestDiscardAll,
                controller.ToggleDiscard, controller.ClickEntry, controller.ConfirmDiscard,
                controller.CancelDiscard, controller.Tick));
            LocalisationService.LanguageChanged += controller.HandleLanguageChanged;
            RuntimeConfigs.OnLoaded += controller.Render;
            controller.GrantHeaderRewards();
            controller.Render();
            return controller;
        }
        catch
        {
            controller.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        try { _ui.CloseWindow<LootPopupView>(); }
        catch (ObjectDisposedException) { }
        finally { HandleClosed(); }
    }

    private void HandleClosed()
    {
        if (_disposed) return;
        _disposed = true;
        LocalisationService.LanguageChanged -= HandleLanguageChanged;
        RuntimeConfigs.OnLoaded -= Render;
        _view = null;
        _closed?.Invoke();
    }

    private void HandleLanguageChanged(string language) => Render();

    private void Tick()
    {
        if (_busy || _disposed) return;
        var pending = _results.Get(_resultId);
        if ((pending?.revision ?? -1) != _shownResultRevision || _storage.GetSnapshot().Revision != _shownStorageRevision)
        {
            if (_storage.GetSnapshot().Revision != _shownStorageRevision && _error == "no_space")
                _error = null;
            // Never confirm a different revision of the bag than the one the player reviewed.
            _confirmDiscard = false;
            Render();
        }
    }

    private void GrantHeaderRewards()
    {
        _busy = true;
        try
        {
            foreach (var entry in _initialEntries.Where(value => !IsItem(value)))
            {
                var current = _results.Get(_resultId);
                var remaining = Find(current, entry.entryId);
                if (remaining == null) continue;
                var result = _results.ClaimQuantity(OperationId(), _resultId, remaining.entryId, remaining.quantity,
                    current.revision, _storage.GetSnapshot().Revision);
                if (!Accept(result)) break;
            }
        }
        finally { _busy = false; }
    }

    private void ClaimAll()
    {
        if (_busy || _disposed || _confirmDiscard) return;
        _error = null;
        GrantHeaderRewards();
        if (_error != null) { Render(); return; }
        _busy = true;
        _discardMode = false;
        try
        {
            foreach (var entry in _initialEntries.Where(IsItem))
            {
                var current = _results.Get(_resultId);
                var remaining = Find(current, entry.entryId);
                if (remaining == null) continue;
                var quantity = AcceptedQuantity(remaining);
                if (quantity <= 0) break;
                var requested = remaining.quantity;
                if (!Accept(_results.ClaimQuantity(OperationId(), _resultId, remaining.entryId, quantity,
                    current.revision, _storage.GetSnapshot().Revision))) break;
                // Keep the visible order, including a partially collected stack.
                if (quantity < requested) break;
            }
        }
        finally { _busy = false; }
        CompleteOrRender();
    }

    private void ClickEntry(string entryId)
    {
        if (_busy || _disposed || _confirmDiscard) return;
        var current = _results.Get(_resultId);
        var entry = Find(current, entryId);
        if (entry == null || !IsItem(entry)) return;
        _busy = true;
        _error = null;
        try
        {
            if (_discardMode)
            {
                if (Accept(_results.DiscardQuantity(OperationId(), _resultId, entryId, entry.quantity, current.revision)))
                    _discarded.Add(entryId);
            }
            else
            {
                var quantity = AcceptedQuantity(entry);
                if (quantity <= 0) _error = "no_space";
                else Accept(_results.ClaimQuantity(OperationId(), _resultId, entryId, quantity,
                    current.revision, _storage.GetSnapshot().Revision));
            }
        }
        finally { _busy = false; }
        CompleteOrRender();
    }

    private void ToggleDiscard()
    {
        if (_busy || _confirmDiscard) return;
        _discardMode = !_discardMode;
        _error = null;
        Render();
    }

    private void RequestDiscardAll()
    {
        if (_busy || _disposed) return;
        _confirmDiscard = true;
        Render();
    }

    private void CancelDiscard() { _confirmDiscard = false; Render(); }

    private void ConfirmDiscard()
    {
        if (_busy || _disposed || !_confirmDiscard) return;
        var current = _results.Get(_resultId);
        _confirmDiscard = false;
        if (current == null) { Dispose(); return; }
        if (current.revision != _shownResultRevision) { Render(); return; }
        // Failed automatic claims must never turn into silent destruction of currency or experience.
        _error = null;
        GrantHeaderRewards();
        if (_error != null) { Render(); return; }
        current = _results.Get(_resultId);
        if (current == null) { Dispose(); return; }
        _busy = true;
        try { Accept(_results.DiscardAll(OperationId(), _resultId, current.revision)); }
        finally { _busy = false; }
        CompleteOrRender();
    }

    private void CompleteOrRender()
    {
        if (_error == null && _results.Get(_resultId) == null) Dispose();
        else Render();
    }

    private bool Accept(PendingResultMutationResult result)
    {
        if (result.Success) return true;
        _error = "failed";
        Debug.LogWarning($"[LootPopup] {result.Code}: {result.Message}");
        return false;
    }

    private long AcceptedQuantity(PendingResultEntrySaveData entry) =>
        _storage.PreviewAdd(entry.targetId, (int)Math.Min(int.MaxValue, entry.quantity)).AcceptedQuantity;

    private void Render()
    {
        if (_disposed || _view == null) return;
        var pending = _results.Get(_resultId);
        var storage = _storage.GetSnapshot();
        _shownResultRevision = pending?.revision ?? -1;
        _shownStorageRevision = storage.Revision;
        var items = new List<LootEntryState>();
        var rewards = new List<LootEntryState>();
        foreach (var original in _initialEntries)
        {
            var entry = Find(pending, original.entryId);
            var item = IsItem(original);
            var status = entry != null ? LootEntryStatus.Pending : _discarded.Contains(original.entryId)
                ? LootEntryStatus.Discarded : LootEntryStatus.Claimed;
            var state = BuildEntry(original, entry?.quantity ?? original.quantity, status);
            (item ? items : rewards).Add(state);
        }
        var first = _initialEntries.Where(IsItem).Select(value => Find(pending, value.entryId)).FirstOrDefault(value => value != null);
        var noSpace = first != null && AcceptedQuantity(first) == 0;
        var hasHeader = (pending?.entries ?? Array.Empty<PendingResultEntrySaveData>()).Any(value => value != null && !IsItem(value));
        _view.Render(new LootPopupState(_context, items, rewards, storage.OccupiedSlots, storage.Capacity,
            (!noSpace && first != null) || hasHeader, first != null, noSpace, _discardMode, _confirmDiscard,
            _error == null ? null : _error == "no_space" ? LootUiText.Get("no_space", "Нет места")
                : LootUiText.Get("failed", "Не удалось сохранить изменение. Награда остаётся в мешке. Попробуйте ещё раз.")));
    }

    private LootEntryState BuildEntry(PendingResultEntrySaveData entry, long quantity, LootEntryStatus status)
    {
        var name = entry.targetId;
        Sprite icon = null;
        if (IsItem(entry) && RuntimeConfigs.Items.TryGet(entry.targetId, out var item))
        {
            name = LootUiText.Localise(item.NameId, name);
            icon = ActivityCardProductionInfo.IconResolver.ResolveItem(item.IconId);
        }
        else if (ActivityTypeParser.TryParseRewardType(entry.rewardType, out var type) && type == RewardTypeEnum.SkillExp)
        {
            var skill = RuntimeConfigs.Activities.Skills.FirstOrDefault(value => value != null && value.skillId == entry.targetId);
            if (skill != null)
            {
                name = LootUiText.Localise(skill.skillNameId, name);
                icon = ActivityCardProductionInfo.IconResolver.ResolveSkill(skill.skillIconId);
            }
            if (!string.IsNullOrEmpty(_heroId) && RuntimeConfigs.Heroes.TryGet(_heroId, out var hero))
                name = LootUiText.Localise(hero.nameId, _heroId) + " · " + name;
            name += " · " + LootUiText.Get("experience", "опыт");
        }
        else if (RuntimeConfigs.Items.TryGetCurrency(entry.targetId, out var currency))
        {
            name = LootUiText.Localise(currency.nameId, name);
            icon = ActivityCardProductionInfo.IconResolver.ResolveItem(currency.iconId);
        }
        return new LootEntryState(entry.entryId, name, icon, quantity, entry.quality, status);
    }

    private static PendingResultEntrySaveData Find(PendingResultSaveData result, string id) =>
        result?.entries?.FirstOrDefault(value => value != null && value.quantity > 0 && value.entryId == id);

    private static bool IsItem(PendingResultEntrySaveData entry)
    {
        if (!ActivityTypeParser.TryParseRewardType(entry.rewardType, out var type)) return false;
        return type == RewardTypeEnum.Resource || type == RewardTypeEnum.Item || type == RewardTypeEnum.Equipment ||
               type == RewardTypeEnum.Consumable || type == RewardTypeEnum.Recipe;
    }

    private static string OperationId() => "loot-popup:" + Guid.NewGuid().ToString("N");
}

internal static class LootUiText
{
    public static string Get(string key, string fallback) => Localise("ui.loot." + key, fallback);
    public static string Localise(string id, string fallback) =>
        RuntimeConfigs.Localisation.TryGet(id, out var text) ? text : fallback ?? string.Empty;
}
