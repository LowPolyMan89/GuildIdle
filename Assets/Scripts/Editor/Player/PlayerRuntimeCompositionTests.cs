using System.Reflection;
using GuildIdle.Activities;
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
                Assert.That(runtime.BindLinkedCombatExecution(handoff.requestId, "combat-child").success, Is.True);
                Assert.That(state.PendingResults.ClaimAll("production-linked-bag", bag.resultId, bag.revision, state.Storage.GetSnapshot().Revision).Success, Is.True);
                var resolved = runtime.ResolveLinkedCombatExecution(handoff.requestId, "combat-child");

                Assert.That(resolved.success, Is.True);
                Assert.That(state.GetActivityExecution(started.executionId), Is.Null);
                Assert.That(state.GetQuestInstance("story:quest_hunt").status, Is.EqualTo(QuestInstanceStatus.Completed));
                Assert.That(updateCount, Is.Zero, "Production diagnostic eventSink must not replay coordinated linked combat ActivityCompleted.");
                var replay = runtime.ResolveLinkedCombatExecution(handoff.requestId, "combat-child");
                Assert.That(replay.success, Is.True);
                Assert.That(replay.replayed, Is.True);
                Assert.That(updateCount, Is.Zero);
            }
            finally
            {
                SetPlayerRuntime(null, null);
            }
        }

        private static void SetPlayerRuntime(PlayerState state, ProgressionRuntimeService progression)
        {
            typeof(global::GuildIdle.Player.Player).GetField("_state", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, state);
            typeof(global::GuildIdle.Player.Player).GetField("_progression", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, progression);
        }

        private static ConfigDatabase CreateConstructionProgressionDatabase()
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
                        new ActivityConfigDto { id = "hunt_rabbits", type = "Work", category = "Hunting", cycleSec = 10, fatigueCost = 1, mainSkillId = "skill_hunting", isRepeatable = true }
                    },
                    skills = new[]
                    {
                        new SkillConfigDto { skillId = "skill_construction" },
                        new SkillConfigDto { skillId = "skill_hunting" }
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
                    storyQuests = new[]
                    {
                        new StoryQuestConfigDto { questId = "quest_build_hall", enabled = true },
                        new StoryQuestConfigDto { questId = "quest_hunt", enabled = true }
                    },
                    questStartConditions = new[]
                    {
                        new QuestStartConditionConfigDto { questId = "quest_build_hall", conditionGroup = "default", conditionType = "NewGame", compareOperator = "GreaterOrEqual", value = 1 },
                        new QuestStartConditionConfigDto { questId = "quest_hunt", conditionGroup = "default", conditionType = "NewGame", compareOperator = "GreaterOrEqual", value = 1 }
                    },
                    questSteps = new[]
                    {
                        new QuestStepConfigDto { questId = "quest_build_hall", stepId = "build_hall", objectiveType = "BuildingLevel", targetId = "building_hall", compareOperator = "GreaterOrEqual", targetValue = 2, required = true },
                        new QuestStepConfigDto { questId = "quest_hunt", stepId = "hunt", objectiveType = "ActivityCompleted", targetId = "hunt_rabbits", compareOperator = "GreaterOrEqual", targetValue = 1, required = true }
                    }
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
