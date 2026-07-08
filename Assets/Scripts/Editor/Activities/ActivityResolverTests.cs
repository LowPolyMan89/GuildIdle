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
        public void CanStart_ValidatesExecutorHeroSlotAndBusyExecution()
        {
            var state = NewState();
            var adapter = new PlayerStateActivityAdapter(state);
            Assert.That(state.AddHero("aska"), Is.True);
            Assert.That(state.SetHeroSlot(1, "aska"), Is.True);

            var slotMismatch = ActivityResolver.CanStart(Context("direct_rewards", slotIndex: 1), adapter);
            Assert.That(slotMismatch.canStart, Is.False);
            Assert.That(slotMismatch.issues[0].issueType, Is.EqualTo("HeroSlot"));

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
            state.SetHeroSlot(0, "ren");
            return state;
        }

        private static ActivityExecutionContext Context(string activityId, string heroId = "ren", int slotIndex = 0, string executionId = "exec_1")
        {
            return new ActivityExecutionContext
            {
                activityId = activityId,
                heroId = heroId,
                heroSlotIndex = slotIndex,
                executionId = executionId
            };
        }

        private static ConfigDatabase CreateDatabase()
        {
            return new ConfigDatabase(
                new ItemsRuntimeConfigDto
                {
                    resources = new[]
                    {
                        new ResourceConfigDto { id = "resource_pine_wood", kind = "resource" },
                        new ResourceConfigDto { id = "resource_flax", kind = "resource" },
                        new ResourceConfigDto { id = "resource_thin_hide", kind = "resource" }
                    },
                    equipmentWeapons = new[]
                    {
                        new EquipmentWeaponConfigDto { id = "item_wooden_club", kind = "equipment" }
                    },
                    recipes = new[]
                    {
                        new RecipeConfigDto { id = "recipe_flax_thread", kind = "recipe" }
                    },
                    consumables = new[]
                    {
                        new ConsumableConfigDto { id = "consumable_hunting_potion", kind = "consumable" }
                    },
                    currencies = new[]
                    {
                        new CurrencyConfigDto { currencyId = "gold_id" },
                        new CurrencyConfigDto { currencyId = "gem_id" }
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
                CreateActivities(),
                new BuildingsRuntimeConfigDto
                {
                    buildings = new[]
                    {
                        new BuildingConfigDto { buildingId = "building_underwood", levels = 2 }
                    }
                },
                new EnemiesRuntimeConfigDto
                {
                    enemyLoot = new[]
                    {
                        new EnemyLootConfigDto { lootGroupId = "loot_enemy_test", enemyId = "enemy_test", lootId = "gold_id", minCount = 1, maxCount = 1, chancePercent = 100 },
                        new EnemyLootConfigDto { lootGroupId = "loot_enemy_test", enemyId = "enemy_test", lootId = "gem_id", minCount = 1, maxCount = 1, chancePercent = 100 }
                    }
                },
                null,
                CreateLoot(),
                new MapRuntimeConfigDto
                {
                    mapLocations = new[]
                    {
                        new MapLocationConfigDto { locationId = "fields_1" }
                    }
                },
                null,
                null);
        }

        private static ActivitiesRuntimeConfigDto CreateActivities()
        {
            return new ActivitiesRuntimeConfigDto
            {
                activities = new[]
                {
                    new ActivityConfigDto { id = "work_pine_wood", isRepeatable = true, fatigueCost = 1 },
                    new ActivityConfigDto { id = "hunt_rabbits", isRepeatable = true },
                    new ActivityConfigDto { id = "cost_activity", fatigueCost = 5 },
                    new ActivityConfigDto { id = "direct_rewards" },
                    new ActivityConfigDto { id = "skill_required" },
                    new ActivityConfigDto { id = "once_complete", isRepeatable = false },
                    new ActivityConfigDto { id = "repeat_complete", isRepeatable = true },
                    new ActivityConfigDto { id = "first_complete", isRepeatable = false },
                    new ActivityConfigDto { id = "loot_activity", isRepeatable = false }
                },
                skills = new[]
                {
                    new SkillConfigDto { skillId = "skill_gathering" }
                },
                requirements = new[]
                {
                    new ActivityRequirementConfigDto { activityId = "work_pine_wood", reqType = "BuildingLevel", targetId = "building_underwood", value = 1 },
                    new ActivityRequirementConfigDto { activityId = "hunt_rabbits", reqType = "HeroAvailable", targetId = "aska", value = 1 },
                    new ActivityRequirementConfigDto { activityId = "skill_required", reqType = "SkillLevel", targetId = "skill_gathering", value = 2 },
                    new ActivityRequirementConfigDto { activityId = "cost_activity", reqType = "Resource", targetId = "resource_pine_wood", value = 2, consume = true },
                    new ActivityRequirementConfigDto { activityId = "cost_activity", reqType = "Currency", targetId = "gold_id", value = 5, consume = true }
                },
                rewards = new[]
                {
                    Reward("direct_rewards", "Resource", "resource_pine_wood", 2, "OnComplete"),
                    Reward("direct_rewards", "Equipment", "item_wooden_club", 1, "OnComplete"),
                    Reward("direct_rewards", "Consumable", "consumable_hunting_potion", 1, "OnComplete"),
                    Reward("direct_rewards", "Recipe", "recipe_flax_thread", 1, "OnComplete"),
                    Reward("direct_rewards", "Gold", "ignored", 7, "OnComplete"),
                    Reward("direct_rewards", "Currency", "gem_id", 3, "OnComplete"),
                    Reward("direct_rewards", "Hero", "ren", 1, "OnComplete"),
                    Reward("direct_rewards", "BuildingUnlock", "building_underwood", 1, "OnComplete"),
                    Reward("direct_rewards", "MapAccess", "fields_1", 1, "OnComplete"),
                    Reward("direct_rewards", "SkillExp", "skill_gathering", 5, "OnComplete"),
                    Reward("once_complete", "Resource", "resource_pine_wood", 1, "OnComplete"),
                    Reward("repeat_complete", "Resource", "resource_flax", 1, "OnComplete"),
                    Reward("first_complete", "Resource", "resource_thin_hide", 1, "OnFirstComplete"),
                    Reward("loot_activity", "LootTable", "hunting_rabbits_resources", 1, "OnComplete")
                },
                skillsProgression = new[]
                {
                    new SkillProgressionConfigDto { level = 1, totalExpRequired = 0 },
                    new SkillProgressionConfigDto { level = 2, totalExpRequired = 100 }
                }
            };
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

        private static LootRuntimeConfigDto CreateLoot()
        {
            return new LootRuntimeConfigDto
            {
                lootTables = new[]
                {
                    new LootTableConfigDto { lootTableId = "weighted_one", rollMode = "WeightedOne", rollCountMin = 1, rollCountMax = 1, enabled = true },
                    new LootTableConfigDto { lootTableId = "weighted_many", rollMode = "WeightedMany", rollCountMin = 2, rollCountMax = 2, enabled = true },
                    new LootTableConfigDto { lootTableId = "guaranteed_all", rollMode = "GuaranteedAll", rollCountMin = 1, rollCountMax = 1, enabled = true },
                    new LootTableConfigDto { lootTableId = "hunting_rabbits_resources", rollMode = "WeightedMany", rollCountMin = 1, rollCountMax = 1, enabled = true }
                },
                lootGroups = new[]
                {
                    Group("weighted_many", "default", "WeightedMany", 2),
                    Group("guaranteed_all", "default", "GuaranteedAll", 1),
                    Group("hunting_rabbits_resources", "default", "WeightedMany", 1)
                },
                lootTableEntries = new[]
                {
                    Entry("weighted_one", "weighted_one_wood", "Resource", "resource_pine_wood", string.Empty),
                    Entry("weighted_many", "weighted_many_wood", "Resource", "resource_pine_wood", "default"),
                    Entry("guaranteed_all", "guaranteed_wood", "Resource", "resource_pine_wood", "default"),
                    Entry("guaranteed_all", "guaranteed_gold", "Gold", "gold_id", "default"),
                    Entry("hunting_rabbits_resources", "thin_hide", "Resource", "resource_thin_hide", "default")
                }
            };
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

        private static LootGroupConfigDto Group(string tableId, string group, string mode, int count)
        {
            return new LootGroupConfigDto { lootTableId = tableId, rollGroup = group, rollMode = mode, rollCountMin = count, rollCountMax = count, chance = 100 };
        }

        private static LootTableEntryConfigDto Entry(string tableId, string entryId, string type, string targetId, string group)
        {
            return new LootTableEntryConfigDto
            {
                lootTableId = tableId,
                entryId = entryId,
                dropType = type,
                targetId = targetId,
                weight = 100,
                min = 1,
                max = 1,
                chance = 100,
                requiredRollGroup = group
            };
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
