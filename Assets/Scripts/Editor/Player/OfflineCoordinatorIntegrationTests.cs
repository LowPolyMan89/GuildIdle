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
        private const int SaveSizeLimitBytes = 200 * 1024;

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
        public void DangerBoundaryCreatesOnePendingRequestWithoutCombatSessionAndReplaysAsNoOp()
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
            var saved = JsonUtility.ToJson(setup.State.ToSaveData());
            var repeated = Run(setup.State, new FixedRandom());

            Assert.That(first.Success, Is.True);
            Assert.That(first.Danger.Completed, Is.EqualTo(1));
            Assert.That(execution.linkedCombat, Is.Not.Null);
            Assert.That(execution.linkedCombat.requestId, Is.Not.Empty);
            Assert.That(execution.linkedCombat.combatExecutionId, Is.Null.Or.Empty);
            Assert.That(setup.State.GetCombatAggregates(), Is.Empty);
            Assert.That(repeated.Code, Is.EqualTo(OfflineCoordinatorCode.NoElapsedTime));
            Assert.That(JsonUtility.ToJson(setup.State.ToSaveData()), Is.EqualTo(saved));
            Assert.That(Encoding.UTF8.GetByteCount(saved), Is.LessThan(SaveSizeLimitBytes));
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

        private static void Seed(PlayerState state, string itemId, int quantity)
        {
            var result = state.Storage.Add(
                $"offline-seed:{itemId}:{Guid.NewGuid():N}",
                state.Storage.GetSnapshot().Revision,
                itemId,
                quantity);
            Assert.That(result.Success, Is.True, result.Message);
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
