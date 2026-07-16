using System.Collections.Generic;
using GuildIdle.Activities;
using GuildIdle.Configs;
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

            var result = runtime.Start("work_pine_wood", "ren");

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
            LogAssert.Expect(LogType.Error, "[ActivityResolver] Unknown activity id 'missing_activity'.");

            var missing = runtime.Start("missing_activity", "ren");
            var emptyHero = runtime.Start("work_pine_wood", string.Empty);

            Assert.That(missing.success, Is.False);
            Assert.That(emptyHero.success, Is.False);
            Assert.That(HasIssue(emptyHero.issues, "HeroExecutor"), Is.True);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(state.GetActivityExecutions(), Is.Empty);
        }

        [Test]
        public void Tick_RepeatableProcessesCyclesRewardsAndLimit()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            Assert.That(runtime.Start("work_pine_wood", "ren").success, Is.True);

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

            var limitedTick = runtime.Tick(2000f);
            execution = state.GetActivityExecutions()[0];

            Assert.That(limitedTick.success, Is.True);
            Assert.That(limitedTick.cycleLimitReached, Is.True);
            Assert.That(limitedTick.processedCycles, Is.EqualTo(ActivityRuntimeService.MaxCyclesPerTick));
            Assert.That(HasIssue(limitedTick.issues, "TickCycleLimitReached"), Is.True);
            Assert.That(execution.completedCycles, Is.EqualTo(102));
            Assert.That(execution.elapsedSeconds, Is.GreaterThanOrEqualTo(10f));
        }

        [Test]
        public void CancelRepeatableWithBagStopsInResultPendingAndAllowsClaim()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            var started = runtime.Start("work_pine_wood", "ren");
            Assert.That(runtime.Tick(10f).success, Is.True);
            var bag = state.PendingResults.GetAll()[0];

            var blockedClaim = state.PendingResults.ClaimAll("claim-before-stop", bag.resultId, bag.revision, state.Storage.GetSnapshot().Revision);
            var stopped = runtime.Cancel(started.executionId);
            var execution = state.GetActivityExecution(started.executionId);
            var claimed = state.PendingResults.ClaimAll("claim-after-stop", bag.resultId, bag.revision, state.Storage.GetSnapshot().Revision);

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
        public void Tick_RewardFailureKeepsRepeatableExecution()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));
            Assert.That(runtime.Start("bad_cycle", "ren").success, Is.True);
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
            var start = runtime.Start("work_pine_wood", "ren");
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
            Assert.That(runtime.Start("work_pine_wood", "ren").success, Is.True);
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
            Assert.That(state.AddHero("aska"), Is.True);
            var runtime = new ActivityRuntimeService(state, new PlayerStateActivityAdapter(state));

            var first = runtime.Start("work_pine_wood", "ren");
            var limited = runtime.Start("work_pine_wood", "aska");

            Assert.That(first.success, Is.True);
            Assert.That(limited.success, Is.False);
            Assert.That(HasIssue(limited.issues, "ActiveHeroLimitReached"), Is.True);
            Assert.That(state.GetActivityExecutions(), Has.Length.EqualTo(1));

            Assert.That(runtime.Cancel(first.executionId).success, Is.True);
            var afterCancel = runtime.Start("work_pine_wood", "aska");

            Assert.That(afterCancel.success, Is.True);
            Assert.That(state.IsHeroBusy("aska"), Is.True);
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

        private static ConfigDatabase CreateDatabase()
        {
            return new ConfigDatabase(
                new ItemsRuntimeConfigDto
                {
                    resources = new[]
                    {
                        new ResourceConfigDto { id = "resource_pine_wood", kind = "resource" }
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
                        new HeroConfigDto { heroId = "ren", enabled = true },
                        new HeroConfigDto { heroId = "aska", enabled = true }
                    }
                },
                new ActivitiesRuntimeConfigDto
                {
                    activities = new[]
                    {
                        new ActivityConfigDto { id = "work_pine_wood", type = "Work", cycleSec = 10, fatigueCost = 2, isRepeatable = true },
                        new ActivityConfigDto { id = "bad_cycle", type = "Work", cycleSec = 5, isRepeatable = true },
                        new ActivityConfigDto { id = "one_shot", type = "Explore", durationSec = 5, fatigueCost = 5, isRepeatable = false },
                        new ActivityConfigDto { id = "one_shot_new", type = "Explore", durationSec = 5, isRepeatable = false }
                    },
                    skills = new[]
                    {
                        new SkillConfigDto { skillId = "skill_gathering" }
                    },
                    skillsProgression = new[]
                    {
                        new SkillProgressionConfigDto { level = 1, totalExpRequired = 0 }
                    },
                    rewards = new[]
                    {
                        Reward("work_pine_wood", "Resource", "resource_pine_wood", 1, "OnCycle"),
                        Reward("work_pine_wood", "SkillExp", "skill_gathering", 1, "OnCycle"),
                        Reward("bad_cycle", "Unsupported", "bad_reward", 1, "OnCycle"),
                        Reward("one_shot_new", "Resource", "resource_pine_wood", 1, "OnComplete"),
                        Reward("one_shot_new", "Gold", "gold_id", 2, "OnFirstComplete")
                    }
                },
                new BuildingsRuntimeConfigDto
                {
                    buildings = new[]
                    {
                        new BuildingConfigDto { buildingId = "building_hall", levels = 1, startLevel = 0, visibleAtStart = true },
                        new BuildingConfigDto { buildingId = "building_warehouse", levels = 0, startLevel = 0, visibleAtStart = true }
                    },
                    buildingLevels = new[]
                    {
                        new BuildingLevelConfigDto { buildingId = "building_hall", level = 0, activeHeroLimit = 1 },
                        new BuildingLevelConfigDto { buildingId = "building_hall", level = 1, activeHeroLimit = 1 },
                        new BuildingLevelConfigDto { buildingId = "building_warehouse", level = 0 }
                    }
                },
                new QuestRuntimeConfigDto
                {
                    stages = new[] { new StageConfigDto { stageId = "stage_arrival", enabled = true } }
                },
                null,
                null,
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
    }
}
