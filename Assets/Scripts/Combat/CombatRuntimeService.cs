using System;
using System.Collections.Generic;
using System.Globalization;

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
        ActorAttack = 100
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
        EffectProcessingFailed = 25
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
            DamageModifierStatId = damageModifierStatId;
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
            TargetHpAfter = targetHpAfter;
        }

        public double DodgeRollPercent { get; }
        public int BaseDamage { get; }
        public double CritRollPercent { get; }
        public bool Critical { get; }
        public double ResistancePercent { get; }
        public int Damage { get; }
        public int TargetHpBefore { get; }
        public int TargetHpAfter { get; }
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
        private const double PercentScale = 100d / 9007199254740992d;

        private readonly ICombatRuntimeStore _store;
        private readonly ICombatDescriptorProvider _descriptors;
        private readonly ICombatRngFactory _rngFactory;
        private readonly ICombatEnemyQueueProvider _enemyQueue;
        private readonly CombatAbilityDispatcher _abilityDispatcher;
        private readonly CombatStatusRuntime _statusRuntime;

        public CombatRuntimeService(
            ICombatRuntimeStore store,
            ICombatDescriptorProvider descriptors,
            ICombatRngFactory rngFactory = null,
            ICombatEnemyQueueProvider enemyQueue = null,
            ICombatAbilityDescriptorProvider abilities = null,
            CombatAbilityRegistry abilityRegistry = null,
            ICombatStatusDescriptorProvider statuses = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _descriptors = descriptors ?? throw new ArgumentNullException(nameof(descriptors));
            _rngFactory = rngFactory ?? new CombatRngFactory();
            _enemyQueue = enemyQueue;
            _abilityDispatcher = new CombatAbilityDispatcher(
                abilities ?? EmptyCombatAbilityDescriptorProvider.Instance,
                abilityRegistry ?? new CombatAbilityRegistry());
            _statusRuntime = statuses == null ? null : new CombatStatusRuntime(statuses);
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

            if (targetCombatTimeSeconds < currentTime)
            {
                return CombatAdvanceResult.Failed(
                    CombatAdvanceErrorCode.InvalidRequest,
                    "Combat time cannot move backwards.",
                    currentTime);
            }

            if (stored.session.simulationStopped)
            {
                return CombatAdvanceResult.Failed(
                    CombatAdvanceErrorCode.SimulationStopped,
                    "Combat simulation is stopped.",
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
                scheduler.scheduledEvents ??= Array.Empty<CombatScheduledEventSaveData>();
                if (!TryDispatchBattleStart(
                        aggregate.session,
                        CombatActorSide.Hero,
                        aggregate.session.combatTimeSeconds,
                        rng,
                        events,
                        out var heroAbilityStateChanged,
                        out error) ||
                    !TryDispatchBattleStart(
                        aggregate.session,
                        CombatActorSide.Enemy,
                        aggregate.session.combatTimeSeconds,
                        rng,
                        events,
                        out var enemyAbilityStateChanged,
                        out error))
                {
                    return CombatAdvanceResult.Failed(error, currentTime);
                }

                if (!heroAbilityStateChanged &&
                    !enemyAbilityStateChanged &&
                    CanReturnWithoutAdvance(aggregate.session, targetCombatTimeSeconds))
                {
                    return CombatAdvanceResult.Succeeded(currentTime, events);
                }

                if (aggregate.session.hero.currentHp > 0 &&
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

                while (TryTakeNextDueEvent(scheduler, targetCombatTimeSeconds, out var scheduledEvent))
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
                                out var statusHpMutated,
                                out error))
                        {
                            return CombatAdvanceResult.Failed(error, currentTime);
                        }

                        if (statusHpMutated &&
                            (aggregate.session.hero.currentHp <= 0 ||
                             aggregate.session.currentEnemy.currentHp <= 0))
                        {
                            if (!TryHandleDefeatedCombatant(
                                    aggregate.session,
                                    scheduledEvent,
                                    heroInterval,
                                    rng,
                                    events,
                                    ref enemy,
                                    ref enemyInterval,
                                    out var queueCompleted,
                                    out error))
                            {
                                return CombatAdvanceResult.Failed(error, currentTime);
                            }

                            if (queueCompleted)
                            {
                                resolvedCombatTime = scheduledEvent.timestampSeconds;
                                break;
                            }
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
                            out error))
                    {
                        return CombatAdvanceResult.Failed(error, currentTime);
                    }

                    attacker.lastAttackEventKey = scheduledEvent.eventKey;
                    var effectHpMutated = false;
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
                            out effectHpMutated,
                            out error))
                    {
                        return CombatAdvanceResult.Failed(error, currentTime);
                    }

                    if (attacker.currentHp <= 0 || target.currentHp <= 0 || effectHpMutated)
                    {
                        if (aggregate.session.hero.currentHp <= 0 ||
                            aggregate.session.currentEnemy.currentHp <= 0)
                        {
                            if (!TryHandleDefeatedCombatant(
                                    aggregate.session,
                                    scheduledEvent,
                                    heroInterval,
                                    rng,
                                    events,
                                    ref enemy,
                                    ref enemyInterval,
                                    out var queueCompleted,
                                    out error))
                            {
                                return CombatAdvanceResult.Failed(error, currentTime);
                            }

                            if (queueCompleted)
                            {
                                resolvedCombatTime = scheduledEvent.timestampSeconds;
                                break;
                            }
                        }
                        else if (attacker.currentHp > 0 && target.currentHp > 0)
                        {
                            var interval = scheduledEvent.actorSide == CombatActorSide.Hero
                                ? heroInterval
                                : enemyInterval;
                            if (scheduledEvent.timestampSeconds + interval <=
                                scheduledEvent.timestampSeconds)
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
                    else
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
                if (!_store.UpdateCombatAggregate(aggregate))
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
                RemovePendingActorAttacks(session.scheduler);
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
                RemovePendingActorAttacks(session.scheduler);
                return true;
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

            _statusRuntime?.RemoveScheduledEventsForCombatant(
                session.scheduler,
                session.currentEnemy.combatantId);
            RemovePendingActorAttacks(session.scheduler, CombatActorSide.Enemy);
            var nextPosition = session.queuePosition + 1;
            if (nextPosition >= queue.Length)
            {
                RemovePendingActorAttacks(session.scheduler);
                session.queuePosition = queue.Length;
                session.currentEnemy = null;
                session.simulationStopped = true;
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
                    out error))
            {
                return false;
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
            out CombatAdvanceError error)
        {
            attackHit = false;
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
            target.currentHp = Math.Max(0, target.currentHp - damage);
            events.Add(new CombatDamageEvent(
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
                target.currentHp));
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
            out CombatAdvanceError error)
        {
            stateChanged = false;
            hpMutated = false;
            error = null;
            var firstEventIndex = events.Count;
            if (!_abilityDispatcher.TryDispatch(
                    session,
                    ownerSide,
                    trigger,
                    triggerEventKey,
                    timestampSeconds,
                    rng,
                    events,
                    out stateChanged,
                    out error))
            {
                return false;
            }

            if (_statusRuntime == null)
                return true;

            var requestCount = events.Count - firstEventIndex;
            for (var offset = 0; offset < requestCount; offset++)
            {
                if (!(events[firstEventIndex + offset] is CombatEffectRequest request))
                    continue;
                if (!_statusRuntime.TryApplyEffectRequest(
                        session,
                        request,
                        events,
                        out var effectStateChanged,
                        out var effectHpMutated,
                        out error))
                {
                    return false;
                }

                stateChanged |= effectStateChanged;
                hpMutated |= effectHpMutated;
            }

            return true;
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
