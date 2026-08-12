using System;
using System.Collections.Generic;
using GuildIdle.Activities;
using GuildIdle.Combat;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Editor.Configs;
using GuildIdle.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Editor.Activities
{
    public sealed class ActivityRuntimeServiceTests
    {
        private PlayerStateFactory _factory;

        [SetUp]
        public void SetUp()
        {
            var database = CreateDatabase();
            RuntimeConfigs.SetDatabaseForTests(database);
            _factory = TestPlayerComposition.CreatePlayerStateFactory(database);
        }

        [Test]
        public void Start_CreatesExecutionAndSpendsHeroFatigue()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var fatigue = state.GetHeroFatigue("ren");

            var result = runtime.Start(WorkStart("work_pine_wood", "ren", 2));

            Assert.That(result.success, Is.True);
            Assert.That(state.GetActivityExecutions(), Has.Length.EqualTo(1));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue - 2));
            Assert.That(state.IsHeroBusy("ren"), Is.True);
        }

        [Test]
        public void Start_RejectsCompletedNonRepeatableBeforeCost()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var fatigue = state.GetHeroFatigue("ren");
            Assert.That(state.CompleteActivity("one_shot"), Is.True);

            var result = runtime.Start("one_shot", "ren");

            Assert.That(result.success, Is.False);
            Assert.That(HasIssue(result.issues, "ActivityCompleted"), Is.True);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(state.GetActivityExecutions(), Is.Empty);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
        }

        [Test]
        public void Start_UnknownActivityAndEmptySlotFailWithoutStateChange()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), progressionProcessor: new RecordingProgressionProcessor());
            var fatigue = state.GetHeroFatigue("ren");
            LogAssert.Expect(LogType.Error, "[ActivityResolver] Unknown activity id 'missing_activity'.");

            var missing = runtime.Start("missing_activity", "ren");
            var emptyHero = runtime.Start(WorkStart("work_pine_wood", string.Empty, 1));

            Assert.That(missing.success, Is.False);
            Assert.That(emptyHero.success, Is.False);
            Assert.That(HasIssue(emptyHero.issues, "HeroExecutor"), Is.True);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(state.GetActivityExecutions(), Is.Empty);
        }

        [Test]
        public void Tick_WorkStopsAfterExactlyPlannedCycles()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            Assert.That(runtime.Start(WorkStart("work_pine_wood", "ren", 3)).success, Is.True);

            var firstTick = runtime.Tick(25f);
            var execution = state.GetActivityExecutions()[0];

            Assert.That(firstTick.success, Is.True);
            Assert.That(firstTick.processedCycles, Is.EqualTo(2));
            Assert.That(execution.completedCycles, Is.EqualTo(2));
            Assert.That(execution.elapsedSeconds, Is.EqualTo(5f));
            Assert.That(state.GetItem("resource_pine_wood"), Is.Zero);
            Assert.That(state.GetHeroSkillExp("ren", "skill_gathering"), Is.Zero);
            Assert.That(state.PendingResults.GetAll(), Has.Length.EqualTo(1));
            Assert.That(state.IsHeroBusy("ren"), Is.True);

            var finalTick = runtime.Tick(2000f);
            execution = state.GetActivityExecutions()[0];

            Assert.That(finalTick.success, Is.True);
            Assert.That(finalTick.processedCycles, Is.EqualTo(1));
            Assert.That(execution.completedCycles, Is.EqualTo(3));
            Assert.That(execution.status, Is.EqualTo(GuildIdle.Core.ActivityRuntimeStatus.ResultPending));
        }

        [Test]
        public void CancelRepeatableWithBagStopsInResultPendingAndAllowsClaim()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start(WorkStart("work_pine_wood", "ren", 2));
            Assert.That(runtime.Tick(10f).success, Is.True);
            var bag = state.PendingResults.GetAll()[0];

            var blockedClaim = state.PendingResults.ClaimAll("claim-before-stop", bag.resultId, bag.revision, state.Storage.GetSnapshot().Revision);
            var stopped = runtime.Cancel(started.executionId);
            var execution = state.GetActivityExecution(started.executionId);
            var claimableBag = state.PendingResults.Get(bag.resultId);
            var claimed = state.PendingResults.ClaimAll("claim-after-stop", bag.resultId, claimableBag.revision, state.Storage.GetSnapshot().Revision);

            Assert.That(blockedClaim.Success, Is.False);
            Assert.That(blockedClaim.Code, Is.EqualTo("SourceNotClaimable"));
            Assert.That(stopped.success, Is.True);
            Assert.That(execution.status, Is.EqualTo(GuildIdle.Core.ActivityRuntimeStatus.ResultPending));
            Assert.That(execution.pendingResultId, Is.EqualTo(bag.resultId));
            Assert.That(claimed.Success, Is.True);
            Assert.That(claimed.Resolved, Is.True);
            Assert.That(state.GetActivityExecution(started.executionId), Is.Null);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(1));
            Assert.That(state.GetHeroSkillExp("ren", "skill_gathering"), Is.EqualTo(1));
        }

        [Test]
        public void EmptyRepeatableCycleStopsWithoutBagAndSurvivesSaveLoad()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start(WorkStart("empty_repeat", "ren", 2));

            var ticked = runtime.Tick(10f);
            var afterCycle = state.GetActivityExecution(started.executionId);
            var stopped = runtime.Cancel(started.executionId);
            var storage = new MemorySaveStorage();
            Assert.That(SaveService.Save(state, storage), Is.True);
            var restored = SaveService.Load(_factory, storage);

            Assert.That(ticked.success, Is.True);
            Assert.That(ticked.processedCycles, Is.EqualTo(1));
            Assert.That(afterCycle.completedCycles, Is.EqualTo(1));
            Assert.That(afterCycle.pendingResultId, Is.Null);
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
            Assert.That(stopped.success, Is.True);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(restored.PendingResults.GetAll(), Is.Empty);
            Assert.That(restored.GetActivityExecutions(), Is.Empty);
            Assert.That(restored.IsHeroBusy("ren"), Is.False);
            Assert.That(restored.ToSaveData().resultSources, Is.Empty);
        }

        [Test]
        public void EmptyOneShotPublishesResolvedEventExactlyOnce()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var resolved = new List<PendingResultResolvedEvent>();
            state.PendingResults.Resolved += resolved.Add;
            Assert.That(runtime.Start("one_shot", "ren").success, Is.True);

            var ticked = runtime.Tick(5f);

            Assert.That(ticked.success, Is.True);
            Assert.That(resolved, Has.Count.EqualTo(1));
            Assert.That(resolved[0].SourceType, Is.EqualTo(PendingResultSourceType.Activity));
            Assert.That(resolved[0].SourceId, Is.EqualTo("one_shot"));
            Assert.That(resolved[0].ResolvedImmediately, Is.True);
            Assert.That(state.IsActivityCompleted("one_shot"), Is.True);
            Assert.That(state.GetActivityExecutions(), Is.Empty);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
        }

        [Test]
        public void Tick_RewardFailureKeepsRepeatableExecution()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            Assert.That(runtime.Start(WorkStart("bad_cycle", "ren", 1)).success, Is.True);

            var result = runtime.Tick(5f);
            var execution = state.GetActivityExecutions()[0];

            Assert.That(result.success, Is.False);
            Assert.That(execution.completedCycles, Is.EqualTo(0));
            Assert.That(execution.elapsedSeconds, Is.Zero);
            Assert.That(state.IsHeroBusy("ren"), Is.True);
        }

        [Test]
        public void Tick_OneShotCreatesPendingResultAndClaimCompletesSource()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            Assert.That(runtime.Start("one_shot_new", "ren").success, Is.True);

            var result = runtime.Tick(5f);

            Assert.That(result.success, Is.True);
            var execution = state.GetActivityExecutions()[0];
            Assert.That(execution.status, Is.EqualTo(GuildIdle.Core.ActivityRuntimeStatus.ResultPending));
            Assert.That(state.IsHeroBusy("ren"), Is.True);
            Assert.That(state.IsActivityCompleted("one_shot_new"), Is.False);
            Assert.That(state.GetItem("resource_pine_wood"), Is.Zero);
            Assert.That(state.GetCurrency("gold_id"), Is.Zero);
            var pending = state.PendingResults.GetAll()[0];
            var claimed = state.PendingResults.ClaimAll("test-claim", pending.resultId, pending.revision, state.Storage.GetSnapshot().Revision);
            Assert.That(claimed.Success, Is.True);
            Assert.That(claimed.Resolved, Is.True);
            Assert.That(state.GetActivityExecutions(), Is.Empty);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.IsActivityCompleted("one_shot_new"), Is.True);
            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(1));
            Assert.That(state.GetCurrency("gold_id"), Is.EqualTo(2));
            foreach (var item in state.ToSaveData().itemStacks)
                Assert.That(item.itemId, Is.Not.EqualTo("gold_id"));
        }

        [Test]
        public void TimerWorkUsesOnCompleteWithoutCycleEffects()
        {
            var state = NewState();
            Assert.That(state.SetHeroEffectCounter("ren", "test_reliable_hands_effect", 1), Is.True);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));

            var descriptor = runtime.GetWorkDescriptor("one_shot_work", "ren", 1);
            var started = runtime.Start("one_shot_work", "ren");
            var running = state.GetActivityExecution(started.executionId);
            var ticked = runtime.Tick(5f);
            var pending = state.PendingResults.GetAll()[0];

            Assert.That(descriptor.success, Is.False);
            Assert.That(started.success, Is.True);
            Assert.That(running.runtimeKind, Is.EqualTo("Activity"));
            Assert.That(ticked.success, Is.True);
            Assert.That(ticked.processedCycles, Is.Zero);
            Assert.That(state.GetHeroEffectCounter("ren", "test_reliable_hands_effect"), Is.EqualTo(1));
            Assert.That(pending.entries, Has.Length.EqualTo(1));
            Assert.That(pending.entries[0].rewardType, Is.EqualTo("Resource"));
            Assert.That(pending.entries[0].targetId, Is.EqualTo("resource_stone"));
            Assert.That(pending.entries[0].quantity, Is.EqualTo(2));
        }

        [Test]
        public void SavedTimerWorkWithLegacyWorkKindCompletesAsOneShot()
        {
            var state = NewState();
            Assert.That(state.SetHeroEffectCounter("ren", "test_reliable_hands_effect", 1), Is.True);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start("one_shot_work", "ren");
            Assert.That(started.success, Is.True);
            var execution = state.GetActivityExecution(started.executionId);
            execution.runtimeKind = "Work";
            execution.plannedCycles = 1;
            execution.currentCycleFatiguePaid = true;
            execution.cyclePhase = "Running";
            Assert.That(state.UpdateActivityExecution(execution), Is.True);

            var storage = new MemorySaveStorage();
            Assert.That(SaveService.Save(state, storage), Is.True);
            var restored = SaveService.Load(_factory, storage);
            runtime = new ActivityRuntimeService(restored, new PlayerStateActivityAdapter(restored));

            var ticked = runtime.Tick(5f);
            var pending = restored.PendingResults.GetAll()[0];

            Assert.That(ticked.success, Is.True);
            Assert.That(ticked.processedCycles, Is.Zero);
            Assert.That(restored.GetHeroEffectCounter("ren", "test_reliable_hands_effect"), Is.EqualTo(1));
            Assert.That(pending.entries, Has.Length.EqualTo(1));
            Assert.That(pending.entries[0].targetId, Is.EqualTo("resource_stone"));
            Assert.That(pending.entries[0].quantity, Is.EqualTo(2));
        }

        [Test]
        public void CancelClearsExecutionWithoutRewardOrRefund()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var fatigue = state.GetHeroFatigue("ren");
            var start = runtime.Start(WorkStart("work_pine_wood", "ren", 2));
            Assert.That(start.success, Is.True);

            var result = runtime.Cancel(start.executionId);

            Assert.That(result.success, Is.True);
            Assert.That(state.GetActivityExecutions(), Is.Empty);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue - 2));
            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(0));
        }

        [Test]
        public void SaveLoadRestoresActiveExecutionAndHeroBusyState()
        {
            var state = NewState();
            var storage = new MemorySaveStorage();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            Assert.That(runtime.Start(WorkStart("work_pine_wood", "ren", 2)).success, Is.True);
            Assert.That(runtime.Tick(3f).success, Is.True);

            Assert.That(SaveService.Save(state, storage), Is.True);
            var restored = SaveService.Load(_factory, storage);
            var execution = restored.GetActivityExecutions()[0];

            Assert.That(execution.activityId, Is.EqualTo("work_pine_wood"));
            Assert.That(execution.heroId, Is.EqualTo("ren"));
            Assert.That(execution.elapsedSeconds, Is.EqualTo(3f));
            Assert.That(restored.IsHeroBusy("ren"), Is.True);
            Assert.That(restored.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo(execution.executionId));
        }

        [Test]
        public void Start_EnforcesActiveHeroLimitAndCancelReleasesIt()
        {
            var state = NewState();
            Assert.That(state.AddHero("test_builder_hero"), Is.True);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), progressionProcessor: new RecordingProgressionProcessor());

            var first = runtime.Start(WorkStart("work_pine_wood", "ren", 2));
            var limited = runtime.Start(WorkStart("work_pine_wood", "test_builder_hero", 2));

            Assert.That(first.success, Is.True);
            Assert.That(limited.success, Is.False);
            Assert.That(HasIssue(limited.issues, "ActiveHeroLimitReached"), Is.True);
            Assert.That(state.GetActivityExecutions(), Has.Length.EqualTo(1));

            Assert.That(runtime.Cancel(first.executionId).success, Is.True);
            var afterCancel = runtime.Start(WorkStart("work_pine_wood", "test_builder_hero", 2));

            Assert.That(afterCancel.success, Is.True);
            Assert.That(state.IsHeroBusy("test_builder_hero"), Is.True);
        }

        [Test]
        public void WorkDescriptorAndStartRequireExplicitAffordableCycleCount()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var fatigue = state.GetHeroFatigue("ren");

            var descriptor = runtime.GetWorkDescriptor("work_pine_wood", "ren", 3);
            var missingCount = runtime.Start("work_pine_wood", "ren");
            var tooMany = runtime.Start(WorkStart("work_pine_wood", "ren", descriptor.descriptor.maxCycleCount + 1));

            Assert.That(descriptor.success, Is.True);
            Assert.That(descriptor.descriptor.minCycleCount, Is.EqualTo(1));
            Assert.That(descriptor.descriptor.plannedFatigue, Is.EqualTo(6));
            Assert.That(descriptor.descriptor.plannedDurationSeconds, Is.EqualTo(30));
            Assert.That(descriptor.descriptor.expectedRewards, Has.Length.EqualTo(2));
            Assert.That(descriptor.descriptor.expectedRewards[0].minAmount, Is.EqualTo(3));
            Assert.That(descriptor.descriptor.expectedRewards[0].maxAmount, Is.EqualTo(3));
            Assert.That(descriptor.descriptor.expectedRewards[1].rewardType, Is.EqualTo("SkillExp"));
            Assert.That(missingCount.success, Is.False);
            Assert.That(HasIssue(missingCount.issues, "CycleCountRequired"), Is.True);
            Assert.That(tooMany.success, Is.False);
            Assert.That(HasIssue(tooMany.issues, "CycleCountOutOfRange"), Is.True);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(state.GetActivityExecutions(), Is.Empty);
        }

        [Test]
        public void ReliableHandsUsesOnePersistentCounterAcrossCompletedGatheringCycles()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), new FixedRandom(1));
            Assert.That(runtime.Start(WorkStart("work_pine_wood", "ren", 2)).success, Is.True);

            var ticked = runtime.Tick(20f);
            var bag = state.PendingResults.GetAll()[0];
            long resourceQuantity = 0;
            foreach (var entry in bag.entries)
                if (entry.rewardType == "Resource") resourceQuantity += entry.quantity;

            Assert.That(ticked.success, Is.True);
            Assert.That(resourceQuantity, Is.EqualTo(3));
            Assert.That(state.GetHeroEffectCounter("ren", "test_reliable_hands_effect"), Is.EqualTo(2));
        }

        [Test]
        public void ReliableHandsTargetsBaseResourceInsteadOfFirstLootEntry()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), new FixedRandom(1));
            Assert.That(runtime.Start(WorkStart("test_multi_loot_work", "ren", 2)).success, Is.True);

            Assert.That(runtime.Tick(20f).success, Is.True);
            var bag = state.PendingResults.GetAll()[0];
            long consumables = 0;
            long resources = 0;
            foreach (var entry in bag.entries)
            {
                if (entry.rewardType == "Consumable") consumables += entry.quantity;
                if (entry.rewardType == "Resource") resources += entry.quantity;
            }

            Assert.That(consumables, Is.EqualTo(2));
            Assert.That(resources, Is.EqualTo(3));
        }

        [Test]
        public void SaveLoadFinalizesStagedWorkCycleWithoutRecalculatingIt()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), new FixedRandom(100));
            var started = runtime.Start(WorkStart("work_pine_wood", "ren", 1));
            var execution = state.GetActivityExecution(started.executionId);
            execution.completedCycles = 1;
            execution.currentCycleFatiguePaid = false;
            execution.cyclePhase = "ResultStaged";
            execution.stagedRewards = new[]
            {
                new ActivityStagedRewardSaveData { rewardType = "Resource", targetId = "resource_pine_wood", quantity = 7, origin = PendingResultOrigin.ActivityReward },
                new ActivityStagedRewardSaveData { rewardType = "SkillExp", targetId = "skill_gathering", quantity = 3, origin = PendingResultOrigin.ActivityReward }
            };
            Assert.That(state.UpdateActivityExecution(execution), Is.True);
            var storage = new MemorySaveStorage();
            Assert.That(SaveService.Save(state, storage), Is.True);

            state = SaveService.Load(_factory, storage);
            runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), new FixedRandom(1));
            var ticked = runtime.Tick(0f);
            var restored = state.GetActivityExecution(started.executionId);
            var bag = state.PendingResults.GetAll()[0];

            Assert.That(ticked.success, Is.True);
            Assert.That(restored.completedCycles, Is.EqualTo(1));
            Assert.That(restored.status, Is.EqualTo(GuildIdle.Core.ActivityRuntimeStatus.ResultPending));
            Assert.That(bag.entries, Has.Length.EqualTo(2));
            Assert.That(bag.entries[0].quantity, Is.EqualTo(7));
            Assert.That(bag.entries[1].quantity, Is.EqualTo(3));
            Assert.That(state.GetHeroEffectCounter("ren", "test_reliable_hands_effect"), Is.Zero);
        }

        [Test]
        public void WorkEffectValidationFailureDoesNotPersistCompletedCycleOrStagedPhase()
        {
            var state = NewState();
            Assert.That(state.SetHeroEffectCounter("ren", "test_reliable_hands_effect", 1), Is.True);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), new FixedRandom(1));
            var started = runtime.Start(WorkStart("bad_effect_target_work", "ren", 1));

            var ticked = runtime.Tick(10f);
            var execution = state.GetActivityExecution(started.executionId);

            Assert.That(ticked.success, Is.False);
            Assert.That(HasIssue(ticked.issues, "HeroEffectTarget"), Is.True);
            Assert.That(execution.completedCycles, Is.Zero);
            Assert.That(execution.cyclePhase, Is.EqualTo("Running"));
            Assert.That(execution.stagedRewards, Is.Empty);
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
            Assert.That(state.GetHeroEffectCounter("ren", "test_reliable_hands_effect"), Is.EqualTo(1));
        }

        [Test]
        public void MissingDangerFormulaDoesNotCompleteCycleOrGrantReward()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), new FixedRandom(1));
            var started = runtime.Start(WorkStart("bad_danger_work", "ren", 1));

            var ticked = runtime.Tick(10f);
            var execution = state.GetActivityExecution(started.executionId);

            Assert.That(ticked.success, Is.False);
            Assert.That(HasIssue(ticked.issues, "DangerFormula"), Is.True);
            Assert.That(execution.completedCycles, Is.Zero);
            Assert.That(execution.cyclePhase, Is.EqualTo("Running"));
            Assert.That(execution.stagedRewards, Is.Empty);
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
        }

        [Test]
        public void FailedWorkDescriptorValidationIsRepeatableWithoutRandomOrMutation()
        {
            var state = NewState();
            var random = new CountingRandom();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), random);
            var fatigue = state.GetHeroFatigue("ren");
            var started = runtime.Start(WorkStart("bad_reward_range_work", "ren", 1));

            var first = runtime.Tick(10f);
            var afterFirst = state.GetActivityExecution(started.executionId);
            var second = runtime.Tick(10f);
            var afterSecond = state.GetActivityExecution(started.executionId);

            Assert.That(started.success, Is.True);
            Assert.That(first.success, Is.False);
            Assert.That(second.success, Is.False);
            Assert.That(HasIssue(first.issues, "Resource"), Is.True);
            Assert.That(HasIssue(second.issues, "Resource"), Is.True);
            Assert.That(random.RangeCalls, Is.Zero);
            Assert.That(random.PercentCalls, Is.Zero);
            Assert.That(afterFirst.completedCycles, Is.Zero);
            Assert.That(afterSecond.completedCycles, Is.Zero);
            Assert.That(afterSecond.cyclePhase, Is.EqualTo("Running"));
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue - 1));
        }

        [TestCase("bad_danger_disabled_work", "FormulaDisabled")]
        [TestCase("bad_danger_unsupported_work", "FormulaTypeUnsupported")]
        public void InvalidDangerFormulaDescriptorFailsBeforeWorkCycleMutation(string activityId, string issueType)
        {
            var state = NewState();
            var random = new CountingRandom();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), random);
            var started = runtime.Start(WorkStart(activityId, "ren", 1));

            var ticked = runtime.Tick(10f);
            var execution = state.GetActivityExecution(started.executionId);

            Assert.That(started.success, Is.True);
            Assert.That(ticked.success, Is.False);
            Assert.That(HasIssue(ticked.issues, issueType), Is.True);
            Assert.That(random.RangeCalls, Is.Zero);
            Assert.That(random.PercentCalls, Is.Zero);
            Assert.That(execution.completedCycles, Is.Zero);
            Assert.That(execution.dangerRollCompleted, Is.False);
            Assert.That(runtime.GetPendingLinkedCombatStarts(), Is.Empty);
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
        }

        [Test]
        public void AdvanceWorkProcessesMultipleCyclesAndPartialWithoutSaving()
        {
            var storage = new MemorySaveStorage();
            var state = NewState();
            Assert.That(SaveService.Save(state, storage), Is.True);
            state = SaveService.Load(_factory, storage);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), new FixedRandom(1));
            var initialFatigue = state.GetHeroFatigue("ren");
            var started = runtime.Start(WorkStart("test_multi_loot_work", "ren", 3));
            var processor = NewWorkAdvanceProcessor(state, new FixedRandom(1));
            var saveCallsBeforeAdvance = storage.SaveCalls;

            var result = processor.Advance(new WorkAdvanceRequest(started.executionId, 25));
            var execution = state.GetActivityExecution(started.executionId);

            Assert.That(result.Success, Is.True);
            Assert.That(result.StopReason, Is.EqualTo(WorkAdvanceStopReason.IntervalExhausted));
            Assert.That(result.ProcessedCycles, Is.EqualTo(2));
            Assert.That(result.ConsumedSeconds, Is.EqualTo(25));
            Assert.That(result.RemainingSeconds, Is.Zero);
            Assert.That(result.HasPartialCycle, Is.True);
            Assert.That(execution.completedCycles, Is.EqualTo(2));
            Assert.That(execution.elapsedSeconds, Is.EqualTo(5f));
            Assert.That(execution.currentCycleFatiguePaid, Is.True);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(initialFatigue - 3));
            Assert.That(state.PendingResults.GetAll()[0].entries, Has.Length.EqualTo(4));
            Assert.That(storage.SaveCalls, Is.EqualTo(saveCallsBeforeAdvance));
        }

        [Test]
        public void AdvanceWorkMatchesOnlineWorkProgressionForSameInterval()
        {
            var onlineState = NewState();
            var offlineState = NewState();
            var online = new ActivityRuntimeService(onlineState, new PlayerStateActivityAdapter(onlineState), new FixedRandom(1));
            var offline = new ActivityRuntimeService(offlineState, new PlayerStateActivityAdapter(offlineState), new FixedRandom(1));
            var onlineStart = online.Start(WorkStart("work_pine_wood", "ren", 3));
            var offlineStart = offline.Start(WorkStart("work_pine_wood", "ren", 3));
            var offlineProcessor = NewWorkAdvanceProcessor(offlineState, new FixedRandom(1));

            Assert.That(online.Tick(25f).success, Is.True);
            var advanced = offlineProcessor.Advance(new WorkAdvanceRequest(offlineStart.executionId, 25));
            var onlineExecution = onlineState.GetActivityExecution(onlineStart.executionId);
            var offlineExecution = offlineState.GetActivityExecution(offlineStart.executionId);

            Assert.That(advanced.Success, Is.True);
            Assert.That(offlineExecution.completedCycles, Is.EqualTo(onlineExecution.completedCycles));
            Assert.That(offlineExecution.elapsedSeconds, Is.EqualTo(onlineExecution.elapsedSeconds));
            Assert.That(offlineExecution.currentCycleFatiguePaid, Is.EqualTo(onlineExecution.currentCycleFatiguePaid));
            Assert.That(offlineState.GetHeroFatigue("ren"), Is.EqualTo(onlineState.GetHeroFatigue("ren")));
            Assert.That(offlineState.GetHeroEffectCounter("ren", "test_reliable_hands_effect"),
                Is.EqualTo(onlineState.GetHeroEffectCounter("ren", "test_reliable_hands_effect")));
            Assert.That(offlineState.PendingResults.GetAll()[0].entries[0].quantity,
                Is.EqualTo(onlineState.PendingResults.GetAll()[0].entries[0].quantity));
        }

        [Test]
        public void AdvanceWorkDoesNotProcessStandardConstructionOrResultPendingExecutions()
        {
            var standardState = NewState();
            var standardRuntime = new ActivityRuntimeService(standardState, new PlayerStateActivityAdapter(standardState));
            var standard = standardRuntime.Start("one_shot_new", "ren");
            var standardResult = NewWorkAdvanceProcessor(standardState).Advance(new WorkAdvanceRequest(standard.executionId, 100));

            Assert.That(standardResult.Success, Is.False);
            Assert.That(standardResult.StopReason, Is.EqualTo(WorkAdvanceStopReason.NotWorkExecution));
            Assert.That(standardState.GetActivityExecution(standard.executionId).elapsedSeconds, Is.Zero);

            var buildState = NewState();
            buildState.UnlockBuilding("building_hall");
            buildState.SetBuildingLevel("building_hall", 0);
            var buildRuntime = new ActivityRuntimeService(
                buildState,
                new PlayerStateActivityAdapter(buildState),
                progressionProcessor: new RecordingProgressionProcessor());
            var build = buildRuntime.Start(new ActivityStartRequest { activityId = "test_build_empty", heroId = "ren" });
            var buildResult = NewWorkAdvanceProcessor(buildState).Advance(new WorkAdvanceRequest(build.executionId, 100));

            Assert.That(buildResult.Success, Is.False);
            Assert.That(buildResult.StopReason, Is.EqualTo(WorkAdvanceStopReason.NotWorkExecution));
            Assert.That(buildState.GetActivityExecution(build.executionId).accumulatedBuildPoints, Is.Zero);

            var pendingState = NewState();
            var pendingRuntime = new ActivityRuntimeService(pendingState, new PlayerStateActivityAdapter(pendingState));
            var pending = pendingRuntime.Start(WorkStart("work_pine_wood", "ren", 1));
            var pendingProcessor = NewWorkAdvanceProcessor(pendingState);
            Assert.That(pendingProcessor.Advance(new WorkAdvanceRequest(pending.executionId, 10)).Success, Is.True);
            var pendingResult = pendingProcessor.Advance(new WorkAdvanceRequest(pending.executionId, 10));

            Assert.That(pendingResult.Success, Is.False);
            Assert.That(pendingResult.StopReason, Is.EqualTo(WorkAdvanceStopReason.ExecutionNotRunning));
            Assert.That(pendingState.GetActivityExecution(pending.executionId).completedCycles, Is.EqualTo(1));
        }

        [Test]
        public void AdvanceWorkStopsCleanlyWhenNextCycleFatigueCannotBePaid()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start(WorkStart("work_pine_wood", "ren", 3));
            var processor = NewWorkAdvanceProcessor(state);
            Assert.That(state.SpendHeroFatigue("ren", state.GetHeroFatigue("ren")), Is.True);

            var result = processor.Advance(new WorkAdvanceRequest(started.executionId, 30));
            var execution = state.GetActivityExecution(started.executionId);
            var repeated = processor.Advance(new WorkAdvanceRequest(started.executionId, 20));
            var bag = state.PendingResults.GetAll()[0];

            Assert.That(result.Success, Is.True);
            Assert.That(result.StopReason, Is.EqualTo(WorkAdvanceStopReason.InsufficientFatigue));
            Assert.That(result.ProcessedCycles, Is.EqualTo(1));
            Assert.That(result.ConsumedSeconds, Is.EqualTo(10));
            Assert.That(result.RemainingSeconds, Is.EqualTo(20));
            Assert.That(execution.completedCycles, Is.EqualTo(1));
            Assert.That(execution.status, Is.EqualTo(ActivityRuntimeStatus.ResultPending));
            Assert.That(execution.endReason, Is.EqualTo("InsufficientFatigue"));
            Assert.That(execution.currentCycleFatiguePaid, Is.False);
            Assert.That(execution.stagedRewards, Is.Empty);
            Assert.That(bag.entries, Has.Length.EqualTo(2));
            Assert.That(repeated.Success, Is.False);
            Assert.That(repeated.StopReason, Is.EqualTo(WorkAdvanceStopReason.ExecutionNotRunning));
            Assert.That(repeated.ProcessedCycles, Is.Zero);
            Assert.That(repeated.ConsumedSeconds, Is.Zero);
            Assert.That(state.PendingResults.GetAll()[0].entries, Has.Length.EqualTo(2));
            Assert.That(state.PendingResults.ClaimAll(
                "claim-fatigue-stop",
                bag.resultId,
                bag.revision,
                state.Storage.GetSnapshot().Revision).Success, Is.True);
            Assert.That(state.GetActivityExecution(started.executionId), Is.Null);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
        }

        [Test]
        public void AdvanceWorkPartialCycleSurvivesSaveLoadWithoutRepayingFatigue()
        {
            var state = NewState();
            var initialFatigue = state.GetHeroFatigue("ren");
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start(WorkStart("work_pine_wood", "ren", 2));
            var processor = NewWorkAdvanceProcessor(state);

            var partial = processor.Advance(new WorkAdvanceRequest(started.executionId, 3));
            var storage = new MemorySaveStorage();
            Assert.That(SaveService.Save(state, storage), Is.True);
            state = SaveService.Load(_factory, storage);
            runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            processor = NewWorkAdvanceProcessor(state);
            var completed = processor.Advance(new WorkAdvanceRequest(started.executionId, 7));
            var execution = state.GetActivityExecution(started.executionId);

            Assert.That(partial.HasPartialCycle, Is.True);
            Assert.That(completed.Success, Is.True);
            Assert.That(completed.ProcessedCycles, Is.EqualTo(1));
            Assert.That(execution.completedCycles, Is.EqualTo(1));
            Assert.That(execution.elapsedSeconds, Is.Zero);
            Assert.That(execution.currentCycleFatiguePaid, Is.True);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(initialFatigue - 4));
        }

        [Test]
        public void AdvanceWorkDangerBoundaryIsStableAndDoesNotCreateCombat()
        {
            var state = NewState();
            var random = new CountingDangerSequenceRandom(100, 1);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), random);
            var started = runtime.Start(WorkStart("hunt_rabbits", "ren", 3));
            var processor = NewWorkAdvanceProcessor(state, random);

            var result = processor.Advance(new WorkAdvanceRequest(started.executionId, 30));
            var execution = state.GetActivityExecution(started.executionId);
            var callsAtBoundary = random.RangeCalls;
            var repeated = processor.Advance(new WorkAdvanceRequest(started.executionId, 10));

            Assert.That(result.Success, Is.True);
            Assert.That(result.StopReason, Is.EqualTo(WorkAdvanceStopReason.DangerBoundaryReached));
            Assert.That(result.ProcessedCycles, Is.EqualTo(2));
            Assert.That(result.ConsumedSeconds, Is.EqualTo(20));
            Assert.That(result.RemainingSeconds, Is.EqualTo(10));
            Assert.That(execution.completedCycles, Is.EqualTo(2));
            Assert.That(execution.cyclePhase, Is.EqualTo("ResultStaged"));
            Assert.That(execution.dangerRollCompleted, Is.True);
            Assert.That(execution.stagedRewards, Has.Length.EqualTo(2));
            Assert.That(execution.linkedCombat, Is.Null);
            Assert.That(runtime.GetPendingLinkedCombatStarts(), Is.Empty);
            Assert.That(state.GetCombatAggregates(), Is.Empty);
            Assert.That(state.PendingResults.GetAll()[0].entries, Has.Length.EqualTo(2));
            Assert.That(repeated.StopReason, Is.EqualTo(WorkAdvanceStopReason.DangerBoundaryReached));
            Assert.That(repeated.ProcessedCycles, Is.Zero);
            Assert.That(random.RangeCalls, Is.EqualTo(callsAtBoundary));
        }

        [Test]
        public void AdvanceWorkDangerBoundarySurvivesSaveLoadWithoutReroll()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(
                state,
                new PlayerStateActivityAdapter(state),
                new CountingDangerSequenceRandom(1));
            var started = runtime.Start(WorkStart("hunt_rabbits", "ren", 2));
            var processor = NewWorkAdvanceProcessor(state, new CountingDangerSequenceRandom(1));
            Assert.That(processor.Advance(new WorkAdvanceRequest(started.executionId, 10)).DangerBoundaryReached, Is.True);
            var storage = new MemorySaveStorage();
            Assert.That(SaveService.Save(state, storage), Is.True);
            state = SaveService.Load(_factory, storage);
            var replayRandom = new CountingRandom();
            runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), replayRandom);
            processor = NewWorkAdvanceProcessor(state, replayRandom);

            var replay = processor.Advance(new WorkAdvanceRequest(started.executionId, 10));

            Assert.That(replay.Success, Is.True);
            Assert.That(replay.DangerBoundaryReached, Is.True);
            Assert.That(replay.ProcessedCycles, Is.Zero);
            Assert.That(replayRandom.RangeCalls, Is.Zero);
            Assert.That(replayRandom.PercentCalls, Is.Zero);
            Assert.That(state.GetActivityExecution(started.executionId).completedCycles, Is.EqualTo(1));
            Assert.That(runtime.GetPendingLinkedCombatStarts(), Is.Empty);
        }

        [Test]
        public void PrepareDangerEncounterPartitionsCurrentCycleAndKeepsPreviousBag()
        {
            var state = NewState();
            var random = new CountingDangerSequenceRandom(100, 1);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), random);
            var started = runtime.Start(WorkStart("hunt_rabbits", "ren", 3));
            var workProcessor = NewWorkAdvanceProcessor(state, random);
            Assert.That(
                workProcessor.Advance(new WorkAdvanceRequest(started.executionId, 30)).DangerBoundaryReached,
                Is.True);
            var randomCallsAtBoundary = random.RangeCalls;
            var fatigueAtBoundary = state.GetHeroFatigue("ren");
            using var processor = NewDangerEncounterPreparationProcessor(state);

            var prepared = processor.Prepare(new DangerEncounterPreparationRequest(started.executionId));
            var execution = state.GetActivityExecution(started.executionId);
            var bag = state.PendingResults.GetAll()[0];
            long bagResources = 0;
            long bagSkillExp = 0;
            foreach (var entry in bag.entries)
            {
                if (entry.rewardType == "Resource") bagResources += entry.quantity;
                if (entry.rewardType == "SkillExp") bagSkillExp += entry.quantity;
            }

            Assert.That(prepared.Success, Is.True);
            Assert.That(prepared.Code, Is.EqualTo(DangerEncounterPreparationCode.PendingEncounterCreated));
            Assert.That(prepared.RequestCreated, Is.True);
            Assert.That(prepared.Replayed, Is.False);
            Assert.That(prepared.CombatEntryCount, Is.EqualTo(1));
            Assert.That(prepared.NonCombatEntryCount, Is.EqualTo(1));
            Assert.That(execution.status, Is.EqualTo(ActivityRuntimeStatus.ResultPending));
            Assert.That(execution.cyclePhase, Is.EqualTo("ResultStaged"));
            Assert.That(execution.stagedRewards, Is.Empty);
            Assert.That(execution.linkedCombat.loot, Has.Length.EqualTo(1));
            Assert.That(
                execution.linkedCombat.loot[0].origin,
                Is.EqualTo(PendingResultOrigin.ActivityLootInCombat));
            Assert.That(bagResources, Is.EqualTo(1), "Previous-cycle loot must stay in the Activity Bag.");
            Assert.That(bagSkillExp, Is.EqualTo(4), "Skill EXP from both cycles must stay outside combat loss.");
            Assert.That(execution.activityBagResolved, Is.False);
            Assert.That(state.IsHeroBusy("ren"), Is.True);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigueAtBoundary));
            Assert.That(random.RangeCalls, Is.EqualTo(randomCallsAtBoundary));
            Assert.That(state.GetCombatAggregates(), Is.Empty);
        }

        [Test]
        public void PrepareDangerEncounterMovesEveryItemLikeTypeAndResolvesEmptyBagAfterCommit()
        {
            var storage = new MemorySaveStorage();
            var state = NewState();
            Assert.That(SaveService.Save(state, storage), Is.True);
            state = SaveService.Load(_factory, storage);
            var random = new CountingDangerSequenceRandom(1);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), random);
            var started = runtime.Start(WorkStart("hunt_boars", "ren", 2));
            Assert.That(
                NewWorkAdvanceProcessor(state, random)
                    .Advance(new WorkAdvanceRequest(started.executionId, 10))
                    .DangerBoundaryReached,
                Is.True);
            var saveCallsBefore = storage.SaveCalls;
            var resolvedEvents = 0;
            state.PendingResults.Resolved += _ => resolvedEvents++;
            using var processor = NewDangerEncounterPreparationProcessor(state);

            var prepared = processor.Prepare(new DangerEncounterPreparationRequest(started.executionId));
            var execution = state.GetActivityExecution(started.executionId);
            var lootTypes = new HashSet<string>();
            foreach (var entry in execution.linkedCombat.loot)
                lootTypes.Add(entry.rewardType);

            Assert.That(prepared.Success, Is.True);
            Assert.That(prepared.CombatEntryCount, Is.EqualTo(5));
            Assert.That(prepared.NonCombatEntryCount, Is.Zero);
            Assert.That(prepared.ActivityPendingResultId, Is.Empty);
            Assert.That(prepared.ActivityBagResolved, Is.True);
            Assert.That(prepared.DeferredResolvedEvents, Has.Count.EqualTo(1));
            Assert.That(execution.pendingResultId, Is.Null.Or.Empty);
            Assert.That(execution.activityBagResolved, Is.True);
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
            Assert.That(lootTypes, Is.EquivalentTo(new[] { "Resource", "Consumable", "Recipe", "Equipment", "Item" }));
            Assert.That(state.GetCombatAggregates(), Is.Empty);
            Assert.That(state.IsHeroBusy("ren"), Is.True);
            Assert.That(storage.SaveCalls, Is.EqualTo(saveCallsBefore), "Offline core must not save.");
            Assert.That(resolvedEvents, Is.Zero);

            Assert.That(SaveService.Save(state, storage), Is.True);
            processor.PublishDeferredResolvedEvents(prepared);
            processor.PublishDeferredResolvedEvents(prepared);
            Assert.That(resolvedEvents, Is.EqualTo(1));

            var restored = SaveService.Load(_factory, storage);
            var restoredExecution = restored.GetActivityExecution(started.executionId);
            Assert.That(restoredExecution, Is.Not.Null);
            Assert.That(restoredExecution.pendingResultId, Is.Null.Or.Empty);
            Assert.That(restoredExecution.activityBagResolved, Is.True);
            Assert.That(restoredExecution.linkedCombat, Is.Not.Null);
            Assert.That(restored.PendingResults.GetAll(), Is.Empty);
            Assert.That(restored.IsHeroBusy("ren"), Is.True);
        }

        [Test]
        public void PrepareDangerEncounterReplayRejectsMissingStartedAggregateWithoutMutation()
        {
            var state = NewState();
            var executionId = PrepareDangerEncounter(state);
            var startedLink = state.GetActivityExecution(executionId);
            startedLink.linkedCombat.combatExecutionId = "missing-combat";
            Assert.That(state.UpdateActivityExecution(startedLink), Is.True);
            var before = JsonUtility.ToJson(state.ToSaveData());
            using var processor = NewDangerEncounterPreparationProcessor(state);

            var replay = processor.Prepare(new DangerEncounterPreparationRequest(executionId));

            Assert.That(replay.Success, Is.False);
            Assert.That(replay.Code, Is.EqualTo(DangerEncounterPreparationCode.DataIntegrityFailure));
            Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(before));
            Assert.That(state.GetCombatAggregates(), Is.Empty);
            Assert.That(state.IsHeroBusy("ren"), Is.True);
        }

        [Test]
        public void PrepareDangerEncounterReplayRejectsMismatchedStartedAggregateWithoutRepair()
        {
            var state = NewState();
            var executionId = PrepareDangerEncounter(state);
            Assert.That(AddStartedLinkedCombat(state, executionId), Is.True);
            var corrupted = state.ToSaveData();
            corrupted.combatRuntime.executions[0].sourceRequestId = "wrong-request";
            state = _factory.Create(corrupted);
            var before = JsonUtility.ToJson(state.ToSaveData());
            using var processor = NewDangerEncounterPreparationProcessor(state);

            var replay = processor.Prepare(new DangerEncounterPreparationRequest(executionId));

            Assert.That(replay.Success, Is.False);
            Assert.That(replay.Code, Is.EqualTo(DangerEncounterPreparationCode.DataIntegrityFailure));
            Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(before));
            Assert.That(state.GetCombatAggregates(), Has.Length.EqualTo(1));
            Assert.That(state.IsHeroBusy("ren"), Is.True);
        }

        [Test]
        public void PrepareDangerEncounterReplayAcceptsValidStartedAggregateWithoutDuplicates()
        {
            var state = NewState();
            var executionId = PrepareDangerEncounter(state);
            Assert.That(AddStartedLinkedCombat(state, executionId), Is.True);
            var before = JsonUtility.ToJson(state.ToSaveData());
            using var processor = NewDangerEncounterPreparationProcessor(state);

            var replay = processor.Prepare(new DangerEncounterPreparationRequest(executionId));

            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Code, Is.EqualTo(DangerEncounterPreparationCode.AlreadyPrepared));
            Assert.That(replay.RequestCreated, Is.False);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(before));
            Assert.That(state.GetCombatAggregates(), Has.Length.EqualTo(1));
            Assert.That(state.ToSaveData().combatRuntime.sessions, Has.Length.EqualTo(1));
        }

        [Test]
        public void PrepareDangerEncounterReplayRejectsCorruptLootWithoutRepair()
        {
            var state = NewState();
            var executionId = PrepareDangerEncounter(state);

            var corrupt = state.GetActivityExecution(executionId);
            corrupt.linkedCombat.loot[0].quantity++;
            Assert.That(state.UpdateActivityExecution(corrupt), Is.True);
            var corruptState = JsonUtility.ToJson(state.ToSaveData());
            using var processor = NewDangerEncounterPreparationProcessor(state);

            var rejected = processor.Prepare(new DangerEncounterPreparationRequest(executionId));

            Assert.That(rejected.Success, Is.False);
            Assert.That(rejected.Code, Is.EqualTo(DangerEncounterPreparationCode.DataIntegrityFailure));
            Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(corruptState));
        }

        [Test]
        public void PrepareDangerEncounterCanBeRolledBackByOuterCheckpoint()
        {
            var state = NewState();
            var activityState = new PlayerStateActivityAdapter(state);
            var random = new CountingDangerSequenceRandom(1);
            var runtime = new ActivityRuntimeService(state, activityState, random);
            var started = runtime.Start(WorkStart("hunt_rabbits", "ren", 2));
            Assert.That(
                NewWorkAdvanceProcessor(state, random)
                    .Advance(new WorkAdvanceRequest(started.executionId, 10))
                    .DangerBoundaryReached,
                Is.True);
            var checkpoint = activityState.CaptureCheckpoint();
            var before = state.GetActivityExecution(started.executionId);
            using var processor = NewDangerEncounterPreparationProcessor(state);

            Assert.That(
                processor.Prepare(new DangerEncounterPreparationRequest(started.executionId)).Success,
                Is.True);
            activityState.RestoreCheckpoint(checkpoint);
            var restored = state.GetActivityExecution(started.executionId);

            Assert.That(restored.status, Is.EqualTo(ActivityRuntimeStatus.Running));
            Assert.That(restored.cyclePhase, Is.EqualTo("ResultStaged"));
            Assert.That(restored.stagedRewards, Has.Length.EqualTo(before.stagedRewards.Length));
            Assert.That(restored.linkedCombat, Is.Null);
            Assert.That(restored.dangerHandoffFingerprint, Is.Null.Or.Empty);
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
            Assert.That(state.IsHeroBusy("ren"), Is.True);
        }

        [Test]
        public void OnlineAndOfflineDangerHandoffProduceEquivalentPartitionAndRequestContext()
        {
            var onlineState = NewState();
            var onlineRuntime = new ActivityRuntimeService(
                onlineState,
                new PlayerStateActivityAdapter(onlineState),
                new CountingDangerSequenceRandom(1));
            var onlineStart = onlineRuntime.Start(WorkStart("hunt_boars", "ren", 2));
            Assert.That(onlineRuntime.Tick(10f).success, Is.True);
            var online = onlineState.GetActivityExecution(onlineStart.executionId);

            var offlineState = NewState();
            var offlineRandom = new CountingDangerSequenceRandom(1);
            var offlineRuntime = new ActivityRuntimeService(
                offlineState,
                new PlayerStateActivityAdapter(offlineState),
                offlineRandom);
            var offlineStart = offlineRuntime.Start(WorkStart("hunt_boars", "ren", 2));
            Assert.That(
                NewWorkAdvanceProcessor(offlineState, offlineRandom)
                    .Advance(new WorkAdvanceRequest(offlineStart.executionId, 10))
                    .DangerBoundaryReached,
                Is.True);
            using var processor = NewDangerEncounterPreparationProcessor(offlineState);
            Assert.That(
                processor.Prepare(new DangerEncounterPreparationRequest(offlineStart.executionId)).Success,
                Is.True);
            var offline = offlineState.GetActivityExecution(offlineStart.executionId);

            Assert.That(offline.endReason, Is.EqualTo(online.endReason));
            Assert.That(offline.status, Is.EqualTo(online.status));
            Assert.That(offline.cyclePhase, Is.EqualTo(online.cyclePhase));
            Assert.That(offline.activityBagResolved, Is.EqualTo(online.activityBagResolved));
            Assert.That(offline.linkedCombat.heroId, Is.EqualTo(online.linkedCombat.heroId));
            Assert.That(offline.linkedCombat.dangerEncounterId, Is.EqualTo(online.linkedCombat.dangerEncounterId));
            Assert.That(offline.linkedCombat.enemyGroupId, Is.EqualTo(online.linkedCombat.enemyGroupId));
            Assert.That(offline.linkedCombat.combatMode, Is.EqualTo(online.linkedCombat.combatMode));
            Assert.That(offline.linkedCombat.enemyExpTargetId, Is.EqualTo(online.linkedCombat.enemyExpTargetId));
            Assert.That(offline.linkedCombat.defeatLossRule, Is.EqualTo(online.linkedCombat.defeatLossRule));
            Assert.That(offline.linkedCombat.suppressFatigueCost, Is.EqualTo(online.linkedCombat.suppressFatigueCost));
            Assert.That(offline.linkedCombat.loot, Has.Length.EqualTo(online.linkedCombat.loot.Length));
            for (var index = 0; index < online.linkedCombat.loot.Length; index++)
            {
                Assert.That(offline.linkedCombat.loot[index].rewardType, Is.EqualTo(online.linkedCombat.loot[index].rewardType));
                Assert.That(offline.linkedCombat.loot[index].targetId, Is.EqualTo(online.linkedCombat.loot[index].targetId));
                Assert.That(offline.linkedCombat.loot[index].quantity, Is.EqualTo(online.linkedCombat.loot[index].quantity));
                Assert.That(offline.linkedCombat.loot[index].origin, Is.EqualTo(online.linkedCombat.loot[index].origin));
            }
            Assert.That(offlineState.GetCombatAggregates(), Is.Empty);
        }

        [Test]
        public void PrepareDangerEncounterRejectsExecutionBeforeTriggeredBoundary()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start(WorkStart("hunt_rabbits", "ren", 2));
            using var processor = NewDangerEncounterPreparationProcessor(state);

            var result = processor.Prepare(new DangerEncounterPreparationRequest(started.executionId));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(DangerEncounterPreparationCode.NotDangerBoundary));
            Assert.That(state.GetActivityExecution(started.executionId).linkedCombat, Is.Null);
            Assert.That(state.GetCombatAggregates(), Is.Empty);
        }

        [Test]
        public void StartedDangerEncounterSurvivesSaveLoadAndReplaysWithoutDuplicates()
        {
            var storage = new MemorySaveStorage();
            var state = NewState();
            var executionId = PrepareDangerEncounter(state);
            Assert.That(AddStartedLinkedCombat(state, executionId), Is.True);
            Assert.That(SaveService.Save(state, storage), Is.True);

            state = SaveService.Load(_factory, storage);
            using var restoredProcessor = NewDangerEncounterPreparationProcessor(state);
            var replay = restoredProcessor.Prepare(new DangerEncounterPreparationRequest(executionId));

            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Code, Is.EqualTo(DangerEncounterPreparationCode.AlreadyPrepared));
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.RequestCreated, Is.False);
            Assert.That(state.GetActivityExecutions(), Has.Length.EqualTo(1));
            Assert.That(state.GetActivityExecution(executionId).linkedCombat.combatExecutionId, Is.Not.Empty);
            Assert.That(state.PendingResults.GetAll(), Has.Length.EqualTo(1));
            Assert.That(state.GetCombatAggregates(), Has.Length.EqualTo(1));
            Assert.That(state.ToSaveData().combatRuntime.sessions, Has.Length.EqualTo(1));
        }

        [Test]
        public void AdvanceWorkProcessingLimitPreservesRemainingIntervalAndCanResume()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start(WorkStart("work_pine_wood", "ren", 3));
            var processor = NewWorkAdvanceProcessor(state);

            var limited = processor.Advance(new WorkAdvanceRequest(started.executionId, 30, 1));
            var resumed = processor.Advance(new WorkAdvanceRequest(started.executionId, limited.RemainingSeconds, 10));

            Assert.That(limited.Success, Is.True);
            Assert.That(limited.StopReason, Is.EqualTo(WorkAdvanceStopReason.ProcessingLimitReached));
            Assert.That(limited.ProcessedCycles, Is.EqualTo(1));
            Assert.That(limited.ConsumedSeconds, Is.EqualTo(10));
            Assert.That(limited.RemainingSeconds, Is.EqualTo(20));
            Assert.That(resumed.Success, Is.True);
            Assert.That(resumed.StopReason, Is.EqualTo(WorkAdvanceStopReason.PlanCompleted));
            Assert.That(resumed.ProcessedCycles, Is.EqualTo(2));
            Assert.That(state.GetActivityExecution(started.executionId).completedCycles, Is.EqualTo(3));
        }

        [Test]
        public void AdvanceWorkCanBeRolledBackByOuterCheckpoint()
        {
            var state = NewState();
            var activityState = new PlayerStateActivityAdapter(state);
            var runtime = new ActivityRuntimeService(state, activityState, new FixedRandom(1));
            var started = runtime.Start(WorkStart("work_pine_wood", "ren", 3));
            var processor = NewWorkAdvanceProcessor(state, new FixedRandom(1));
            var checkpoint = activityState.CaptureCheckpoint();
            var randomCheckpoint = processor.CaptureRandomState();
            var fatigueBefore = state.GetHeroFatigue("ren");

            Assert.That(processor.Advance(new WorkAdvanceRequest(started.executionId, 20)).Success, Is.True);
            activityState.RestoreCheckpoint(checkpoint);
            processor.RestoreRandomState(randomCheckpoint);
            var restored = state.GetActivityExecution(started.executionId);

            Assert.That(restored.completedCycles, Is.Zero);
            Assert.That(restored.elapsedSeconds, Is.Zero);
            Assert.That(restored.currentCycleFatiguePaid, Is.True);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigueBefore));
            Assert.That(state.GetHeroEffectCounter("ren", "test_reliable_hands_effect"), Is.Zero);
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
        }

        [Test]
        public void AdvanceWorkIgnoresFullStorageAndKeepsRewardsInActivityBag()
        {
            var state = NewState();
            var fill = state.Storage.Add(
                "fill-storage",
                state.Storage.GetSnapshot().Revision,
                "resource_pine_wood",
                2000);
            Assert.That(fill.Success, Is.True);
            Assert.That(state.Storage.GetSnapshot().FreeSlots, Is.Zero);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start(WorkStart("work_pine_wood", "ren", 1));
            var processor = NewWorkAdvanceProcessor(state);

            var result = processor.Advance(new WorkAdvanceRequest(started.executionId, 10));
            var bag = state.PendingResults.GetAll()[0];

            Assert.That(result.Success, Is.True);
            Assert.That(result.PlanCompleted, Is.True);
            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(2000));
            Assert.That(bag.entries, Has.Length.EqualTo(2));
            Assert.That(bag.entries[0].rewardType, Is.EqualTo("Resource"));
            Assert.That(bag.entries[0].quantity, Is.EqualTo(1));
        }

        [Test]
        public void AdvanceWorkEmptyRewardPlanCompletesWithoutManualClaim()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start(WorkStart("empty_repeat", "ren", 1));
            var processor = NewWorkAdvanceProcessor(state);

            var result = processor.Advance(new WorkAdvanceRequest(started.executionId, 10));

            Assert.That(result.Success, Is.True);
            Assert.That(result.StopReason, Is.EqualTo(WorkAdvanceStopReason.PlanCompleted));
            Assert.That(result.PlanCompleted, Is.True);
            Assert.That(result.ExecutionStatus, Is.EqualTo(ActivityRuntimeStatus.Completed));
            Assert.That(state.GetActivityExecution(started.executionId), Is.Null);
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(result.DeferredResolvedEvents, Has.Count.EqualTo(1));
        }

        [Test]
        public void AdvanceWorkValidationFailureLeavesCycleStateUnchanged()
        {
            var state = NewState();
            var random = new CountingRandom();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), random);
            var started = runtime.Start(WorkStart("bad_reward_range_work", "ren", 1));
            var processor = NewWorkAdvanceProcessor(state, random);
            var checkpoint = state.GetActivityExecution(started.executionId);

            var result = processor.Advance(new WorkAdvanceRequest(started.executionId, 10));
            var execution = state.GetActivityExecution(started.executionId);

            Assert.That(result.Success, Is.False);
            Assert.That(result.StopReason, Is.EqualTo(WorkAdvanceStopReason.ValidationFailed));
            Assert.That(execution.completedCycles, Is.EqualTo(checkpoint.completedCycles));
            Assert.That(execution.elapsedSeconds, Is.EqualTo(checkpoint.elapsedSeconds));
            Assert.That(execution.cyclePhase, Is.EqualTo(checkpoint.cyclePhase));
            Assert.That(execution.stagedRewards, Is.Empty);
            Assert.That(random.RangeCalls, Is.Zero);
            Assert.That(random.PercentCalls, Is.Zero);
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
        }

        [Test]
        public void WorkAdvanceProcessorCreationDoesNotReconcileOrSave()
        {
            var storage = new MemorySaveStorage();
            var state = NewState();
            state.UnlockBuilding("building_hall");
            state.SetBuildingLevel("building_hall", 0);
            Assert.That(SaveService.Save(state, storage), Is.True);
            state = SaveService.Load(_factory, storage);
            var progression = new RecordingProgressionProcessor { FailBuildingLevelChanged = true };
            var runtime = new ActivityRuntimeService(
                state,
                new PlayerStateActivityAdapter(state),
                progressionProcessor: progression);
            var started = runtime.Start(new ActivityStartRequest { activityId = "test_build_empty", heroId = "ren" });
            Assert.That(runtime.Tick(1f).success, Is.False);
            runtime.Dispose();
            var before = JsonUtility.ToJson(state.ToSaveData());
            var saveCalls = storage.SaveCalls;

            using var processor = NewWorkAdvanceProcessor(state);

            Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(before));
            Assert.That(storage.SaveCalls, Is.EqualTo(saveCalls));
            Assert.That(state.GetActivityExecution(started.executionId).buildingEventPending, Is.True);
            Assert.That(progression.BuildingLevelChangedCount, Is.Zero);
        }

        [Test]
        public void TransactionalEmptyResultDefersResolvedUntilCommitAndPublishesOnce()
        {
            var state = NewState();
            var activityState = new PlayerStateActivityAdapter(state);
            var runtime = new ActivityRuntimeService(state, activityState);
            var started = runtime.Start(WorkStart("empty_repeat", "ren", 1));
            var random = new SystemActivityRandom(77);
            var processor = NewWorkAdvanceProcessor(state, random);
            var checkpoint = activityState.CaptureCheckpoint();
            var randomCheckpoint = processor.CaptureRandomState();
            var resolvedEvents = 0;
            state.PendingResults.Resolved += _ => resolvedEvents++;

            var rolledBack = processor.Advance(new WorkAdvanceRequest(started.executionId, 10));

            Assert.That(rolledBack.Success, Is.True);
            Assert.That(rolledBack.DeferredResolvedEvents, Has.Count.EqualTo(1));
            Assert.That(resolvedEvents, Is.Zero);
            activityState.RestoreCheckpoint(checkpoint);
            processor.RestoreRandomState(randomCheckpoint);

            var committed = processor.Advance(new WorkAdvanceRequest(started.executionId, 10));
            var storage = new MemorySaveStorage();
            Assert.That(SaveService.Save(state, storage), Is.True);

            Assert.That(committed.DeferredResolvedEvents, Has.Count.EqualTo(1));
            Assert.That(resolvedEvents, Is.Zero);
            processor.PublishDeferredResolvedEvents(committed);
            processor.PublishDeferredResolvedEvents(committed);
            Assert.That(resolvedEvents, Is.EqualTo(1));
        }

        [Test]
        public void AdvanceWorkStateAndRandomRollbackRetryProduceIdenticalOutcome()
        {
            var state = NewState();
            var activityState = new PlayerStateActivityAdapter(state);
            var runtime = new ActivityRuntimeService(state, activityState);
            var started = runtime.Start(WorkStart("hunt_rabbits", "ren", 3));
            var random = new SystemActivityRandom(91234);
            var processor = NewWorkAdvanceProcessor(state, random);
            var checkpoint = activityState.CaptureCheckpoint();
            var randomCheckpoint = processor.CaptureRandomState();

            var first = processor.Advance(new WorkAdvanceRequest(started.executionId, 20));
            var firstExecution = state.GetActivityExecution(started.executionId);
            var firstBag = state.PendingResults.GetAll();
            var firstCounter = state.GetHeroEffectCounter("ren", "test_reliable_hands_effect");
            var firstRandomAfter = processor.CaptureRandomState();

            activityState.RestoreCheckpoint(checkpoint);
            processor.RestoreRandomState(randomCheckpoint);

            var retry = processor.Advance(new WorkAdvanceRequest(started.executionId, 20));
            var retryExecution = state.GetActivityExecution(started.executionId);
            var retryBag = state.PendingResults.GetAll();

            Assert.That(retry.StopReason, Is.EqualTo(first.StopReason));
            Assert.That(retry.ProcessedCycles, Is.EqualTo(first.ProcessedCycles));
            Assert.That(retry.RemainingSeconds, Is.EqualTo(first.RemainingSeconds));
            Assert.That(retryExecution.completedCycles, Is.EqualTo(firstExecution.completedCycles));
            Assert.That(retryExecution.dangerRollCompleted, Is.EqualTo(firstExecution.dangerRollCompleted));
            Assert.That(retryExecution.dangerRiskPercent, Is.EqualTo(firstExecution.dangerRiskPercent));
            Assert.That(retryExecution.dangerRoll, Is.EqualTo(firstExecution.dangerRoll));
            Assert.That(state.GetHeroEffectCounter("ren", "test_reliable_hands_effect"), Is.EqualTo(firstCounter));
            Assert.That(retryBag, Has.Length.EqualTo(firstBag.Length));
            for (var resultIndex = 0; resultIndex < firstBag.Length; resultIndex++)
            {
                Assert.That(retryBag[resultIndex].entries, Has.Length.EqualTo(firstBag[resultIndex].entries.Length));
                for (var entryIndex = 0; entryIndex < firstBag[resultIndex].entries.Length; entryIndex++)
                    Assert.That(retryBag[resultIndex].entries[entryIndex].quantity,
                        Is.EqualTo(firstBag[resultIndex].entries[entryIndex].quantity));
            }
            Assert.That(processor.CaptureRandomState().Value, Is.EqualTo(firstRandomAfter.Value));
        }

        [Test]
        public void AdvanceWorkOuterRollbackRestoresRandomAfterLaterPendingResultFailure()
        {
            var state = NewState();
            var activityState = new PlayerStateActivityAdapter(state);
            var runtime = new ActivityRuntimeService(state, activityState);
            var started = runtime.Start(WorkStart("random_reward_work", "ren", 1));
            var random = new SystemActivityRandom(4567);
            var processor = NewWorkAdvanceProcessor(state, random);
            var checkpoint = activityState.CaptureCheckpoint();
            var randomBefore = processor.CaptureRandomState();
            activityState.RecordOperationReceipt(new OperationReceiptSaveData
            {
                aggregateId = $"result:{PendingResultSourceType.Activity}:{started.executionId}",
                operationId = $"activity:{started.executionId}:cycle:1",
                fingerprint = "conflicting-test-payload",
                success = true,
                code = "Formed"
            });

            var failed = processor.Advance(new WorkAdvanceRequest(started.executionId, 10));
            var randomAfterFailedAttempt = processor.CaptureRandomState();

            Assert.That(failed.Success, Is.False);
            Assert.That(failed.StopReason, Is.EqualTo(WorkAdvanceStopReason.RuntimeError));
            Assert.That(randomAfterFailedAttempt.Value, Is.Not.EqualTo(randomBefore.Value));
            activityState.RestoreCheckpoint(checkpoint);
            processor.RestoreRandomState(randomBefore);
            Assert.That(processor.CaptureRandomState().Value, Is.EqualTo(randomBefore.Value));

            var retry = processor.Advance(new WorkAdvanceRequest(started.executionId, 10));

            Assert.That(retry.Success, Is.True);
            Assert.That(retry.StopReason, Is.EqualTo(WorkAdvanceStopReason.PlanCompleted));
            Assert.That(processor.CaptureRandomState().Value, Is.EqualTo(randomAfterFailedAttempt.Value));
            Assert.That(state.PendingResults.GetAll(), Has.Length.EqualTo(1));
        }

        [Test]
        public void AdvanceWorkFatigueStopWithEmptyResultResolvesAndFreesHero()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start(WorkStart("empty_repeat", "ren", 3));
            var processor = NewWorkAdvanceProcessor(state);
            Assert.That(state.SpendHeroFatigue("ren", state.GetHeroFatigue("ren")), Is.True);

            var result = processor.Advance(new WorkAdvanceRequest(started.executionId, 30));

            Assert.That(result.Success, Is.True);
            Assert.That(result.StopReason, Is.EqualTo(WorkAdvanceStopReason.InsufficientFatigue));
            Assert.That(result.ProcessedCycles, Is.EqualTo(1));
            Assert.That(result.ConsumedSeconds, Is.EqualTo(10));
            Assert.That(result.RemainingSeconds, Is.EqualTo(20));
            Assert.That(result.ExecutionStatus, Is.EqualTo(ActivityRuntimeStatus.Completed));
            Assert.That(result.DeferredResolvedEvents, Has.Count.EqualTo(1));
            Assert.That(state.GetActivityExecution(started.executionId), Is.Null);
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
        }

        [Test]
        public void DangerHandoffMovesOnlyCycleLootAndKeepsRootOccupation()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), new DangerSequenceRandom(100, 1));
            var started = runtime.Start(WorkStart("hunt_rabbits", "ren", 3));

            var ticked = runtime.Tick(20f);
            var storage = new MemorySaveStorage();
            Assert.That(SaveService.Save(state, storage), Is.True);
            state = SaveService.Load(_factory, storage);
            runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), new FixedRandom(100));
            var execution = state.GetActivityExecution(started.executionId);
            var handoff = runtime.GetPendingLinkedCombatStarts()[0];
            var bag = state.PendingResults.GetAll()[0];
            long bagResources = 0;
            long bagSkillExp = 0;
            foreach (var entry in bag.entries)
            {
                if (entry.rewardType == "Resource") bagResources += entry.quantity;
                if (entry.rewardType == "SkillExp") bagSkillExp += entry.quantity;
            }

            Assert.That(ticked.success, Is.True);
            Assert.That(execution.endReason, Is.EqualTo("DangerTriggered"));
            Assert.That(execution.completedCycles, Is.EqualTo(2));
            Assert.That(handoff.rootExecutionId, Is.EqualTo(started.executionId));
            Assert.That(handoff.occupationOwnerId, Is.EqualTo(started.executionId));
            Assert.That(handoff.enemyExpTargetId,
                Is.EqualTo("skill_hunting"));
            Assert.That(handoff.suppressFatigueCost, Is.True);
            Assert.That(handoff.loot, Has.Length.EqualTo(1));
            Assert.That(handoff.loot[0].origin, Is.EqualTo(PendingResultOrigin.ActivityLootInCombat));
            Assert.That(bagResources, Is.EqualTo(1), "Previous-cycle loot must remain in the Activity Bag.");
            Assert.That(bagSkillExp, Is.EqualTo(4), "Skill EXP from both cycles must stay outside combat loss.");

            var claimed = state.PendingResults.ClaimAll("danger-skill-claim", bag.resultId, bag.revision, state.Storage.GetSnapshot().Revision);
            Assert.That(claimed.Success, Is.True);
            Assert.That(state.IsHeroBusy("ren"), Is.True);
            Assert.That(state.GetActivityExecution(started.executionId), Is.Not.Null);

            var progression = new RecordingProgressionProcessor();
            runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), new FixedRandom(100), progressionProcessor: progression);
            var beforeBind = runtime.ResolveLinkedCombatExecution(handoff.requestId, "combat-child");
            Assert.That(beforeBind.success, Is.False);
            Assert.That(beforeBind.code, Is.EqualTo("CombatNotBound"));
            Assert.That(runtime.BindLinkedCombatExecution(handoff.requestId, "combat-child").success, Is.True);
            var mismatch = runtime.ResolveLinkedCombatExecution(handoff.requestId, "other-combat");
            Assert.That(mismatch.success, Is.False);
            Assert.That(mismatch.code, Is.EqualTo("CombatExecutionMismatch"));
            var resolved = runtime.ResolveLinkedCombatExecution(handoff.requestId, "combat-child");
            Assert.That(resolved.success, Is.True);
            Assert.That(resolved.completedActivityId, Is.EqualTo("hunt_rabbits"));
            Assert.That(progression.ActivityCompletedCount, Is.EqualTo(1));
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.GetActivityExecution(started.executionId), Is.Null);
            state = SaveService.Load(_factory, storage);
            runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), progressionProcessor: progression);
            var replay = runtime.ResolveLinkedCombatExecution(handoff.requestId, "combat-child");
            Assert.That(replay.success, Is.True);
            Assert.That(replay.replayed, Is.True);
            Assert.That(progression.ActivityCompletedCount, Is.EqualTo(1));
        }

        [Test]
        public void LinkedCombatCompletionSupportsCombatBeforeActivityBag()
        {
            var state = NewState();
            var progression = new RecordingProgressionProcessor();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), new DangerSequenceRandom(100, 1), progressionProcessor: progression);
            var started = runtime.Start(WorkStart("hunt_rabbits", "ren", 3));
            Assert.That(runtime.Tick(20f).success, Is.True);
            var handoff = runtime.GetPendingLinkedCombatStarts()[0];
            var bag = state.PendingResults.GetAll()[0];
            Assert.That(runtime.BindLinkedCombatExecution(handoff.requestId, "combat-child").success, Is.True);

            var combatResolved = runtime.ResolveLinkedCombatExecution(handoff.requestId, "combat-child");
            Assert.That(combatResolved.success, Is.True);
            Assert.That(combatResolved.completedActivityId, Is.Null);
            Assert.That(progression.ActivityCompletedCount, Is.Zero);
            Assert.That(state.GetActivityExecution(started.executionId), Is.Not.Null);

            var claimed = state.PendingResults.ClaimAll("bag-after-combat", bag.resultId, bag.revision, state.Storage.GetSnapshot().Revision);
            Assert.That(claimed.Success, Is.True);
            Assert.That(claimed.Resolved, Is.True);

            Assert.That(progression.ActivityCompletedCount, Is.EqualTo(1));
            Assert.That(state.GetActivityExecution(started.executionId), Is.Null);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            var replay = runtime.ResolveLinkedCombatExecution(handoff.requestId, "combat-child");
            Assert.That(replay.success, Is.True);
            Assert.That(replay.replayed, Is.True);
            Assert.That(progression.ActivityCompletedCount, Is.EqualTo(1));
        }

        [Test]
        public void ReplacingRuntimeWhileWaitingForActivityBagLeavesOnlyCurrentHandler()
        {
            var state = NewState();
            var replacedProgression = new RecordingProgressionProcessor();
            var replaced = new ActivityRuntimeService(
                state,
                new PlayerStateActivityAdapter(state),
                new DangerSequenceRandom(100, 1),
                progressionProcessor: replacedProgression);
            var started = replaced.Start(WorkStart("hunt_rabbits", "ren", 3));
            Assert.That(replaced.Tick(20f).success, Is.True);
            var handoff = replaced.GetPendingLinkedCombatStarts()[0];
            var bag = state.PendingResults.GetAll()[0];
            Assert.That(replaced.BindLinkedCombatExecution(handoff.requestId, "combat-child").success, Is.True);
            Assert.That(replaced.ResolveLinkedCombatExecution(handoff.requestId, "combat-child").success, Is.True);

            replaced.Dispose();
            var currentProgression = new RecordingProgressionProcessor();
            var current = new ActivityRuntimeService(
                state,
                new PlayerStateActivityAdapter(state),
                progressionProcessor: currentProgression);

            var claimed = state.PendingResults.ClaimAll(
                "bag-after-runtime-replacement",
                bag.resultId,
                bag.revision,
                state.Storage.GetSnapshot().Revision);

            Assert.That(claimed.Success, Is.True);
            Assert.That(claimed.Resolved, Is.True);
            Assert.That(replacedProgression.ActivityCompletedCount, Is.Zero);
            Assert.That(currentProgression.ActivityCompletedCount, Is.EqualTo(1));
            Assert.That(state.GetActivityExecution(started.executionId), Is.Null);
            current.Dispose();
        }

        [Test]
        public void ReplacingRuntimeWhileWaitingForCombatCompletionIsSafe()
        {
            var state = NewState();
            var replacedProgression = new RecordingProgressionProcessor();
            var replaced = new ActivityRuntimeService(
                state,
                new PlayerStateActivityAdapter(state),
                new DangerSequenceRandom(100, 1),
                progressionProcessor: replacedProgression);
            var started = replaced.Start(WorkStart("hunt_rabbits", "ren", 3));
            Assert.That(replaced.Tick(20f).success, Is.True);
            var handoff = replaced.GetPendingLinkedCombatStarts()[0];
            var bag = state.PendingResults.GetAll()[0];
            Assert.That(replaced.BindLinkedCombatExecution(handoff.requestId, "combat-child").success, Is.True);
            Assert.That(state.PendingResults.ClaimAll(
                "bag-before-runtime-replacement",
                bag.resultId,
                bag.revision,
                state.Storage.GetSnapshot().Revision).Success, Is.True);

            replaced.Dispose();
            var currentProgression = new RecordingProgressionProcessor();
            var current = new ActivityRuntimeService(
                state,
                new PlayerStateActivityAdapter(state),
                progressionProcessor: currentProgression);
            var resolved = current.ResolveLinkedCombatExecution(handoff.requestId, "combat-child");

            Assert.That(resolved.success, Is.True);
            Assert.That(replacedProgression.ActivityCompletedCount, Is.Zero);
            Assert.That(currentProgression.ActivityCompletedCount, Is.EqualTo(1));
            Assert.That(state.GetActivityExecution(started.executionId), Is.Null);
            current.Dispose();
        }

        [Test]
        public void DisposeWithoutLinkedCombatIsIdempotentAndReplacementKeepsWorkBehavior()
        {
            var state = NewState();
            var replaced = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));

            replaced.Dispose();
            Assert.DoesNotThrow(replaced.Dispose);

            var current = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = current.Start(WorkStart("work_pine_wood", "ren", 1));

            Assert.That(started.success, Is.True);
            Assert.That(current.Tick(10f).success, Is.True);
            Assert.That(state.GetActivityExecution(started.executionId).status, Is.EqualTo(ActivityRuntimeStatus.ResultPending));
            current.Dispose();
        }

        [Test]
        public void LinkedCombatCompletionProcessorFailureIsRetryable()
        {
            var state = NewState();
            var progression = new RecordingProgressionProcessor { FailActivityCompleted = true };
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), new DangerSequenceRandom(100, 1), progressionProcessor: progression);
            var started = runtime.Start(WorkStart("hunt_rabbits", "ren", 3));
            Assert.That(runtime.Tick(20f).success, Is.True);
            var handoff = runtime.GetPendingLinkedCombatStarts()[0];
            var bag = state.PendingResults.GetAll()[0];
            Assert.That(runtime.BindLinkedCombatExecution(handoff.requestId, "combat-child").success, Is.True);
            Assert.That(state.PendingResults.ClaimAll("bag-before-failed-combat", bag.resultId, bag.revision, state.Storage.GetSnapshot().Revision).Success, Is.True);

            var failed = runtime.ResolveLinkedCombatExecution(handoff.requestId, "combat-child");
            var executionAfterFailure = state.GetActivityExecution(started.executionId);

            Assert.That(failed.success, Is.False);
            Assert.That(failed.code, Is.EqualTo("TestActivityFailed"));
            Assert.That(executionAfterFailure, Is.Not.Null);
            Assert.That(executionAfterFailure.activityBagResolved, Is.True);
            Assert.That(executionAfterFailure.linkedCombat.resolved, Is.False);
            Assert.That(HasLinkedCombatReceipt(state, handoff.requestId), Is.False);
            progression.FailActivityCompleted = false;
            var retried = runtime.ResolveLinkedCombatExecution(handoff.requestId, "combat-child");
            Assert.That(retried.success, Is.True);
            Assert.That(state.GetActivityExecution(started.executionId), Is.Null);
            Assert.That(progression.ActivityCompletedCount, Is.EqualTo(1));
        }

        [Test]
        public void LinkedCombatCompletionSaveFailureRollsBackExecutionRemovalAndReceipt()
        {
            var storage = new MemorySaveStorage();
            var state = NewState();
            Assert.That(SaveService.Save(state, storage), Is.True);
            state = SaveService.Load(_factory, storage);
            var progression = new RecordingProgressionProcessor();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), new DangerSequenceRandom(100, 1), progressionProcessor: progression);
            var started = runtime.Start(WorkStart("hunt_rabbits", "ren", 3));
            Assert.That(runtime.Tick(20f).success, Is.True);
            var handoff = runtime.GetPendingLinkedCombatStarts()[0];
            var bag = state.PendingResults.GetAll()[0];
            Assert.That(runtime.BindLinkedCombatExecution(handoff.requestId, "combat-child").success, Is.True);
            Assert.That(state.PendingResults.ClaimAll("bag-before-save-failure", bag.resultId, bag.revision, state.Storage.GetSnapshot().Revision).Success, Is.True);
            storage.ThrowOnSaveCall = storage.SaveCalls + 1;
            LogAssert.Expect(LogType.Error, "[SaveService] Failed to save player state. simulated save failure");

            var failed = runtime.ResolveLinkedCombatExecution(handoff.requestId, "combat-child");
            var executionAfterFailure = state.GetActivityExecution(started.executionId);

            Assert.That(failed.success, Is.False);
            Assert.That(failed.code, Is.EqualTo("SaveFailed"));
            Assert.That(executionAfterFailure, Is.Not.Null);
            Assert.That(executionAfterFailure.activityBagResolved, Is.True);
            Assert.That(executionAfterFailure.linkedCombat.resolved, Is.False);
            Assert.That(state.IsActivityCompleted("hunt_rabbits"), Is.False);
            Assert.That(HasLinkedCombatReceipt(state, handoff.requestId), Is.False);
        }

        [Test]
        public void LinkedCombatCompletionActivityBagBeforeCombatPublishesCoordinatedDiagnosticEvent()
        {
            var state = NewState();
            var progression = new RecordingProgressionProcessor();
            var delivered = new List<ActivityRuntimeEvent>();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), new DangerSequenceRandom(100, 1), eventSink: delivered.Add, progressionProcessor: progression);
            Assert.That(runtime.Start(WorkStart("hunt_rabbits", "ren", 3)).success, Is.True);
            Assert.That(runtime.Tick(20f).success, Is.True);
            var handoff = runtime.GetPendingLinkedCombatStarts()[0];
            var bag = state.PendingResults.GetAll()[0];
            Assert.That(runtime.BindLinkedCombatExecution(handoff.requestId, "combat-child").success, Is.True);
            Assert.That(state.PendingResults.ClaimAll("bag-before-combat-event", bag.resultId, bag.revision, state.Storage.GetSnapshot().Revision).Success, Is.True);

            var resolved = runtime.ResolveLinkedCombatExecution(handoff.requestId, "combat-child");
            var replay = runtime.ResolveLinkedCombatExecution(handoff.requestId, "combat-child");

            Assert.That(resolved.success, Is.True);
            Assert.That(delivered, Has.Count.EqualTo(1));
            Assert.That(delivered[0].eventType, Is.EqualTo(ActivityRuntimeEventType.ActivityCompleted));
            Assert.That(delivered[0].progressionAlreadyProcessed, Is.True);
            Assert.That(replay.success, Is.True);
            Assert.That(replay.replayed, Is.True);
            Assert.That(progression.ActivityCompletedCount, Is.EqualTo(1));
        }

        [Test]
        public void ConstructionStartFailureIsAtomicForTestOnlyBuildAction()
        {
            var state = NewState();
            state.UnlockBuilding("building_campfire");
            state.SetBuildingLevel("building_campfire", 0);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), progressionProcessor: new RecordingProgressionProcessor());
            var fatigue = state.GetHeroFatigue("ren");

            var started = runtime.Start(new ActivityStartRequest { activityId = "test_build_campfire", heroId = "ren" });

            Assert.That(started.success, Is.False);
            Assert.That(HasIssue(started.issues, "BuildMaterials"), Is.True);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(state.GetActivityExecutions(), Is.Empty);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
        }

        [Test]
        public void ConstructionPaysMaterialsOnceAndResumePaysOnlyAssignmentFatigue()
        {
            var state = NewState();
            Assert.That(state.AddHero("test_builder_hero"), Is.True);
            state.UnlockBuilding("building_campfire");
            state.SetBuildingLevel("building_campfire", 0);
            Assert.That(state.Storage.Add("add-build-wood", state.Storage.GetSnapshot().Revision, "resource_pine_wood", 2).Success, Is.True);
            Assert.That(state.Storage.Add("add-build-stone", state.Storage.GetSnapshot().Revision, "resource_stone", 2).Success, Is.True);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var initialFatigue = state.GetHeroFatigue("ren");
            var replacementFatigue = state.GetHeroFatigue("test_builder_hero");

            var started = runtime.Start(new ActivityStartRequest { activityId = "test_build_campfire", heroId = "ren" });
            Assert.That(started.success, Is.True);
            Assert.That(state.GetItem("resource_pine_wood"), Is.Zero);
            Assert.That(state.GetItem("resource_stone"), Is.Zero);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(initialFatigue - 3));
            var runningFatigue = state.GetHeroFatigue("test_builder_hero");
            var duplicateRunning = runtime.Start(new ActivityStartRequest { activityId = "test_build_campfire", heroId = "test_builder_hero" });
            Assert.That(duplicateRunning.success, Is.False);
            Assert.That(HasIssue(duplicateRunning.issues, "ConstructionAlreadyRunning"), Is.True);
            Assert.That(state.GetHeroFatigue("test_builder_hero"), Is.EqualTo(runningFatigue));

            runtime.Tick(1f);
            var beforePause = state.GetActivityExecution(started.executionId).accumulatedBuildPoints;
            Assert.That(runtime.PauseConstruction(started.executionId).success, Is.True);
            Assert.That(runtime.Cancel(started.executionId).success, Is.True);
            var pausedFatigue = state.GetHeroFatigue("test_builder_hero");
            var duplicateStart = runtime.Start(new ActivityStartRequest { activityId = "test_build_campfire", heroId = "test_builder_hero" });
            Assert.That(duplicateStart.success, Is.False);
            Assert.That(HasIssue(duplicateStart.issues, "ConstructionResumeRequired"), Is.True);
            Assert.That(state.GetActivityExecutions(), Has.Length.EqualTo(1));
            Assert.That(state.GetHeroFatigue("test_builder_hero"), Is.EqualTo(pausedFatigue));
            Assert.That(state.GetItem("resource_pine_wood"), Is.Zero);
            Assert.That(state.GetItem("resource_stone"), Is.Zero);
            var saveStorage = new MemorySaveStorage();
            Assert.That(SaveService.Save(state, saveStorage), Is.True);
            state = SaveService.Load(_factory, saveStorage);
            runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), progressionProcessor: new RecordingProgressionProcessor());
            runtime.Tick(100f);
            Assert.That(state.GetActivityExecution(started.executionId).accumulatedBuildPoints, Is.EqualTo(beforePause));
            Assert.That(state.GetActivityExecution(started.executionId).status, Is.EqualTo(GuildIdle.Core.ActivityRuntimeStatus.Paused));
            Assert.That(state.GetActivityExecution(started.executionId).materialsPaid, Is.True);
            Assert.That(state.IsHeroBusy("ren"), Is.False);

            Assert.That(runtime.ResumeConstruction(started.executionId, "test_builder_hero").success, Is.True);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(initialFatigue - 3));
            Assert.That(state.GetHeroFatigue("test_builder_hero"), Is.EqualTo(replacementFatigue - 3));
            var completed = runtime.Tick(1f);
            Assert.That(completed.events, Has.Length.EqualTo(1));
            Assert.That(completed.events[0].eventType, Is.EqualTo(ActivityRuntimeEventType.BuildingLevelChanged));
            Assert.That(state.GetBuildingLevel("building_campfire"), Is.EqualTo(1));
            Assert.That(state.GetActivityExecution(started.executionId).status, Is.EqualTo(GuildIdle.Core.ActivityRuntimeStatus.ResultPending));
            Assert.That(state.GetActivityExecution(started.executionId).linkedCombat, Is.Null);
            var duplicatePending = runtime.Start(new ActivityStartRequest { activityId = "test_build_campfire", heroId = "ren" });
            Assert.That(duplicatePending.success, Is.False);
            Assert.That(HasIssue(duplicatePending.issues, "ConstructionResultPending"), Is.True);

            var result = state.PendingResults.GetAll()[0];
            var claimed = state.PendingResults.ClaimAll("claim-build", result.resultId, result.revision, state.Storage.GetSnapshot().Revision);
            Assert.That(claimed.Success, Is.True);
            Assert.That(claimed.Resolved, Is.True);
            Assert.That(state.GetHeroSkillExp("test_builder_hero", "skill_construction"), Is.EqualTo(4));
            Assert.That(state.GetActivityExecution(started.executionId), Is.Null);
            Assert.That(state.IsHeroBusy("test_builder_hero"), Is.False);
        }

        [Test]
        public void ConstructionAdvanceKeepsFractionalProgressAndDoesNotSaveOrRepayCosts()
        {
            var storage = new MemorySaveStorage();
            var state = NewState();
            state.UnlockBuilding("building_campfire");
            state.SetBuildingLevel("building_campfire", 0);
            Assert.That(state.Storage.Add("offline-build-wood", state.Storage.GetSnapshot().Revision,
                "resource_pine_wood", 2).Success, Is.True);
            Assert.That(state.Storage.Add("offline-build-stone", state.Storage.GetSnapshot().Revision,
                "resource_stone", 2).Success, Is.True);
            Assert.That(SaveService.Save(state, storage), Is.True);
            state = SaveService.Load(_factory, storage);
            var runtime = new ActivityRuntimeService(
                state,
                new PlayerStateActivityAdapter(state),
                progressionProcessor: new RecordingProgressionProcessor());
            var fatigueBeforeStart = state.GetHeroFatigue("ren");
            var started = runtime.Start(new ActivityStartRequest
                { activityId = "test_build_campfire", heroId = "ren" });
            Assert.That(runtime.Tick(0.5f).success, Is.True);
            var saveCallsBeforeAdvance = storage.SaveCalls;
            var fatigueAfterStart = state.GetHeroFatigue("ren");

            using var processor = NewConstructionAdvanceProcessor(state);
            var advanced = processor.Advance(new ConstructionAdvanceRequest(started.executionId, 1));
            var execution = state.GetActivityExecution(started.executionId);

            Assert.That(advanced.Success, Is.True);
            Assert.That(advanced.StopReason, Is.EqualTo(ConstructionAdvanceStopReason.IntervalExhausted));
            Assert.That(advanced.ConsumedSeconds, Is.EqualTo(1));
            Assert.That(advanced.RemainingSeconds, Is.Zero);
            Assert.That(advanced.AddedBuildPoints, Is.EqualTo(1f));
            Assert.That(execution.accumulatedBuildPoints, Is.EqualTo(1f));
            Assert.That(execution.elapsedSeconds, Is.EqualTo(0.5f));
            Assert.That(storage.SaveCalls, Is.EqualTo(saveCallsBeforeAdvance));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigueAfterStart));
            Assert.That(fatigueAfterStart, Is.EqualTo(fatigueBeforeStart - 3));
            Assert.That(state.GetItem("resource_pine_wood"), Is.Zero);
            Assert.That(state.GetItem("resource_stone"), Is.Zero);
        }

        [Test]
        public void ConstructionAdvanceMatchesSequentialOnlineFormulaAndRounding()
        {
            var onlineState = NewState();
            onlineState.UnlockBuilding("building_campfire");
            onlineState.SetBuildingLevel("building_campfire", 0);
            Assert.That(onlineState.Storage.Add("online-parity-wood",
                onlineState.Storage.GetSnapshot().Revision, "resource_pine_wood", 2).Success, Is.True);
            Assert.That(onlineState.Storage.Add("online-parity-stone",
                onlineState.Storage.GetSnapshot().Revision, "resource_stone", 2).Success, Is.True);
            var onlineRuntime = new ActivityRuntimeService(onlineState, new PlayerStateActivityAdapter(onlineState));
            var onlineStart = onlineRuntime.Start(new ActivityStartRequest
                { activityId = "test_build_campfire", heroId = "ren" });

            var offlineState = NewState();
            offlineState.UnlockBuilding("building_campfire");
            offlineState.SetBuildingLevel("building_campfire", 0);
            Assert.That(offlineState.Storage.Add("offline-parity-wood",
                offlineState.Storage.GetSnapshot().Revision, "resource_pine_wood", 2).Success, Is.True);
            Assert.That(offlineState.Storage.Add("offline-parity-stone",
                offlineState.Storage.GetSnapshot().Revision, "resource_stone", 2).Success, Is.True);
            var offlineRuntime = new ActivityRuntimeService(offlineState,
                new PlayerStateActivityAdapter(offlineState));
            var offlineStart = offlineRuntime.Start(new ActivityStartRequest
                { activityId = "test_build_campfire", heroId = "ren" });
            using var processor = NewConstructionAdvanceProcessor(offlineState);

            Assert.That(onlineRuntime.Tick(0.25f).success, Is.True);
            Assert.That(onlineRuntime.Tick(0.75f).success, Is.True);
            var advanced = processor.Advance(new ConstructionAdvanceRequest(offlineStart.executionId, 1));
            var onlineExecution = onlineState.GetActivityExecution(onlineStart.executionId);
            var offlineExecution = offlineState.GetActivityExecution(offlineStart.executionId);

            Assert.That(advanced.Success, Is.True);
            Assert.That(offlineExecution.accumulatedBuildPoints,
                Is.EqualTo(onlineExecution.accumulatedBuildPoints));
            Assert.That(offlineExecution.elapsedSeconds, Is.EqualTo(onlineExecution.elapsedSeconds));
            Assert.That(offlineExecution.status, Is.EqualTo(onlineExecution.status));
        }

        [Test]
        public void ConstructionAdvanceCompletesIntoPendingResultAndDefersEventUntilOuterCommit()
        {
            var storage = new MemorySaveStorage();
            var state = NewState();
            state.UnlockBuilding("building_campfire");
            state.SetBuildingLevel("building_campfire", 0);
            Assert.That(state.Storage.Add("complete-build-wood", state.Storage.GetSnapshot().Revision,
                "resource_pine_wood", 2).Success, Is.True);
            Assert.That(state.Storage.Add("complete-build-stone", state.Storage.GetSnapshot().Revision,
                "resource_stone", 2).Success, Is.True);
            Assert.That(SaveService.Save(state, storage), Is.True);
            state = SaveService.Load(_factory, storage);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start(new ActivityStartRequest
                { activityId = "test_build_campfire", heroId = "ren" });
            var saveCallsBeforeAdvance = storage.SaveCalls;
            var delivered = new List<ActivityRuntimeEvent>();
            var progression = new RecordingProgressionProcessor();
            using var processor = NewConstructionAdvanceProcessor(state, progression, delivered.Add);

            var advanced = processor.Advance(new ConstructionAdvanceRequest(started.executionId, 10));
            var execution = state.GetActivityExecution(started.executionId);

            Assert.That(advanced.Success, Is.True);
            Assert.That(advanced.StopReason, Is.EqualTo(ConstructionAdvanceStopReason.ConstructionCompleted));
            Assert.That(advanced.Completed, Is.True);
            Assert.That(advanced.ConsumedSeconds, Is.EqualTo(2));
            Assert.That(advanced.RemainingSeconds, Is.EqualTo(8));
            Assert.That(advanced.AddedBuildPoints, Is.EqualTo(2f));
            Assert.That(advanced.ExecutionStatus, Is.EqualTo(GuildIdle.Core.ActivityRuntimeStatus.ResultPending));
            Assert.That(execution.accumulatedBuildPoints, Is.EqualTo(2f));
            Assert.That(execution.status, Is.EqualTo(GuildIdle.Core.ActivityRuntimeStatus.ResultPending));
            Assert.That(execution.buildingLevelApplied, Is.True);
            Assert.That(execution.buildingEventPublished, Is.True);
            Assert.That(state.GetBuildingLevel("building_campfire"), Is.EqualTo(1));
            Assert.That(state.PendingResults.GetAll(), Has.Length.EqualTo(1));
            Assert.That(progression.BuildingLevelChangedCount, Is.EqualTo(1));
            Assert.That(advanced.DeferredEvents, Has.Count.EqualTo(1));
            Assert.That(delivered, Is.Empty);
            Assert.That(storage.SaveCalls, Is.EqualTo(saveCallsBeforeAdvance));

            Assert.That(SaveService.Save(state, storage), Is.True);
            processor.PublishDeferredEvents(advanced);
            processor.PublishDeferredEvents(advanced);
            Assert.That(delivered, Has.Count.EqualTo(1));
            Assert.That(delivered[0].eventType, Is.EqualTo(ActivityRuntimeEventType.BuildingLevelChanged));
        }

        [Test]
        public void ConstructionAdvanceEmptyCompletionUsesImmediatePathAndReturnsRemainingInterval()
        {
            var state = NewState();
            state.UnlockBuilding("building_hall");
            state.SetBuildingLevel("building_hall", 0);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start(new ActivityStartRequest
                { activityId = "test_build_empty", heroId = "ren" });
            var delivered = new List<ActivityRuntimeEvent>();
            using var processor = NewConstructionAdvanceProcessor(
                state,
                new RecordingProgressionProcessor(),
                delivered.Add);

            var advanced = processor.Advance(new ConstructionAdvanceRequest(started.executionId, 5));

            Assert.That(advanced.Success, Is.True);
            Assert.That(advanced.Completed, Is.True);
            Assert.That(advanced.ConsumedSeconds, Is.EqualTo(1));
            Assert.That(advanced.RemainingSeconds, Is.EqualTo(4));
            Assert.That(advanced.ExecutionStatus, Is.EqualTo(GuildIdle.Core.ActivityRuntimeStatus.Completed));
            Assert.That(advanced.DeferredEvents, Has.Count.EqualTo(2));
            Assert.That(delivered, Is.Empty);
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
            Assert.That(state.GetActivityExecution(started.executionId), Is.Null);
            Assert.That(state.IsActivityCompleted("test_build_empty"), Is.True);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
        }

        [Test]
        public void ConstructionAdvanceSafetyLimitIsResumableWithoutLosingInterval()
        {
            var state = NewState();
            state.UnlockBuilding("building_campfire");
            state.SetBuildingLevel("building_campfire", 0);
            Assert.That(state.Storage.Add("limit-build-wood", state.Storage.GetSnapshot().Revision,
                "resource_pine_wood", 2).Success, Is.True);
            Assert.That(state.Storage.Add("limit-build-stone", state.Storage.GetSnapshot().Revision,
                "resource_stone", 2).Success, Is.True);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start(new ActivityStartRequest
                { activityId = "test_build_campfire", heroId = "ren" });
            using var processor = NewConstructionAdvanceProcessor(state);

            var limited = processor.Advance(new ConstructionAdvanceRequest(started.executionId, 1000, 1));
            var resumed = processor.Advance(
                new ConstructionAdvanceRequest(started.executionId, limited.RemainingSeconds, 10));

            Assert.That(limited.Success, Is.True);
            Assert.That(limited.StopReason, Is.EqualTo(ConstructionAdvanceStopReason.ProcessingLimitReached));
            Assert.That(limited.ConsumedSeconds, Is.EqualTo(1));
            Assert.That(limited.RemainingSeconds, Is.EqualTo(999));
            Assert.That(resumed.Success, Is.True);
            Assert.That(resumed.StopReason, Is.EqualTo(ConstructionAdvanceStopReason.ConstructionCompleted));
            Assert.That(resumed.ConsumedSeconds, Is.EqualTo(1));
            Assert.That(resumed.RemainingSeconds, Is.EqualTo(998));
        }

        [Test]
        public void ConstructionAdvanceStopsWithoutMutatingPausedPendingOrOtherExecutionTypes()
        {
            var pausedState = NewState();
            pausedState.UnlockBuilding("building_hall");
            pausedState.SetBuildingLevel("building_hall", 0);
            var pausedRuntime = new ActivityRuntimeService(pausedState,
                new PlayerStateActivityAdapter(pausedState));
            var pausedStart = pausedRuntime.Start(new ActivityStartRequest
                { activityId = "test_build_empty", heroId = "ren" });
            Assert.That(pausedRuntime.PauseConstruction(pausedStart.executionId).success, Is.True);
            using var pausedProcessor = NewConstructionAdvanceProcessor(pausedState);
            var paused = pausedProcessor.Advance(new ConstructionAdvanceRequest(pausedStart.executionId, 10));

            var pendingState = NewState();
            pendingState.UnlockBuilding("building_campfire");
            pendingState.SetBuildingLevel("building_campfire", 0);
            Assert.That(pendingState.Storage.Add("pending-stop-wood",
                pendingState.Storage.GetSnapshot().Revision, "resource_pine_wood", 2).Success, Is.True);
            Assert.That(pendingState.Storage.Add("pending-stop-stone",
                pendingState.Storage.GetSnapshot().Revision, "resource_stone", 2).Success, Is.True);
            var pendingRuntime = new ActivityRuntimeService(pendingState,
                new PlayerStateActivityAdapter(pendingState));
            var pendingStart = pendingRuntime.Start(new ActivityStartRequest
                { activityId = "test_build_campfire", heroId = "ren" });
            using var pendingProcessor = NewConstructionAdvanceProcessor(pendingState);
            Assert.That(pendingProcessor.Advance(
                new ConstructionAdvanceRequest(pendingStart.executionId, 2)).Success, Is.True);
            var pendingBefore = pendingState.GetActivityExecution(pendingStart.executionId);
            var pending = pendingProcessor.Advance(new ConstructionAdvanceRequest(pendingStart.executionId, 10));

            var workState = NewState();
            var workRuntime = new ActivityRuntimeService(workState, new PlayerStateActivityAdapter(workState));
            var workStart = workRuntime.Start(WorkStart("work_pine_wood", "ren", 1));
            using var workProcessor = NewConstructionAdvanceProcessor(workState);
            var work = workProcessor.Advance(new ConstructionAdvanceRequest(workStart.executionId, 10));

            Assert.That(paused.StopReason, Is.EqualTo(ConstructionAdvanceStopReason.ExecutionNotRunning));
            Assert.That(pausedState.GetActivityExecution(pausedStart.executionId).accumulatedBuildPoints, Is.Zero);
            Assert.That(pending.StopReason, Is.EqualTo(ConstructionAdvanceStopReason.ExecutionNotRunning));
            Assert.That(pendingState.GetActivityExecution(pendingStart.executionId).accumulatedBuildPoints,
                Is.EqualTo(pendingBefore.accumulatedBuildPoints));
            Assert.That(pendingState.PendingResults.GetAll(), Has.Length.EqualTo(1));
            Assert.That(work.StopReason, Is.EqualTo(ConstructionAdvanceStopReason.NotConstructionExecution));
            Assert.That(workState.GetActivityExecution(workStart.executionId).elapsedSeconds, Is.Zero);
        }

        [Test]
        public void ConstructionAdvanceOuterRollbackRestoresProgressCompletionAndOutbox()
        {
            var state = NewState();
            state.UnlockBuilding("building_hall");
            state.SetBuildingLevel("building_hall", 0);
            var adapter = new PlayerStateActivityAdapter(state);
            var runtime = new ActivityRuntimeService(state, adapter);
            var started = runtime.Start(new ActivityStartRequest
                { activityId = "test_build_empty", heroId = "ren" });
            var checkpoint = adapter.CaptureCheckpoint();
            using var processor = NewConstructionAdvanceProcessor(state);

            var advanced = processor.Advance(new ConstructionAdvanceRequest(started.executionId, 1));
            adapter.RestoreCheckpoint(checkpoint);
            var restored = state.GetActivityExecution(started.executionId);

            Assert.That(advanced.Success, Is.True);
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.status, Is.EqualTo(GuildIdle.Core.ActivityRuntimeStatus.Running));
            Assert.That(restored.accumulatedBuildPoints, Is.Zero);
            Assert.That(restored.buildingLevelApplied, Is.False);
            Assert.That(restored.buildingEventPending, Is.False);
            Assert.That(state.GetBuildingLevel("building_hall"), Is.Zero);
            Assert.That(state.IsActivityCompleted("test_build_empty"), Is.False);
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
            Assert.That(state.IsHeroBusy("ren"), Is.True);
        }

        [Test]
        public void FailedEmptyConstructionCompletionDoesNotExposeOrPublishDeferredEvents()
        {
            var state = NewState();
            state.UnlockBuilding("building_hall");
            state.SetBuildingLevel("building_hall", 0);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start(new ActivityStartRequest
                { activityId = "test_build_empty", heroId = "ren" });
            var progression = new RecordingProgressionProcessor { FailActivityCompleted = true };
            var delivered = new List<ActivityRuntimeEvent>();
            using var processor = NewConstructionAdvanceProcessor(state, progression, delivered.Add);

            var advanced = processor.Advance(new ConstructionAdvanceRequest(started.executionId, 1));
            processor.PublishDeferredEvents(advanced);
            var execution = state.GetActivityExecution(started.executionId);

            Assert.That(advanced.Success, Is.False);
            Assert.That(advanced.StopReason, Is.EqualTo(ConstructionAdvanceStopReason.RuntimeError));
            Assert.That(HasIssue(advanced.Issues, "TestActivityFailed"), Is.True);
            Assert.That(advanced.DeferredEvents, Is.Empty);
            Assert.That(advanced.DeferredResolvedEvents, Is.Empty);
            Assert.That(delivered, Is.Empty);
            Assert.That(progression.BuildingLevelChangedCount, Is.EqualTo(1));
            Assert.That(progression.ActivityCompletedCount, Is.Zero);
            Assert.That(execution, Is.Not.Null);
            Assert.That(execution.status, Is.EqualTo(GuildIdle.Core.ActivityRuntimeStatus.Running));
            Assert.That(execution.accumulatedBuildPoints, Is.Zero);
            Assert.That(execution.buildingLevelApplied, Is.False);
            Assert.That(execution.buildingEventPending, Is.False);
            Assert.That(execution.buildingEventPublished, Is.False);
            Assert.That(state.GetBuildingLevel("building_hall"), Is.Zero);
            Assert.That(state.IsActivityCompleted("test_build_empty"), Is.False);
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
            Assert.That(state.IsHeroBusy("ren"), Is.True);
        }

        [Test]
        public void ConstructionAdvanceFormulaFailureLeavesNoPartialMutation()
        {
            var state = NewState();
            state.UnlockBuilding("building_campfire");
            state.SetBuildingLevel("building_campfire", 0);
            Assert.That(state.Storage.Add("formula-build-wood", state.Storage.GetSnapshot().Revision,
                "resource_pine_wood", 2).Success, Is.True);
            Assert.That(state.Storage.Add("formula-build-stone", state.Storage.GetSnapshot().Revision,
                "resource_stone", 2).Success, Is.True);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start(new ActivityStartRequest
                { activityId = "test_build_campfire", heroId = "ren" });
            Assert.That(RuntimeConfigs.Formulas.TryGetFormula("test_build_points", out var formula), Is.True);
            using var processor = NewConstructionAdvanceProcessor(state);

            formula.enabled = false;
            ConstructionAdvanceResult advanced;
            try
            {
                advanced = processor.Advance(new ConstructionAdvanceRequest(started.executionId, 2));
            }
            finally
            {
                formula.enabled = true;
            }
            var execution = state.GetActivityExecution(started.executionId);

            Assert.That(advanced.Success, Is.False);
            Assert.That(advanced.StopReason, Is.EqualTo(ConstructionAdvanceStopReason.RuntimeError));
            Assert.That(HasIssue(advanced.Issues, "FormulaDisabled"), Is.True);
            Assert.That(execution.accumulatedBuildPoints, Is.Zero);
            Assert.That(execution.elapsedSeconds, Is.Zero);
            Assert.That(state.GetBuildingLevel("building_campfire"), Is.Zero);
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
        }

        [Test]
        public void FormulaRuntimeSupportsContextSkillTypesAndDangerInclusiveBoundaries()
        {
            var runtime = new FormulaRuntime();
            var context = new FormulaEvaluationContext { skillLevel = 1, hasContextBase = true, contextBase = 10 };
            context.SetStat("Agility", 2);
            context.SetStat("Luck", 1);
            var result = runtime.Evaluate(new FormulaConfigDto
            {
                formulaId = "test_fractional_risk",
                formulaType = "context_base_minus_stats_and_skill_level",
                primaryStat = "Agility",
                primaryStatMultiplier = 0.5f,
                secondaryStat = "Luck",
                secondaryStatMultiplier = 0.5f,
                levelMultiplier = 1,
                minValue = 5,
                rounding = "round_2",
                enabled = true
            }, context);

            Assert.That(result.success, Is.True);
            Assert.That(result.value, Is.EqualTo(7.5f));
            Assert.That(runtime.Evaluate(new FormulaConfigDto
            {
                formulaId = "disabled",
                formulaType = "linear_stats_with_skill_level",
                primaryStat = "Agility",
                secondaryStat = "Luck",
                rounding = "floor",
                enabled = false
            }, context).code, Is.EqualTo("FormulaDisabled"));
            Assert.That(runtime.Evaluate(new FormulaConfigDto
            {
                formulaId = "unsupported",
                formulaType = "production_formula_id_switch",
                primaryStat = "Agility",
                secondaryStat = "Luck",
                rounding = "floor",
                enabled = true
            }, context).code, Is.EqualTo("FormulaTypeUnsupported"));
            Assert.That(runtime.Evaluate(new FormulaConfigDto
            {
                formulaId = "incomplete",
                formulaType = "linear_stats_with_skill_level",
                primaryStat = "Agility",
                secondaryStat = "Luck",
                enabled = true
            }, context).code, Is.EqualTo("FormulaRoundingUnsupported"));
            Assert.That(ActivityRuntimeService.RollDanger(1, new FixedRandom(1), out _), Is.True);
            Assert.That(ActivityRuntimeService.RollDanger(5, new FixedRandom(5), out _), Is.True);
            Assert.That(ActivityRuntimeService.RollDanger(5, new FixedRandom(6), out _), Is.False);
            Assert.That(ActivityRuntimeService.RollDanger(100, new FixedRandom(100), out _), Is.True);
        }

        [Test]
        public void EmptyConstructionResultCompletesImmediatelyAndReleasesHero()
        {
            var state = NewState();
            state.UnlockBuilding("building_hall");
            state.SetBuildingLevel("building_hall", 0);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), progressionProcessor: new RecordingProgressionProcessor());
            var started = runtime.Start(new ActivityStartRequest { activityId = "test_build_empty", heroId = "ren" });

            var completed = runtime.Tick(1f);

            Assert.That(started.success, Is.True);
            Assert.That(completed.success, Is.True);
            Assert.That(completed.events, Has.Length.EqualTo(2));
            Assert.That(completed.events[0].eventType, Is.EqualTo(ActivityRuntimeEventType.BuildingLevelChanged));
            Assert.That(completed.events[1].eventType, Is.EqualTo(ActivityRuntimeEventType.ActivityCompleted));
            Assert.That(state.GetBuildingLevel("building_hall"), Is.EqualTo(1));
            Assert.That(state.GetActivityExecution(started.executionId), Is.Null);
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
            Assert.That(state.IsActivityCompleted("test_build_empty"), Is.True);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
        }

        [Test]
        public void PendingBuildingLevelEventIsReconciledExactlyOnce()
        {
            var state = NewState();
            state.UnlockBuilding("building_campfire");
            state.SetBuildingLevel("building_campfire", 0);
            Assert.That(state.Storage.Add("outbox-wood", state.Storage.GetSnapshot().Revision, "resource_pine_wood", 2).Success, Is.True);
            Assert.That(state.Storage.Add("outbox-stone", state.Storage.GetSnapshot().Revision, "resource_stone", 2).Success, Is.True);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start(new ActivityStartRequest { activityId = "test_build_campfire", heroId = "ren" });
            Assert.That(started.success, Is.True);
            var ticked = runtime.Tick(2f);
            Assert.That(ticked.success, Is.False);
            Assert.That(HasIssue(ticked.issues, "BuildingEventProcessorMissing"), Is.True);
            Assert.That(state.GetActivityExecution(started.executionId).buildingEventPending, Is.True);
            var storage = new MemorySaveStorage();
            Assert.That(SaveService.Save(state, storage), Is.True);
            state = SaveService.Load(_factory, storage);

            var delivered = new List<ActivityRuntimeEvent>();
            var progression = new RecordingProgressionProcessor();
            _ = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), eventSink: delivered.Add, progressionProcessor: progression);
            _ = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), eventSink: delivered.Add, progressionProcessor: progression);

            Assert.That(delivered, Has.Count.EqualTo(1));
            Assert.That(delivered[0].eventType, Is.EqualTo(ActivityRuntimeEventType.BuildingLevelChanged));
            Assert.That(state.GetActivityExecution(started.executionId).buildingEventPending, Is.False);
            Assert.That(state.GetActivityExecution(started.executionId).buildingEventPublished, Is.True);
        }

        [Test]
        public void EmptyConstructionKeepsOutboxWhenBuildingEventProcessorFails()
        {
            var state = NewState();
            state.UnlockBuilding("building_hall");
            state.SetBuildingLevel("building_hall", 0);
            var progression = new RecordingProgressionProcessor { FailBuildingLevelChanged = true };
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), progressionProcessor: progression);
            var started = runtime.Start(new ActivityStartRequest { activityId = "test_build_empty", heroId = "ren" });

            var completed = runtime.Tick(1f);
            var execution = state.GetActivityExecution(started.executionId);

            Assert.That(completed.success, Is.False);
            Assert.That(HasIssue(completed.issues, "TestBuildingFailed"), Is.True);
            Assert.That(execution, Is.Not.Null);
            Assert.That(execution.buildingLevelApplied, Is.True);
            Assert.That(execution.buildingEventPending, Is.True);
            Assert.That(execution.completionPhase, Is.EqualTo("BuildingEventPending"));
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.IsActivityCompleted("test_build_empty"), Is.False);
        }

        [Test]
        public void BuildingEventAckSaveFailureLeavesOutboxRetryable()
        {
            var storage = new MemorySaveStorage();
            var state = NewState();
            state.UnlockBuilding("building_hall");
            state.SetBuildingLevel("building_hall", 0);
            Assert.That(SaveService.Save(state, storage), Is.True);
            state = SaveService.Load(_factory, storage);
            var progression = new RecordingProgressionProcessor();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state), progressionProcessor: progression);
            var started = runtime.Start(new ActivityStartRequest { activityId = "test_build_empty", heroId = "ren" });
            storage.ThrowOnSaveCall = storage.SaveCalls + 2;
            LogAssert.Expect(LogType.Error, "[SaveService] Failed to save player state. simulated save failure");

            var completed = runtime.Tick(1f);
            var execution = state.GetActivityExecution(started.executionId);

            Assert.That(completed.success, Is.False);
            Assert.That(HasIssue(completed.issues, "BuildingEventAck"), Is.True);
            Assert.That(progression.BuildingLevelChangedCount, Is.EqualTo(1));
            Assert.That(execution, Is.Not.Null);
            Assert.That(execution.buildingEventPending, Is.True);
            Assert.That(execution.buildingEventPublished, Is.False);
            Assert.That(execution.completionPhase, Is.EqualTo("BuildingEventPending"));
            Assert.That(state.IsActivityCompleted("test_build_empty"), Is.False);
        }

        [Test]
        public void RealProgressionStateRollsBackWhenBuildingEventAckSaveFails()
        {
            var storage = new MemorySaveStorage();
            var state = NewState();
            state.UnlockBuilding("building_hall");
            state.SetBuildingLevel("building_hall", 0);
            Assert.That(SaveService.Save(state, storage), Is.True);
            state = SaveService.Load(_factory, storage);
            var progression = PlayerRuntimeComposition.CreateProgressionRuntimeService(state);
            Assert.That(progression.Handle(new GuildIdle.Progression.NewGame()).Issues, Is.Empty);
            Assert.That(state.GetQuestInstance("story:quest_build_hall").status, Is.EqualTo(QuestInstanceStatus.Active));
            var runtime = PlayerRuntimeComposition.CreateRuntimeService(state);
            var started = runtime.Start(new ActivityStartRequest { activityId = "test_build_empty", heroId = "ren" });
            storage.ThrowOnSaveCall = storage.SaveCalls + 2;
            LogAssert.Expect(LogType.Error, "[SaveService] Failed to save player state. simulated save failure");

            var completed = runtime.Tick(1f);
            var execution = state.GetActivityExecution(started.executionId);
            var quest = state.GetQuestInstance("story:quest_build_hall");

            Assert.That(completed.success, Is.False);
            Assert.That(HasIssue(completed.issues, "BuildingEventAck"), Is.True);
            Assert.That(execution, Is.Not.Null);
            Assert.That(execution.buildingEventPending, Is.True);
            Assert.That(execution.buildingEventPublished, Is.False);
            Assert.That(quest.status, Is.EqualTo(QuestInstanceStatus.Active));
            Assert.That(quest.steps[0].currentValue, Is.Zero);
            Assert.That(quest.steps[0].completed, Is.False);
        }

        private PlayerState NewState()
        {
            var state = _factory.Create(new SaveData { currentStageId = "stage_arrival" });
            state.AddHero("ren");
            state.UnlockBuilding("building_warehouse");
            state.SetBuildingLevel("building_warehouse", 0);
            return state;
        }

        private string PrepareDangerEncounter(PlayerState state)
        {
            var random = new CountingDangerSequenceRandom(1);
            var runtime = new ActivityRuntimeService(
                state,
                new PlayerStateActivityAdapter(state),
                random);
            var started = runtime.Start(WorkStart("hunt_rabbits", "ren", 2));
            Assert.That(
                NewWorkAdvanceProcessor(state, random)
                    .Advance(new WorkAdvanceRequest(started.executionId, 10))
                    .DangerBoundaryReached,
                Is.True);
            using var processor = NewDangerEncounterPreparationProcessor(state);
            Assert.That(
                processor.Prepare(new DangerEncounterPreparationRequest(started.executionId)).Success,
                Is.True);
            return started.executionId;
        }

        private static bool AddStartedLinkedCombat(
            PlayerState state,
            string sourceExecutionId)
        {
            var source = state.GetActivityExecution(sourceExecutionId);
            var request = source.linkedCombat;
            var executionId = $"combat-{sourceExecutionId}";
            var sessionId = $"session-{sourceExecutionId}";
            var staged = request.loot ?? Array.Empty<ActivityStagedRewardSaveData>();
            var transferredLoot = new CombatRewardEntrySaveData[staged.Length];
            for (var index = 0; index < staged.Length; index++)
            {
                var entry = staged[index];
                transferredLoot[index] = new CombatRewardEntrySaveData
                {
                    entryId = $"{sessionId}:activity-loot:{index}",
                    sortOrder = index,
                    rewardType = entry.rewardType,
                    targetId = entry.targetId,
                    quantity = entry.quantity,
                    origin = entry.origin,
                    quality = entry.quality,
                    instanceId = entry.instanceId
                };
            }

            var aggregate = new CombatRuntimeAggregate
            {
                execution = new CombatExecutionSaveData
                {
                    executionId = executionId,
                    sessionId = sessionId,
                    sourceActivityId = source.activityId,
                    sourceExecutionId = request.rootExecutionId,
                    sourceRequestId = request.requestId,
                    occupationOwnerId = request.occupationOwnerId,
                    heroId = request.heroId,
                    startOperationId = $"linked-combat-start:{request.requestId}",
                    startFingerprint = "linked-start-fixture",
                    status = CombatExecutionStatus.Running,
                    startedAtUnixSeconds = 100
                },
                session = new CombatSessionSaveData
                {
                    sessionId = sessionId,
                    executionId = executionId,
                    enemyGroupId = request.enemyGroupId,
                    combatMode = request.combatMode,
                    enemyQueue = Array.Empty<CombatEnemyQueueEntrySaveData>(),
                    hero = new CombatantStateSaveData
                    {
                        combatantId = $"{sessionId}:hero",
                        definitionId = request.heroId,
                        currentHp = 100,
                        maxHp = 100
                    },
                    scheduler = new CombatSchedulerStateSaveData(),
                    rng = CombatRngStateFactory.CreateSplitMix64(1234UL),
                    loot = transferredLoot,
                    enemyExpTargetId = request.enemyExpTargetId,
                    completionRewardsSnapshotCreated = true,
                    loadoutKind = CombatLoadoutKind.Empty
                }
            };
            if (!state.AddCombatAggregate(aggregate))
                return false;

            source.linkedCombat.combatExecutionId = executionId;
            return state.UpdateActivityExecution(source);
        }

        private static WorkAdvanceProcessor NewWorkAdvanceProcessor(
            PlayerState state,
            ITransactionalActivityRandom random = null)
        {
            return new WorkAdvanceProcessor(
                state,
                new PlayerStateActivityAdapter(state),
                random ?? new SystemActivityRandom(12345));
        }

        private static DangerEncounterPreparationProcessor NewDangerEncounterPreparationProcessor(
            PlayerState state)
        {
            return new DangerEncounterPreparationProcessor(
                state,
                new PlayerStateActivityAdapter(state),
                new LinkedCombatIntegrityReader(state));
        }

        private static ConstructionAdvanceProcessor NewConstructionAdvanceProcessor(
            PlayerState state,
            IActivityRuntimeProgressionProcessor progression = null,
            System.Action<ActivityRuntimeEvent> eventSink = null)
        {
            return new ConstructionAdvanceProcessor(
                state,
                new PlayerStateActivityAdapter(state),
                progression ?? new RecordingProgressionProcessor(),
                eventSink: eventSink);
        }

        private static bool HasIssue(IReadOnlyList<ActivityRequirementIssue> issues, string issueType)
        {
            foreach (var issue in issues)
            {
                if (issue.issueType == issueType)
                    return true;
            }

            return false;
        }

        private static bool HasLinkedCombatReceipt(PlayerState state, string requestId)
        {
            var aggregateId = $"linked-combat-resolution:{requestId}";
            foreach (var receipt in state.ToSaveData().operationReceipts)
                if (receipt != null && receipt.aggregateId == aggregateId && receipt.operationId == "resolve") return true;
            return false;
        }

        private static ActivityStartRequest WorkStart(string activityId, string heroId, int cycles)
        {
            return new ActivityStartRequest { activityId = activityId, heroId = heroId, plannedCycleCount = cycles };
        }

        internal static ConfigDatabase CreateDatabase()
        {
            return new ConfigDatabase(
                new ItemsRuntimeConfigDto
                {
                    resources = new[]
                    {
                        new ResourceConfigDto { id = "resource_pine_wood", kind = "resource" },
                        new ResourceConfigDto { id = "resource_stone", kind = "resource" },
                        new ResourceConfigDto { id = "resource_rabbit_meat", kind = "resource" }
                    },
                    consumables = new[]
                    {
                        new ConsumableConfigDto
                        {
                            id = "test_work_consumable",
                            kind = "consumable",
                            usePlace = "combat",
                            useCondition = "hp_percent<=40",
                            effects = new[] { "RestoreHealthFlat:25" },
                            cooldownSeconds = 5d,
                            checkIntervalSeconds = 1d
                        }
                    },
                    equipmentWeapons = new[]
                    {
                        new EquipmentWeaponConfigDto
                        {
                            id = "test_danger_weapon",
                            kind = "equipment",
                            equipmentSlot = "weapon",
                            weaponDamageMin = 1,
                            weaponDamageMax = 1,
                            weaponAttackInterval = 1f
                        }
                    },
                    recipes = new[]
                    {
                        new RecipeConfigDto
                        {
                            id = "test_danger_recipe",
                            kind = "recipe",
                            enabled = true
                        }
                    },
                    currencies = new[]
                    {
                        new CurrencyConfigDto { currencyId = "gold_id" }
                    }
                },
                new HeroesRuntimeConfigDto
                {
                    heroes = new[]
                    {
                        new HeroConfigDto
                        {
                            heroId = "ren",
                            enabled = true,
                            uniqueSkillIds = new[] { "test_reliable_hands" },
                            baseStats = new HeroBaseStatsDto { strength = 2, agility = 2, intelligence = 2, luck = 2, endurance = 2 }
                        },
                        new HeroConfigDto { heroId = "test_builder_hero", enabled = true }
                    },
                    heroSkillEffects = new[]
                    {
                        new HeroSkillEffectConfigDto
                        {
                            skillId = "test_reliable_hands",
                            effectId = "test_reliable_hands_effect",
                            trigger = "OnWorkCycleComplete",
                            condition = "activity_category=Gathering",
                            chancePercent = 100,
                            interval = "2",
                            effect = "AddExtraBaseResource",
                            target = "completed_work_base_resource",
                            value = 1
                        }
                    }
                },
                new ActivitiesRuntimeConfigDto
                {
                    activities = new[]
                    {
                        new ActivityConfigDto { id = "work_pine_wood", type = "Work", category = "Gathering", progressMode = "Cycle", cycleSec = 10, fatigueCost = 2, mainSkillId = "skill_gathering", isRepeatable = true },
                        new ActivityConfigDto { id = "random_reward_work", type = "Work", category = "Gathering", cycleSec = 10, fatigueCost = 1, mainSkillId = "skill_gathering", isRepeatable = true },
                        new ActivityConfigDto { id = "test_multi_loot_work", type = "Work", category = "Gathering", cycleSec = 10, fatigueCost = 1, mainSkillId = "skill_gathering", isRepeatable = true },
                        new ActivityConfigDto { id = "bad_effect_target_work", type = "Work", category = "Gathering", cycleSec = 10, fatigueCost = 1, mainSkillId = "skill_gathering", isRepeatable = true },
                        new ActivityConfigDto { id = "bad_danger_work", type = "Work", category = "Hunting", cycleSec = 10, fatigueCost = 1, mainSkillId = "skill_hunting", isRepeatable = true },
                        new ActivityConfigDto { id = "bad_danger_disabled_work", type = "Work", category = "Hunting", cycleSec = 10, fatigueCost = 1, mainSkillId = "skill_hunting", isRepeatable = true },
                        new ActivityConfigDto { id = "bad_danger_unsupported_work", type = "Work", category = "Hunting", cycleSec = 10, fatigueCost = 1, mainSkillId = "skill_hunting", isRepeatable = true },
                        new ActivityConfigDto { id = "bad_reward_range_work", type = "Work", category = "Hunting", cycleSec = 10, fatigueCost = 1, mainSkillId = "skill_hunting", isRepeatable = true },
                        new ActivityConfigDto { id = "hunt_rabbits", type = "Work", category = "Hunting", cycleSec = 10, fatigueCost = 1, mainSkillId = "skill_hunting", isRepeatable = true },
                        new ActivityConfigDto { id = "hunt_boars", type = "Work", category = "Hunting", cycleSec = 10, fatigueCost = 1, mainSkillId = "skill_hunting", isRepeatable = true },
                        new ActivityConfigDto { id = "empty_repeat", type = "Work", cycleSec = 10, fatigueCost = 1, mainSkillId = "skill_gathering", isRepeatable = true },
                        new ActivityConfigDto { id = "bad_cycle", type = "Work", cycleSec = 5, fatigueCost = 1, mainSkillId = "skill_gathering", isRepeatable = true },
                        new ActivityConfigDto { id = "one_shot", type = "Explore", durationSec = 5, fatigueCost = 5, isRepeatable = false },
                        new ActivityConfigDto { id = "one_shot_new", type = "Explore", durationSec = 5, isRepeatable = false },
                        new ActivityConfigDto { id = "one_shot_work", type = "Work", category = "Gathering", progressMode = "Timer", durationSec = 5, fatigueCost = 1, mainSkillId = "skill_gathering", isRepeatable = false }
                    },
                    skills = new[]
                    {
                        new SkillConfigDto { skillId = "skill_gathering" },
                        new SkillConfigDto { skillId = "skill_hunting" },
                        new SkillConfigDto { skillId = "skill_construction" }
                    },
                    skillsProgression = new[]
                    {
                        new SkillProgressionConfigDto { level = 1, totalExpRequired = 0 }
                    },
                    rewards = new[]
                    {
                        Reward("work_pine_wood", "Resource", "resource_pine_wood", 1, "OnCycle"),
                        Reward("work_pine_wood", "SkillExp", "skill_gathering", 1, "OnCycle"),
                        Reward("random_reward_work", "Resource", "resource_pine_wood", 1, 5, 100, "OnCycle"),
                        Reward("test_multi_loot_work", "Consumable", "test_work_consumable", 1, "OnCycle"),
                        Reward("test_multi_loot_work", "Resource", "resource_pine_wood", 1, "OnCycle"),
                        Reward("bad_effect_target_work", "Consumable", "test_work_consumable", 1, "OnCycle"),
                        Reward("bad_danger_work", "Resource", "resource_pine_wood", 1, "OnCycle"),
                        Reward("bad_danger_disabled_work", "Resource", "resource_pine_wood", 1, "OnCycle"),
                        Reward("bad_danger_unsupported_work", "Resource", "resource_pine_wood", 1, "OnCycle"),
                        Reward("bad_reward_range_work", "Resource", "resource_pine_wood", 5, 1, 100, "OnCycle"),
                        Reward("hunt_rabbits", "Resource", "resource_rabbit_meat", 1, "OnCycle"),
                        Reward("hunt_rabbits", "SkillExp", "skill_hunting", 2, "OnCycle"),
                        Reward("hunt_boars", "Resource", "resource_rabbit_meat", 1, "OnCycle"),
                        Reward("hunt_boars", "Consumable", "test_work_consumable", 1, "OnCycle"),
                        Reward("hunt_boars", "Recipe", "test_danger_recipe", 1, "OnCycle"),
                        Reward("hunt_boars", "Equipment", "test_danger_weapon", 1, "OnCycle"),
                        Reward("hunt_boars", "Item", "resource_stone", 1, "OnCycle"),
                        Reward("bad_cycle", "Unsupported", "bad_reward", 1, "OnCycle"),
                        Reward("one_shot_new", "Resource", "resource_pine_wood", 1, "OnComplete"),
                        Reward("one_shot_new", "Gold", "gold_id", 2, "OnFirstComplete"),
                        Reward("one_shot_work", "Resource", "resource_stone", 2, "OnComplete")
                    },
                    dangerEncounters = new[]
                    {
                        new DangerEncounterConfigDto
                        {
                            dangerEncounterId = "danger_disabled_formula",
                            activityId = "bad_danger_disabled_work",
                            riskPercent = 25,
                            enemyGroupId = "enemy_group_test_rabbits",
                            combatMode = "Queue_1v1",
                            defeatLossRule = "CombatDefeatLootLoss25To50",
                            riskFormulaId = "test_disabled_danger_risk"
                        },
                        new DangerEncounterConfigDto
                        {
                            dangerEncounterId = "danger_unsupported_formula",
                            activityId = "bad_danger_unsupported_work",
                            riskPercent = 25,
                            enemyGroupId = "enemy_group_test_rabbits",
                            combatMode = "Queue_1v1",
                            defeatLossRule = "CombatDefeatLootLoss25To50",
                            riskFormulaId = "test_unsupported_danger_risk"
                        },
                        new DangerEncounterConfigDto
                        {
                            dangerEncounterId = "danger_bad_formula",
                            activityId = "bad_danger_work",
                            riskPercent = 25,
                            enemyGroupId = "enemy_group_test_rabbits",
                            combatMode = "Queue_1v1",
                            defeatLossRule = "CombatDefeatLootLoss25To50",
                            riskFormulaId = "missing_danger_formula"
                        },
                        new DangerEncounterConfigDto
                        {
                            dangerEncounterId = "danger_test_rabbits",
                            activityId = "hunt_rabbits",
                            riskPercent = 25,
                            enemyGroupId = "enemy_group_test_rabbits",
                            combatMode = "Queue_1v1",
                            defeatLossRule = "CombatDefeatLootLoss25To50",
                            riskFormulaId = "test_danger_risk"
                        },
                        new DangerEncounterConfigDto
                        {
                            dangerEncounterId = "danger_test_boars",
                            activityId = "hunt_boars",
                            riskPercent = 25,
                            enemyGroupId = "enemy_group_test_rabbits",
                            combatMode = "Queue_1v1",
                            defeatLossRule = "CombatDefeatLootLoss25To50",
                            riskFormulaId = "test_danger_risk"
                        }
                    }
                },
                new BuildingsRuntimeConfigDto
                {
                    buildings = new[]
                    {
                        new BuildingConfigDto { buildingId = "building_hall", levels = 1, startLevel = 0, visibleAtStart = true },
                        new BuildingConfigDto { buildingId = "building_campfire", levels = 1, startLevel = 0, visibleAtStart = true },
                        new BuildingConfigDto { buildingId = "building_warehouse", levels = 0, startLevel = 0, visibleAtStart = true }
                    },
                    buildingLevels = new[]
                    {
                        new BuildingLevelConfigDto { buildingId = "building_hall", level = 0, activeHeroLimit = 1 },
                        new BuildingLevelConfigDto { buildingId = "building_hall", level = 1, sourceActivityId = "test_build_empty", buildFormulaId = "test_build_points", buildPointsRequired = 1, skillId = "skill_construction", fatigueCost = 1, activeHeroLimit = 1 },
                        new BuildingLevelConfigDto { buildingId = "building_campfire", level = 0 },
                        new BuildingLevelConfigDto { buildingId = "building_campfire", level = 1, sourceActivityId = "test_build_campfire", buildFormulaId = "test_build_points", buildPointsRequired = 2, skillId = "skill_construction", fatigueCost = 3, skillExp = 4 },
                        new BuildingLevelConfigDto { buildingId = "building_warehouse", level = 0 }
                    },
                    buildActions = new[]
                    {
                        new BuildActionConfigDto
                        {
                            id = "test_build_campfire",
                            type = "Build",
                            targetBuildingId = "building_campfire",
                            targetLevel = 1,
                            buildFormulaId = "test_build_points",
                            buildPointsRequired = 2,
                            skillId = "skill_construction",
                            fatigueCost = 3,
                            materials = new[]
                            {
                                new MaterialCostDto { id = "resource_pine_wood", count = 2 },
                                new MaterialCostDto { id = "resource_stone", count = 2 }
                            },
                            skillExp = 4
                        },
                        new BuildActionConfigDto
                        {
                            id = "test_build_empty",
                            type = "Build",
                            targetBuildingId = "building_hall",
                            targetLevel = 1,
                            buildFormulaId = "test_build_points",
                            buildPointsRequired = 1,
                            skillId = "skill_construction",
                            fatigueCost = 1,
                            skillExp = 0
                        }
                    }
                },
                new QuestRuntimeConfigDto
                {
                    stages = new[] { new StageConfigDto { stageId = "stage_arrival", enabled = true } },
                    storyQuests = new[] { new StoryQuestConfigDto { questId = "quest_build_hall", enabled = true } },
                    questStartConditions = new[] { new QuestStartConditionConfigDto { questId = "quest_build_hall", conditionGroup = "default", conditionType = "NewGame", compareOperator = "GreaterOrEqual", value = 1 } },
                    questSteps = new[] { new QuestStepConfigDto { questId = "quest_build_hall", stepId = "build_hall", objectiveType = "BuildingLevel", targetId = "building_hall", compareOperator = "GreaterOrEqual", targetValue = 2, required = true } }
                },
                null,
                new FormulaRuntimeConfigDto
                {
                    formulas = new[]
                    {
                        new FormulaConfigDto
                        {
                            formulaId = "test_build_points",
                            formulaType = "linear_stats_with_skill_level",
                            baseValue = 1,
                            primaryStat = "Intelligence",
                            secondaryStat = "Strength",
                            rounding = "round_2",
                            enabled = true
                        },
                        new FormulaConfigDto
                        {
                            formulaId = "test_danger_risk",
                            formulaType = "context_base_minus_stats_and_skill_level",
                            primaryStat = "Agility",
                            secondaryStat = "Luck",
                            primaryStatMultiplier = 0.5f,
                            secondaryStatMultiplier = 0.5f,
                            levelMultiplier = 0.5f,
                            minValue = 5,
                            rounding = "round_2",
                            enabled = true
                        },
                        new FormulaConfigDto
                        {
                            formulaId = "test_disabled_danger_risk",
                            formulaType = "context_base_minus_stats_and_skill_level",
                            primaryStat = "Agility",
                            secondaryStat = "Luck",
                            rounding = "round_2",
                            enabled = false
                        },
                        new FormulaConfigDto
                        {
                            formulaId = "test_unsupported_danger_risk",
                            formulaType = "production_formula_id_switch",
                            primaryStat = "Agility",
                            secondaryStat = "Luck",
                            rounding = "round_2",
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
                        new StorageRuleConfigDto { storageRuleId = "storage_resource", itemKind = "resource", mode = "stack", maxStack = 100, occupiesSlot = true },
                        new StorageRuleConfigDto { storageRuleId = "storage_consumable", itemKind = "consumable", mode = "stack", maxStack = 20, occupiesSlot = true }
                    },
                    storageBuildings = new[]
                    {
                        new StorageBuildingConfigDto { buildingId = "building_warehouse", level = 0, slotCount = 20 }
                    },
                    itemStates = new[]
                    {
                        new ItemStateConfigDto { stateId = "on_storage", isInStorage = true, occupiesCapacity = true, availabilityMode = ItemAvailabilityMode.Available },
                        new ItemStateConfigDto { stateId = "equipped", requiresOwner = true, availabilityMode = ItemAvailabilityMode.Equipped },
                        new ItemStateConfigDto { stateId = "reserved_for_task", isInStorage = true, occupiesCapacity = true, availabilityMode = ItemAvailabilityMode.Reserved },
                        new ItemStateConfigDto { stateId = "in_task", availabilityMode = ItemAvailabilityMode.InAction }
                    }
                },
                null);
        }

        private static ActivityRewardConfigDto Reward(string activityId, string type, string targetId, int amount, string moment)
        {
            return Reward(activityId, type, targetId, amount, amount, 100, moment);
        }

        private static ActivityRewardConfigDto Reward(string activityId, string type, string targetId, int min, int max, float chance, string moment)
        {
            return new ActivityRewardConfigDto
            {
                activityId = activityId,
                rewardType = type,
                targetId = targetId,
                min = min,
                max = max,
                chance = chance,
                grantMoment = moment
            };
        }

        private sealed class MemorySaveStorage : ISaveStorage
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>();
            public bool ThrowOnSave { get; set; }
            public int SaveCalls { get; private set; }
            public int ThrowOnSaveCall { get; set; } = -1;

            public bool HasKey(string key) => _values.ContainsKey(key);
            public string GetString(string key, string defaultValue) => _values.TryGetValue(key, out var value) ? value : defaultValue;
            public void SetString(string key, string value) => _values[key] = value;
            public void DeleteKey(string key) => _values.Remove(key);
            public void Save()
            {
                SaveCalls++;
                if (ThrowOnSave || (ThrowOnSaveCall > 0 && SaveCalls >= ThrowOnSaveCall))
                    throw new System.InvalidOperationException("simulated save failure");
            }
        }

        private sealed class RecordingProgressionProcessor : IActivityRuntimeProgressionProcessor
        {
            public int BuildingLevelChangedCount { get; private set; }
            public int ActivityCompletedCount { get; private set; }
            public bool FailBuildingLevelChanged { get; set; }
            public bool FailActivityCompleted { get; set; }

            public ActivityRuntimeProgressionResult ProcessBuildingLevelChanged(string buildingId, int level)
            {
                if (FailBuildingLevelChanged)
                    return new ActivityRuntimeProgressionResult { success = false, code = "TestBuildingFailed", message = "test failure" };
                BuildingLevelChangedCount++;
                return new ActivityRuntimeProgressionResult { success = true, code = "Applied" };
            }

            public ActivityRuntimeProgressionResult ProcessActivityCompleted(string activityId)
            {
                if (FailActivityCompleted)
                    return new ActivityRuntimeProgressionResult { success = false, code = "TestActivityFailed", message = "test failure" };
                ActivityCompletedCount++;
                return new ActivityRuntimeProgressionResult { success = true, code = "Applied" };
            }

            public ActivityRuntimeProgressionResult ProcessActivityFailed(string activityId) =>
                new ActivityRuntimeProgressionResult
                {
                    success = true,
                    code = "Applied"
                };
        }

        private sealed class FixedRandom : ITransactionalActivityRandom
        {
            private readonly int _value;
            public FixedRandom(int value) { _value = value; }
            public int RangeInclusive(int min, int max) => Mathf.Clamp(_value, min, max);
            public float Percent() => 0f;
            public ActivityRandomState CaptureState() => default;
            public void RestoreState(ActivityRandomState state) { }
        }

        private sealed class CountingRandom : ITransactionalActivityRandom
        {
            public int RangeCalls { get; private set; }
            public int PercentCalls { get; private set; }
            public int RangeInclusive(int min, int max)
            {
                RangeCalls++;
                return min;
            }

            public float Percent()
            {
                PercentCalls++;
                return 0f;
            }

            public ActivityRandomState CaptureState() =>
                new ActivityRandomState(((ulong)(uint)RangeCalls << 32) | (uint)PercentCalls);

            public void RestoreState(ActivityRandomState state)
            {
                RangeCalls = (int)(state.Value >> 32);
                PercentCalls = (int)(state.Value & uint.MaxValue);
            }
        }

        private sealed class DangerSequenceRandom : IActivityRandom
        {
            private readonly Queue<int> _dangerRolls;
            public DangerSequenceRandom(params int[] dangerRolls) { _dangerRolls = new Queue<int>(dangerRolls); }
            public int RangeInclusive(int min, int max)
            {
                if (min == 1 && max == 100 && _dangerRolls.Count > 0)
                    return Mathf.Clamp(_dangerRolls.Dequeue(), min, max);
                return min;
            }
            public float Percent() => 0f;
        }

        private sealed class CountingDangerSequenceRandom : ITransactionalActivityRandom
        {
            private readonly int[] _dangerRolls;
            private int _dangerRollIndex;
            public CountingDangerSequenceRandom(params int[] dangerRolls) { _dangerRolls = dangerRolls ?? System.Array.Empty<int>(); }
            public int RangeCalls { get; private set; }
            public int PercentCalls { get; private set; }

            public int RangeInclusive(int min, int max)
            {
                RangeCalls++;
                if (min == 1 && max == 100 && _dangerRollIndex < _dangerRolls.Length)
                    return Mathf.Clamp(_dangerRolls[_dangerRollIndex++], min, max);
                return min;
            }

            public float Percent()
            {
                PercentCalls++;
                return 0f;
            }

            public ActivityRandomState CaptureState() =>
                new ActivityRandomState(
                    ((ulong)(uint)_dangerRollIndex << 42) |
                    ((ulong)(uint)RangeCalls << 21) |
                    (uint)PercentCalls);

            public void RestoreState(ActivityRandomState state)
            {
                _dangerRollIndex = (int)(state.Value >> 42);
                RangeCalls = (int)((state.Value >> 21) & 0x1FFFFFUL);
                PercentCalls = (int)(state.Value & 0x1FFFFFUL);
            }
        }
    }
}
