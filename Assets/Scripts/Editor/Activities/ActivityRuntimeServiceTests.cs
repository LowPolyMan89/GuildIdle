using System.Collections.Generic;
using GuildIdle.Activities;
using GuildIdle.Configs;
using GuildIdle.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Editor.Activities
{
    public sealed class ActivityRuntimeServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            RuntimeConfigs.SetDatabaseForTests(CreateDatabase());
        }

        [Test]
        public void Start_CreatesExecutionAndSpendsHeroFatigue()
        {
            var state = NewState();
            var storage = new MemorySaveStorage();
            var runtime = new ActivityRuntimeService(state, storage);
            var fatigue = state.GetHeroFatigue("ren");

            var result = runtime.Start("work_pine_wood", 0);

            Assert.That(result.success, Is.True);
            Assert.That(state.GetActivityExecutions(), Has.Length.EqualTo(1));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue - 2));
            Assert.That(state.IsHeroBusy("ren"), Is.True);
            Assert.That(storage.HasKey(SaveService.SaveKey), Is.True);
        }

        [Test]
        public void Start_RejectsCompletedNonRepeatableBeforeCost()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new MemorySaveStorage());
            var fatigue = state.GetHeroFatigue("ren");
            Assert.That(state.CompleteActivity("one_shot"), Is.True);

            var result = runtime.Start("one_shot", 0);

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
            var runtime = new ActivityRuntimeService(state, new MemorySaveStorage());
            var fatigue = state.GetHeroFatigue("ren");
            LogAssert.Expect(LogType.Error, "[ActivityResolver] Unknown activity id 'missing_activity'.");
            LogAssert.Expect(LogType.Error, "[ActivityResolver] Unknown activity id 'missing_activity'.");

            var missing = runtime.Start("missing_activity", 0);
            var emptySlot = runtime.Start("work_pine_wood", 7);

            Assert.That(missing.success, Is.False);
            Assert.That(emptySlot.success, Is.False);
            Assert.That(HasIssue(emptySlot.issues, "HeroSlot"), Is.True);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(state.GetActivityExecutions(), Is.Empty);
        }

        [Test]
        public void Tick_RepeatableProcessesCyclesRewardsAndLimit()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new MemorySaveStorage());
            Assert.That(runtime.Start("work_pine_wood", 0).success, Is.True);

            var firstTick = runtime.Tick(25f);
            var execution = state.GetActivityExecutions()[0];

            Assert.That(firstTick.success, Is.True);
            Assert.That(firstTick.processedCycles, Is.EqualTo(2));
            Assert.That(execution.completedCycles, Is.EqualTo(2));
            Assert.That(execution.elapsedSeconds, Is.EqualTo(5f));
            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(2));
            Assert.That(state.GetHeroSkillExp("ren", "skill_gathering"), Is.EqualTo(2));
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
        public void Tick_RewardFailureKeepsRepeatableExecution()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new MemorySaveStorage());
            Assert.That(runtime.Start("bad_cycle", 0).success, Is.True);
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
        public void Tick_OneShotCompletesBothMomentsAndReleasesHero()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new MemorySaveStorage());
            Assert.That(runtime.Start("one_shot_new", 0).success, Is.True);

            var result = runtime.Tick(5f);

            Assert.That(result.success, Is.True);
            Assert.That(state.GetActivityExecutions(), Is.Empty);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.IsActivityCompleted("one_shot_new"), Is.True);
            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(1));
            Assert.That(state.GetCurrency("gold_id"), Is.EqualTo(2));
            foreach (var item in state.ToSaveData().items)
                Assert.That(item.itemId, Is.Not.EqualTo("gold_id"));
        }

        [Test]
        public void CancelClearsExecutionWithoutRewardOrRefund()
        {
            var state = NewState();
            var runtime = new ActivityRuntimeService(state, new MemorySaveStorage());
            var fatigue = state.GetHeroFatigue("ren");
            var start = runtime.Start("work_pine_wood", 0);
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
            var runtime = new ActivityRuntimeService(state, storage);
            Assert.That(runtime.Start("work_pine_wood", 0).success, Is.True);
            Assert.That(runtime.Tick(3f).success, Is.True);

            var restored = SaveService.Load(storage);
            var execution = restored.GetActivityExecutions()[0];

            Assert.That(execution.activityId, Is.EqualTo("work_pine_wood"));
            Assert.That(execution.heroId, Is.EqualTo("ren"));
            Assert.That(execution.elapsedSeconds, Is.EqualTo(3f));
            Assert.That(restored.IsHeroBusy("ren"), Is.True);
            Assert.That(restored.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo(execution.executionId));
        }

        private static PlayerState NewState()
        {
            var state = new PlayerState(new SaveData());
            state.AddHero("ren");
            state.SetHeroSlot(0, "ren");
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
                        new HeroConfigDto { heroId = "ren", enabled = true }
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
                        Reward("one_shot_new", "Gold", "ignored", 2, "OnFirstComplete")
                    }
                },
                null,
                null,
                null,
                null,
                null,
                null,
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
