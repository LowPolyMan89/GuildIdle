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
    public sealed class ActivityResolverTests
    {
        [SetUp]
        public void SetUp()
        {
            RuntimeConfigs.SetDatabaseForTests(CreateDatabase());
        }

        [Test]
        public void CanStart_ReturnsMissingRequirements()
        {
            var state = NewState();
            var adapter = new PlayerStateActivityAdapter(state);

            var missingBuilding = ActivityResolver.CanStart(Context("work_pine_wood"), adapter);
            Assert.That(missingBuilding.canStart, Is.False);
            Assert.That(missingBuilding.issues[0].issueType, Is.EqualTo("BuildingLevel"));

            Assert.That(state.UnlockBuilding("building_underwood"), Is.True);
            Assert.That(state.SetBuildingLevel("building_underwood", 1), Is.True);
            Assert.That(ActivityResolver.CanStart(Context("work_pine_wood"), adapter).canStart, Is.True);

            var missingHero = ActivityResolver.CanStart(Context("hunt_rabbits"), adapter);
            Assert.That(missingHero.canStart, Is.False);
            Assert.That(missingHero.issues[0].targetId, Is.EqualTo("aska"));

            Assert.That(state.AddHero("aska"), Is.True);
            Assert.That(ActivityResolver.CanStart(Context("hunt_rabbits"), adapter).canStart, Is.True);
        }

        [Test]
        public void UnknownActivity_ReturnsIssueWithoutThrowing()
        {
            LogAssert.Expect(LogType.Error, "[ActivityResolver] Unknown activity id 'missing_activity'.");

            var result = ActivityResolver.CanStart(Context("missing_activity"), new PlayerStateActivityAdapter(NewState()));

            Assert.That(result.canStart, Is.False);
            Assert.That(result.issues, Has.Length.EqualTo(1));
            Assert.That(result.issues[0].isError, Is.True);
        }

        [Test]
        public void CanStart_ValidatesExecutorHeroAndBusyExecution()
        {
            var state = NewState();
            var adapter = new PlayerStateActivityAdapter(state);

            var missingHero = ActivityResolver.CanStart(Context("direct_rewards", heroId: "aska"), adapter);
            Assert.That(missingHero.canStart, Is.False);
            Assert.That(missingHero.issues[0].issueType, Is.EqualTo("HeroAvailable"));

            Assert.That(state.SetHeroBusy("ren", "exec_1"), Is.True);
            Assert.That(ActivityResolver.CanStart(Context("direct_rewards", executionId: "exec_1"), adapter).canStart, Is.True);

            var busyDifferentExecution = ActivityResolver.CanStart(Context("direct_rewards", executionId: "exec_2"), adapter);
            Assert.That(busyDifferentExecution.canStart, Is.False);
            Assert.That(busyDifferentExecution.issues[0].issueType, Is.EqualTo("HeroBusy"));
        }

        [Test]
        public void CanStart_SkillLevelChecksExecutorHero()
        {
            var state = NewState();
            var adapter = new PlayerStateActivityAdapter(state);
            var context = Context("skill_required");

            var missingSkill = ActivityResolver.CanStart(context, adapter);
            Assert.That(missingSkill.canStart, Is.False);
            Assert.That(missingSkill.issues[0].issueType, Is.EqualTo("SkillLevel"));

            Assert.That(state.AddHeroSkillExp("ren", "skill_gathering", 100), Is.True);
            Assert.That(ActivityResolver.CanStart(context, adapter).canStart, Is.True);
        }

        [Test]
        public void ApplyCost_IsAtomicForConsumableRequirements()
        {
            var state = NewState();
            var adapter = new PlayerStateActivityAdapter(state);
            var maxFatigue = state.GetHeroFatigue("ren");
            Assert.That(state.AddItem("resource_pine_wood", 2), Is.True);
            Assert.That(state.AddCurrency("gold_id", 4), Is.True);

            var failed = ActivityResolver.ApplyCost(Context("cost_activity"), adapter);
            Assert.That(failed.success, Is.False);
            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(2));
            Assert.That(state.GetCurrency("gold_id"), Is.EqualTo(4));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(maxFatigue));

            Assert.That(state.AddCurrency("gold_id", 1), Is.True);
            var paid = ActivityResolver.ApplyCost(Context("cost_activity"), adapter);
            Assert.That(paid.success, Is.True);
            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(0));
            Assert.That(state.GetCurrency("gold_id"), Is.EqualTo(0));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(maxFatigue - 5));
        }

        [Test]
        public void ApplyRewards_AppliesDirectRewardsAndCurrencyRules()
        {
            var state = NewState();
            var adapter = new PlayerStateActivityAdapter(state);

            var result = ActivityRewardResolver.ApplyRewards(Context("direct_rewards"), "OnComplete", adapter, new FixedRandom());

            Assert.That(result.success, Is.True);
            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(2));
            Assert.That(state.GetItem("item_wooden_club"), Is.EqualTo(1));
            Assert.That(state.GetItem("consumable_hunting_potion"), Is.EqualTo(1));
            Assert.That(state.GetItem("recipe_flax_thread"), Is.EqualTo(1));
            Assert.That(state.GetCurrency("gold_id"), Is.EqualTo(7));
            Assert.That(state.GetCurrency("gem_id"), Is.EqualTo(3));
            Assert.That(state.HasHero("ren"), Is.True);
            Assert.That(state.IsBuildingUnlocked("building_underwood"), Is.True);
            Assert.That(state.IsLocationUnlocked("fields_1"), Is.True);
            Assert.That(state.GetHeroSkillExp("ren", "skill_gathering"), Is.EqualTo(5));
            Assert.That(HasRewardType(result.rewards, "BuildingUnlock"), Is.True);
            Assert.That(HasRewardType(result.rewards, "UnlockBuilding"), Is.True);
            Assert.That(HasRewardType(result.rewards, "MapAccess"), Is.True);
            Assert.That(HasRewardType(result.rewards, "UnlockLocation"), Is.True);
            Assert.That(HasHeroSkillExpReward(result.rewards), Is.True);

            var saveData = state.ToSaveData();
            foreach (var item in saveData.items)
                Assert.That(item.itemId, Is.Not.EqualTo("gold_id"));
        }

        [Test]
        public void ApplyRewards_UsesGrantMomentCompletionSemantics()
        {
            var state = NewState();
            var adapter = new PlayerStateActivityAdapter(state);

            Assert.That(ActivityRewardResolver.ApplyRewards(Context("once_complete"), "OnComplete", adapter, new FixedRandom()).success, Is.True);
            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(1));
            var duplicate = ActivityRewardResolver.ApplyRewards(Context("once_complete"), "OnComplete", adapter, new FixedRandom());
            Assert.That(duplicate.skippedDuplicate, Is.True);
            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(1));

            Assert.That(ActivityRewardResolver.ApplyRewards(Context("repeat_complete"), "OnComplete", adapter, new FixedRandom()).success, Is.True);
            Assert.That(ActivityRewardResolver.ApplyRewards(Context("repeat_complete"), "OnComplete", adapter, new FixedRandom()).success, Is.True);
            Assert.That(state.GetItem("resource_flax"), Is.EqualTo(2));

            Assert.That(ActivityRewardResolver.ApplyRewards(Context("first_complete"), "OnFirstComplete", adapter, new FixedRandom()).success, Is.True);
            Assert.That(ActivityRewardResolver.ApplyRewards(Context("first_complete"), "OnFirstComplete", adapter, new FixedRandom()).skippedDuplicate, Is.True);
            Assert.That(state.GetItem("resource_thin_hide"), Is.EqualTo(1));
        }

        [Test]
        public void LootResolver_RollsTableModesAndEnemyGoldAsCurrency()
        {
            var weightedOne = LootResolver.RollLootTable("weighted_one", new FixedRandom());
            Assert.That(weightedOne.success, Is.True);
            Assert.That(weightedOne.drops, Has.Length.EqualTo(1));

            var weightedMany = LootResolver.RollLootTable("weighted_many", new FixedRandom());
            Assert.That(weightedMany.success, Is.True);
            Assert.That(weightedMany.drops, Has.Length.EqualTo(2));

            var guaranteed = LootResolver.RollLootTable("guaranteed_all", new FixedRandom());
            Assert.That(guaranteed.success, Is.True);
            Assert.That(guaranteed.drops, Has.Length.EqualTo(2));

            var enemyLoot = LootResolver.RollEnemyLoot("loot_enemy_test", new FixedRandom());
            Assert.That(enemyLoot.success, Is.True);
            Assert.That(enemyLoot.drops[0].targetId, Is.EqualTo("gold_id"));
            Assert.That(enemyLoot.drops[0].isCurrency, Is.True);
            Assert.That(enemyLoot.drops[1].targetId, Is.EqualTo("gem_id"));
            Assert.That(enemyLoot.drops[1].isCurrency, Is.True);
        }

        [Test]
        public void LootResolver_RejectsUnknownDropType()
        {
            LogAssert.Expect(LogType.Error, "[LootResolver] Unsupported drop type 'UnknownDrop' from 'invalid_drop_entry'.");

            var result = LootResolver.RollLootTable("invalid_drop", new FixedRandom());

            Assert.That(result.success, Is.False);
            Assert.That(result.issues, Has.Length.EqualTo(1));
            Assert.That(result.issues[0], Does.Contain("Unsupported drop type 'UnknownDrop'"));
        }

        [Test]
        public void ApplyRewards_CanApplyLootTableAndSaveRoundtrip()
        {
            var state = NewState();
            var adapter = new PlayerStateActivityAdapter(state);

            var result = ActivityRewardResolver.ApplyRewards(Context("loot_activity"), "OnComplete", adapter, new FixedRandom());
            Assert.That(result.success, Is.True);
            Assert.That(state.GetItem("resource_thin_hide"), Is.EqualTo(1));

            var storage = new MemorySaveStorage();
            Assert.That(SaveService.Save(state, storage), Is.True);
            var restored = SaveService.Load(storage);
            Assert.That(restored.GetItem("resource_thin_hide"), Is.EqualTo(1));
        }

        private static PlayerState NewState()
        {
            var state = new PlayerState(new SaveData());
            state.AddHero("ren");
            return state;
        }

        private static ActivityExecutionContext Context(string activityId, string heroId = "ren", string executionId = "exec_1")
        {
            return new ActivityExecutionContext
            {
                activityId = activityId,
                heroId = heroId,
                executionId = executionId
            };
        }

        private static ConfigDatabase CreateDatabase()
        {
            return new TestConfigDatabaseBuilder()
                .WithFullResolverTestData()
                .Build();
        }


        private static bool HasHeroSkillExpReward(ActivityAppliedReward[] rewards)
        {
            foreach (var reward in rewards)
            {
                if (reward.rewardType == "SkillExp" &&
                    reward.ownerType == "Hero" &&
                    reward.ownerId == "ren" &&
                    !reward.isResultOnly)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasRewardType(ActivityAppliedReward[] rewards, string rewardType)
        {
            foreach (var reward in rewards)
            {
                if (reward.rewardType == rewardType)
                    return true;
            }

            return false;
        }



        private sealed class FixedRandom : IActivityRandom
        {
            public int RangeInclusive(int min, int max)
            {
                return min;
            }

            public float Percent()
            {
                return 0f;
            }
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
