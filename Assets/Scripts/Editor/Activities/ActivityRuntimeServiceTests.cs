using System.Collections.Generic;
using GuildIdle.Activities;
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
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
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
            LogAssert.Expect(LogType.Error, "[ActivityRewardResolver] Unsupported reward type 'Unsupported'.");
            LogAssert.Expect(LogType.Error, "[ActivityRewardResolver] Unsupported reward type 'Unsupported' for activity 'bad_cycle'.");

            var result = runtime.Tick(5f);
            var execution = state.GetActivityExecutions()[0];

            Assert.That(result.success, Is.False);
            Assert.That(execution.completedCycles, Is.EqualTo(0));
            Assert.That(execution.elapsedSeconds, Is.EqualTo(5f));
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
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));

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
            Assert.That(handoff.suppressFatigueCost, Is.True);
            Assert.That(handoff.loot, Has.Length.EqualTo(1));
            Assert.That(handoff.loot[0].origin, Is.EqualTo(PendingResultOrigin.ActivityLootInCombat));
            Assert.That(bagResources, Is.EqualTo(1), "Previous-cycle loot must remain in the Activity Bag.");
            Assert.That(bagSkillExp, Is.EqualTo(4), "Skill EXP from both cycles must stay outside combat loss.");

            var claimed = state.PendingResults.ClaimAll("danger-skill-claim", bag.resultId, bag.revision, state.Storage.GetSnapshot().Revision);
            Assert.That(claimed.Success, Is.True);
            Assert.That(state.IsHeroBusy("ren"), Is.True);
            Assert.That(state.GetActivityExecution(started.executionId), Is.Not.Null);

            Assert.That(runtime.BindLinkedCombatExecution(handoff.requestId, "combat-child").success, Is.True);
            var resolved = runtime.ResolveLinkedCombatExecution(handoff.requestId);
            Assert.That(resolved.success, Is.True);
            Assert.That(resolved.completedActivityId, Is.EqualTo("hunt_rabbits"));
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.GetActivityExecution(started.executionId), Is.Null);
        }

        [Test]
        public void ConstructionStartFailureIsAtomicForTestOnlyBuildAction()
        {
            var state = NewState();
            state.UnlockBuilding("building_campfire");
            state.SetBuildingLevel("building_campfire", 0);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
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

            runtime.Tick(1f);
            var beforePause = state.GetActivityExecution(started.executionId).accumulatedBuildPoints;
            Assert.That(runtime.PauseConstruction(started.executionId).success, Is.True);
            Assert.That(runtime.Cancel(started.executionId).success, Is.True);
            var saveStorage = new MemorySaveStorage();
            Assert.That(SaveService.Save(state, saveStorage), Is.True);
            state = SaveService.Load(_factory, saveStorage);
            runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
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

            var result = state.PendingResults.GetAll()[0];
            var claimed = state.PendingResults.ClaimAll("claim-build", result.resultId, result.revision, state.Storage.GetSnapshot().Revision);
            Assert.That(claimed.Success, Is.True);
            Assert.That(claimed.Resolved, Is.True);
            Assert.That(state.GetHeroSkillExp("test_builder_hero", "skill_construction"), Is.EqualTo(4));
            Assert.That(state.GetActivityExecution(started.executionId), Is.Null);
            Assert.That(state.IsHeroBusy("test_builder_hero"), Is.False);
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
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
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

        private PlayerState NewState()
        {
            var state = _factory.Create(new SaveData { currentStageId = "stage_arrival" });
            state.AddHero("ren");
            state.UnlockBuilding("building_warehouse");
            state.SetBuildingLevel("building_warehouse", 0);
            return state;
        }

        private static bool HasIssue(ActivityRequirementIssue[] issues, string issueType)
        {
            foreach (var issue in issues)
            {
                if (issue.issueType == issueType)
                    return true;
            }

            return false;
        }

        private static ActivityStartRequest WorkStart(string activityId, string heroId, int cycles)
        {
            return new ActivityStartRequest { activityId = activityId, heroId = heroId, plannedCycleCount = cycles };
        }

        private static ConfigDatabase CreateDatabase()
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
                        new ActivityConfigDto { id = "work_pine_wood", type = "Work", category = "Gathering", cycleSec = 10, fatigueCost = 2, mainSkillId = "skill_gathering", isRepeatable = true },
                        new ActivityConfigDto { id = "hunt_rabbits", type = "Work", category = "Hunting", cycleSec = 10, fatigueCost = 1, mainSkillId = "skill_hunting", isRepeatable = true },
                        new ActivityConfigDto { id = "empty_repeat", type = "Work", cycleSec = 10, fatigueCost = 1, mainSkillId = "skill_gathering", isRepeatable = true },
                        new ActivityConfigDto { id = "bad_cycle", type = "Work", cycleSec = 5, fatigueCost = 1, mainSkillId = "skill_gathering", isRepeatable = true },
                        new ActivityConfigDto { id = "one_shot", type = "Explore", durationSec = 5, fatigueCost = 5, isRepeatable = false },
                        new ActivityConfigDto { id = "one_shot_new", type = "Explore", durationSec = 5, isRepeatable = false }
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
                        Reward("hunt_rabbits", "Resource", "resource_rabbit_meat", 1, "OnCycle"),
                        Reward("hunt_rabbits", "SkillExp", "skill_hunting", 2, "OnCycle"),
                        Reward("bad_cycle", "Unsupported", "bad_reward", 1, "OnCycle"),
                        Reward("one_shot_new", "Resource", "resource_pine_wood", 1, "OnComplete"),
                        Reward("one_shot_new", "Gold", "gold_id", 2, "OnFirstComplete")
                    },
                    dangerEncounters = new[]
                    {
                        new DangerEncounterConfigDto
                        {
                            dangerEncounterId = "danger_test_rabbits",
                            activityId = "hunt_rabbits",
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
                    stages = new[] { new StageConfigDto { stageId = "stage_arrival", enabled = true } }
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
                        }
                    }
                },
                null,
                null,
                new StorageRuntimeConfigDto
                {
                    storageRules = new[]
                    {
                        new StorageRuleConfigDto { storageRuleId = "storage_resource", itemKind = "resource", mode = "stack", maxStack = 100, occupiesSlot = true }
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
            return new ActivityRewardConfigDto
            {
                activityId = activityId,
                rewardType = type,
                targetId = targetId,
                min = amount,
                max = amount,
                chance = 100,
                grantMoment = moment
            };
        }

        private sealed class MemorySaveStorage : ISaveStorage
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

            public bool HasKey(string key) => _values.ContainsKey(key);
            public string GetString(string key, string defaultValue) => _values.TryGetValue(key, out var value) ? value : defaultValue;
            public void SetString(string key, string value) => _values[key] = value;
            public void DeleteKey(string key) => _values.Remove(key);
            public void Save() { }
        }

        private sealed class FixedRandom : IActivityRandom
        {
            private readonly int _value;
            public FixedRandom(int value) { _value = value; }
            public int RangeInclusive(int min, int max) => Mathf.Clamp(_value, min, max);
            public float Percent() => 0f;
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
    }
}
