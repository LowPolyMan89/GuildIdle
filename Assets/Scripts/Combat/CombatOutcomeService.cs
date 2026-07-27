using System;
using System.Collections.Generic;
using GuildIdle.Core;
using GuildIdle.Player;

namespace GuildIdle.Combat
{
    public sealed class CombatRetreatCommand
    {
        public string OperationId { get; set; }
        public string ExecutionId { get; set; }
    }

    public sealed class CombatOutcomeResult
    {
        public bool Success { get; internal set; }
        public bool Replayed { get; internal set; }
        public string Code { get; internal set; }
        public string Message { get; internal set; }
        public string ExecutionId { get; internal set; }
        public string Outcome { get; internal set; }
        public string PendingResultId { get; internal set; }
        public bool ResolvedImmediately { get; internal set; }
    }

    public sealed class CombatOutcomeService : ICombatAggregateCommitter
    {
        private const string RetreatReceiptAggregateId = "combat-retreat";

        private readonly PlayerState _state;
        private readonly ICombatRngFactory _rngFactory;

        public CombatOutcomeService(
            PlayerState state,
            ICombatRngFactory rngFactory = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _rngFactory = rngFactory ?? new CombatRngFactory();
        }

        public CombatOutcomeResult RequestRetreat(CombatRetreatCommand command)
        {
            if (command == null ||
                string.IsNullOrWhiteSpace(command.OperationId) ||
                string.IsNullOrWhiteSpace(command.ExecutionId))
                return Failed(null, "InvalidCommand", "Retreat operation and execution ids are required.");

            var fingerprint = $"retreat|{command.ExecutionId}";
            if (_state.TryGetOperationReceipt(
                    RetreatReceiptAggregateId,
                    command.OperationId,
                    out var receipt))
            {
                if (!string.Equals(
                        receipt.fingerprint,
                        fingerprint,
                        StringComparison.Ordinal))
                    return Failed(command.ExecutionId, "OperationConflict",
                        "Retreat operation id was already used for another execution.");
                var replay = _state.GetCombatAggregate(command.ExecutionId);
                return new CombatOutcomeResult
                {
                    Success = receipt.success,
                    Replayed = true,
                    Code = receipt.code,
                    ExecutionId = command.ExecutionId,
                    Outcome = replay?.execution?.outcome,
                    PendingResultId = replay?.execution?.pendingResultId,
                    ResolvedImmediately =
                        replay?.execution?.pendingResultResolved == true
                };
            }

            var stored = _state.GetCombatAggregate(command.ExecutionId);
            if (stored == null)
                return Failed(command.ExecutionId, "CombatNotFound",
                    "Combat execution was not found.");
            if (stored.execution.status != CombatExecutionStatus.Running ||
                stored.execution.outcomeFinalized ||
                stored.session.simulationStopped ||
                stored.session.terminalCandidate != null)
                return Failed(command.ExecutionId, "CombatNotRunning",
                    "Only a running combat without a terminal candidate can retreat.");

            var checkpoint = _state.ToSaveData();
            var aggregate = CombatRuntimeSaveDataUtility.CloneAggregate(stored);
            aggregate.session.simulationStopped = true;
            aggregate.session.scheduler.scheduledEvents =
                Array.Empty<CombatScheduledEventSaveData>();
            aggregate.session.terminalCandidate =
                new CombatTerminalCandidateSaveData
                {
                    candidateId = $"{aggregate.session.sessionId}:retreat",
                    kind = CombatTerminalCandidateKinds.Retreat,
                    eventKey =
                        $"{aggregate.session.sessionId}:retreat:{command.OperationId}",
                    createdAtSeconds = aggregate.session.combatTimeSeconds
                };
            return FinalizeAggregate(
                aggregate,
                checkpoint,
                new OperationReceiptSaveData
                {
                    aggregateId = RetreatReceiptAggregateId,
                    operationId = command.OperationId,
                    fingerprint = fingerprint,
                    success = true,
                    code = "Applied"
                });
        }

        public bool TryCommit(
            CombatRuntimeAggregate aggregate,
            out CombatAdvanceError error)
        {
            error = null;
            if (aggregate?.execution == null || aggregate.session == null)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.OutcomeCommitFailed,
                    "Combat aggregate commit requires execution and session.");
                return false;
            }
            var checkpoint = _state.ToSaveData();
            if (aggregate.session.terminalCandidate == null)
            {
                if (!_state.UpdateCombatAggregate(aggregate) || !_state.Save())
                {
                    _state.RestoreTransactional(checkpoint);
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.StoreUpdateFailed,
                        "Combat aggregate update could not be saved.");
                    return false;
                }
                return true;
            }

            var result = FinalizeAggregate(aggregate, checkpoint, null);
            if (result.Success)
                return true;
            error = new CombatAdvanceError(
                CombatAdvanceErrorCode.OutcomeCommitFailed,
                result.Message ?? result.Code ?? "Combat outcome commit failed.");
            return false;
        }

        public CombatOutcomeResult FinalizeTerminal(string executionId)
        {
            if (string.IsNullOrWhiteSpace(executionId))
                return Failed(executionId, "InvalidCommand",
                    "Combat execution id is required.");
            var stored = _state.GetCombatAggregate(executionId);
            if (stored == null)
                return Failed(executionId, "CombatNotFound",
                    "Combat execution was not found.");
            if (stored.execution.outcomeFinalized)
            {
                return new CombatOutcomeResult
                {
                    Success = true,
                    Replayed = true,
                    Code = stored.execution.pendingResultResolved
                        ? "Completed"
                        : "ResultPending",
                    ExecutionId = executionId,
                    Outcome = stored.execution.outcome,
                    PendingResultId = stored.execution.pendingResultId,
                    ResolvedImmediately =
                        stored.execution.pendingResultResolved
                };
            }

            return FinalizeAggregate(
                CombatRuntimeSaveDataUtility.CloneAggregate(stored),
                _state.ToSaveData(),
                null);
        }

        private CombatOutcomeResult FinalizeAggregate(
            CombatRuntimeAggregate aggregate,
            SaveData checkpoint,
            OperationReceiptSaveData receipt)
        {
            var execution = aggregate?.execution;
            var session = aggregate?.session;
            var candidate = session?.terminalCandidate;
            if (execution == null || session == null || candidate == null ||
                execution.status != CombatExecutionStatus.Running ||
                execution.outcomeFinalized || !session.simulationStopped)
                return Failed(execution?.executionId, "InvalidTerminalState",
                    "Combat terminal candidate is missing or inconsistent.");
            if (!session.completionRewardsSnapshotCreated)
                return Failed(execution.executionId, "CorruptedState",
                    "Combat completion reward snapshot is missing.");
            if (session.accumulatedEnemyExp > 0 &&
                string.IsNullOrWhiteSpace(session.enemyExpTargetId))
                return Failed(execution.executionId, "CorruptedState",
                    "Enemy EXP target snapshot is missing.");
            if (!_rngFactory.TryRestore(
                    session.rng,
                    out var rng,
                    out var rngError))
                return Failed(execution.executionId, "InvalidRngState",
                    rngError?.Message ?? "Combat RNG state is invalid.");
            if (_state.LastCombatResultSequence == long.MaxValue)
                return Failed(execution.executionId, "SequenceExhausted",
                    "Combat result sequence is exhausted.");

            if (!TryBuildOutcomeRewards(
                    session,
                    candidate,
                    rng,
                    out var rewards,
                    out var loss,
                    out var error))
                return Failed(execution.executionId, "OutcomeFailed", error);

            session.outcomeRewards = rewards;
            session.defeatLoss = loss;
            session.rng = rng.CaptureState();
            execution.outcome = candidate.kind;
            execution.outcomeFinalized = true;
            execution.resultSourceSequence =
                _state.LastCombatResultSequence + 1;
            if (!_state.UpdateCombatAggregate(aggregate))
                return Failed(execution.executionId, "StoreUpdateFailed",
                    "Combat outcome aggregate update was rejected.");
            if (receipt != null)
                _state.RecordOperationReceipt(receipt);

            var draftEntries =
                new PendingResultEntryDraft[rewards.Length];
            for (var index = 0; index < rewards.Length; index++)
            {
                var value = rewards[index];
                draftEntries[index] = new PendingResultEntryDraft
                {
                    SortOrder = value.sortOrder,
                    RewardType = value.rewardType,
                    TargetId = value.targetId,
                    Quantity = value.quantity,
                    Origin = value.origin,
                    Quality = value.quality,
                    InstanceId = value.instanceId
                };
            }

            var formation = _state.PendingResults.CreateCombatResult(
                $"combat-result:{execution.executionId}:{candidate.candidateId}",
                new PendingResultDraft
                {
                    SourceId = execution.sourceActivityId,
                    SourceExecutionId = execution.executionId,
                    OwnerHeroId = execution.heroId,
                    SourceSequence = execution.resultSourceSequence,
                    Entries = draftEntries
                },
                null,
                null,
                _state.StorageRevision);
            if (!formation.Success)
            {
                _state.RestoreTransactional(checkpoint);
                return Failed(execution.executionId,
                    formation.Code ?? "PendingResultFailed",
                    formation.Message ?? "Combat PendingResult formation failed.");
            }

            var stored = _state.GetCombatAggregate(execution.executionId);
            return new CombatOutcomeResult
            {
                Success = true,
                Code = formation.ResolvedImmediately
                    ? "Completed"
                    : "ResultPending",
                ExecutionId = execution.executionId,
                Outcome = candidate.kind,
                PendingResultId = stored?.execution?.pendingResultId,
                ResolvedImmediately = formation.ResolvedImmediately
            };
        }

        private static bool TryBuildOutcomeRewards(
            CombatSessionSaveData session,
            CombatTerminalCandidateSaveData candidate,
            ICombatRng rng,
            out CombatRewardEntrySaveData[] rewards,
            out CombatDefeatLossSaveData loss,
            out string error)
        {
            rewards = Array.Empty<CombatRewardEntrySaveData>();
            loss = null;
            error = null;
            var entries = new List<CombatRewardEntrySaveData>();
            if (string.Equals(
                    candidate.kind,
                    CombatTerminalCandidateKinds.Victory,
                    StringComparison.Ordinal) ||
                string.Equals(
                    candidate.kind,
                    CombatTerminalCandidateKinds.Retreat,
                    StringComparison.Ordinal))
            {
                AddCopies(entries, session.loot);
            }
            else if (string.Equals(
                         candidate.kind,
                         CombatTerminalCandidateKinds.Defeat,
                         StringComparison.Ordinal))
            {
                if (!TryApplyDefeatLoss(
                        session.loot,
                        rng,
                        entries,
                        out loss,
                        out error))
                    return false;
            }
            else
            {
                error = $"Unsupported terminal candidate '{candidate.kind}'.";
                return false;
            }

            if (session.accumulatedEnemyExp > 0)
            {
                entries.Add(new CombatRewardEntrySaveData
                {
                    rewardType = RewardType.SkillExp,
                    targetId = session.enemyExpTargetId,
                    quantity = session.accumulatedEnemyExp,
                    origin = PendingResultOrigin.EnemyCombatExp
                });
            }
            if (string.Equals(
                    candidate.kind,
                    CombatTerminalCandidateKinds.Victory,
                    StringComparison.Ordinal))
                AddCopies(entries, session.completionRewards);
            if (session.broughtConsumable?.remainingQuantity > 0)
            {
                entries.Add(new CombatRewardEntrySaveData
                {
                    rewardType = RewardType.Consumable,
                    targetId = session.broughtConsumable.itemId,
                    quantity = session.broughtConsumable.remainingQuantity,
                    origin = PendingResultOrigin.BroughtConsumable
                });
            }
            if (entries.Count >
                CombatRuntimeSaveDataUtility.PersistentCollectionLimit)
            {
                error = "Combat outcome exceeds its bounded reward limit.";
                return false;
            }

            for (var index = 0; index < entries.Count; index++)
            {
                entries[index].entryId =
                    $"{candidate.candidateId}:reward:{index}";
                entries[index].sortOrder = index;
            }
            rewards = entries.ToArray();
            return true;
        }

        private static bool TryApplyDefeatLoss(
            CombatRewardEntrySaveData[] source,
            ICombatRng rng,
            List<CombatRewardEntrySaveData> kept,
            out CombatDefeatLossSaveData loss,
            out string error)
        {
            error = null;
            var eligible = new Dictionary<string, CombatRewardEntrySaveData>(
                StringComparer.Ordinal);
            foreach (var value in source ??
                     Array.Empty<CombatRewardEntrySaveData>())
            {
                if (!IsLossEligible(value))
                {
                    if (value != null)
                        kept.Add(CloneReward(value));
                    continue;
                }
                var key = string.IsNullOrWhiteSpace(value.instanceId)
                    ? $"{value.origin}\n{value.rewardType}\n{value.targetId}\n{value.quality}"
                    : $"{value.origin}\n{value.rewardType}\n{value.targetId}\n{value.quality}\n{value.instanceId}";
                if (!eligible.TryGetValue(key, out var aggregate))
                {
                    eligible.Add(key, CloneReward(value));
                    continue;
                }
                if (value.quantity > long.MaxValue - aggregate.quantity)
                {
                    loss = null;
                    error = "Defeat loot aggregation exceeds Int64.";
                    return false;
                }
                aggregate.quantity += value.quantity;
            }

            var keys = new List<string>(eligible.Keys);
            keys.Sort(StringComparer.Ordinal);
            var lossPercent = CombatRngRolls.Inclusive(rng, 25, 50);
            var breakdown =
                new CombatDefeatLossEntrySaveData[keys.Count];
            for (var index = 0; index < keys.Count; index++)
            {
                var value = eligible[keys[index]];
                var lost =
                    value.quantity / 100L * lossPercent +
                    value.quantity % 100L * lossPercent / 100L;
                var quantityKept = value.quantity - lost;
                breakdown[index] = new CombatDefeatLossEntrySaveData
                {
                    origin = value.origin,
                    rewardType = value.rewardType,
                    targetId = value.targetId,
                    quality = value.quality,
                    instanceId = value.instanceId,
                    quantityBefore = value.quantity,
                    quantityLost = lost,
                    quantityKept = quantityKept
                };
                if (quantityKept > 0)
                {
                    value.quantity = quantityKept;
                    kept.Add(value);
                }
            }
            loss = new CombatDefeatLossSaveData
            {
                lossPercent = lossPercent,
                entries = breakdown
            };
            return true;
        }

        private static bool IsLossEligible(CombatRewardEntrySaveData value) =>
            value != null &&
            (string.Equals(
                 value.origin,
                 PendingResultOrigin.CombatLoot,
                 StringComparison.Ordinal) ||
             string.Equals(
                 value.origin,
                 PendingResultOrigin.ActivityLootInCombat,
                 StringComparison.Ordinal));

        private static void AddCopies(
            List<CombatRewardEntrySaveData> target,
            CombatRewardEntrySaveData[] source)
        {
            foreach (var value in source ??
                     Array.Empty<CombatRewardEntrySaveData>())
                if (value != null)
                    target.Add(CloneReward(value));
        }

        private static CombatRewardEntrySaveData CloneReward(
            CombatRewardEntrySaveData value) =>
            new CombatRewardEntrySaveData
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

        private static CombatOutcomeResult Failed(
            string executionId,
            string code,
            string message) =>
            new CombatOutcomeResult
            {
                Success = false,
                Code = code,
                Message = message,
                ExecutionId = executionId
            };
    }
}
