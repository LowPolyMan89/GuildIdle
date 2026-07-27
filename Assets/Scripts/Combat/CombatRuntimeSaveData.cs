using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildIdle.Combat
{
    public enum CombatExecutionStatus
    {
        None = 0,
        Running = 1,
        ResultPending = 2,
        Completed = 3
    }

    public enum CombatLoadoutKind
    {
        None = 0,
        Empty = 1,
        Consumable = 2
    }

    [Serializable]
    public sealed class CombatRuntimeSaveData
    {
        public CombatExecutionSaveData[] executions = Array.Empty<CombatExecutionSaveData>();
        public CombatSessionSaveData[] sessions = Array.Empty<CombatSessionSaveData>();
    }

    [Serializable]
    public sealed class CombatRuntimeAggregate
    {
        public CombatExecutionSaveData execution;
        public CombatSessionSaveData session;
    }

    [Serializable]
    public sealed class CombatExecutionSaveData
    {
        public string executionId;
        public string sessionId;
        public string sourceActivityId;
        public string sourceExecutionId;
        public string sourceRequestId;
        public string occupationOwnerId;
        public string heroId;
        public string startOperationId;
        public string startFingerprint;
        public CombatExecutionStatus status = CombatExecutionStatus.Running;
        public string outcome;
        public bool outcomeFinalized;
        public bool resultCreated;
        public bool pendingResultResolved;
        public bool completionPublished;
        public bool failurePublished;
        public string pendingResultId;
        public long resultSourceSequence;
        public long startedAtUnixSeconds;
        public long completedAtUnixSeconds;
    }

    [Serializable]
    public sealed class CombatSessionSaveData
    {
        public string sessionId;
        public string executionId;
        public string enemyGroupId;
        public string combatMode;
        public CombatEnemyQueueEntrySaveData[] enemyQueue = Array.Empty<CombatEnemyQueueEntrySaveData>();
        public int queuePosition;
        public CombatantStateSaveData hero;
        public CombatantStateSaveData currentEnemy;
        public double combatTimeSeconds;
        public CombatSchedulerStateSaveData scheduler = new CombatSchedulerStateSaveData();
        public CombatRngStateSaveData rng = new CombatRngStateSaveData();
        public CombatRewardEntrySaveData[] loot = Array.Empty<CombatRewardEntrySaveData>();
        public long accumulatedEnemyExp;
        public string enemyExpTargetId;
        public CombatRewardEntrySaveData[] completionRewards = Array.Empty<CombatRewardEntrySaveData>();
        public bool completionRewardsSnapshotCreated;
        public CombatEnemyRewardOperationSaveData lastEnemyRewardOperation;
        public CombatRewardEntrySaveData[] outcomeRewards = Array.Empty<CombatRewardEntrySaveData>();
        public CombatDefeatLossSaveData defeatLoss;
        public CombatLoadoutKind loadoutKind;
        public CombatConsumableStateSaveData broughtConsumable;
        public CombatDeathPreventionOperationSaveData lastDeathPreventionOperation;
        public CombatTerminalCandidateSaveData terminalCandidate;
        public bool simulationStopped;
    }

    [Serializable]
    public sealed class CombatEnemyQueueEntrySaveData
    {
        public string combatantId;
        public string enemyId;
        public int level;
        public int queueIndex;
    }

    [Serializable]
    public sealed class CombatantStateSaveData
    {
        public string combatantId;
        public string definitionId;
        public int currentHp;
        public int maxHp;
        public double nextAttackAtSeconds;
        public string lastAttackEventKey;
        public CombatAbilityCooldownSaveData[] abilityCooldowns = Array.Empty<CombatAbilityCooldownSaveData>();
        public CombatStatusInstanceSaveData[] statuses = Array.Empty<CombatStatusInstanceSaveData>();
        public CombatTemporaryModifierSaveData[] independentModifiers = Array.Empty<CombatTemporaryModifierSaveData>();
    }

    [Serializable]
    public sealed class CombatAbilityCooldownSaveData
    {
        public string abilityId;
        public double nextReadyAtSeconds;
        public string lastTriggerEventKey;
        // Deterministic basis-point roll in the inclusive range 1..10000.
        public int lastChanceRoll;
        public bool lastChanceResolved;
    }

    [Serializable]
    public sealed class CombatStatusInstanceSaveData
    {
        public string statusInstanceId;
        public string statusId;
        public string sourceCombatantId;
        public string[] stackIds = Array.Empty<string>();
        public double expiresAtSeconds;
        public double nextTickAtSeconds;
        public string lastApplyEventKey;
        public string lastTickEventKey;
    }

    [Serializable]
    public sealed class CombatTemporaryModifierSaveData
    {
        public string modifierInstanceId;
        public string sourceId;
        public string statId;
        public string operation;
        public float value;
        public double expiresAtSeconds;
        public string appliedEventKey;
    }

    [Serializable]
    public sealed class CombatSchedulerStateSaveData
    {
        public long nextSequence;
        public string lastResolvedEventKey;
        // Invariant: this bounded collection contains pending events only. A successfully resolved
        // event is removed before the aggregate is saved. lastResolvedEventKey only guards against
        // replay of the most recently resolved event; future event types must provide any broader
        // deduplication they require instead of treating it as a complete resolved-event history.
        public CombatScheduledEventSaveData[] scheduledEvents = Array.Empty<CombatScheduledEventSaveData>();
    }

    [Serializable]
    public sealed class CombatScheduledEventSaveData
    {
        public string eventKey;
        public string eventType;
        public double timestampSeconds;
        public int phasePriority;
        public CombatActorSide actorSide;
        public long sequence;
        public string subjectCombatantId;
        public string effectInstanceId;
    }

    [Serializable]
    public sealed class CombatRngStateSaveData
    {
        public string algorithmId;
        public int formatVersion;
        public string state;
        public long drawCount;
    }

    [Serializable]
    public sealed class CombatRewardEntrySaveData
    {
        public string entryId;
        public int sortOrder;
        public string rewardType;
        public string targetId;
        public long quantity;
        public string origin;
        public int quality;
        public string instanceId;
    }

    [Serializable]
    public sealed class CombatConsumableStateSaveData
    {
        public string sourceStackId;
        [SerializeField, HideInInspector]
        private string originStackId;
        public string itemId;
        public int initialQuantity;
        public int remainingQuantity;
        public double nextCheckAtSeconds;
        public double nextAllowedUseAtSeconds;
        public string lastAppliedEventKey;

        internal string ResolveSourceStackId()
        {
            return string.IsNullOrWhiteSpace(sourceStackId)
                ? originStackId
                : sourceStackId;
        }

        internal bool HasLegacySourceStackId => !string.IsNullOrWhiteSpace(originStackId);
    }

    [Serializable]
    public sealed class CombatTerminalCandidateSaveData
    {
        public string candidateId;
        public string kind;
        public string eventKey;
        public double createdAtSeconds;
    }

    [Serializable]
    public sealed class CombatDeathPreventionOperationSaveData
    {
        public string operationKey;
        public string targetCombatantId;
        public string effectId;
        // Deterministic basis-point roll in the inclusive range 1..10000.
        public int chanceRoll;
        public bool successful;
    }

    [Serializable]
    public sealed class CombatEnemyRewardOperationSaveData
    {
        public string operationKey;
        public string combatantId;
        public string enemyId;
        public int queueIndex;
        public long enemyExp;
        public bool legacyEmptyQueue;
    }

    [Serializable]
    public sealed class CombatDefeatLossSaveData
    {
        public int lossPercent;
        public CombatDefeatLossEntrySaveData[] entries =
            Array.Empty<CombatDefeatLossEntrySaveData>();
    }

    [Serializable]
    public sealed class CombatDefeatLossEntrySaveData
    {
        public string origin;
        public string rewardType;
        public string targetId;
        public int quality;
        public string instanceId;
        public long quantityBefore;
        public long quantityLost;
        public long quantityKept;
    }

    public interface ICombatRuntimeStore
    {
        CombatRuntimeAggregate[] GetCombatAggregates();
        CombatRuntimeAggregate GetCombatAggregate(string executionId);
        bool AddCombatAggregate(CombatRuntimeAggregate aggregate);
        bool UpdateCombatAggregate(CombatRuntimeAggregate aggregate);
        bool RemoveCombatAggregate(string executionId);
    }

    internal static class CombatTerminalTransition
    {
        public static void ClearScheduledEvents(CombatSessionSaveData session)
        {
            if (session?.scheduler != null)
                session.scheduler.scheduledEvents =
                    Array.Empty<CombatScheduledEventSaveData>();
        }
    }

    internal static class CombatRuntimeSaveDataUtility
    {
        public const int PersistentCollectionLimit = 64;
        public const int StatusStackLimit = 8;

        public static bool TryNormalize(
            CombatExecutionSaveData executionSource,
            CombatSessionSaveData sessionSource,
            out CombatRuntimeAggregate aggregate,
            out bool changed,
            out string error)
        {
            aggregate = null;
            changed = false;
            error = null;
            if (executionSource == null || sessionSource == null)
                return Fail("Combat aggregate requires both execution and session.", out error);

            changed = HasNullCollections(sessionSource) ||
                      sessionSource.broughtConsumable?.HasLegacySourceStackId == true;
            var execution = CloneExecution(executionSource);
            var session = CloneSession(sessionSource);
            changed |= Canonicalize(session);

            if (!ValidateExecution(execution, out error) || !ValidateSession(session, out error))
                return false;
            if (!string.Equals(execution.executionId, session.executionId, StringComparison.Ordinal) ||
                !string.Equals(execution.sessionId, session.sessionId, StringComparison.Ordinal))
                return Fail("Combat execution/session links do not form one aggregate.", out error);
            if (!string.Equals(execution.heroId, session.hero.definitionId, StringComparison.Ordinal))
                return Fail("Combat execution hero does not match the session hero snapshot.", out error);
            if (session.simulationStopped &&
                session.queuePosition == session.enemyQueue.Length &&
                session.terminalCandidate == null &&
                !execution.outcomeFinalized)
                return Fail(
                    "Stopped completed combat queue requires a terminal candidate or finalized outcome.",
                    out error);
            if (execution.outcomeFinalized && session.terminalCandidate == null)
                return Fail(
                    "Finalized combat outcome requires its saved terminal candidate.",
                    out error);
            if (session.terminalCandidate != null &&
                !string.IsNullOrWhiteSpace(execution.outcome) &&
                !string.Equals(
                    execution.outcome,
                    session.terminalCandidate.kind,
                    StringComparison.Ordinal))
            {
                return Fail(
                    "Combat execution outcome conflicts with the saved terminal candidate.",
                    out error);
            }

            aggregate = new CombatRuntimeAggregate { execution = execution, session = session };
            return true;
        }

        public static CombatRuntimeAggregate CloneAggregate(CombatRuntimeAggregate source)
        {
            return source == null ? null : new CombatRuntimeAggregate
            {
                execution = CloneExecution(source.execution),
                session = CloneSession(source.session)
            };
        }

        public static CombatExecutionSaveData CloneExecution(CombatExecutionSaveData source)
        {
            if (source == null)
                return null;
            return new CombatExecutionSaveData
            {
                executionId = source.executionId,
                sessionId = source.sessionId,
                sourceActivityId = source.sourceActivityId,
                sourceExecutionId = source.sourceExecutionId,
                sourceRequestId = source.sourceRequestId,
                occupationOwnerId = source.occupationOwnerId,
                heroId = source.heroId,
                startOperationId = source.startOperationId,
                startFingerprint = source.startFingerprint,
                status = source.status,
                outcome = source.outcome,
                outcomeFinalized = source.outcomeFinalized,
                resultCreated = source.resultCreated,
                pendingResultResolved = source.pendingResultResolved,
                completionPublished = source.completionPublished,
                failurePublished = source.failurePublished,
                pendingResultId = source.pendingResultId,
                resultSourceSequence = source.resultSourceSequence,
                startedAtUnixSeconds = source.startedAtUnixSeconds,
                completedAtUnixSeconds = source.completedAtUnixSeconds
            };
        }

        public static CombatSessionSaveData CloneSession(CombatSessionSaveData source)
        {
            if (source == null)
                return null;
            return new CombatSessionSaveData
            {
                sessionId = source.sessionId,
                executionId = source.executionId,
                enemyGroupId = source.enemyGroupId,
                combatMode = source.combatMode,
                enemyQueue = CloneQueue(source.enemyQueue),
                queuePosition = source.queuePosition,
                hero = CloneCombatant(source.hero),
                currentEnemy = CloneCombatant(source.currentEnemy),
                combatTimeSeconds = source.combatTimeSeconds,
                scheduler = source.scheduler == null ? new CombatSchedulerStateSaveData() : new CombatSchedulerStateSaveData
                {
                    nextSequence = source.scheduler.nextSequence,
                    lastResolvedEventKey = source.scheduler.lastResolvedEventKey,
                    scheduledEvents = CloneScheduledEvents(source.scheduler.scheduledEvents)
                },
                rng = source.rng == null ? new CombatRngStateSaveData() : new CombatRngStateSaveData
                {
                    algorithmId = source.rng.algorithmId,
                    formatVersion = source.rng.formatVersion,
                    state = source.rng.state,
                    drawCount = source.rng.drawCount
                },
                loot = CloneRewards(source.loot),
                accumulatedEnemyExp = source.accumulatedEnemyExp,
                enemyExpTargetId = source.enemyExpTargetId,
                completionRewards = CloneRewards(source.completionRewards),
                completionRewardsSnapshotCreated =
                    source.completionRewardsSnapshotCreated,
                lastEnemyRewardOperation =
                    CloneEnemyRewardOperation(source.lastEnemyRewardOperation),
                outcomeRewards = CloneRewards(source.outcomeRewards),
                defeatLoss = CloneDefeatLoss(source.defeatLoss),
                loadoutKind = source.loadoutKind,
                broughtConsumable = CloneConsumable(source.broughtConsumable),
                lastDeathPreventionOperation =
                    CloneDeathPreventionOperation(source.lastDeathPreventionOperation),
                terminalCandidate = CloneTerminalCandidate(source.terminalCandidate),
                simulationStopped = source.simulationStopped
            };
        }

        public static bool HasSameIdentity(CombatRuntimeAggregate left, CombatRuntimeAggregate right)
        {
            if (left?.execution == null || left.session == null || right?.execution == null || right.session == null)
                return false;
            var a = left.execution;
            var b = right.execution;
            return string.Equals(a.executionId, b.executionId, StringComparison.Ordinal) &&
                   string.Equals(a.sessionId, b.sessionId, StringComparison.Ordinal) &&
                   string.Equals(a.sourceActivityId, b.sourceActivityId, StringComparison.Ordinal) &&
                   string.Equals(a.sourceExecutionId, b.sourceExecutionId, StringComparison.Ordinal) &&
                   string.Equals(a.sourceRequestId, b.sourceRequestId, StringComparison.Ordinal) &&
                   string.Equals(a.occupationOwnerId, b.occupationOwnerId, StringComparison.Ordinal) &&
                   string.Equals(a.heroId, b.heroId, StringComparison.Ordinal) &&
                   string.Equals(a.startOperationId, b.startOperationId, StringComparison.Ordinal) &&
                   string.Equals(a.startFingerprint, b.startFingerprint, StringComparison.Ordinal) &&
                   a.startedAtUnixSeconds == b.startedAtUnixSeconds &&
                   string.Equals(left.session.sessionId, right.session.sessionId, StringComparison.Ordinal) &&
                   string.Equals(left.session.executionId, right.session.executionId, StringComparison.Ordinal) &&
                   string.Equals(left.session.enemyGroupId, right.session.enemyGroupId, StringComparison.Ordinal) &&
                   string.Equals(left.session.combatMode, right.session.combatMode, StringComparison.Ordinal) &&
                   string.Equals(
                       left.session.enemyExpTargetId,
                       right.session.enemyExpTargetId,
                       StringComparison.Ordinal) &&
                   left.session.loadoutKind == right.session.loadoutKind &&
                   SameConsumableIdentity(
                       left.session.broughtConsumable,
                       right.session.broughtConsumable) &&
                   SameCombatantIdentity(left.session.hero, right.session.hero);
        }

        public static bool IsUnfinished(CombatExecutionSaveData execution)
        {
            return execution != null &&
                   (execution.status != CombatExecutionStatus.Completed ||
                    (execution.resultCreated && !execution.pendingResultResolved));
        }

        private static bool ValidateExecution(CombatExecutionSaveData execution, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(execution.executionId) || string.IsNullOrWhiteSpace(execution.sessionId) ||
                string.IsNullOrWhiteSpace(execution.sourceActivityId) || string.IsNullOrWhiteSpace(execution.sourceExecutionId) ||
                string.IsNullOrWhiteSpace(execution.occupationOwnerId) || string.IsNullOrWhiteSpace(execution.heroId) ||
                execution.resultSourceSequence < 0 ||
                execution.startedAtUnixSeconds < 0 || execution.completedAtUnixSeconds < 0)
                return Fail("Combat execution has an invalid identity/source snapshot.", out error);
            if (execution.status != CombatExecutionStatus.Running && execution.status != CombatExecutionStatus.ResultPending &&
                execution.status != CombatExecutionStatus.Completed)
                return Fail("Combat execution has an unsupported lifecycle status.", out error);
            if (string.IsNullOrWhiteSpace(execution.startOperationId) !=
                string.IsNullOrWhiteSpace(execution.startFingerprint))
                return Fail("Combat start idempotency marker is incomplete.", out error);
            if (execution.outcomeFinalized != !string.IsNullOrWhiteSpace(execution.outcome))
                return Fail("Combat execution outcome flags are inconsistent.", out error);
            if (execution.resultCreated != !string.IsNullOrWhiteSpace(execution.pendingResultId) ||
                (execution.pendingResultResolved && !execution.resultCreated))
                return Fail("Combat execution PendingResult flags are inconsistent.", out error);
            if (execution.status == CombatExecutionStatus.Running && execution.resultCreated)
                return Fail("Running combat execution cannot already have a PendingResult.", out error);
            if (execution.status == CombatExecutionStatus.ResultPending &&
                (!execution.resultCreated || execution.pendingResultResolved))
                return Fail("ResultPending combat execution requires an unresolved PendingResult.", out error);
            if (execution.status == CombatExecutionStatus.Completed && execution.resultCreated && !execution.pendingResultResolved)
                return Fail("Completed combat execution cannot retain an unresolved PendingResult.", out error);
            if (execution.outcomeFinalized && execution.resultSourceSequence <= 0)
                return Fail("Finalized combat execution requires a result source sequence.", out error);
            if (execution.completionPublished &&
                execution.failurePublished)
                return Fail("Combat execution cannot publish both completion and failure.", out error);
            if ((execution.completionPublished ||
                 execution.failurePublished) &&
                (execution.status != CombatExecutionStatus.Completed ||
                 !execution.pendingResultResolved ||
                 !string.Equals(
                     execution.sourceExecutionId,
                     execution.executionId,
                     StringComparison.Ordinal) ||
                 !string.Equals(
                     execution.occupationOwnerId,
                     execution.executionId,
                     StringComparison.Ordinal)))
                return Fail("Combat progression publication requires one resolved direct execution.", out error);
            if (execution.completionPublished &&
                !string.Equals(
                    execution.outcome,
                    CombatTerminalCandidateKinds.Victory,
                    StringComparison.Ordinal))
                return Fail("Combat completion publication requires Victory.", out error);
            if (execution.failurePublished &&
                !string.Equals(
                    execution.outcome,
                    CombatTerminalCandidateKinds.Defeat,
                    StringComparison.Ordinal))
                return Fail("Combat failure publication requires Defeat.", out error);
            return true;
        }

        private static bool ValidateSession(CombatSessionSaveData session, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(session.sessionId) || string.IsNullOrWhiteSpace(session.executionId) ||
                string.IsNullOrWhiteSpace(session.enemyGroupId) || string.IsNullOrWhiteSpace(session.combatMode) ||
                InvalidTime(session.combatTimeSeconds) || session.scheduler == null || session.scheduler.nextSequence < 0 ||
                session.accumulatedEnemyExp < 0)
                return Fail("Combat session has invalid identity, clock, scheduler or RNG state.", out error);
            if (!ValidateScheduler(session.scheduler, out error))
                return false;
            if (!ValidateRng(session.rng, out error))
                return false;
            if (!WithinLimit(session.enemyQueue) || !WithinLimit(session.loot) ||
                !WithinLimit(session.completionRewards) ||
                !WithinLimit(session.outcomeRewards) ||
                (session.defeatLoss != null &&
                 !WithinLimit(session.defeatLoss.entries)))
                return Fail("Combat session exceeds the persistent collection limit.", out error);
            if (!ValidateQueue(session.enemyQueue, out error) || !ValidateCombatant(session.hero, out error) ||
                (session.currentEnemy != null && !ValidateCombatant(session.currentEnemy, out error)) ||
                !ValidateRewards(session.loot, out error) ||
                !ValidateRewards(session.completionRewards, out error) ||
                !ValidateRewards(session.outcomeRewards, out error) ||
                !ValidateEnemyRewardOperation(
                    session.lastEnemyRewardOperation,
                    session.enemyQueue,
                    session.queuePosition,
                    out error) ||
                !ValidateDefeatLoss(session.defeatLoss, out error))
                return false;
            if (session.queuePosition < 0 || session.queuePosition > session.enemyQueue.Length)
                return Fail("Combat queue position is outside the saved queue.", out error);
            if (session.queuePosition == session.enemyQueue.Length)
            {
                if (session.currentEnemy != null)
                    return Fail("Completed combat queue cannot retain a current enemy.", out error);
            }
            else
            {
                var queueEntry = session.enemyQueue[session.queuePosition];
                if (session.currentEnemy == null ||
                    !string.Equals(queueEntry.combatantId, session.currentEnemy.combatantId, StringComparison.Ordinal) ||
                    !string.Equals(queueEntry.enemyId, session.currentEnemy.definitionId, StringComparison.Ordinal))
                    return Fail("Current enemy does not match the saved queue position.", out error);
            }
            if (!ValidateLoadout(session, out error) ||
                !ValidateConsumableSchedule(session, out error) ||
                !ValidateDeathPreventionOperation(
                    session.lastDeathPreventionOperation,
                    out error) ||
                !ValidateTerminalCandidate(session.terminalCandidate, out error))
                return false;
            if (session.terminalCandidate != null)
            {
                if (!session.simulationStopped)
                    return Fail("Terminal candidate requires a stopped simulation.", out error);
                if (session.scheduler.scheduledEvents.Length != 0)
                    return Fail("Terminal candidate cannot retain scheduled events.", out error);
                if (session.combatTimeSeconds != session.terminalCandidate.createdAtSeconds)
                    return Fail("Terminal candidate timestamp must match combat time.", out error);
                if (string.Equals(
                        session.terminalCandidate.kind,
                        CombatTerminalCandidateKinds.Defeat,
                        StringComparison.Ordinal) &&
                    session.hero.currentHp != 0)
                    return Fail("Defeat terminal candidate requires a defeated hero.", out error);
                if (string.Equals(
                        session.terminalCandidate.kind,
                        CombatTerminalCandidateKinds.Victory,
                        StringComparison.Ordinal) &&
                    (session.queuePosition != session.enemyQueue.Length ||
                     session.currentEnemy != null ||
                     session.hero.currentHp <= 0))
                    return Fail("Victory terminal candidate requires a completed enemy queue and living hero.", out error);
            }
            return true;
        }

        private static bool ValidateScheduler(CombatSchedulerStateSaveData scheduler, out string error)
        {
            error = null;
            if (!WithinLimit(scheduler.scheduledEvents))
                return Fail("Combat scheduler exceeds the persistent collection limit.", out error);

            var eventKeys = new HashSet<string>(StringComparer.Ordinal);
            var sequences = new HashSet<long>();
            var attackSides = new HashSet<CombatActorSide>();
            var effectEvents = new HashSet<string>(StringComparer.Ordinal);
            var hasConsumableCheck = false;
            foreach (var value in scheduler.scheduledEvents)
            {
                if (value == null || string.IsNullOrWhiteSpace(value.eventKey) ||
                    string.IsNullOrWhiteSpace(value.eventType) || InvalidTime(value.timestampSeconds) ||
                    (value.actorSide != CombatActorSide.Hero &&
                     value.actorSide != CombatActorSide.Enemy &&
                     value.actorSide != CombatActorSide.System) ||
                    value.sequence < 0 || value.sequence >= scheduler.nextSequence ||
                    !eventKeys.Add(value.eventKey) || !sequences.Add(value.sequence))
                {
                    return Fail("Combat scheduler contains an invalid or duplicated event.", out error);
                }

                if (string.Equals(value.eventType, CombatRuntimeService.ActorAttackEventType, StringComparison.Ordinal) &&
                    (value.phasePriority != (int)CombatScheduledEventPhase.ActorAttack ||
                      value.actorSide == CombatActorSide.System ||
                      !string.IsNullOrWhiteSpace(value.subjectCombatantId) ||
                      !string.IsNullOrWhiteSpace(value.effectInstanceId) ||
                      !attackSides.Add(value.actorSide)))
                {
                    return Fail("Combat scheduler contains an invalid or duplicated actor attack.", out error);
                }

                var isStatusTick = string.Equals(
                    value.eventType,
                    CombatStatusRuntime.StatusTickEventType,
                    StringComparison.Ordinal);
                var isStatusExpiration = string.Equals(
                    value.eventType,
                    CombatStatusRuntime.StatusExpirationEventType,
                    StringComparison.Ordinal);
                var isModifierExpiration = string.Equals(
                    value.eventType,
                    CombatStatusRuntime.ModifierExpirationEventType,
                    StringComparison.Ordinal);
                if ((isStatusTick || isStatusExpiration || isModifierExpiration) &&
                    (value.actorSide == CombatActorSide.System ||
                     string.IsNullOrWhiteSpace(value.subjectCombatantId) ||
                     string.IsNullOrWhiteSpace(value.effectInstanceId) ||
                     (isStatusTick &&
                      value.phasePriority != (int)CombatScheduledEventPhase.StatusTick) ||
                     (isStatusExpiration &&
                      value.phasePriority != (int)CombatScheduledEventPhase.StatusExpiration) ||
                     (isModifierExpiration &&
                      value.phasePriority != (int)CombatScheduledEventPhase.ModifierExpiration) ||
                     !effectEvents.Add($"{value.eventType}:{value.effectInstanceId}")))
                {
                    return Fail("Combat scheduler contains an invalid or duplicated status event.", out error);
                }

                if (string.Equals(
                        value.eventType,
                        CombatRuntimeService.ConsumableCheckEventType,
                        StringComparison.Ordinal) &&
                    (hasConsumableCheck ||
                     value.phasePriority != (int)CombatScheduledEventPhase.ConsumableCheck ||
                     value.actorSide != CombatActorSide.System ||
                     !string.IsNullOrWhiteSpace(value.subjectCombatantId) ||
                     !string.IsNullOrWhiteSpace(value.effectInstanceId)))
                {
                    return Fail("Combat scheduler contains an invalid or duplicated consumable check.", out error);
                }

                if (string.Equals(
                        value.eventType,
                        CombatRuntimeService.ConsumableCheckEventType,
                        StringComparison.Ordinal))
                {
                    hasConsumableCheck = true;
                }
            }

            return true;
        }

        private static bool ValidateRng(CombatRngStateSaveData rng, out string error)
        {
            error = null;
            if (rng == null || string.IsNullOrWhiteSpace(rng.algorithmId) || rng.formatVersion <= 0 ||
                string.IsNullOrWhiteSpace(rng.state) || rng.drawCount < 0)
                return Fail("Combat RNG descriptor is invalid.", out error);
            return true;
        }

        private static bool ValidateQueue(CombatEnemyQueueEntrySaveData[] queue, out string error)
        {
            error = null;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < queue.Length; index++)
            {
                var entry = queue[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.combatantId) || string.IsNullOrWhiteSpace(entry.enemyId) ||
                    entry.level <= 0 || entry.queueIndex != index || !ids.Add(entry.combatantId))
                    return Fail("Combat enemy queue is invalid or contains duplicate identities.", out error);
            }
            return true;
        }

        private static bool ValidateCombatant(CombatantStateSaveData combatant, out string error)
        {
            error = null;
            if (combatant == null || string.IsNullOrWhiteSpace(combatant.combatantId) ||
                string.IsNullOrWhiteSpace(combatant.definitionId) || combatant.maxHp <= 0 ||
                combatant.currentHp < 0 ||
                combatant.currentHp > combatant.maxHp || InvalidTime(combatant.nextAttackAtSeconds) ||
                !WithinLimit(combatant.abilityCooldowns) || !WithinLimit(combatant.statuses) ||
                !WithinLimit(combatant.independentModifiers))
                return Fail("Combatant state is invalid or exceeds the persistent collection limit.", out error);

            var abilityIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in combatant.abilityCooldowns)
                if (value == null || string.IsNullOrWhiteSpace(value.abilityId) || InvalidTime(value.nextReadyAtSeconds) ||
                    !abilityIds.Add(value.abilityId))
                    return Fail("Ability cooldown state is invalid or duplicated.", out error);

            var statusIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in combatant.statuses)
            {
                if (value == null || string.IsNullOrWhiteSpace(value.statusInstanceId) ||
                    string.IsNullOrWhiteSpace(value.statusId) ||
                    string.IsNullOrWhiteSpace(value.sourceCombatantId) ||
                    value.stackIds == null || value.stackIds.Length == 0 ||
                    value.stackIds.Length > StatusStackLimit ||
                    InvalidTime(value.expiresAtSeconds) ||
                    InvalidTime(value.nextTickAtSeconds) ||
                    !statusIds.Add(value.statusInstanceId))
                {
                    return Fail("Combat status state is invalid or duplicated.", out error);
                }

                var stackIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var stackId in value.stackIds)
                {
                    if (string.IsNullOrWhiteSpace(stackId) || !stackIds.Add(stackId))
                        return Fail("Combat status stack identity is invalid or duplicated.", out error);
                }
            }

            var modifierIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in combatant.independentModifiers)
                if (value == null || string.IsNullOrWhiteSpace(value.modifierInstanceId) || string.IsNullOrWhiteSpace(value.sourceId) ||
                    string.IsNullOrWhiteSpace(value.statId) || string.IsNullOrWhiteSpace(value.operation) || float.IsNaN(value.value) ||
                    float.IsInfinity(value.value) || InvalidTime(value.expiresAtSeconds) ||
                    string.IsNullOrWhiteSpace(value.appliedEventKey) ||
                    !modifierIds.Add(value.modifierInstanceId))
                    return Fail("Independent modifier state is invalid or duplicated.", out error);
            return true;
        }

        private static bool ValidateRewards(CombatRewardEntrySaveData[] rewards, out string error)
        {
            error = null;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in rewards)
                if (value == null || string.IsNullOrWhiteSpace(value.entryId) || string.IsNullOrWhiteSpace(value.rewardType) ||
                    string.IsNullOrWhiteSpace(value.targetId) || value.quantity <= 0 || string.IsNullOrWhiteSpace(value.origin) ||
                    value.quality < 0 || !ids.Add(value.entryId))
                    return Fail("Combat reward state is invalid or duplicated.", out error);
            return true;
        }

        private static bool ValidateEnemyRewardOperation(
            CombatEnemyRewardOperationSaveData value,
            CombatEnemyQueueEntrySaveData[] queue,
            int queuePosition,
            out string error)
        {
            error = null;
            if (value == null)
                return true;
            if (string.IsNullOrWhiteSpace(value.operationKey) ||
                string.IsNullOrWhiteSpace(value.combatantId) ||
                string.IsNullOrWhiteSpace(value.enemyId) ||
                value.enemyExp < 0)
                return Fail("Enemy reward operation state is invalid.", out error);
            if (value.legacyEmptyQueue)
            {
                if (queue.Length != 0 ||
                    queuePosition != 0 ||
                    value.queueIndex != -1)
                {
                    return Fail(
                        "Legacy enemy reward operation requires an empty queue.",
                        out error);
                }

                return true;
            }
            if (value.queueIndex < 0 || value.queueIndex >= queue.Length ||
                value.queueIndex >= queuePosition)
                return Fail("Enemy reward operation state is invalid.", out error);
            var entry = queue[value.queueIndex];
            return entry != null &&
                   string.Equals(
                       entry.combatantId,
                       value.combatantId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       entry.enemyId,
                       value.enemyId,
                       StringComparison.Ordinal) ||
                   Fail("Enemy reward operation does not match the saved queue.", out error);
        }

        private static bool ValidateDefeatLoss(
            CombatDefeatLossSaveData value,
            out string error)
        {
            error = null;
            if (value == null)
                return true;
            if (value.lossPercent < 25 || value.lossPercent > 50 ||
                value.entries == null)
                return Fail("Defeat loss state is invalid.", out error);
            foreach (var entry in value.entries)
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.origin) ||
                    string.IsNullOrWhiteSpace(entry.rewardType) ||
                    string.IsNullOrWhiteSpace(entry.targetId) ||
                    entry.quality < 0 ||
                    entry.quantityBefore <= 0 ||
                    entry.quantityLost < 0 ||
                    entry.quantityKept < 0 ||
                    entry.quantityLost + entry.quantityKept != entry.quantityBefore)
                    return Fail("Defeat loss breakdown entry is invalid.", out error);
            return true;
        }

        private static bool ValidateLoadout(CombatSessionSaveData session, out string error)
        {
            error = null;
            if (session.loadoutKind == CombatLoadoutKind.Empty)
            {
                return session.broughtConsumable == null ||
                       Fail("Empty combat loadout cannot retain brought consumable state.", out error);
            }

            if (session.loadoutKind == CombatLoadoutKind.Consumable)
            {
                return session.broughtConsumable != null
                    ? ValidateConsumable(session.broughtConsumable, out error)
                    : Fail("Consumable combat loadout requires brought consumable state.", out error);
            }

            return Fail("Combat loadout kind is invalid.", out error);
        }

        private static bool ValidateConsumable(CombatConsumableStateSaveData value, out string error)
        {
            error = null;
            if (value == null || string.IsNullOrWhiteSpace(value.sourceStackId) || string.IsNullOrWhiteSpace(value.itemId) ||
                value.initialQuantity <= 0 || value.remainingQuantity < 0 || value.remainingQuantity > value.initialQuantity ||
                InvalidTime(value.nextCheckAtSeconds) || InvalidTime(value.nextAllowedUseAtSeconds))
                return Fail("Brought consumable state is invalid.", out error);
            return true;
        }

        private static bool ValidateConsumableSchedule(
            CombatSessionSaveData session,
            out string error)
        {
            error = null;
            CombatScheduledEventSaveData pending = null;
            foreach (var value in session.scheduler.scheduledEvents)
            {
                if (!string.Equals(
                        value.eventType,
                        CombatRuntimeService.ConsumableCheckEventType,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                pending = value;
                break;
            }

            if (pending == null)
                return true;
            if (session.loadoutKind != CombatLoadoutKind.Consumable ||
                session.broughtConsumable == null ||
                session.broughtConsumable.remainingQuantity <= 0 ||
                session.simulationStopped ||
                session.terminalCandidate != null ||
                session.hero.currentHp <= 0 ||
                session.currentEnemy?.currentHp <= 0 ||
                pending.timestampSeconds != session.broughtConsumable.nextCheckAtSeconds ||
                string.Equals(
                    pending.eventKey,
                    session.broughtConsumable.lastAppliedEventKey,
                    StringComparison.Ordinal) ||
                string.Equals(
                    pending.eventKey,
                    session.scheduler.lastResolvedEventKey,
                    StringComparison.Ordinal))
            {
                return Fail(
                    "Pending consumable check does not match the saved consumable state.",
                    out error);
            }

            return true;
        }

        private static bool ValidateTerminalCandidate(CombatTerminalCandidateSaveData value, out string error)
        {
            error = null;
            if (value == null)
                return true;
            if (string.IsNullOrWhiteSpace(value.candidateId) || string.IsNullOrWhiteSpace(value.kind) ||
                string.IsNullOrWhiteSpace(value.eventKey) || InvalidTime(value.createdAtSeconds))
                return Fail("Combat terminal candidate is invalid.", out error);
            if (!string.Equals(value.kind, CombatTerminalCandidateKinds.Defeat, StringComparison.Ordinal) &&
                !string.Equals(value.kind, CombatTerminalCandidateKinds.Victory, StringComparison.Ordinal) &&
                !string.Equals(value.kind, CombatTerminalCandidateKinds.Retreat, StringComparison.Ordinal))
                return Fail($"Combat terminal candidate kind '{value.kind}' is unsupported.", out error);
            return true;
        }

        private static bool ValidateDeathPreventionOperation(
            CombatDeathPreventionOperationSaveData value,
            out string error)
        {
            error = null;
            if (value == null)
                return true;
            if (string.IsNullOrWhiteSpace(value.operationKey) ||
                string.IsNullOrWhiteSpace(value.targetCombatantId) ||
                string.IsNullOrWhiteSpace(value.effectId) ||
                value.chanceRoll < 1 ||
                value.chanceRoll > 10000)
            {
                return Fail("Combat death-prevention operation state is invalid.", out error);
            }

            return true;
        }

        private static bool Canonicalize(CombatSessionSaveData session)
        {
            var changed = false;
            if (session.currentEnemy != null &&
                session.queuePosition == session.enemyQueue.Length &&
                IsEmptyCombatant(session.currentEnemy))
            {
                session.currentEnemy = null;
                changed = true;
            }
            if (session.loadoutKind == CombatLoadoutKind.None)
            {
                session.loadoutKind = session.broughtConsumable == null
                    ? CombatLoadoutKind.Empty
                    : CombatLoadoutKind.Consumable;
                changed = true;
            }
            changed |= Sort(session.enemyQueue, CompareQueue);
            changed |= Sort(session.scheduler.scheduledEvents, CombatScheduledEventComparer.Instance.Compare);
            changed |= CanonicalizeCombatant(session.hero);
            changed |= CanonicalizeCombatant(session.currentEnemy);
            changed |= Sort(session.loot, CompareReward);
            changed |= Sort(session.completionRewards, CompareReward);
            changed |= Sort(session.outcomeRewards, CompareReward);
            return changed;
        }

        private static bool IsEmptyCombatant(
            CombatantStateSaveData combatant)
        {
            return string.IsNullOrWhiteSpace(combatant.combatantId) &&
                   string.IsNullOrWhiteSpace(combatant.definitionId) &&
                   combatant.currentHp == 0 &&
                   combatant.maxHp == 0 &&
                   combatant.nextAttackAtSeconds == 0d &&
                   string.IsNullOrWhiteSpace(combatant.lastAttackEventKey) &&
                   (combatant.abilityCooldowns == null ||
                    combatant.abilityCooldowns.Length == 0) &&
                   (combatant.statuses == null ||
                    combatant.statuses.Length == 0) &&
                   (combatant.independentModifiers == null ||
                    combatant.independentModifiers.Length == 0);
        }

        private static bool CanonicalizeCombatant(CombatantStateSaveData combatant)
        {
            if (combatant == null)
                return false;
            var changed = false;
            changed |= Sort(combatant.abilityCooldowns, (left, right) => CompareText(left?.abilityId, right?.abilityId));
            changed |= Sort(combatant.statuses, (left, right) => CompareText(left?.statusInstanceId, right?.statusInstanceId));
            foreach (var status in combatant.statuses)
            {
                if (status != null)
                    changed |= Sort(status.stackIds, CompareText);
            }
            changed |= Sort(combatant.independentModifiers, (left, right) => CompareText(left?.modifierInstanceId, right?.modifierInstanceId));
            return changed;
        }

        private static bool HasNullCollections(CombatSessionSaveData session)
        {
            return session.enemyQueue == null || session.loot == null ||
                   session.completionRewards == null || session.outcomeRewards == null ||
                   (session.defeatLoss != null && session.defeatLoss.entries == null) ||
                   HasNullCollections(session.hero) || HasNullCollections(session.currentEnemy) || session.scheduler == null ||
                   session.scheduler.scheduledEvents == null || session.rng == null;
        }

        private static bool HasNullCollections(CombatantStateSaveData combatant)
        {
            if (combatant == null)
                return false;
            if (combatant.abilityCooldowns == null ||
                combatant.statuses == null ||
                combatant.independentModifiers == null)
            {
                return true;
            }

            foreach (var status in combatant.statuses)
            {
                if (status != null && status.stackIds == null)
                    return true;
            }

            return false;
        }

        private static CombatEnemyQueueEntrySaveData[] CloneQueue(CombatEnemyQueueEntrySaveData[] source)
        {
            source ??= Array.Empty<CombatEnemyQueueEntrySaveData>();
            var result = new CombatEnemyQueueEntrySaveData[source.Length];
            for (var index = 0; index < result.Length; index++)
            {
                var value = source[index];
                result[index] = value == null ? null : new CombatEnemyQueueEntrySaveData
                {
                    combatantId = value.combatantId,
                    enemyId = value.enemyId,
                    level = value.level,
                    queueIndex = value.queueIndex
                };
            }
            return result;
        }

        private static CombatScheduledEventSaveData[] CloneScheduledEvents(CombatScheduledEventSaveData[] source)
        {
            source ??= Array.Empty<CombatScheduledEventSaveData>();
            var result = new CombatScheduledEventSaveData[source.Length];
            for (var index = 0; index < result.Length; index++)
            {
                var value = source[index];
                result[index] = value == null ? null : new CombatScheduledEventSaveData
                {
                    eventKey = value.eventKey,
                    eventType = value.eventType,
                    timestampSeconds = value.timestampSeconds,
                    phasePriority = value.phasePriority,
                    actorSide = value.actorSide,
                    sequence = value.sequence,
                    subjectCombatantId = value.subjectCombatantId,
                    effectInstanceId = value.effectInstanceId
                };
            }

            return result;
        }

        private static CombatantStateSaveData CloneCombatant(CombatantStateSaveData source)
        {
            if (source == null)
                return null;
            var cooldowns = source.abilityCooldowns ?? Array.Empty<CombatAbilityCooldownSaveData>();
            var cooldownCopies = new CombatAbilityCooldownSaveData[cooldowns.Length];
            for (var index = 0; index < cooldownCopies.Length; index++)
            {
                var value = cooldowns[index];
                cooldownCopies[index] = value == null ? null : new CombatAbilityCooldownSaveData
                {
                    abilityId = value.abilityId,
                    nextReadyAtSeconds = value.nextReadyAtSeconds,
                    lastTriggerEventKey = value.lastTriggerEventKey,
                    lastChanceRoll = value.lastChanceRoll,
                    lastChanceResolved = value.lastChanceResolved
                };
            }
            var statuses = source.statuses ?? Array.Empty<CombatStatusInstanceSaveData>();
            var statusCopies = new CombatStatusInstanceSaveData[statuses.Length];
            for (var index = 0; index < statusCopies.Length; index++)
            {
                var value = statuses[index];
                statusCopies[index] = value == null ? null : new CombatStatusInstanceSaveData
                {
                    statusInstanceId = value.statusInstanceId,
                    statusId = value.statusId,
                    sourceCombatantId = value.sourceCombatantId,
                    stackIds = value.stackIds == null
                        ? Array.Empty<string>()
                        : (string[])value.stackIds.Clone(),
                    expiresAtSeconds = value.expiresAtSeconds,
                    nextTickAtSeconds = value.nextTickAtSeconds,
                    lastApplyEventKey = value.lastApplyEventKey,
                    lastTickEventKey = value.lastTickEventKey
                };
            }
            var modifiers = source.independentModifiers ?? Array.Empty<CombatTemporaryModifierSaveData>();
            var modifierCopies = new CombatTemporaryModifierSaveData[modifiers.Length];
            for (var index = 0; index < modifierCopies.Length; index++)
            {
                var value = modifiers[index];
                modifierCopies[index] = value == null ? null : new CombatTemporaryModifierSaveData
                {
                    modifierInstanceId = value.modifierInstanceId,
                    sourceId = value.sourceId,
                    statId = value.statId,
                    operation = value.operation,
                    value = value.value,
                    expiresAtSeconds = value.expiresAtSeconds,
                    appliedEventKey = value.appliedEventKey
                };
            }
            return new CombatantStateSaveData
            {
                combatantId = source.combatantId,
                definitionId = source.definitionId,
                currentHp = source.currentHp,
                maxHp = source.maxHp,
                nextAttackAtSeconds = source.nextAttackAtSeconds,
                lastAttackEventKey = source.lastAttackEventKey,
                abilityCooldowns = cooldownCopies,
                statuses = statusCopies,
                independentModifiers = modifierCopies
            };
        }

        private static CombatRewardEntrySaveData[] CloneRewards(CombatRewardEntrySaveData[] source)
        {
            source ??= Array.Empty<CombatRewardEntrySaveData>();
            var result = new CombatRewardEntrySaveData[source.Length];
            for (var index = 0; index < result.Length; index++)
            {
                var value = source[index];
                result[index] = value == null ? null : new CombatRewardEntrySaveData
                {
                    entryId = value.entryId,
                    sortOrder = value.sortOrder,
                    rewardType = value.rewardType,
                    targetId = value.targetId,
                    quantity = value.quantity,
                    origin = value.origin,
                    quality = value.quality,
                    instanceId = value.instanceId
                };
            }
            return result;
        }

        private static CombatConsumableStateSaveData CloneConsumable(CombatConsumableStateSaveData source)
        {
            var sourceStackId = source?.ResolveSourceStackId();
            return source == null ||
                   (string.IsNullOrWhiteSpace(sourceStackId) &&
                    string.IsNullOrWhiteSpace(source.itemId) &&
                    source.initialQuantity == 0 &&
                    source.remainingQuantity == 0 &&
                    source.nextCheckAtSeconds == 0d &&
                    source.nextAllowedUseAtSeconds == 0d &&
                    string.IsNullOrWhiteSpace(source.lastAppliedEventKey))
                ? null
                : new CombatConsumableStateSaveData
            {
                sourceStackId = sourceStackId,
                itemId = source.itemId,
                initialQuantity = source.initialQuantity,
                remainingQuantity = source.remainingQuantity,
                nextCheckAtSeconds = source.nextCheckAtSeconds,
                nextAllowedUseAtSeconds = source.nextAllowedUseAtSeconds,
                lastAppliedEventKey = source.lastAppliedEventKey
            };
        }

        private static CombatTerminalCandidateSaveData CloneTerminalCandidate(CombatTerminalCandidateSaveData source)
        {
            return source == null ||
                   (string.IsNullOrWhiteSpace(source.candidateId) &&
                    string.IsNullOrWhiteSpace(source.kind) &&
                    string.IsNullOrWhiteSpace(source.eventKey) &&
                    source.createdAtSeconds == 0d)
                ? null
                : new CombatTerminalCandidateSaveData
            {
                candidateId = source.candidateId,
                kind = source.kind,
                eventKey = source.eventKey,
                createdAtSeconds = source.createdAtSeconds
            };
        }

        private static CombatDeathPreventionOperationSaveData CloneDeathPreventionOperation(
            CombatDeathPreventionOperationSaveData source)
        {
            return source == null ||
                   (string.IsNullOrWhiteSpace(source.operationKey) &&
                    string.IsNullOrWhiteSpace(source.targetCombatantId) &&
                    string.IsNullOrWhiteSpace(source.effectId) &&
                    source.chanceRoll == 0 &&
                    !source.successful)
                ? null
                : new CombatDeathPreventionOperationSaveData
            {
                operationKey = source.operationKey,
                targetCombatantId = source.targetCombatantId,
                effectId = source.effectId,
                chanceRoll = source.chanceRoll,
                successful = source.successful
            };
        }

        private static CombatEnemyRewardOperationSaveData CloneEnemyRewardOperation(
            CombatEnemyRewardOperationSaveData source)
        {
            return source == null ||
                   (string.IsNullOrWhiteSpace(source.operationKey) &&
                    string.IsNullOrWhiteSpace(source.combatantId) &&
                    string.IsNullOrWhiteSpace(source.enemyId) &&
                    source.queueIndex == 0 &&
                    source.enemyExp == 0 &&
                    !source.legacyEmptyQueue)
                ? null
                : new CombatEnemyRewardOperationSaveData
            {
                operationKey = source.operationKey,
                combatantId = source.combatantId,
                enemyId = source.enemyId,
                queueIndex = source.queueIndex,
                enemyExp = source.enemyExp,
                legacyEmptyQueue = source.legacyEmptyQueue
            };
        }

        private static CombatDefeatLossSaveData CloneDefeatLoss(
            CombatDefeatLossSaveData source)
        {
            if (source == null ||
                (source.lossPercent == 0 &&
                 (source.entries == null || source.entries.Length == 0)))
                return null;
            var entries = source.entries ?? Array.Empty<CombatDefeatLossEntrySaveData>();
            var copies = new CombatDefeatLossEntrySaveData[entries.Length];
            for (var index = 0; index < copies.Length; index++)
            {
                var value = entries[index];
                copies[index] = value == null ? null : new CombatDefeatLossEntrySaveData
                {
                    origin = value.origin,
                    rewardType = value.rewardType,
                    targetId = value.targetId,
                    quality = value.quality,
                    instanceId = value.instanceId,
                    quantityBefore = value.quantityBefore,
                    quantityLost = value.quantityLost,
                    quantityKept = value.quantityKept
                };
            }
            return new CombatDefeatLossSaveData
            {
                lossPercent = source.lossPercent,
                entries = copies
            };
        }

        private static bool SameCombatantIdentity(CombatantStateSaveData left, CombatantStateSaveData right)
        {
            return left != null && right != null &&
                   string.Equals(left.combatantId, right.combatantId, StringComparison.Ordinal) &&
                   string.Equals(left.definitionId, right.definitionId, StringComparison.Ordinal) &&
                   left.maxHp == right.maxHp;
        }

        private static bool SameConsumableIdentity(
            CombatConsumableStateSaveData left,
            CombatConsumableStateSaveData right)
        {
            if (left == null || right == null)
                return left == null && right == null;
            return string.Equals(
                       left.sourceStackId,
                       right.sourceStackId,
                       StringComparison.Ordinal) &&
                   string.Equals(left.itemId, right.itemId, StringComparison.Ordinal) &&
                   left.initialQuantity == right.initialQuantity;
        }

        private static int CompareQueue(CombatEnemyQueueEntrySaveData left, CombatEnemyQueueEntrySaveData right)
        {
            if (left == null || right == null)
                return left == right ? 0 : left == null ? 1 : -1;
            var order = left.queueIndex.CompareTo(right.queueIndex);
            return order != 0 ? order : CompareText(left.combatantId, right.combatantId);
        }

        private static int CompareReward(CombatRewardEntrySaveData left, CombatRewardEntrySaveData right)
        {
            if (left == null || right == null)
                return left == right ? 0 : left == null ? 1 : -1;
            var order = left.sortOrder.CompareTo(right.sortOrder);
            return order != 0 ? order : CompareText(left.entryId, right.entryId);
        }

        private static int CompareText(string left, string right) => string.Compare(left, right, StringComparison.Ordinal);

        private static bool Sort<T>(T[] values, Comparison<T> comparison)
        {
            var changed = false;
            for (var index = 1; index < values.Length; index++)
                if (comparison(values[index - 1], values[index]) > 0) { changed = true; break; }
            if (changed)
                Array.Sort(values, comparison);
            return changed;
        }

        private static bool InvalidTime(double value) => double.IsNaN(value) || double.IsInfinity(value) || value < 0d;
        private static bool WithinLimit<T>(T[] values) => values != null && values.Length <= PersistentCollectionLimit;

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
