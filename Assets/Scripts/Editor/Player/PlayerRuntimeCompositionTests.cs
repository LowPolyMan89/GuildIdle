using System.Reflection;
using GuildIdle.Activities;
using GuildIdle.Combat;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Editor.Configs;
using GuildIdle.Player;
using GuildIdle.Progression;
using NUnit.Framework;
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
                                "enemy_group_test_rabbits",
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

        private static ConfigDatabase CreateConstructionProgressionDatabase(
            bool includeDirectCombatQuests = false)
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
                        new ActivityRewardConfigDto { activityId = "hunt_rabbits", rewardType = "Resource", targetId = "resource_rabbit_meat", min = 1, max = 1, chance = 100, grantMoment = "OnCycle" },
                        new ActivityRewardConfigDto { activityId = "hunt_rabbits", rewardType = "SkillExp", targetId = "skill_hunting", min = 1, max = 1, chance = 100, grantMoment = "OnCycle" }
                    },
                    dangerEncounters = new[]
                    {
                        new DangerEncounterConfigDto
                        {
                            dangerEncounterId = "danger_test_rabbits",
                            activityId = "hunt_rabbits",
                            riskPercent = 100,
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
                                "enemy_group_test_rabbits",
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
                new QuestRuntimeConfigDto
                {
                    stages = new[] { new StageConfigDto { stageId = "stage_arrival", enabled = true } },
                    storyQuests = includeDirectCombatQuests
                        ? new[]
                        {
                            new StoryQuestConfigDto { questId = "quest_build_hall", enabled = true },
                            new StoryQuestConfigDto { questId = "quest_hunt", enabled = true },
                            new StoryQuestConfigDto { questId = "quest_combat_completed", enabled = true },
                            new StoryQuestConfigDto { questId = "quest_combat_failed", enabled = true }
                        }
                        : new[]
                        {
                            new StoryQuestConfigDto { questId = "quest_build_hall", enabled = true },
                            new StoryQuestConfigDto { questId = "quest_hunt", enabled = true }
                        },
                    questStartConditions = includeDirectCombatQuests
                        ? new[]
                        {
                            new QuestStartConditionConfigDto { questId = "quest_build_hall", conditionGroup = "default", conditionType = "NewGame", compareOperator = "GreaterOrEqual", value = 1 },
                            new QuestStartConditionConfigDto { questId = "quest_hunt", conditionGroup = "default", conditionType = "NewGame", compareOperator = "GreaterOrEqual", value = 1 },
                            new QuestStartConditionConfigDto { questId = "quest_combat_completed", conditionGroup = "default", conditionType = "NewGame", compareOperator = "GreaterOrEqual", value = 1 },
                            new QuestStartConditionConfigDto { questId = "quest_combat_failed", conditionGroup = "default", conditionType = "NewGame", compareOperator = "GreaterOrEqual", value = 1 }
                        }
                        : new[]
                        {
                            new QuestStartConditionConfigDto { questId = "quest_build_hall", conditionGroup = "default", conditionType = "NewGame", compareOperator = "GreaterOrEqual", value = 1 },
                            new QuestStartConditionConfigDto { questId = "quest_hunt", conditionGroup = "default", conditionType = "NewGame", compareOperator = "GreaterOrEqual", value = 1 }
                        },
                    questSteps = includeDirectCombatQuests
                        ? new[]
                        {
                            new QuestStepConfigDto { questId = "quest_build_hall", stepId = "build_hall", objectiveType = "BuildingLevel", targetId = "building_hall", compareOperator = "GreaterOrEqual", targetValue = 2, required = true },
                            new QuestStepConfigDto { questId = "quest_hunt", stepId = "hunt", objectiveType = "ActivityCompleted", targetId = "hunt_rabbits", compareOperator = "GreaterOrEqual", targetValue = 1, required = true },
                            new QuestStepConfigDto { questId = "quest_combat_completed", stepId = "combat_completed", objectiveType = "ActivityCompleted", targetId = "combat_clear_hall_forest", compareOperator = "GreaterOrEqual", targetValue = 1, required = true },
                            new QuestStepConfigDto { questId = "quest_combat_failed", stepId = "combat_failed", objectiveType = "ActivityFailed", targetId = "combat_clear_hall_forest", compareOperator = "GreaterOrEqual", targetValue = 1, required = true }
                        }
                        : new[]
                        {
                            new QuestStepConfigDto { questId = "quest_build_hall", stepId = "build_hall", objectiveType = "BuildingLevel", targetId = "building_hall", compareOperator = "GreaterOrEqual", targetValue = 2, required = true },
                            new QuestStepConfigDto { questId = "quest_hunt", stepId = "hunt", objectiveType = "ActivityCompleted", targetId = "hunt_rabbits", compareOperator = "GreaterOrEqual", targetValue = 1, required = true }
                        }
                },
                new EnemiesRuntimeConfigDto
                {
                    enemies = new[]
                    {
                        new EnemyConfigDto
                        {
                            enemyId = "enemy_rabbit",
                            hp = 10,
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
                            minCount = 1,
                            maxCount = 3
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
                            minValue = 100,
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
    }
}
