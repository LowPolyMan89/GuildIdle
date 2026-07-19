using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Crafting;
using GuildIdle.Editor.Configs;
using GuildIdle.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Editor.Crafting
{
    public sealed class CraftRuntimeServiceTests
    {
        private ConfigDatabase _database;
        private PlayerStateFactory _factory;
        private MemorySaveStorage _storage;

        [SetUp]
        public void SetUp()
        {
            _database = CreateDatabase();
            RuntimeConfigs.SetDatabaseForTests(_database);
            _factory = TestPlayerComposition.CreatePlayerStateFactory(_database);
            _storage = new MemorySaveStorage();
        }

        [Test]
        public void StartWithoutRecipeAggregatesMaterialsAcrossStorageStacksAndPersistsSnapshot()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 5);
            Seed(state, "resource_herb", 2);
            var fatigue = state.GetHeroFatigue("ren");
            var saves = _storage.SaveCalls;
            var events = new List<CraftStartedEvent>();
            var runtime = Runtime(state, events.Add);

            var result = runtime.Start(Request("craft_basic", "op-basic"));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Code, Is.EqualTo(CraftStartCode.Applied));
            Assert.That(result.Execution.status, Is.EqualTo(CraftExecutionStatus.Running));
            Assert.That(result.Execution.progressSeconds, Is.Zero);
            Assert.That(result.Execution.durationSeconds, Is.EqualTo(10));
            Assert.That(result.Execution.outputItemId, Is.EqualTo("consumable_roasted_rabbit_meat"));
            Assert.That(result.Execution.outputCount, Is.EqualTo(1));
            Assert.That(result.Execution.skillId, Is.EqualTo("skill_crafting"));
            Assert.That(result.Execution.skillExp, Is.EqualTo(2));
            Assert.That(result.Execution.fatigueCostPaid, Is.EqualTo(2));
            Assert.That(result.Execution.costsPaid, Is.True);
            Assert.That(result.Execution.paidCosts, Has.Length.EqualTo(2));
            Assert.That(FindCost(result.Execution, "resource_rabbit_meat").quantity, Is.EqualTo(3));
            Assert.That(state.GetItem("resource_rabbit_meat"), Is.EqualTo(2));
            Assert.That(state.GetItem("resource_herb"), Is.EqualTo(1));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue - 2));
            Assert.That(state.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo(result.ExecutionId));
            Assert.That(state.GetCraftExecutions(), Has.Length.EqualTo(1));
            Assert.That(_storage.SaveCalls, Is.EqualTo(saves + 1));
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(HasStartReceipt(state, "op-basic", result.ExecutionId), Is.True);
            Assert.Throws<NotSupportedException>(() => ((IList<CraftCostDescriptor>)result.Descriptor.PaidCosts).Clear());
            var mutatedSnapshot = state.GetCraftExecution(result.ExecutionId);
            mutatedSnapshot.durationSeconds++;
            Assert.That(state.UpdateCraftExecution(mutatedSnapshot), Is.False);
        }

        [Test]
        public void PartialAdvancePersistsProgressAndReplayDoesNotApplyDeltaTwice()
        {
            var state = StartBasic(out var runtime, out var executionId);
            var saves = _storage.SaveCalls;

            var first = runtime.Advance(executionId, 4d, "advance-partial", 1);
            var replay = runtime.Advance(executionId, 4d, "advance-partial", 1);
            var conflict = runtime.Advance(executionId, 1d, "advance-partial", 1);

            Assert.That(first.Success, Is.True);
            Assert.That(first.Code, Is.EqualTo(CraftAdvanceCode.Applied));
            Assert.That(first.ProgressSeconds, Is.EqualTo(4f));
            Assert.That(first.Completed, Is.False);
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.Code, Is.EqualTo(first.Code));
            Assert.That(replay.ProgressSeconds, Is.EqualTo(first.ProgressSeconds));
            Assert.That(replay.Completed, Is.EqualTo(first.Completed));
            Assert.That(replay.PendingResultId, Is.EqualTo(first.PendingResultId));
            Assert.That(conflict.Success, Is.False);
            Assert.That(conflict.Code, Is.EqualTo(CraftAdvanceCode.OperationReplayConflict));
            Assert.That(state.GetCraftExecution(executionId).progressSeconds, Is.EqualTo(4f));
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
            Assert.That(_storage.SaveCalls, Is.EqualTo(saves + 1));
        }

        [Test]
        public void PartialAdvanceReplayAfterCompletionReturnsOriginalOperationResult()
        {
            var state = StartBasic(out var runtime, out var executionId);
            var partial = runtime.Advance(executionId, 4d, "advance-replay-partial", 1);
            var completed = runtime.Advance(executionId, 6d, "advance-replay-complete", 2);
            var beforeReplay = JsonUtility.ToJson(state.ToSaveData());

            var replay = runtime.Advance(executionId, 4d, "advance-replay-partial", 1);

            Assert.That(completed.Success, Is.True);
            Assert.That(completed.Completed, Is.True);
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.Code, Is.EqualTo(partial.Code));
            Assert.That(replay.ProgressSeconds, Is.EqualTo(partial.ProgressSeconds));
            Assert.That(replay.Completed, Is.EqualTo(partial.Completed));
            Assert.That(replay.PendingResultId, Is.EqualTo(partial.PendingResultId));
            Assert.That(replay.Execution.status, Is.EqualTo(CraftExecutionStatus.Running));
            Assert.That(replay.Execution.progressSeconds, Is.EqualTo(4f));
            Assert.That(replay.Execution.pendingResultId, Is.Null.Or.Empty);
            Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(beforeReplay));
        }

        [Test]
        public void ExactBoundaryCreatesOneCraftResultWithoutApplyingRewardsOrReleasingHero()
        {
            var state = StartBasic(out _, out var executionId);
            var events = new List<CraftResultPendingEvent>();
            var runtime = Runtime(state, resultPendingEventSink: events.Add);
            var saves = _storage.SaveCalls;

            var completed = runtime.Advance(executionId, 10d, "advance-complete", 1);

            Assert.That(completed.Success, Is.True);
            Assert.That(completed.Code, Is.EqualTo(CraftAdvanceCode.ResultPending));
            Assert.That(completed.Completed, Is.True);
            Assert.That(completed.ProgressSeconds, Is.EqualTo(10f));
            Assert.That(_storage.SaveCalls, Is.EqualTo(saves + 1), "Completion must have one Save boundary.");
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].PendingResultId, Is.EqualTo(completed.PendingResultId));

            var execution = state.GetCraftExecution(executionId);
            Assert.That(execution.status, Is.EqualTo(CraftExecutionStatus.ResultPending));
            Assert.That(execution.completionRecorded, Is.True);
            Assert.That(execution.pendingResultId, Is.EqualTo(completed.PendingResultId));
            Assert.That(state.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo(executionId));
            Assert.That(state.GetItem("consumable_roasted_rabbit_meat"), Is.Zero);
            Assert.That(state.GetHeroSkillExp("ren", "skill_crafting"), Is.Zero);

            var result = state.PendingResults.Get(completed.PendingResultId);
            Assert.That(result.sourceType, Is.EqualTo(PendingResultSourceType.Craft));
            Assert.That(result.sourceId, Is.EqualTo("craft_basic"));
            Assert.That(result.sourceExecutionId, Is.EqualTo(executionId));
            Assert.That(result.ownerHeroId, Is.EqualTo("ren"));
            AssertEntry(result, "Item", "consumable_roasted_rabbit_meat", 1, PendingResultOrigin.CraftOutput);
            AssertEntry(result, "SkillExp", "skill_crafting", 2, PendingResultOrigin.CraftOutput);
        }

        [Test]
        public void PartialCraftClaimLeavesResultAndExecutionPending()
        {
            var state = CompleteBasic(out var executionId, out var result);
            var skillExp = FindEntry(result, RewardType.SkillExp);

            var claimed = state.PendingResults.ClaimQuantity(
                "claim-craft-partial",
                result.resultId,
                skillExp.entryId,
                1,
                result.revision,
                state.Storage.GetSnapshot().Revision);

            Assert.That(claimed.Success, Is.True);
            Assert.That(claimed.Resolved, Is.False);
            Assert.That(state.GetHeroSkillExp("ren", "skill_crafting"), Is.EqualTo(1));
            AssertEntry(claimed.Result, RewardType.SkillExp, "skill_crafting", 1, PendingResultOrigin.CraftOutput);
            AssertCraftResultStillPending(state, executionId, result.resultId);
            Assert.That(Runtime(state).Advance(executionId, 1d, "advance-after-partial-claim", 2).Code, Is.EqualTo(CraftAdvanceCode.ResultPending));
        }

        [Test]
        public void PartialCraftDiscardLeavesResultAndExecutionPending()
        {
            var state = CompleteBasic(out var executionId, out var result);
            var skillExp = FindEntry(result, RewardType.SkillExp);

            var discarded = state.PendingResults.DiscardQuantity(
                "discard-craft-partial",
                result.resultId,
                skillExp.entryId,
                1,
                result.revision);

            Assert.That(discarded.Success, Is.True);
            Assert.That(discarded.Resolved, Is.False);
            Assert.That(state.GetHeroSkillExp("ren", "skill_crafting"), Is.Zero);
            AssertEntry(discarded.Result, RewardType.SkillExp, "skill_crafting", 1, PendingResultOrigin.CraftOutput);
            AssertCraftResultStillPending(state, executionId, result.resultId);
            Assert.That(Runtime(state).Advance(executionId, 1d, "advance-after-partial-discard", 2).Code, Is.EqualTo(CraftAdvanceCode.ResultPending));
        }

        [Test]
        public void LastCraftClaimFinalizesExecutionReleasesHeroAndReplaysWithoutDuplicateRewards()
        {
            var state = CompleteBasic(out var executionId, out var result);
            var operationId = "claim-craft-final";
            var storageBefore = state.Storage.GetSnapshot();
            var savesBefore = _storage.SaveCalls;
            var events = new List<PendingResultResolvedEvent>();
            state.PendingResults.Resolved += events.Add;

            var claimed = state.PendingResults.ClaimAll(
                operationId,
                result.resultId,
                result.revision,
                storageBefore.Revision);
            var replay = state.PendingResults.ClaimAll(
                operationId,
                result.resultId,
                result.revision,
                storageBefore.Revision);

            Assert.That(claimed.Success, Is.True);
            Assert.That(claimed.Resolved, Is.True);
            Assert.That(claimed.Code, Is.EqualTo("Resolved"));
            Assert.That(state.GetItem("consumable_roasted_rabbit_meat"), Is.EqualTo(1));
            Assert.That(state.GetHeroSkillExp("ren", "skill_crafting"), Is.EqualTo(2));
            AssertCraftFinalized(state, executionId, result.resultId);
            Assert.That(HasOperationReceipt(state, result.resultId, operationId), Is.True);
            Assert.That(_storage.SaveCalls, Is.EqualTo(savesBefore + 1));
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].ResultId, Is.EqualTo(result.resultId));
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.Resolved, Is.True);
            Assert.That(state.GetItem("consumable_roasted_rabbit_meat"), Is.EqualTo(1));
            Assert.That(state.GetHeroSkillExp("ren", "skill_crafting"), Is.EqualTo(2));
            Assert.That(_storage.SaveCalls, Is.EqualTo(savesBefore + 1));
            Assert.That(events, Has.Count.EqualTo(1));
        }

        [Test]
        public void LastCraftDiscardFinalizesThroughTheSamePathWithoutApplyingRewards()
        {
            var state = CompleteBasic(out var executionId, out var result);
            var operationId = "discard-craft-final";
            var savesBefore = _storage.SaveCalls;

            var discarded = state.PendingResults.DiscardAll(operationId, result.resultId, result.revision);

            Assert.That(discarded.Success, Is.True);
            Assert.That(discarded.Resolved, Is.True);
            Assert.That(state.GetItem("consumable_roasted_rabbit_meat"), Is.Zero);
            Assert.That(state.GetHeroSkillExp("ren", "skill_crafting"), Is.Zero);
            AssertCraftFinalized(state, executionId, result.resultId);
            Assert.That(HasOperationReceipt(state, result.resultId, operationId), Is.True);
            Assert.That(_storage.SaveCalls, Is.EqualTo(savesBefore + 1));
        }

        [Test]
        public void ItemOutputAndSkillExpResolveIndependentlyBeforeFinalization()
        {
            var state = CompleteBasic(out var executionId, out var result);
            var output = FindEntry(result, RewardType.Item);
            var discardedOutput = state.PendingResults.DiscardQuantity(
                "discard-craft-output",
                result.resultId,
                output.entryId,
                output.quantity,
                result.revision);

            var claimedExp = state.PendingResults.ClaimAll(
                "claim-craft-exp-final",
                result.resultId,
                discardedOutput.ResultRevision,
                state.Storage.GetSnapshot().Revision);

            Assert.That(discardedOutput.Success, Is.True);
            Assert.That(discardedOutput.Resolved, Is.False);
            Assert.That(claimedExp.Success, Is.True);
            Assert.That(claimedExp.Resolved, Is.True);
            Assert.That(state.GetItem("consumable_roasted_rabbit_meat"), Is.Zero);
            Assert.That(state.GetHeroSkillExp("ren", "skill_crafting"), Is.EqualTo(2));
            AssertCraftFinalized(state, executionId, result.resultId);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FinalCraftResolutionAndReplayWorkAfterLoad(bool claim)
        {
            CompleteBasic(out var executionId, out var result);
            var loaded = SaveService.Load(_factory, _storage);
            var loadedResult = loaded.PendingResults.Get(result.resultId);
            var operationId = claim ? "claim-craft-final-loaded" : "discard-craft-final-loaded";
            var expectedStorageRevision = loaded.Storage.GetSnapshot().Revision;

            var resolved = claim
                ? loaded.PendingResults.ClaimAll(operationId, result.resultId, loadedResult.revision, expectedStorageRevision)
                : loaded.PendingResults.DiscardAll(operationId, result.resultId, loadedResult.revision);
            var restored = SaveService.Load(_factory, _storage);
            var replay = claim
                ? restored.PendingResults.ClaimAll(operationId, result.resultId, loadedResult.revision, expectedStorageRevision)
                : restored.PendingResults.DiscardAll(operationId, result.resultId, loadedResult.revision);

            Assert.That(resolved.Success, Is.True);
            Assert.That(resolved.Resolved, Is.True);
            AssertCraftFinalized(loaded, executionId, result.resultId);
            AssertCraftFinalized(restored, executionId, result.resultId);
            Assert.That(restored.GetItem("consumable_roasted_rabbit_meat"), Is.EqualTo(claim ? 1 : 0));
            Assert.That(restored.GetHeroSkillExp("ren", "skill_crafting"), Is.EqualTo(claim ? 2 : 0));
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.Resolved, Is.True);
        }

        [Test]
        public void FinalCraftClaimSaveFailureRestoresRewardsResultSourceExecutionOccupationAndReceipt()
        {
            var state = CompleteBasic(out var executionId, out var result);
            var operationId = "claim-craft-final-save-failure";
            var before = JsonUtility.ToJson(state.ToSaveData());
            var persistedBefore = _storage.GetString(SaveService.SaveKey, string.Empty);
            var events = new List<PendingResultResolvedEvent>();
            state.PendingResults.Resolved += events.Add;
            _storage.ThrowOnSaveCall = _storage.SaveCalls + 1;
            LogAssert.Expect(LogType.Error, "[SaveService] Failed to save player state. simulated save flush failure");

            var failed = state.PendingResults.ClaimAll(
                operationId,
                result.resultId,
                result.revision,
                state.Storage.GetSnapshot().Revision);

            Assert.That(failed.Success, Is.False);
            Assert.That(failed.Code, Is.EqualTo("SaveFailed"));
            Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(before));
            Assert.That(_storage.GetString(SaveService.SaveKey, string.Empty), Is.EqualTo(persistedBefore));
            Assert.That(state.GetItem("consumable_roasted_rabbit_meat"), Is.Zero);
            Assert.That(state.GetHeroSkillExp("ren", "skill_crafting"), Is.Zero);
            Assert.That(HasOperationReceipt(state, result.resultId, operationId), Is.False);
            Assert.That(events, Is.Empty);
            AssertCraftResultStillPending(state, executionId, result.resultId);
        }

        [Test]
        public void OccupationMismatchRejectsFinalizationBeforeMutationsAndDoesNotReleaseForeignOccupation()
        {
            var state = CompleteBasic(out var executionId, out var result);
            Assert.That(state.ClearHeroBusy("ren", executionId), Is.True);
            Assert.That(state.SetHeroBusy("ren", "foreign-execution"), Is.True);
            var before = JsonUtility.ToJson(state.ToSaveData());

            var rejected = state.PendingResults.ClaimAll(
                "claim-craft-occupation-mismatch",
                result.resultId,
                result.revision,
                state.Storage.GetSnapshot().Revision);

            Assert.That(rejected.Success, Is.False);
            Assert.That(rejected.Code, Is.EqualTo("SourceNotClaimable"));
            Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(before));
            Assert.That(state.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo("foreign-execution"));
            Assert.That(state.GetItem("consumable_roasted_rabbit_meat"), Is.Zero);
            Assert.That(state.GetHeroSkillExp("ren", "skill_crafting"), Is.Zero);
        }

        [Test]
        public void SourceFinalizationFailureRollsBackRewardsResultExecutionAndOccupation()
        {
            var state = CompleteBasic(out var executionId, out var result);
            state.PendingResults.RegisterSourceHandler(new FailingCraftFinalizationHandler());
            var before = JsonUtility.ToJson(state.ToSaveData());

            var failed = state.PendingResults.ClaimAll(
                "claim-craft-source-failure",
                result.resultId,
                result.revision,
                state.Storage.GetSnapshot().Revision);

            Assert.That(failed.Success, Is.False);
            Assert.That(failed.Code, Is.EqualTo("SourceResolutionFailed"));
            Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(before));
            Assert.That(state.GetItem("consumable_roasted_rabbit_meat"), Is.Zero);
            Assert.That(state.GetHeroSkillExp("ren", "skill_crafting"), Is.Zero);
            AssertCraftResultStillPending(state, executionId, result.resultId);
        }

        [Test]
        public void RewardMutationFailureRollsBackSkillExpAndLeavesCraftPending()
        {
            var state = CompleteBasic(out var executionId, out var result);
            FillStorage(state);
            var before = JsonUtility.ToJson(state.ToSaveData());

            var failed = state.PendingResults.ClaimAll(
                "claim-craft-full-storage",
                result.resultId,
                result.revision,
                state.Storage.GetSnapshot().Revision);

            Assert.That(failed.Success, Is.False);
            Assert.That(failed.Code, Is.EqualTo("Rejected"));
            Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(before));
            Assert.That(state.GetHeroSkillExp("ren", "skill_crafting"), Is.Zero);
            AssertCraftResultStillPending(state, executionId, result.resultId);
        }

        [Test]
        public void LargeDeltaAndFurtherAdvanceCreateNoDuplicateResult()
        {
            var state = StartBasic(out var runtime, out var executionId);

            var completed = runtime.Advance(executionId, double.MaxValue, "advance-large", 1);
            var saves = _storage.SaveCalls;
            var replay = runtime.Advance(executionId, double.MaxValue, "advance-large", 1);
            var further = runtime.Advance(executionId, 100d, "advance-after-completion", 2);

            Assert.That(completed.Success, Is.True);
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(further.Success, Is.True);
            Assert.That(further.Code, Is.EqualTo(CraftAdvanceCode.ResultPending));
            Assert.That(state.PendingResults.GetAll(), Has.Length.EqualTo(1));
            Assert.That(_storage.SaveCalls, Is.EqualTo(saves));
        }

        [Test]
        public void ZeroDeltaIsAcceptedAndClockRollbackCallerCannotReduceProgress()
        {
            var state = StartBasic(out var runtime, out var executionId);
            Assert.That(runtime.Advance(executionId, 3d, "advance-first", 1).Success, Is.True);

            var zero = runtime.Advance(executionId, 0d, "advance-zero", 2);
            var replayConflict = runtime.Advance(executionId, 1d, "advance-zero", 2);
            var negative = runtime.Advance(executionId, -1d, "advance-negative", 3);

            Assert.That(zero.Success, Is.True);
            Assert.That(zero.ProgressSeconds, Is.EqualTo(3f));
            Assert.That(replayConflict.Code, Is.EqualTo(CraftAdvanceCode.OperationReplayConflict));
            Assert.That(negative.Code, Is.EqualTo(CraftAdvanceCode.InvalidDelta));
            Assert.That(state.GetCraftExecution(executionId).progressSeconds, Is.EqualTo(3f));
        }

        [Test]
        public void AdvanceWithoutSequenceAndAdvanceWithGapAreRejectedWithoutMutation()
        {
            var state = StartBasic(out var runtime, out var executionId);
            var before = JsonUtility.ToJson(state.ToSaveData());

            var missingSequence = runtime.Advance(executionId, 1d, "advance-without-sequence");
            var sequenceGap = runtime.Advance(executionId, 1d, "advance-sequence-gap", 2);

            Assert.That(missingSequence.Success, Is.False);
            Assert.That(missingSequence.Code, Is.EqualTo(CraftAdvanceCode.OperationSequenceRequired));
            Assert.That(sequenceGap.Success, Is.False);
            Assert.That(sequenceGap.Code, Is.EqualTo(CraftAdvanceCode.OperationSequenceGap));
            Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(before));
        }

        [Test]
        public void CompletionUsesImmutableExecutionSnapshotAfterCraftConfigChanges()
        {
            var state = StartBasic(out var runtime, out var executionId);
            var definition = FindDefinition("craft_basic");
            definition.craftDurationSec = 999;
            definition.targetItemId = "consumable_other";
            definition.outputCount = 7;
            definition.skillExp = 99;

            var completed = runtime.Advance(executionId, 10d, "advance-snapshot", 1);

            Assert.That(completed.Success, Is.True);
            var result = state.PendingResults.Get(completed.PendingResultId);
            AssertEntry(result, "Item", "consumable_roasted_rabbit_meat", 1, PendingResultOrigin.CraftOutput);
            AssertEntry(result, "SkillExp", "skill_crafting", 2, PendingResultOrigin.CraftOutput);
        }

        [Test]
        public void FullStorageDoesNotBlockCraftCompletion()
        {
            var state = StartBasic(out var runtime, out var executionId);
            FillStorage(state);
            Assert.That(state.Storage.GetSnapshot().FreeSlots, Is.Zero);

            var completed = runtime.Advance(executionId, 10d, "advance-full-storage", 1);

            Assert.That(completed.Success, Is.True);
            Assert.That(state.PendingResults.Get(completed.PendingResultId), Is.Not.Null);
            Assert.That(state.GetItem("consumable_roasted_rabbit_meat"), Is.Zero);
        }

        [Test]
        public void RewardValidationFailureLeavesRunningExecutionUnchanged()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 3);
            Seed(state, "resource_herb", 1);
            var runtime = Runtime(state);
            var started = runtime.Start(Request("craft_invalid_reward", "start-invalid-reward"));
            Assert.That(started.Success, Is.True);
            var saves = _storage.SaveCalls;

            var advanced = runtime.Advance(started.ExecutionId, 10d, "advance-invalid-reward", 1);

            Assert.That(advanced.Success, Is.False);
            Assert.That(advanced.Code, Is.EqualTo(CraftAdvanceCode.RewardValidationFailure));
            AssertRunningWithoutResult(state, started.ExecutionId, 0f);
            Assert.That(_storage.SaveCalls, Is.EqualTo(saves));
        }

        [Test]
        public void PendingResultCreationFailureRollsBackProgressCompletionAndReceipt()
        {
            var state = StartBasic(out _, out var executionId);
            var fault = new FaultInjectingCraftState(new PlayerStateCraftAdapter(state)) { FailPendingResultCreation = true };

            var advanced = new CraftRuntimeService(_database.Crafts, fault).Advance(executionId, 10d, "advance-result-failure", 1);

            Assert.That(advanced.Success, Is.False);
            Assert.That(advanced.Code, Is.EqualTo(CraftAdvanceCode.PendingResultFailure));
            AssertRunningWithoutResult(state, executionId, 0f);
            Assert.That(state.GetCraftExecution(executionId).advanceReceipts, Is.Empty);
        }

        [Test]
        public void CompletionSaveFailureRestoresMemoryAndPersistedRunningState()
        {
            var state = StartBasic(out _, out var executionId);
            var events = new List<CraftResultPendingEvent>();
            var runtime = Runtime(state, resultPendingEventSink: events.Add);
            _storage.ThrowOnSaveCall = _storage.SaveCalls + 1;
            LogAssert.Expect(LogType.Error, "[SaveService] Failed to save player state. simulated save flush failure");

            var advanced = runtime.Advance(executionId, 10d, "advance-save-failure", 1);

            Assert.That(advanced.Success, Is.False);
            Assert.That(advanced.Code, Is.EqualTo(CraftAdvanceCode.SaveFailure));
            Assert.That(events, Is.Empty);
            AssertRunningWithoutResult(state, executionId, 0f);
            Assert.That(state.GetCraftExecution(executionId).advanceReceipts, Is.Empty);
            var loaded = SaveService.Load(_factory, _storage);
            AssertRunningWithoutResult(loaded, executionId, 0f);
        }

        [Test]
        public void SaveLoadBeforeAndAfterBoundaryDoesNotDuplicateCraftResult()
        {
            var state = StartBasic(out var runtime, out var executionId);
            Assert.That(runtime.Advance(executionId, 4d, "advance-before-load", 1).Success, Is.True);

            var loadedBefore = SaveService.Load(_factory, _storage);
            var loadedRuntime = Runtime(loadedBefore);
            var replay = loadedRuntime.Advance(executionId, 4d, "advance-before-load", 1);
            var completed = loadedRuntime.Advance(executionId, 6d, "advance-after-load", 2);
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(completed.Success, Is.True);

            var loadedAfter = SaveService.Load(_factory, _storage);
            var noOp = Runtime(loadedAfter).Advance(executionId, 10d, "advance-loaded-result", 3);

            Assert.That(noOp.Success, Is.True);
            Assert.That(noOp.Code, Is.EqualTo(CraftAdvanceCode.ResultPending));
            Assert.That(loadedAfter.PendingResults.GetAll(), Has.Length.EqualTo(1));
            Assert.That(loadedAfter.GetCraftExecution(executionId).pendingResultId, Is.EqualTo(completed.PendingResultId));
        }

        [Test]
        public void CompletionBoundarySequenceReplaysAfterReceiptEvictionAndLoad()
        {
            var state = CompleteBasic(out var executionId, out var result);
            var save = state.ToSaveData();
            save.craftRuntime.executions[0].advanceReceipts = Array.Empty<CraftAdvanceReceiptSaveData>();
            var loaded = _factory.Create(save);
            var events = new List<CraftResultPendingEvent>();
            var before = JsonUtility.ToJson(loaded.ToSaveData());

            var replay = Runtime(loaded, resultPendingEventSink: events.Add)
                .Advance(executionId, 10d, "evicted-completion-replay", 1);

            Assert.That(replay.Success, Is.True, replay.Message);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.Completed, Is.True);
            Assert.That(replay.Code, Is.EqualTo(CraftAdvanceCode.ResultPending));
            Assert.That(replay.PendingResultId, Is.EqualTo(result.resultId));
            Assert.That(JsonUtility.ToJson(loaded.ToSaveData()), Is.EqualTo(before));
            Assert.That(events, Is.Empty);
        }

        [Test]
        public void MissingLinkedResultAfterLoadIsIntegrityFailureAndIsNotRerolled()
        {
            var state = StartBasic(out var runtime, out var executionId);
            Assert.That(runtime.Advance(executionId, 10d, "advance-before-corruption", 1).Success, Is.True);
            var corrupted = state.ToSaveData();
            corrupted.pendingResults = Array.Empty<PendingResultSaveData>();
            LogAssert.Expect(LogType.Error, $"[PlayerState] Craft execution '{executionId}' has a Pending source but no linked PendingResult and remains blocked for manual recovery.");
            var corruptedState = _factory.Create(corrupted);

            var advanced = Runtime(corruptedState).Advance(executionId, 1d, "advance-corrupt-result", 2);

            Assert.That(advanced.Success, Is.False);
            Assert.That(advanced.Code, Is.EqualTo(CraftAdvanceCode.DataIntegrityFailure));
            Assert.That(corruptedState.PendingResults.GetAll(), Is.Empty);
            Assert.That(corruptedState.GetCraftExecution(executionId).status, Is.EqualTo(CraftExecutionStatus.ResultPending));
            Assert.That(corruptedState.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo(executionId));
            Assert.That(FindCraftSourceReference(corruptedState, executionId).state, Is.EqualTo(PendingResultSourceState.Blocked));
        }

        [Test]
        public void ResolvedSourceWithLeftoverExecutionAndNoResultIsReconciledWithoutRewards()
        {
            var state = CompleteBasic(out var executionId, out var result);
            var interrupted = state.ToSaveData();
            interrupted.pendingResults = Array.Empty<PendingResultSaveData>();
            FindCraftSourceReference(interrupted, executionId).state = PendingResultSourceState.Resolved;
            _storage.SetString(SaveService.SaveKey, JsonUtility.ToJson(interrupted));
            _storage.Save();
            var savesBefore = _storage.SaveCalls;

            var reconciled = SaveService.Load(_factory, _storage);
            var restored = SaveService.Load(_factory, _storage);

            AssertCraftFinalized(reconciled, executionId, result.resultId);
            AssertCraftFinalized(restored, executionId, result.resultId);
            Assert.That(reconciled.GetItem("consumable_roasted_rabbit_meat"), Is.Zero);
            Assert.That(reconciled.GetHeroSkillExp("ren", "skill_crafting"), Is.Zero);
            Assert.That(_storage.SaveCalls, Is.EqualTo(savesBefore + 1));
        }

        [Test]
        public void BlockedCraftSourceIsNotReconciledOrReleased()
        {
            var state = CompleteBasic(out var executionId, out _);
            var blocked = state.ToSaveData();
            blocked.pendingResults = Array.Empty<PendingResultSaveData>();
            FindCraftSourceReference(blocked, executionId).state = PendingResultSourceState.Blocked;

            var restored = _factory.Create(blocked);

            Assert.That(restored.GetCraftExecution(executionId), Is.Not.Null);
            Assert.That(restored.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo(executionId));
            Assert.That(FindCraftSourceReference(restored, executionId).state, Is.EqualTo(PendingResultSourceState.Blocked));
        }

        [TestCase("other-output-item")]
        [TestCase("excess-output-quantity")]
        [TestCase("other-skill")]
        [TestCase("excess-skill-exp")]
        [TestCase("additional-reward")]
        [TestCase("wrong-origin")]
        public void CorruptedSavedCraftResultEntriesFailImmutableSnapshotValidation(string corruption)
        {
            var state = CompleteBasic(out var executionId, out _);
            var corrupted = state.ToSaveData();
            CorruptCraftResult(corrupted, corruption);
            ExpectCorruptCraftResultLoadLog(corruption);

            var loaded = _factory.Create(corrupted);
            var beforeAdvance = JsonUtility.ToJson(loaded.ToSaveData());

            var advanced = Runtime(loaded).Advance(executionId, 1d, $"advance-corrupt-{corruption}", 2);

            Assert.That(advanced.Success, Is.False);
            Assert.That(advanced.Code, Is.EqualTo(CraftAdvanceCode.DataIntegrityFailure));
            Assert.That(JsonUtility.ToJson(loaded.ToSaveData()), Is.EqualTo(beforeAdvance));
            Assert.That(loaded.GetCraftExecution(executionId).status, Is.EqualTo(CraftExecutionStatus.ResultPending));
        }

        [TestCase("other-output-item")]
        [TestCase("excess-output-quantity")]
        [TestCase("other-skill")]
        [TestCase("excess-skill-exp")]
        [TestCase("additional-reward")]
        [TestCase("wrong-origin")]
        [TestCase("duplicate-item")]
        [TestCase("duplicate-skill-exp")]
        public void CorruptedCraftResultCannotBeClaimedDirectlyWithoutAdvance(string corruption)
        {
            var state = CompleteBasic(out var executionId, out _);
            var corrupted = state.ToSaveData();
            var resultId = corrupted.pendingResults[0].resultId;
            var entryId = CorruptCraftResult(corrupted, corruption);
            _storage.SetString(SaveService.SaveKey, JsonUtility.ToJson(corrupted));
            _storage.Save();
            ExpectCorruptCraftResultLoadLog(corruption);
            var loaded = SaveService.Load(_factory, _storage);
            var loadedResult = loaded.PendingResults.Get(resultId);
            var expectedRevision = loadedResult?.revision ?? corrupted.pendingResults[0].revision;
            var before = JsonUtility.ToJson(loaded.ToSaveData());
            var persistedBefore = _storage.GetString(SaveService.SaveKey, string.Empty);
            var savesBefore = _storage.SaveCalls;
            var storageRevisionBefore = loaded.Storage.GetSnapshot().Revision;
            var expBefore = loaded.GetHeroSkillExp("ren", "skill_crafting");
            var operationId = $"claim-corrupt-direct:{corruption}";

            var rejected = loaded.PendingResults.ClaimQuantity(
                operationId,
                resultId,
                entryId,
                1,
                expectedRevision,
                storageRevisionBefore);

            Assert.That(rejected.Success, Is.False);
            Assert.That(rejected.Code == "SourceNotClaimable" || rejected.Code == "ResultNotFound", Is.True);
            Assert.That(JsonUtility.ToJson(loaded.ToSaveData()), Is.EqualTo(before));
            Assert.That(_storage.GetString(SaveService.SaveKey, string.Empty), Is.EqualTo(persistedBefore));
            Assert.That(_storage.SaveCalls, Is.EqualTo(savesBefore));
            Assert.That(loaded.Storage.GetSnapshot().Revision, Is.EqualTo(storageRevisionBefore));
            Assert.That(loaded.GetHeroSkillExp("ren", "skill_crafting"), Is.EqualTo(expBefore));
            Assert.That(loaded.GetItem("consumable_roasted_rabbit_meat"), Is.Zero);
            Assert.That(loadedResult == null || loaded.PendingResults.Get(resultId).revision == loadedResult.revision, Is.True);
            Assert.That(HasOperationReceipt(loaded, resultId, operationId), Is.False);
            AssertCraftExecutionStillOccupied(loaded, executionId, resultId);
        }

        [Test]
        public void CorruptedCraftResultCannotDiscardInvalidEntryToBypassValidation()
        {
            var state = CompleteBasic(out var executionId, out _);
            var corrupted = state.ToSaveData();
            var resultId = corrupted.pendingResults[0].resultId;
            var invalidEntryId = CorruptCraftResult(corrupted, "additional-reward");
            var originalEntryId = FindEntry(corrupted.pendingResults[0], RewardType.Item).entryId;
            _storage.SetString(SaveService.SaveKey, JsonUtility.ToJson(corrupted));
            _storage.Save();
            ExpectCorruptCraftResultLoadLog("additional-reward");
            var loaded = SaveService.Load(_factory, _storage);
            var loadedResult = loaded.PendingResults.Get(resultId);
            var before = JsonUtility.ToJson(loaded.ToSaveData());
            var persistedBefore = _storage.GetString(SaveService.SaveKey, string.Empty);
            var savesBefore = _storage.SaveCalls;

            var discard = loaded.PendingResults.DiscardQuantity(
                "discard-corrupt-extra",
                resultId,
                invalidEntryId,
                1,
                loadedResult.revision);
            var claim = loaded.PendingResults.ClaimQuantity(
                "claim-after-corrupt-discard",
                resultId,
                originalEntryId,
                1,
                loadedResult.revision,
                loaded.Storage.GetSnapshot().Revision);

            Assert.That(discard.Success, Is.False);
            Assert.That(discard.Code, Is.EqualTo("SourceNotClaimable"));
            Assert.That(claim.Success, Is.False);
            Assert.That(claim.Code, Is.EqualTo("SourceNotClaimable"));
            Assert.That(JsonUtility.ToJson(loaded.ToSaveData()), Is.EqualTo(before));
            Assert.That(_storage.GetString(SaveService.SaveKey, string.Empty), Is.EqualTo(persistedBefore));
            Assert.That(_storage.SaveCalls, Is.EqualTo(savesBefore));
            Assert.That(HasOperationReceipt(loaded, resultId, "discard-corrupt-extra"), Is.False);
            Assert.That(HasOperationReceipt(loaded, resultId, "claim-after-corrupt-discard"), Is.False);
            AssertCraftExecutionStillOccupied(loaded, executionId, resultId);
        }

        [Test]
        public void ValidPartiallyResolvedCraftResultRemainsClaimableWithoutAdvance()
        {
            var state = CompleteBasic(out var executionId, out var result);
            var output = FindEntry(result, RewardType.Item);
            var discarded = state.PendingResults.DiscardQuantity(
                "discard-valid-output",
                result.resultId,
                output.entryId,
                output.quantity,
                result.revision);
            var skillExp = FindEntry(discarded.Result, RewardType.SkillExp);

            var claimed = state.PendingResults.ClaimQuantity(
                "claim-valid-partial",
                result.resultId,
                skillExp.entryId,
                1,
                discarded.ResultRevision,
                state.Storage.GetSnapshot().Revision);

            Assert.That(discarded.Success, Is.True);
            Assert.That(claimed.Success, Is.True);
            Assert.That(claimed.Resolved, Is.False);
            Assert.That(state.GetHeroSkillExp("ren", "skill_crafting"), Is.EqualTo(1));
            AssertEntry(claimed.Result, RewardType.SkillExp, "skill_crafting", 1, PendingResultOrigin.CraftOutput);
            AssertCraftResultStillPending(state, executionId, result.resultId);
        }

        [Test]
        public void ValidPartiallyResolvedCraftResultRemainsClaimableAfterSaveLoad()
        {
            var state = CompleteBasic(out var executionId, out var result);
            var output = FindEntry(result, RewardType.Item);
            var discarded = state.PendingResults.DiscardQuantity(
                "discard-valid-output-before-load",
                result.resultId,
                output.entryId,
                output.quantity,
                result.revision);
            Assert.That(discarded.Success, Is.True);
            var loaded = SaveService.Load(_factory, _storage);
            var loadedResult = loaded.PendingResults.Get(result.resultId);
            var skillExp = FindEntry(loadedResult, RewardType.SkillExp);

            var claimed = loaded.PendingResults.ClaimQuantity(
                "claim-valid-partial-after-load",
                result.resultId,
                skillExp.entryId,
                1,
                loadedResult.revision,
                loaded.Storage.GetSnapshot().Revision);

            Assert.That(claimed.Success, Is.True);
            Assert.That(claimed.Resolved, Is.False);
            Assert.That(loaded.GetHeroSkillExp("ren", "skill_crafting"), Is.EqualTo(1));
            AssertEntry(claimed.Result, RewardType.SkillExp, "skill_crafting", 1, PendingResultOrigin.CraftOutput);
            AssertCraftResultStillPending(loaded, executionId, result.resultId);
        }

        [Test]
        public void SavedPartiallyResolvedCraftResultMatchesImmutableSnapshot()
        {
            var state = CompleteBasic(out var executionId, out _);
            var saved = state.ToSaveData();
            var savedResult = saved.pendingResults[0];
            var skillExp = FindEntry(savedResult, RewardType.SkillExp);
            skillExp.quantity = 1;
            savedResult.entries = new[] { skillExp };
            var loaded = _factory.Create(saved);

            var advanced = Runtime(loaded).Advance(executionId, 1d, "advance-valid-partial-result", 2);

            Assert.That(advanced.Success, Is.True);
            Assert.That(advanced.Code, Is.EqualTo(CraftAdvanceCode.ResultPending));
            Assert.That(advanced.Completed, Is.True);
            AssertEntry(loaded.PendingResults.Get(savedResult.resultId), RewardType.SkillExp, "skill_crafting", 1, PendingResultOrigin.CraftOutput);
            AssertCraftResultStillPending(loaded, executionId, savedResult.resultId);
        }

        [Test]
        public void OnlineAndOfflineCallersShareTheSameAdvanceApi()
        {
            var state = StartBasic(out var onlineRuntime, out var executionId);
            Assert.That(onlineRuntime.Advance(executionId, 4d, "online-delta", 1).Success, Is.True);

            var offlineRuntime = Runtime(state);
            var completed = offlineRuntime.Advance(executionId, 6d, "offline-delta", 2);

            Assert.That(completed.Success, Is.True);
            Assert.That(completed.Completed, Is.True);
            Assert.That(state.PendingResults.GetAll(), Has.Length.EqualTo(1));
        }

        [Test]
        public void CookRoastedRabbitMeatSnapshotCreatesExpectedItemAndSkillExpAfterTenSeconds()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 3);
            Seed(state, "resource_herb", 1);
            var runtime = Runtime(state);
            var started = runtime.Start(Request("cook_roasted_rabbit_meat", "start-cook-rabbit"));

            var completed = runtime.Advance(started.ExecutionId, 10d, "advance-cook-rabbit", 1);

            Assert.That(completed.Success, Is.True);
            var result = state.PendingResults.Get(completed.PendingResultId);
            AssertEntry(result, "Item", "consumable_roasted_rabbit_meat", 1, PendingResultOrigin.CraftOutput);
            AssertEntry(result, "SkillExp", "skill_crafting", 2, PendingResultOrigin.CraftOutput);
        }

        [Test]
        public void NonConsumableRecipeIsRequiredButRemainsInStorage()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 1);
            Seed(state, "recipe_roasting", 1);

            var result = Runtime(state).Start(Request("craft_recipe_kept", "op-recipe-kept"));

            Assert.That(result.Success, Is.True);
            Assert.That(state.GetItem("resource_rabbit_meat"), Is.Zero);
            Assert.That(state.GetItem("recipe_roasting"), Is.EqualTo(1));
            Assert.That(result.Execution.recipe.requiredItemId, Is.EqualTo("recipe_roasting"));
            Assert.That(result.Execution.recipe.requiredCount, Is.EqualTo(1));
            Assert.That(result.Execution.recipe.consume, Is.False);
            Assert.That(result.Execution.recipe.consumedCount, Is.Zero);
            Assert.That(FindCost(result.Execution, "recipe_roasting"), Is.Null);
        }

        [Test]
        public void ConsumableRecipeIsAggregatedIntoPaidBatchAndRemoved()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 1);
            Seed(state, "recipe_roasting", 2);

            var result = Runtime(state).Start(Request("craft_recipe_consumed", "op-recipe-consumed"));

            Assert.That(result.Success, Is.True);
            Assert.That(state.GetItem("recipe_roasting"), Is.Zero);
            Assert.That(FindCost(result.Execution, "recipe_roasting").quantity, Is.EqualTo(2));
            Assert.That(FindCost(result.Execution, "recipe_roasting").kind, Is.EqualTo(CraftPaidCostKind.Recipe));
            Assert.That(result.Execution.recipe.consumedCount, Is.EqualTo(2));
        }

        [Test]
        public void FailureOnLastRemovalRollsBackWholeCostBatch()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 3);
            Seed(state, "resource_herb", 1);
            var fatigue = state.GetHeroFatigue("ren");
            var fault = new FaultInjectingCraftState(new PlayerStateCraftAdapter(state)) { FailConsumeCall = 2 };

            var result = new CraftRuntimeService(_database.Crafts, fault).Start(Request("craft_basic", "op-last-removal"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(CraftStartCode.TransactionFailure));
            Assert.That(state.GetItem("resource_rabbit_meat"), Is.EqualTo(3));
            Assert.That(state.GetItem("resource_herb"), Is.EqualTo(1));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.GetCraftExecutions(), Is.Empty);
            Assert.That(HasStartReceipt(state, "op-last-removal", null), Is.False);
        }

        [TestCase("occupation")]
        [TestCase("execution")]
        public void OccupationOrExecutionCreationFailureRestoresItemsAndFatigue(string failure)
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 3);
            Seed(state, "resource_herb", 1);
            var fatigue = state.GetHeroFatigue("ren");
            var fault = new FaultInjectingCraftState(new PlayerStateCraftAdapter(state))
            {
                FailOccupation = failure == "occupation",
                FailAddExecution = failure == "execution"
            };

            var result = new CraftRuntimeService(_database.Crafts, fault).Start(Request("craft_basic", $"op-{failure}-failure"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(CraftStartCode.TransactionFailure));
            Assert.That(state.GetItem("resource_rabbit_meat"), Is.EqualTo(3));
            Assert.That(state.GetItem("resource_herb"), Is.EqualTo(1));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.GetCraftExecutions(), Is.Empty);
        }

        [Test]
        public void SaveFailureRestoresStorageFatigueOccupationExecutionReceiptAndSuppressesEvent()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 3);
            Seed(state, "resource_herb", 1);
            var fatigue = state.GetHeroFatigue("ren");
            var events = new List<CraftStartedEvent>();
            _storage.ThrowOnSet = true;
            LogAssert.Expect(LogType.Error, "[SaveService] Failed to save player state. simulated save failure");

            var result = Runtime(state, events.Add).Start(Request("craft_basic", "op-save-failure"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(CraftStartCode.SaveFailure));
            Assert.That(state.GetItem("resource_rabbit_meat"), Is.EqualTo(3));
            Assert.That(state.GetItem("resource_herb"), Is.EqualTo(1));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.GetCraftExecutions(), Is.Empty);
            Assert.That(HasStartReceipt(state, "op-save-failure", null), Is.False);
            Assert.That(events, Is.Empty);
        }

        [Test]
        public void SaveFailureAfterSetStringRestoresPersistedStateAndSuppressesEvent()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 3);
            Seed(state, "resource_herb", 1);
            Assert.That(state.Save(), Is.True);
            var fatigue = state.GetHeroFatigue("ren");
            var events = new List<CraftStartedEvent>();
            _storage.ThrowOnSaveCall = _storage.SaveCalls + 1;
            LogAssert.Expect(LogType.Error, "[SaveService] Failed to save player state. simulated save flush failure");

            var result = Runtime(state, events.Add).Start(Request("craft_basic", "op-late-save-failure"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(CraftStartCode.SaveFailure));
            Assert.That(state.GetItem("resource_rabbit_meat"), Is.EqualTo(3));
            Assert.That(state.GetItem("resource_herb"), Is.EqualTo(1));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.GetCraftExecutions(), Is.Empty);
            Assert.That(HasStartReceipt(state, "op-late-save-failure", null), Is.False);
            Assert.That(events, Is.Empty);

            var loaded = SaveService.Load(_factory, _storage);
            Assert.That(loaded.GetItem("resource_rabbit_meat"), Is.EqualTo(3));
            Assert.That(loaded.GetItem("resource_herb"), Is.EqualTo(1));
            Assert.That(loaded.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(loaded.IsHeroBusy("ren"), Is.False);
            Assert.That(loaded.GetCraftExecutions(), Is.Empty);
            Assert.That(HasStartReceipt(loaded, "op-late-save-failure", null), Is.False);
        }

        [Test]
        public void ProductionCompositionPublishesCraftStartedExactlyOnceAfterCommit()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 3);
            Seed(state, "resource_herb", 1);
            var events = new List<CraftStartedEvent>();
            Action<CraftStartedEvent> handler = events.Add;
            PlayerRuntimeComposition.CraftStarted += handler;
            try
            {
                var result = PlayerRuntimeComposition.CreateCraftRuntimeService(state)
                    .Start(Request("craft_basic", "op-production-event"));

                Assert.That(result.Success, Is.True);
                Assert.That(events, Has.Count.EqualTo(1));
                Assert.That(events[0].ExecutionId, Is.EqualTo(result.ExecutionId));
            }
            finally
            {
                PlayerRuntimeComposition.CraftStarted -= handler;
            }
        }

        [Test]
        public void ProductionCompositionPublishesCraftResultPendingExactlyOnceAfterCommit()
        {
            var state = StartBasic(out _, out var executionId);
            var events = new List<CraftResultPendingEvent>();
            Action<CraftResultPendingEvent> handler = events.Add;
            PlayerRuntimeComposition.CraftResultPending += handler;
            try
            {
                var result = PlayerRuntimeComposition.CreateCraftRuntimeService(state)
                    .Advance(executionId, 10d, "advance-production-result", 1);

                Assert.That(result.Success, Is.True);
                Assert.That(events, Has.Count.EqualTo(1));
                Assert.That(events[0].ExecutionId, Is.EqualTo(executionId));
                Assert.That(events[0].PendingResultId, Is.EqualTo(result.PendingResultId));
                Assert.That(state.PendingResults.Get(result.PendingResultId), Is.Not.Null);
            }
            finally
            {
                PlayerRuntimeComposition.CraftResultPending -= handler;
            }
        }

        [Test]
        public void ProductionCompositionDoesNotPublishCraftStartedWhenSaveFails()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 3);
            Seed(state, "resource_herb", 1);
            var events = new List<CraftStartedEvent>();
            Action<CraftStartedEvent> handler = events.Add;
            PlayerRuntimeComposition.CraftStarted += handler;
            _storage.ThrowOnSet = true;
            LogAssert.Expect(LogType.Error, "[SaveService] Failed to save player state. simulated save failure");
            try
            {
                var result = PlayerRuntimeComposition.CreateCraftRuntimeService(state)
                    .Start(Request("craft_basic", "op-production-save-failure"));

                Assert.That(result.Success, Is.False);
                Assert.That(result.Code, Is.EqualTo(CraftStartCode.SaveFailure));
                Assert.That(events, Is.Empty);
            }
            finally
            {
                PlayerRuntimeComposition.CraftStarted -= handler;
            }
        }

        [Test]
        public void ProductionCompositionHandlerFailureDoesNotRollbackCommittedStart()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 3);
            Seed(state, "resource_herb", 1);
            Action<CraftStartedEvent> handler = _ => throw new InvalidOperationException("simulated craft event failure");
            PlayerRuntimeComposition.CraftStarted += handler;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: simulated craft event failure"));
            try
            {
                var result = PlayerRuntimeComposition.CreateCraftRuntimeService(state)
                    .Start(Request("craft_basic", "op-production-handler-failure"));

                Assert.That(result.Success, Is.True);
                Assert.That(state.GetCraftExecution(result.ExecutionId), Is.Not.Null);
                Assert.That(state.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo(result.ExecutionId));
                Assert.That(HasStartReceipt(state, "op-production-handler-failure", result.ExecutionId), Is.True);

                var loaded = SaveService.Load(_factory, _storage);
                Assert.That(loaded.GetCraftExecution(result.ExecutionId), Is.Not.Null);
                Assert.That(loaded.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo(result.ExecutionId));
            }
            finally
            {
                PlayerRuntimeComposition.CraftStarted -= handler;
            }
        }

        [Test]
        public void SameOperationKeyAndPayloadReplaysWithoutSecondExecutionOrCosts()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 5);
            Seed(state, "resource_herb", 2);
            var runtime = Runtime(state);
            var request = Request("craft_basic", "op-replay");
            var first = runtime.Start(request);
            var meat = state.GetItem("resource_rabbit_meat");
            var herb = state.GetItem("resource_herb");
            var fatigue = state.GetHeroFatigue("ren");
            var saves = _storage.SaveCalls;

            var replay = runtime.Start(request);

            Assert.That(first.Success, Is.True);
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.Code, Is.EqualTo(CraftStartCode.Replayed));
            Assert.That(replay.ExecutionId, Is.EqualTo(first.ExecutionId));
            Assert.That(state.GetCraftExecutions(), Has.Length.EqualTo(1));
            Assert.That(state.GetItem("resource_rabbit_meat"), Is.EqualTo(meat));
            Assert.That(state.GetItem("resource_herb"), Is.EqualTo(herb));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(_storage.SaveCalls, Is.EqualTo(saves));
        }

        [Test]
        public void ReceiptWithoutLiveExecutionCannotReplaySuccessfully()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 5);
            Seed(state, "resource_herb", 2);
            var request = Request("craft_basic", "op-orphan-receipt");
            var started = Runtime(state).Start(request);
            Assert.That(started.Success, Is.True);

            var orphanedSave = state.ToSaveData();
            orphanedSave.craftRuntime.executions = Array.Empty<CraftExecutionSaveData>();
            var receiptOnlyState = _factory.Create(orphanedSave);
            Assert.That(SaveService.Save(receiptOnlyState, _storage), Is.True);
            var loaded = SaveService.Load(_factory, _storage);
            Assert.That(HasStartReceipt(loaded, request.OperationKey, started.ExecutionId), Is.True);
            var meat = loaded.GetItem("resource_rabbit_meat");
            var herb = loaded.GetItem("resource_herb");
            var fatigue = loaded.GetHeroFatigue("ren");

            var replay = Runtime(loaded).Start(request);

            Assert.That(replay.Success, Is.False);
            Assert.That(replay.Replayed, Is.False);
            Assert.That(replay.Code, Is.EqualTo(CraftStartCode.TransactionFailure));
            Assert.That(loaded.GetCraftExecutions(), Is.Empty);
            Assert.That(loaded.IsHeroBusy("ren"), Is.False);
            Assert.That(loaded.GetItem("resource_rabbit_meat"), Is.EqualTo(meat));
            Assert.That(loaded.GetItem("resource_herb"), Is.EqualTo(herb));
            Assert.That(loaded.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
        }

        [Test]
        public void ActiveExecutionReplaysAfterCraftReceiptIsEvictedByGlobalCap()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 5);
            Seed(state, "resource_herb", 2);
            var request = Request("craft_basic", "op-evicted-receipt");
            var started = Runtime(state).Start(request);
            Assert.That(started.Success, Is.True);

            var adapter = new PlayerStateCraftAdapter(state);
            for (var index = 0; index < 65; index++)
            {
                adapter.RecordOperationReceipt(new OperationReceiptSaveData
                {
                    aggregateId = "unrelated-test",
                    operationId = $"unrelated-{index}",
                    fingerprint = $"fingerprint-{index}",
                    success = true,
                    code = "Applied"
                });
            }
            Assert.That(HasStartReceipt(state, request.OperationKey, started.ExecutionId), Is.False);
            Assert.That(state.Save(), Is.True);

            var loaded = SaveService.Load(_factory, _storage);
            Assert.That(HasStartReceipt(loaded, request.OperationKey, started.ExecutionId), Is.False);
            var meat = loaded.GetItem("resource_rabbit_meat");
            var herb = loaded.GetItem("resource_herb");
            var fatigue = loaded.GetHeroFatigue("ren");

            var replay = Runtime(loaded).Start(request);

            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.ExecutionId, Is.EqualTo(started.ExecutionId));
            Assert.That(loaded.GetCraftExecutions(), Has.Length.EqualTo(1));
            Assert.That(loaded.GetItem("resource_rabbit_meat"), Is.EqualTo(meat));
            Assert.That(loaded.GetItem("resource_herb"), Is.EqualTo(herb));
            Assert.That(loaded.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
        }

        [Test]
        public void ReusedOperationKeyWithDifferentPayloadIsConflictBeforeOtherChecks()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 5);
            Seed(state, "resource_herb", 5);
            var runtime = Runtime(state);
            var first = runtime.Start(Request("craft_basic", "op-conflict"));
            var items = state.GetItem("resource_herb");
            var fatigue = state.GetHeroFatigue("ren");

            var conflict = runtime.Start(Request("craft_other", "op-conflict"));

            Assert.That(first.Success, Is.True);
            Assert.That(conflict.Success, Is.False);
            Assert.That(conflict.Code, Is.EqualTo(CraftStartCode.OperationReplayConflict));
            Assert.That(state.GetCraftExecutions(), Has.Length.EqualTo(1));
            Assert.That(state.GetItem("resource_herb"), Is.EqualTo(items));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
        }

        [Test]
        public void RunningExecutionSurvivesSaveLoadWithoutRepaymentAndKeepsImmutableSnapshot()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 5);
            Seed(state, "resource_herb", 2);
            var started = Runtime(state).Start(Request("craft_basic", "op-roundtrip"));
            var meatAfterStart = state.GetItem("resource_rabbit_meat");
            var fatigueAfterStart = state.GetHeroFatigue("ren");

            var source = FindDefinition("craft_basic");
            source.craftDurationSec = 999;
            source.targetItemId = "consumable_other";
            source.outputCount = 7;
            source.skillExp = 99;

            var loaded = SaveService.Load(_factory, _storage);
            var execution = loaded.GetCraftExecution(started.ExecutionId);

            Assert.That(execution, Is.Not.Null);
            Assert.That(execution.status, Is.EqualTo(CraftExecutionStatus.Running));
            Assert.That(execution.durationSeconds, Is.EqualTo(10));
            Assert.That(execution.outputItemId, Is.EqualTo("consumable_roasted_rabbit_meat"));
            Assert.That(execution.outputCount, Is.EqualTo(1));
            Assert.That(execution.skillExp, Is.EqualTo(2));
            Assert.That(loaded.GetItem("resource_rabbit_meat"), Is.EqualTo(meatAfterStart));
            Assert.That(loaded.GetHeroFatigue("ren"), Is.EqualTo(fatigueAfterStart));
            Assert.That(loaded.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo(started.ExecutionId));

            var replay = Runtime(loaded).Start(Request("craft_basic", "op-roundtrip"));
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.ExecutionId, Is.EqualTo(started.ExecutionId));
            Assert.That(loaded.GetItem("resource_rabbit_meat"), Is.EqualTo(meatAfterStart));
            Assert.That(loaded.GetHeroFatigue("ren"), Is.EqualTo(fatigueAfterStart));
        }

        [Test]
        public void BusyLimitFatigueAndUnknownCraftChecksDoNotMutateState()
        {
            var unknownState = NewState();
            AssertBlockedWithoutMutation(unknownState, Request("missing", "op-unknown"), CraftStartCode.UnknownOrDisabledCraft);

            var busyState = NewState();
            Assert.That(busyState.SetHeroBusy("ren", "foreign-execution"), Is.True);
            AssertBlockedWithoutMutation(busyState, Request("craft_basic", "op-busy"), CraftStartCode.HeroBusy);

            var fatigueState = NewState();
            Seed(fatigueState, "resource_rabbit_meat", 3);
            Seed(fatigueState, "resource_herb", 1);
            var currentFatigue = fatigueState.GetHeroFatigue("ren");
            Assert.That(fatigueState.SpendHeroFatigue("ren", currentFatigue - 1), Is.True);
            AssertBlockedWithoutMutation(fatigueState, Request("craft_basic", "op-fatigue"), CraftStartCode.InsufficientFatigue);

            var limitState = NewState();
            Assert.That(limitState.AddHero("aska"), Is.True);
            Assert.That(limitState.SetHeroBusy("ren", "foreign-execution"), Is.True);
            Seed(limitState, "resource_rabbit_meat", 3);
            Seed(limitState, "resource_herb", 1);
            AssertBlockedWithoutMutation(limitState, Request("craft_basic", "op-limit", "aska"), CraftStartCode.ActiveHeroLimitReached);
        }

        [Test]
        public void StationAdditionalRequirementAndRecipeFailuresAreTypedAndReadOnly()
        {
            var station = NewState();
            AssertBlockedWithoutMutation(station, Request("craft_basic", "op-station", stationLevel: 0), CraftStartCode.StationUnavailable);

            var requirement = NewState();
            AssertBlockedWithoutMutation(requirement, Request("craft_requires_locked_building", "op-requirement"), CraftStartCode.AdditionalBuildingUnavailable);

            var recipe = NewState();
            Seed(recipe, "resource_rabbit_meat", 1);
            AssertBlockedWithoutMutation(recipe, Request("craft_recipe_kept", "op-missing-recipe"), CraftStartCode.MissingOrInvalidRecipe);

            var materials = NewState();
            Seed(materials, "resource_rabbit_meat", 2);
            Seed(materials, "resource_herb", 1);
            AssertBlockedWithoutMutation(materials, Request("craft_basic", "op-missing-material"), CraftStartCode.MissingMaterials);
        }

        [Test]
        public void NewOperationForMatchingActiveExecutionIsRejectedWithoutSecondMutation()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 6);
            Seed(state, "resource_herb", 2);
            var runtime = Runtime(state);
            Assert.That(runtime.Start(Request("craft_basic", "op-first-active")).Success, Is.True);
            var meat = state.GetItem("resource_rabbit_meat");
            var fatigue = state.GetHeroFatigue("ren");

            var duplicate = runtime.Start(Request("craft_basic", "op-second-active"));

            Assert.That(duplicate.Success, Is.False);
            Assert.That(duplicate.Code, Is.EqualTo(CraftStartCode.ExecutionAlreadyActive));
            Assert.That(state.GetCraftExecutions(), Has.Length.EqualTo(1));
            Assert.That(state.GetItem("resource_rabbit_meat"), Is.EqualTo(meat));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
        }

        [Test]
        public void AdditionalCraftStartsThroughSameGenericPath()
        {
            var state = NewState();
            Seed(state, "resource_herb", 2);

            var result = Runtime(state).Start(Request("craft_other", "op-other"));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Execution.craftId, Is.EqualTo("craft_other"));
            Assert.That(result.Execution.durationSeconds, Is.EqualTo(20));
            Assert.That(result.Execution.outputItemId, Is.EqualTo("consumable_other"));
            Assert.That(state.GetItem("resource_herb"), Is.Zero);
        }

        private PlayerState NewState()
        {
            return SaveService.Load(_factory, _storage);
        }

        private PlayerState StartBasic(out CraftRuntimeService runtime, out string executionId)
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 3);
            Seed(state, "resource_herb", 1);
            runtime = Runtime(state);
            var started = runtime.Start(Request("craft_basic", $"start-basic:{Guid.NewGuid():N}"));
            Assert.That(started.Success, Is.True);
            executionId = started.ExecutionId;
            return state;
        }

        private PlayerState CompleteBasic(out string executionId, out PendingResultSaveData result)
        {
            var state = StartBasic(out var runtime, out executionId);
            var completed = runtime.Advance(executionId, 10d, $"complete-basic:{Guid.NewGuid():N}", 1);
            Assert.That(completed.Success, Is.True);
            result = state.PendingResults.Get(completed.PendingResultId);
            Assert.That(result, Is.Not.Null);
            return state;
        }

        private CraftRuntimeService Runtime(
            PlayerState state,
            Action<CraftStartedEvent> eventSink = null,
            Action<CraftResultPendingEvent> resultPendingEventSink = null)
        {
            return new CraftRuntimeService(_database.Crafts, new PlayerStateCraftAdapter(state), eventSink, resultPendingEventSink);
        }

        private static CraftStartRequest Request(
            string craftId,
            string operationKey,
            string heroId = "ren",
            string stationId = "building_campfire",
            int stationLevel = 1)
        {
            return new CraftStartRequest
            {
                CraftId = craftId,
                HeroId = heroId,
                StationBuildingId = stationId,
                StationBuildingLevel = stationLevel,
                OperationKey = operationKey
            };
        }

        private static void Seed(PlayerState state, string itemId, int quantity)
        {
            var result = state.Storage.Add($"seed:{itemId}:{Guid.NewGuid():N}", state.Storage.GetSnapshot().Revision, itemId, quantity);
            Assert.That(result.Success, Is.True, result.Message);
        }

        private static void FillStorage(PlayerState state)
        {
            var index = 0;
            while (state.Storage.GetSnapshot().FreeSlots > 0)
            {
                var result = state.Storage.Add($"fill:{index++}", state.Storage.GetSnapshot().Revision, "resource_herb", 2);
                Assert.That(result.Success, Is.True, result.Message);
            }
        }

        private static void AssertRunningWithoutResult(PlayerState state, string executionId, float progress)
        {
            var execution = state.GetCraftExecution(executionId);
            Assert.That(execution, Is.Not.Null);
            Assert.That(execution.status, Is.EqualTo(CraftExecutionStatus.Running));
            Assert.That(execution.progressSeconds, Is.EqualTo(progress));
            Assert.That(execution.completionRecorded, Is.False);
            Assert.That(execution.pendingResultId, Is.Null.Or.Empty);
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
            Assert.That(state.GetHeroCurrentActivityExecutionId(execution.heroId), Is.EqualTo(executionId));
        }

        private static void AssertCraftResultStillPending(PlayerState state, string executionId, string resultId)
        {
            AssertCraftExecutionStillOccupied(state, executionId, resultId);
            Assert.That(state.PendingResults.Get(resultId), Is.Not.Null);
        }

        private static void AssertCraftExecutionStillOccupied(PlayerState state, string executionId, string resultId)
        {
            var execution = state.GetCraftExecution(executionId);
            Assert.That(execution, Is.Not.Null);
            Assert.That(execution.status, Is.EqualTo(CraftExecutionStatus.ResultPending));
            Assert.That(execution.completionRecorded, Is.True);
            Assert.That(execution.pendingResultId, Is.EqualTo(resultId));
            Assert.That(state.GetHeroCurrentActivityExecutionId(execution.heroId), Is.EqualTo(executionId));
        }

        private static void AssertCraftFinalized(PlayerState state, string executionId, string resultId)
        {
            Assert.That(state.GetCraftExecution(executionId), Is.Null);
            Assert.That(state.PendingResults.Get(resultId), Is.Null);
            Assert.That(state.GetHeroCurrentActivityExecutionId("ren"), Is.Null.Or.Empty);
            Assert.That(state.GetActiveHeroCount(), Is.Zero);
            Assert.That(FindCraftSourceReference(state, executionId).state, Is.EqualTo(PendingResultSourceState.Resolved));
        }

        private static PendingResultSourceReferenceSaveData FindCraftSourceReference(PlayerState state, string executionId) =>
            FindCraftSourceReference(state.ToSaveData(), executionId);

        private static PendingResultSourceReferenceSaveData FindCraftSourceReference(SaveData saveData, string executionId)
        {
            foreach (var source in saveData?.resultSources ?? Array.Empty<PendingResultSourceReferenceSaveData>())
                if (source != null && string.Equals(source.sourceType, PendingResultSourceType.Craft, StringComparison.Ordinal) &&
                    string.Equals(source.sourceExecutionId, executionId, StringComparison.Ordinal)) return source;
            Assert.Fail($"Missing Craft source reference for execution '{executionId}'.");
            return null;
        }

        private static PendingResultEntrySaveData FindEntry(PendingResultSaveData result, string rewardType)
        {
            foreach (var entry in result?.entries ?? Array.Empty<PendingResultEntrySaveData>())
                if (entry != null && string.Equals(entry.rewardType, rewardType, StringComparison.Ordinal)) return entry;
            Assert.Fail($"Missing result entry of type '{rewardType}'.");
            return null;
        }

        private static string CorruptCraftResult(SaveData saveData, string corruption)
        {
            var result = saveData.pendingResults[0];
            var execution = saveData.craftRuntime.executions[0];
            var output = FindEntry(result, RewardType.Item);
            var skillExp = FindEntry(result, RewardType.SkillExp);
            switch (corruption)
            {
                case "other-output-item":
                    output.targetId = "consumable_other";
                    return output.entryId;
                case "excess-output-quantity":
                    output.quantity = execution.outputCount + 1L;
                    return output.entryId;
                case "other-skill":
                    skillExp.targetId = "skill_other";
                    return skillExp.entryId;
                case "excess-skill-exp":
                    skillExp.quantity = execution.skillExp + 1L;
                    return skillExp.entryId;
                case "additional-reward":
                    var additional = new PendingResultEntrySaveData
                    {
                        entryId = "corrupt-additional-reward",
                        sortOrder = 100,
                        rewardType = RewardType.Resource,
                        targetId = "resource_herb",
                        quantity = 1,
                        origin = PendingResultOrigin.CraftOutput
                    };
                    AppendEntry(result, additional);
                    return additional.entryId;
                case "wrong-origin":
                    output.origin = PendingResultOrigin.ActivityReward;
                    return output.entryId;
                case "duplicate-item":
                    var duplicateItem = CloneEntry(output, "corrupt-duplicate-item");
                    AppendEntry(result, duplicateItem);
                    return duplicateItem.entryId;
                case "duplicate-skill-exp":
                    var duplicateSkillExp = CloneEntry(skillExp, "corrupt-duplicate-skill-exp");
                    AppendEntry(result, duplicateSkillExp);
                    return duplicateSkillExp.entryId;
                default:
                    Assert.Fail($"Unknown corruption case '{corruption}'.");
                    return null;
            }
        }

        private static void ExpectCorruptCraftResultLoadLog(string corruption)
        {
            var pattern = string.Equals(corruption, "wrong-origin", StringComparison.Ordinal)
                ? @"\[PendingResult\] Corrupt result '.+' was quarantined; its source remains blocked to prevent reward reroll\."
                : @"\[PendingResult\] Result '.+' could not bind to source and remains blocked for manual recovery\.";
            LogAssert.Expect(LogType.Error, new Regex(pattern));
        }

        private static void AppendEntry(PendingResultSaveData result, PendingResultEntrySaveData entry)
        {
            var entries = new List<PendingResultEntrySaveData>(result.entries) { entry };
            result.entries = entries.ToArray();
        }

        private static PendingResultEntrySaveData CloneEntry(PendingResultEntrySaveData source, string entryId)
        {
            return new PendingResultEntrySaveData
            {
                entryId = entryId,
                sortOrder = source.sortOrder + 100,
                rewardType = source.rewardType,
                targetId = source.targetId,
                quantity = source.quantity,
                origin = source.origin,
                quality = source.quality,
                instanceId = source.instanceId
            };
        }

        private static void AssertEntry(PendingResultSaveData result, string rewardType, string targetId, long quantity, string origin)
        {
            Assert.That(result, Is.Not.Null);
            foreach (var entry in result.entries ?? Array.Empty<PendingResultEntrySaveData>())
            {
                if (entry != null && string.Equals(entry.rewardType, rewardType, StringComparison.Ordinal) &&
                    string.Equals(entry.targetId, targetId, StringComparison.Ordinal))
                {
                    Assert.That(entry.quantity, Is.EqualTo(quantity));
                    Assert.That(entry.origin, Is.EqualTo(origin));
                    return;
                }
            }
            Assert.Fail($"Missing result entry {rewardType}:{targetId}.");
        }

        private void AssertBlockedWithoutMutation(PlayerState state, CraftStartRequest request, string code)
        {
            var before = JsonUtility.ToJson(state.ToSaveData());
            var result = Runtime(state).Start(request);
            var after = JsonUtility.ToJson(state.ToSaveData());
            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(code));
            Assert.That(after, Is.EqualTo(before));
        }

        private CraftDefinitionConfigDto FindDefinition(string craftId)
        {
            foreach (var definition in _database.Items.CraftDefinitions)
                if (string.Equals(definition.craftId, craftId, StringComparison.Ordinal)) return definition;
            return null;
        }

        private static CraftPaidCostSaveData FindCost(CraftExecutionSaveData execution, string itemId)
        {
            foreach (var cost in execution.paidCosts ?? Array.Empty<CraftPaidCostSaveData>())
                if (cost != null && string.Equals(cost.itemId, itemId, StringComparison.Ordinal)) return cost;
            return null;
        }

        private static bool HasStartReceipt(PlayerState state, string operationKey, string executionId)
        {
            foreach (var receipt in state.ToSaveData().operationReceipts)
            {
                if (receipt == null || !string.Equals(receipt.aggregateId, "craft-start", StringComparison.Ordinal) ||
                    !string.Equals(receipt.operationId, operationKey, StringComparison.Ordinal))
                    continue;
                return executionId == null || string.Equals(receipt.executionId, executionId, StringComparison.Ordinal);
            }
            return false;
        }

        private static bool HasOperationReceipt(PlayerState state, string aggregateId, string operationId)
        {
            foreach (var receipt in state.ToSaveData().operationReceipts)
            {
                if (receipt != null && string.Equals(receipt.aggregateId, aggregateId, StringComparison.Ordinal) &&
                    string.Equals(receipt.operationId, operationId, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static ConfigDatabase CreateDatabase()
        {
            return new ConfigDatabase(
                new ItemsRuntimeConfigDto
                {
                    resources = new[]
                    {
                        new ResourceConfigDto { id = "resource_rabbit_meat", kind = "resource" },
                        new ResourceConfigDto { id = "resource_herb", kind = "resource" }
                    },
                    recipes = new[]
                    {
                        new RecipeConfigDto { id = "recipe_roasting", kind = "recipe", enabled = true }
                    },
                    consumables = new[]
                    {
                        new ConsumableConfigDto { id = "consumable_roasted_rabbit_meat", kind = "consumable" },
                        new ConsumableConfigDto { id = "consumable_other", kind = "consumable" }
                    },
                    craftDefinitions = new[]
                    {
                        Definition("craft_basic", "consumable_roasted_rabbit_meat", 10, 2, 2,
                            new[]
                            {
                                new MaterialCostDto { id = "resource_rabbit_meat", count = 1 },
                                new MaterialCostDto { id = "resource_rabbit_meat", count = 2 },
                                new MaterialCostDto { id = "resource_herb", count = 1 }
                            }),
                        Definition("cook_roasted_rabbit_meat", "consumable_roasted_rabbit_meat", 10, 2, 2,
                            new[]
                            {
                                new MaterialCostDto { id = "resource_rabbit_meat", count = 3 },
                                new MaterialCostDto { id = "resource_herb", count = 1 }
                            }),
                        Definition("craft_invalid_reward", "missing_output_item", 10, 2, 2,
                            new[]
                            {
                                new MaterialCostDto { id = "resource_rabbit_meat", count = 3 },
                                new MaterialCostDto { id = "resource_herb", count = 1 }
                            }),
                        Definition("craft_recipe_kept", "consumable_roasted_rabbit_meat", 10, 1, 2,
                            new[] { new MaterialCostDto { id = "resource_rabbit_meat", count = 1 } },
                            "recipe_roasting", 1, false),
                        Definition("craft_recipe_consumed", "consumable_roasted_rabbit_meat", 10, 1, 2,
                            new[] { new MaterialCostDto { id = "resource_rabbit_meat", count = 1 } },
                            "recipe_roasting", 2, true),
                        Definition("craft_other", "consumable_other", 20, 1, 5,
                            new[] { new MaterialCostDto { id = "resource_herb", count = 2 } }),
                        Definition("craft_requires_locked_building", "consumable_other", 5, 0, 0,
                            Array.Empty<MaterialCostDto>(), requiredBuildings: new[] { new RequiredBuildingDto { buildingId = "building_locked", level = 1 } })
                    }
                },
                new HeroesRuntimeConfigDto
                {
                    heroes = new[]
                    {
                        new HeroConfigDto { heroId = "ren", enabled = true, baseStats = new HeroBaseStatsDto { endurance = 5 } },
                        new HeroConfigDto { heroId = "aska", enabled = true, baseStats = new HeroBaseStatsDto { endurance = 5 } }
                    }
                },
                new ActivitiesRuntimeConfigDto
                {
                    skills = new[]
                    {
                        new SkillConfigDto { skillId = "skill_crafting" },
                        new SkillConfigDto { skillId = "skill_other" }
                    },
                    skillsProgression = new[] { new SkillProgressionConfigDto { level = 1, totalExpRequired = 0 } }
                },
                new BuildingsRuntimeConfigDto
                {
                    buildings = new[]
                    {
                        Building("building_hall", true),
                        Building("building_campfire", true),
                        Building("building_warehouse", true),
                        Building("building_locked", false)
                    },
                    buildingLevels = new[]
                    {
                        new BuildingLevelConfigDto { buildingId = "building_hall", level = 1, activeHeroLimit = 1 },
                        new BuildingLevelConfigDto { buildingId = "building_campfire", level = 1 },
                        new BuildingLevelConfigDto { buildingId = "building_warehouse", level = 1 },
                        new BuildingLevelConfigDto { buildingId = "building_locked", level = 1 }
                    },
                    buildingCraftables = new[]
                    {
                        Craftable("craft_basic"),
                        Craftable("cook_roasted_rabbit_meat"),
                        Craftable("craft_invalid_reward"),
                        Craftable("craft_recipe_kept"),
                        Craftable("craft_recipe_consumed"),
                        Craftable("craft_other"),
                        Craftable("craft_requires_locked_building")
                    },
                    settlementStageStarterHeroes = new[]
                    {
                        new SettlementStageStarterHeroConfigDto { stageId = "stage_arrival", heroId = "ren" }
                    }
                },
                new QuestRuntimeConfigDto
                {
                    stages = new[] { new StageConfigDto { stageId = "stage_arrival", enabled = true } }
                },
                null,
                new FormulaRuntimeConfigDto
                {
                    formulas = new[]
                    {
                        new FormulaConfigDto
                        {
                            formulaId = "hero_max_fatigue",
                            derivedStatId = "max_fatigue",
                            formulaType = "linear_stats_with_skill_level",
                            baseValue = 100,
                            primaryStat = "Endurance",
                            primaryStatMultiplier = 1,
                            secondaryStat = "Endurance",
                            rounding = "floor",
                            enabled = true
                        }
                    }
                },
                null,
                null,
                new StorageRuntimeConfigDto
                {
                    storageRules = new[]
                    {
                        new StorageRuleConfigDto { storageRuleId = "resource", itemKind = "resource", mode = "stack", maxStack = 2, occupiesSlot = true },
                        new StorageRuleConfigDto { storageRuleId = "recipe", itemKind = "recipe", mode = "stack", maxStack = 2, occupiesSlot = true },
                        new StorageRuleConfigDto { storageRuleId = "consumable", itemKind = "consumable", mode = "stack", maxStack = 20, occupiesSlot = true }
                    },
                    storageBuildings = new[] { new StorageBuildingConfigDto { buildingId = "building_warehouse", level = 1, slotCount = 30 } },
                    itemStates = new[]
                    {
                        new ItemStateConfigDto { stateId = "on_storage", isInStorage = true, occupiesCapacity = true, availabilityMode = "available", availableForCraft = true },
                        new ItemStateConfigDto { stateId = "equipped", requiresOwner = true, availabilityMode = "equipped" }
                    }
                },
                null);
        }

        private static CraftDefinitionConfigDto Definition(
            string craftId,
            string outputItemId,
            int duration,
            int fatigue,
            int skillExp,
            MaterialCostDto[] materials,
            string recipeItemId = null,
            int recipeCount = 0,
            bool consumeRecipe = false,
            RequiredBuildingDto[] requiredBuildings = null)
        {
            return new CraftDefinitionConfigDto
            {
                craftId = craftId,
                targetItemId = outputItemId,
                craftStationId = "building_campfire",
                craftDurationSec = duration,
                craftSkillId = "skill_crafting",
                requiredBuildings = requiredBuildings ?? Array.Empty<RequiredBuildingDto>(),
                materials = materials,
                requiredRecipeItemId = recipeItemId,
                requiredRecipeItemCount = recipeCount,
                consumeRecipeItem = consumeRecipe,
                outputCount = 1,
                fatigueCost = fatigue,
                skillExp = skillExp
            };
        }

        private static BuildingConfigDto Building(string id, bool visibleAtStart)
        {
            return new BuildingConfigDto { buildingId = id, levels = 1, startLevel = 1, visibleAtStart = visibleAtStart };
        }

        private static BuildingCraftableConfigDto Craftable(string craftId)
        {
            return new BuildingCraftableConfigDto
            {
                buildingId = "building_campfire",
                buildingLevel = 1,
                craftId = craftId,
                enabled = true
            };
        }

        private sealed class FailingCraftFinalizationHandler : IPendingResultSourceHandler
        {
            public string SourceType => PendingResultSourceType.Craft;
            public bool AcceptsOrigin(string origin) => string.Equals(origin, PendingResultOrigin.CraftOutput, StringComparison.Ordinal);
            public bool TryBind(PendingResultSaveData result, bool makeClaimable, PendingResultBindMode mode) => true;
            public bool CanClaim(PendingResultSaveData result) => true;
            public bool Resolve(PendingResultSaveData result) => false;
        }

        private sealed class FaultInjectingCraftState : ICraftPlayerState
        {
            private readonly ICraftPlayerState _inner;
            private int _consumeCalls;

            public FaultInjectingCraftState(ICraftPlayerState inner) => _inner = inner;
            public int FailConsumeCall { get; set; } = -1;
            public bool FailOccupation { get; set; }
            public bool FailAddExecution { get; set; }
            public bool FailUpdateExecution { get; set; }
            public bool FailPendingResultCreation { get; set; }
            public SaveData CaptureCheckpoint() => _inner.CaptureCheckpoint();
            public void RestoreCheckpoint(SaveData checkpoint) => _inner.RestoreCheckpoint(checkpoint);
            public bool TryGetOperationReceipt(string aggregateId, string operationId, out OperationReceiptSaveData receipt) => _inner.TryGetOperationReceipt(aggregateId, operationId, out receipt);
            public void RecordOperationReceipt(OperationReceiptSaveData receipt) => _inner.RecordOperationReceipt(receipt);
            public bool HasHero(string heroId) => _inner.HasHero(heroId);
            public bool HasHeroState(string heroId) => _inner.HasHeroState(heroId);
            public int GetHeroFatigue(string heroId) => _inner.GetHeroFatigue(heroId);
            public bool SpendHeroFatigue(string heroId, int amount) => _inner.SpendHeroFatigue(heroId, amount);
            public bool IsHeroBusy(string heroId) => _inner.IsHeroBusy(heroId);
            public string GetHeroOccupationOwnerId(string heroId) => _inner.GetHeroOccupationOwnerId(heroId);
            public int GetActiveHeroCount() => _inner.GetActiveHeroCount();
            public int GetActiveHeroLimit() => _inner.GetActiveHeroLimit();
            public bool TryOccupyHero(string heroId, string executionId) => !FailOccupation && _inner.TryOccupyHero(heroId, executionId);
            public bool IsBuildingUnlocked(string buildingId) => _inner.IsBuildingUnlocked(buildingId);
            public int GetBuildingLevel(string buildingId) => _inner.GetBuildingLevel(buildingId);
            public int GetAvailableForCraftCount(string itemId) => _inner.GetAvailableForCraftCount(itemId);

            public bool TryConsumeCraftCost(string itemId, int quantity, out string error)
            {
                _consumeCalls++;
                if (_consumeCalls == FailConsumeCall)
                {
                    error = "simulated removal failure";
                    return false;
                }
                return _inner.TryConsumeCraftCost(itemId, quantity, out error);
            }

            public void PublishCraftStartCommit() => _inner.PublishCraftStartCommit();

            public CraftExecutionSaveData[] GetCraftExecutions() => _inner.GetCraftExecutions();
            public CraftExecutionSaveData GetCraftExecution(string executionId) => _inner.GetCraftExecution(executionId);
            public bool AddCraftExecution(CraftExecutionSaveData execution) => !FailAddExecution && _inner.AddCraftExecution(execution);
            public bool UpdateCraftExecution(CraftExecutionSaveData execution) => !FailUpdateExecution && _inner.UpdateCraftExecution(execution);
            public PendingResultSaveData GetPendingResult(string resultId) => _inner.GetPendingResult(resultId);
            public PendingResultFormationResult CreatePendingResult(string operationId, PendingResultDraft draft) =>
                FailPendingResultCreation
                    ? new PendingResultFormationResult { Success = false, Code = "SimulatedFailure", Message = "simulated PendingResult failure" }
                    : _inner.CreatePendingResult(operationId, draft);
            public bool Save() => _inner.Save();
        }

        private sealed class MemorySaveStorage : ISaveStorage
        {
            private string _json;
            public int SaveCalls { get; private set; }
            public bool ThrowOnSet { get; set; }
            public int ThrowOnSaveCall { get; set; } = -1;
            public bool HasKey(string key) => _json != null;
            public string GetString(string key, string defaultValue) => _json ?? defaultValue;
            public void SetString(string key, string value)
            {
                if (ThrowOnSet)
                    throw new InvalidOperationException("simulated save failure");
                _json = value;
            }
            public void DeleteKey(string key) => _json = null;
            public void Save()
            {
                SaveCalls++;
                if (SaveCalls != ThrowOnSaveCall)
                    return;
                ThrowOnSaveCall = -1;
                throw new InvalidOperationException("simulated save flush failure");
            }
        }
    }
}
