using System;
using System.Collections.Generic;

namespace GuildIdle.Combat
{
    public enum CombatExecutionStatus
    {
        None = 0,
        Running = 1,
        ResultPending = 2,
        Completed = 3
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
        public CombatExecutionStatus status = CombatExecutionStatus.Running;
        public string outcome;
        public bool outcomeFinalized;
        public bool resultCreated;
        public bool pendingResultResolved;
        public bool completionPublished;
        public string pendingResultId;
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
        public CombatRewardEntrySaveData[] completionRewards = Array.Empty<CombatRewardEntrySaveData>();
        public CombatConsumableStateSaveData broughtConsumable;
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
        public int lastChanceRoll;
        public bool lastChanceResolved;
    }

    [Serializable]
    public sealed class CombatStatusInstanceSaveData
    {
        public string statusInstanceId;
        public string statusId;
        public string sourceCombatantId;
        public int stacks;
        public double expiresAtSeconds;
        public double nextTickAtSeconds;
        public string lastEventKey;
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
    }

    [Serializable]
    public sealed class CombatSchedulerStateSaveData
    {
        public long nextSequence;
        public string lastResolvedEventKey;
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
        public string originStackId;
        public string itemId;
        public int initialQuantity;
        public int remainingQuantity;
        public double nextCheckAtSeconds;
        public double nextAllowedUseAtSeconds;
        public string lastAppliedEventKey;
    }

    [Serializable]
    public sealed class CombatTerminalCandidateSaveData
    {
        public string candidateId;
        public string kind;
        public string eventKey;
        public double createdAtSeconds;
    }

    public interface ICombatRuntimeStore
    {
        CombatRuntimeAggregate[] GetCombatAggregates();
        CombatRuntimeAggregate GetCombatAggregate(string executionId);
        bool AddCombatAggregate(CombatRuntimeAggregate aggregate);
        bool UpdateCombatAggregate(CombatRuntimeAggregate aggregate);
        bool RemoveCombatAggregate(string executionId);
    }

    internal static class CombatRuntimeSaveDataUtility
    {
        public const int PersistentCollectionLimit = 64;

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

            changed = HasNullCollections(sessionSource);
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
                status = source.status,
                outcome = source.outcome,
                outcomeFinalized = source.outcomeFinalized,
                resultCreated = source.resultCreated,
                pendingResultResolved = source.pendingResultResolved,
                completionPublished = source.completionPublished,
                pendingResultId = source.pendingResultId,
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
                completionRewards = CloneRewards(source.completionRewards),
                broughtConsumable = CloneConsumable(source.broughtConsumable),
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
                   a.startedAtUnixSeconds == b.startedAtUnixSeconds &&
                   string.Equals(left.session.sessionId, right.session.sessionId, StringComparison.Ordinal) &&
                   string.Equals(left.session.executionId, right.session.executionId, StringComparison.Ordinal) &&
                   string.Equals(left.session.enemyGroupId, right.session.enemyGroupId, StringComparison.Ordinal) &&
                   string.Equals(left.session.combatMode, right.session.combatMode, StringComparison.Ordinal) &&
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
                execution.startedAtUnixSeconds < 0 || execution.completedAtUnixSeconds < 0)
                return Fail("Combat execution has an invalid identity/source snapshot.", out error);
            if (execution.status != CombatExecutionStatus.Running && execution.status != CombatExecutionStatus.ResultPending &&
                execution.status != CombatExecutionStatus.Completed)
                return Fail("Combat execution has an unsupported lifecycle status.", out error);
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
            if (!WithinLimit(session.enemyQueue) || !WithinLimit(session.loot) || !WithinLimit(session.completionRewards))
                return Fail("Combat session exceeds the persistent collection limit.", out error);
            if (!ValidateQueue(session.enemyQueue, out error) || !ValidateCombatant(session.hero, out error) ||
                (session.currentEnemy != null && !ValidateCombatant(session.currentEnemy, out error)) ||
                !ValidateRewards(session.loot, out error) || !ValidateRewards(session.completionRewards, out error))
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
            if (!ValidateConsumable(session.broughtConsumable, out error) ||
                !ValidateTerminalCandidate(session.terminalCandidate, out error))
                return false;
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
                     !attackSides.Add(value.actorSide)))
                {
                    return Fail("Combat scheduler contains an invalid or duplicated actor attack.", out error);
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
                if (value == null || string.IsNullOrWhiteSpace(value.statusInstanceId) || string.IsNullOrWhiteSpace(value.statusId) ||
                    string.IsNullOrWhiteSpace(value.sourceCombatantId) || value.stacks <= 0 || InvalidTime(value.expiresAtSeconds) ||
                    InvalidTime(value.nextTickAtSeconds) || !statusIds.Add(value.statusInstanceId))
                    return Fail("Combat status state is invalid or duplicated.", out error);

            var modifierIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in combatant.independentModifiers)
                if (value == null || string.IsNullOrWhiteSpace(value.modifierInstanceId) || string.IsNullOrWhiteSpace(value.sourceId) ||
                    string.IsNullOrWhiteSpace(value.statId) || string.IsNullOrWhiteSpace(value.operation) || float.IsNaN(value.value) ||
                    float.IsInfinity(value.value) || InvalidTime(value.expiresAtSeconds) || !modifierIds.Add(value.modifierInstanceId))
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

        private static bool ValidateConsumable(CombatConsumableStateSaveData value, out string error)
        {
            error = null;
            if (value == null)
                return true;
            if (string.IsNullOrWhiteSpace(value.originStackId) || string.IsNullOrWhiteSpace(value.itemId) ||
                value.initialQuantity <= 0 || value.remainingQuantity < 0 || value.remainingQuantity > value.initialQuantity ||
                InvalidTime(value.nextCheckAtSeconds) || InvalidTime(value.nextAllowedUseAtSeconds))
                return Fail("Brought consumable state is invalid.", out error);
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
            return true;
        }

        private static bool Canonicalize(CombatSessionSaveData session)
        {
            var changed = false;
            changed |= Sort(session.enemyQueue, CompareQueue);
            changed |= Sort(session.scheduler.scheduledEvents, CombatScheduledEventComparer.Instance.Compare);
            changed |= CanonicalizeCombatant(session.hero);
            changed |= CanonicalizeCombatant(session.currentEnemy);
            changed |= Sort(session.loot, CompareReward);
            changed |= Sort(session.completionRewards, CompareReward);
            return changed;
        }

        private static bool CanonicalizeCombatant(CombatantStateSaveData combatant)
        {
            if (combatant == null)
                return false;
            var changed = false;
            changed |= Sort(combatant.abilityCooldowns, (left, right) => CompareText(left?.abilityId, right?.abilityId));
            changed |= Sort(combatant.statuses, (left, right) => CompareText(left?.statusInstanceId, right?.statusInstanceId));
            changed |= Sort(combatant.independentModifiers, (left, right) => CompareText(left?.modifierInstanceId, right?.modifierInstanceId));
            return changed;
        }

        private static bool HasNullCollections(CombatSessionSaveData session)
        {
            return session.enemyQueue == null || session.loot == null || session.completionRewards == null ||
                   HasNullCollections(session.hero) || HasNullCollections(session.currentEnemy) || session.scheduler == null ||
                   session.scheduler.scheduledEvents == null || session.rng == null;
        }

        private static bool HasNullCollections(CombatantStateSaveData combatant)
        {
            return combatant != null && (combatant.abilityCooldowns == null || combatant.statuses == null || combatant.independentModifiers == null);
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
                    sequence = value.sequence
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
                    stacks = value.stacks,
                    expiresAtSeconds = value.expiresAtSeconds,
                    nextTickAtSeconds = value.nextTickAtSeconds,
                    lastEventKey = value.lastEventKey
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
                    expiresAtSeconds = value.expiresAtSeconds
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
            return source == null ? null : new CombatConsumableStateSaveData
            {
                originStackId = source.originStackId,
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
            return source == null ? null : new CombatTerminalCandidateSaveData
            {
                candidateId = source.candidateId,
                kind = source.kind,
                eventKey = source.eventKey,
                createdAtSeconds = source.createdAtSeconds
            };
        }

        private static bool SameCombatantIdentity(CombatantStateSaveData left, CombatantStateSaveData right)
        {
            return left != null && right != null &&
                   string.Equals(left.combatantId, right.combatantId, StringComparison.Ordinal) &&
                   string.Equals(left.definitionId, right.definitionId, StringComparison.Ordinal);
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
