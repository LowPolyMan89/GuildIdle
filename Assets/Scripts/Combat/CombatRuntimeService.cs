using System;
using System.Collections.Generic;
using System.Globalization;
using GuildIdle.Player;

namespace GuildIdle.Combat
{
    public enum CombatActorSide
    {
        Hero = 0,
        Enemy = 1,
        System = 2
    }

    public enum CombatAttackCadenceKind
    {
        AttackIntervalSeconds = 0,
        AttacksPerSecond = 1
    }

    public enum CombatScheduledEventPhase
    {
        StatusTick = 10,
        StatusExpiration = 20,
        ModifierExpiration = 30,
        ActorAttack = 100,
        ConsumableCheck = 200
    }

    public static class CombatStatIds
    {
        public const string HeroDamagePercent = "hero_damage_percent";
    }

    public enum CombatAdvanceErrorCode
    {
        None = 0,
        InvalidRequest = 1,
        CombatNotFound = 2,
        SimulationStopped = 3,
        InvalidAggregate = 4,
        DescriptorNotFound = 5,
        InvalidDescriptor = 6,
        InvalidAttackCadence = 7,
        UnsupportedRngDescriptor = 8,
        InvalidRngState = 9,
        UnsupportedScheduledEvent = 10,
        ProcessingFailed = 11,
        StoreUpdateFailed = 12,
        UnsupportedCombatMode = 13,
        EnemyQueueNotFound = 14,
        InvalidEnemyQueue = 15,
        EnemyStateCreationFailed = 16,
        AbilityDescriptorNotFound = 17,
        InvalidAbilityDescriptor = 18,
        UnsupportedAbilityTrigger = 19,
        UnsupportedAbilityTarget = 20,
        AbilityDispatchFailed = 21,
        StatusDescriptorNotFound = 22,
        InvalidStatusDescriptor = 23,
        StatusProcessingFailed = 24,
        EffectProcessingFailed = 25,
        DeathPreventionDescriptorNotFound = 26,
        InvalidDeathPreventionDescriptor = 27,
        DeathPreventionFailed = 28,
        ConsumableDescriptorNotFound = 29,
        InvalidConsumableDescriptor = 30,
        ConsumableConditionFailed = 31,
        ConsumableProcessingFailed = 32,
        EnemyRewardFailed = 33,
        OutcomeCommitFailed = 34
    }

    public readonly struct CombatAttackCadence
    {
        public CombatAttackCadence(CombatAttackCadenceKind kind, double value)
        {
            Kind = kind;
            Value = value;
        }

        public CombatAttackCadenceKind Kind { get; }
        public double Value { get; }

        public static CombatAttackCadence HeroInterval(double attackIntervalSeconds)
        {
            return new CombatAttackCadence(CombatAttackCadenceKind.AttackIntervalSeconds, attackIntervalSeconds);
        }

        public static CombatAttackCadence EnemyRate(double attacksPerSecond)
        {
            return new CombatAttackCadence(CombatAttackCadenceKind.AttacksPerSecond, attacksPerSecond);
        }
    }

    public sealed class CombatActorDescriptor
    {
        public CombatActorDescriptor(
            CombatActorSide side,
            CombatAttackCadence cadence,
            int damageMin,
            int damageMax,
            string damageType,
            double critChancePercent,
            double critDamageMultiplier,
            double dodgeChancePercent,
            double physicalResistancePercent,
            double magicResistancePercent,
            string damageModifierStatId = null)
        {
            Side = side;
            Cadence = cadence;
            DamageMin = damageMin;
            DamageMax = damageMax;
            DamageType = damageType;
            CritChancePercent = critChancePercent;
            CritDamageMultiplier = critDamageMultiplier;
            DodgeChancePercent = dodgeChancePercent;
            PhysicalResistancePercent = physicalResistancePercent;
            MagicResistancePercent = magicResistancePercent;
            DamageModifierStatId = damageModifierStatId ??
                (side == CombatActorSide.Hero ? CombatStatIds.HeroDamagePercent : null);
        }

        public CombatActorSide Side { get; }
        public CombatAttackCadence Cadence { get; }
        public int DamageMin { get; }
        public int DamageMax { get; }
        public string DamageType { get; }
        public double CritChancePercent { get; }
        public double CritDamageMultiplier { get; }
        public double DodgeChancePercent { get; }
        public double PhysicalResistancePercent { get; }
        public double MagicResistancePercent { get; }
        public string DamageModifierStatId { get; }

        public bool TryGetResistancePercent(string damageType, out double resistancePercent)
        {
            if (string.Equals(damageType, "physical", StringComparison.OrdinalIgnoreCase))
            {
                resistancePercent = PhysicalResistancePercent;
                return true;
            }

            if (string.Equals(damageType, "magic", StringComparison.OrdinalIgnoreCase))
            {
                resistancePercent = MagicResistancePercent;
                return true;
            }

            resistancePercent = 0d;
            return false;
        }
    }

    public interface ICombatDescriptorProvider
    {
        bool TryGetDescriptor(
            CombatActorSide side,
            string definitionId,
            out CombatActorDescriptor descriptor,
            out string error);
    }

    public interface ICombatRng
    {
        ulong NextUInt64();
        CombatRngStateSaveData CaptureState();
    }

    public interface ICombatRngFactory
    {
        bool TryRestore(
            CombatRngStateSaveData state,
            out ICombatRng rng,
            out CombatAdvanceError error);
    }

    public interface ICombatAggregateCommitter
    {
        bool TryCommit(
            CombatRuntimeAggregate aggregate,
            out CombatAdvanceError error);
    }

    public sealed class CombatAdvanceError
    {
        public CombatAdvanceError(CombatAdvanceErrorCode code, string message)
        {
            Code = code;
            Message = message;
        }

        public CombatAdvanceErrorCode Code { get; }
        public string Message { get; }
    }

    public abstract class CombatEvent
    {
        protected CombatEvent(
            string eventKey,
            double timestampSeconds,
            long sequence,
            CombatActorSide actorSide,
            string actorCombatantId,
            string targetCombatantId)
        {
            EventKey = eventKey;
            TimestampSeconds = timestampSeconds;
            Sequence = sequence;
            ActorSide = actorSide;
            ActorCombatantId = actorCombatantId;
            TargetCombatantId = targetCombatantId;
        }

        public string EventKey { get; }
        public double TimestampSeconds { get; }
        public long Sequence { get; }
        public CombatActorSide ActorSide { get; }
        public string ActorCombatantId { get; }
        public string TargetCombatantId { get; }
    }

    public sealed class CombatAttackEvent : CombatEvent
    {
        public CombatAttackEvent(
            string eventKey,
            double timestampSeconds,
            long sequence,
            CombatActorSide actorSide,
            string actorCombatantId,
            string targetCombatantId)
            : base(eventKey, timestampSeconds, sequence, actorSide, actorCombatantId, targetCombatantId)
        {
        }
    }

    public sealed class CombatDodgeEvent : CombatEvent
    {
        public CombatDodgeEvent(
            string eventKey,
            double timestampSeconds,
            long sequence,
            CombatActorSide actorSide,
            string actorCombatantId,
            string targetCombatantId,
            double dodgeRollPercent)
            : base(eventKey, timestampSeconds, sequence, actorSide, actorCombatantId, targetCombatantId)
        {
            DodgeRollPercent = dodgeRollPercent;
        }

        public double DodgeRollPercent { get; }
        public int Damage => 0;
    }

    public sealed class CombatDamageEvent : CombatEvent
    {
        public CombatDamageEvent(
            string eventKey,
            double timestampSeconds,
            long sequence,
            CombatActorSide actorSide,
            string actorCombatantId,
            string targetCombatantId,
            double dodgeRollPercent,
            int baseDamage,
            double critRollPercent,
            bool critical,
            double resistancePercent,
            int damage,
            int targetHpBefore,
            long targetRawHpAfter,
            int targetHpAfter)
            : base(eventKey, timestampSeconds, sequence, actorSide, actorCombatantId, targetCombatantId)
        {
            DodgeRollPercent = dodgeRollPercent;
            BaseDamage = baseDamage;
            CritRollPercent = critRollPercent;
            Critical = critical;
            ResistancePercent = resistancePercent;
            Damage = damage;
            TargetHpBefore = targetHpBefore;
            TargetRawHpAfter = targetRawHpAfter;
            TargetHpAfter = targetHpAfter;
        }

        public double DodgeRollPercent { get; }
        public int BaseDamage { get; }
        public double CritRollPercent { get; }
        public bool Critical { get; }
        public double ResistancePercent { get; }
        public int Damage { get; }
        public int TargetHpBefore { get; }
        public long TargetRawHpAfter { get; }
        public int TargetHpAfter { get; private set; }

        internal void SetTargetHpAfter(int value)
        {
            TargetHpAfter = value;
        }
    }

    public sealed class CombatConsumableUsedEvent : CombatEvent
    {
        public CombatConsumableUsedEvent(
            string eventKey,
            double timestampSeconds,
            long sequence,
            string itemId,
            string sourceStackId,
            int quantityBefore,
            int quantityAfter,
            double nextAllowedUseAtSeconds,
            string targetCombatantId)
            : base(
                eventKey,
                timestampSeconds,
                sequence,
                CombatActorSide.System,
                null,
                targetCombatantId)
        {
            ItemId = itemId;
            SourceStackId = sourceStackId;
            QuantityBefore = quantityBefore;
            QuantityAfter = quantityAfter;
            NextAllowedUseAtSeconds = nextAllowedUseAtSeconds;
        }

        public string ItemId { get; }
        public string SourceStackId { get; }
        public int QuantityBefore { get; }
        public int QuantityAfter { get; }
        public double NextAllowedUseAtSeconds { get; }
    }

    public sealed class CombatAdvanceResult
    {
        private CombatAdvanceResult(
            bool success,
            double combatTimeSeconds,
            CombatEvent[] events,
            CombatAdvanceError error)
        {
            Success = success;
            CombatTimeSeconds = combatTimeSeconds;
            Events = events ?? Array.Empty<CombatEvent>();
            Error = error;
        }

        public bool Success { get; }
        public double CombatTimeSeconds { get; }
        public CombatEvent[] Events { get; }
        public CombatAdvanceError Error { get; }

        internal static CombatAdvanceResult Succeeded(double combatTimeSeconds, List<CombatEvent> events)
        {
            return new CombatAdvanceResult(true, combatTimeSeconds, events.ToArray(), null);
        }

        internal static CombatAdvanceResult Failed(
            CombatAdvanceErrorCode code,
            string message,
            double combatTimeSeconds = 0d)
        {
            return new CombatAdvanceResult(
                false,
                combatTimeSeconds,
                Array.Empty<CombatEvent>(),
                new CombatAdvanceError(code, message));
        }

        internal static CombatAdvanceResult Failed(CombatAdvanceError error, double combatTimeSeconds)
        {
            return new CombatAdvanceResult(false, combatTimeSeconds, Array.Empty<CombatEvent>(), error);
        }
    }

    public sealed class CombatScheduledEventComparer : IComparer<CombatScheduledEventSaveData>
    {
        public static readonly CombatScheduledEventComparer Instance = new CombatScheduledEventComparer();

        private CombatScheduledEventComparer()
        {
        }

        public int Compare(CombatScheduledEventSaveData left, CombatScheduledEventSaveData right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return 1;
            if (right == null)
                return -1;

            var order = left.timestampSeconds.CompareTo(right.timestampSeconds);
            if (order != 0)
                return order;
            order = left.phasePriority.CompareTo(right.phasePriority);
            if (order != 0)
                return order;
            order = left.actorSide.CompareTo(right.actorSide);
            return order != 0 ? order : left.sequence.CompareTo(right.sequence);
        }
    }

    public static class CombatRngStateFactory
    {
        public const string SplitMix64AlgorithmId = "splitmix64";
        public const int SplitMix64FormatVersion = 1;

        public static CombatRngStateSaveData CreateSplitMix64(ulong seed)
        {
            return new CombatRngStateSaveData
            {
                algorithmId = SplitMix64AlgorithmId,
                formatVersion = SplitMix64FormatVersion,
                state = seed.ToString("X16", CultureInfo.InvariantCulture),
                drawCount = 0
            };
        }
    }

    public sealed class CombatRngFactory : ICombatRngFactory
    {
        public bool TryRestore(
            CombatRngStateSaveData state,
            out ICombatRng rng,
            out CombatAdvanceError error)
        {
            rng = null;
            error = null;
            if (state == null ||
                !string.Equals(
                    state.algorithmId,
                    CombatRngStateFactory.SplitMix64AlgorithmId,
                    StringComparison.Ordinal) ||
                state.formatVersion != CombatRngStateFactory.SplitMix64FormatVersion)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.UnsupportedRngDescriptor,
                    $"Unsupported combat RNG descriptor '{state?.algorithmId ?? "<null>"}' v{state?.formatVersion ?? 0}.");
                return false;
            }

            if (state.drawCount < 0 ||
                string.IsNullOrWhiteSpace(state.state) ||
                !ulong.TryParse(state.state, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedState))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidRngState,
                    "Combat RNG state is malformed.");
                return false;
            }

            rng = new SplitMix64CombatRng(parsedState, state.drawCount);
            return true;
        }

        private sealed class SplitMix64CombatRng : ICombatRng
        {
            private ulong _state;
            private long _drawCount;

            public SplitMix64CombatRng(ulong state, long drawCount)
            {
                _state = state;
                _drawCount = drawCount;
            }

            public ulong NextUInt64()
            {
                if (_drawCount == long.MaxValue)
                    throw new InvalidOperationException("Combat RNG draw count is exhausted.");

                unchecked
                {
                    _state += 0x9E3779B97F4A7C15UL;
                    var value = _state;
                    value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                    value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                    _drawCount++;
                    return value ^ (value >> 31);
                }
            }

            public CombatRngStateSaveData CaptureState()
            {
                return new CombatRngStateSaveData
                {
                    algorithmId = CombatRngStateFactory.SplitMix64AlgorithmId,
                    formatVersion = CombatRngStateFactory.SplitMix64FormatVersion,
                    state = _state.ToString("X16", CultureInfo.InvariantCulture),
                    drawCount = _drawCount
                };
            }
        }
    }

    public sealed class CombatRuntimeService
    {
        public const string ActorAttackEventType = "actor_attack";
        public const string ConsumableCheckEventType = "consumable_check";
        public const string ConsumableAutoUseTrigger = "AutoUse";
        private const double PercentScale = 100d / 9007199254740992d;

        private readonly ICombatRuntimeStore _store;
        private readonly ICombatDescriptorProvider _descriptors;
        private readonly ICombatRngFactory _rngFactory;
        private readonly ICombatEnemyQueueProvider _enemyQueue;
        private readonly CombatAbilityDispatcher _abilityDispatcher;
        private readonly CombatStatusRuntime _statusRuntime;
        private readonly CombatStatusRuntime _effectRuntime;
        private readonly CombatDeathPreventionResolver _deathPrevention;
        private readonly ICombatConsumableDescriptorProvider _consumables;
        private readonly CombatConsumableConditionRegistry _consumableConditions;
        private readonly ICombatEnemyRewardProvider _enemyRewards;
        private readonly ICombatAggregateCommitter _committer;

        public CombatRuntimeService(
            ICombatRuntimeStore store,
            ICombatDescriptorProvider descriptors,
            ICombatRngFactory rngFactory = null,
            ICombatEnemyQueueProvider enemyQueue = null,
            ICombatAbilityDescriptorProvider abilities = null,
            CombatAbilityRegistry abilityRegistry = null,
            ICombatStatusDescriptorProvider statuses = null,
            ICombatDeathPreventionDescriptorProvider deathPrevention = null,
            CombatDeathPreventionRegistry deathPreventionRegistry = null,
            ICombatConsumableDescriptorProvider consumables = null,
            CombatConsumableConditionRegistry consumableConditions = null,
            CombatEffectExecutorRegistry effects = null,
            ICombatEnemyRewardProvider enemyRewards = null,
            ICombatAggregateCommitter committer = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _descriptors = descriptors ?? throw new ArgumentNullException(nameof(descriptors));
            _rngFactory = rngFactory ?? new CombatRngFactory();
            _enemyQueue = enemyQueue;
            _abilityDispatcher = new CombatAbilityDispatcher(
                abilities ?? EmptyCombatAbilityDescriptorProvider.Instance,
                abilityRegistry ?? new CombatAbilityRegistry());
            _statusRuntime = statuses == null
                ? null
                : new CombatStatusRuntime(statuses, effects);
            _effectRuntime = _statusRuntime ??
                new CombatStatusRuntime(
                    EmptyCombatStatusDescriptorProvider.Instance,
                    effects);
            _deathPrevention = new CombatDeathPreventionResolver(
                deathPrevention ?? EmptyCombatDeathPreventionDescriptorProvider.Instance,
                deathPreventionRegistry ?? new CombatDeathPreventionRegistry());
            _consumables = consumables ?? EmptyCombatConsumableDescriptorProvider.Instance;
            _consumableConditions =
                consumableConditions ?? new CombatConsumableConditionRegistry();
            _enemyRewards = enemyRewards;
            _committer = committer;
        }

        public CombatAdvanceResult AdvanceTo(string executionId, double targetCombatTimeSeconds)
        {
            if (string.IsNullOrWhiteSpace(executionId) ||
                InvalidTime(targetCombatTimeSeconds))
            {
                return CombatAdvanceResult.Failed(
                    CombatAdvanceErrorCode.InvalidRequest,
                    "Combat execution id and a finite non-negative target time are required.");
            }

            var stored = _store.GetCombatAggregate(executionId);
            if (stored == null)
            {
                return CombatAdvanceResult.Failed(
                    CombatAdvanceErrorCode.CombatNotFound,
                    $"Combat execution '{executionId}' was not found.");
            }

            var currentTime = stored.session?.combatTimeSeconds ?? 0d;
            if (stored.session == null || stored.session.hero == null ||
                stored.session.scheduler == null || stored.session.rng == null)
            {
                return CombatAdvanceResult.Failed(
                    CombatAdvanceErrorCode.InvalidAggregate,
                    "Combat aggregate does not contain a complete duel state.",
                    currentTime);
            }

            if (stored.session.simulationStopped ||
                HasTerminalCandidate(stored.session))
            {
                return CombatAdvanceResult.Failed(
                    CombatAdvanceErrorCode.SimulationStopped,
                    "Combat simulation is stopped.",
                    currentTime);
            }

            if (targetCombatTimeSeconds < currentTime)
            {
                return CombatAdvanceResult.Failed(
                    CombatAdvanceErrorCode.InvalidRequest,
                    "Combat time cannot move backwards.",
                    currentTime);
            }

            if (stored.session.currentEnemy == null)
            {
                return CombatAdvanceResult.Failed(
                    CombatAdvanceErrorCode.InvalidAggregate,
                    "Combat aggregate does not contain a current enemy.",
                    currentTime);
            }

            if (!TryResolveDescriptor(stored.session.hero, CombatActorSide.Hero, out var hero, out var error) ||
                !TryResolveDescriptor(stored.session.currentEnemy, CombatActorSide.Enemy, out var enemy, out error))
            {
                return CombatAdvanceResult.Failed(error, currentTime);
            }

            if (!TryResolveAttackInterval(hero, out var heroInterval, out error) ||
                !TryResolveAttackInterval(enemy, out var enemyInterval, out error))
            {
                return CombatAdvanceResult.Failed(error, currentTime);
            }

            var savedRng = new CombatRngStateSaveData
            {
                algorithmId = stored.session.rng.algorithmId,
                formatVersion = stored.session.rng.formatVersion,
                state = stored.session.rng.state,
                drawCount = stored.session.rng.drawCount
            };
            if (!TryRestoreRng(savedRng, out var rng, out error))
                return CombatAdvanceResult.Failed(error, currentTime);

            var aggregate = CombatRuntimeSaveDataUtility.CloneAggregate(stored);
            var events = new List<CombatEvent>();
            try
            {
                var scheduler = aggregate.session.scheduler;
                var resolvedCombatTime = targetCombatTimeSeconds;
                var terminalAtBattleStart = false;
                scheduler.scheduledEvents ??= Array.Empty<CombatScheduledEventSaveData>();
                if (!TryEnsureConsumableSchedule(
                        aggregate.session,
                        out var consumableScheduleChanged,
                        out error))
                {
                    return CombatAdvanceResult.Failed(error, currentTime);
                }
                if (!TryDispatchBattleStart(
                        aggregate.session,
                        CombatActorSide.Hero,
                        aggregate.session.combatTimeSeconds,
                        rng,
                        events,
                        out var heroAbilityStateChanged,
                        out var heroBattleStartTerminal,
                        out error) ||
                    (heroBattleStartTerminal != null &&
                     !TryHandleDefeatedCombatant(
                         aggregate.session,
                         ToScheduledEvent(heroBattleStartTerminal),
                         heroInterval,
                         rng,
                         events,
                         ref enemy,
                         ref enemyInterval,
                         out terminalAtBattleStart,
                         out error)))
                {
                    return CombatAdvanceResult.Failed(error, currentTime);
                }

                var enemyAbilityStateChanged = false;
                CombatHpMutation enemyBattleStartTerminal = null;
                if (!terminalAtBattleStart &&
                    !TryDispatchBattleStart(
                        aggregate.session,
                        CombatActorSide.Enemy,
                        aggregate.session.combatTimeSeconds,
                        rng,
                        events,
                        out enemyAbilityStateChanged,
                        out enemyBattleStartTerminal,
                        out error))
                {
                    return CombatAdvanceResult.Failed(error, currentTime);
                }

                if (!terminalAtBattleStart &&
                    enemyBattleStartTerminal != null &&
                    !TryHandleDefeatedCombatant(
                        aggregate.session,
                        ToScheduledEvent(enemyBattleStartTerminal),
                        heroInterval,
                        rng,
                        events,
                        ref enemy,
                        ref enemyInterval,
                        out terminalAtBattleStart,
                        out error))
                {
                    return CombatAdvanceResult.Failed(error, currentTime);
                }

                if (terminalAtBattleStart)
                    resolvedCombatTime = aggregate.session.combatTimeSeconds;

                if (!heroAbilityStateChanged &&
                    !enemyAbilityStateChanged &&
                    !consumableScheduleChanged &&
                    !terminalAtBattleStart &&
                    CanReturnWithoutAdvance(aggregate.session, targetCombatTimeSeconds))
                {
                    return CombatAdvanceResult.Succeeded(currentTime, events);
                }

                if (!terminalAtBattleStart &&
                    aggregate.session.hero.currentHp > 0 &&
                    aggregate.session.currentEnemy.currentHp > 0)
                {
                    if (!HasPendingActorAttack(scheduler, CombatActorSide.Hero) &&
                        !TryScheduleInitialAttack(
                            aggregate.session,
                            CombatActorSide.Hero,
                            heroInterval,
                            out error))
                    {
                        return CombatAdvanceResult.Failed(error, currentTime);
                    }

                    if (!HasPendingActorAttack(scheduler, CombatActorSide.Enemy) &&
                        !TryScheduleInitialAttack(
                            aggregate.session,
                            CombatActorSide.Enemy,
                            enemyInterval,
                            out error))
                    {
                        return CombatAdvanceResult.Failed(error, currentTime);
                    }
                }

                while (!terminalAtBattleStart &&
                       TryTakeNextDueEvent(scheduler, targetCombatTimeSeconds, out var scheduledEvent))
                {
                    var alreadyResolved = string.Equals(
                        scheduler.lastResolvedEventKey,
                        scheduledEvent.eventKey,
                        StringComparison.Ordinal);
                    scheduler.lastResolvedEventKey = scheduledEvent.eventKey;
                    if (_statusRuntime != null &&
                        _statusRuntime.IsScheduledEvent(scheduledEvent))
                    {
                        if (alreadyResolved)
                            continue;
                        if (!_statusRuntime.TryResolveScheduledEvent(
                                 aggregate.session,
                                 scheduledEvent,
                                 events,
                                 out _,
                                 out var statusMutation,
                                 out error))
                        {
                            return CombatAdvanceResult.Failed(error, currentTime);
                        }

                        var statusTargetSurvived = true;
                        if (statusMutation != null &&
                            !TryResolveTerminalBoundary(
                                aggregate.session,
                                statusMutation,
                                rng,
                                events,
                                out statusTargetSurvived,
                                out error))
                        {
                            return CombatAdvanceResult.Failed(error, currentTime);
                        }

                        if (statusMutation != null && !statusTargetSurvived)
                        {
                            if (!TryHandleDefeatedCombatant(
                                    aggregate.session,
                                    scheduledEvent,
                                    heroInterval,
                                    rng,
                                    events,
                                    ref enemy,
                                    ref enemyInterval,
                                    out var simulationTerminated,
                                    out error))
                            {
                                return CombatAdvanceResult.Failed(error, currentTime);
                            }

                            if (simulationTerminated)
                            {
                                resolvedCombatTime = scheduledEvent.timestampSeconds;
                                break;
                            }
                        }

                        continue;
                    }

                    if (string.Equals(
                            scheduledEvent.eventType,
                            ConsumableCheckEventType,
                            StringComparison.Ordinal))
                    {
                        if (alreadyResolved)
                            continue;
                        if (!TryResolveConsumableCheck(
                                aggregate.session,
                                scheduledEvent,
                                rng,
                                events,
                                out var consumableTerminal,
                                out error))
                        {
                            return CombatAdvanceResult.Failed(error, currentTime);
                        }

                        if (consumableTerminal)
                        {
                            resolvedCombatTime = scheduledEvent.timestampSeconds;
                            break;
                        }

                        continue;
                    }

                    if (!string.Equals(scheduledEvent.eventType, ActorAttackEventType, StringComparison.Ordinal))
                    {
                        return CombatAdvanceResult.Failed(
                            CombatAdvanceErrorCode.UnsupportedScheduledEvent,
                            $"Scheduled event type '{scheduledEvent.eventType}' is not supported by this runtime.",
                            currentTime);
                    }

                    if (scheduledEvent.actorSide != CombatActorSide.Hero &&
                        scheduledEvent.actorSide != CombatActorSide.Enemy)
                    {
                        return CombatAdvanceResult.Failed(
                            CombatAdvanceErrorCode.InvalidAggregate,
                            "Actor attack event requires a hero or enemy actor side.",
                            currentTime);
                    }

                    var attacker = GetCombatant(aggregate.session, scheduledEvent.actorSide);
                    var target = GetCombatant(aggregate.session, Opposite(scheduledEvent.actorSide));
                    if (attacker == null || target == null)
                    {
                        return CombatAdvanceResult.Failed(
                            CombatAdvanceErrorCode.InvalidAggregate,
                            "Scheduled attack does not resolve to both combatants.",
                            currentTime);
                    }

                    if (alreadyResolved ||
                        string.Equals(attacker.lastAttackEventKey, scheduledEvent.eventKey, StringComparison.Ordinal) ||
                        attacker.currentHp <= 0 || target.currentHp <= 0)
                    {
                        continue;
                    }

                    var attackerDescriptor = scheduledEvent.actorSide == CombatActorSide.Hero ? hero : enemy;
                    var targetDescriptor = scheduledEvent.actorSide == CombatActorSide.Hero ? enemy : hero;
                    var damageModifierPercent = 0d;
                    if (_statusRuntime != null &&
                        !_statusRuntime.TryGetStatModifier(
                            attacker,
                            attackerDescriptor.DamageModifierStatId,
                            scheduledEvent.timestampSeconds,
                            out damageModifierPercent,
                            out error))
                    {
                        return CombatAdvanceResult.Failed(error, currentTime);
                    }

                    if (!TryResolveAttack(
                            scheduledEvent,
                            attacker,
                            target,
                            attackerDescriptor,
                            targetDescriptor,
                            damageModifierPercent,
                             rng,
                             events,
                             out var attackHit,
                             out var attackMutation,
                             out error))
                    {
                        return CombatAdvanceResult.Failed(error, currentTime);
                    }

                    attacker.lastAttackEventKey = scheduledEvent.eventKey;
                    var attackTargetSurvived = true;
                    if (attackMutation != null &&
                        !TryResolveTerminalBoundary(
                            aggregate.session,
                            attackMutation,
                            rng,
                            events,
                            out attackTargetSurvived,
                            out error))
                    {
                        return CombatAdvanceResult.Failed(error, currentTime);
                    }

                    if (attackMutation != null && !attackTargetSurvived)
                    {
                        if (!TryHandleDefeatedCombatant(
                                aggregate.session,
                                scheduledEvent,
                                heroInterval,
                                rng,
                                events,
                                ref enemy,
                                ref enemyInterval,
                                out var simulationTerminated,
                                out error))
                        {
                            return CombatAdvanceResult.Failed(error, currentTime);
                        }

                        if (simulationTerminated)
                        {
                            resolvedCombatTime = scheduledEvent.timestampSeconds;
                            break;
                        }

                        continue;
                    }

                    CombatHpMutation effectTerminalMutation = null;
                    if (attackHit &&
                        !TryDispatchAndApplyEffects(
                            aggregate.session,
                            scheduledEvent.actorSide,
                            CombatAbilityTriggers.OnAttackHit,
                            scheduledEvent.eventKey,
                            scheduledEvent.timestampSeconds,
                            rng,
                            events,
                            out _,
                            out _,
                            out effectTerminalMutation,
                            out error))
                    {
                        return CombatAdvanceResult.Failed(error, currentTime);
                    }

                    if (effectTerminalMutation != null)
                    {
                        if (!TryHandleDefeatedCombatant(
                                aggregate.session,
                                ToScheduledEvent(effectTerminalMutation),
                                heroInterval,
                                rng,
                                events,
                                ref enemy,
                                ref enemyInterval,
                                out var simulationTerminated,
                                out error))
                        {
                            return CombatAdvanceResult.Failed(error, currentTime);
                        }

                        if (simulationTerminated)
                        {
                            resolvedCombatTime = effectTerminalMutation.TimestampSeconds;
                            break;
                        }

                        continue;
                    }

                    if (attacker.currentHp > 0 && target.currentHp > 0)
                    {
                        var interval = scheduledEvent.actorSide == CombatActorSide.Hero ? heroInterval : enemyInterval;
                        if (scheduledEvent.timestampSeconds + interval <= scheduledEvent.timestampSeconds)
                        {
                            return CombatAdvanceResult.Failed(
                                CombatAdvanceErrorCode.InvalidAttackCadence,
                                "Attack cadence is too small to advance the scheduler clock.",
                                currentTime);
                        }
                        if (!TryScheduleAttack(
                                aggregate.session,
                                scheduledEvent.actorSide,
                                scheduledEvent.timestampSeconds + interval,
                                out error))
                        {
                            return CombatAdvanceResult.Failed(error, currentTime);
                        }
                    }
                }

                aggregate.session.combatTimeSeconds = resolvedCombatTime;
                aggregate.session.rng = rng.CaptureState();
                if (_committer != null)
                {
                    if (!_committer.TryCommit(aggregate, out var commitError))
                        return CombatAdvanceResult.Failed(
                            commitError ?? new CombatAdvanceError(
                                CombatAdvanceErrorCode.OutcomeCommitFailed,
                                "Combat aggregate commit failed."),
                            currentTime);
                }
                else if (!_store.UpdateCombatAggregate(aggregate))
                {
                    return CombatAdvanceResult.Failed(
                        CombatAdvanceErrorCode.StoreUpdateFailed,
                        "Combat aggregate update was rejected.",
                        currentTime);
                }

                return CombatAdvanceResult.Succeeded(resolvedCombatTime, events);
            }
            catch (Exception exception)
            {
                return CombatAdvanceResult.Failed(
                    CombatAdvanceErrorCode.ProcessingFailed,
                    $"Combat advance failed: {exception.Message}",
                    currentTime);
            }
        }

        private bool TryResolveDescriptor(
            CombatantStateSaveData combatant,
            CombatActorSide expectedSide,
            out CombatActorDescriptor descriptor,
            out CombatAdvanceError error)
        {
            descriptor = null;
            error = null;
            try
            {
                if (!_descriptors.TryGetDescriptor(
                        expectedSide,
                        combatant.definitionId,
                        out descriptor,
                        out var providerError) ||
                    descriptor == null)
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.DescriptorNotFound,
                        providerError ?? $"Combat descriptor '{combatant.definitionId}' was not found.");
                    return false;
                }
            }
            catch (Exception exception)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidDescriptor,
                    $"Combat descriptor provider failed: {exception.Message}");
                return false;
            }

            if (descriptor.Side != expectedSide ||
                descriptor.DamageMin < 0 ||
                descriptor.DamageMax < descriptor.DamageMin ||
                !ValidPercent(descriptor.CritChancePercent) ||
                !ValidPercent(descriptor.DodgeChancePercent) ||
                !ValidPercent(descriptor.PhysicalResistancePercent) ||
                !ValidPercent(descriptor.MagicResistancePercent) ||
                InvalidNumber(descriptor.CritDamageMultiplier) ||
                descriptor.CritDamageMultiplier < 1d ||
                (descriptor.DamageModifierStatId != null &&
                 string.IsNullOrWhiteSpace(descriptor.DamageModifierStatId)) ||
                !descriptor.TryGetResistancePercent(descriptor.DamageType, out _))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidDescriptor,
                    $"Combat descriptor '{combatant.definitionId}' is invalid.");
                return false;
            }

            return true;
        }

        private static bool CanReturnWithoutAdvance(
            CombatSessionSaveData session,
            double targetCombatTimeSeconds)
        {
            if (targetCombatTimeSeconds != session.combatTimeSeconds ||
                session.scheduler.scheduledEvents == null ||
                session.hero.currentHp <= 0 ||
                session.currentEnemy.currentHp <= 0 ||
                !HasPendingActorAttack(session.scheduler, CombatActorSide.Hero) ||
                !HasPendingActorAttack(session.scheduler, CombatActorSide.Enemy))
            {
                return false;
            }

            foreach (var value in session.scheduler.scheduledEvents)
            {
                if (value == null || value.timestampSeconds <= targetCombatTimeSeconds)
                    return false;
            }

            return true;
        }

        private bool TryRestoreRng(
            CombatRngStateSaveData state,
            out ICombatRng rng,
            out CombatAdvanceError error)
        {
            rng = null;
            error = null;
            try
            {
                if (_rngFactory.TryRestore(state, out rng, out error) && rng != null)
                    return true;
            }
            catch (Exception exception)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidRngState,
                    $"Combat RNG restore failed: {exception.Message}");
                return false;
            }

            error ??= new CombatAdvanceError(
                CombatAdvanceErrorCode.InvalidRngState,
                "Combat RNG factory rejected the saved state.");
            return false;
        }

        private static bool TryResolveAttackInterval(
            CombatActorDescriptor descriptor,
            out double interval,
            out CombatAdvanceError error)
        {
            interval = 0d;
            error = null;
            var cadence = descriptor.Cadence;
            if (InvalidNumber(cadence.Value) || cadence.Value <= 0d)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidAttackCadence,
                    $"Combat {descriptor.Side} attack cadence must be greater than zero.");
                return false;
            }

            if (descriptor.Side == CombatActorSide.Hero)
            {
                if (cadence.Kind != CombatAttackCadenceKind.AttackIntervalSeconds)
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.InvalidAttackCadence,
                        "Hero cadence must be a derived attack interval.");
                    return false;
                }

                interval = cadence.Value;
                return true;
            }

            if (cadence.Kind != CombatAttackCadenceKind.AttacksPerSecond)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidAttackCadence,
                    "Enemy cadence must be expressed as attacks per second.");
                return false;
            }

            interval = 1d / cadence.Value;
            if (InvalidNumber(interval) || interval <= 0d)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidAttackCadence,
                    "Enemy attacks per second does not produce a valid attack interval.");
                return false;
            }

            return true;
        }

        private bool TryEnsureConsumableSchedule(
            CombatSessionSaveData session,
            out bool changed,
            out CombatAdvanceError error)
        {
            changed = false;
            error = null;
            var scheduler = session?.scheduler;
            if (scheduler?.scheduledEvents == null)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidAggregate,
                    "Consumable scheduling requires a combat session and scheduler.");
                return false;
            }

            CombatScheduledEventSaveData pending = null;
            foreach (var value in scheduler.scheduledEvents)
            {
                if (!string.Equals(
                        value?.eventType,
                        ConsumableCheckEventType,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (pending != null)
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.InvalidAggregate,
                        "Combat scheduler contains duplicated consumable checks.");
                    return false;
                }

                pending = value;
            }

            var state = session.broughtConsumable;
            if (session.loadoutKind != CombatLoadoutKind.Consumable || state == null)
            {
                if (pending == null)
                    return true;
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidAggregate,
                    "Combat scheduler retains a consumable check without a consumable loadout.");
                return false;
            }

            if (state.remainingQuantity <= 0 ||
                session.simulationStopped ||
                HasTerminalCandidate(session) ||
                session.hero?.currentHp <= 0 ||
                session.currentEnemy?.currentHp <= 0)
            {
                if (pending == null)
                    return true;
                RemovePendingConsumableChecks(scheduler);
                changed = true;
                return true;
            }

            if (pending != null)
            {
                if (pending.actorSide != CombatActorSide.System ||
                    pending.phasePriority != (int)CombatScheduledEventPhase.ConsumableCheck ||
                    !string.IsNullOrWhiteSpace(pending.subjectCombatantId) ||
                    !string.IsNullOrWhiteSpace(pending.effectInstanceId) ||
                    pending.timestampSeconds != state.nextCheckAtSeconds ||
                    string.Equals(
                        pending.eventKey,
                        state.lastAppliedEventKey,
                        StringComparison.Ordinal) ||
                    string.Equals(
                        pending.eventKey,
                        scheduler.lastResolvedEventKey,
                        StringComparison.Ordinal))
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.InvalidAggregate,
                        "Combat scheduler contains an invalid consumable check.");
                    return false;
                }

                return true;
            }

            if (!TryScheduleConsumableCheck(
                    session,
                    state.nextCheckAtSeconds,
                    out error))
            {
                return false;
            }

            changed = true;
            return true;
        }

        private bool TryResolveConsumableCheck(
            CombatSessionSaveData session,
            CombatScheduledEventSaveData scheduledEvent,
            ICombatRng rng,
            List<CombatEvent> events,
            out bool terminal,
            out CombatAdvanceError error)
        {
            terminal = false;
            error = null;
            var state = session?.broughtConsumable;
            if (session?.scheduler == null ||
                state == null ||
                scheduledEvent == null ||
                events == null ||
                scheduledEvent.actorSide != CombatActorSide.System ||
                scheduledEvent.phasePriority != (int)CombatScheduledEventPhase.ConsumableCheck ||
                scheduledEvent.timestampSeconds != state.nextCheckAtSeconds)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.ConsumableProcessingFailed,
                    "Consumable check does not match the saved combat state.");
                return false;
            }

            if (session.simulationStopped ||
                HasTerminalCandidate(session) ||
                session.hero?.currentHp <= 0 ||
                session.currentEnemy?.currentHp <= 0)
            {
                return true;
            }

            if (string.Equals(
                    state.lastAppliedEventKey,
                    scheduledEvent.eventKey,
                    StringComparison.Ordinal))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.ConsumableProcessingFailed,
                    "Consumable check event was already applied.");
                return false;
            }

            if (state.remainingQuantity <= 0)
                return true;

            CombatConsumableDescriptor descriptor;
            try
            {
                if (!_consumables.TryGet(state.itemId, out descriptor) ||
                    descriptor == null)
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.ConsumableDescriptorNotFound,
                        $"Combat consumable descriptor '{state.itemId}' was not found.");
                    return false;
                }
            }
            catch (Exception exception)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidConsumableDescriptor,
                    $"Combat consumable provider failed: {exception.Message}");
                return false;
            }

            if (!ValidateConsumableDescriptor(state, descriptor, out error))
                return false;

            var nextCheckAtSeconds =
                scheduledEvent.timestampSeconds + descriptor.CheckIntervalSeconds;
            if (InvalidTime(nextCheckAtSeconds) ||
                nextCheckAtSeconds <= scheduledEvent.timestampSeconds)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidConsumableDescriptor,
                    "Combat consumable check interval does not advance scheduler time.");
                return false;
            }

            if (!_consumableConditions.TryEvaluate(
                    descriptor.Condition,
                    session.hero,
                    out var conditionSatisfied,
                    out var conditionError))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.ConsumableConditionFailed,
                    conditionError ?? "Combat consumable condition evaluation failed.");
                return false;
            }

            if (!conditionSatisfied ||
                scheduledEvent.timestampSeconds < state.nextAllowedUseAtSeconds)
            {
                state.nextCheckAtSeconds = nextCheckAtSeconds;
                return TryScheduleConsumableCheck(
                    session,
                    nextCheckAtSeconds,
                    out error);
            }

            var nextAllowedUseAtSeconds =
                scheduledEvent.timestampSeconds + descriptor.CooldownSeconds;
            if (InvalidTime(nextAllowedUseAtSeconds) ||
                nextAllowedUseAtSeconds < scheduledEvent.timestampSeconds)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidConsumableDescriptor,
                    "Combat consumable cooldown produces an invalid timestamp.");
                return false;
            }

            if (!TryReserveSequence(session.scheduler, out var effectSequence))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.ConsumableProcessingFailed,
                    "Combat scheduler sequence is exhausted during consumable use.");
                return false;
            }

            var effectEventKey = $"{scheduledEvent.eventKey}:effect";
            var request = new CombatEffectRequest(
                effectEventKey,
                scheduledEvent.eventKey,
                scheduledEvent.timestampSeconds,
                effectSequence,
                CombatEffectSourceKind.Consumable,
                CombatActorSide.Hero,
                session.hero.combatantId,
                descriptor.ItemId,
                ConsumableAutoUseTrigger,
                session.hero.combatantId,
                descriptor.Effect);
            var quantityBefore = state.remainingQuantity;
            var quantityAfter = quantityBefore - 1;
            events.Add(new CombatConsumableUsedEvent(
                scheduledEvent.eventKey,
                scheduledEvent.timestampSeconds,
                scheduledEvent.sequence,
                state.itemId,
                state.sourceStackId,
                quantityBefore,
                quantityAfter,
                nextAllowedUseAtSeconds,
                session.hero.combatantId));
            events.Add(request);
            if (!_effectRuntime.TryApplyEffectRequest(
                    session,
                    request,
                    events,
                    out _,
                    out var mutation,
                    out error))
            {
                return false;
            }

            if (mutation != null &&
                !TryResolveTerminalBoundary(
                    session,
                    mutation,
                    rng,
                    events,
                    out var targetSurvived,
                    out error))
            {
                return false;
            }

            state.remainingQuantity = quantityAfter;
            state.nextAllowedUseAtSeconds = nextAllowedUseAtSeconds;
            state.nextCheckAtSeconds = nextCheckAtSeconds;
            state.lastAppliedEventKey = scheduledEvent.eventKey;
            terminal = session.simulationStopped || HasTerminalCandidate(session);
            if (terminal || state.remainingQuantity == 0)
                return true;

            return TryScheduleConsumableCheck(
                session,
                nextCheckAtSeconds,
                out error);
        }

        private bool ValidateConsumableDescriptor(
            CombatConsumableStateSaveData state,
            CombatConsumableDescriptor descriptor,
            out CombatAdvanceError error)
        {
            error = null;
            if (state == null ||
                descriptor == null ||
                !string.Equals(state.itemId, descriptor.ItemId, StringComparison.Ordinal) ||
                descriptor.UsePlace != CombatConsumableUsePlace.Combat ||
                descriptor.Condition == null ||
                !_consumableConditions.IsRegistered(
                    descriptor.Condition.Kind,
                    descriptor.Condition.Operator) ||
                InvalidTime(descriptor.CooldownSeconds) ||
                InvalidTime(descriptor.CheckIntervalSeconds) ||
                descriptor.CheckIntervalSeconds <= 0d ||
                !ValidateConsumableEffect(descriptor.Effect))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidConsumableDescriptor,
                    $"Combat consumable descriptor '{descriptor?.ItemId ?? "<null>"}' is invalid or unsupported.");
                return false;
            }

            return true;
        }

        private bool ValidateConsumableEffect(CombatEffectDescriptor effect)
        {
            if (effect == null || !_effectRuntime.IsEffectSupported(effect.Kind))
                return false;
            if (effect.Kind == CombatEffectKind.ApplyStatus)
                return !string.IsNullOrWhiteSpace(effect.StatusId);
            if (effect.Kind == CombatEffectKind.ModifyStat)
            {
                return !string.IsNullOrWhiteSpace(effect.StatId) &&
                       !InvalidNumber(effect.Value) &&
                       !InvalidTime(effect.DurationSeconds) &&
                       effect.DurationSeconds > 0d;
            }

            if (effect.Kind == CombatEffectKind.Damage ||
                effect.Kind == CombatEffectKind.Heal)
            {
                return !InvalidNumber(effect.Value) && effect.Value > 0d;
            }

            return true;
        }

        private static bool TryScheduleConsumableCheck(
            CombatSessionSaveData session,
            double timestampSeconds,
            out CombatAdvanceError error)
        {
            error = null;
            if (session?.scheduler?.scheduledEvents == null ||
                InvalidTime(timestampSeconds))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.ConsumableProcessingFailed,
                    "Consumable check requires a valid scheduler timestamp.");
                return false;
            }

            foreach (var value in session.scheduler.scheduledEvents)
            {
                if (string.Equals(
                        value?.eventType,
                        ConsumableCheckEventType,
                        StringComparison.Ordinal))
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.ConsumableProcessingFailed,
                        "Combat scheduler already contains a pending consumable check.");
                    return false;
                }
            }

            if (session.scheduler.scheduledEvents.Length >=
                CombatRuntimeSaveDataUtility.PersistentCollectionLimit)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.ConsumableProcessingFailed,
                    "Combat scheduler reached its bounded event limit.");
                return false;
            }

            if (!TryReserveSequence(session.scheduler, out var sequence))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.ConsumableProcessingFailed,
                    "Combat scheduler sequence is exhausted.");
                return false;
            }

            var scheduledEvent = new CombatScheduledEventSaveData
            {
                eventKey = $"{session.sessionId}:consumable:check:{sequence}",
                eventType = ConsumableCheckEventType,
                timestampSeconds = timestampSeconds,
                phasePriority = (int)CombatScheduledEventPhase.ConsumableCheck,
                actorSide = CombatActorSide.System,
                sequence = sequence
            };
            var expanded =
                new CombatScheduledEventSaveData[session.scheduler.scheduledEvents.Length + 1];
            Array.Copy(
                session.scheduler.scheduledEvents,
                expanded,
                session.scheduler.scheduledEvents.Length);
            expanded[expanded.Length - 1] = scheduledEvent;
            Array.Sort(expanded, CombatScheduledEventComparer.Instance);
            session.scheduler.scheduledEvents = expanded;
            return true;
        }

        private static bool TryScheduleInitialAttack(
            CombatSessionSaveData session,
            CombatActorSide side,
            double interval,
            out CombatAdvanceError error)
        {
            var combatant = GetCombatant(session, side);
            if (combatant.currentHp <= 0)
            {
                error = null;
                return true;
            }

            var timestamp = combatant.nextAttackAtSeconds > session.combatTimeSeconds
                ? combatant.nextAttackAtSeconds
                : session.combatTimeSeconds + interval;
            return TryScheduleAttack(session, side, timestamp, out error);
        }

        private static bool TryScheduleAttack(
            CombatSessionSaveData session,
            CombatActorSide side,
            double timestampSeconds,
            out CombatAdvanceError error)
        {
            error = null;
            if (InvalidTime(timestampSeconds) || timestampSeconds <= session.combatTimeSeconds)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidAttackCadence,
                    "Attack cadence does not advance combat time.");
                return false;
            }

            var scheduler = session.scheduler;
            if (scheduler.scheduledEvents.Length >= CombatRuntimeSaveDataUtility.PersistentCollectionLimit)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.ProcessingFailed,
                    "Combat scheduler reached its bounded event limit.");
                return false;
            }

            var sequence = scheduler.nextSequence;
            if (sequence == long.MaxValue)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.ProcessingFailed,
                    "Combat scheduler sequence is exhausted.");
                return false;
            }

            scheduler.nextSequence++;
            var scheduledEvent = new CombatScheduledEventSaveData
            {
                eventKey = $"{session.sessionId}:attack:{side.ToString().ToLowerInvariant()}:{sequence}",
                eventType = ActorAttackEventType,
                timestampSeconds = timestampSeconds,
                phasePriority = (int)CombatScheduledEventPhase.ActorAttack,
                actorSide = side,
                sequence = sequence
            };
            var pending = new CombatScheduledEventSaveData[scheduler.scheduledEvents.Length + 1];
            Array.Copy(scheduler.scheduledEvents, pending, scheduler.scheduledEvents.Length);
            pending[pending.Length - 1] = scheduledEvent;
            Array.Sort(pending, CombatScheduledEventComparer.Instance);
            scheduler.scheduledEvents = pending;
            GetCombatant(session, side).nextAttackAtSeconds = timestampSeconds;
            return true;
        }

        private static bool TryTakeNextDueEvent(
            CombatSchedulerStateSaveData scheduler,
            double targetCombatTimeSeconds,
            out CombatScheduledEventSaveData scheduledEvent)
        {
            scheduledEvent = null;
            if (scheduler.scheduledEvents.Length == 0)
                return false;

            Array.Sort(scheduler.scheduledEvents, CombatScheduledEventComparer.Instance);
            if (scheduler.scheduledEvents[0].timestampSeconds > targetCombatTimeSeconds)
                return false;

            scheduledEvent = scheduler.scheduledEvents[0];
            var remaining = new CombatScheduledEventSaveData[scheduler.scheduledEvents.Length - 1];
            if (remaining.Length > 0)
                Array.Copy(scheduler.scheduledEvents, 1, remaining, 0, remaining.Length);
            scheduler.scheduledEvents = remaining;
            return true;
        }

        private static bool HasPendingActorAttack(
            CombatSchedulerStateSaveData scheduler,
            CombatActorSide side)
        {
            foreach (var value in scheduler.scheduledEvents)
            {
                if (value != null &&
                    value.actorSide == side &&
                    string.Equals(value.eventType, ActorAttackEventType, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryHandleDefeatedCombatant(
            CombatSessionSaveData session,
            CombatScheduledEventSaveData deathEvent,
            double heroInterval,
            ICombatRng rng,
            List<CombatEvent> events,
            ref CombatActorDescriptor enemyDescriptor,
            ref double enemyInterval,
            out bool queueCompleted,
            out CombatAdvanceError error)
        {
            queueCompleted = false;
            error = null;
            if (session.hero.currentHp <= 0)
            {
                queueCompleted = true;
                return true;
            }

            if (session.currentEnemy.currentHp <= 0)
            {
                return TryAdvanceEnemyQueue(
                    session,
                    deathEvent,
                    heroInterval,
                    rng,
                    events,
                    ref enemyDescriptor,
                    ref enemyInterval,
                    out queueCompleted,
                    out error);
            }

            return true;
        }

        private bool TryAdvanceEnemyQueue(
            CombatSessionSaveData session,
            CombatScheduledEventSaveData deathEvent,
            double heroInterval,
            ICombatRng rng,
            List<CombatEvent> events,
            ref CombatActorDescriptor enemyDescriptor,
            ref double enemyInterval,
            out bool queueCompleted,
            out CombatAdvanceError error)
        {
            queueCompleted = false;
            error = null;
            var queue = session.enemyQueue ?? Array.Empty<CombatEnemyQueueEntrySaveData>();
            if (queue.Length == 0)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidEnemyQueue,
                    "Running combat cannot resolve an empty enemy queue.");
                return false;
            }

            if (!string.Equals(session.combatMode, CombatEnemyQueueBuilder.Queue1V1Mode, StringComparison.Ordinal))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.UnsupportedCombatMode,
                    $"Combat mode '{session.combatMode ?? "<null>"}' cannot advance an enemy queue.");
                return false;
            }

            if (session.queuePosition < 0 ||
                session.queuePosition >= queue.Length ||
                session.currentEnemy == null ||
                session.currentEnemy.currentHp > 0)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidEnemyQueue,
                    "Current enemy death does not match the saved queue position.");
                return false;
            }

            var currentEntry = queue[session.queuePosition];
            if (currentEntry == null ||
                currentEntry.queueIndex != session.queuePosition ||
                !string.Equals(
                    currentEntry.combatantId,
                    session.currentEnemy.combatantId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    currentEntry.enemyId,
                    session.currentEnemy.definitionId,
                    StringComparison.Ordinal))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidEnemyQueue,
                    "Current enemy does not match the saved queue position.");
                return false;
            }

            if (!TryApplyEnemyReward(
                    session,
                    currentEntry,
                    rng,
                    out error))
                return false;

            _statusRuntime?.RemoveScheduledEventsForCombatant(
                session.scheduler,
                session.currentEnemy.combatantId);
            RemovePendingActorAttacks(session.scheduler, CombatActorSide.Enemy);
            var nextPosition = session.queuePosition + 1;
            if (nextPosition >= queue.Length)
            {
                RemovePendingActorAttacks(session.scheduler);
                RemovePendingConsumableChecks(session.scheduler);
                session.queuePosition = queue.Length;
                session.currentEnemy = null;
                session.simulationStopped = true;
                var candidateId = $"{session.sessionId}:victory";
                var candidateEventKey =
                    $"{deathEvent.eventKey}:terminal:victory";
                session.terminalCandidate = new CombatTerminalCandidateSaveData
                {
                    candidateId = candidateId,
                    kind = CombatTerminalCandidateKinds.Victory,
                    eventKey = candidateEventKey,
                    createdAtSeconds = deathEvent.timestampSeconds
                };
                session.combatTimeSeconds = deathEvent.timestampSeconds;
                if (!TryReserveSequence(session.scheduler, out var sequence))
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.ProcessingFailed,
                        "Combat scheduler sequence is exhausted during victory candidate creation.");
                    return false;
                }
                events.Add(new CombatTerminalCandidateCreatedEvent(
                    candidateEventKey,
                    deathEvent.timestampSeconds,
                    sequence,
                    currentEntry.combatantId,
                    null,
                    candidateId,
                    CombatTerminalCandidateKinds.Victory,
                    0));
                queueCompleted = true;
                return true;
            }

            var entry = queue[nextPosition];
            if (entry == null || entry.queueIndex != nextPosition)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidEnemyQueue,
                    "Next enemy entry does not match its saved queue position.");
                return false;
            }

            if (_enemyQueue == null)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.EnemyStateCreationFailed,
                    "Combat runtime has no enemy queue provider for the next queue entry.");
                return false;
            }

            if (!_enemyQueue.TryCreateEnemyState(entry, out var nextEnemy, out var providerError) ||
                nextEnemy == null ||
                nextEnemy.currentHp <= 0 ||
                nextEnemy.currentHp != nextEnemy.maxHp ||
                !string.Equals(nextEnemy.combatantId, entry?.combatantId, StringComparison.Ordinal) ||
                !string.Equals(nextEnemy.definitionId, entry?.enemyId, StringComparison.Ordinal))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.EnemyStateCreationFailed,
                    providerError ?? "The next enemy state is invalid.");
                return false;
            }

            if (!TryResolveDescriptor(nextEnemy, CombatActorSide.Enemy, out var nextDescriptor, out error) ||
                !TryResolveAttackInterval(nextDescriptor, out var nextEnemyInterval, out error))
            {
                return false;
            }

            session.queuePosition = nextPosition;
            session.currentEnemy = nextEnemy;
            enemyDescriptor = nextDescriptor;
            enemyInterval = nextEnemyInterval;
            if (!TryDispatchBattleStart(
                    session,
                    CombatActorSide.Enemy,
                    deathEvent.timestampSeconds,
                     rng,
                     events,
                     out _,
                     out var nextEnemyBattleStartTerminal,
                     out error))
            {
                return false;
            }

            if (nextEnemyBattleStartTerminal != null)
            {
                return TryHandleDefeatedCombatant(
                    session,
                    ToScheduledEvent(nextEnemyBattleStartTerminal),
                    heroInterval,
                    rng,
                    events,
                    ref enemyDescriptor,
                    ref enemyInterval,
                    out queueCompleted,
                    out error);
            }

            if (!HasPendingActorAttack(session.scheduler, CombatActorSide.Hero))
            {
                var nextHeroAttack = deathEvent.timestampSeconds + heroInterval;
                if (nextHeroAttack <= deathEvent.timestampSeconds ||
                    !TryScheduleAttack(session, CombatActorSide.Hero, nextHeroAttack, out error))
                {
                    error ??= new CombatAdvanceError(
                        CombatAdvanceErrorCode.InvalidAttackCadence,
                        "Hero attack cadence did not continue across the enemy transition.");
                    return false;
                }
            }

            var firstEnemyAttack = deathEvent.timestampSeconds + nextEnemyInterval;
            if (firstEnemyAttack <= deathEvent.timestampSeconds ||
                !TryScheduleAttack(session, CombatActorSide.Enemy, firstEnemyAttack, out error))
            {
                error ??= new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidAttackCadence,
                    "The next enemy did not receive a full initial attack cooldown.");
                return false;
            }

            return true;
        }

        private bool TryApplyEnemyReward(
            CombatSessionSaveData session,
            CombatEnemyQueueEntrySaveData queueEntry,
            ICombatRng rng,
            out CombatAdvanceError error)
        {
            error = null;
            if (_enemyRewards == null)
                return true;
            var operationKey =
                $"{session.sessionId}:enemy-reward:{queueEntry.queueIndex}:{queueEntry.combatantId}";
            if (string.Equals(
                    session.lastEnemyRewardOperation?.operationKey,
                    operationKey,
                    StringComparison.Ordinal))
                return true;
            if (!_enemyRewards.TryResolve(
                    queueEntry.enemyId,
                    rng,
                    out var reward,
                    out var providerError) ||
                reward == null || reward.EnemyExp < 0)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.EnemyRewardFailed,
                    providerError ?? "Enemy reward provider failed.");
                return false;
            }

            var existing = session.loot ??
                           Array.Empty<CombatRewardEntrySaveData>();
            var added = reward.Loot ??
                        Array.Empty<CombatRewardEntrySaveData>();
            if (existing.Length + added.Length >
                CombatRuntimeSaveDataUtility.PersistentCollectionLimit)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.EnemyRewardFailed,
                    "Combat loot exceeds its bounded retention limit.");
                return false;
            }
            if (reward.EnemyExp > long.MaxValue - session.accumulatedEnemyExp)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.EnemyRewardFailed,
                    "Accumulated enemy EXP exceeds Int64.");
                return false;
            }

            var combined =
                new CombatRewardEntrySaveData[existing.Length + added.Length];
            Array.Copy(existing, combined, existing.Length);
            for (var index = 0; index < added.Length; index++)
            {
                var value = added[index];
                if (value == null || string.IsNullOrWhiteSpace(value.rewardType) ||
                    string.IsNullOrWhiteSpace(value.targetId) ||
                    value.quantity <= 0 || value.quality < 0)
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.EnemyRewardFailed,
                        "Enemy reward provider returned an invalid loot entry.");
                    return false;
                }
                combined[existing.Length + index] =
                    new CombatRewardEntrySaveData
                    {
                        entryId = $"{operationKey}:loot:{index}",
                        sortOrder = existing.Length + index,
                        rewardType = value.rewardType,
                        targetId = value.targetId,
                        quantity = value.quantity,
                        origin = PendingResultOrigin.CombatLoot,
                        quality = value.quality,
                        instanceId = value.instanceId
                    };
            }

            session.loot = combined;
            session.accumulatedEnemyExp += reward.EnemyExp;
            session.lastEnemyRewardOperation =
                new CombatEnemyRewardOperationSaveData
                {
                    operationKey = operationKey,
                    combatantId = queueEntry.combatantId,
                    enemyId = queueEntry.enemyId,
                    queueIndex = queueEntry.queueIndex,
                    enemyExp = reward.EnemyExp
                };
            return true;
        }

        private static void RemovePendingActorAttacks(CombatSchedulerStateSaveData scheduler)
        {
            RemovePendingActorAttacks(scheduler, null);
        }

        private static void RemovePendingActorAttacks(
            CombatSchedulerStateSaveData scheduler,
            CombatActorSide? side)
        {
            var retained = new List<CombatScheduledEventSaveData>(scheduler.scheduledEvents.Length);
            foreach (var value in scheduler.scheduledEvents)
            {
                if (!string.Equals(value.eventType, ActorAttackEventType, StringComparison.Ordinal) ||
                    (side.HasValue && value.actorSide != side.Value))
                {
                    retained.Add(value);
                }
            }

            scheduler.scheduledEvents = retained.ToArray();
        }

        private static void RemovePendingConsumableChecks(
            CombatSchedulerStateSaveData scheduler)
        {
            var retained = new List<CombatScheduledEventSaveData>(
                scheduler?.scheduledEvents?.Length ?? 0);
            foreach (var value in scheduler?.scheduledEvents ??
                                  Array.Empty<CombatScheduledEventSaveData>())
            {
                if (!string.Equals(
                        value?.eventType,
                        ConsumableCheckEventType,
                        StringComparison.Ordinal))
                {
                    retained.Add(value);
                }
            }

            if (scheduler != null)
                scheduler.scheduledEvents = retained.ToArray();
        }

        private static bool TryResolveAttack(
            CombatScheduledEventSaveData scheduledEvent,
            CombatantStateSaveData attacker,
            CombatantStateSaveData target,
            CombatActorDescriptor attackerDescriptor,
            CombatActorDescriptor targetDescriptor,
            double damageModifierPercent,
            ICombatRng rng,
            List<CombatEvent> events,
            out bool attackHit,
            out CombatHpMutation mutation,
            out CombatAdvanceError error)
        {
            attackHit = false;
            mutation = null;
            error = null;
            events.Add(new CombatAttackEvent(
                scheduledEvent.eventKey,
                scheduledEvent.timestampSeconds,
                scheduledEvent.sequence,
                scheduledEvent.actorSide,
                attacker.combatantId,
                target.combatantId));

            var dodgeRoll = RollPercent(rng);
            if (dodgeRoll < targetDescriptor.DodgeChancePercent)
            {
                events.Add(new CombatDodgeEvent(
                    scheduledEvent.eventKey,
                    scheduledEvent.timestampSeconds,
                    scheduledEvent.sequence,
                    scheduledEvent.actorSide,
                    attacker.combatantId,
                    target.combatantId,
                    dodgeRoll));
                return true;
            }

            var baseDamage = RollInclusive(rng, attackerDescriptor.DamageMin, attackerDescriptor.DamageMax);
            var critRoll = RollPercent(rng);
            var critical = critRoll < attackerDescriptor.CritChancePercent;
            if (!targetDescriptor.TryGetResistancePercent(attackerDescriptor.DamageType, out var resistancePercent))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidDescriptor,
                    $"Damage type '{attackerDescriptor.DamageType}' has no corresponding resistance.");
                return false;
            }

            var afterModifier = baseDamage * (1d + damageModifierPercent / 100d);
            var afterCrit = critical
                ? afterModifier * attackerDescriptor.CritDamageMultiplier
                : afterModifier;
            var afterResistance = afterCrit * (1d - resistancePercent / 100d);
            if (InvalidNumber(afterResistance))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.ProcessingFailed,
                    "Combat damage calculation produced a non-finite value.");
                return false;
            }

            var rounded = Math.Ceiling(afterResistance);
            var damage = rounded >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)rounded);
            var hpBefore = target.currentHp;
            var rawHpAfter = (long)hpBefore - damage;
            target.currentHp = (int)Math.Max(0L, rawHpAfter);
            var damageEvent = new CombatDamageEvent(
                scheduledEvent.eventKey,
                scheduledEvent.timestampSeconds,
                scheduledEvent.sequence,
                scheduledEvent.actorSide,
                attacker.combatantId,
                target.combatantId,
                dodgeRoll,
                baseDamage,
                critRoll,
                critical,
                resistancePercent,
                damage,
                hpBefore,
                rawHpAfter,
                target.currentHp);
            events.Add(damageEvent);
            mutation = new CombatHpMutation(
                scheduledEvent.eventKey,
                null,
                scheduledEvent.timestampSeconds,
                scheduledEvent.sequence,
                scheduledEvent.actorSide,
                attacker.combatantId,
                Opposite(scheduledEvent.actorSide),
                target.combatantId,
                hpBefore,
                rawHpAfter,
                target.currentHp,
                damageEvent.SetTargetHpAfter);
            attackHit = true;
            return true;
        }

        private bool TryDispatchBattleStart(
            CombatSessionSaveData session,
            CombatActorSide ownerSide,
            double timestampSeconds,
            ICombatRng rng,
            List<CombatEvent> events,
            out bool stateChanged,
            out CombatHpMutation terminalMutation,
            out CombatAdvanceError error)
        {
            var owner = GetCombatant(session, ownerSide);
            var triggerEventKey =
                $"{session.sessionId}:battle-start:{owner?.combatantId ?? ownerSide.ToString().ToLowerInvariant()}";
            return TryDispatchAndApplyEffects(
                session,
                ownerSide,
                CombatAbilityTriggers.OnBattleStart,
                triggerEventKey,
                timestampSeconds,
                rng,
                events,
                out stateChanged,
                out _,
                out terminalMutation,
                out error);
        }

        private bool TryDispatchAndApplyEffects(
            CombatSessionSaveData session,
            CombatActorSide ownerSide,
            string trigger,
            string triggerEventKey,
            double timestampSeconds,
            ICombatRng rng,
            List<CombatEvent> events,
            out bool stateChanged,
            out bool hpMutated,
            out CombatHpMutation terminalMutation,
            out CombatAdvanceError error)
        {
            stateChanged = false;
            hpMutated = false;
            terminalMutation = null;
            error = null;
            var effectStateChanged = false;
            var effectHpMutated = false;
            CombatHpMutation resolvedTerminalMutation = null;

            bool TryHandleRequest(
                CombatEffectRequest request,
                out bool continueDispatch,
                out CombatAdvanceError requestError)
            {
                continueDispatch = true;
                requestError = null;
                if (!_effectRuntime.TryApplyEffectRequest(
                        session,
                        request,
                        events,
                        out var requestStateChanged,
                        out var mutation,
                        out requestError))
                {
                    return false;
                }

                effectStateChanged |= requestStateChanged;
                effectHpMutated |= mutation != null;
                if (mutation == null)
                    return true;
                if (!TryResolveTerminalBoundary(
                        session,
                        mutation,
                        rng,
                        events,
                        out var targetSurvived,
                        out requestError))
                {
                    return false;
                }

                if (targetSurvived)
                    return true;
                resolvedTerminalMutation = mutation;
                continueDispatch = false;
                return true;
            }

            if (!_abilityDispatcher.TryDispatch(
                    session,
                    ownerSide,
                    trigger,
                    triggerEventKey,
                    timestampSeconds,
                    rng,
                    events,
                    TryHandleRequest,
                    out var dispatchStateChanged,
                    out error))
            {
                return false;
            }

            stateChanged = dispatchStateChanged || effectStateChanged;
            hpMutated = effectHpMutated;
            terminalMutation = resolvedTerminalMutation;
            return true;
        }

        private bool TryResolveTerminalBoundary(
            CombatSessionSaveData session,
            CombatHpMutation mutation,
            ICombatRng rng,
            List<CombatEvent> events,
            out bool targetSurvived,
            out CombatAdvanceError error)
        {
            targetSurvived = false;
            error = null;
            var target = GetCombatant(session, mutation?.TargetSide ?? CombatActorSide.System);
            if (session?.scheduler == null ||
                mutation == null ||
                target == null ||
                rng == null ||
                events == null ||
                !string.Equals(
                    target.combatantId,
                    mutation.TargetCombatantId,
                    StringComparison.Ordinal))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.DeathPreventionFailed,
                    "Terminal boundary requires a matching combatant, scheduler, mutation, RNG and event sink.");
                return false;
            }

            var normalizedHp = mutation.RawHpAfter <= 0L
                ? 0
                : (int)Math.Min(target.maxHp, mutation.RawHpAfter);
            target.currentHp = normalizedHp;
            mutation.SetFinalHp(normalizedHp);

            if (HasTerminalCandidate(session))
            {
                target.currentHp = 0;
                mutation.SetFinalHp(0);
                session.simulationStopped = true;
                session.combatTimeSeconds = mutation.TimestampSeconds;
                session.scheduler.scheduledEvents = Array.Empty<CombatScheduledEventSaveData>();
                return true;
            }

            var prevented = false;
            if (mutation.RawHpAfter < 0L &&
                !_deathPrevention.TryResolve(
                    session,
                    target,
                    mutation,
                    rng,
                    events,
                    out prevented,
                    out error))
            {
                return false;
            }

            if (mutation.RawHpAfter < 0L && prevented)
            {
                targetSurvived = true;
                return true;
            }

            if (target.currentHp > 0)
            {
                targetSurvived = true;
                return true;
            }

            target.currentHp = 0;
            mutation.SetFinalHp(0);
            if (mutation.TargetSide == CombatActorSide.Enemy)
            {
                _statusRuntime?.RemoveScheduledEventsForCombatant(
                    session.scheduler,
                    target.combatantId);
                RemovePendingActorAttacks(session.scheduler, CombatActorSide.Enemy);
                return true;
            }

            if (mutation.TargetSide != CombatActorSide.Hero)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.DeathPreventionFailed,
                    "Terminal boundary target must be a hero or enemy.");
                return false;
            }

            var candidateEventKey =
                $"{mutation.SourceEventKey}:terminal:defeat:{target.combatantId}";
            var candidateId = $"{session.sessionId}:defeat";
            session.terminalCandidate = new CombatTerminalCandidateSaveData
            {
                candidateId = candidateId,
                eventKey = candidateEventKey,
                kind = CombatTerminalCandidateKinds.Defeat,
                createdAtSeconds = mutation.TimestampSeconds
            };
            session.simulationStopped = true;
            session.combatTimeSeconds = mutation.TimestampSeconds;
            session.scheduler.scheduledEvents = Array.Empty<CombatScheduledEventSaveData>();
            if (!TryReserveSequence(session.scheduler, out var sequence))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.DeathPreventionFailed,
                    "Combat scheduler sequence is exhausted during terminal candidate creation.");
                return false;
            }

            events.Add(new CombatTerminalCandidateCreatedEvent(
                candidateEventKey,
                mutation.TimestampSeconds,
                sequence,
                target.combatantId,
                mutation.SourceEffectId,
                candidateId,
                CombatTerminalCandidateKinds.Defeat,
                0));
            return true;
        }

        private static CombatScheduledEventSaveData ToScheduledEvent(CombatHpMutation mutation)
        {
            return new CombatScheduledEventSaveData
            {
                eventKey = mutation.SourceEventKey,
                eventType = "inline_hp_mutation",
                timestampSeconds = mutation.TimestampSeconds,
                phasePriority = (int)CombatScheduledEventPhase.ActorAttack,
                actorSide = mutation.SourceSide,
                sequence = mutation.Sequence,
                subjectCombatantId = mutation.TargetCombatantId,
                effectInstanceId = mutation.SourceEffectId
            };
        }

        private static bool TryReserveSequence(
            CombatSchedulerStateSaveData scheduler,
            out long sequence)
        {
            sequence = scheduler.nextSequence;
            if (sequence == long.MaxValue)
                return false;
            scheduler.nextSequence++;
            return true;
        }

        private static bool HasTerminalCandidate(CombatSessionSaveData session)
        {
            return !string.IsNullOrWhiteSpace(session?.terminalCandidate?.candidateId);
        }

        private static CombatantStateSaveData GetCombatant(
            CombatSessionSaveData session,
            CombatActorSide side)
        {
            return side == CombatActorSide.Hero ? session.hero : session.currentEnemy;
        }

        private static CombatActorSide Opposite(CombatActorSide side)
        {
            return side == CombatActorSide.Hero ? CombatActorSide.Enemy : CombatActorSide.Hero;
        }

        private static double RollPercent(ICombatRng rng)
        {
            return (rng.NextUInt64() >> 11) * PercentScale;
        }

        private static int RollInclusive(ICombatRng rng, int minimum, int maximum)
        {
            var range = (ulong)((long)maximum - minimum + 1L);
            var threshold = unchecked(0UL - range) % range;
            ulong value;
            do
            {
                value = rng.NextUInt64();
            } while (value < threshold);

            return (int)(minimum + (long)(value % range));
        }

        private static bool ValidPercent(double value)
        {
            return !InvalidNumber(value) && value >= 0d && value <= 100d;
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
