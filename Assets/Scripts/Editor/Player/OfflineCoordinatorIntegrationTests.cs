using System;
using System.Collections.Generic;
using System.Text;
using GuildIdle.Activities;
using GuildIdle.Combat;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Crafting;
using GuildIdle.Editor.Configs;
using GuildIdle.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Editor.Player
{
    public sealed class OfflineCoordinatorIntegrationTests
    {
        private const int SaveSizeLimitBytes = 1024 * 1024;
        private const int PersistentCombatAggregateLimit = 8;
        private const int PersistentCombatCollectionLimit = 64;
        private const int ReceiptRetentionLimit = 64;
        private const int ResolvedResultSourceRetentionLimit = 64;
        private const int StorageCapacity = 20;
        private const int StatusStackLimit = 8;

        [Test]
        public void MissingBaselineIsInitializedByOneCoordinatorSaveWithoutProgressOrRecovery()
        {
            var setup = ActivitySetup();
            Assert.That(setup.State.SpendHeroFatigue("ren", 5), Is.True);
            var save = setup.State.ToSaveData();
            save.timeProgress = new TimeProgressSaveData
            {
                baselineInitialized = false,
                lastProcessedUtcSeconds = 0L,
                fatigueRemainders = new[]
                {
                    new HeroFatigueRemainderSaveData
                    {
                        heroId = "ren",
                        fatigueRemainderSeconds = 0
                    }
                }
            };
            setup.Storage.Replace(JsonUtility.ToJson(save));
            setup.Clock.UtcNowSeconds = 5_000L;
            var loaded = SaveService.Load(setup.Factory, setup.Storage);
            var fatigue = loaded.GetHeroFatigue("ren");
            var saves = setup.Storage.SaveCalls;

            var report = Run(loaded);

            Assert.That(report.Success, Is.True);
            Assert.That(report.Code, Is.EqualTo(OfflineCoordinatorCode.BaselineInitialized));
            Assert.That(report.Saved, Is.True);
            Assert.That(report.DeltaSeconds, Is.Zero);
            Assert.That(report.ProcessedExecutionIds, Is.Empty);
            Assert.That(loaded.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(loaded.ToSaveData().timeProgress.lastProcessedUtcSeconds, Is.EqualTo(5_000L));
            Assert.That(setup.Storage.SaveCalls, Is.EqualTo(saves + 1));
        }

        [Test]
        public void FreeHeroRecoveryUsesRemainderAndSameOrEarlierTimestampDoesNotSave()
        {
            var setup = ActivitySetup();
            Assert.That(setup.State.SpendHeroFatigue("ren", 3), Is.True);
            setup.Clock.UtcNowSeconds = 1_059L;

            var first = Run(setup.State);
            var afterFirst = JsonUtility.ToJson(setup.State.ToSaveData());
            var saves = setup.Storage.SaveCalls;
            var repeated = Run(setup.State);
            setup.Clock.UtcNowSeconds = 900L;
            var rollback = Run(setup.State);

            Assert.That(first.Code, Is.EqualTo(OfflineCoordinatorCode.Applied));
            Assert.That(first.Fatigue.RestoredFatigue, Is.Zero);
            Assert.That(GetRemainder(setup.State, "ren"), Is.EqualTo(59));
            Assert.That(repeated.Code, Is.EqualTo(OfflineCoordinatorCode.NoElapsedTime));
            Assert.That(rollback.Code, Is.EqualTo(OfflineCoordinatorCode.ClockRollback));
            Assert.That(repeated.Saved, Is.False);
            Assert.That(rollback.Saved, Is.False);
            Assert.That(JsonUtility.ToJson(setup.State.ToSaveData()), Is.EqualTo(afterFirst));
            Assert.That(setup.Storage.SaveCalls, Is.EqualTo(saves));
        }

        [Test]
        public void WorkUsesWholeIntervalAndBusySnapshotBlocksRecoveryAfterCompletion()
        {
            var setup = ActivitySetup();
            Assert.That(setup.State.SpendHeroFatigue("ren", 4), Is.True);
            var runtime = new ActivityRuntimeService(
                setup.State,
                new PlayerStateActivityAdapter(setup.State));
            var started = runtime.Start(new ActivityStartRequest
            {
                activityId = "work_pine_wood",
                runtimeKind = "Work",
                heroId = "ren",
                plannedCycleCount = 1
            });
            runtime.Dispose();
            Assert.That(started.success, Is.True);
            var fatigue = setup.State.GetHeroFatigue("ren");
            setup.Clock.UtcNowSeconds = 1_060L;

            var report = Run(setup.State, new FixedRandom());

            Assert.That(report.Success, Is.True);
            Assert.That(report.Work.Attempted, Is.EqualTo(1));
            Assert.That(report.Work.Completed, Is.EqualTo(1));
            Assert.That(report.ProcessedExecutionIds, Is.EqualTo(new[] { started.executionId }));
            Assert.That(setup.State.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(GetRemainder(setup.State, "ren"), Is.Zero);
            Assert.That(setup.State.GetActivityExecution(started.executionId).status,
                Is.EqualTo(ActivityRuntimeStatus.ResultPending));
        }

        [Test]
        public void SavedTimerWorkWithLegacyWorkKindSkipsOfflineCycleProcessor()
        {
            var setup = ActivitySetup();
            using var runtime = new ActivityRuntimeService(
                setup.State,
                new PlayerStateActivityAdapter(setup.State));
            var started = runtime.Start("one_shot_work", "ren");
            Assert.That(started.success, Is.True);
            var execution = setup.State.GetActivityExecution(started.executionId);
            execution.runtimeKind = "Work";
            execution.plannedCycles = 1;
            execution.currentCycleFatiguePaid = true;
            execution.cyclePhase = "Running";
            Assert.That(setup.State.UpdateActivityExecution(execution), Is.True);
            setup.Clock.UtcNowSeconds = 1_010L;

            var report = Run(setup.State, new FixedRandom());
            var ticked = runtime.Tick(5f);

            Assert.That(report.Success, Is.True);
            Assert.That(report.Work.Attempted, Is.Zero);
            Assert.That(report.ProcessedExecutionIds, Is.Empty);
            Assert.That(ticked.success, Is.True);
            Assert.That(ticked.processedCycles, Is.Zero);
            Assert.That(setup.State.GetActivityExecution(started.executionId).status,
                Is.EqualTo(ActivityRuntimeStatus.ResultPending));
        }

        [Test]
        public void SnapshotOrderingIsOrdinalAndIndependentAcrossHeroes()
        {
            var setup = ActivitySetup();
            Assert.That(setup.State.AddHero("test_builder_hero"), Is.True);
            var runtime = new ActivityRuntimeService(
                setup.State,
                new PlayerStateActivityAdapter(setup.State));
            var started = runtime.Start(new ActivityStartRequest
            {
                activityId = "work_pine_wood",
                runtimeKind = "Work",
                heroId = "ren",
                plannedCycleCount = 1
            });
            runtime.Dispose();
            Assert.That(started.success, Is.True);
            var template = setup.State.GetActivityExecution(started.executionId);
            Assert.That(setup.State.RemoveActivityExecution(started.executionId), Is.True);
            AddWorkClone(setup.State, template, "work-z", "ren");
            AddWorkClone(setup.State, template, "work-a", "test_builder_hero");
            setup.Clock.UtcNowSeconds = 1_010L;

            var report = Run(setup.State, new FixedRandom());

            Assert.That(report.Success, Is.True);
            Assert.That(report.Work.Attempted, Is.EqualTo(2));
            Assert.That(report.ProcessedExecutionIds, Is.EqualTo(new[] { "work-a", "work-z" }));
            Assert.That(setup.State.GetActivityExecution("work-a").status,
                Is.EqualTo(ActivityRuntimeStatus.ResultPending));
            Assert.That(setup.State.GetActivityExecution("work-z").status,
                Is.EqualTo(ActivityRuntimeStatus.ResultPending));
        }

        [Test]
        public void DangerBoundaryCreatesOnePendingRequestWithoutCombatSessionAndReplaysThroughDangerAsNoOp()
        {
            var setup = ActivitySetup();
            var runtime = new ActivityRuntimeService(
                setup.State,
                new PlayerStateActivityAdapter(setup.State));
            var started = runtime.Start(new ActivityStartRequest
            {
                activityId = "hunt_rabbits",
                runtimeKind = "Work",
                heroId = "ren",
                plannedCycleCount = 2
            });
            runtime.Dispose();
            Assert.That(started.success, Is.True);
            setup.Clock.UtcNowSeconds = 1_010L;

            var first = Run(setup.State, new FixedRandom());
            var execution = setup.State.GetActivityExecution(started.executionId);
            var requestBefore = JsonUtility.ToJson(execution.linkedCombat);
            var bagBefore = JsonUtility.ToJson(setup.State.PendingResults.GetAll()[0]);
            var saves = setup.Storage.SaveCalls;
            setup.Clock.UtcNowSeconds = 1_020L;
            var repeated = Run(setup.State, new FixedRandom());

            Assert.That(first.Success, Is.True);
            Assert.That(first.Danger.Completed, Is.EqualTo(1));
            Assert.That(execution.linkedCombat, Is.Not.Null);
            Assert.That(execution.linkedCombat.requestId, Is.Not.Empty);
            Assert.That(execution.linkedCombat.combatExecutionId, Is.Null.Or.Empty);
            Assert.That(setup.State.GetCombatAggregates(), Is.Empty);
            Assert.That(repeated.Code, Is.EqualTo(OfflineCoordinatorCode.Applied));
            Assert.That(repeated.Danger.Attempted, Is.EqualTo(1));
            Assert.That(repeated.Danger.NoOp, Is.EqualTo(1));
            Assert.That(JsonUtility.ToJson(setup.State.GetActivityExecution(started.executionId).linkedCombat),
                Is.EqualTo(requestBefore));
            Assert.That(JsonUtility.ToJson(setup.State.PendingResults.GetAll()[0]), Is.EqualTo(bagBefore));
            Assert.That(setup.State.GetCombatAggregates(), Is.Empty);
            Assert.That(setup.Storage.SaveCalls, Is.EqualTo(saves + 1));
        }

        [Test]
        public void CorruptedStartedDangerRelationFailsValidationWithoutMutationOrSave()
        {
            var setup = ActivitySetup();
            var executionId = PrepareOfflineDanger(setup, "hunt_rabbits");
            Assert.That(AddStartedLinkedCombat(setup.State, executionId), Is.True);
            var corruptedSave = setup.State.ToSaveData();
            corruptedSave.combatRuntime.executions[0].sourceRequestId = "corrupt-request";
            setup.Storage.Replace(JsonUtility.ToJson(corruptedSave));
            var corruptedState = SaveService.Load(setup.Factory, setup.Storage);
            var before = JsonUtility.ToJson(corruptedState.ToSaveData());
            var saves = setup.Storage.SaveCalls;
            setup.Clock.UtcNowSeconds = 1_020L;

            var report = Run(corruptedState, new FixedRandom());

            Assert.That(report.Code, Is.EqualTo(OfflineCoordinatorCode.DataIntegrityFailure));
            Assert.That(report.FailedStage, Is.EqualTo(OfflineCoordinatorStage.Validation));
            Assert.That(report.StateCommitted, Is.False);
            Assert.That(JsonUtility.ToJson(corruptedState.ToSaveData()), Is.EqualTo(before));
            Assert.That(setup.Storage.SaveCalls, Is.EqualTo(saves));
            Assert.That(corruptedState.GetCombatAggregates(), Has.Length.EqualTo(1));
        }

        [Test]
        public void DangerProcessorFailureRollsBackEarlierWorkAndPreservesCorruptHandoff()
        {
            var setup = ActivitySetup();
            var dangerExecutionId = PrepareOfflineDanger(setup, "hunt_rabbits");
            var corrupt = setup.State.GetActivityExecution(dangerExecutionId);
            corrupt.linkedCombat.loot[0].quantity++;
            Assert.That(setup.State.UpdateActivityExecution(corrupt), Is.True);
            Assert.That(setup.State.AddHero("test_builder_hero"), Is.True);
            AddRunningWorkExecution(setup.State, "work-before-danger", "test_builder_hero", 1);
            var before = JsonUtility.ToJson(setup.State.ToSaveData());
            var saves = setup.Storage.SaveCalls;
            setup.Clock.UtcNowSeconds = 1_020L;

            var report = Run(setup.State, new FixedRandom());

            Assert.That(report.Code, Is.EqualTo(OfflineCoordinatorCode.DataIntegrityFailure));
            Assert.That(report.FailedStage, Is.EqualTo(OfflineCoordinatorStage.Danger));
            Assert.That(report.Work.Attempted, Is.EqualTo(1));
            Assert.That(JsonUtility.ToJson(setup.State.ToSaveData()), Is.EqualTo(before));
            Assert.That(setup.Storage.SaveCalls, Is.EqualTo(saves));
        }

        [Test]
        public void DangerWithOnlyItemLikeLootResolvesEmptyActivityBagWithoutStartingCombat()
        {
            var setup = ActivitySetup();
            using var runtime = new ActivityRuntimeService(
                setup.State,
                new PlayerStateActivityAdapter(setup.State));
            var started = runtime.Start(new ActivityStartRequest
            {
                activityId = "hunt_boars",
                runtimeKind = "Work",
                heroId = "ren",
                plannedCycleCount = 2
            });
            Assert.That(started.success, Is.True);
            setup.Clock.UtcNowSeconds = 1_010L;

            var report = Run(setup.State, new FixedRandom());
            var execution = setup.State.GetActivityExecution(started.executionId);

            Assert.That(report.Success, Is.True);
            Assert.That(report.Danger.Completed, Is.EqualTo(1));
            Assert.That(execution.linkedCombat, Is.Not.Null);
            Assert.That(execution.linkedCombat.loot, Has.Length.EqualTo(5));
            Assert.That(execution.activityBagResolved, Is.True);
            Assert.That(execution.pendingResultId, Is.Null.Or.Empty);
            Assert.That(setup.State.PendingResults.GetAll(), Is.Empty);
            Assert.That(setup.State.IsHeroBusy("ren"), Is.True);
            Assert.That(setup.State.GetCombatAggregates(), Is.Empty);
        }

        [Test]
        public void WorkProcessesMultipleCyclesAndLeavesPartialNextCycleWithPriorRewardsAndEffects()
        {
            var setup = ActivitySetup();
            using var runtime = new ActivityRuntimeService(
                setup.State,
                new PlayerStateActivityAdapter(setup.State));
            var started = runtime.Start(new ActivityStartRequest
            {
                activityId = "work_pine_wood",
                runtimeKind = "Work",
                heroId = "ren",
                plannedCycleCount = 5
            });
            Assert.That(started.success, Is.True);
            setup.Clock.UtcNowSeconds = 1_035L;

            var report = Run(setup.State, new FixedRandom());
            var execution = setup.State.GetActivityExecution(started.executionId);
            var bag = setup.State.PendingResults.GetAll()[0];

            Assert.That(report.Success, Is.True);
            Assert.That(report.Work.Partial, Is.EqualTo(1));
            Assert.That(execution.completedCycles, Is.EqualTo(3));
            Assert.That(execution.elapsedSeconds, Is.EqualTo(5f));
            Assert.That(execution.currentCycleFatiguePaid, Is.True);
            Assert.That(EntryQuantity(bag, "Resource", "resource_pine_wood"), Is.EqualTo(4));
            Assert.That(EntryQuantity(bag, "SkillExp", "skill_gathering"), Is.EqualTo(3));
            Assert.That(setup.State.GetHeroEffectCounter("ren", "test_reliable_hands_effect"), Is.EqualTo(3));
        }

        [Test]
        public void WorkFatigueBoundaryCommitsCompletedCycleAndTimestampAsSuccessfulTerminalStop()
        {
            var setup = ActivitySetup();
            using var runtime = new ActivityRuntimeService(
                setup.State,
                new PlayerStateActivityAdapter(setup.State));
            var started = runtime.Start(new ActivityStartRequest
            {
                activityId = "work_pine_wood",
                runtimeKind = "Work",
                heroId = "ren",
                plannedCycleCount = 3
            });
            Assert.That(started.success, Is.True);
            Assert.That(setup.State.SpendHeroFatigue("ren", setup.State.GetHeroFatigue("ren")), Is.True);
            setup.Clock.UtcNowSeconds = 1_030L;

            var report = Run(setup.State, new FixedRandom());
            var execution = setup.State.GetActivityExecution(started.executionId);

            Assert.That(report.Success, Is.True);
            Assert.That(report.Code, Is.EqualTo(OfflineCoordinatorCode.Applied));
            Assert.That(report.Work.Completed, Is.EqualTo(1));
            Assert.That(execution.completedCycles, Is.EqualTo(1));
            Assert.That(execution.endReason, Is.EqualTo("InsufficientFatigue"));
            Assert.That(execution.currentCycleFatiguePaid, Is.False);
            Assert.That(setup.State.ToSaveData().timeProgress.lastProcessedUtcSeconds, Is.EqualTo(1_030L));
            Assert.That(setup.State.PendingResults.GetAll(), Has.Length.EqualTo(1));
        }

        [Test]
        public void PausedConstructionStaysPausedWhileReleasedHeroRecovers()
        {
            var setup = ActivitySetup();
            var runtime = new ActivityRuntimeService(
                setup.State,
                new PlayerStateActivityAdapter(setup.State));
            var started = runtime.Start(new ActivityStartRequest
            {
                activityId = "test_build_empty",
                runtimeKind = "Build",
                heroId = "ren"
            });
            Assert.That(started.success, Is.True);
            Assert.That(runtime.PauseConstruction(started.executionId).success, Is.True);
            runtime.Dispose();
            var before = setup.State.GetActivityExecution(started.executionId);
            var fatigue = setup.State.GetHeroFatigue("ren");
            setup.Clock.UtcNowSeconds = 1_060L;

            var report = Run(setup.State);
            var after = setup.State.GetActivityExecution(started.executionId);

            Assert.That(report.Success, Is.True);
            Assert.That(report.Construction.Attempted, Is.Zero);
            Assert.That(after.status, Is.EqualTo(ActivityRuntimeStatus.Paused));
            Assert.That(after.accumulatedBuildPoints, Is.EqualTo(before.accumulatedBuildPoints));
            Assert.That(setup.State.GetHeroFatigue("ren"), Is.EqualTo(fatigue + 1));
        }

        [Test]
        public void ConstructionCompletionCommitsLevelResultAndPostCommitEventOnce()
        {
            var setup = ActivitySetup();
            var events = new List<ActivityRuntimeEvent>();
            var runtime = new ActivityRuntimeService(
                setup.State,
                new PlayerStateActivityAdapter(setup.State));
            var started = runtime.Start(new ActivityStartRequest
            {
                activityId = "test_build_empty",
                runtimeKind = "Build",
                heroId = "ren"
            });
            runtime.Dispose();
            Assert.That(started.success, Is.True);
            setup.Clock.UtcNowSeconds = 1_010L;

            var report = Run(setup.State, activityEvents: events.Add);

            Assert.That(report.Success, Is.True);
            Assert.That(report.Construction.Completed, Is.EqualTo(1));
            Assert.That(setup.State.GetBuildingLevel("building_hall"), Is.EqualTo(1));
            Assert.That(events, Has.Some.Matches<ActivityRuntimeEvent>(runtimeEvent =>
                runtimeEvent.eventType == ActivityRuntimeEventType.BuildingLevelChanged));
            Assert.That(report.DeferredEventCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(report.PublishedEventCount, Is.EqualTo(report.DeferredEventCount));
        }

        [Test]
        public void ConstructionPartialPersistsFractionalProgressWithoutResultOrEventAndSavesOnce()
        {
            var setup = ActivitySetup();
            Seed(setup.State, "resource_pine_wood", 2);
            Seed(setup.State, "resource_stone", 2);
            setup.State.UnlockBuilding("building_campfire");
            Assert.That(setup.State.SetBuildingLevel("building_campfire", 0), Is.True);
            using var runtime = new ActivityRuntimeService(
                setup.State,
                new PlayerStateActivityAdapter(setup.State));
            var started = runtime.Start(new ActivityStartRequest
            {
                activityId = "test_build_campfire",
                runtimeKind = "Build",
                heroId = "ren"
            });
            Assert.That(started.success, Is.True);
            var saves = setup.Storage.SaveCalls;
            var events = new List<ActivityRuntimeEvent>();
            setup.Clock.UtcNowSeconds = 1_001L;

            var report = Run(setup.State, activityEvents: events.Add);
            var execution = setup.State.GetActivityExecution(started.executionId);

            Assert.That(report.Success, Is.True);
            Assert.That(report.Construction.Partial, Is.EqualTo(1));
            Assert.That(execution.accumulatedBuildPoints, Is.EqualTo(1f));
            Assert.That(execution.status, Is.EqualTo(ActivityRuntimeStatus.Running));
            Assert.That(setup.State.GetBuildingLevel("building_campfire"), Is.Zero);
            Assert.That(setup.State.PendingResults.GetAll(), Is.Empty);
            Assert.That(events, Is.Empty);
            Assert.That(setup.Storage.SaveCalls, Is.EqualTo(saves + 1));
        }

        [Test]
        public void ConstructionFailureRollsBackEarlierWorkProgressAndDoesNotSave()
        {
            var setup = ActivitySetup();
            Assert.That(setup.State.AddHero("test_builder_hero"), Is.True);
            using (var runtime = new ActivityRuntimeService(
                       setup.State,
                       new PlayerStateActivityAdapter(setup.State)))
            {
                Assert.That(runtime.Start(new ActivityStartRequest
                {
                    activityId = "test_build_empty",
                    runtimeKind = "Build",
                    heroId = "ren"
                }).success, Is.True);
            }
            AddRunningWorkExecution(setup.State, "work-before-construction", "test_builder_hero", 1);
            Assert.That(RuntimeConfigs.Formulas.TryGetFormula("test_build_points", out var formula), Is.True);
            var before = JsonUtility.ToJson(setup.State.ToSaveData());
            var saves = setup.Storage.SaveCalls;
            setup.Clock.UtcNowSeconds = 1_010L;

            OfflineCoordinatorReport report;
            formula.enabled = false;
            try
            {
                report = Run(setup.State, new FixedRandom());
            }
            finally
            {
                formula.enabled = true;
            }

            Assert.That(report.Code, Is.EqualTo(OfflineCoordinatorCode.ProcessorFailed));
            Assert.That(report.FailedStage, Is.EqualTo(OfflineCoordinatorStage.Construction));
            Assert.That(report.Work.Attempted, Is.EqualTo(1));
            Assert.That(JsonUtility.ToJson(setup.State.ToSaveData()), Is.EqualTo(before));
            Assert.That(setup.Storage.SaveCalls, Is.EqualTo(saves));
        }

        [Test]
        public void CraftCompletionWithPostCommitSinkFailureKeepsCommittedStateAndReportsError()
        {
            var setup = CraftSetup();
            Seed(setup.State, "resource_rabbit_meat", 3);
            Seed(setup.State, "resource_herb", 1);
            var runtime = new CraftRuntimeService(
                RuntimeConfigs.Crafts,
                new PlayerStateCraftAdapter(setup.State));
            var started = runtime.Start(new CraftStartRequest
            {
                CraftId = "craft_basic",
                HeroId = "ren",
                StationBuildingId = "building_campfire",
                StationBuildingLevel = 1,
                OperationKey = "offline-craft-start"
            });
            Assert.That(started.Success, Is.True);
            setup.Clock.UtcNowSeconds = 1_010L;

            var report = Run(
                setup.State,
                craftEvents: _ => throw new InvalidOperationException("expected sink failure"));

            Assert.That(report.Success, Is.True);
            Assert.That(report.Code, Is.EqualTo(OfflineCoordinatorCode.AppliedWithPostCommitErrors));
            Assert.That(report.StateCommitted, Is.True);
            Assert.That(report.Craft.Completed, Is.EqualTo(1));
            Assert.That(report.FailedEventCount, Is.EqualTo(1));
            Assert.That(setup.State.GetCraftExecution(started.ExecutionId).status,
                Is.EqualTo(CraftExecutionStatus.ResultPending));
            Assert.That(setup.State.ToSaveData().timeProgress.lastProcessedUtcSeconds, Is.EqualTo(1_010L));
        }

        [Test]
        public void CraftPartialAdvancesWithoutRepayingCostsOrCreatingResultAndKeepsHeroBusy()
        {
            var setup = CraftSetup();
            Seed(setup.State, "resource_rabbit_meat", 3);
            Seed(setup.State, "resource_herb", 1);
            var runtime = new CraftRuntimeService(
                RuntimeConfigs.Crafts,
                new PlayerStateCraftAdapter(setup.State));
            var started = runtime.Start(new CraftStartRequest
            {
                CraftId = "craft_basic",
                HeroId = "ren",
                StationBuildingId = "building_campfire",
                StationBuildingLevel = 1,
                OperationKey = "offline-craft-partial"
            });
            Assert.That(started.Success, Is.True);
            var fatigue = setup.State.GetHeroFatigue("ren");
            var meat = setup.State.GetItem("resource_rabbit_meat");
            var herb = setup.State.GetItem("resource_herb");
            var events = new List<CraftResultPendingEvent>();
            setup.Clock.UtcNowSeconds = 1_004L;

            var report = Run(setup.State, craftEvents: events.Add);
            var execution = setup.State.GetCraftExecution(started.ExecutionId);

            Assert.That(report.Success, Is.True);
            Assert.That(report.Craft.Partial, Is.EqualTo(1));
            Assert.That(execution.progressSeconds, Is.EqualTo(4f));
            Assert.That(execution.status, Is.EqualTo(CraftExecutionStatus.Running));
            Assert.That(setup.State.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(setup.State.GetItem("resource_rabbit_meat"), Is.EqualTo(meat));
            Assert.That(setup.State.GetItem("resource_herb"), Is.EqualTo(herb));
            Assert.That(setup.State.PendingResults.GetAll(), Is.Empty);
            Assert.That(events, Is.Empty);
            Assert.That(setup.State.IsHeroBusy("ren"), Is.True);
        }

        [Test]
        public void CraftFailureRollsBackProgressTimestampFatigueAndPendingStateWithoutSave()
        {
            var setup = CraftSetup();
            Seed(setup.State, "resource_rabbit_meat", 3);
            Seed(setup.State, "resource_herb", 1);
            var runtime = new CraftRuntimeService(
                RuntimeConfigs.Crafts,
                new PlayerStateCraftAdapter(setup.State));
            Assert.That(runtime.Start(new CraftStartRequest
            {
                CraftId = "craft_invalid_reward",
                HeroId = "ren",
                StationBuildingId = "building_campfire",
                StationBuildingLevel = 1,
                OperationKey = "offline-invalid-craft"
            }).Success, Is.True);
            var before = JsonUtility.ToJson(setup.State.ToSaveData());
            var saves = setup.Storage.SaveCalls;
            setup.Clock.UtcNowSeconds = 1_010L;

            var report = Run(setup.State);

            Assert.That(report.Code, Is.EqualTo(OfflineCoordinatorCode.ProcessorFailed));
            Assert.That(report.FailedStage, Is.EqualTo(OfflineCoordinatorStage.Craft));
            Assert.That(report.StateCommitted, Is.False);
            Assert.That(JsonUtility.ToJson(setup.State.ToSaveData()), Is.EqualTo(before));
            Assert.That(setup.Storage.SaveCalls, Is.EqualTo(saves));
        }

        [Test]
        public void FullStorageDoesNotBlockOfflineWorkOrCraftPendingResults()
        {
            var work = ActivitySetup();
            FillStorage(work.State, "resource_pine_wood", 2_000);
            using (var runtime = new ActivityRuntimeService(
                       work.State,
                       new PlayerStateActivityAdapter(work.State)))
            {
                Assert.That(runtime.Start(new ActivityStartRequest
                {
                    activityId = "work_pine_wood",
                    runtimeKind = "Work",
                    heroId = "ren",
                    plannedCycleCount = 1
                }).success, Is.True);
            }
            work.Clock.UtcNowSeconds = 1_010L;

            var workReport = Run(work.State, new FixedRandom());

            Assert.That(workReport.Success, Is.True);
            Assert.That(work.State.Storage.GetSnapshot().FreeSlots, Is.Zero);
            Assert.That(work.State.PendingResults.GetAll(), Has.Length.EqualTo(1));
            Assert.That(work.State.GetItem("resource_pine_wood"), Is.EqualTo(2_000));

            var construction = ActivitySetup();
            Seed(construction.State, "resource_pine_wood", 2);
            Seed(construction.State, "resource_stone", 2);
            construction.State.UnlockBuilding("building_campfire");
            Assert.That(construction.State.SetBuildingLevel("building_campfire", 0), Is.True);
            string buildExecutionId;
            using (var runtime = new ActivityRuntimeService(
                       construction.State,
                       new PlayerStateActivityAdapter(construction.State)))
            {
                var started = runtime.Start(new ActivityStartRequest
                {
                    activityId = "test_build_campfire",
                    runtimeKind = "Build",
                    heroId = "ren"
                });
                Assert.That(started.success, Is.True);
                buildExecutionId = started.executionId;
            }
            FillStorage(construction.State, "resource_pine_wood", 2_000);
            construction.Clock.UtcNowSeconds = 1_002L;

            var constructionReport = Run(construction.State);

            Assert.That(constructionReport.Success, Is.True);
            Assert.That(construction.State.Storage.GetSnapshot().FreeSlots, Is.Zero);
            Assert.That(construction.State.GetBuildingLevel("building_campfire"), Is.EqualTo(1));
            Assert.That(construction.State.GetActivityExecution(buildExecutionId).status,
                Is.EqualTo(ActivityRuntimeStatus.ResultPending));
            Assert.That(construction.State.PendingResults.GetAll(), Has.Length.EqualTo(1));

            var craft = CraftSetup();
            Seed(craft.State, "resource_rabbit_meat", 3);
            Seed(craft.State, "resource_herb", 1);
            var craftRuntime = new CraftRuntimeService(
                RuntimeConfigs.Crafts,
                new PlayerStateCraftAdapter(craft.State));
            Assert.That(craftRuntime.Start(new CraftStartRequest
            {
                CraftId = "craft_basic",
                HeroId = "ren",
                StationBuildingId = "building_campfire",
                StationBuildingLevel = 1,
                OperationKey = "offline-full-storage-craft"
            }).Success, Is.True);
            FillStorage(craft.State, "resource_herb", 2);
            craft.Clock.UtcNowSeconds = 1_010L;

            var craftReport = Run(craft.State);

            Assert.That(craftReport.Success, Is.True);
            Assert.That(craft.State.Storage.GetSnapshot().FreeSlots, Is.Zero);
            Assert.That(craft.State.PendingResults.GetAll(), Has.Length.EqualTo(1));
            Assert.That(craft.State.GetItem("consumable_roasted_rabbit_meat"), Is.Zero);
        }

        [Test]
        public void BusySnapshotSkipsRecoveryUntilNextPassAndDiffersFromOneCombinedPass()
        {
            var split = ActivitySetup();
            using (var runtime = new ActivityRuntimeService(
                       split.State,
                       new PlayerStateActivityAdapter(split.State)))
            {
                Assert.That(runtime.Start(new ActivityStartRequest
                {
                    activityId = "empty_repeat",
                    runtimeKind = "Work",
                    heroId = "ren",
                    plannedCycleCount = 1
                }).success, Is.True);
            }
            Assert.That(split.State.SpendHeroFatigue("ren", 3), Is.True);
            var fatigueBefore = split.State.GetHeroFatigue("ren");
            split.Clock.UtcNowSeconds = 1_060L;
            var first = Run(split.State, new FixedRandom());
            split.Clock.UtcNowSeconds = 1_120L;
            var second = Run(split.State, new FixedRandom());

            Assert.That(first.Success, Is.True);
            Assert.That(first.Fatigue.EligibleHeroCount, Is.Zero);
            Assert.That(split.State.IsHeroBusy("ren"), Is.False);
            Assert.That(second.Fatigue.RestoredFatigue, Is.EqualTo(1));
            Assert.That(split.State.GetHeroFatigue("ren"), Is.EqualTo(fatigueBefore + 1));

            var combined = ActivitySetup();
            using (var runtime = new ActivityRuntimeService(
                       combined.State,
                       new PlayerStateActivityAdapter(combined.State)))
            {
                Assert.That(runtime.Start(new ActivityStartRequest
                {
                    activityId = "empty_repeat",
                    runtimeKind = "Work",
                    heroId = "ren",
                    plannedCycleCount = 1
                }).success, Is.True);
            }
            Assert.That(combined.State.SpendHeroFatigue("ren", 3), Is.True);
            var combinedFatigue = combined.State.GetHeroFatigue("ren");
            combined.Clock.UtcNowSeconds = 1_120L;

            var onePass = Run(combined.State, new FixedRandom());

            Assert.That(onePass.Success, Is.True);
            Assert.That(onePass.Fatigue.RestoredFatigue, Is.Zero);
            Assert.That(combined.State.GetHeroFatigue("ren"), Is.EqualTo(combinedFatigue));
        }

        [Test]
        public void PostCommitFailureDoesNotStopLaterEventsAndKeepsStageExecutionLocalOrder()
        {
            var setup = ActivitySetup();
            Assert.That(setup.State.AddHero("test_builder_hero"), Is.True);
            string constructionExecutionId;
            using (var runtime = new ActivityRuntimeService(
                       setup.State,
                       new PlayerStateActivityAdapter(setup.State)))
            {
                var construction = runtime.Start(new ActivityStartRequest
                {
                    activityId = "test_build_empty",
                    runtimeKind = "Build",
                    heroId = "ren"
                });
                Assert.That(construction.success, Is.True);
                constructionExecutionId = construction.executionId;
            }
            const string workExecutionId = "work-empty-before-construction";
            AddRunningWorkExecution(
                setup.State,
                workExecutionId,
                "test_builder_hero",
                1,
                "empty_repeat");
            var order = new List<string>();
            void OnResolved(PendingResultResolvedEvent resolved) =>
                order.Add($"resolved:{resolved.SourceExecutionId}");
            setup.State.PendingResults.Resolved += OnResolved;
            var saves = setup.Storage.SaveCalls;
            setup.Clock.UtcNowSeconds = 1_010L;

            OfflineCoordinatorReport report;
            try
            {
                report = Run(
                    setup.State,
                    new FixedRandom(),
                    runtimeEvent =>
                    {
                        order.Add($"activity:{runtimeEvent.eventType}");
                        if (runtimeEvent.eventType == ActivityRuntimeEventType.BuildingLevelChanged)
                            throw new InvalidOperationException("expected ordered sink failure");
                    });
            }
            finally
            {
                setup.State.PendingResults.Resolved -= OnResolved;
            }

            Assert.That(report.Code, Is.EqualTo(OfflineCoordinatorCode.AppliedWithPostCommitErrors));
            Assert.That(report.StateCommitted, Is.True);
            Assert.That(report.FailedEventCount, Is.EqualTo(1));
            Assert.That(report.AttemptedEventCount, Is.EqualTo(report.PublishedEventCount + 1));
            Assert.That(setup.Storage.SaveCalls, Is.EqualTo(saves + 1));
            Assert.That(setup.State.GetActivityExecution(workExecutionId), Is.Null);
            Assert.That(setup.State.GetActivityExecution(constructionExecutionId), Is.Null);
            Assert.That(order[0], Is.EqualTo($"resolved:{workExecutionId}"));
            Assert.That(order, Does.Contain($"activity:{ActivityRuntimeEventType.BuildingLevelChanged}"));
            Assert.That(order[order.Count - 1], Is.EqualTo($"activity:{ActivityRuntimeEventType.ActivityCompleted}"));
        }

        [Test]
        public void OfflineDangerThenRepeatedOnlineRuntimeCreationStartsOneCombatWithoutExtraFatigueOrSubscriptions()
        {
            var database = PlayerRuntimeCompositionTests.CreateConstructionProgressionDatabase();
            var setup = Setup(database, addActivityHero: false);
            Assert.That(setup.State.AddHero("ren"), Is.True);
            Assert.That(setup.State.Save(), Is.True);
            var executionId = PrepareOfflineDanger(setup, "hunt_rabbits");
            var fatigue = setup.State.GetHeroFatigue("ren");
            var requestId = setup.State.GetActivityExecution(executionId).linkedCombat.requestId;
            var startedEvents = 0;
            var resolvedEvents = 0;
            Action<CombatStartedEvent> onStarted = _ => startedEvents++;
            void OnResolved(PendingResultResolvedEvent _) => resolvedEvents++;
            PlayerRuntimeComposition.CombatStarted += onStarted;
            setup.State.PendingResults.Resolved += OnResolved;
            try
            {
                for (var index = 0; index < 3; index++)
                {
                    using var runtime = PlayerRuntimeComposition.CreateRuntimeService(setup.State);
                    Assert.That(runtime.GetPendingLinkedCombatStarts()[0].requestId, Is.EqualTo(requestId));
                }

                Assert.That(setup.State.GetCombatAggregates(), Has.Length.EqualTo(1));
                var linked = setup.State.GetActivityExecution(executionId).linkedCombat;
                Assert.That(linked.combatExecutionId, Is.Not.Null.And.Not.Empty);
                Assert.That(setup.State.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
                Assert.That(startedEvents, Is.EqualTo(1));

                var bag = setup.State.PendingResults.GetAll()[0];
                var first = setup.State.PendingResults.ClaimAll(
                    "offline-online-bag",
                    bag.resultId,
                    bag.revision,
                    setup.State.Storage.GetSnapshot().Revision);
                var replay = setup.State.PendingResults.ClaimAll(
                    "offline-online-bag",
                    bag.resultId,
                    bag.revision,
                    setup.State.Storage.GetSnapshot().Revision);
                Assert.That(first.Success, Is.True);
                Assert.That(replay.Success, Is.True);
                Assert.That(replay.Replayed, Is.True);
                Assert.That(resolvedEvents, Is.EqualTo(1));
                Assert.That(setup.State.GetCombatAggregates(), Has.Length.EqualTo(1));
            }
            finally
            {
                setup.State.PendingResults.Resolved -= OnResolved;
                PlayerRuntimeComposition.CombatStarted -= onStarted;
            }
        }

        [Test]
        public void OfflineDangerSaveLoadReplayAtSameAndNewTimestampDoesNotDuplicateState()
        {
            var database = PlayerRuntimeCompositionTests.CreateConstructionProgressionDatabase();
            var setup = Setup(database, addActivityHero: false);
            Assert.That(setup.State.AddHero("ren"), Is.True);
            Assert.That(setup.State.Save(), Is.True);
            var executionId = PrepareOfflineDanger(setup, "hunt_rabbits");
            var savedExecution = setup.State.GetActivityExecution(executionId);
            var requestId = savedExecution.linkedCombat.requestId;
            var completedCycles = savedExecution.completedCycles;
            var fatigue = setup.State.GetHeroFatigue("ren");
            var bagJson = JsonUtility.ToJson(setup.State.PendingResults.GetAll()[0]);

            var restored = SaveService.Load(setup.Factory, setup.Storage);
            var sameTime = Run(restored, new FixedRandom());
            setup.Clock.UtcNowSeconds = 1_020L;
            var newTime = Run(restored, new FixedRandom());
            var replayedExecution = restored.GetActivityExecution(executionId);

            Assert.That(sameTime.Code, Is.EqualTo(OfflineCoordinatorCode.NoElapsedTime));
            Assert.That(newTime.Code, Is.EqualTo(OfflineCoordinatorCode.Applied));
            Assert.That(newTime.Danger.NoOp, Is.EqualTo(1));
            Assert.That(replayedExecution.completedCycles, Is.EqualTo(completedCycles));
            Assert.That(replayedExecution.linkedCombat.requestId, Is.EqualTo(requestId));
            Assert.That(JsonUtility.ToJson(restored.PendingResults.GetAll()[0]), Is.EqualTo(bagJson));
            Assert.That(restored.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(restored.GetCombatAggregates(), Is.Empty);

            using (PlayerRuntimeComposition.CreateRuntimeService(restored))
            {
                Assert.That(restored.GetCombatAggregates(), Has.Length.EqualTo(1));
            }
            Assert.That(restored.Save(), Is.True);
            var withCombat = SaveService.Load(setup.Factory, setup.Storage);
            using (PlayerRuntimeComposition.CreateRuntimeService(withCombat))
            {
                Assert.That(withCombat.GetCombatAggregates(), Has.Length.EqualTo(1));
                Assert.That(withCombat.GetActivityExecution(executionId).linkedCombat.requestId, Is.EqualTo(requestId));
            }
        }

        [Test]
        public void CoordinatorBatchingMatchesTransactionAwareWorkConstructionAndCraftCores()
        {
            var coordinatedWork = ActivitySetup();
            var directWork = ActivitySetup();
            var coordinatedWorkId = StartWork(coordinatedWork.State, "work_pine_wood", 4);
            var directWorkId = StartWork(directWork.State, "work_pine_wood", 4);
            coordinatedWork.Clock.UtcNowSeconds = 1_025L;
            var workReport = Run(coordinatedWork.State, new FixedRandom());
            using (var workCore = new WorkAdvanceProcessor(
                       directWork.State,
                       new PlayerStateActivityAdapter(directWork.State),
                       new FixedRandom()))
            {
                Assert.That(workCore.Advance(new WorkAdvanceRequest(directWorkId, 25)).Success, Is.True);
            }
            AssertActivityExecutionParity(
                coordinatedWork.State.GetActivityExecution(coordinatedWorkId),
                directWork.State.GetActivityExecution(directWorkId));
            var coordinatedBag = coordinatedWork.State.PendingResults.GetAll()[0];
            var directBag = directWork.State.PendingResults.GetAll()[0];
            Assert.That(EntryQuantity(coordinatedBag, "Resource", "resource_pine_wood"),
                Is.EqualTo(EntryQuantity(directBag, "Resource", "resource_pine_wood")));
            Assert.That(EntryQuantity(coordinatedBag, "SkillExp", "skill_gathering"),
                Is.EqualTo(EntryQuantity(directBag, "SkillExp", "skill_gathering")));
            Assert.That(workReport.Fatigue.RestoredFatigue, Is.Zero);

            var coordinatedBuild = ActivitySetup();
            var directBuild = ActivitySetup();
            var coordinatedBuildId = StartEmptyConstruction(coordinatedBuild.State);
            var directBuildId = StartEmptyConstruction(directBuild.State);
            coordinatedBuild.Clock.UtcNowSeconds = 1_001L;
            Assert.That(Run(coordinatedBuild.State).Success, Is.True);
            using (var buildCore = PlayerRuntimeComposition.CreateConstructionAdvanceProcessor(directBuild.State))
                Assert.That(buildCore.Advance(new ConstructionAdvanceRequest(directBuildId, 1)).Success, Is.True);
            Assert.That(coordinatedBuild.State.GetBuildingLevel("building_hall"),
                Is.EqualTo(directBuild.State.GetBuildingLevel("building_hall")));
            Assert.That(coordinatedBuild.State.IsActivityCompleted("test_build_empty"),
                Is.EqualTo(directBuild.State.IsActivityCompleted("test_build_empty")));

            var coordinatedCraft = CraftSetup();
            var directCraft = CraftSetup();
            var coordinatedCraftId = StartBasicCraft(coordinatedCraft.State, "coordinated-parity");
            var directCraftId = StartBasicCraft(directCraft.State, "direct-parity");
            coordinatedCraft.Clock.UtcNowSeconds = 1_004L;
            Assert.That(Run(coordinatedCraft.State).Success, Is.True);
            var craftCore = new CraftAdvanceProcessor(new PlayerStateCraftAdapter(directCraft.State));
            Assert.That(craftCore.Advance(new CraftAdvanceRequest(directCraftId, 4d)).Success, Is.True);
            Assert.That(coordinatedCraft.State.GetCraftExecution(coordinatedCraftId).progressSeconds,
                Is.EqualTo(directCraft.State.GetCraftExecution(directCraftId).progressSeconds));
            Assert.That(coordinatedCraft.State.GetHeroFatigue("ren"),
                Is.EqualTo(directCraft.State.GetHeroFatigue("ren")));
        }

        [Test]
        public void WorstCaseBoundedRuntimeFixtureRemainsBelowSerializedSaveLimit()
        {
            var setup = ActivitySetup();
            Assert.That(setup.State.AddHero("test_builder_hero"), Is.True);

            var save = setup.State.ToSaveData();
            save.activityRuntime = new ActivityRuntimeSaveData
            {
                executions = new ActivityExecutionSaveData[8]
            };
            for (var index = 0; index < save.activityRuntime.executions.Length; index++)
            {
                save.activityRuntime.executions[index] = new ActivityExecutionSaveData
                {
                    executionId = $"bounded-paused-activity-{index}",
                    activityId = "work_pine_wood",
                    runtimeKind = "Work",
                    status = ActivityRuntimeStatus.Paused,
                    plannedCycles = 10,
                    completedCycles = index,
                    elapsedSeconds = 5f,
                    currentCycleFatiguePaid = true,
                    cyclePhase = "Running",
                    stagedRewards = Array.Empty<ActivityStagedRewardSaveData>(),
                    startedAtUnixSeconds = 1_000L + index
                };
            }
            save.operationReceipts = new OperationReceiptSaveData[ReceiptRetentionLimit];
            for (var index = 0; index < save.operationReceipts.Length; index++)
            {
                save.operationReceipts[index] = new OperationReceiptSaveData
                {
                    aggregateId = "combat-start",
                    operationId = $"bounded-operation-{index:D2}",
                    fingerprint = $"bounded-fingerprint-{index:D2}-0123456789abcdef",
                    success = true,
                    code = "Applied",
                    storageRevision = index + 1L,
                    executionId = $"bounded-receipt-combat-{index:D2}",
                    resultPayload = $"{{\"executionId\":\"bounded-receipt-combat-{index:D2}\",\"sessionId\":\"bounded-receipt-session-{index:D2}\"}}"
                };
            }
            save.resultSources = new PendingResultSourceReferenceSaveData[ResolvedResultSourceRetentionLimit];
            for (var index = 0; index < save.resultSources.Length; index++)
            {
                var executionId = $"bounded-resolved-activity-{index:D2}";
                save.resultSources[index] = new PendingResultSourceReferenceSaveData
                {
                    sourceType = PendingResultSourceType.Activity,
                    sourceId = "work_pine_wood",
                    sourceExecutionId = executionId,
                    resultId = $"result:{PendingResultSourceType.Activity}:{executionId}",
                    state = PendingResultSourceState.Resolved,
                    resolutionSequence = index + 1L
                };
            }
            save.itemStacks = new ItemStackSaveData[StorageCapacity];
            for (var index = 0; index < save.itemStacks.Length; index++)
            {
                save.itemStacks[index] = new ItemStackSaveData
                {
                    stackId = $"bounded-storage-stack-{index:D2}",
                    itemId = "resource_pine_wood",
                    quantity = 100,
                    stateId = "on_storage"
                };
            }

            var combats = CreateBoundedCombatFixtures(PersistentCombatAggregateLimit);
            AssertRunningCombatCollectionsAreMaxFilled(combats[0]);
            AssertTerminalCombatCollectionsAreMaxFilled(combats[1]);
            save.combatRuntime = new CombatRuntimeSaveData
            {
                executions = new CombatExecutionSaveData[combats.Length],
                sessions = new CombatSessionSaveData[combats.Length]
            };
            for (var index = 0; index < combats.Length; index++)
            {
                save.combatRuntime.executions[index] = combats[index].execution;
                save.combatRuntime.sessions[index] = combats[index].session;
            }

            var bounded = setup.Factory.Create(save);
            var boundedSave = bounded.ToSaveData();
            var json = JsonUtility.ToJson(boundedSave);
            var bytes = Encoding.UTF8.GetByteCount(json);
            TestContext.WriteLine($"Full bounded worst-case save size: {bytes} / {SaveSizeLimitBytes} bytes.");
            TestContext.WriteLine($"Combat runtime size: {Encoding.UTF8.GetByteCount(JsonUtility.ToJson(boundedSave.combatRuntime))} bytes.");

            var persistedCombats = bounded.GetCombatAggregates();
            var unfinishedCount = 0;
            var compactCompletedCount = 0;
            CombatRuntimeAggregate heavy = null;
            foreach (var aggregate in persistedCombats)
            {
                if (aggregate.execution.status != CombatExecutionStatus.Completed ||
                    (aggregate.execution.resultCreated && !aggregate.execution.pendingResultResolved))
                {
                    unfinishedCount++;
                    heavy = aggregate;
                    continue;
                }

                AssertCompletedCombatIsCompacted(aggregate);
                compactCompletedCount++;
            }

            Assert.That(boundedSave.heroes, Has.Length.EqualTo(2));
            Assert.That(bounded.GetActivityExecutions(), Has.Length.EqualTo(8));
            Assert.That(bounded.GetCraftExecutions(), Is.Empty);
            Assert.That(persistedCombats, Has.Length.EqualTo(PersistentCombatAggregateLimit));
            Assert.That(
                ActiveHeroLimitResolver.GetCurrentLimit(new PlayerStateActivityAdapter(bounded)),
                Is.EqualTo(1),
                "The production active-hero limit permits only one unfinished heavy execution.");
            Assert.That(unfinishedCount, Is.EqualTo(1));
            Assert.That(compactCompletedCount, Is.EqualTo(PersistentCombatAggregateLimit - 1));
            Assert.That(heavy, Is.Not.Null);
            AssertRunningCombatCollectionsAreMaxFilled(heavy);
            Assert.That(boundedSave.operationReceipts, Has.Length.EqualTo(ReceiptRetentionLimit));
            Assert.That(boundedSave.resultSources, Has.Length.EqualTo(ResolvedResultSourceRetentionLimit));
            Assert.That(boundedSave.resultSources, Has.All.Matches<PendingResultSourceReferenceSaveData>(source =>
                source != null && string.Equals(source.state, PendingResultSourceState.Resolved, StringComparison.Ordinal)));
            Assert.That(bounded.Storage.GetSnapshot().Capacity, Is.EqualTo(StorageCapacity));
            Assert.That(bounded.Storage.GetSnapshot().OccupiedSlots, Is.EqualTo(StorageCapacity));
            Assert.That(boundedSave.itemStacks, Has.Length.EqualTo(StorageCapacity));
            Assert.That(bytes, Is.LessThanOrEqualTo(SaveSizeLimitBytes));
        }

        [Test]
        public void ProcessingLimitAndSaveFailureBothRestoreWholePassAndRandomState()
        {
            var limited = ActivitySetup();
            var limitedRandom = new FixedRandom();
            var runtime = new ActivityRuntimeService(
                limited.State,
                new PlayerStateActivityAdapter(limited.State));
            var started = runtime.Start(new ActivityStartRequest
            {
                activityId = "work_pine_wood",
                runtimeKind = "Work",
                heroId = "ren",
                plannedCycleCount = 3
            });
            runtime.Dispose();
            Assert.That(started.success, Is.True);
            var beforeLimit = JsonUtility.ToJson(limited.State.ToSaveData());
            var randomBefore = limitedRandom.CaptureState().Value;
            limited.Clock.UtcNowSeconds = 1_030L;

            var limitReport = Run(limited.State, limitedRandom, workOperationLimit: 1);

            Assert.That(limitReport.Code, Is.EqualTo(OfflineCoordinatorCode.ProcessingLimitReached));
            Assert.That(limitReport.StateCommitted, Is.False);
            Assert.That(JsonUtility.ToJson(limited.State.ToSaveData()), Is.EqualTo(beforeLimit));
            Assert.That(limitedRandom.CaptureState().Value, Is.EqualTo(randomBefore));

            var failing = ActivitySetup();
            runtime = new ActivityRuntimeService(
                failing.State,
                new PlayerStateActivityAdapter(failing.State));
            started = runtime.Start(new ActivityStartRequest
            {
                activityId = "work_pine_wood",
                runtimeKind = "Work",
                heroId = "ren",
                plannedCycleCount = 1
            });
            runtime.Dispose();
            Assert.That(started.success, Is.True);
            var beforeSave = JsonUtility.ToJson(failing.State.ToSaveData());
            failing.Clock.UtcNowSeconds = 1_010L;
            failing.Storage.ThrowOnSave = true;
            LogAssert.Expect(LogType.Error,
                "[SaveService] Failed to save player state. Expected save failure.");

            var saveReport = Run(failing.State, new FixedRandom());

            Assert.That(saveReport.Code, Is.EqualTo(OfflineCoordinatorCode.SaveFailed));
            Assert.That(saveReport.StateCommitted, Is.False);
            Assert.That(JsonUtility.ToJson(failing.State.ToSaveData()), Is.EqualTo(beforeSave));
        }

        [Test]
        public void InvalidOccupationStopsBeforeProcessorsAndRunningCombatIsOtherwiseFrozen()
        {
            var invalid = ActivitySetup();
            Assert.That(invalid.State.SetHeroBusy("ren", "missing-owner"), Is.True);
            var before = JsonUtility.ToJson(invalid.State.ToSaveData());
            var saves = invalid.Storage.SaveCalls;
            invalid.Clock.UtcNowSeconds = 1_060L;

            var invalidReport = Run(invalid.State);

            Assert.That(invalidReport.Code, Is.EqualTo(OfflineCoordinatorCode.DataIntegrityFailure));
            Assert.That(invalidReport.FailedStage, Is.EqualTo(OfflineCoordinatorStage.Validation));
            Assert.That(JsonUtility.ToJson(invalid.State.ToSaveData()), Is.EqualTo(before));
            Assert.That(invalid.Storage.SaveCalls, Is.EqualTo(saves));

            var combat = ActivitySetup();
            var aggregate = CombatAggregate("combat-running", "combat-session", "ren");
            Assert.That(combat.State.AddCombatAggregate(aggregate), Is.True);
            var combatBefore = JsonUtility.ToJson(combat.State.ToSaveData().combatRuntime);
            combat.Clock.UtcNowSeconds = 1_060L;

            var combatReport = Run(combat.State);

            Assert.That(combatReport.Success, Is.True);
            Assert.That(JsonUtility.ToJson(combat.State.ToSaveData().combatRuntime), Is.EqualTo(combatBefore));
            Assert.That(combatReport.Work.Attempted, Is.Zero);
            Assert.That(combatReport.Construction.Attempted, Is.Zero);
            Assert.That(combatReport.Craft.Attempted, Is.Zero);
        }

        private static OfflineCoordinatorReport Run(
            PlayerState state,
            ITransactionalActivityRandom random = null,
            Action<ActivityRuntimeEvent> activityEvents = null,
            Action<CraftResultPendingEvent> craftEvents = null,
            int workOperationLimit = ActivityRuntimeService.DefaultWorkAdvanceOperationLimit)
        {
            using var coordinator = PlayerRuntimeComposition.CreateOfflineCoordinator(
                state,
                random ?? new FixedRandom(),
                activityEvents,
                craftEvents,
                diagnosticSink: _ => { },
                workOperationLimit: workOperationLimit);
            return coordinator.Run();
        }

        private static string PrepareOfflineDanger(TestSetup setup, string activityId)
        {
            using var runtime = new ActivityRuntimeService(
                setup.State,
                new PlayerStateActivityAdapter(setup.State));
            var started = runtime.Start(new ActivityStartRequest
            {
                activityId = activityId,
                runtimeKind = "Work",
                heroId = "ren",
                plannedCycleCount = 2
            });
            Assert.That(started.success, Is.True);
            setup.Clock.UtcNowSeconds = 1_010L;
            var report = Run(setup.State, new FixedRandom());
            Assert.That(report.Success, Is.True);
            Assert.That(report.Danger.Completed, Is.EqualTo(1));
            return started.executionId;
        }

        private static bool AddStartedLinkedCombat(PlayerState state, string sourceExecutionId)
        {
            var source = state.GetActivityExecution(sourceExecutionId);
            var request = source?.linkedCombat;
            if (request == null)
                return false;
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
                    startFingerprint = "offline-started-link-fixture",
                    status = CombatExecutionStatus.Running,
                    startedAtUnixSeconds = 1_010L
                },
                session = new CombatSessionSaveData
                {
                    sessionId = sessionId,
                    executionId = executionId,
                    enemyGroupId = request.enemyGroupId,
                    combatMode = request.combatMode,
                    enemyQueue = Array.Empty<CombatEnemyQueueEntrySaveData>(),
                    hero = Combatant($"{sessionId}:hero", request.heroId),
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

        private static TestSetup ActivitySetup()
        {
            var database = GuildIdle.Editor.Activities.ActivityRuntimeServiceTests.CreateDatabase();
            return Setup(database, addActivityHero: true);
        }

        private static TestSetup CraftSetup()
        {
            var database = GuildIdle.Editor.Crafting.CraftRuntimeServiceTests.CreateDatabase();
            return Setup(database, addActivityHero: false);
        }

        private static TestSetup Setup(ConfigDatabase database, bool addActivityHero)
        {
            RuntimeConfigs.SetDatabaseForTests(database);
            var clock = new FakeTimeProvider(1_000L);
            var factory = TestPlayerComposition.CreatePlayerStateFactory(database, timeProvider: clock);
            var storage = new MemorySaveStorage();
            var state = SaveService.Load(factory, storage);
            if (addActivityHero)
            {
                Assert.That(state.AddHero("ren"), Is.True);
                if (!state.IsBuildingUnlocked("building_warehouse"))
                    Assert.That(state.UnlockBuilding("building_warehouse"), Is.True);
                Assert.That(state.SetBuildingLevel("building_warehouse", 0), Is.True);
                Assert.That(state.Save(), Is.True);
            }
            return new TestSetup(database, clock, factory, storage, state);
        }

        private static void AddWorkClone(
            PlayerState state,
            ActivityExecutionSaveData template,
            string executionId,
            string heroId)
        {
            var clone = new ActivityExecutionSaveData
            {
                executionId = executionId,
                activityId = template.activityId,
                runtimeKind = template.runtimeKind,
                heroId = heroId,
                status = ActivityRuntimeStatus.Running,
                elapsedSeconds = 0f,
                completedCycles = 0,
                plannedCycles = 1,
                currentCycleFatiguePaid = true,
                cyclePhase = template.cyclePhase,
                stagedRewards = Array.Empty<ActivityStagedRewardSaveData>(),
                startedAtUnixSeconds = template.startedAtUnixSeconds
            };
            Assert.That(state.SetHeroBusy(heroId, executionId), Is.True);
            Assert.That(state.AddActivityExecution(clone), Is.True);
        }

        private static void AddRunningWorkExecution(
            PlayerState state,
            string executionId,
            string heroId,
            int plannedCycles,
            string activityId = "work_pine_wood")
        {
            Assert.That(state.SetHeroBusy(heroId, executionId), Is.True);
            Assert.That(state.AddActivityExecution(new ActivityExecutionSaveData
            {
                executionId = executionId,
                activityId = activityId,
                runtimeKind = "Work",
                heroId = heroId,
                status = ActivityRuntimeStatus.Running,
                elapsedSeconds = 0f,
                completedCycles = 0,
                plannedCycles = plannedCycles,
                currentCycleFatiguePaid = true,
                cyclePhase = "Running",
                stagedRewards = Array.Empty<ActivityStagedRewardSaveData>(),
                startedAtUnixSeconds = 1_000L
            }), Is.True);
        }

        private static string StartWork(PlayerState state, string activityId, int cycles)
        {
            using var runtime = new ActivityRuntimeService(
                state,
                new PlayerStateActivityAdapter(state));
            var started = runtime.Start(new ActivityStartRequest
            {
                activityId = activityId,
                runtimeKind = "Work",
                heroId = "ren",
                plannedCycleCount = cycles
            });
            Assert.That(started.success, Is.True);
            return started.executionId;
        }

        private static string StartEmptyConstruction(PlayerState state)
        {
            using var runtime = new ActivityRuntimeService(
                state,
                new PlayerStateActivityAdapter(state));
            var started = runtime.Start(new ActivityStartRequest
            {
                activityId = "test_build_empty",
                runtimeKind = "Build",
                heroId = "ren"
            });
            Assert.That(started.success, Is.True);
            return started.executionId;
        }

        private static string StartBasicCraft(PlayerState state, string operationKey)
        {
            Seed(state, "resource_rabbit_meat", 3);
            Seed(state, "resource_herb", 1);
            var runtime = new CraftRuntimeService(
                RuntimeConfigs.Crafts,
                new PlayerStateCraftAdapter(state));
            var started = runtime.Start(new CraftStartRequest
            {
                CraftId = "craft_basic",
                HeroId = "ren",
                StationBuildingId = "building_campfire",
                StationBuildingLevel = 1,
                OperationKey = operationKey
            });
            Assert.That(started.Success, Is.True);
            return started.ExecutionId;
        }

        private static void AssertActivityExecutionParity(
            ActivityExecutionSaveData coordinated,
            ActivityExecutionSaveData direct)
        {
            Assert.That(coordinated, Is.Not.Null);
            Assert.That(direct, Is.Not.Null);
            Assert.That(coordinated.activityId, Is.EqualTo(direct.activityId));
            Assert.That(coordinated.status, Is.EqualTo(direct.status));
            Assert.That(coordinated.completedCycles, Is.EqualTo(direct.completedCycles));
            Assert.That(coordinated.elapsedSeconds, Is.EqualTo(direct.elapsedSeconds));
            Assert.That(coordinated.currentCycleFatiguePaid, Is.EqualTo(direct.currentCycleFatiguePaid));
            Assert.That(coordinated.cyclePhase, Is.EqualTo(direct.cyclePhase));
        }

        private static void Seed(PlayerState state, string itemId, int quantity)
        {
            var result = state.Storage.Add(
                $"offline-seed:{itemId}:{Guid.NewGuid():N}",
                state.Storage.GetSnapshot().Revision,
                itemId,
                quantity);
            Assert.That(result.Success, Is.True, result.Message);
        }

        private static void FillStorage(PlayerState state, string itemId, int quantity)
        {
            var index = 0;
            while (state.Storage.GetSnapshot().FreeSlots > 0)
            {
                var result = state.Storage.Add(
                    $"offline-fill:{itemId}:{index++}",
                    state.Storage.GetSnapshot().Revision,
                    itemId,
                    quantity);
                Assert.That(result.Success, Is.True, result.Message);
            }
        }

        private static long EntryQuantity(
            PendingResultSaveData result,
            string rewardType,
            string targetId)
        {
            long quantity = 0;
            foreach (var entry in result?.entries ?? Array.Empty<PendingResultEntrySaveData>())
            {
                if (entry != null &&
                    string.Equals(entry.rewardType, rewardType, StringComparison.Ordinal) &&
                    string.Equals(entry.targetId, targetId, StringComparison.Ordinal))
                    quantity += entry.quantity;
            }
            return quantity;
        }

        private static int GetRemainder(PlayerState state, string heroId)
        {
            foreach (var entry in state.ToSaveData().timeProgress.fatigueRemainders)
                if (string.Equals(entry.heroId, heroId, StringComparison.Ordinal)) return entry.fatigueRemainderSeconds;
            Assert.Fail($"Missing fatigue remainder for '{heroId}'.");
            return -1;
        }

        private static CombatRuntimeAggregate CombatAggregate(
            string executionId,
            string sessionId,
            string heroId)
        {
            var enemyCombatantId = $"{sessionId}:enemy:0";
            return new CombatRuntimeAggregate
            {
                execution = new CombatExecutionSaveData
                {
                    executionId = executionId,
                    sessionId = sessionId,
                    sourceActivityId = "combat_activity",
                    sourceExecutionId = executionId,
                    sourceRequestId = $"request:{executionId}",
                    occupationOwnerId = executionId,
                    heroId = heroId,
                    status = CombatExecutionStatus.Running,
                    startedAtUnixSeconds = 100
                },
                session = new CombatSessionSaveData
                {
                    sessionId = sessionId,
                    executionId = executionId,
                    enemyGroupId = "enemy_group_test",
                    combatMode = "Queue_1v1",
                    enemyQueue = new[]
                    {
                        new CombatEnemyQueueEntrySaveData
                        {
                            combatantId = enemyCombatantId,
                            enemyId = "enemy_test",
                            level = 1,
                            queueIndex = 0
                        }
                    },
                    hero = Combatant($"{sessionId}:hero", heroId),
                    currentEnemy = Combatant(enemyCombatantId, "enemy_test"),
                    scheduler = new CombatSchedulerStateSaveData(),
                    rng = new CombatRngStateSaveData
                    {
                        algorithmId = "test-rng",
                        formatVersion = 1,
                        state = "fixture-state"
                    }
                }
            };
        }

        private static CombatRuntimeAggregate[] CreateBoundedCombatFixtures(int count)
        {
            var fixtures = new CombatRuntimeAggregate[count];
            for (var index = 0; index < fixtures.Length; index++)
            {
                var executionId = $"bounded-combat-{index}";
                var sessionId = $"bounded-session-{index}";
                var aggregate = CombatAggregate(executionId, sessionId, "ren");
                aggregate.session.rng = CombatRngStateFactory.CreateSplitMix64((ulong)(index + 1));
                PopulateBoundedCombatCollections(aggregate.session, PersistentCombatCollectionLimit);
                if (index > 0)
                    CompleteCombatAggregate(aggregate, index);
                else
                {
                    // Outcome rewards and defeat loss are produced only after the
                    // scheduler has stopped, so they cannot coexist with this
                    // max-filled Running scheduler in a persisted production state.
                    aggregate.session.outcomeRewards = Array.Empty<CombatRewardEntrySaveData>();
                    aggregate.session.defeatLoss = null;
                }
                fixtures[index] = aggregate;
            }
            return fixtures;
        }

        private static void PopulateBoundedCombatCollections(CombatSessionSaveData session, int count)
        {
            session.enemyQueue = new CombatEnemyQueueEntrySaveData[count];
            session.hero.abilityCooldowns = new CombatAbilityCooldownSaveData[count];
            session.hero.statuses = new CombatStatusInstanceSaveData[count];
            session.hero.independentModifiers = new CombatTemporaryModifierSaveData[count];
            session.currentEnemy.abilityCooldowns = new CombatAbilityCooldownSaveData[count];
            session.currentEnemy.statuses = new CombatStatusInstanceSaveData[count];
            session.currentEnemy.independentModifiers = new CombatTemporaryModifierSaveData[count];
            session.scheduler.nextSequence = count;
            session.scheduler.scheduledEvents = new CombatScheduledEventSaveData[count];
            session.loot = new CombatRewardEntrySaveData[count];
            session.completionRewards = new CombatRewardEntrySaveData[count];
            session.outcomeRewards = new CombatRewardEntrySaveData[count];
            session.defeatLoss = new CombatDefeatLossSaveData
            {
                lossPercent = 25,
                entries = new CombatDefeatLossEntrySaveData[count]
            };
            for (var index = 0; index < count; index++)
            {
                session.enemyQueue[index] = new CombatEnemyQueueEntrySaveData
                {
                    combatantId = $"{session.sessionId}:enemy:{index}",
                    enemyId = $"bounded-enemy-{index}",
                    level = index + 1,
                    queueIndex = index
                };
                session.hero.abilityCooldowns[index] = new CombatAbilityCooldownSaveData
                {
                    abilityId = $"bounded-ability-{index}",
                    nextReadyAtSeconds = index + 1d,
                    lastTriggerEventKey = $"bounded-trigger-{index}",
                    lastChanceRoll = 5_000,
                    lastChanceResolved = true
                };
                var stackIds = new string[StatusStackLimit];
                for (var stackIndex = 0; stackIndex < stackIds.Length; stackIndex++)
                    stackIds[stackIndex] = $"bounded-status-stack-{index}-{stackIndex}";
                session.hero.statuses[index] = new CombatStatusInstanceSaveData
                {
                    statusInstanceId = $"bounded-status-instance-{index}",
                    statusId = $"bounded-status-{index}",
                    sourceCombatantId = session.hero.combatantId,
                    stackIds = stackIds,
                    expiresAtSeconds = 100d + index,
                    nextTickAtSeconds = 50d + index,
                    lastApplyEventKey = $"bounded-status-apply-{index}",
                    lastTickEventKey = $"bounded-status-tick-{index}"
                };
                session.hero.independentModifiers[index] = new CombatTemporaryModifierSaveData
                {
                    modifierInstanceId = $"bounded-modifier-{index}",
                    sourceId = $"bounded-source-{index}",
                    statId = "damage",
                    operation = "Add",
                    value = index + 1,
                    expiresAtSeconds = 100d + index,
                    appliedEventKey = $"bounded-modifier-apply-{index}"
                };
                session.currentEnemy.abilityCooldowns[index] = new CombatAbilityCooldownSaveData
                {
                    abilityId = $"bounded-enemy-ability-{index}",
                    nextReadyAtSeconds = index + 1d,
                    lastTriggerEventKey = $"bounded-enemy-trigger-{index}",
                    lastChanceRoll = 5_000,
                    lastChanceResolved = true
                };
                var enemyStackIds = new string[StatusStackLimit];
                for (var stackIndex = 0; stackIndex < enemyStackIds.Length; stackIndex++)
                    enemyStackIds[stackIndex] = $"bounded-enemy-status-stack-{index}-{stackIndex}";
                session.currentEnemy.statuses[index] = new CombatStatusInstanceSaveData
                {
                    statusInstanceId = $"bounded-enemy-status-instance-{index}",
                    statusId = $"bounded-enemy-status-{index}",
                    sourceCombatantId = session.currentEnemy.combatantId,
                    stackIds = enemyStackIds,
                    expiresAtSeconds = 100d + index,
                    nextTickAtSeconds = 50d + index,
                    lastApplyEventKey = $"bounded-enemy-status-apply-{index}",
                    lastTickEventKey = $"bounded-enemy-status-tick-{index}"
                };
                session.currentEnemy.independentModifiers[index] = new CombatTemporaryModifierSaveData
                {
                    modifierInstanceId = $"bounded-enemy-modifier-{index}",
                    sourceId = $"bounded-enemy-source-{index}",
                    statId = "damage",
                    operation = "Add",
                    value = index + 1,
                    expiresAtSeconds = 100d + index,
                    appliedEventKey = $"bounded-enemy-modifier-apply-{index}"
                };
                session.scheduler.scheduledEvents[index] = new CombatScheduledEventSaveData
                {
                    eventKey = $"bounded-event-{index}",
                    eventType = "BoundedFixtureEvent",
                    timestampSeconds = index + 1d,
                    phasePriority = 0,
                    actorSide = CombatActorSide.System,
                    sequence = index
                };
                session.loot[index] = CombatReward($"bounded-loot-{index}", index);
                session.completionRewards[index] = CombatReward($"bounded-completion-{index}", index);
                session.outcomeRewards[index] = CombatReward($"bounded-outcome-{index}", index);
                session.defeatLoss.entries[index] = new CombatDefeatLossEntrySaveData
                {
                    origin = PendingResultOrigin.CombatLoot,
                    rewardType = "Resource",
                    targetId = "resource_pine_wood",
                    quantityBefore = 4,
                    quantityLost = 1,
                    quantityKept = 3
                };
            }

            session.currentEnemy.combatantId = session.enemyQueue[0].combatantId;
            session.currentEnemy.definitionId = session.enemyQueue[0].enemyId;
        }

        private static void CompleteCombatAggregate(CombatRuntimeAggregate aggregate, int index)
        {
            aggregate.execution.status = CombatExecutionStatus.Completed;
            aggregate.execution.outcome = CombatTerminalCandidateKinds.Defeat;
            aggregate.execution.outcomeFinalized = true;
            aggregate.execution.resultCreated = true;
            aggregate.execution.pendingResultResolved = true;
            aggregate.execution.failurePublished = true;
            aggregate.execution.pendingResultId = $"bounded-combat-result-{index}";
            aggregate.execution.resultSourceSequence = index;
            aggregate.execution.completedAtUnixSeconds = 200 + index;
            aggregate.session.combatTimeSeconds = index;
            aggregate.session.hero.currentHp = 0;
            aggregate.session.scheduler.scheduledEvents = Array.Empty<CombatScheduledEventSaveData>();
            aggregate.session.terminalCandidate = new CombatTerminalCandidateSaveData
            {
                candidateId = $"bounded-terminal-{index}",
                kind = CombatTerminalCandidateKinds.Defeat,
                eventKey = $"bounded-terminal-event-{index}",
                createdAtSeconds = index
            };
            aggregate.session.simulationStopped = true;
        }

        private static void AssertRunningCombatCollectionsAreMaxFilled(CombatRuntimeAggregate aggregate)
        {
            Assert.That(aggregate.session.enemyQueue, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.hero.abilityCooldowns, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.hero.statuses, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.hero.statuses[0].stackIds, Has.Length.EqualTo(StatusStackLimit));
            Assert.That(aggregate.session.hero.independentModifiers, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.currentEnemy.abilityCooldowns, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.currentEnemy.statuses, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.currentEnemy.statuses[0].stackIds, Has.Length.EqualTo(StatusStackLimit));
            Assert.That(aggregate.session.currentEnemy.independentModifiers, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.scheduler.scheduledEvents, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.loot, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.completionRewards, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.outcomeRewards, Is.Empty);
            Assert.That(aggregate.session.defeatLoss, Is.Null);
        }

        private static void AssertTerminalCombatCollectionsAreMaxFilled(CombatRuntimeAggregate aggregate)
        {
            Assert.That(aggregate.session.enemyQueue, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.hero.abilityCooldowns, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.hero.statuses, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.hero.statuses[0].stackIds, Has.Length.EqualTo(StatusStackLimit));
            Assert.That(aggregate.session.hero.independentModifiers, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.currentEnemy.abilityCooldowns, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.currentEnemy.statuses, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.currentEnemy.statuses[0].stackIds, Has.Length.EqualTo(StatusStackLimit));
            Assert.That(aggregate.session.currentEnemy.independentModifiers, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.scheduler.scheduledEvents, Is.Empty);
            Assert.That(aggregate.session.loot, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.completionRewards, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.outcomeRewards, Has.Length.EqualTo(PersistentCombatCollectionLimit));
            Assert.That(aggregate.session.defeatLoss.entries, Has.Length.EqualTo(PersistentCombatCollectionLimit));
        }

        private static void AssertCompletedCombatIsCompacted(CombatRuntimeAggregate aggregate)
        {
            Assert.That(aggregate.execution.status, Is.EqualTo(CombatExecutionStatus.Completed));
            Assert.That(aggregate.session.enemyQueue, Is.Empty);
            Assert.That(aggregate.session.currentEnemy, Is.Null);
            Assert.That(aggregate.session.hero.abilityCooldowns, Is.Empty);
            Assert.That(aggregate.session.hero.statuses, Is.Empty);
            Assert.That(aggregate.session.hero.independentModifiers, Is.Empty);
            Assert.That(aggregate.session.scheduler.scheduledEvents, Is.Empty);
            Assert.That(aggregate.session.loot, Is.Empty);
            Assert.That(aggregate.session.completionRewards, Is.Empty);
            Assert.That(aggregate.session.outcomeRewards, Is.Empty);
            Assert.That(aggregate.session.defeatLoss, Is.Null);
        }

        private static CombatRewardEntrySaveData CombatReward(string entryId, int sortOrder)
        {
            return new CombatRewardEntrySaveData
            {
                entryId = entryId,
                sortOrder = sortOrder,
                rewardType = "Resource",
                targetId = "resource_pine_wood",
                quantity = 1,
                origin = PendingResultOrigin.CombatLoot
            };
        }

        private static CombatantStateSaveData Combatant(string combatantId, string definitionId)
        {
            return new CombatantStateSaveData
            {
                combatantId = combatantId,
                definitionId = definitionId,
                currentHp = 100,
                maxHp = 100,
                nextAttackAtSeconds = 1d
            };
        }

        private sealed class TestSetup
        {
            public TestSetup(
                ConfigDatabase database,
                FakeTimeProvider clock,
                PlayerStateFactory factory,
                MemorySaveStorage storage,
                PlayerState state)
            {
                Database = database;
                Clock = clock;
                Factory = factory;
                Storage = storage;
                State = state;
            }

            public ConfigDatabase Database { get; }
            public FakeTimeProvider Clock { get; }
            public PlayerStateFactory Factory { get; }
            public MemorySaveStorage Storage { get; }
            public PlayerState State { get; }
        }

        private sealed class FakeTimeProvider : ITimeProvider
        {
            public FakeTimeProvider(long utcNowSeconds) => UtcNowSeconds = utcNowSeconds;
            public long UtcNowSeconds { get; set; }
            public long GetUtcNowUnixSeconds() => UtcNowSeconds;
        }

        private sealed class FixedRandom : ITransactionalActivityRandom
        {
            private ulong _state = 1UL;
            public int RangeInclusive(int min, int max)
            {
                _state++;
                return min;
            }
            public float Percent()
            {
                _state++;
                return 0f;
            }
            public ActivityRandomState CaptureState() => new ActivityRandomState(_state);
            public void RestoreState(ActivityRandomState state) => _state = state.Value;
        }

        private sealed class MemorySaveStorage : ISaveStorage
        {
            private string _json;
            public int SaveCalls { get; private set; }
            public bool ThrowOnSave { get; set; }
            public bool HasKey(string key) => _json != null;
            public string GetString(string key, string defaultValue) => _json ?? defaultValue;
            public void SetString(string key, string value) => _json = value;
            public void DeleteKey(string key) => _json = null;
            public void Save()
            {
                SaveCalls++;
                if (ThrowOnSave)
                    throw new InvalidOperationException("Expected save failure.");
            }
            public void Replace(string json) => _json = json;
        }
    }
}
