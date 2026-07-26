using System;
using System.Collections.Generic;
using GuildIdle.Configs;

namespace GuildIdle.Combat
{
    public static class CombatModifierOperations
    {
        public const string Add = "add";
    }

    public sealed class CombatStatusDescriptor
    {
        public CombatStatusDescriptor(
            string statusId,
            double durationSeconds,
            double tickIntervalSeconds,
            int maxStacks,
            CombatEffectDescriptor periodicEffect = null,
            CombatEffectDescriptor modifierEffect = null,
            bool refreshDurationOnReapply = true)
        {
            StatusId = statusId;
            DurationSeconds = durationSeconds;
            TickIntervalSeconds = tickIntervalSeconds;
            MaxStacks = maxStacks;
            PeriodicEffect = periodicEffect;
            ModifierEffect = modifierEffect;
            RefreshDurationOnReapply = refreshDurationOnReapply;
        }

        public string StatusId { get; }
        public double DurationSeconds { get; }
        public double TickIntervalSeconds { get; }
        public int MaxStacks { get; }
        public CombatEffectDescriptor PeriodicEffect { get; }
        public CombatEffectDescriptor ModifierEffect { get; }
        public bool RefreshDurationOnReapply { get; }
    }

    public interface ICombatStatusDescriptorProvider
    {
        bool TryGetStatus(
            string statusId,
            out CombatStatusDescriptor descriptor,
            out string error);
    }

    public sealed class EmptyCombatStatusDescriptorProvider : ICombatStatusDescriptorProvider
    {
        public static readonly EmptyCombatStatusDescriptorProvider Instance =
            new EmptyCombatStatusDescriptorProvider();

        private EmptyCombatStatusDescriptorProvider()
        {
        }

        public bool TryGetStatus(
            string statusId,
            out CombatStatusDescriptor descriptor,
            out string error)
        {
            descriptor = null;
            error = $"Combat status '{statusId ?? "<null>"}' was not found.";
            return false;
        }
    }

    public sealed class ConfigCombatStatusDescriptorProvider : ICombatStatusDescriptorProvider
    {
        private readonly EnemiesConfigRepository _configs;

        public ConfigCombatStatusDescriptorProvider(EnemiesConfigRepository configs)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public bool TryGetStatus(
            string statusId,
            out CombatStatusDescriptor descriptor,
            out string error)
        {
            descriptor = null;
            error = null;
            if (!_configs.TryGetCombatStatus(statusId, out var source))
            {
                error = $"Combat status '{statusId ?? "<null>"}' was not found.";
                return false;
            }

            CombatEffectDescriptor periodicEffect = null;
            CombatEffectDescriptor modifierEffect = null;
            if (string.Equals(source.effectType, "DamageOverTime", StringComparison.Ordinal))
            {
                periodicEffect = new CombatEffectDescriptor(
                    CombatEffectKind.Damage,
                    value: source.damageValue,
                    damageType: source.damageType);
            }
            else if (string.Equals(source.effectType, "HealOverTime", StringComparison.Ordinal))
            {
                periodicEffect = new CombatEffectDescriptor(
                    CombatEffectKind.Heal,
                    value: source.damageValue);
            }
            else if (string.Equals(source.effectType, "ModifyStat", StringComparison.Ordinal))
            {
                modifierEffect = new CombatEffectDescriptor(
                    CombatEffectKind.ModifyStat,
                    statId: source.statId,
                    value: source.statModifierValue,
                    operation: CombatModifierOperations.Add);
            }
            else
            {
                error = $"Combat status '{statusId}' has unsupported effect type '{source.effectType ?? "<null>"}'.";
                return false;
            }

            descriptor = new CombatStatusDescriptor(
                source.statusId,
                source.durationSec,
                source.tickIntervalSec,
                source.maxStacks,
                periodicEffect,
                modifierEffect);
            return true;
        }
    }

    public sealed class CombatStatusAppliedEvent : CombatEvent
    {
        public CombatStatusAppliedEvent(
            CombatEffectRequest request,
            string statusInstanceId,
            string statusId,
            int stackCount,
            bool stackAdded,
            bool durationRefreshed)
            : base(
                request.EventKey,
                request.TimestampSeconds,
                request.Sequence,
                request.ActorSide,
                request.SourceOwnerCombatantId,
                request.TargetCombatantId)
        {
            StatusInstanceId = statusInstanceId;
            StatusId = statusId;
            StackCount = stackCount;
            StackAdded = stackAdded;
            DurationRefreshed = durationRefreshed;
        }

        public string StatusInstanceId { get; }
        public string StatusId { get; }
        public int StackCount { get; }
        public bool StackAdded { get; }
        public bool DurationRefreshed { get; }
    }

    public sealed class CombatStatusTickEvent : CombatEvent
    {
        public CombatStatusTickEvent(
            CombatScheduledEventSaveData scheduledEvent,
            string sourceCombatantId,
            string targetCombatantId,
            string statusInstanceId,
            string statusId,
            int stackCount,
            CombatEffectKind effectKind,
            string damageType,
            int amount,
            int hpBefore,
            long rawHpAfter,
            int hpAfter)
            : base(
                scheduledEvent.eventKey,
                scheduledEvent.timestampSeconds,
                scheduledEvent.sequence,
                scheduledEvent.actorSide,
                sourceCombatantId,
                targetCombatantId)
        {
            StatusInstanceId = statusInstanceId;
            StatusId = statusId;
            StackCount = stackCount;
            EffectKind = effectKind;
            DamageType = damageType;
            Amount = amount;
            HpBefore = hpBefore;
            RawHpAfter = rawHpAfter;
            HpAfter = hpAfter;
        }

        public string StatusInstanceId { get; }
        public string StatusId { get; }
        public int StackCount { get; }
        public CombatEffectKind EffectKind { get; }
        public string DamageType { get; }
        public int Amount { get; }
        public int HpBefore { get; }
        public long RawHpAfter { get; }
        public int HpAfter { get; private set; }

        internal void SetHpAfter(int value)
        {
            HpAfter = value;
        }
    }

    public sealed class CombatImmediateEffectEvent : CombatEvent
    {
        public CombatImmediateEffectEvent(
            CombatEffectRequest request,
            CombatEffectKind effectKind,
            int amount,
            int hpBefore,
            long rawHpAfter,
            int hpAfter)
            : base(
                request.EventKey,
                request.TimestampSeconds,
                request.Sequence,
                request.ActorSide,
                request.SourceOwnerCombatantId,
                request.TargetCombatantId)
        {
            SourceEffectId = request.SourceDescriptorId;
            SourceKind = request.SourceKind;
            EffectKind = effectKind;
            Amount = amount;
            HpBefore = hpBefore;
            RawHpAfter = rawHpAfter;
            HpAfter = hpAfter;
        }

        public string SourceEffectId { get; }
        public CombatEffectSourceKind SourceKind { get; }
        public CombatEffectKind EffectKind { get; }
        public int Amount { get; }
        public int HpBefore { get; }
        public long RawHpAfter { get; }
        public int HpAfter { get; private set; }

        internal void SetHpAfter(int value)
        {
            HpAfter = value;
        }
    }

    public sealed class CombatStatusExpiredEvent : CombatEvent
    {
        public CombatStatusExpiredEvent(
            CombatScheduledEventSaveData scheduledEvent,
            string sourceCombatantId,
            string targetCombatantId,
            string statusInstanceId,
            string statusId,
            int stackCount)
            : base(
                scheduledEvent.eventKey,
                scheduledEvent.timestampSeconds,
                scheduledEvent.sequence,
                scheduledEvent.actorSide,
                sourceCombatantId,
                targetCombatantId)
        {
            StatusInstanceId = statusInstanceId;
            StatusId = statusId;
            StackCount = stackCount;
        }

        public string StatusInstanceId { get; }
        public string StatusId { get; }
        public int StackCount { get; }
    }

    public sealed class CombatTemporaryModifierAppliedEvent : CombatEvent
    {
        public CombatTemporaryModifierAppliedEvent(
            CombatEffectRequest request,
            string modifierInstanceId,
            string statId,
            double value,
            double expiresAtSeconds)
            : base(
                request.EventKey,
                request.TimestampSeconds,
                request.Sequence,
                request.ActorSide,
                request.SourceOwnerCombatantId,
                request.TargetCombatantId)
        {
            ModifierInstanceId = modifierInstanceId;
            StatId = statId;
            Value = value;
            ExpiresAtSeconds = expiresAtSeconds;
        }

        public string ModifierInstanceId { get; }
        public string StatId { get; }
        public double Value { get; }
        public double ExpiresAtSeconds { get; }
    }

    public sealed class CombatTemporaryModifierExpiredEvent : CombatEvent
    {
        public CombatTemporaryModifierExpiredEvent(
            CombatScheduledEventSaveData scheduledEvent,
            string sourceCombatantId,
            string targetCombatantId,
            string modifierInstanceId,
            string statId,
            double value)
            : base(
                scheduledEvent.eventKey,
                scheduledEvent.timestampSeconds,
                scheduledEvent.sequence,
                scheduledEvent.actorSide,
                sourceCombatantId,
                targetCombatantId)
        {
            ModifierInstanceId = modifierInstanceId;
            StatId = statId;
            Value = value;
        }

        public string ModifierInstanceId { get; }
        public string StatId { get; }
        public double Value { get; }
    }

    public delegate bool TryExecuteCombatEffect(
        CombatSessionSaveData session,
        CombatEffectRequest request,
        List<CombatEvent> events,
        out bool stateChanged,
        out CombatHpMutation mutation,
        out CombatAdvanceError error);

    public sealed class CombatEffectExecutorRegistry
    {
        private readonly Dictionary<CombatEffectKind, TryExecuteCombatEffect> _handlers =
            new Dictionary<CombatEffectKind, TryExecuteCombatEffect>();

        public void Register(
            CombatEffectKind kind,
            TryExecuteCombatEffect handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            _handlers[kind] = handler;
        }

        internal IEnumerable<KeyValuePair<CombatEffectKind, TryExecuteCombatEffect>>
            Handlers => _handlers;

        internal bool TryGet(
            CombatEffectKind kind,
            out TryExecuteCombatEffect handler)
        {
            return _handlers.TryGetValue(kind, out handler);
        }
    }

    internal sealed class CombatStatusRuntime
    {
        public const string StatusTickEventType = "status_tick";
        public const string StatusExpirationEventType = "status_expiration";
        public const string ModifierExpirationEventType = "modifier_expiration";

        private readonly ICombatStatusDescriptorProvider _provider;
        private readonly CombatEffectExecutorRegistry _effects;

        public CombatStatusRuntime(
            ICombatStatusDescriptorProvider provider,
            CombatEffectExecutorRegistry effects = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _effects = new CombatEffectExecutorRegistry();
            _effects.Register(CombatEffectKind.ApplyStatus, TryExecuteApplyStatus);
            _effects.Register(CombatEffectKind.ModifyStat, TryExecuteModifyStat);
            _effects.Register(CombatEffectKind.Damage, TryExecuteImmediate);
            _effects.Register(CombatEffectKind.Heal, TryExecuteImmediate);
            if (effects == null)
                return;
            foreach (var value in effects.Handlers)
                _effects.Register(value.Key, value.Value);
        }

        public bool IsEffectSupported(CombatEffectKind kind)
        {
            return _effects.TryGet(kind, out _);
        }

        public bool IsScheduledEvent(CombatScheduledEventSaveData scheduledEvent)
        {
            return scheduledEvent != null &&
                   (string.Equals(scheduledEvent.eventType, StatusTickEventType, StringComparison.Ordinal) ||
                    string.Equals(scheduledEvent.eventType, StatusExpirationEventType, StringComparison.Ordinal) ||
                    string.Equals(scheduledEvent.eventType, ModifierExpirationEventType, StringComparison.Ordinal));
        }

        public bool TryApplyEffectRequest(
            CombatSessionSaveData session,
            CombatEffectRequest request,
            List<CombatEvent> events,
            out bool stateChanged,
            out CombatHpMutation mutation,
            out CombatAdvanceError error)
        {
            stateChanged = false;
            mutation = null;
            error = null;
            if (session?.scheduler == null || request?.Effect == null || events == null)
            {
                return Fail(
                    CombatAdvanceErrorCode.EffectProcessingFailed,
                    "Combat effect processing requires a session, scheduler, request and event sink.",
                    out error);
            }

            var target = FindCombatant(session, request.TargetCombatantId);
            if (target == null)
            {
                return Fail(
                    CombatAdvanceErrorCode.EffectProcessingFailed,
                    $"Combat effect target '{request.TargetCombatantId ?? "<null>"}' was not found.",
                    out error);
            }
            if (target.currentHp <= 0)
                return true;

            if (!_effects.TryGet(request.Effect.Kind, out var handler))
            {
                return Fail(
                    CombatAdvanceErrorCode.EffectProcessingFailed,
                    $"Combat effect kind '{request.Effect.Kind}' is not registered.",
                    out error);
            }

            try
            {
                return handler(
                    session,
                    request,
                    events,
                    out stateChanged,
                    out mutation,
                    out error);
            }
            catch (Exception exception)
            {
                stateChanged = false;
                mutation = null;
                return Fail(
                    CombatAdvanceErrorCode.EffectProcessingFailed,
                    $"Combat effect handler failed: {exception.Message}",
                    out error);
            }
        }

        private bool TryExecuteApplyStatus(
            CombatSessionSaveData session,
            CombatEffectRequest request,
            List<CombatEvent> events,
            out bool stateChanged,
            out CombatHpMutation mutation,
            out CombatAdvanceError error)
        {
            mutation = null;
            var target = FindCombatant(session, request.TargetCombatantId);
            return TryApplyStatus(
                session,
                target,
                request,
                events,
                out stateChanged,
                out error);
        }

        private bool TryExecuteModifyStat(
            CombatSessionSaveData session,
            CombatEffectRequest request,
            List<CombatEvent> events,
            out bool stateChanged,
            out CombatHpMutation mutation,
            out CombatAdvanceError error)
        {
            mutation = null;
            var target = FindCombatant(session, request.TargetCombatantId);
            return TryApplyIndependentModifier(
                session,
                target,
                request,
                events,
                out stateChanged,
                out error);
        }

        private static bool TryExecuteImmediate(
            CombatSessionSaveData session,
            CombatEffectRequest request,
            List<CombatEvent> events,
            out bool stateChanged,
            out CombatHpMutation mutation,
            out CombatAdvanceError error)
        {
            stateChanged = false;
            mutation = null;
            var target = FindCombatant(session, request.TargetCombatantId);
            if (!TryApplyImmediateEffect(
                    target,
                    request.Effect,
                    1,
                    out var amount,
                    out var hpBefore,
                    out var rawHpAfter,
                    out var hpAfter,
                    out var hpMutated,
                    out error))
            {
                return false;
            }

            var immediateEvent = new CombatImmediateEffectEvent(
                request,
                request.Effect.Kind,
                amount,
                hpBefore,
                rawHpAfter,
                hpAfter);
            events.Add(immediateEvent);
            var targetSide = string.Equals(
                session.hero?.combatantId,
                target.combatantId,
                StringComparison.Ordinal)
                ? CombatActorSide.Hero
                : CombatActorSide.Enemy;
            mutation = new CombatHpMutation(
                request.EventKey,
                request.SourceDescriptorId,
                request.TimestampSeconds,
                request.Sequence,
                request.SourceOwnerSide,
                request.SourceOwnerCombatantId,
                targetSide,
                target.combatantId,
                hpBefore,
                rawHpAfter,
                hpAfter,
                immediateEvent.SetHpAfter);
            stateChanged = hpMutated;
            return true;
        }

        public bool TryResolveScheduledEvent(
            CombatSessionSaveData session,
            CombatScheduledEventSaveData scheduledEvent,
            List<CombatEvent> events,
            out bool stateChanged,
            out CombatHpMutation mutation,
            out CombatAdvanceError error)
        {
            stateChanged = false;
            mutation = null;
            error = null;
            if (session?.scheduler == null || scheduledEvent == null || events == null)
            {
                return Fail(
                    CombatAdvanceErrorCode.StatusProcessingFailed,
                    "Status event processing requires a session, scheduler, event and event sink.",
                    out error);
            }

            var target = FindCombatant(session, scheduledEvent.subjectCombatantId);
            if (target == null)
            {
                return Fail(
                    CombatAdvanceErrorCode.StatusProcessingFailed,
                    $"Status event target '{scheduledEvent.subjectCombatantId ?? "<null>"}' was not found.",
                    out error);
            }
            if (target.currentHp <= 0)
                return true;

            if (string.Equals(scheduledEvent.eventType, StatusTickEventType, StringComparison.Ordinal))
            {
                return TryResolveTick(
                    session,
                    target,
                    scheduledEvent,
                    events,
                    out stateChanged,
                    out mutation,
                    out error);
            }

            if (string.Equals(scheduledEvent.eventType, StatusExpirationEventType, StringComparison.Ordinal))
            {
                return TryResolveStatusExpiration(
                    session,
                    target,
                    scheduledEvent,
                    events,
                    out stateChanged,
                    out error);
            }

            if (string.Equals(scheduledEvent.eventType, ModifierExpirationEventType, StringComparison.Ordinal))
            {
                return TryResolveModifierExpiration(
                    target,
                    scheduledEvent,
                    events,
                    out stateChanged,
                    out error);
            }

            return Fail(
                CombatAdvanceErrorCode.UnsupportedScheduledEvent,
                $"Scheduled status event type '{scheduledEvent.eventType ?? "<null>"}' is not supported.",
                out error);
        }

        public bool TryGetStatModifier(
            CombatantStateSaveData combatant,
            string statId,
            double timestampSeconds,
            out double value,
            out CombatAdvanceError error)
        {
            value = 0d;
            error = null;
            if (combatant == null || InvalidTime(timestampSeconds))
            {
                return Fail(
                    CombatAdvanceErrorCode.StatusProcessingFailed,
                    "Modifier resolution requires a combatant and valid combat time.",
                    out error);
            }

            if (string.IsNullOrWhiteSpace(statId))
                return true;

            foreach (var modifier in combatant.independentModifiers ??
                                     Array.Empty<CombatTemporaryModifierSaveData>())
            {
                if (modifier == null ||
                    modifier.expiresAtSeconds <= timestampSeconds ||
                    !string.Equals(modifier.statId, statId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(
                        modifier.operation,
                        CombatModifierOperations.Add,
                        StringComparison.OrdinalIgnoreCase) ||
                    float.IsNaN(modifier.value) ||
                    float.IsInfinity(modifier.value))
                {
                    return Fail(
                        CombatAdvanceErrorCode.StatusProcessingFailed,
                        $"Temporary modifier '{modifier.modifierInstanceId ?? "<null>"}' is invalid.",
                        out error);
                }

                value += modifier.value;
            }

            foreach (var status in combatant.statuses ?? Array.Empty<CombatStatusInstanceSaveData>())
            {
                if (status == null || status.expiresAtSeconds <= timestampSeconds)
                    continue;
                if (!TryResolveDescriptor(status.statusId, out var descriptor, out error))
                    return false;
                if (!ValidateSavedStatus(status, descriptor, out error))
                    return false;

                var modifier = descriptor.ModifierEffect;
                if (modifier == null ||
                    !string.Equals(modifier.StatId, statId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(
                        modifier.Operation ?? CombatModifierOperations.Add,
                        CombatModifierOperations.Add,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Fail(
                        CombatAdvanceErrorCode.InvalidStatusDescriptor,
                        $"Combat status '{descriptor.StatusId}' uses unsupported modifier operation.",
                        out error);
                }

                value += modifier.Value * status.stackIds.Length;
            }

            if (InvalidNumber(value))
            {
                return Fail(
                    CombatAdvanceErrorCode.StatusProcessingFailed,
                    $"Resolved modifier '{statId}' is not finite.",
                    out error);
            }

            return true;
        }

        public void RemoveScheduledEventsForCombatant(
            CombatSchedulerStateSaveData scheduler,
            string combatantId)
        {
            if (scheduler == null || string.IsNullOrWhiteSpace(combatantId))
                return;
            RemoveScheduledEvents(
                scheduler,
                value => string.Equals(
                    value?.subjectCombatantId,
                    combatantId,
                    StringComparison.Ordinal));
        }

        private bool TryApplyStatus(
            CombatSessionSaveData session,
            CombatantStateSaveData target,
            CombatEffectRequest request,
            List<CombatEvent> events,
            out bool stateChanged,
            out CombatAdvanceError error)
        {
            stateChanged = false;
            error = null;
            if (!TryResolveDescriptor(request.Effect.StatusId, out var descriptor, out error))
                return false;

            var values = target.statuses ?? Array.Empty<CombatStatusInstanceSaveData>();
            var status = FindStatus(values, descriptor.StatusId, request.SourceOwnerCombatantId);
            if (string.Equals(status?.lastApplyEventKey, request.EventKey, StringComparison.Ordinal))
                return true;

            var expiresAtSeconds = request.TimestampSeconds + descriptor.DurationSeconds;
            if (InvalidTime(expiresAtSeconds) || expiresAtSeconds <= request.TimestampSeconds)
            {
                return Fail(
                    CombatAdvanceErrorCode.InvalidStatusDescriptor,
                    $"Combat status '{descriptor.StatusId}' duration does not advance combat time.",
                    out error);
            }

            var stackAdded = false;
            var durationRefreshed = false;
            if (status == null)
            {
                if (values.Length >= CombatRuntimeSaveDataUtility.PersistentCollectionLimit)
                {
                    return Fail(
                        CombatAdvanceErrorCode.StatusProcessingFailed,
                        "Combat status state reached its bounded retention limit.",
                        out error);
                }

                var statusInstanceId =
                    $"{target.combatantId}:status:{request.Sequence}";
                status = new CombatStatusInstanceSaveData
                {
                    statusInstanceId = statusInstanceId,
                    statusId = descriptor.StatusId,
                    sourceCombatantId = request.SourceOwnerCombatantId,
                    stackIds = new[] { $"{statusInstanceId}:stack:{request.Sequence}" },
                    expiresAtSeconds = expiresAtSeconds,
                    nextTickAtSeconds = 0d,
                    lastApplyEventKey = request.EventKey
                };
                stackAdded = true;

                var expanded = new CombatStatusInstanceSaveData[values.Length + 1];
                Array.Copy(values, expanded, values.Length);
                expanded[expanded.Length - 1] = status;
                Array.Sort(expanded, CompareStatus);
                target.statuses = expanded;
            }
            else
            {
                if (!ValidateSavedStatus(status, descriptor, out error))
                    return false;

                var stackIds = status.stackIds;
                if (stackIds.Length < descriptor.MaxStacks)
                {
                    var expanded = new string[stackIds.Length + 1];
                    Array.Copy(stackIds, expanded, stackIds.Length);
                    expanded[expanded.Length - 1] =
                        $"{status.statusInstanceId}:stack:{request.Sequence}";
                    status.stackIds = expanded;
                    stackAdded = true;
                }

                if (descriptor.RefreshDurationOnReapply)
                {
                    durationRefreshed = status.expiresAtSeconds != expiresAtSeconds;
                    status.expiresAtSeconds = expiresAtSeconds;
                }

                status.lastApplyEventKey = request.EventKey;
                RemoveScheduledEvents(
                    session.scheduler,
                    value => string.Equals(
                                 value?.eventType,
                                 StatusExpirationEventType,
                                 StringComparison.Ordinal) &&
                             string.Equals(
                                 value.effectInstanceId,
                                 status.statusInstanceId,
                                 StringComparison.Ordinal));
            }

            if (!TryEnsureStatusSchedule(
                    session,
                    target,
                    status,
                    descriptor,
                    request.SourceOwnerSide,
                    request.TimestampSeconds,
                    out error))
            {
                return false;
            }

            events.Add(new CombatStatusAppliedEvent(
                request,
                status.statusInstanceId,
                status.statusId,
                status.stackIds.Length,
                stackAdded,
                durationRefreshed));
            stateChanged = true;
            return true;
        }

        private bool TryApplyIndependentModifier(
            CombatSessionSaveData session,
            CombatantStateSaveData target,
            CombatEffectRequest request,
            List<CombatEvent> events,
            out bool stateChanged,
            out CombatAdvanceError error)
        {
            stateChanged = false;
            error = null;
            var effect = request.Effect;
            var operation = effect.Operation ?? CombatModifierOperations.Add;
            if (string.IsNullOrWhiteSpace(effect.StatId) ||
                !string.Equals(
                    operation,
                    CombatModifierOperations.Add,
                    StringComparison.OrdinalIgnoreCase) ||
                InvalidNumber(effect.Value) ||
                InvalidTime(effect.DurationSeconds) ||
                effect.DurationSeconds <= 0d ||
                effect.Value < float.MinValue ||
                effect.Value > float.MaxValue)
            {
                return Fail(
                    CombatAdvanceErrorCode.EffectProcessingFailed,
                    "Temporary modifier descriptor is invalid.",
                    out error);
            }

            var values = target.independentModifiers ??
                         Array.Empty<CombatTemporaryModifierSaveData>();
            foreach (var existing in values)
            {
                if (existing != null &&
                    string.Equals(existing.appliedEventKey, request.EventKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (values.Length >= CombatRuntimeSaveDataUtility.PersistentCollectionLimit)
            {
                return Fail(
                    CombatAdvanceErrorCode.EffectProcessingFailed,
                    "Temporary modifier state reached its bounded retention limit.",
                    out error);
            }

            var expiresAtSeconds = request.TimestampSeconds + effect.DurationSeconds;
            if (InvalidTime(expiresAtSeconds) || expiresAtSeconds <= request.TimestampSeconds)
            {
                return Fail(
                    CombatAdvanceErrorCode.EffectProcessingFailed,
                    "Temporary modifier duration does not advance combat time.",
                    out error);
            }

            var modifierInstanceId =
                $"{target.combatantId}:modifier:{request.Sequence}";
            if (!TrySchedule(
                    session,
                    ModifierExpirationEventType,
                    CombatScheduledEventPhase.ModifierExpiration,
                    request.SourceOwnerSide,
                    target.combatantId,
                    modifierInstanceId,
                    expiresAtSeconds,
                    out error))
            {
                return false;
            }

            var modifier = new CombatTemporaryModifierSaveData
            {
                modifierInstanceId = modifierInstanceId,
                sourceId = request.SourceDescriptorId ?? request.SourceOwnerCombatantId,
                statId = effect.StatId,
                operation = CombatModifierOperations.Add,
                value = (float)effect.Value,
                expiresAtSeconds = expiresAtSeconds,
                appliedEventKey = request.EventKey
            };
            var expanded = new CombatTemporaryModifierSaveData[values.Length + 1];
            Array.Copy(values, expanded, values.Length);
            expanded[expanded.Length - 1] = modifier;
            Array.Sort(expanded, CompareModifier);
            target.independentModifiers = expanded;
            events.Add(new CombatTemporaryModifierAppliedEvent(
                request,
                modifierInstanceId,
                modifier.statId,
                modifier.value,
                expiresAtSeconds));
            stateChanged = true;
            return true;
        }

        private bool TryResolveTick(
            CombatSessionSaveData session,
            CombatantStateSaveData target,
            CombatScheduledEventSaveData scheduledEvent,
            List<CombatEvent> events,
            out bool stateChanged,
            out CombatHpMutation mutation,
            out CombatAdvanceError error)
        {
            stateChanged = false;
            mutation = null;
            error = null;
            var status = FindStatus(target.statuses, scheduledEvent.effectInstanceId);
            if (status == null ||
                string.Equals(status.lastTickEventKey, scheduledEvent.eventKey, StringComparison.Ordinal) ||
                status.nextTickAtSeconds != scheduledEvent.timestampSeconds)
            {
                return true;
            }

            if (!TryResolveDescriptor(status.statusId, out var descriptor, out error) ||
                !ValidateSavedStatus(status, descriptor, out error))
            {
                return false;
            }

            if (descriptor.PeriodicEffect == null)
            {
                return Fail(
                    CombatAdvanceErrorCode.InvalidStatusDescriptor,
                    $"Combat status '{status.statusId}' scheduled a tick without a periodic effect.",
                    out error);
            }

            if (!TryApplyImmediateEffect(
                    target,
                    descriptor.PeriodicEffect,
                     status.stackIds.Length,
                     out var amount,
                     out var hpBefore,
                     out var rawHpAfter,
                     out var hpAfter,
                     out var hpMutated,
                     out error))
            {
                return false;
            }

            status.lastTickEventKey = scheduledEvent.eventKey;
            status.nextTickAtSeconds = 0d;
            var nextTickAtSeconds = scheduledEvent.timestampSeconds + descriptor.TickIntervalSeconds;
            if (nextTickAtSeconds <= status.expiresAtSeconds)
            {
                if (nextTickAtSeconds <= scheduledEvent.timestampSeconds ||
                    !TrySchedule(
                        session,
                        StatusTickEventType,
                        CombatScheduledEventPhase.StatusTick,
                        scheduledEvent.actorSide,
                        target.combatantId,
                        status.statusInstanceId,
                        nextTickAtSeconds,
                        out error))
                {
                    error ??= new CombatAdvanceError(
                        CombatAdvanceErrorCode.StatusProcessingFailed,
                        $"Combat status '{status.statusId}' could not schedule its next tick.");
                    return false;
                }

                status.nextTickAtSeconds = nextTickAtSeconds;
            }

            var tickEvent = new CombatStatusTickEvent(
                scheduledEvent,
                status.sourceCombatantId,
                target.combatantId,
                status.statusInstanceId,
                status.statusId,
                status.stackIds.Length,
                descriptor.PeriodicEffect.Kind,
                descriptor.PeriodicEffect.DamageType,
                amount,
                hpBefore,
                rawHpAfter,
                hpAfter);
            events.Add(tickEvent);
            if (hpMutated)
            {
                var targetSide = string.Equals(
                    session.hero?.combatantId,
                    target.combatantId,
                    StringComparison.Ordinal)
                    ? CombatActorSide.Hero
                    : CombatActorSide.Enemy;
                mutation = new CombatHpMutation(
                    scheduledEvent.eventKey,
                    status.statusId,
                    scheduledEvent.timestampSeconds,
                    scheduledEvent.sequence,
                    scheduledEvent.actorSide,
                    status.sourceCombatantId,
                    targetSide,
                    target.combatantId,
                    hpBefore,
                    rawHpAfter,
                    hpAfter,
                    tickEvent.SetHpAfter);
            }
            stateChanged = true;
            return true;
        }

        private bool TryResolveStatusExpiration(
            CombatSessionSaveData session,
            CombatantStateSaveData target,
            CombatScheduledEventSaveData scheduledEvent,
            List<CombatEvent> events,
            out bool stateChanged,
            out CombatAdvanceError error)
        {
            stateChanged = false;
            error = null;
            var values = target.statuses ?? Array.Empty<CombatStatusInstanceSaveData>();
            var index = FindStatusIndex(values, scheduledEvent.effectInstanceId);
            if (index < 0 || values[index].expiresAtSeconds != scheduledEvent.timestampSeconds)
                return true;

            var status = values[index];
            var retained = new CombatStatusInstanceSaveData[values.Length - 1];
            if (index > 0)
                Array.Copy(values, 0, retained, 0, index);
            if (index < values.Length - 1)
                Array.Copy(values, index + 1, retained, index, values.Length - index - 1);
            target.statuses = retained;
            RemoveScheduledEvents(
                session.scheduler,
                value => string.Equals(
                             value?.eventType,
                             StatusTickEventType,
                             StringComparison.Ordinal) &&
                         string.Equals(
                             value.effectInstanceId,
                             status.statusInstanceId,
                             StringComparison.Ordinal));
            events.Add(new CombatStatusExpiredEvent(
                scheduledEvent,
                status.sourceCombatantId,
                target.combatantId,
                status.statusInstanceId,
                status.statusId,
                status.stackIds.Length));
            stateChanged = true;
            return true;
        }

        private static bool TryResolveModifierExpiration(
            CombatantStateSaveData target,
            CombatScheduledEventSaveData scheduledEvent,
            List<CombatEvent> events,
            out bool stateChanged,
            out CombatAdvanceError error)
        {
            stateChanged = false;
            error = null;
            var values = target.independentModifiers ??
                         Array.Empty<CombatTemporaryModifierSaveData>();
            var index = -1;
            for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
            {
                if (values[valueIndex] != null &&
                    string.Equals(
                        values[valueIndex].modifierInstanceId,
                        scheduledEvent.effectInstanceId,
                        StringComparison.Ordinal))
                {
                    index = valueIndex;
                    break;
                }
            }

            if (index < 0 || values[index].expiresAtSeconds != scheduledEvent.timestampSeconds)
                return true;

            var modifier = values[index];
            var retained = new CombatTemporaryModifierSaveData[values.Length - 1];
            if (index > 0)
                Array.Copy(values, 0, retained, 0, index);
            if (index < values.Length - 1)
                Array.Copy(values, index + 1, retained, index, values.Length - index - 1);
            target.independentModifiers = retained;
            events.Add(new CombatTemporaryModifierExpiredEvent(
                scheduledEvent,
                modifier.sourceId,
                target.combatantId,
                modifier.modifierInstanceId,
                modifier.statId,
                modifier.value));
            stateChanged = true;
            return true;
        }

        private bool TryEnsureStatusSchedule(
            CombatSessionSaveData session,
            CombatantStateSaveData target,
            CombatStatusInstanceSaveData status,
            CombatStatusDescriptor descriptor,
            CombatActorSide sourceSide,
            double timestampSeconds,
            out CombatAdvanceError error)
        {
            error = null;
            if (!HasScheduledEvent(
                    session.scheduler,
                    StatusExpirationEventType,
                    status.statusInstanceId) &&
                !TrySchedule(
                    session,
                    StatusExpirationEventType,
                    CombatScheduledEventPhase.StatusExpiration,
                    sourceSide,
                    target.combatantId,
                    status.statusInstanceId,
                    status.expiresAtSeconds,
                    out error))
            {
                return false;
            }

            if (descriptor.PeriodicEffect == null)
            {
                status.nextTickAtSeconds = 0d;
                return true;
            }

            if (status.nextTickAtSeconds > timestampSeconds &&
                HasScheduledEvent(
                    session.scheduler,
                    StatusTickEventType,
                    status.statusInstanceId))
            {
                return true;
            }

            var nextTickAtSeconds = timestampSeconds + descriptor.TickIntervalSeconds;
            if (nextTickAtSeconds > status.expiresAtSeconds)
            {
                status.nextTickAtSeconds = 0d;
                return true;
            }

            if (nextTickAtSeconds <= timestampSeconds ||
                !TrySchedule(
                    session,
                    StatusTickEventType,
                    CombatScheduledEventPhase.StatusTick,
                    sourceSide,
                    target.combatantId,
                    status.statusInstanceId,
                    nextTickAtSeconds,
                    out error))
            {
                error ??= new CombatAdvanceError(
                    CombatAdvanceErrorCode.StatusProcessingFailed,
                    $"Combat status '{status.statusId}' could not schedule its first tick.");
                return false;
            }

            status.nextTickAtSeconds = nextTickAtSeconds;
            return true;
        }

        private bool TryResolveDescriptor(
            string statusId,
            out CombatStatusDescriptor descriptor,
            out CombatAdvanceError error)
        {
            descriptor = null;
            error = null;
            try
            {
                if (!_provider.TryGetStatus(statusId, out descriptor, out var providerError) ||
                    descriptor == null)
                {
                    return Fail(
                        CombatAdvanceErrorCode.StatusDescriptorNotFound,
                        providerError ?? $"Combat status '{statusId ?? "<null>"}' was not found.",
                        out error);
                }
            }
            catch (Exception exception)
            {
                return Fail(
                    CombatAdvanceErrorCode.InvalidStatusDescriptor,
                    $"Combat status provider failed: {exception.Message}",
                    out error);
            }

            if (!ValidateDescriptor(descriptor, statusId))
            {
                return Fail(
                    CombatAdvanceErrorCode.InvalidStatusDescriptor,
                    $"Combat status '{statusId ?? "<null>"}' has an invalid descriptor.",
                    out error);
            }

            return true;
        }

        private static bool ValidateDescriptor(
            CombatStatusDescriptor descriptor,
            string expectedStatusId)
        {
            if (descriptor == null ||
                string.IsNullOrWhiteSpace(descriptor.StatusId) ||
                !string.Equals(descriptor.StatusId, expectedStatusId, StringComparison.Ordinal) ||
                InvalidNumber(descriptor.DurationSeconds) ||
                descriptor.DurationSeconds <= 0d ||
                InvalidNumber(descriptor.TickIntervalSeconds) ||
                descriptor.TickIntervalSeconds < 0d ||
                descriptor.MaxStacks <= 0 ||
                descriptor.MaxStacks > CombatRuntimeSaveDataUtility.StatusStackLimit)
            {
                return false;
            }

            var periodic = descriptor.PeriodicEffect;
            if (periodic != null &&
                ((periodic.Kind != CombatEffectKind.Damage &&
                  periodic.Kind != CombatEffectKind.Heal) ||
                 InvalidNumber(periodic.Value) ||
                 periodic.Value <= 0d ||
                 descriptor.TickIntervalSeconds <= 0d ||
                 (periodic.Kind == CombatEffectKind.Damage &&
                  string.IsNullOrWhiteSpace(periodic.DamageType))))
            {
                return false;
            }

            var modifier = descriptor.ModifierEffect;
            if (modifier != null &&
                (modifier.Kind != CombatEffectKind.ModifyStat ||
                 string.IsNullOrWhiteSpace(modifier.StatId) ||
                 InvalidNumber(modifier.Value) ||
                 !string.Equals(
                     modifier.Operation ?? CombatModifierOperations.Add,
                     CombatModifierOperations.Add,
                     StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return periodic != null || modifier != null;
        }

        private static bool ValidateSavedStatus(
            CombatStatusInstanceSaveData status,
            CombatStatusDescriptor descriptor,
            out CombatAdvanceError error)
        {
            error = null;
            if (status?.stackIds == null ||
                status.stackIds.Length == 0 ||
                status.stackIds.Length > descriptor.MaxStacks ||
                status.stackIds.Length > CombatRuntimeSaveDataUtility.StatusStackLimit)
            {
                return Fail(
                    CombatAdvanceErrorCode.StatusProcessingFailed,
                    $"Saved combat status '{status?.statusId ?? "<null>"}' has invalid stacks.",
                    out error);
            }

            return true;
        }

        private static bool TryApplyImmediateEffect(
            CombatantStateSaveData target,
            CombatEffectDescriptor effect,
            int multiplier,
            out int amount,
            out int hpBefore,
            out long rawHpAfter,
            out int hpAfter,
            out bool hpMutated,
            out CombatAdvanceError error)
        {
            amount = 0;
            hpBefore = target?.currentHp ?? 0;
            rawHpAfter = hpBefore;
            hpAfter = hpBefore;
            hpMutated = false;
            error = null;
            if (target == null ||
                effect == null ||
                (effect.Kind != CombatEffectKind.Damage &&
                 effect.Kind != CombatEffectKind.Heal) ||
                multiplier <= 0 ||
                InvalidNumber(effect.Value) ||
                effect.Value <= 0d)
            {
                return Fail(
                    CombatAdvanceErrorCode.EffectProcessingFailed,
                    "Immediate combat effect descriptor is invalid.",
                    out error);
            }

            var scaled = Math.Ceiling(effect.Value * multiplier);
            if (InvalidNumber(scaled) || scaled <= 0d)
            {
                return Fail(
                    CombatAdvanceErrorCode.EffectProcessingFailed,
                    "Immediate combat effect produced an invalid amount.",
                    out error);
            }

            amount = scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
            rawHpAfter = effect.Kind == CombatEffectKind.Damage
                ? (long)hpBefore - amount
                : (long)hpBefore + amount;
            hpAfter = effect.Kind == CombatEffectKind.Damage
                ? (int)Math.Max(0L, rawHpAfter)
                : (int)Math.Min(target.maxHp, rawHpAfter);
            hpMutated = hpAfter != hpBefore;
            target.currentHp = hpAfter;
            return true;
        }

        private static bool TrySchedule(
            CombatSessionSaveData session,
            string eventType,
            CombatScheduledEventPhase phase,
            CombatActorSide actorSide,
            string subjectCombatantId,
            string effectInstanceId,
            double timestampSeconds,
            out CombatAdvanceError error)
        {
            error = null;
            var scheduler = session.scheduler;
            scheduler.scheduledEvents ??= Array.Empty<CombatScheduledEventSaveData>();
            if (InvalidTime(timestampSeconds) ||
                timestampSeconds <= session.combatTimeSeconds ||
                actorSide == CombatActorSide.System ||
                string.IsNullOrWhiteSpace(subjectCombatantId) ||
                string.IsNullOrWhiteSpace(effectInstanceId))
            {
                return Fail(
                    CombatAdvanceErrorCode.StatusProcessingFailed,
                    "Status scheduler event is invalid or does not advance combat time.",
                    out error);
            }

            if (scheduler.scheduledEvents.Length >=
                CombatRuntimeSaveDataUtility.PersistentCollectionLimit)
            {
                return Fail(
                    CombatAdvanceErrorCode.StatusProcessingFailed,
                    "Combat scheduler reached its bounded event limit.",
                    out error);
            }

            var sequence = scheduler.nextSequence;
            if (sequence == long.MaxValue)
            {
                return Fail(
                    CombatAdvanceErrorCode.StatusProcessingFailed,
                    "Combat scheduler sequence is exhausted.",
                    out error);
            }

            scheduler.nextSequence++;
            var scheduledEvent = new CombatScheduledEventSaveData
            {
                eventKey = $"{session.sessionId}:{eventType}:{sequence}",
                eventType = eventType,
                timestampSeconds = timestampSeconds,
                phasePriority = (int)phase,
                actorSide = actorSide,
                sequence = sequence,
                subjectCombatantId = subjectCombatantId,
                effectInstanceId = effectInstanceId
            };
            var expanded =
                new CombatScheduledEventSaveData[scheduler.scheduledEvents.Length + 1];
            Array.Copy(
                scheduler.scheduledEvents,
                expanded,
                scheduler.scheduledEvents.Length);
            expanded[expanded.Length - 1] = scheduledEvent;
            Array.Sort(expanded, CombatScheduledEventComparer.Instance);
            scheduler.scheduledEvents = expanded;
            return true;
        }

        private static bool HasScheduledEvent(
            CombatSchedulerStateSaveData scheduler,
            string eventType,
            string effectInstanceId)
        {
            foreach (var value in scheduler.scheduledEvents ??
                                  Array.Empty<CombatScheduledEventSaveData>())
            {
                if (value != null &&
                    string.Equals(value.eventType, eventType, StringComparison.Ordinal) &&
                    string.Equals(
                        value.effectInstanceId,
                        effectInstanceId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveScheduledEvents(
            CombatSchedulerStateSaveData scheduler,
            Predicate<CombatScheduledEventSaveData> predicate)
        {
            var values = scheduler.scheduledEvents ??
                         Array.Empty<CombatScheduledEventSaveData>();
            var retained = new List<CombatScheduledEventSaveData>(values.Length);
            foreach (var value in values)
            {
                if (!predicate(value))
                    retained.Add(value);
            }

            scheduler.scheduledEvents = retained.ToArray();
        }

        private static CombatantStateSaveData FindCombatant(
            CombatSessionSaveData session,
            string combatantId)
        {
            if (session == null || string.IsNullOrWhiteSpace(combatantId))
                return null;
            if (string.Equals(
                    session.hero?.combatantId,
                    combatantId,
                    StringComparison.Ordinal))
            {
                return session.hero;
            }

            return string.Equals(
                session.currentEnemy?.combatantId,
                combatantId,
                StringComparison.Ordinal)
                ? session.currentEnemy
                : null;
        }

        private static CombatStatusInstanceSaveData FindStatus(
            CombatStatusInstanceSaveData[] values,
            string statusId,
            string sourceCombatantId)
        {
            foreach (var value in values ?? Array.Empty<CombatStatusInstanceSaveData>())
            {
                if (value != null &&
                    string.Equals(value.statusId, statusId, StringComparison.Ordinal) &&
                    string.Equals(
                        value.sourceCombatantId,
                        sourceCombatantId,
                        StringComparison.Ordinal))
                {
                    return value;
                }
            }

            return null;
        }

        private static CombatStatusInstanceSaveData FindStatus(
            CombatStatusInstanceSaveData[] values,
            string statusInstanceId)
        {
            var index = FindStatusIndex(values, statusInstanceId);
            return index < 0 ? null : values[index];
        }

        private static int FindStatusIndex(
            CombatStatusInstanceSaveData[] values,
            string statusInstanceId)
        {
            values ??= Array.Empty<CombatStatusInstanceSaveData>();
            for (var index = 0; index < values.Length; index++)
            {
                if (values[index] != null &&
                    string.Equals(
                        values[index].statusInstanceId,
                        statusInstanceId,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private static int CompareStatus(
            CombatStatusInstanceSaveData left,
            CombatStatusInstanceSaveData right)
        {
            return string.Compare(
                left?.statusInstanceId,
                right?.statusInstanceId,
                StringComparison.Ordinal);
        }

        private static int CompareModifier(
            CombatTemporaryModifierSaveData left,
            CombatTemporaryModifierSaveData right)
        {
            return string.Compare(
                left?.modifierInstanceId,
                right?.modifierInstanceId,
                StringComparison.Ordinal);
        }

        private static bool Fail(
            CombatAdvanceErrorCode code,
            string message,
            out CombatAdvanceError error)
        {
            error = new CombatAdvanceError(code, message);
            return false;
        }

        private static bool InvalidTime(double value)
        {
            return InvalidNumber(value) || value < 0d;
        }

        private static bool InvalidNumber(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value);
        }
    }
}
