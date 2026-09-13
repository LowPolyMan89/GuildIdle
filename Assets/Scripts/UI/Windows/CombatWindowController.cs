using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GuildIdle.Activities;
using GuildIdle.Combat;
using GuildIdle.Configs;
using GuildIdle.Localisation;
using GuildIdle.Player;
using GuildIdle.UI.Core;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

public sealed class CombatWindowController : IDisposable
{
    private const int MaximumActions = 4;
    private const int MaximumEventsPerAdvance = 8;
    private static readonly Color DamageColor = new Color(1f, 0.35f, 0.24f);
    private static readonly Color HealColor = new Color(0.3f, 0.95f, 0.55f);
    private static readonly Color DodgeColor = new Color(0.55f, 0.85f, 1f);

    private readonly UIService _ui;
    private readonly string _executionId;
    private readonly Action _closed;
    private CombatWindowView _view;
    private LootPopupController _lootPopup;
    private bool _openingResult;
    private bool _closing;
    private bool _disposed;

    private CombatWindowController(UIService ui, string executionId, Action closed)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _executionId = string.IsNullOrWhiteSpace(executionId)
            ? throw new ArgumentException("Combat execution id is required.", nameof(executionId))
            : executionId;
        _closed = closed;
    }

    public static CombatWindowController Open(UIService ui, string executionId, Action closed = null)
    {
        if (ui.IsWindowOpen<CombatWindowView>())
            ui.CloseWindow<CombatWindowView>();
        var controller = new CombatWindowController(ui, executionId, closed);
        try
        {
            controller._view = ui.OpenWindow<CombatWindowView, CombatWindowOpenArgs>(
                new CombatWindowOpenArgs(controller.Dispose, controller.Retreat, controller.HandleViewClosed));
            OnlineActivityRuntime.CombatAdvanced += controller.HandleCombatAdvanced;
            OnlineActivityRuntime.Updated += controller.HandleRuntimeUpdated;
            RuntimeConfigs.OnLoaded += controller.Refresh;
            LocalisationService.LanguageChanged += controller.HandleLanguageChanged;
            controller.Refresh();
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
        if (_disposed || _closing)
            return;
        _closing = true;
        try
        {
            _lootPopup?.Dispose();
            if (_ui.IsWindowOpen<CombatWindowView>())
                _ui.CloseWindow<CombatWindowView>();
            else
                HandleViewClosed();
        }
        catch (ObjectDisposedException)
        {
            HandleViewClosed();
        }
    }

    private void HandleViewClosed()
    {
        if (_disposed)
            return;
        _disposed = true;
        OnlineActivityRuntime.CombatAdvanced -= HandleCombatAdvanced;
        OnlineActivityRuntime.Updated -= HandleRuntimeUpdated;
        RuntimeConfigs.OnLoaded -= Refresh;
        LocalisationService.LanguageChanged -= HandleLanguageChanged;
        _lootPopup?.Dispose();
        _lootPopup = null;
        _view = null;
        _closed?.Invoke();
    }

    private void HandleLanguageChanged(string language) => Refresh();
    private void HandleRuntimeUpdated(ActivityRuntimeSnapshot snapshot) => Refresh();

    private void HandleCombatAdvanced(OnlineCombatAdvanceResult result)
    {
        if (_disposed || result?.snapshot == null ||
            !string.Equals(result.snapshot.executionId, _executionId, StringComparison.Ordinal))
            return;
        _view?.Render(BuildState(result.snapshot));
        _view?.ShowEvents(BuildEvents(result.snapshot, result.events));
        TryOpenResult(result.snapshot);
    }

    private void Refresh()
    {
        if (_disposed || _view == null)
            return;
        var snapshot = OnlineActivityRuntime.GetCombatSnapshot(_executionId);
        _view.Render(BuildState(snapshot));
        TryOpenResult(snapshot);
    }

    private CombatWindowState BuildState(OnlineCombatSnapshot snapshot)
    {
        if (snapshot == null)
            return CombatWindowState.Empty;
        return new CombatWindowState(snapshot.enemyIndex, snapshot.enemyCount, BuildHero(snapshot),
            BuildEnemy(snapshot), BuildActions(snapshot), snapshot.lootQuantity,
            snapshot.combatExp, snapshot.outcome,
            snapshot.status == CombatExecutionStatus.Running,
            snapshot.status == CombatExecutionStatus.ResultPending && HasPendingResult(snapshot.pendingResultId));
    }

    private static CombatActorState BuildHero(OnlineCombatSnapshot snapshot)
    {
        RuntimeConfigs.Heroes.TryGet(snapshot.heroId, out var config);
        var actor = snapshot.hero;
        return new CombatActorState(Localise(config?.nameId, snapshot.heroId),
            ActivityCardProductionInfo.IconResolver.ResolveHero(
                config?.battleSpriteId ?? config?.fullSpriteId ?? config?.iconSpriteId),
            actor?.currentHp ?? 0, actor?.maxHp ?? 0, actor?.physicalResistancePercent ?? 0d,
            actor?.dodgeChancePercent ?? 0d, BuildConsumable(snapshot));
    }

    private static CombatActorState BuildEnemy(OnlineCombatSnapshot snapshot)
    {
        RuntimeConfigs.Enemies.TryGet(snapshot.enemy?.definitionId, out var config);
        var actor = snapshot.enemy;
        return new CombatActorState(Localise(config?.nameId, actor?.definitionId),
            ActivityCardProductionInfo.IconResolver.ResolveEnemy(config?.battleImageId ?? config?.iconId),
            actor?.currentHp ?? 0, actor?.maxHp ?? 0, actor?.physicalResistancePercent ?? 0d,
            actor?.dodgeChancePercent ?? 0d, null);
    }

    private static CombatConsumableState BuildConsumable(OnlineCombatSnapshot snapshot)
    {
        if (snapshot.consumableRemainingQuantity <= 0 ||
            !RuntimeConfigs.Items.TryGetConsumable(snapshot.consumableItemId, out var item))
            return null;
        return new CombatConsumableState(Localise(item.nameId, item.id),
            Localise(item.descriptionId, string.Empty),
            ActivityCardProductionInfo.IconResolver.ResolveItem(item.iconId),
            snapshot.consumableRemainingQuantity);
    }

    private static IReadOnlyList<CombatActionState> BuildActions(OnlineCombatSnapshot snapshot)
    {
        var actions = new List<CombatActionState>(MaximumActions);
        var actor = snapshot.hero;
        if (actor != null)
        {
            var interval = actor.attacksPerSecond > 0d ? 1d / actor.attacksPerSecond : 0d;
            var remaining = Math.Max(0d, actor.nextAttackAtSeconds - snapshot.combatTimeSeconds);
            actions.Add(new CombatActionState(CombatUiText.Get(CombatUiText.Attack),
                FormatRange(actor.damageMin, actor.damageMax),
                ResolveEquippedWeaponIcon(snapshot.heroId), interval > 0d ? (float)(remaining / interval) : 0f,
                (float)remaining));
        }

        if (!RuntimeConfigs.Heroes.TryGet(snapshot.heroId, out var hero) || hero?.uniqueSkillIds == null)
            return actions;
        foreach (var skillId in hero.uniqueSkillIds)
        {
            if (actions.Count >= MaximumActions)
                break;
            var skill = RuntimeConfigs.Heroes.HeroUniqueSkills.FirstOrDefault(value => value != null &&
                value.enabled && string.Equals(value.skillId, skillId, StringComparison.Ordinal));
            if (!IsCombatSkill(skill))
                continue;
            var effect = RuntimeConfigs.Heroes.HeroSkillEffects.FirstOrDefault(value => value != null &&
                string.Equals(value.skillId, skillId, StringComparison.Ordinal));
            var cooldown = Math.Max(0, effect?.cooldownSeconds ?? 0);
            var remaining = FindCooldown(actor, skillId, snapshot.combatTimeSeconds);
            actions.Add(new CombatActionState(Localise(skill.nameId, skill.skillId), FormatEffect(effect),
                ActivityCardProductionInfo.IconResolver.ResolveSkill(skill.iconId),
                cooldown > 0 ? (float)(remaining / cooldown) : 0f, (float)remaining));
        }
        return actions;
    }

    private static Sprite ResolveEquippedWeaponIcon(string heroId)
    {
        var player = GuildIdle.Player.Player.State;
        if (player != null)
        {
            var weapon = player.GetEquippedItem(heroId, "weapon");
            if (weapon != null && RuntimeConfigs.Items.TryGetEquipmentWeapon(weapon.itemId, out var config))
                return ActivityCardProductionInfo.IconResolver.ResolveItem(config.iconId);
        }
        return ActivityCardProductionInfo.IconResolver.ResolveItem("sword_icon");
    }

    private static double FindCooldown(OnlineCombatActorSnapshot actor, string abilityId, double combatTime)
    {
        var cooldown = actor?.abilityCooldowns?.FirstOrDefault(value => value != null &&
            string.Equals(value.abilityId, abilityId, StringComparison.Ordinal));
        return Math.Max(0d, (cooldown?.nextReadyAtSeconds ?? 0d) - combatTime);
    }

    private static IReadOnlyList<CombatEventPopupState> BuildEvents(
        OnlineCombatSnapshot snapshot, IReadOnlyList<CombatEvent> events)
    {
        var result = new List<CombatEventPopupState>(MaximumEventsPerAdvance);
        if (events == null)
            return result;
        for (var index = Math.Max(0, events.Count - MaximumEventsPerAdvance); index < events.Count; index++)
        {
            var combatEvent = events[index];
            CombatEventPopupState state = null;
            if (combatEvent is CombatDamageEvent damage)
                state = CreateEvent(snapshot, damage, "−" + damage.Damage.ToString(CultureInfo.InvariantCulture), DamageColor);
            else if (combatEvent is CombatDodgeEvent dodge)
                state = CreateEvent(snapshot, dodge, CombatUiText.Get(CombatUiText.DodgeEvent), DodgeColor);
            else if (combatEvent is CombatImmediateEffectEvent immediate &&
                     (immediate.EffectKind == CombatEffectKind.Heal || immediate.EffectKind == CombatEffectKind.Damage))
                state = CreateEvent(snapshot, immediate,
                    (immediate.EffectKind == CombatEffectKind.Heal ? "+" : "−") + immediate.Amount.ToString(CultureInfo.InvariantCulture),
                    immediate.EffectKind == CombatEffectKind.Heal ? HealColor : DamageColor,
                    ResolveEffectIcon(immediate.SourceKind, immediate.SourceEffectId));
            else if (combatEvent is CombatStatusTickEvent tick &&
                     (tick.EffectKind == CombatEffectKind.Heal || tick.EffectKind == CombatEffectKind.Damage))
                state = CreateEvent(snapshot, tick,
                    (tick.EffectKind == CombatEffectKind.Heal ? "+" : "−") + tick.Amount.ToString(CultureInfo.InvariantCulture),
                    tick.EffectKind == CombatEffectKind.Heal ? HealColor : DamageColor);
            if (state != null)
                result.Add(state);
        }
        return result;
    }

    private static CombatEventPopupState CreateEvent(OnlineCombatSnapshot snapshot, CombatEvent combatEvent,
        string text, Color color, Sprite icon = null)
    {
        var target = string.Equals(combatEvent.TargetCombatantId, snapshot.hero?.combatantId, StringComparison.Ordinal)
            ? CombatEventTarget.Hero
            : CombatEventTarget.Enemy;
        return new CombatEventPopupState(target, text,
            icon ?? ResolveAttackIcon(snapshot, combatEvent.ActorSide), color);
    }

    private static Sprite ResolveAttackIcon(OnlineCombatSnapshot snapshot, CombatActorSide side)
    {
        if (side == CombatActorSide.Hero)
            return ResolveEquippedWeaponIcon(snapshot.heroId);
        RuntimeConfigs.Enemies.TryGet(snapshot.enemy?.definitionId, out var enemy);
        return ActivityCardProductionInfo.IconResolver.ResolveCard(enemy?.iconId ?? "sword_icon");
    }

    private static Sprite ResolveEffectIcon(CombatEffectSourceKind kind, string sourceId)
    {
        if (kind == CombatEffectSourceKind.Consumable &&
            RuntimeConfigs.Items.TryGetConsumable(sourceId, out var item))
            return ActivityCardProductionInfo.IconResolver.ResolveItem(item.iconId);
        var skill = RuntimeConfigs.Heroes.HeroUniqueSkills.FirstOrDefault(value => value != null &&
            string.Equals(value.skillId, sourceId, StringComparison.Ordinal));
        return ActivityCardProductionInfo.IconResolver.ResolveSkill(skill?.iconId ?? sourceId);
    }

    private void Retreat()
    {
        if (_disposed || GuildIdle.Player.Player.State == null)
            return;
        var result = PlayerRuntimeComposition.CreateCombatOutcomeService().RequestRetreat(new CombatRetreatCommand
        {
            OperationId = $"combat-ui:retreat:{Guid.NewGuid():N}",
            ExecutionId = _executionId
        });
        if (!result.Success)
            Debug.LogWarning($"[CombatWindow] Retreat failed: {result.Code}: {result.Message}");
        Refresh();
    }

    private void TryOpenResult(OnlineCombatSnapshot snapshot)
    {
        if (_openingResult || _lootPopup != null || !HasPendingResult(snapshot?.pendingResultId))
            return;
        _openingResult = true;
        try
        {
            _lootPopup = LootPopupController.Open(_ui, GuildIdle.Player.Player.State.PendingResults,
                GuildIdle.Player.Player.State.Storage, snapshot.pendingResultId, LocaliseActivity(snapshot.activityId),
                HandleResultClosed);
        }
        finally
        {
            _openingResult = false;
        }
    }

    private void HandleResultClosed()
    {
        _lootPopup = null;
        if (!_disposed && !_closing)
            Dispose();
    }

    private static bool IsCombatSkill(HeroUniqueSkillConfigDto skill) => skill != null &&
        string.Equals(skill.type, "Combat", StringComparison.OrdinalIgnoreCase);

    private static bool HasPendingResult(string resultId) => GuildIdle.Player.Player.State != null &&
        !string.IsNullOrWhiteSpace(resultId) && GuildIdle.Player.Player.State.PendingResults.Get(resultId) != null;

    private static string LocaliseActivity(string activityId)
    {
        return RuntimeConfigs.Activities.TryGet(activityId, out var activity)
            ? Localise(activity.nameId, activityId)
            : activityId ?? string.Empty;
    }

    private static string Localise(string id, string fallback)
    {
        var value = RuntimeConfigs.Localisation.Get(id);
        return string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value;
    }

    private static string FormatRange(int minimum, int maximum)
    {
        minimum = Math.Max(0, minimum);
        maximum = Math.Max(minimum, maximum);
        return minimum == maximum ? minimum.ToString(CultureInfo.InvariantCulture) :
            $"{minimum.ToString(CultureInfo.InvariantCulture)} – {maximum.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FormatEffect(HeroSkillEffectConfigDto effect)
    {
        if (effect == null)
            return string.Empty;
        var value = effect.value.ToString("0.#", CultureInfo.InvariantCulture);
        if (string.Equals(effect.effect, "heal", StringComparison.OrdinalIgnoreCase))
            return CombatUiText.Format(CombatUiText.HealEffect, value);
        if (string.Equals(effect.effect, "damage", StringComparison.OrdinalIgnoreCase))
            return CombatUiText.Format(CombatUiText.DamageEffect, value);
        return value;
    }
}

internal static class CombatUiText
{
    public const string EnemyQueue = "ui.combat.enemy_queue";
    public const string EnemyQueueEmpty = "ui.combat.enemy_queue_empty";
    public const string Close = "ui.combat.close";
    public const string Retreat = "ui.combat.retreat";
    public const string Loot = "ui.combat.loot";
    public const string Armor = "ui.combat.armor";
    public const string Dodge = "ui.combat.dodge";
    public const string SecondsShort = "ui.combat.seconds_short";
    public const string Attack = "ui.combat.attack";
    public const string DodgeEvent = "ui.combat.event.dodge";
    public const string HealEffect = "ui.combat.effect.heal";
    public const string DamageEffect = "ui.combat.effect.damage";
    public const string StatusRunning = "ui.combat.status.running";
    public const string OutcomeVictory = "ui.combat.outcome.victory";
    public const string OutcomeDefeat = "ui.combat.outcome.defeat";
    public const string OutcomeRetreat = "ui.combat.outcome.retreat";
    public const string StatusResolved = "ui.combat.status.resolved";
    public const string StatusResultPending = "ui.combat.status.result_pending";

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
            Debug.LogError($"[CombatUiText] Invalid localisation format for '{id}': {exception.Message}");
            return template;
        }
    }
}
