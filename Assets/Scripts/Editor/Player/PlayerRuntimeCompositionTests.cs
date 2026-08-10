using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GuildIdle.Activities;
using GuildIdle.Combat;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Editor.Configs;
using GuildIdle.Player;
using GuildIdle.Progression;
using NUnit.Framework;
using UnityEngine.TestTools;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Editor.Player
{
    public sealed class PlayerRuntimeCompositionTests
    {
        [Test]
        public void PlayerStateFactoryGraph_IsCachedUntilConfigFailureInvalidatesIt()
        {
            RuntimeConfigs.SetDatabaseForTests(new TestConfigDatabaseBuilder()
                .WithFullPlayerStateTestData()
                .Build());

            var getFactory = typeof(PlayerRuntimeComposition).GetMethod(
                "GetPlayerStateFactory",
                BindingFlags.NonPublic | BindingFlags.Static);
            var invalidateFactory = typeof(PlayerRuntimeComposition).GetMethod(
                "InvalidatePlayerStateFactory",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(getFactory, Is.Not.Null);
            Assert.That(invalidateFactory, Is.Not.Null);

            try
            {
                invalidateFactory.Invoke(null, null);
                var first = getFactory.Invoke(null, null);
                var repeated = getFactory.Invoke(null, null);

                Assert.That(repeated, Is.SameAs(first));

                invalidateFactory.Invoke(null, null);
                var afterFailure = getFactory.Invoke(null, null);

                Assert.That(afterFailure, Is.Not.SameAs(first));
                Assert.That(getFactory.Invoke(null, null), Is.SameAs(afterFailure));
            }
            finally
            {
                invalidateFactory.Invoke(null, null);
            }
        }

        [Test]
        public void ProgressionRuntimeFactory_UsesProvidedPlayerState()
        {
            var database = new TestConfigDatabaseBuilder()
                .WithFullPlayerStateTestData()
                .Build();
            RuntimeConfigs.SetDatabaseForTests(database);
            var state = TestPlayerComposition.CreatePlayerStateFactory(database).CreateDefault();

            var runtime = PlayerRuntimeComposition.CreateProgressionRuntimeService(state);

            Assert.That(runtime.GetStageSnapshot().StageId, Is.EqualTo("stage_arrival"));
        }

        [Test]
        public void ActivityRewardBatchDependency_IsExplicitInContracts()
        {
            Assert.That(typeof(IRewardBatchStore).IsAssignableFrom(typeof(IActivityPlayerState)), Is.True);
            Assert.That(typeof(IRewardBatchStore).IsAssignableFrom(typeof(PlayerState)), Is.True);
        }

        [Test]
        public void ProductionActivityRuntimeDoesNotReplayCoordinatedConstructionEventsIntoProgression()
        {
            var database = CreateConstructionProgressionDatabase();
            RuntimeConfigs.SetDatabaseForTests(database);
            var state = TestPlayerComposition.CreatePlayerStateFactory(database).CreateDefault();
            state.AddHero("ren");
            state.UnlockBuilding("building_hall");
            state.SetBuildingLevel("building_hall", 0);
            var progression = PlayerRuntimeComposition.CreateProgressionRuntimeService(state);
            progression.Handle(new NewGame());
            var updateCount = 0;
            progression.Updated += _ => updateCount++;
            SetPlayerRuntime(state, progression);

            try
            {
                var runtime = PlayerRuntimeComposition.CreateRuntimeService();
                var started = runtime.Start(new ActivityStartRequest { activityId = "test_build_empty", heroId = "ren" });
                var completed = runtime.Tick(1f);
                var quest = state.GetQuestInstance("story:quest_build_hall");

                Assert.That(started.success, Is.True);
                Assert.That(completed.success, Is.True);
                Assert.That(completed.events, Has.Length.EqualTo(2));
                Assert.That(completed.events[0].progressionAlreadyProcessed, Is.True);
                Assert.That(completed.events[1].progressionAlreadyProcessed, Is.True);
                Assert.That(quest.status, Is.EqualTo(QuestInstanceStatus.Active));
                Assert.That(quest.steps[0].currentValue, Is.EqualTo(1));
                Assert.That(quest.steps[0].completed, Is.False);
                Assert.That(updateCount, Is.Zero, "Post-commit ActivityRuntime eventSink must be diagnostic for coordinated events.");
            }
            finally
            {
                SetPlayerRuntime(null, null);
            }
        }

        [Test]
        public void ProductionEventSinkStillHandlesUncoordinatedActivityCompleted()
        {
            var database = CreateConstructionProgressionDatabase();
            RuntimeConfigs.SetDatabaseForTests(database);
            var state = TestPlayerComposition.CreatePlayerStateFactory(database).CreateDefault();
            var progression = PlayerRuntimeComposition.CreateProgressionRuntimeService(state);
            var updateCount = 0;
            progression.Updated += _ => updateCount++;
            SetPlayerRuntime(state, progression);

            try
            {
                var handler = typeof(PlayerRuntimeComposition).GetMethod(
                    "HandleActivityRuntimeEvent",
                    BindingFlags.NonPublic | BindingFlags.Static);

                handler.Invoke(null, new object[]
                {
                    new ActivityRuntimeEvent
                    {
                        eventType = ActivityRuntimeEventType.ActivityCompleted,
                        targetId = "linked_combat_root",
                        value = 1
                    }
                });

                Assert.That(updateCount, Is.EqualTo(1));
            }
            finally
            {
                SetPlayerRuntime(null, null);
            }
        }

        [Test]
        public void ProductionLinkedCombatCompletionUsesCoordinatedProgressionOnly()
        {
            var database = CreateConstructionProgressionDatabase();
            RuntimeConfigs.SetDatabaseForTests(database);
            var state = TestPlayerComposition.CreatePlayerStateFactory(database).CreateDefault();
            state.AddHero("ren");
            var progression = PlayerRuntimeComposition.CreateProgressionRuntimeService(state);
            progression.Handle(new NewGame());
            var updateCount = 0;
            progression.Updated += _ => updateCount++;
            SetPlayerRuntime(state, progression);

            try
            {
                var runtime = PlayerRuntimeComposition.CreateRuntimeService();
                var started = runtime.Start(new ActivityStartRequest { activityId = "hunt_rabbits", heroId = "ren", plannedCycleCount = 3 });
                var ticked = runtime.Tick(20f);
                var handoff = runtime.GetPendingLinkedCombatStarts()[0];
                var bag = state.PendingResults.GetAll()[0];

                Assert.That(started.success, Is.True);
                Assert.That(ticked.success, Is.True);
                Assert.That(
                    handoff.combatExecutionId,
                    Is.Not.Null.And.Not.Empty);
                Assert.That(state.PendingResults.ClaimAll("production-linked-bag", bag.resultId, bag.revision, state.Storage.GetSnapshot().Revision).Success, Is.True);
                PrepareTerminal(
                    state,
                    handoff.combatExecutionId,
                    CombatTerminalCandidateKinds.Retreat);
                var formed = new CombatOutcomeService(state)
                    .FinalizeTerminal(handoff.combatExecutionId);
                Assert.That(formed.Success, Is.True, formed.Message);
                var combatResult =
                    state.PendingResults.GetAll()[0];
                var resolved = state.PendingResults.ClaimAll(
                    "production-linked-combat",
                    combatResult.resultId,
                    combatResult.revision,
                    state.Storage.GetSnapshot().Revision);

                Assert.That(resolved.Success, Is.True, resolved.Message);
                Assert.That(resolved.Resolved, Is.True);
                Assert.That(state.GetActivityExecution(started.executionId), Is.Null);
                Assert.That(state.GetQuestInstance("story:quest_hunt").status, Is.EqualTo(QuestInstanceStatus.Completed));
                Assert.That(updateCount, Is.Zero, "Production diagnostic eventSink must not replay coordinated linked combat ActivityCompleted.");
                var replay = runtime.ResolveLinkedCombatExecution(
                    handoff.requestId,
                    handoff.combatExecutionId);
                Assert.That(replay.success, Is.True);
                Assert.That(replay.replayed, Is.True);
                Assert.That(updateCount, Is.Zero);
            }
            finally
            {
                SetPlayerRuntime(null, null);
            }
        }

        [Test]
        public void ProductionRuntimeAutomaticallyStartsOneLinkedCombatWithCycleLoot()
        {
            var database = CreateConstructionProgressionDatabase();
            RuntimeConfigs.SetDatabaseForTests(database);
            var state = TestPlayerComposition
                .CreatePlayerStateFactory(database)
                .CreateDefault();
            state.AddHero("ren");
            var progression =
                PlayerRuntimeComposition
                    .CreateProgressionRuntimeService(state);
            progression.Handle(new NewGame());
            SetPlayerRuntime(state, progression);

            try
            {
                using var runtime =
                    PlayerRuntimeComposition.CreateRuntimeService();
                var started = runtime.Start(
                    new ActivityStartRequest
                    {
                        activityId = "hunt_rabbits",
                        heroId = "ren",
                        plannedCycleCount = 3
                    });
                var fatigueAfterWorkStart =
                    state.GetHeroFatigue("ren");
                var storageRevision =
                    state.Storage.GetSnapshot().Revision;

                var ticked = runtime.Tick(20f);

                Assert.That(started.success, Is.True);
                Assert.That(ticked.success, Is.True);
                var request =
                    runtime.GetPendingLinkedCombatStarts()[0];
                Assert.That(
                    request.combatExecutionId,
                    Is.Not.Null.And.Not.Empty);
                Assert.That(
                    state.GetCombatAggregates(),
                    Has.Length.EqualTo(1));
                var combat = state.GetCombatAggregate(
                    request.combatExecutionId);
                Assert.That(
                    combat.execution.startOperationId,
                    Is.EqualTo(
                        $"linked-combat-start:{request.requestId}"));
                Assert.That(
                    combat.session.loadoutKind,
                    Is.EqualTo(CombatLoadoutKind.Empty));
                Assert.That(
                    combat.session.broughtConsumable,
                    Is.Null);
                Assert.That(
                    combat.session.loot,
                    Has.Length.EqualTo(1));
                Assert.That(
                    combat.session.loot[0].targetId,
                    Is.EqualTo("resource_rabbit_meat"));
                Assert.That(
                    combat.session.loot[0].origin,
                    Is.EqualTo(
                        PendingResultOrigin
                            .ActivityLootInCombat));
                Assert.That(
                    combat.session.enemyQueue.Length,
                    Is.InRange(1, 3));
                Assert.That(
                    state.GetHeroFatigue("ren"),
                    Is.EqualTo(fatigueAfterWorkStart));
                Assert.That(
                    state.Storage.GetSnapshot().Revision,
                    Is.EqualTo(storageRevision));
                var activityBag =
                    state.PendingResults.GetAll()[0];
                Assert.That(
                    activityBag.sourceType,
                    Is.EqualTo(PendingResultSourceType.Activity));
                Assert.That(
                    activityBag.entries,
                    Has.Length.EqualTo(1));
                Assert.That(
                    activityBag.entries[0].rewardType,
                    Is.EqualTo(RewardType.SkillExp));

                runtime.Tick(0f);

                Assert.That(
                    state.GetCombatAggregates(),
                    Has.Length.EqualTo(1));
                runtime.Dispose();
                using var replacement =
                    PlayerRuntimeComposition.CreateRuntimeService();
                Assert.That(
                    replacement.GetPendingLinkedCombatStarts()[0]
                        .combatExecutionId,
                    Is.EqualTo(request.combatExecutionId));
                Assert.That(
                    state.GetCombatAggregates(),
                    Has.Length.EqualTo(1));
            }
            finally
            {
                SetPlayerRuntime(null, null);
            }
        }

        [TestCase(CombatTerminalCandidateKinds.Victory, true, false)]
        [TestCase(CombatTerminalCandidateKinds.Defeat, false, true)]
        [TestCase(CombatTerminalCandidateKinds.Retreat, false, false)]
        public void DirectCombatPublishesTypedProgressionOnlyAfterResultResolution(
            string outcome,
            bool completedExpected,
            bool failedExpected)
        {
            var database = CreateConstructionProgressionDatabase(
                includeDirectCombatQuests: true);
            RuntimeConfigs.SetDatabaseForTests(database);
            var state = TestPlayerComposition
                .CreatePlayerStateFactory(database)
                .CreateDefault();
            state.AddHero("ren");
            state.SetActivityAvailable(
                "combat_clear_hall_forest",
                true);
            var progression =
                PlayerRuntimeComposition
                    .CreateProgressionRuntimeService(state);
            progression.Handle(new NewGame());
            SetPlayerRuntime(state, progression);

            try
            {
                using var runtime =
                    PlayerRuntimeComposition.CreateRuntimeService();
                var start =
                    PlayerRuntimeComposition
                        .CreateCombatStartService(state)
                        .Start(new CombatStartCommand
                        {
                            OperationId =
                                $"direct-start:{outcome}",
                            Kind = CombatStartKind.Direct,
                            SourceActivityId =
                                "combat_clear_hall_forest",
                            SourceRequestId =
                                $"direct-request:{outcome}",
                            HeroId = "ren",
                            EnemyGroupId =
                                "enemy_group_underwood_wolves",
                            CombatMode =
                                CombatEnemyQueueBuilder
                                    .Queue1V1Mode,
                            RequestedQuantity = 0,
                            ExpectedStorageRevision =
                                state.Storage.GetSnapshot()
                                    .Revision
                        });
                Assert.That(start.Success, Is.True, start.Message);
                PrepareTerminal(
                    state,
                    start.ExecutionId,
                    outcome);

                var formed =
                    new CombatOutcomeService(state)
                        .FinalizeTerminal(start.ExecutionId);
                Assert.That(formed.Success, Is.True, formed.Message);
                Assert.That(
                    state.GetQuestInstance(
                        "story:quest_combat_completed")
                        .status,
                    Is.EqualTo(QuestInstanceStatus.Active));
                Assert.That(
                    state.GetQuestInstance(
                        "story:quest_combat_failed")
                        .status,
                    Is.EqualTo(QuestInstanceStatus.Active));

                var pending =
                    state.PendingResults.GetAll()[0];
                var resolved =
                    state.PendingResults.ClaimAll(
                        $"resolve:{outcome}",
                        pending.resultId,
                        pending.revision,
                        state.Storage.GetSnapshot().Revision);

                Assert.That(resolved.Success, Is.True, resolved.Message);
                Assert.That(resolved.Resolved, Is.True);
                var saved =
                    state.GetCombatAggregate(start.ExecutionId)
                        .execution;
                Assert.That(
                    saved.completionPublished,
                    Is.EqualTo(completedExpected));
                Assert.That(
                    saved.failurePublished,
                    Is.EqualTo(failedExpected));
                Assert.That(
                    state.GetQuestInstance(
                        "story:quest_combat_completed")
                        .status ==
                    QuestInstanceStatus.Completed,
                    Is.EqualTo(completedExpected));
                Assert.That(
                    state.GetQuestInstance(
                        "story:quest_combat_failed")
                        .status ==
                    QuestInstanceStatus.Completed,
                    Is.EqualTo(failedExpected));

                runtime.Tick(0f);

                Assert.That(
                    state.GetCombatAggregate(start.ExecutionId)
                        .execution.completionPublished,
                    Is.EqualTo(completedExpected));
                Assert.That(
                    state.GetCombatAggregate(start.ExecutionId)
                        .execution.failurePublished,
                    Is.EqualTo(failedExpected));
            }
            finally
            {
                SetPlayerRuntime(null, null);
            }
        }

        [TestCase("DescriptorPersisted")]
        [TestCase("SessionCreated")]
        [TestCase("CombatInProgress")]
        [TestCase("TerminalResultPending")]
        [TestCase("CombatResolved")]
        [TestCase("ActivityBagResolved")]
        [TestCase("FlowClosed")]
        public void LinkedDangerEncounterSurvivesRealSaveLoadBoundary(
            string boundary)
        {
            var database = CreateConstructionProgressionDatabase(
                dangerRiskPercent: 25);
            RuntimeConfigs.SetDatabaseForTests(database);
            var factory =
                TestPlayerComposition.CreatePlayerStateFactory(database);
            var storage = new MemorySaveStorage();
            var state = SaveService.Load(factory, storage);
            state.AddHero("ren");
            PlayerRuntimeComposition
                .CreateProgressionRuntimeService(state)
                .Handle(new NewGame());
            Assert.That(SaveService.Save(state, storage), Is.True);

            var startedEvents = 0;
            using var runtime = CreateLinkedActivityRuntime(
                state,
                new SequenceActivityRandom(100, 1),
                coordinateCombat:
                    !string.Equals(
                        boundary,
                        "DescriptorPersisted",
                        StringComparison.Ordinal),
                combatStarted: _ => startedEvents++);
            var started = runtime.Start(
                new ActivityStartRequest
                {
                    activityId = "hunt_rabbits",
                    heroId = "ren",
                    plannedCycleCount = 3
                });
            Assert.That(started.success, Is.True);
            Assert.That(runtime.Tick(20f).success, Is.True);

            var request = runtime.GetPendingLinkedCombatStarts().Single();
            var combatExecutionId = request.combatExecutionId;
            if (!string.Equals(
                    boundary,
                    "DescriptorPersisted",
                    StringComparison.Ordinal))
            {
                Assert.That(combatExecutionId, Is.Not.Null.And.Not.Empty);
                Assert.That(startedEvents, Is.EqualTo(1));
            }

            if (string.Equals(
                    boundary,
                    "CombatInProgress",
                    StringComparison.Ordinal))
            {
                var advanced = CreateCombatRuntimeService(
                        state,
                        CombatScenarioDescriptors.Stalemate())
                    .AdvanceTo(combatExecutionId, 0.25d);
                Assert.That(advanced.Success, Is.True, advanced.Error?.Message);
            }
            else if (IsAtOrAfterTerminalBoundary(boundary))
            {
                var retreated =
                    new CombatOutcomeService(state)
                        .RequestRetreat(new CombatRetreatCommand
                        {
                            OperationId =
                                $"save-load-retreat:{boundary}",
                            ExecutionId = combatExecutionId
                        });
                Assert.That(
                    retreated.Success,
                    Is.True,
                    retreated.Message);

                if (string.Equals(
                        boundary,
                        "CombatResolved",
                        StringComparison.Ordinal) ||
                    string.Equals(
                        boundary,
                        "FlowClosed",
                        StringComparison.Ordinal))
                {
                    ResolvePendingResult(
                        state,
                        PendingResultSourceType.Combat,
                        $"save-load-combat:{boundary}");
                }

                if (string.Equals(
                        boundary,
                        "ActivityBagResolved",
                        StringComparison.Ordinal) ||
                    string.Equals(
                        boundary,
                        "FlowClosed",
                        StringComparison.Ordinal))
                {
                    ResolvePendingResult(
                        state,
                        PendingResultSourceType.Activity,
                        $"save-load-activity:{boundary}");
                }
            }

            var executionBefore =
                state.GetActivityExecution(started.executionId);
            var aggregateBefore =
                string.IsNullOrWhiteSpace(combatExecutionId)
                    ? null
                    : state.GetCombatAggregate(combatExecutionId);
            var queueBefore = QueueFingerprint(aggregateBefore);
            var lootBefore = LootFingerprint(aggregateBefore);
            var pendingBefore = PendingResultIds(state);
            var fatigueBefore = state.GetHeroFatigue("ren");
            var storageRevisionBefore =
                state.Storage.GetSnapshot().Revision;
            var completedBefore =
                state.IsActivityCompleted("hunt_rabbits");
            var busyBefore = state.IsHeroBusy("ren");

            runtime.Dispose();
            Assert.That(SaveService.Save(state, storage), Is.True);

            var reloadStartedEvents = 0;
            Action<CombatStartedEvent> onCombatStarted =
                _ => reloadStartedEvents++;
            PlayerRuntimeComposition.CombatStarted += onCombatStarted;
            PlayerState restored;
            ActivityRuntimeService restoredRuntime = null;
            try
            {
                restored = SaveService.Load(factory, storage);
                restoredRuntime =
                    PlayerRuntimeComposition.CreateRuntimeService(restored);
            }
            finally
            {
                PlayerRuntimeComposition.CombatStarted -= onCombatStarted;
            }

            using (restoredRuntime)
            {
                var restoredRequest =
                    restoredRuntime
                        .GetPendingLinkedCombatStarts()
                        .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(combatExecutionId))
                {
                    Assert.That(restoredRequest, Is.Not.Null);
                    combatExecutionId =
                        restoredRequest.combatExecutionId;
                    Assert.That(
                        combatExecutionId,
                        Is.Not.Null.And.Not.Empty);
                    Assert.That(reloadStartedEvents, Is.EqualTo(1));
                }
                else
                {
                    Assert.That(reloadStartedEvents, Is.Zero);
                }

                var aggregateAfter =
                    restored.GetCombatAggregate(combatExecutionId);
                Assert.That(
                    restored.GetCombatAggregates(),
                    Has.Length.EqualTo(1));
                Assert.That(aggregateAfter, Is.Not.Null);
                Assert.That(
                    restored.GetHeroFatigue("ren"),
                    Is.EqualTo(fatigueBefore));
                Assert.That(
                    restored.Storage.GetSnapshot().Revision,
                    Is.EqualTo(storageRevisionBefore));
                Assert.That(
                    PendingResultIds(restored),
                    Is.EqualTo(pendingBefore));
                Assert.That(
                    restored.IsActivityCompleted("hunt_rabbits"),
                    Is.EqualTo(completedBefore));
                Assert.That(
                    restored.IsHeroBusy("ren"),
                    Is.EqualTo(busyBefore));

                if (aggregateBefore != null)
                {
                    Assert.That(
                        QueueFingerprint(aggregateAfter),
                        Is.EqualTo(queueBefore));
                    Assert.That(
                        LootFingerprint(aggregateAfter),
                        Is.EqualTo(lootBefore));
                }

                if (executionBefore == null)
                {
                    Assert.That(
                        restored.GetActivityExecution(
                            started.executionId),
                        Is.Null);
                }
                else
                {
                    var restoredExecution =
                        restored.GetActivityExecution(
                            started.executionId);
                    Assert.That(restoredExecution, Is.Not.Null);
                    Assert.That(
                        restoredExecution.completedCycles,
                        Is.EqualTo(executionBefore.completedCycles));
                    Assert.That(
                        restoredExecution.endReason,
                        Is.EqualTo(executionBefore.endReason));
                    Assert.That(
                        restoredExecution.linkedCombat.requestId,
                        Is.EqualTo(
                            executionBefore.linkedCombat.requestId));
                }

                Assert.That(
                    restoredRuntime.Tick(100f).success,
                    Is.True);
                Assert.That(
                    restored.GetCombatAggregates(),
                    Has.Length.EqualTo(1));
                Assert.That(
                    PendingResultIds(restored),
                    Is.EqualTo(pendingBefore));
                Assert.That(
                    restored.GetActivityExecution(
                            started.executionId)
                        ?.completedCycles,
                    Is.EqualTo(executionBefore?.completedCycles));
                Assert.That(
                    restored.IsHeroBusy("ren"),
                    Is.EqualTo(busyBefore));
            }
        }

        [Test]
        public void CombatClearHallForestRunsHeadlessThroughFourWolvesAndLeader()
        {
            var database = CreateConstructionProgressionDatabase(
                includeDirectCombatQuests: true);
            RuntimeConfigs.SetDatabaseForTests(database);
            var factory =
                TestPlayerComposition.CreatePlayerStateFactory(database);
            var storage = new MemorySaveStorage();
            var state = SaveService.Load(factory, storage);
            state.AddHero("ren");
            state.SetActivityAvailable(
                "combat_clear_hall_forest",
                true);
            PlayerRuntimeComposition
                .CreateProgressionRuntimeService(state)
                .Handle(new NewGame());
            Assert.That(SaveService.Save(state, storage), Is.True);

            using var activityRuntime =
                PlayerRuntimeComposition.CreateRuntimeService(state);
            var started =
                PlayerRuntimeComposition
                    .CreateCombatStartService(state)
                    .Start(new CombatStartCommand
                    {
                        OperationId = "forest-headless-start",
                        Kind = CombatStartKind.Direct,
                        SourceActivityId =
                            "combat_clear_hall_forest",
                        SourceRequestId =
                            "forest-headless-request",
                        HeroId = "ren",
                        EnemyGroupId =
                            "enemy_group_underwood_wolves",
                        CombatMode =
                            CombatEnemyQueueBuilder.Queue1V1Mode,
                        RequestedQuantity = 0,
                        ExpectedStorageRevision =
                            state.Storage.GetSnapshot().Revision
                    });
            Assert.That(started.Success, Is.True, started.Message);
            Assert.That(
                started.Aggregate.session.enemyQueue
                    .Select(value => value.enemyId),
                Is.EqualTo(new[]
                {
                    "enemy_lean_wolf",
                    "enemy_lean_wolf",
                    "enemy_lean_wolf",
                    "enemy_lean_wolf",
                    "enemy_wolf_leader"
                }));
            Assert.That(
                started.Aggregate.session.currentEnemy.definitionId,
                Is.EqualTo("enemy_lean_wolf"));

            var advanced = CreateCombatRuntimeService(
                    state,
                    CombatScenarioDescriptors.FastVictory())
                .AdvanceTo(started.ExecutionId, 100d);
            Assert.That(advanced.Success, Is.True, advanced.Error?.Message);

            var completed =
                state.GetCombatAggregate(started.ExecutionId);
            Assert.That(
                completed.execution.outcome,
                Is.EqualTo(CombatTerminalCandidateKinds.Victory));
            Assert.That(
                completed.session.queuePosition,
                Is.EqualTo(completed.session.enemyQueue.Length));
            Assert.That(completed.session.currentEnemy, Is.Null);
            Assert.That(
                state.GetQuestInstance(
                        "story:quest_combat_completed")
                    .status,
                Is.EqualTo(QuestInstanceStatus.Active));

            ResolvePendingResult(
                state,
                PendingResultSourceType.Combat,
                "forest-headless-claim");

            completed = state.GetCombatAggregate(started.ExecutionId);
            Assert.That(completed.execution.completionPublished, Is.True);
            Assert.That(completed.execution.failurePublished, Is.False);
            Assert.That(
                state.GetQuestInstance(
                        "story:quest_combat_completed")
                    .status,
                Is.EqualTo(QuestInstanceStatus.Completed));
            Assert.That(
                state.GetQuestInstance(
                        "story:quest_combat_failed")
                    .status,
                Is.EqualTo(QuestInstanceStatus.Active));

            activityRuntime.Dispose();
            Assert.That(SaveService.Save(state, storage), Is.True);
            var restored = SaveService.Load(factory, storage);
            using var restoredRuntime =
                PlayerRuntimeComposition.CreateRuntimeService(restored);
            Assert.That(restoredRuntime.Tick(0f).success, Is.True);
            Assert.That(
                restored.GetCombatAggregate(started.ExecutionId)
                    .execution.completionPublished,
                Is.True);
            Assert.That(
                restored.GetQuestInstance(
                        "story:quest_combat_completed")
                    .status,
                Is.EqualTo(QuestInstanceStatus.Completed));
            Assert.That(
                restored.PendingResults.GetAll(),
                Is.Empty);
        }

        [TestCase(CombatTerminalCandidateKinds.Victory)]
        [TestCase(CombatTerminalCandidateKinds.Defeat)]
        [TestCase(CombatTerminalCandidateKinds.Retreat)]
        public void LinkedDangerEncounterRunsEndToEndForEveryOutcome(
            string outcome)
        {
            var database = CreateConstructionProgressionDatabase(
                rabbitMinCount: 3,
                rabbitMaxCount: 3,
                includeLinkedFailureQuest: true,
                dangerRiskPercent: 25);
            RuntimeConfigs.SetDatabaseForTests(database);
            var factory =
                TestPlayerComposition.CreatePlayerStateFactory(database);
            var storage = new MemorySaveStorage();
            var state = SaveService.Load(factory, storage);
            state.AddHero("ren");
            PlayerRuntimeComposition
                .CreateProgressionRuntimeService(state)
                .Handle(new NewGame());
            Assert.That(SaveService.Save(state, storage), Is.True);

            using var runtime = CreateLinkedActivityRuntime(
                state,
                new SequenceActivityRandom(100, 1));
            var started = runtime.Start(
                new ActivityStartRequest
                {
                    activityId = "hunt_rabbits",
                    heroId = "ren",
                    plannedCycleCount = 3
                });
            Assert.That(started.success, Is.True);
            var fatigueAfterStart = state.GetHeroFatigue("ren");
            var storageRevisionBeforeDanger =
                state.Storage.GetSnapshot().Revision;
            Assert.That(runtime.Tick(20f).success, Is.True);

            var request = runtime.GetPendingLinkedCombatStarts().Single();
            var combat =
                state.GetCombatAggregate(request.combatExecutionId);
            var activityBag = GetPendingResult(
                state,
                PendingResultSourceType.Activity);
            Assert.That(combat.session.enemyQueue, Has.Length.EqualTo(3));
            Assert.That(combat.session.broughtConsumable, Is.Null);
            Assert.That(
                state.GetHeroFatigue("ren"),
                Is.EqualTo(fatigueAfterStart - 1),
                "Only the next planned work cycle may consume fatigue; linked combat itself must not.");
            Assert.That(
                state.Storage.GetSnapshot().Revision,
                Is.EqualTo(storageRevisionBeforeDanger));
            Assert.That(
                EntryQuantity(
                    activityBag,
                    RewardType.Resource,
                    "resource_rabbit_meat"),
                Is.EqualTo(4),
                "The previous cycle must remain in Activity Bag.");
            Assert.That(
                EntryQuantity(
                    activityBag,
                    RewardType.SkillExp,
                    "skill_hunting"),
                Is.EqualTo(2),
                "Non-loot rewards from both cycles must stay in Activity Bag.");
            Assert.That(
                combat.session.loot.Single().quantity,
                Is.EqualTo(4));
            Assert.That(
                combat.session.loot.Single().origin,
                Is.EqualTo(PendingResultOrigin.ActivityLootInCombat));

            if (string.Equals(
                    outcome,
                    CombatTerminalCandidateKinds.Retreat,
                    StringComparison.Ordinal))
            {
                var advanced = CreateCombatRuntimeService(
                        state,
                        CombatScenarioDescriptors.Stalemate())
                    .AdvanceTo(request.combatExecutionId, 0.25d);
                Assert.That(
                    advanced.Success,
                    Is.True,
                    advanced.Error?.Message);
                var retreated =
                    new CombatOutcomeService(state)
                        .RequestRetreat(new CombatRetreatCommand
                        {
                            OperationId =
                                "linked-outcome-retreat",
                            ExecutionId =
                                request.combatExecutionId
                        });
                Assert.That(
                    retreated.Success,
                    Is.True,
                    retreated.Message);
            }
            else
            {
                var descriptors = string.Equals(
                    outcome,
                    CombatTerminalCandidateKinds.Victory,
                    StringComparison.Ordinal)
                    ? CombatScenarioDescriptors.FastVictory()
                    : CombatScenarioDescriptors.AttritionDefeat();
                var advanced = CreateCombatRuntimeService(
                        state,
                        descriptors)
                    .AdvanceTo(request.combatExecutionId, 100d);
                Assert.That(
                    advanced.Success,
                    Is.True,
                    advanced.Error?.Message);
            }

            combat = state.GetCombatAggregate(
                request.combatExecutionId);
            Assert.That(combat.execution.outcome, Is.EqualTo(outcome));
            var combatResult = GetPendingResult(
                state,
                PendingResultSourceType.Combat);
            Assert.That(
                state.GetActivityExecution(started.executionId),
                Is.Not.Null);
            Assert.That(state.IsHeroBusy("ren"), Is.True);
            Assert.That(
                state.GetQuestInstance("story:quest_hunt").status,
                Is.EqualTo(QuestInstanceStatus.Active));
            Assert.That(
                state.GetQuestInstance(
                        "story:quest_hunt_failed")
                    .status,
                Is.EqualTo(QuestInstanceStatus.Active));

            if (string.Equals(
                    outcome,
                    CombatTerminalCandidateKinds.Defeat,
                    StringComparison.Ordinal))
            {
                Assert.That(
                    combat.session.defeatLoss.lossPercent,
                    Is.InRange(25, 50));
                var activityLoss =
                    combat.session.defeatLoss.entries.Single(
                        value =>
                            string.Equals(
                                value.origin,
                                PendingResultOrigin
                                    .ActivityLootInCombat,
                                StringComparison.Ordinal));
                Assert.That(activityLoss.quantityBefore, Is.EqualTo(4));
                Assert.That(activityLoss.quantityLost, Is.InRange(1, 2));
                Assert.That(
                    EntryQuantity(
                        activityBag,
                        RewardType.Resource,
                        "resource_rabbit_meat"),
                    Is.EqualTo(4),
                    "Previous Activity Bag must not participate in combat loss.");
                Assert.That(combat.session.accumulatedEnemyExp, Is.GreaterThan(0));
                Assert.That(
                    combatResult.entries.Any(
                        value =>
                            string.Equals(
                                value.origin,
                                PendingResultOrigin.EnemyCombatExp,
                                StringComparison.Ordinal) &&
                            value.quantity ==
                            combat.session.accumulatedEnemyExp),
                    Is.True);
            }
            else
            {
                Assert.That(
                    EntryQuantity(
                        combatResult,
                        RewardType.Resource,
                        "resource_rabbit_meat"),
                    Is.EqualTo(4));
            }

            if (string.Equals(
                    outcome,
                    CombatTerminalCandidateKinds.Defeat,
                    StringComparison.Ordinal))
            {
                ResolvePendingResult(
                    state,
                    PendingResultSourceType.Activity,
                    $"linked-activity:{outcome}");
                AssertLinkedFlowStillWaiting(state, started.executionId);
                ResolvePendingResult(
                    state,
                    PendingResultSourceType.Combat,
                    $"linked-combat:{outcome}");
            }
            else
            {
                ResolvePendingResult(
                    state,
                    PendingResultSourceType.Combat,
                    $"linked-combat:{outcome}");
                AssertLinkedFlowStillWaiting(state, started.executionId);
                ResolvePendingResult(
                    state,
                    PendingResultSourceType.Activity,
                    $"linked-activity:{outcome}");
            }

            Assert.That(
                state.GetActivityExecution(started.executionId),
                Is.Null);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.IsActivityCompleted("hunt_rabbits"), Is.True);
            Assert.That(
                state.GetQuestInstance("story:quest_hunt").status,
                Is.EqualTo(QuestInstanceStatus.Completed));
            Assert.That(
                state.GetQuestInstance(
                        "story:quest_hunt_failed")
                    .status,
                Is.EqualTo(QuestInstanceStatus.Active));
            Assert.That(runtime.Tick(100f).success, Is.True);
            Assert.That(
                state.GetActivityExecution(started.executionId),
                Is.Null);
            Assert.That(
                state.GetCombatAggregates(),
                Has.Length.EqualTo(1));
        }

        [Test]
        public void LinkedCoordinatorRetriesAutomaticStartAfterTransientSaveFailure()
        {
            var database = CreateConstructionProgressionDatabase(
                dangerRiskPercent: 25);
            RuntimeConfigs.SetDatabaseForTests(database);
            var factory =
                TestPlayerComposition.CreatePlayerStateFactory(database);
            var storage = new MemorySaveStorage();
            var state = SaveService.Load(factory, storage);
            state.AddHero("ren");
            PlayerRuntimeComposition
                .CreateProgressionRuntimeService(state)
                .Handle(new NewGame());
            Assert.That(SaveService.Save(state, storage), Is.True);

            using var descriptorRuntime = CreateLinkedActivityRuntime(
                state,
                new SequenceActivityRandom(100, 1),
                coordinateCombat: false);
            var started = descriptorRuntime.Start(
                new ActivityStartRequest
                {
                    activityId = "hunt_rabbits",
                    heroId = "ren",
                    plannedCycleCount = 3
                });
            Assert.That(started.success, Is.True);
            Assert.That(descriptorRuntime.Tick(20f).success, Is.True);
            var descriptor =
                descriptorRuntime.GetPendingLinkedCombatStarts().Single();
            Assert.That(descriptor.combatExecutionId, Is.Null.Or.Empty);
            descriptorRuntime.Dispose();

            var startedEvents = 0;
            storage.ThrowOnSaveCall = storage.SaveCalls + 1;
            LogAssert.Expect(
                UnityEngine.LogType.Error,
                "[SaveService] Failed to save player state. simulated save failure");
            using var retryRuntime = CreateLinkedActivityRuntime(
                state,
                new SequenceActivityRandom(),
                combatStarted: _ => startedEvents++);

            var afterFailure =
                retryRuntime.GetPendingLinkedCombatStarts().Single();
            Assert.That(afterFailure.combatExecutionId, Is.Null.Or.Empty);
            Assert.That(state.GetCombatAggregates(), Is.Empty);
            Assert.That(
                HasOperationReceipt(
                    state,
                    "combat-start",
                    $"linked-combat-start:{descriptor.requestId}"),
                Is.False);
            Assert.That(startedEvents, Is.Zero);

            storage.ThrowOnSaveCall = 0;
            Assert.That(retryRuntime.Tick(0f).success, Is.True);
            var afterRetry =
                retryRuntime.GetPendingLinkedCombatStarts().Single();
            Assert.That(
                afterRetry.combatExecutionId,
                Is.Not.Null.And.Not.Empty);
            Assert.That(
                state.GetCombatAggregates(),
                Has.Length.EqualTo(1));
            Assert.That(
                state.GetCombatAggregate(afterRetry.combatExecutionId)
                    .session.loot,
                Has.Length.EqualTo(1));
            Assert.That(startedEvents, Is.EqualTo(1));

            Assert.That(retryRuntime.Tick(0f).success, Is.True);
            Assert.That(
                state.GetCombatAggregates(),
                Has.Length.EqualTo(1));
            Assert.That(startedEvents, Is.EqualTo(1));
        }

        private static void PrepareTerminal(
            PlayerState state,
            string executionId,
            string outcome)
        {
            var aggregate =
                state.GetCombatAggregate(executionId);
            aggregate.session.simulationStopped = true;
            aggregate.session.combatTimeSeconds = 1d;
            aggregate.session.scheduler.scheduledEvents =
                System.Array.Empty<CombatScheduledEventSaveData>();
            aggregate.session.terminalCandidate =
                new CombatTerminalCandidateSaveData
                {
                    candidateId =
                        $"{aggregate.session.sessionId}:{outcome}",
                    kind = outcome,
                    eventKey = $"terminal:{outcome}",
                    createdAtSeconds = 1d
                };
            aggregate.session.loot = new[]
            {
                new CombatRewardEntrySaveData
                {
                    entryId = "terminal-loot",
                    rewardType = RewardType.Resource,
                    targetId = "resource_rabbit_meat",
                    quantity = 4,
                    origin = PendingResultOrigin.CombatLoot
                }
            };
            if (outcome ==
                CombatTerminalCandidateKinds.Victory)
            {
                aggregate.session.enemyQueue =
                    System.Array
                        .Empty<CombatEnemyQueueEntrySaveData>();
                aggregate.session.queuePosition = 0;
                aggregate.session.currentEnemy = null;
            }
            else if (outcome ==
                     CombatTerminalCandidateKinds.Defeat)
            {
                aggregate.session.hero.currentHp = 0;
            }

            Assert.That(
                state.UpdateCombatAggregate(aggregate),
                Is.True);
        }

        private static void SetPlayerRuntime(PlayerState state, ProgressionRuntimeService progression)
        {
            typeof(global::GuildIdle.Player.Player).GetField("_state", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, state);
            typeof(global::GuildIdle.Player.Player).GetField("_progression", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, progression);
        }

        private static bool IsAtOrAfterTerminalBoundary(string boundary)
        {
            return string.Equals(
                       boundary,
                       "TerminalResultPending",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       boundary,
                       "CombatResolved",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       boundary,
                       "ActivityBagResolved",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       boundary,
                       "FlowClosed",
                       StringComparison.Ordinal);
        }

        private static ActivityRuntimeService CreateLinkedActivityRuntime(
            PlayerState state,
            IActivityRandom random,
            bool coordinateCombat = true,
            Action<CombatStartedEvent> combatStarted = null)
        {
            var progression =
                new TestActivityProgressionProcessor(
                    PlayerRuntimeComposition
                        .CreateProgressionRuntimeService(state));
            return new ActivityRuntimeService(
                state,
                new PlayerStateActivityAdapter(state),
                random,
                progressionProcessor: progression,
                linkedCombatCoordinatorFactory:
                    coordinateCombat
                        ? (gateway, processor) =>
                            new LinkedCombatRuntimeCoordinator(
                                gateway,
                                CreateDeterministicCombatStartService(
                                    state,
                                    combatStarted),
                                state,
                                processor)
                        : null);
        }

        private static CombatStartService
            CreateDeterministicCombatStartService(
                PlayerState state,
                Action<CombatStartedEvent> eventSink)
        {
            return new CombatStartService(
                new PlayerStateCombatStartAdapter(
                    state,
                    RuntimeConfigs.Formulas,
                    RuntimeConfigs.Items,
                    RuntimeConfigs.Buildings),
                new ConfigCombatStartActivityDescriptorProvider(
                    RuntimeConfigs.Activities),
                RuntimeConfigs.CombatConsumables,
                new ConfigCombatEnemyQueueProvider(
                    RuntimeConfigs.Enemies),
                identity: new DeterministicCombatIdentity(),
                eventSink: eventSink,
                completionRewards:
                    new ConfigCombatCompletionRewardProvider(
                        RuntimeConfigs.Activities));
        }

        private static CombatRuntimeService CreateCombatRuntimeService(
            PlayerState state,
            ICombatDescriptorProvider descriptors)
        {
            return PlayerRuntimeComposition
                .CreateCombatRuntimeService(state, descriptors);
        }

        private static PendingResultSaveData GetPendingResult(
            PlayerState state,
            string sourceType)
        {
            return state.PendingResults.GetAll().Single(
                value =>
                    string.Equals(
                        value.sourceType,
                        sourceType,
                        StringComparison.Ordinal));
        }

        private static void ResolvePendingResult(
            PlayerState state,
            string sourceType,
            string operationId)
        {
            var pending = GetPendingResult(state, sourceType);
            var resolved = state.PendingResults.ClaimAll(
                operationId,
                pending.resultId,
                pending.revision,
                state.Storage.GetSnapshot().Revision);
            Assert.That(resolved.Success, Is.True, resolved.Message);
            Assert.That(resolved.Resolved, Is.True);
        }

        private static string[] PendingResultIds(PlayerState state)
        {
            return state.PendingResults.GetAll()
                .Select(value => value.resultId)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private static string QueueFingerprint(
            CombatRuntimeAggregate aggregate)
        {
            if (aggregate?.session?.enemyQueue == null)
                return null;
            return string.Join(
                "|",
                aggregate.session.enemyQueue.Select(
                    value =>
                        $"{value.queueIndex}:{value.enemyId}:{value.combatantId}"));
        }

        private static string LootFingerprint(
            CombatRuntimeAggregate aggregate)
        {
            if (aggregate?.session?.loot == null)
                return null;
            return string.Join(
                "|",
                aggregate.session.loot.Select(
                    value =>
                        $"{value.entryId}:{value.origin}:{value.rewardType}:{value.targetId}:{value.quantity}"));
        }

        private static long EntryQuantity(
            PendingResultSaveData result,
            string rewardType,
            string targetId)
        {
            return (result?.entries ??
                    Array.Empty<PendingResultEntrySaveData>())
                .Where(
                    value =>
                        value != null &&
                        string.Equals(
                            value.rewardType,
                            rewardType,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            value.targetId,
                            targetId,
                            StringComparison.Ordinal))
                .Sum(value => value.quantity);
        }

        private static void AssertLinkedFlowStillWaiting(
            PlayerState state,
            string activityExecutionId)
        {
            Assert.That(
                state.GetActivityExecution(activityExecutionId),
                Is.Not.Null);
            Assert.That(state.IsHeroBusy("ren"), Is.True);
            Assert.That(
                state.IsActivityCompleted("hunt_rabbits"),
                Is.False);
        }

        private static bool HasOperationReceipt(
            PlayerState state,
            string aggregateId,
            string operationId)
        {
            return (state.ToSaveData().operationReceipts ??
                    Array.Empty<OperationReceiptSaveData>())
                .Any(
                    value =>
                        value != null &&
                        string.Equals(
                            value.aggregateId,
                            aggregateId,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            value.operationId,
                            operationId,
                            StringComparison.Ordinal));
        }

        internal static ConfigDatabase CreateConstructionProgressionDatabase(
            bool includeDirectCombatQuests = false,
            int rabbitMinCount = 1,
            int rabbitMaxCount = 3,
            bool includeLinkedFailureQuest = false,
            int dangerRiskPercent = 100)
        {
            return new ConfigDatabase(
                new ItemsRuntimeConfigDto
                {
                    resources = new[]
                    {
                        new ResourceConfigDto { id = "resource_pine_wood", kind = "resource" },
                        new ResourceConfigDto { id = "resource_rabbit_meat", kind = "resource" }
                    },
                    currencies = new[] { new CurrencyConfigDto { currencyId = "gold_id" } }
                },
                new HeroesRuntimeConfigDto
                {
                    heroes = new[]
                    {
                        new HeroConfigDto
                        {
                            heroId = "ren",
                            enabled = true,
                            baseStats = new HeroBaseStatsDto { strength = 2, agility = 2, intelligence = 2, luck = 2, endurance = 2 }
                        }
                    }
                },
                new ActivitiesRuntimeConfigDto
                {
                    activities = new[]
                    {
                        new ActivityConfigDto { id = "test_build_empty", type = "Build", durationSec = 1, fatigueCost = 1, isRepeatable = false },
                        new ActivityConfigDto { id = "hunt_rabbits", type = "Work", category = "Hunting", cycleSec = 10, fatigueCost = 1, mainSkillId = "skill_hunting", isRepeatable = true },
                        new ActivityConfigDto { id = "combat_clear_hall_forest", type = "CombatTask", fatigueCost = 5, mainSkillId = "skill_combat", isRepeatable = false }
                    },
                    skills = new[]
                    {
                        new SkillConfigDto { skillId = "skill_construction" },
                        new SkillConfigDto { skillId = "skill_hunting" },
                        new SkillConfigDto { skillId = "skill_combat" }
                    },
                    skillsProgression = new[] { new SkillProgressionConfigDto { level = 1, totalExpRequired = 0 } },
                    rewards = new[]
                    {
                        new ActivityRewardConfigDto { activityId = "hunt_rabbits", rewardType = "Resource", targetId = "resource_rabbit_meat", min = 4, max = 4, chance = 100, grantMoment = "OnCycle" },
                        new ActivityRewardConfigDto { activityId = "hunt_rabbits", rewardType = "SkillExp", targetId = "skill_hunting", min = 1, max = 1, chance = 100, grantMoment = "OnCycle" }
                    },
                    dangerEncounters = new[]
                    {
                        new DangerEncounterConfigDto
                        {
                            dangerEncounterId = "danger_test_rabbits",
                            activityId = "hunt_rabbits",
                            riskPercent = dangerRiskPercent,
                            enemyGroupId = "enemy_group_test_rabbits",
                            combatMode = "Queue_1v1",
                            defeatLossRule = "CombatDefeatLootLoss25To50",
                            riskFormulaId = "test_danger_risk"
                        }
                    },
                    combatDetails = new[]
                    {
                        new CombatDetailConfigDto
                        {
                            activityId =
                                "combat_clear_hall_forest",
                            enemyGroupId =
                                "enemy_group_underwood_wolves",
                            combatMode =
                                CombatEnemyQueueBuilder.Queue1V1Mode
                        }
                    }
                },
                new BuildingsRuntimeConfigDto
                {
                    buildings = new[] { new BuildingConfigDto { buildingId = "building_hall", levels = 1, startLevel = 0, visibleAtStart = true } },
                    buildingLevels = new[]
                    {
                        new BuildingLevelConfigDto { buildingId = "building_hall", level = 0, activeHeroLimit = 1 },
                        new BuildingLevelConfigDto { buildingId = "building_hall", level = 1, sourceActivityId = "test_build_empty", buildFormulaId = "test_build_points", buildPointsRequired = 1, skillId = "skill_construction", fatigueCost = 1, activeHeroLimit = 1 }
                    },
                    buildActions = new[]
                    {
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
                CreateProgressionQuestConfig(
                    includeDirectCombatQuests,
                    includeLinkedFailureQuest),
                new EnemiesRuntimeConfigDto
                {
                    enemies = new[]
                    {
                        new EnemyConfigDto
                        {
                            enemyId = "enemy_rabbit",
                            combatExp = 5,
                            hp = 10,
                            damageMin = 1,
                            damageMax = 1,
                            attacksPerSecond = 1f,
                            damageType = "physical",
                            critDamageMultiplier = 1.5f
                        },
                        new EnemyConfigDto
                        {
                            enemyId = "enemy_lean_wolf",
                            combatExp = 5,
                            hp = 20,
                            damageMin = 1,
                            damageMax = 1,
                            attacksPerSecond = 1f,
                            damageType = "physical",
                            critDamageMultiplier = 1.5f
                        },
                        new EnemyConfigDto
                        {
                            enemyId = "enemy_wolf_leader",
                            combatExp = 10,
                            hp = 30,
                            damageMin = 1,
                            damageMax = 1,
                            attacksPerSecond = 1f,
                            damageType = "physical",
                            critDamageMultiplier = 1.5f
                        }
                    },
                    enemyLevels = new[]
                    {
                        new EnemyLevelConfigDto
                        {
                            level = 1,
                            hpMultiplier = 1f,
                            damageMultiplier = 1f,
                            combatExpMultiplier = 1f,
                            lootQuantityMultiplier = 1f,
                            attackSpeedMultiplier = 1f
                        }
                    },
                    enemyGroups = new[]
                    {
                        new EnemyGroupConfigDto
                        {
                            enemyGroupId =
                                "enemy_group_test_rabbits",
                            enemyRef = "enemy_rabbit:1",
                            sortOrder = 10,
                            weight = 100,
                            minCount = rabbitMinCount,
                            maxCount = rabbitMaxCount
                        },
                        new EnemyGroupConfigDto
                        {
                            enemyGroupId =
                                "enemy_group_underwood_wolves",
                            enemyRef = "enemy_lean_wolf:1",
                            sortOrder = 10,
                            weight = 100,
                            minCount = 4,
                            maxCount = 4
                        },
                        new EnemyGroupConfigDto
                        {
                            enemyGroupId =
                                "enemy_group_underwood_wolves",
                            enemyRef = "enemy_wolf_leader:1",
                            sortOrder = 20,
                            weight = 100,
                            minCount = 1,
                            maxCount = 1
                        }
                    }
                },
                new FormulaRuntimeConfigDto
                {
                    formulas = new[]
                    {
                        new FormulaConfigDto
                        {
                            formulaId = "hero_max_hp",
                            derivedStatId = "max_hp",
                            formulaType =
                                "linear_stat_with_level",
                            baseValue = 50,
                            primaryStat = "Endurance",
                            primaryStatMultiplier = 8,
                            levelMultiplier = 2,
                            minValue = 1,
                            rounding = "floor",
                            enabled = true
                        },
                        new FormulaConfigDto
                        {
                            formulaId = "hero_max_fatigue",
                            derivedStatId = "max_fatigue",
                            formulaType =
                                "linear_stat_with_level",
                            baseValue = 100,
                            primaryStat = "Endurance",
                            primaryStatMultiplier = 4,
                            levelMultiplier = 1,
                            minValue = 1,
                            rounding = "round",
                            enabled = true
                        },
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
                            primaryStatMultiplier =
                                dangerRiskPercent >= 100 ? 0f : 0.5f,
                            secondaryStatMultiplier =
                                dangerRiskPercent >= 100 ? 0f : 0.5f,
                            levelMultiplier =
                                dangerRiskPercent >= 100 ? 0f : 0.5f,
                            minValue =
                                dangerRiskPercent >= 100
                                    ? 100
                                    : 5,
                            rounding = "round_2",
                            enabled = true
                        }
                    }
                },
                null,
                null,
                new StorageRuntimeConfigDto
                {
                    storageRules = new[] { new StorageRuleConfigDto { storageRuleId = "storage_resource", itemKind = "resource", mode = "stack", maxStack = 100, occupiesSlot = true } },
                    storageBuildings = new[] { new StorageBuildingConfigDto { buildingId = "building_hall", level = 0, slotCount = 20 } },
                    itemStates = new[]
                    {
                        new ItemStateConfigDto { stateId = "on_storage", isInStorage = true, occupiesCapacity = true, availabilityMode = ItemAvailabilityMode.Available },
                        new ItemStateConfigDto { stateId = "equipped", requiresOwner = true, availabilityMode = ItemAvailabilityMode.Equipped }
                    }
                },
                null);
        }

        private static QuestRuntimeConfigDto
            CreateProgressionQuestConfig(
                bool includeDirectCombatQuests,
                bool includeLinkedFailureQuest)
        {
            var quests = new List<StoryQuestConfigDto>
            {
                new StoryQuestConfigDto
                {
                    questId = "quest_build_hall",
                    enabled = true
                },
                new StoryQuestConfigDto
                {
                    questId = "quest_hunt",
                    enabled = true
                }
            };
            var starts = new List<QuestStartConditionConfigDto>
            {
                NewGameCondition("quest_build_hall"),
                NewGameCondition("quest_hunt")
            };
            var steps = new List<QuestStepConfigDto>
            {
                new QuestStepConfigDto
                {
                    questId = "quest_build_hall",
                    stepId = "build_hall",
                    objectiveType = "BuildingLevel",
                    targetId = "building_hall",
                    compareOperator = "GreaterOrEqual",
                    targetValue = 2,
                    required = true
                },
                ActivityQuestStep(
                    "quest_hunt",
                    "hunt",
                    ActivityRuntimeEventType.ActivityCompleted,
                    "hunt_rabbits")
            };

            if (includeDirectCombatQuests)
            {
                quests.Add(
                    new StoryQuestConfigDto
                    {
                        questId = "quest_combat_completed",
                        enabled = true
                    });
                quests.Add(
                    new StoryQuestConfigDto
                    {
                        questId = "quest_combat_failed",
                        enabled = true
                    });
                starts.Add(NewGameCondition("quest_combat_completed"));
                starts.Add(NewGameCondition("quest_combat_failed"));
                steps.Add(
                    ActivityQuestStep(
                        "quest_combat_completed",
                        "combat_completed",
                        ActivityRuntimeEventType.ActivityCompleted,
                        "combat_clear_hall_forest"));
                steps.Add(
                    ActivityQuestStep(
                        "quest_combat_failed",
                        "combat_failed",
                        "ActivityFailed",
                        "combat_clear_hall_forest"));
            }

            if (includeLinkedFailureQuest)
            {
                quests.Add(
                    new StoryQuestConfigDto
                    {
                        questId = "quest_hunt_failed",
                        enabled = true
                    });
                starts.Add(NewGameCondition("quest_hunt_failed"));
                steps.Add(
                    ActivityQuestStep(
                        "quest_hunt_failed",
                        "hunt_failed",
                        "ActivityFailed",
                        "hunt_rabbits"));
            }

            return new QuestRuntimeConfigDto
            {
                stages = new[]
                {
                    new StageConfigDto
                    {
                        stageId = "stage_arrival",
                        enabled = true
                    }
                },
                storyQuests = quests.ToArray(),
                questStartConditions = starts.ToArray(),
                questSteps = steps.ToArray()
            };
        }

        private static QuestStartConditionConfigDto NewGameCondition(
            string questId)
        {
            return new QuestStartConditionConfigDto
            {
                questId = questId,
                conditionGroup = "default",
                conditionType = "NewGame",
                compareOperator = "GreaterOrEqual",
                value = 1
            };
        }

        private static QuestStepConfigDto ActivityQuestStep(
            string questId,
            string stepId,
            string objectiveType,
            string targetId)
        {
            return new QuestStepConfigDto
            {
                questId = questId,
                stepId = stepId,
                objectiveType = objectiveType,
                targetId = targetId,
                compareOperator = "GreaterOrEqual",
                targetValue = 1,
                required = true
            };
        }

        private sealed class TestActivityProgressionProcessor :
            IActivityRuntimeProgressionProcessor
        {
            private readonly ProgressionRuntimeService _progression;

            public TestActivityProgressionProcessor(
                ProgressionRuntimeService progression)
            {
                _progression = progression ??
                               throw new ArgumentNullException(
                                   nameof(progression));
            }

            public ActivityRuntimeProgressionResult
                ProcessBuildingLevelChanged(
                    string buildingId,
                    int level)
            {
                return ToResult(
                    _progression.Handle(
                        new BuildingLevelChanged(buildingId, level)));
            }

            public ActivityRuntimeProgressionResult
                ProcessActivityCompleted(string activityId)
            {
                return ToResult(
                    _progression
                        .HandleActivityCompleted(activityId));
            }

            public ActivityRuntimeProgressionResult
                ProcessActivityFailed(string activityId)
            {
                return ToResult(
                    _progression.Handle(
                        new ActivityFailed(activityId)));
            }

            private static ActivityRuntimeProgressionResult ToResult(
                ProgressionRuntimeUpdate update)
            {
                if (update?.Issues != null &&
                    update.Issues.Count > 0)
                {
                    var issue = update.Issues[0];
                    return new ActivityRuntimeProgressionResult
                    {
                        success = false,
                        code = issue.Code,
                        message = issue.Message
                    };
                }

                return new ActivityRuntimeProgressionResult
                {
                    success = true,
                    code = "Applied"
                };
            }
        }

        private sealed class SequenceActivityRandom : IActivityRandom
        {
            private readonly Queue<int> _dangerRolls;

            public SequenceActivityRandom(params int[] dangerRolls)
            {
                _dangerRolls = new Queue<int>(
                    dangerRolls ?? Array.Empty<int>());
            }

            public int RangeInclusive(int min, int max)
            {
                if (min == 1 &&
                    max == 100 &&
                    _dangerRolls.Count > 0)
                {
                    return Math.Max(
                        min,
                        Math.Min(max, _dangerRolls.Dequeue()));
                }

                return min;
            }

            public float Percent()
            {
                return 0f;
            }
        }

        private sealed class DeterministicCombatIdentity :
            ICombatStartIdentityProvider
        {
            private int _executionSequence;
            private int _sessionSequence;

            public string CreateExecutionId()
            {
                _executionSequence++;
                return $"test-combat-execution-{_executionSequence}";
            }

            public string CreateSessionId()
            {
                _sessionSequence++;
                return $"test-combat-session-{_sessionSequence}";
            }

            public ulong CreateRngSeed()
            {
                return 0x123456789ABCDEF0UL;
            }

            public long GetUtcNowUnixSeconds()
            {
                return 1_700_000_000L;
            }
        }

        private static class CombatScenarioDescriptors
        {
            public static ICombatDescriptorProvider FastVictory()
            {
                return new ScenarioDescriptorProvider(
                    Actor(
                        CombatActorSide.Hero,
                        CombatAttackCadence.HeroInterval(0.1d),
                        100),
                    Actor(
                        CombatActorSide.Enemy,
                        CombatAttackCadence.EnemyRate(0.01d),
                        1));
            }

            public static ICombatDescriptorProvider AttritionDefeat()
            {
                return new ScenarioDescriptorProvider(
                    Actor(
                        CombatActorSide.Hero,
                        CombatAttackCadence.HeroInterval(1d),
                        100),
                    Actor(
                        CombatActorSide.Enemy,
                        CombatAttackCadence.EnemyRate(2d),
                        30));
            }

            public static ICombatDescriptorProvider Stalemate()
            {
                return new ScenarioDescriptorProvider(
                    Actor(
                        CombatActorSide.Hero,
                        CombatAttackCadence.HeroInterval(10d),
                        1),
                    Actor(
                        CombatActorSide.Enemy,
                        CombatAttackCadence.EnemyRate(0.1d),
                        1));
            }

            private static CombatActorDescriptor Actor(
                CombatActorSide side,
                CombatAttackCadence cadence,
                int damage)
            {
                return new CombatActorDescriptor(
                    side,
                    cadence,
                    damage,
                    damage,
                    "physical",
                    0d,
                    1.5d,
                    0d,
                    0d,
                    0d);
            }
        }

        private sealed class ScenarioDescriptorProvider :
            ICombatDescriptorProvider
        {
            private readonly CombatActorDescriptor _hero;
            private readonly CombatActorDescriptor _enemy;

            public ScenarioDescriptorProvider(
                CombatActorDescriptor hero,
                CombatActorDescriptor enemy)
            {
                _hero = hero;
                _enemy = enemy;
            }

            public bool TryGetDescriptor(
                CombatActorSide side,
                string definitionId,
                out CombatActorDescriptor descriptor,
                out string error)
            {
                descriptor =
                    side == CombatActorSide.Hero ? _hero : _enemy;
                error = descriptor == null
                    ? $"No descriptor for '{definitionId}'."
                    : null;
                return descriptor != null;
            }
        }

        private sealed class MemorySaveStorage : ISaveStorage
        {
            private readonly Dictionary<string, string> _values =
                new Dictionary<string, string>(StringComparer.Ordinal);

            public int SaveCalls { get; private set; }
            public int ThrowOnSaveCall { get; set; }

            public bool HasKey(string key)
            {
                return _values.ContainsKey(key);
            }

            public string GetString(
                string key,
                string defaultValue)
            {
                return _values.TryGetValue(key, out var value)
                    ? value
                    : defaultValue;
            }

            public void SetString(string key, string value)
            {
                _values[key] = value;
            }

            public void DeleteKey(string key)
            {
                _values.Remove(key);
            }

            public void Save()
            {
                SaveCalls++;
                if (ThrowOnSaveCall == SaveCalls)
                    throw new InvalidOperationException(
                        "simulated save failure");
            }
        }
    }
}
