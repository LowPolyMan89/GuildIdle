using System;
using GuildIdle.Configs;

namespace GuildIdle.Editor.Configs
{
    /// <summary>
    /// Fluent builder for creating test ConfigDatabase instances.
    /// Reduces duplication across PlayerStateTests, ActivityResolverTests, and PlayerLifecycleTests.
    /// </summary>
    public sealed class TestConfigDatabaseBuilder
    {
        private ItemsRuntimeConfigDto _items;
        private HeroesRuntimeConfigDto _heroes;
        private ActivitiesRuntimeConfigDto _activities;
        private BuildingsRuntimeConfigDto _buildings;
        private QuestRuntimeConfigDto _quests;
        private EnemiesRuntimeConfigDto _enemies;
        private FormulaRuntimeConfigDto _formulas;
        private LootRuntimeConfigDto _loot;
        private MapRuntimeConfigDto _map;
        private StorageRuntimeConfigDto _storage;
        private LocalisationRuntimeConfigDto _localisation;

        public TestConfigDatabaseBuilder()
        {
            // Start with everything null — caller adds what they need
        }

        public ConfigDatabase Build()
        {
            return new ConfigDatabase(
                _items,
                _heroes,
                _activities,
                _buildings,
                _quests,
                _enemies,
                _formulas,
                _loot,
                _map,
                _storage,
                _localisation);
        }

        // ──────────────────────────────────────────────
        //  Items
        // ──────────────────────────────────────────────

        public TestConfigDatabaseBuilder WithMinimalItems()
        {
            _items = new ItemsRuntimeConfigDto
            {
                resources = new[]
                {
                    new ResourceConfigDto { id = "resource_pine_wood", kind = "resource" },
                    new ResourceConfigDto { id = "resource_stone", kind = "resource" },
                    new ResourceConfigDto { id = "resource_flax", kind = "resource" },
                    new ResourceConfigDto { id = "resource_thin_hide", kind = "resource" }
                },
                equipmentWeapons = new[]
                {
                    new EquipmentWeaponConfigDto { id = "item_wooden_club", kind = "equipment", equipmentSlot = "weapon" },
                    new EquipmentWeaponConfigDto { id = "item_unused_sword", kind = "equipment", equipmentSlot = "weapon" }
                },
                recipes = new[]
                {
                    new RecipeConfigDto { id = "recipe_flax_thread", kind = "recipe" }
                },
                consumables = new[]
                {
                    new ConsumableConfigDto
                    {
                        id = "consumable_hunting_potion",
                        kind = "consumable",
                        usePlace = "combat",
                        useCondition = "hp_percent<=40",
                        effects = new[] { "RestoreHealthFlat:25" },
                        cooldownSeconds = 5d,
                        checkIntervalSeconds = 1d
                    }
                },
                currencies = new[]
                {
                    new CurrencyConfigDto { currencyId = "gold_id" },
                    new CurrencyConfigDto { currencyId = "gem_id" }
                }
            };
            return this;
        }

        // ──────────────────────────────────────────────
        //  Heroes
        // ──────────────────────────────────────────────

        public TestConfigDatabaseBuilder WithMinimalHeroes()
        {
            _heroes = new HeroesRuntimeConfigDto
            {
                heroes = new[]
                {
                    new HeroConfigDto
                    {
                        heroId = "ren",
                        enabled = true,
                        uniqueSkillIds = new[] { "reliable_hands", "will_to_live" },
                        baseStats = new HeroBaseStatsDto { endurance = 5 }
                    },
                    new HeroConfigDto
                    {
                        heroId = "aska",
                        enabled = true
                    }
                },
                heroSkillEffects = new[]
                {
                    new HeroSkillEffectConfigDto
                    {
                        skillId = "reliable_hands",
                        effectId = "reliable_hands_extra_resource",
                        interval = "5"
                    },
                    new HeroSkillEffectConfigDto
                    {
                        skillId = "will_to_live",
                        effectId = "will_to_live_prevent_death",
                        interval = "0"
                    }
                }
            };
            return this;
        }

        // ──────────────────────────────────────────────
        //  Activities — resolver test suite
        // ──────────────────────────────────────────────

        public TestConfigDatabaseBuilder WithResolverActivities()
        {
            _activities = new ActivitiesRuntimeConfigDto
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
                    Reward("direct_rewards", "Gold", "gold_id", 7, "OnComplete"),
                    Reward("direct_rewards", "Currency", "gem_id", 3, "OnComplete"),
                    Reward("direct_rewards", "Hero", "ren", 1, "OnComplete"),
                    Reward("direct_rewards", "BuildingUnlock", "building_underwood", 1, "OnComplete"),
                    Reward("direct_rewards", "UnlockBuilding", "building_underwood", 1, "OnComplete"),
                    Reward("direct_rewards", "MapAccess", "fields_1", 1, "OnComplete"),
                    Reward("direct_rewards", "UnlockLocation", "fields_1", 1, "OnComplete"),
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
            return this;
        }

        // ──────────────────────────────────────────────
        //  Activities — minimal (for PlayerState / lifecycle tests)
        // ──────────────────────────────────────────────

        public TestConfigDatabaseBuilder WithMinimalActivities()
        {
            _activities = new ActivitiesRuntimeConfigDto
            {
                activities = new[]
                {
                    new ActivityConfigDto { id = "combat_first_map_node" },
                    new ActivityConfigDto { id = "combat_clear_hall_forest" }
                },
                skills = new[]
                {
                    new SkillConfigDto { skillId = "skill_gathering" },
                    new SkillConfigDto { skillId = "skill_processing" },
                    new SkillConfigDto { skillId = "skill_crafting" },
                    new SkillConfigDto { skillId = "skill_hunting" },
                    new SkillConfigDto { skillId = "skill_production" },
                    new SkillConfigDto { skillId = "skill_exploration" },
                    new SkillConfigDto { skillId = "skill_construction" },
                    new SkillConfigDto { skillId = "skill_combat" }
                },
                skillsProgression = new[]
                {
                    new SkillProgressionConfigDto { level = 1, totalExpRequired = 0 },
                    new SkillProgressionConfigDto { level = 2, totalExpRequired = 100 }
                }
            };
            _quests = new QuestRuntimeConfigDto
            {
                storyQuests = new[]
                {
                    new StoryQuestConfigDto { questId = "quest_build_hut", enabled = true },
                    new StoryQuestConfigDto { questId = "quest_clear_underwood", enabled = true },
                    new StoryQuestConfigDto { questId = "quest_disabled_new_game", enabled = false }
                },
                questStartConditions = new[]
                {
                    new QuestStartConditionConfigDto { questId = "quest_build_hut", conditionType = "NewGame" },
                    new QuestStartConditionConfigDto { questId = "quest_clear_underwood", conditionType = "NewGame" },
                    new QuestStartConditionConfigDto { questId = "quest_disabled_new_game", conditionType = "NewGame" }
                },
                questSteps = new[]
                {
                    new QuestStepConfigDto { questId = "quest_build_hut", stepId = "step_collect_wood", stepOrder = 10 },
                    new QuestStepConfigDto { questId = "quest_build_hut", stepId = "step_collect_stone", stepOrder = 20 },
                    new QuestStepConfigDto { questId = "quest_build_hut", stepId = "step_build_hut", stepOrder = 30 },
                    new QuestStepConfigDto { questId = "quest_clear_underwood", stepId = "step_clear_underwood", stepOrder = 10 },
                    new QuestStepConfigDto { questId = "quest_disabled_new_game", stepId = "step_disabled", stepOrder = 10 }
                }
            };
            return this;
        }

        // ──────────────────────────────────────────────
        //  Buildings
        // ──────────────────────────────────────────────

        public TestConfigDatabaseBuilder WithQuestStartConditions(params QuestStartConditionConfigDto[] conditions)
        {
            _quests ??= new QuestRuntimeConfigDto();
            _quests.questStartConditions = conditions ?? Array.Empty<QuestStartConditionConfigDto>();
            return this;
        }

        public TestConfigDatabaseBuilder WithMinimalBuildings()
        {
            _buildings = new BuildingsRuntimeConfigDto
            {
                buildings = new[]
                {
                    new BuildingConfigDto { buildingId = "building_hall", levels = 1, startLevel = 0, visibleAtStart = true },
                    new BuildingConfigDto { buildingId = "building_underwood", levels = 1, startLevel = 0, visibleAtStart = true },
                    new BuildingConfigDto { buildingId = "building_stone_pile", levels = 1, startLevel = 1, visibleAtStart = true },
                    new BuildingConfigDto { buildingId = "building_campfire", levels = 1, startLevel = 0, visibleAtStart = true },
                    new BuildingConfigDto { buildingId = "building_warehouse", levels = 0, startLevel = 0, visibleAtStart = true },
                    new BuildingConfigDto { buildingId = "building_tavern", levels = 1, startLevel = 1, visibleAtStart = false },
                    new BuildingConfigDto { buildingId = "building_watchtower", levels = 1, startLevel = 0, visibleAtStart = false, clickableRequirement = "building_hall:1" },
                    new BuildingConfigDto { buildingId = "building_hidden", levels = 1, startLevel = 0, visibleAtStart = false }
                },
                buildingLevels = new[]
                {
                    new BuildingLevelConfigDto { buildingId = "building_hall", level = 0, activeHeroLimit = 1 },
                    new BuildingLevelConfigDto { buildingId = "building_hall", level = 1, activeHeroLimit = 1 },
                    new BuildingLevelConfigDto { buildingId = "building_underwood", level = 0 },
                    new BuildingLevelConfigDto { buildingId = "building_underwood", level = 1 },
                    new BuildingLevelConfigDto { buildingId = "building_stone_pile", level = 0 },
                    new BuildingLevelConfigDto { buildingId = "building_stone_pile", level = 1 },
                    new BuildingLevelConfigDto { buildingId = "building_campfire", level = 0 },
                    new BuildingLevelConfigDto { buildingId = "building_campfire", level = 1 },
                    new BuildingLevelConfigDto { buildingId = "building_warehouse", level = 0 },
                    new BuildingLevelConfigDto { buildingId = "building_tavern", level = 0 },
                    new BuildingLevelConfigDto { buildingId = "building_tavern", level = 1 },
                    new BuildingLevelConfigDto { buildingId = "building_watchtower", level = 0 },
                    new BuildingLevelConfigDto { buildingId = "building_watchtower", level = 1 },
                    new BuildingLevelConfigDto { buildingId = "building_hidden", level = 0 },
                    new BuildingLevelConfigDto { buildingId = "building_hidden", level = 1 }
                },
                settlementStageStarterHeroes = new[]
                {
                    new SettlementStageStarterHeroConfigDto { stageId = "stage_arrival", heroId = "ren", sortOrder = 10 }
                },
                settlementStageStarterEquipment = new[]
                {
                    new SettlementStageStarterEquipmentConfigDto
                    {
                        stageId = "stage_arrival",
                        heroId = "ren",
                        itemId = "item_wooden_club",
                        equipmentSlot = "weapon",
                        sortOrder = 10
                    }
                }
            };
            _quests ??= new QuestRuntimeConfigDto();
            _quests.stages = new[]
            {
                new StageConfigDto { stageId = "stage_arrival", enabled = true, nextStageId = "stage_2" },
                new StageConfigDto { stageId = "stage_2", enabled = true }
            };
            return this;
        }

        public TestConfigDatabaseBuilder WithMinimalStorage()
        {
            _storage = new StorageRuntimeConfigDto
            {
                storageRules = new[]
                {
                    new StorageRuleConfigDto { storageRuleId = "storage_resource", itemKind = "resource", mode = "stack", maxStack = 100, occupiesSlot = true },
                    new StorageRuleConfigDto { storageRuleId = "storage_consumable", itemKind = "consumable", mode = "stack", maxStack = 20, occupiesSlot = true },
                    new StorageRuleConfigDto { storageRuleId = "storage_recipe", itemKind = "recipe", mode = "stack", maxStack = 20, occupiesSlot = true },
                    new StorageRuleConfigDto { storageRuleId = "storage_equipment", itemKind = "equipment", mode = "single", maxStack = 1, occupiesSlot = true, allowQuality = true, allowInstanceId = true }
                },
                storageBuildings = new[] { new StorageBuildingConfigDto { buildingId = "building_warehouse", level = 0, slotCount = 20 } },
                itemStates = new[]
                {
                    new ItemStateConfigDto { stateId = "on_storage", isInStorage = true, occupiesCapacity = true, availabilityMode = "available", availableForCraft = true, availableForSale = true, availableForOrder = true, availableForEquip = true },
                    new ItemStateConfigDto { stateId = "equipped", requiresOwner = true, availabilityMode = "equipped" },
                    new ItemStateConfigDto { stateId = "reserved_for_task", isInStorage = true, occupiesCapacity = true, availabilityMode = "reserved" },
                    new ItemStateConfigDto { stateId = "in_task", availabilityMode = "in_action" },
                    new ItemStateConfigDto { stateId = "unavailable", availabilityMode = "unavailable" }
                }
            };
            return this;
        }

        public TestConfigDatabaseBuilder WithStageBootstrap(
            SettlementStageStarterHeroConfigDto[] starterHeroes,
            SettlementStageStarterEquipmentConfigDto[] starterEquipment)
        {
            if (_buildings == null)
                throw new InvalidOperationException("Buildings must be configured before stage bootstrap.");

            _buildings.settlementStageStarterHeroes = starterHeroes ?? Array.Empty<SettlementStageStarterHeroConfigDto>();
            _buildings.settlementStageStarterEquipment = starterEquipment ?? Array.Empty<SettlementStageStarterEquipmentConfigDto>();
            return this;
        }

        public TestConfigDatabaseBuilder WithResolverBuildings()
        {
            _buildings = new BuildingsRuntimeConfigDto
            {
                buildings = new[]
                {
                    new BuildingConfigDto { buildingId = "building_underwood", levels = 2 },
                    new BuildingConfigDto { buildingId = "building_warehouse", levels = 0 }
                },
                buildingLevels = new[]
                {
                    new BuildingLevelConfigDto { buildingId = "building_warehouse", level = 0 }
                },
                buildingActivities = new[]
                {
                    new BuildingActivityConfigDto
                    {
                        buildingId = "building_underwood",
                        buildingLevel = 1,
                        activityId = "work_pine_wood"
                    }
                }
            };
            _quests ??= new QuestRuntimeConfigDto();
            _quests.stages = new[] { new StageConfigDto { stageId = "stage_arrival", enabled = true } };
            return this;
        }

        // ──────────────────────────────────────────────
        //  Enemies
        // ──────────────────────────────────────────────

        public TestConfigDatabaseBuilder WithResolverEnemies()
        {
            _enemies = new EnemiesRuntimeConfigDto
            {
                enemyLoot = new[]
                {
                    new EnemyLootConfigDto { lootGroupId = "loot_enemy_test", enemyId = "enemy_test", lootId = "gold_id", minCount = 1, maxCount = 1, chancePercent = 100 },
                    new EnemyLootConfigDto { lootGroupId = "loot_enemy_test", enemyId = "enemy_test", lootId = "gem_id", minCount = 1, maxCount = 1, chancePercent = 100 }
                }
            };
            return this;
        }

        // ──────────────────────────────────────────────
        //  Formulas
        // ──────────────────────────────────────────────

        public TestConfigDatabaseBuilder WithFatigueFormula()
        {
            _formulas = new FormulaRuntimeConfigDto
            {
                formulas = new[]
                {
                    new FormulaConfigDto
                    {
                        formulaId = "hero_max_fatigue",
                        derivedStatId = "max_fatigue",
                        baseValue = 100,
                        primaryStat = "Endurance",
                        primaryStatMultiplier = 4,
                        levelMultiplier = 1,
                        rounding = "Round",
                        enabled = true
                    }
                }
            };
            return this;
        }

        // ──────────────────────────────────────────────
        //  Loot
        // ──────────────────────────────────────────────

        public TestConfigDatabaseBuilder WithResolverLoot()
        {
            _loot = new LootRuntimeConfigDto
            {
                lootTables = new[]
                {
                    new LootTableConfigDto { lootTableId = "weighted_one", rollMode = "WeightedOne", rollCountMin = 1, rollCountMax = 1, enabled = true },
                    new LootTableConfigDto { lootTableId = "weighted_many", rollMode = "WeightedMany", rollCountMin = 2, rollCountMax = 2, enabled = true },
                    new LootTableConfigDto { lootTableId = "guaranteed_all", rollMode = "GuaranteedAll", rollCountMin = 1, rollCountMax = 1, enabled = true },
                    new LootTableConfigDto { lootTableId = "hunting_rabbits_resources", rollMode = "WeightedMany", rollCountMin = 1, rollCountMax = 1, enabled = true },
                    new LootTableConfigDto { lootTableId = "invalid_drop", rollMode = "GuaranteedAll", rollCountMin = 1, rollCountMax = 1, enabled = true }
                },
                lootGroups = new[]
                {
                    Group("weighted_many", "default", "WeightedMany", 2),
                    Group("guaranteed_all", "default", "GuaranteedAll", 1),
                    Group("hunting_rabbits_resources", "default", "WeightedMany", 1)
                },
                lootTableEntries = new[]
                {
                    Entry("weighted_one", "weighted_one_wood", "resource", "resource_pine_wood", string.Empty),
                    Entry("weighted_many", "weighted_many_wood", "Resource", "resource_pine_wood", "default"),
                    Entry("guaranteed_all", "guaranteed_wood", "Resource", "resource_pine_wood", "default"),
                    Entry("guaranteed_all", "guaranteed_gold", "Gold", "gold_id", "default"),
                    Entry("hunting_rabbits_resources", "thin_hide", "Resource", "resource_thin_hide", "default"),
                    Entry("invalid_drop", "invalid_drop_entry", "UnknownDrop", "resource_pine_wood", string.Empty)
                }
            };
            return this;
        }

        // ──────────────────────────────────────────────
        //  Map
        // ──────────────────────────────────────────────

        public TestConfigDatabaseBuilder WithResolverMap()
        {
            _map = new MapRuntimeConfigDto
            {
                mapLocations = new[]
                {
                    new MapLocationConfigDto { locationId = "fields_1" }
                }
            };
            return this;
        }

        public TestConfigDatabaseBuilder WithMinimalMap()
        {
            _map = new MapRuntimeConfigDto
            {
                mapLocations = new[]
                {
                    new MapLocationConfigDto { locationId = "old_wolf_den_1_1" }
                }
            };
            return this;
        }

        // ──────────────────────────────────────────────
        //  Convenience: full resolver test database
        // ──────────────────────────────────────────────

        public TestConfigDatabaseBuilder WithFullResolverTestData()
        {
            return WithMinimalItems()
                .WithMinimalHeroes()
                .WithResolverActivities()
                .WithResolverBuildings()
                .WithResolverEnemies()
                .WithFatigueFormula()
                .WithResolverLoot()
                .WithResolverMap()
                .WithMinimalStorage();
        }

        public TestConfigDatabaseBuilder WithFullPlayerStateTestData()
        {
            return WithMinimalItems()
                .WithMinimalHeroes()
                .WithMinimalActivities()
                .WithMinimalBuildings()
                .WithFatigueFormula()
                .WithMinimalMap()
                .WithMinimalStorage();
        }

        // ──────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────

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
            return new LootGroupConfigDto
            {
                lootTableId = tableId,
                rollGroup = group,
                rollMode = mode,
                rollCountMin = count,
                rollCountMax = count,
                chance = 100
            };
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
    }
}
