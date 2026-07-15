using System;
using System.Collections.Generic;
using System.IO;
using GuildIdle.Configs;
using GuildIdle.Core;
using NUnit.Framework;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class ConfigCrossConfigValidatorTests
    {
        private const string TestRoot = "Temp/ConfigCrossConfigValidatorTests";
        private const string RuntimeTestRoot = "Assets/Temp/ConfigCrossConfigValidatorTests";

        [TearDown]
        public void TearDown()
        {
            var fullPath = FullProjectPath(TestRoot);
            if (Directory.Exists(fullPath))
                Directory.Delete(fullPath, true);

            var runtimeFullPath = FullProjectPath(RuntimeTestRoot);
            if (Directory.Exists(runtimeFullPath))
                Directory.Delete(runtimeFullPath, true);
        }

        [Test]
        public void Validate_ActivityCombatDetailsEnemyGroupIdSucceeds()
        {
            var collection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityCombatDownload("enemy_group_rats")),
                Source("enemies_configs", "GuildIdle - Enemies Configs", "enemies.json", EnemiesDownload(enemyLootId: string.Empty)));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(report.Warnings, Is.Empty);
        }

        [Test]
        public void Validate_DangerAndBuildReferencesUseUniversalFormulaRegistry()
        {
            var valid = Collection(
                Source("activity_configs", "Activity Configs", "activity-formulas.json", Download(
                    Sheet("Activities", Row("id"), Row("hunt_rabbits")),
                    Sheet("Skills", Row("skill_id"), Row("skill_construction")),
                    Sheet("DangerEncounters", Row("danger_encounter_id", "activity_id", "enemy_group_id", "risk_formula_id"), Row("danger_rabbits", "hunt_rabbits", "enemy_group_rats", "formula_risk")))),
                Source("enemies_configs", "Enemies Configs", "enemies-formulas.json", EnemiesDownload(string.Empty)),
                Source("formula_configs", "Formula Configs", "formulas.json", Download(
                    Sheet("HeroDerivedStats", Row("formula_id"), Row("formula_risk"), Row("formula_build")))),
                Source("buildings_configs", "Buildings Configs", "buildings-formulas.json", Download(
                    Sheet("Index", Row("building_id", "levels", "start_level", "clickable_requirement"), Row("building_hall", "1", "1", "")),
                    Sheet("Hall", Row("building_id", "building_hall"), Row("level", "source_activity_id", "build_formula_id", "skill_id", "active_hero_limit"), Row("1", "build_hall", "formula_build", "skill_construction", "1")),
                    Sheet("SettlementStages", Row("stage_id", "enabled"), Row("stage_2", "TRUE")))));

            var validReport = ConfigCrossConfigValidator.Validate(valid);

            Assert.That(validReport.Success, Is.True, validReport.ToDisplayMessage());

            var invalid = Collection(
                Source("activity_configs", "Activity Configs", "activity-formulas-invalid.json", Download(
                    Sheet("Activities", Row("id"), Row("hunt_rabbits")),
                    Sheet("Skills", Row("skill_id"), Row("skill_construction")),
                    Sheet("DangerEncounters", Row("danger_encounter_id", "activity_id", "enemy_group_id", "risk_formula_id"), Row("danger_rabbits", "hunt_rabbits", "enemy_group_rats", "missing_risk")))),
                Source("enemies_configs", "Enemies Configs", "enemies-formulas-invalid.json", EnemiesDownload(string.Empty)),
                Source("formula_configs", "Formula Configs", "formulas-invalid.json", Download(
                    Sheet("HeroDerivedStats", Row("formula_id"), Row("formula_risk"), Row("formula_build")))),
                Source("buildings_configs", "Buildings Configs", "buildings-formulas-invalid.json", Download(
                    Sheet("Index", Row("building_id", "levels", "start_level", "clickable_requirement"), Row("building_hall", "1", "1", "")),
                    Sheet("Hall", Row("building_id", "building_hall"), Row("level", "source_activity_id", "build_formula_id", "skill_id", "active_hero_limit"), Row("1", "build_hall", "0", "skill_construction", "1")),
                    Sheet("SettlementStages", Row("stage_id", "enabled"), Row("stage_2", "TRUE")))));

            var invalidReport = ConfigCrossConfigValidator.Validate(invalid);
            var message = invalidReport.ToDisplayMessage();

            Assert.That(invalidReport.Success, Is.False);
            Assert.That(message, Does.Contain("risk_formula_id").And.Contain("missing_risk"));
            Assert.That(message, Does.Contain("Hall row 3 column 'build_formula_id' value '0'"));
        }

        [Test]
        public void Validate_BuildingCraftablesUseCraftDefinitionRegistry()
        {
            var items = Source("items_configs", "Items Configs", "craft-items.json", Download(
                Sheet("CraftDefinitions", Row("craft_id"), Row("craft_known"))));
            var validBuildings = Source("buildings_configs", "Buildings Configs", "craft-buildings.json", Download(
                Sheet("Index", Row("building_id", "levels", "start_level", "clickable_requirement"), Row("building_hall", "0", "0", "")),
                Sheet("Hall", Row("building_id", "building_hall"), Row("level", "source_activity_id", "active_hero_limit"), Row("0", "", "1")),
                Sheet("Craftables - Hall", Row("building_id", "building_level", "craft_id"), Row("building_hall", "0", "craft_known")),
                Sheet("SettlementStages", Row("stage_id", "enabled"), Row("stage_2", "TRUE"))));

            var validReport = ConfigCrossConfigValidator.Validate(Collection(items, validBuildings));
            Assert.That(validReport.Success, Is.True, validReport.ToDisplayMessage());

            var invalidBuildings = Source("buildings_configs", "Buildings Configs", "craft-buildings-invalid.json", Download(
                Sheet("Index", Row("building_id", "levels", "start_level", "clickable_requirement"), Row("building_hall", "0", "0", "")),
                Sheet("Hall", Row("building_id", "building_hall"), Row("level", "source_activity_id", "active_hero_limit"), Row("0", "", "1")),
                Sheet("Craftables - Hall", Row("building_id", "building_level", "craft_id"), Row("building_hall", "0", "craft_missing")),
                Sheet("SettlementStages", Row("stage_id", "enabled"), Row("stage_2", "TRUE"))));

            var invalidReport = ConfigCrossConfigValidator.Validate(Collection(items, invalidBuildings));
            Assert.That(invalidReport.Success, Is.False);
            Assert.That(invalidReport.ToDisplayMessage(), Does.Contain("craft_id").And.Contain("CraftDefinitions.craft_id"));
        }

        [Test]
        public void Validate_CraftReferencesResolveOnlyEnabledRuntimeItems()
        {
            const string staleRuntime = "{\"recipes\":[{\"id\":\"recipe_old\",\"kind\":\"recipe\"}]}";

            var disabledRecipeReference = Collection(Source(
                "items_configs",
                "GuildIdle - Items Configs",
                "items-disabled-recipe.json",
                ItemsCraftReferenceDownload("resource_pine_wood", "recipe_old", "TRUE"),
                staleRuntime));
            var disabledRecipeReport = ConfigCrossConfigValidator.Validate(disabledRecipeReference);

            Assert.That(disabledRecipeReport.Success, Is.False);
            Assert.That(disabledRecipeReport.ToDisplayMessage(), Does.Contain("required_recipe_item_id").And.Contain("Recipes.id registry"));

            var disabledTargetReference = Collection(Source(
                "items_configs",
                "GuildIdle - Items Configs",
                "items-disabled-target.json",
                ItemsCraftReferenceDownload("recipe_old", "", "TRUE"),
                staleRuntime));
            var disabledTargetReport = ConfigCrossConfigValidator.Validate(disabledTargetReference);

            Assert.That(disabledTargetReport.Success, Is.False);
            Assert.That(disabledTargetReport.ToDisplayMessage(), Does.Contain("target_item_id").And.Contain("item registry"));

            var disabledCraft = Collection(Source(
                "items_configs",
                "GuildIdle - Items Configs",
                "items-disabled-craft.json",
                ItemsCraftReferenceDownload("resource_pine_wood", "recipe_old", "FALSE"),
                staleRuntime));
            var disabledCraftReport = ConfigCrossConfigValidator.Validate(disabledCraft);

            Assert.That(disabledCraftReport.Success, Is.True, disabledCraftReport.ToDisplayMessage());
        }

        [Test]
        public void Validate_ActivityCombatDetailsReportsMissingEnemyGroupId()
        {
            var collection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityCombatDownload("missing_enemy_group")),
                Source("enemies_configs", "GuildIdle - Enemies Configs", "enemies.json", EnemiesDownload(enemyLootId: string.Empty)));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("Activity Configs / CombatDetails row 2 column 'enemy_group_id' value 'missing_enemy_group'"));
            Assert.That(report.ToDisplayMessage(), Does.Contain("Enemies Configs / EnemyGroups.enemy_group_id"));
        }

        [Test]
        public void Validate_ActivityCombatDetailsSkipsWhenEnemiesRegistryMissing()
        {
            var collection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityCombatDownload("enemy_group_rats")));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(report.ToDisplayMessage(), Does.Contain("Warning: Cross-config validation skipped: Enemies Configs registry is not available yet."));
        }

        [Test]
        public void Validate_ActivityRequirementsUsesCaseInsensitiveCanonicalTypesAndRejectsUnknownTypes()
        {
            var validCollection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityRequirementsDownload("resource", "resource_pine_wood")),
                Source("items_configs", "GuildIdle - Items Configs", "items.json", EmptyDownload(), ItemsRuntimeJson()));

            var validReport = ConfigCrossConfigValidator.Validate(validCollection);

            Assert.That(validReport.Success, Is.True, validReport.ToDisplayMessage());

            var unknownCollection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityRequirementsDownload("UnknownRequirement", "resource_pine_wood")),
                Source("items_configs", "GuildIdle - Items Configs", "items.json", EmptyDownload(), ItemsRuntimeJson()));

            var unknownReport = ConfigCrossConfigValidator.Validate(unknownCollection);

            Assert.That(unknownReport.Success, Is.False);
            Assert.That(unknownReport.ToDisplayMessage(), Does.Contain("Unknown req_type 'UnknownRequirement'."));
        }

        [Test]
        public void Validate_ActivityRewardsRejectsUnknownTypes()
        {
            var collection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityRewardsDownload(
                    ("UnknownReward", "resource_pine_wood"))));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("Unknown reward_type 'UnknownReward'."));
        }

        [Test]
        public void Validate_LootDropTypesUsesCaseInsensitiveCanonicalTypesAndRejectsUnknownTypes()
        {
            var validCollection = Collection(
                Source("loot_configs", "GuildIdle - Loot Configs", "loot.json", LootEntriesDownload("resource", "resource_pine_wood")),
                Source("items_configs", "GuildIdle - Items Configs", "items.json", EmptyDownload(), ItemsRuntimeJson()));

            var validReport = ConfigCrossConfigValidator.Validate(validCollection);

            Assert.That(validReport.Success, Is.True, validReport.ToDisplayMessage());

            var unknownCollection = Collection(
                Source("loot_configs", "GuildIdle - Loot Configs", "loot.json", LootEntriesDownload("UnknownDrop", "resource_pine_wood")),
                Source("items_configs", "GuildIdle - Items Configs", "items.json", EmptyDownload(), ItemsRuntimeJson()));

            var unknownReport = ConfigCrossConfigValidator.Validate(unknownCollection);

            Assert.That(unknownReport.Success, Is.False);
            Assert.That(unknownReport.ToDisplayMessage(), Does.Contain("Unknown drop_type 'UnknownDrop'."));
        }

        [TestCase("GiveItem", TriggerTypeEnum.GiveItem)]
        [TestCase("unlocklocation", TriggerTypeEnum.UnlockLocation)]
        [TestCase("StartCombat", TriggerTypeEnum.StartCombat)]
        public void ActivityTypeParser_UsesCanonicalTriggerTypes(string value, TriggerTypeEnum expected)
        {
            Assert.That(ActivityTypeParser.TryParseTriggerType(value, out var actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCase("Resource", RequirementTypeEnum.Resource)]
        [TestCase("herolevel", RequirementTypeEnum.HeroLevel)]
        [TestCase("HeroClass", RequirementTypeEnum.HeroClass)]
        [TestCase("QuestCompleted", RequirementTypeEnum.QuestCompleted)]
        public void ActivityTypeParser_UsesCanonicalRequirementTypes(string value, RequirementTypeEnum expected)
        {
            Assert.That(ActivityTypeParser.TryParseRequirementType(value, out var actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCase("BuildingUnlock", RewardTypeEnum.UnlockBuilding)]
        [TestCase("unlockbuilding", RewardTypeEnum.UnlockBuilding)]
        [TestCase("Building", RewardTypeEnum.UnlockBuilding)]
        [TestCase("MapAccess", RewardTypeEnum.UnlockLocation)]
        [TestCase("unlocklocation", RewardTypeEnum.UnlockLocation)]
        [TestCase("Location", RewardTypeEnum.UnlockLocation)]
        public void ActivityTypeParser_UsesCanonicalRewardTypes(string value, RewardTypeEnum expected)
        {
            Assert.That(ActivityTypeParser.TryParseRewardType(value, out var actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void ActivityTypeParser_RejectsUnknownValuesForAllRuntimeTypeFamilies()
        {
            Assert.That(ActivityTypeParser.TryParseRequirementType("Unknown", out _), Is.False);
            Assert.That(ActivityTypeParser.TryParseRewardType("Unknown", out _), Is.False);
            Assert.That(ActivityTypeParser.TryParseDropType("Unknown", out _), Is.False);
            Assert.That(ActivityTypeParser.TryParseLootRollMode("Unknown", out _), Is.False);
        }

        [TestCase("0")]
        [TestCase("1")]
        [TestCase("-1")]
        [TestCase("+0")]
        public void ActivityTypeParser_RejectsNumericRepresentations(string value)
        {
            Assert.That(ActivityTypeParser.TryParseRequirementType(value, out _), Is.False);
            Assert.That(ActivityTypeParser.TryParseRewardType(value, out _), Is.False);
            Assert.That(ActivityTypeParser.TryParseDropType(value, out _), Is.False);
            Assert.That(ActivityTypeParser.TryParseLootRollMode(value, out _), Is.False);
        }

        [Test]
        public void ActivityTypeParser_RejectsCombinedRepresentations()
        {
            Assert.That(ActivityTypeParser.TryParseRequirementType("Resource, Item", out _), Is.False);
            Assert.That(ActivityTypeParser.TryParseRewardType("Hero, Equipment", out _), Is.False);
            Assert.That(ActivityTypeParser.TryParseDropType("Resource, Gold", out _), Is.False);
            Assert.That(ActivityTypeParser.TryParseLootRollMode("WeightedOne, WeightedMany", out _), Is.False);
        }

        [Test]
        public void Validate_LootRollModesUsesSharedParserAndRejectsUnknownTypes()
        {
            var validCollection = Collection(
                Source("loot_configs", "GuildIdle - Loot Configs", "loot.json", LootRollModesDownload("weightedmany", "GuaranteedAll")));

            var validReport = ConfigCrossConfigValidator.Validate(validCollection);

            Assert.That(validReport.Success, Is.True, validReport.ToDisplayMessage());

            var unknownCollection = Collection(
                Source("loot_configs", "GuildIdle - Loot Configs", "loot.json", LootRollModesDownload("UnknownMode", "WeightedOne")));

            var unknownReport = ConfigCrossConfigValidator.Validate(unknownCollection);

            Assert.That(unknownReport.Success, Is.False);
            Assert.That(unknownReport.ToDisplayMessage(), Does.Contain("Unknown roll_mode 'UnknownMode'."));
        }

        [Test]
        public void Validate_StorageItemStateNameIdUsesLocalisationRegistry()
        {
            var collection = Collection(
                Source("storage_configs", "GuildIdle - Storage Configs", "storage.json", StorageItemStatesDownload("storage_item_state_on_storage_name_id")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload("storage_item_state_on_storage_name_id")));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(report.Warnings, Is.Empty);
        }

        [Test]
        public void Validate_StorageItemStateNameIdSkipsWhenLocalisationMissing()
        {
            var collection = Collection(
                Source("storage_configs", "GuildIdle - Storage Configs", "storage.json", StorageItemStatesDownload("storage_item_state_on_storage_name_id")));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(report.ToDisplayMessage(), Does.Contain("Warning: Cross-config validation skipped: Localisation registry is not available yet."));
        }

        [Test]
        public void Validate_HeroUniqueSkillNameIdUsesLocalisationRegistry()
        {
            var collection = Collection(
                Source("heroes_configs", "GuildIdle - Heroes Configs", "heroes.json", HeroesDownload("missing_name_id", "hero_skill.gatherer.description")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload("hero_skill.gatherer.description")));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("Heroes Configs / HeroUniqueSkills row 2 column 'NameId' value 'missing_name_id'"));
            Assert.That(report.ToDisplayMessage(), Does.Contain("Localisation.id"));
        }

        [Test]
        public void Validate_ActivityEnemyAbilityAndStatusLocalisationReferences()
        {
            var collection = Collection(
                Source("activity_configs", "Activity Configs", "localised-activity.json", Download(
                    Sheet("Activities", Row("id", "name_id", "description_id"), Row("work_test", "activity.name", "activity.description")),
                    Sheet("Rarities", Row("id", "name_id", "description_id"), Row("Common", "rarity.name", "rarity.description")),
                    Sheet("Skills", Row("skill_id", "skill_name_id", "skill_description_id"), Row("skill_test", "skill.name", "skill.description")))),
                Source("enemies_configs", "Enemies Configs", "localised-enemies.json", Download(
                    Sheet("Enemies", Row("enemy_id", "name_id", "description_id"), Row("enemy_test", "enemy.name", "enemy.description")),
                    Sheet("EnemyAbilities", Row("ability_id", "name_id"), Row("bleeding_claws", "ability.bleeding_claws.name")),
                    Sheet("CombatStatuses", Row("status_id", "name_id"), Row("bleeding", "status.bleeding.name")))),
                Source("localisation", "Localisation", "localisation-refs.json", LocalisationDownload(
                    "activity.name", "activity.description", "rarity.name", "rarity.description", "skill.name", "skill.description",
                    "enemy.name", "enemy.description", "status.bleeding.name")));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("EnemyAbilities row 2 column 'name_id' value 'ability.bleeding_claws.name'"));
            Assert.That(report.ToDisplayMessage(), Does.Contain("Localisation.id"));
        }

        [Test]
        public void Validate_StorageBuildingsUsesBuildingsRegistry()
        {
            var collection = Collection(
                Source("storage_configs", "GuildIdle - Storage Configs", "storage.json", StorageBuildingsDownload("building_warehouse", "2")),
                Source("buildings_configs", "GuildIdle - Buildings Configs", "buildings.json", BuildingsIndexDownload("building_warehouse", "warehouse.name", "warehouse.description", "3")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload(
                    "warehouse.name", "warehouse.description",
                    "stage_arrival_name_id", "stage_arrival_description_id",
                    "stage_2_name_id", "stage_2_description_id")));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
        }

        [Test]
        public void Validate_EmptyRecipesSheetStillDeclaresRecipeItemKind()
        {
            var collection = Collection(
                Source("items_configs", "GuildIdle - Items Configs", "items-empty-recipes.json", Download(
                    Sheet("Рецепты", Row("id", "kind")))),
                Source("storage_configs", "GuildIdle - Storage Configs", "storage-recipe.json", Download(
                    Sheet("StorageRules",
                        Row("storage_rule_id", "item_kind", "mode"),
                        Row("storage_recipe", "recipe", "stack")))));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
        }

        [Test]
        public void Validate_PlainBuildingActivityRequirementUsesActivityRegistry()
        {
            var collection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity-requirement.json", Download(
                    Sheet("Activities", Row("id"), Row("combat_clear_hall_forest")))),
                Source("buildings_configs", "GuildIdle - Buildings Configs", "building-requirement.json", Download(
                    Sheet("Index",
                        Row("building_id", "levels", "start_level", "clickable_requirement"),
                        Row("building_underwood", "1", "1", "")),
                    Sheet("Underwood",
                        Row("building_id", "building_underwood"),
                        Row("level", "source_activity_id", "requirements_activities", "active_hero_limit"),
                        Row("1", "combat_clear_hall_forest", "combat_clear_hall_forest", "")),
                    Sheet("SettlementStages", Row("stage_id", "enabled"), Row("stage_2", "TRUE")))));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
        }

        [Test]
        public void Validate_BuildingActivitiesAcceptsUnifiedActionRegistryAndEmptyOptionalCompletionFields()
        {
            var collection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityIdsDownload("combat_clear_hall_forest")),
                Source("buildings_configs", "GuildIdle - Buildings Configs", "buildings.json", BuildingsActivitiesDownload(
                    startLevel: "0",
                    clickableRequirement: "",
                    buildingActivityLevel: "0",
                    buildingActivityId: "build_hall",
                    showIfCompleted: "",
                    hideIfCompleted: "")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload(
                    "building_hall_name_id", "building_hall_description_id",
                    "stage_arrival_name_id", "stage_arrival_description_id",
                    "stage_2_name_id", "stage_2_description_id")));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
        }

        [Test]
        public void Validate_BuildingActivitiesReportsMissingLevelAndActionReferences()
        {
            var collection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityIdsDownload("combat_clear_hall_forest")),
                Source("buildings_configs", "GuildIdle - Buildings Configs", "buildings.json", BuildingsActivitiesDownload(
                    startLevel: "2",
                    clickableRequirement: "missing_building:1",
                    buildingActivityLevel: "2",
                    buildingActivityId: "missing_action",
                    showIfCompleted: "build_hall",
                    hideIfCompleted: "missing_hide_action")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload(
                    "building_hall_name_id", "building_hall_description_id",
                    "stage_arrival_name_id", "stage_arrival_description_id",
                    "stage_2_name_id", "stage_2_description_id")));

            var report = ConfigCrossConfigValidator.Validate(collection);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("Index row 2 column 'start_level' value '2'"));
            Assert.That(message, Does.Contain("clickable_requirement references missing Buildings Configs building_id:level"));
            Assert.That(message, Does.Contain("BuildingActivities row 2 column 'building_level' value '2'"));
            Assert.That(message, Does.Contain("BuildingActivities row 2 column 'activity_id' value 'missing_action'"));
            Assert.That(message, Does.Contain("BuildingActivities row 2 column 'hide_if_activity_completed' value 'missing_hide_action'"));
            Assert.That(message, Does.Not.Contain("show_if_activity_completed"));
        }

        [Test]
        public void Validate_BuildingActivitiesReportsMissingRequiredFields()
        {
            var collection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityIdsDownload("combat_clear_hall_forest")),
                Source("buildings_configs", "GuildIdle - Buildings Configs", "buildings.json", BuildingsActivitiesDownload(
                    startLevel: "",
                    clickableRequirement: "",
                    buildingActivityLevel: "",
                    buildingActivityId: "",
                    showIfCompleted: "",
                    hideIfCompleted: "",
                    buildingActivityBuildingId: "")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload(
                    "building_hall_name_id", "building_hall_description_id",
                    "stage_arrival_name_id", "stage_arrival_description_id",
                    "stage_2_name_id", "stage_2_description_id")));

            var report = ConfigCrossConfigValidator.Validate(collection);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("Index row 2 column 'start_level'"));
            Assert.That(message, Does.Contain("BuildingActivities row 2 column 'building_id'"));
            Assert.That(message, Does.Contain("BuildingActivities row 2 column 'building_level'"));
            Assert.That(message, Does.Contain("BuildingActivities row 2 column 'activity_id'"));
        }


        [Test]
        public void Validate_EnemyLootGoldIdUsesCurrencyRegistry()
        {
            var collection = Collection(
                Source("enemies_configs", "GuildIdle - Enemies Configs", "enemies.json", EnemiesDownload("gold_id")),
                Source("items_configs", "GuildIdle - Items Configs", "items.json", ItemsCurrenciesDownload("gold_id", "gold.name", "gold.description")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload("gold.name", "gold.description")));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
        }

        [Test]
        public void Validate_ItemsRegistryUsesRuntimeArrays()
        {
            var collection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityRewardsDownload(
                    ("Resource", "resource_pine_wood"),
                    ("Equipment", "item_wooden_club"),
                    ("Equipment", "item_simple_shield"),
                    ("Recipe", "recipe_aska_bow"),
                    ("Consumable", "consumable_hunting_potion"),
                    ("Currency", "gold_id"))),
                Source(
                    "items_configs",
                    "GuildIdle - Items Configs",
                    "items.json",
                    EmptyDownload(),
                    ItemsRuntimeJson()));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
        }

        [Test]
        public void Validate_EnemyLootAllowsRuntimeCurrencyId()
        {
            var collection = Collection(
                Source("enemies_configs", "GuildIdle - Enemies Configs", "enemies.json", EnemiesDownload("premium_currency_id")),
                Source(
                    "items_configs",
                    "GuildIdle - Items Configs",
                    "items.json",
                    EmptyDownload(),
                    ItemsRuntimeJson(currencyId: "premium_currency_id")));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
        }

        [Test]
        public void Validate_EnemyLootReportsMissingItemReference()
        {
            var collection = Collection(
                Source("enemies_configs", "GuildIdle - Enemies Configs", "enemies.json", EnemiesDownload("resource_missing")),
                Source("items_configs", "GuildIdle - Items Configs", "items.json", ItemsResourcesDownload("resource_rat_tail", "resource.name", "resource.description")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload("resource.name", "resource.description")));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("EnemyLoot row 2 column 'loot_id' value 'resource_missing'"));
            Assert.That(report.ToDisplayMessage(), Does.Contain("item/resource/recipe/consumable registry or currency registry"));
        }

        [Test]
        public void Validate_GoldIdIsForbiddenAsStorageItem()
        {
            var collection = Collection(
                Source("storage_configs", "GuildIdle - Storage Configs", "storage.json", StorageRuleWithValue("gold_id")));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("gold_id is a currency_id and must not be used as a storage item."));
        }

        [Test]
        public void Validate_ItemGoldIsAlwaysError()
        {
            var collection = Collection(
                Source("storage_configs", "GuildIdle - Storage Configs", "storage.json", StorageRuleWithValue("item_gold")));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("item_gold is a forbidden legacy id."));
        }

        [Test]
        public void Validate_StageQuestReferencesSucceed()
        {
            var collection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityQuestsDownload(
                    questStepTargetId: "resource_pine_wood",
                    questRewardTargetId: string.Empty)),
                Source("buildings_configs", "GuildIdle - Buildings Configs", "buildings.json", BuildingsStagesDownload(
                    objectiveQuestId: "quest_build_hut",
                    firstWeight: "100",
                    includeStage2Slot: false)),
                Source("items_configs", "GuildIdle - Items Configs", "items.json", ItemsResourcesDownload("resource_pine_wood", "resource.name", "resource.description")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload(
                    "quest_build_hut_name_id",
                    "quest_build_hut_description_id",
                    "quest_build_hut_step_collect_wood_id",
                    "stage_arrival_name_id",
                    "stage_arrival_description_id",
                    "stage_2_name_id",
                    "stage_2_description_id",
                    "building_hall_name_id",
                    "building_hall_description_id",
                    "resource.name",
                    "resource.description")));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
        }

        [Test]
        public void Validate_StageQuestReferencesReportMissingQuestWeightAndStage2Content()
        {
            var collection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityQuestsDownload(
                    questStepTargetId: "resource_pine_wood",
                    questRewardTargetId: string.Empty)),
                Source("buildings_configs", "GuildIdle - Buildings Configs", "buildings.json", BuildingsStagesDownload(
                    objectiveQuestId: "quest_missing",
                    firstWeight: "50",
                    includeStage2Slot: true)),
                Source("items_configs", "GuildIdle - Items Configs", "items.json", ItemsResourcesDownload("resource_pine_wood", "resource.name", "resource.description")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload(
                    "quest_build_hut_name_id",
                    "quest_build_hut_description_id",
                    "quest_build_hut_step_collect_wood_id",
                    "stage_arrival_name_id",
                    "stage_arrival_description_id",
                    "stage_2_name_id",
                    "stage_2_description_id",
                    "building_hall_name_id",
                    "building_hall_description_id",
                    "resource.name",
                    "resource.description")));

            var report = ConfigCrossConfigValidator.Validate(collection);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("SettlementStageObjectives row 2 column 'quest_id' value 'quest_missing'"));
            Assert.That(message, Does.Contain("Required objective weight_percent total must be 100"));
            Assert.That(message, Does.Contain("stage_2 must not have slots."));
        }

        [Test]
        public void Validate_StageQuestReferencesRejectDisabledQuestAndStage()
        {
            var activityDownload = ActivityQuestsDownload(
                questStepTargetId: "resource_pine_wood",
                questRewardTargetId: string.Empty);
            FindSheet(activityDownload, "Quests").rows = Append(
                FindSheet(activityDownload, "Quests").rows,
                Row("quest_disabled", "quest_disabled_name_id", "quest_disabled_description_id", "StageObjective", "20", "FALSE", "FALSE"));

            var buildingsDownload = BuildingsStagesDownload(
                objectiveQuestId: "quest_disabled",
                firstWeight: "100",
                includeStage2Slot: false);
            FindSheet(buildingsDownload, "SettlementStages").rows = Append(
                FindSheet(buildingsDownload, "SettlementStages").rows,
                Row("stage_disabled", "stage_disabled_name_id", "stage_disabled_description_id", "stage_disabled_location", "0", "AllRequired", "", "30", "FALSE"));
            FindSheet(buildingsDownload, "SettlementStageSlots").rows = Append(
                FindSheet(buildingsDownload, "SettlementStageSlots").rows,
                Row("stage_disabled", "slot_disabled", "building_hall", "30", "TRUE"));
            FindSheet(buildingsDownload, "SettlementStageObjectives").rows = Append(
                FindSheet(buildingsDownload, "SettlementStageObjectives").rows,
                Row("stage_disabled", "quest_build_hut", "100", "TRUE", "30"));

            var collection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", activityDownload),
                Source("buildings_configs", "GuildIdle - Buildings Configs", "buildings.json", buildingsDownload),
                Source("items_configs", "GuildIdle - Items Configs", "items.json", ItemsResourcesDownload("resource_pine_wood", "resource.name", "resource.description")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload(
                    "quest_build_hut_name_id",
                    "quest_build_hut_description_id",
                    "quest_build_hut_step_collect_wood_id",
                    "quest_disabled_name_id",
                    "quest_disabled_description_id",
                    "stage_arrival_name_id",
                    "stage_arrival_description_id",
                    "stage_2_name_id",
                    "stage_2_description_id",
                    "stage_disabled_name_id",
                    "stage_disabled_description_id",
                    "building_hall_name_id",
                    "building_hall_description_id",
                    "resource.name",
                    "resource.description")));

            var report = ConfigCrossConfigValidator.Validate(collection);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("SettlementStageObjectives row 2 column 'quest_id' value 'quest_disabled'"));
            Assert.That(message, Does.Contain("SettlementStageSlots row 3 column 'stage_id' value 'stage_disabled'"));
            Assert.That(message, Does.Contain("SettlementStageObjectives row 3 column 'stage_id' value 'stage_disabled'"));
            Assert.That(message, Does.Contain("enabled"));
        }

        [Test]
        public void Validate_StageRowsRejectDisabledNextStage()
        {
            var buildingsDownload = BuildingsStagesDownload(
                objectiveQuestId: "quest_build_hut",
                firstWeight: "100",
                includeStage2Slot: false);
            FindSheet(buildingsDownload, "SettlementStages").rows[1].cells[6] = "stage_disabled";
            FindSheet(buildingsDownload, "SettlementStages").rows = Append(
                FindSheet(buildingsDownload, "SettlementStages").rows,
                Row("stage_disabled", "stage_disabled_name_id", "stage_disabled_description_id", "stage_disabled_location", "0", "AllRequired", "", "30", "FALSE"));

            var collection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityQuestsDownload(
                    questStepTargetId: "resource_pine_wood",
                    questRewardTargetId: string.Empty)),
                Source("buildings_configs", "GuildIdle - Buildings Configs", "buildings.json", buildingsDownload),
                Source("items_configs", "GuildIdle - Items Configs", "items.json", ItemsResourcesDownload("resource_pine_wood", "resource.name", "resource.description")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload(
                    "quest_build_hut_name_id",
                    "quest_build_hut_description_id",
                    "quest_build_hut_step_collect_wood_id",
                    "stage_arrival_name_id",
                    "stage_arrival_description_id",
                    "stage_2_name_id",
                    "stage_2_description_id",
                    "stage_disabled_name_id",
                    "stage_disabled_description_id",
                    "building_hall_name_id",
                    "building_hall_description_id",
                    "resource.name",
                    "resource.description")));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("SettlementStages row 2 column 'next_stage_id' value 'stage_disabled'"));
            Assert.That(report.ToDisplayMessage(), Does.Contain("enabled SettlementStages.stage_id"));
        }

        [TestCase("ActivityFailed")]
        [TestCase("ActivityCompleted")]
        [TestCase("StageEntered")]
        [TestCase("BuildingLevel")]
        public void Validate_QuestStartConditionsRequireTargetId(string conditionType)
        {
            var collection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityQuestsDownload(
                    questStepTargetId: "resource_pine_wood",
                    questRewardTargetId: string.Empty,
                    conditionType: conditionType,
                    conditionTargetId: string.Empty)),
                Source("items_configs", "GuildIdle - Items Configs", "items.json", ItemsResourcesDownload("resource_pine_wood", "resource.name", "resource.description")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload(
                    "quest_build_hut_name_id",
                    "quest_build_hut_description_id",
                    "quest_build_hut_step_collect_wood_id",
                    "resource.name",
                    "resource.description")));

            var report = ConfigCrossConfigValidator.Validate(collection);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain($"{conditionType} condition requires target_id."));
        }

        [Test]
        public void Validate_QuestStartConditionsRejectNewGameTargetUnknownTypeAndDisabledStageTarget()
        {
            var newGameCollection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityQuestsDownload(
                    questStepTargetId: "resource_pine_wood",
                    questRewardTargetId: string.Empty,
                    conditionType: "NewGame",
                    conditionTargetId: "unexpected_target")),
                Source("items_configs", "GuildIdle - Items Configs", "items.json", ItemsResourcesDownload("resource_pine_wood", "resource.name", "resource.description")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload(
                    "quest_build_hut_name_id",
                    "quest_build_hut_description_id",
                    "quest_build_hut_step_collect_wood_id",
                    "resource.name",
                    "resource.description")));

            var newGameReport = ConfigCrossConfigValidator.Validate(newGameCollection);
            Assert.That(newGameReport.Success, Is.False);
            Assert.That(newGameReport.ToDisplayMessage(), Does.Contain("NewGame condition requires empty target_id."));

            var unknownCollection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityQuestsDownload(
                    questStepTargetId: "resource_pine_wood",
                    questRewardTargetId: string.Empty,
                    conditionType: "BadCondition",
                    conditionTargetId: string.Empty)),
                Source("items_configs", "GuildIdle - Items Configs", "items.json", ItemsResourcesDownload("resource_pine_wood", "resource.name", "resource.description")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload(
                    "quest_build_hut_name_id",
                    "quest_build_hut_description_id",
                    "quest_build_hut_step_collect_wood_id",
                    "resource.name",
                    "resource.description")));

            var unknownReport = ConfigCrossConfigValidator.Validate(unknownCollection);
            Assert.That(unknownReport.Success, Is.False);
            Assert.That(unknownReport.ToDisplayMessage(), Does.Contain("Unknown QuestStartConditions.condition_type."));

            var buildingsDownload = BuildingsStagesDownload(
                objectiveQuestId: "quest_build_hut",
                firstWeight: "100",
                includeStage2Slot: false);
            FindSheet(buildingsDownload, "SettlementStages").rows = Append(
                FindSheet(buildingsDownload, "SettlementStages").rows,
                Row("stage_disabled", "stage_disabled_name_id", "stage_disabled_description_id", "stage_disabled_location", "0", "AllRequired", "", "30", "FALSE"));
            var disabledStageCollection = Collection(
                Source("activity_configs", "GuildIdle - Activity Configs", "activity.json", ActivityQuestsDownload(
                    questStepTargetId: "resource_pine_wood",
                    questRewardTargetId: string.Empty,
                    conditionType: "StageEntered",
                    conditionTargetId: "stage_disabled")),
                Source("buildings_configs", "GuildIdle - Buildings Configs", "buildings.json", buildingsDownload),
                Source("items_configs", "GuildIdle - Items Configs", "items.json", ItemsResourcesDownload("resource_pine_wood", "resource.name", "resource.description")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload(
                    "quest_build_hut_name_id",
                    "quest_build_hut_description_id",
                    "quest_build_hut_step_collect_wood_id",
                    "stage_arrival_name_id",
                    "stage_arrival_description_id",
                    "stage_2_name_id",
                    "stage_2_description_id",
                    "stage_disabled_name_id",
                    "stage_disabled_description_id",
                    "building_hall_name_id",
                    "building_hall_description_id",
                    "resource.name",
                    "resource.description")));

            var disabledStageReport = ConfigCrossConfigValidator.Validate(disabledStageCollection);
            Assert.That(disabledStageReport.Success, Is.False);
            Assert.That(disabledStageReport.ToDisplayMessage(), Does.Contain("QuestStartConditions row 2 column 'target_id' value 'stage_disabled'"));
            Assert.That(disabledStageReport.ToDisplayMessage(), Does.Contain("enabled SettlementStages.stage_id"));
        }

        [Test]
        public void RuntimeDtosDeserializeAndRepositoriesLookupQuestAndStageIds()
        {
            var activities = JsonUtility.FromJson<ActivitiesRuntimeConfigDto>(
                "{\"quests\":[{\"questId\":\"quest_runtime\",\"nameId\":\"quest.name\",\"descriptionId\":\"quest.desc\",\"category\":\"StageObjective\",\"sortOrder\":10,\"isTutorial\":true}]," +
                "\"questStartConditions\":[{\"questId\":\"quest_runtime\",\"conditionType\":\"NewGame\",\"targetId\":\"\",\"value\":1}]," +
                "\"questSteps\":[{\"questId\":\"quest_runtime\",\"stepId\":\"step_runtime\",\"stepOrder\":10,\"objectiveType\":\"ResourceCount\",\"targetId\":\"resource_pine_wood\",\"targetValue\":1,\"descriptionId\":\"step.desc\",\"required\":true}]," +
                "\"questRewards\":[{\"questId\":\"quest_runtime\",\"rewardType\":\"Resource\",\"targetId\":\"resource_pine_wood\",\"min\":1,\"max\":1,\"grantMoment\":\"OnComplete\"}]}");
            var activityRepository = new ActivitiesConfigRepository(activities);

            Assert.That(activityRepository.TryGetQuest("quest_runtime", out var quest), Is.True);
            Assert.That(quest.category, Is.EqualTo("StageObjective"));
            Assert.That(activityRepository.GetQuestStartConditions("quest_runtime"), Has.Length.EqualTo(1));
            Assert.That(activityRepository.GetQuestSteps("quest_runtime"), Has.Length.EqualTo(1));
            Assert.That(activityRepository.GetQuestRewards("quest_runtime"), Has.Length.EqualTo(1));

            var buildings = JsonUtility.FromJson<BuildingsRuntimeConfigDto>(
                "{\"buildingLevels\":[{\"buildingId\":\"building_hall\",\"level\":0,\"activeHeroLimit\":1}]," +
                "\"settlementStages\":[{\"stageId\":\"stage_runtime\",\"nameId\":\"stage.name\",\"descriptionId\":\"stage.desc\",\"stagePrefabId\":\"stage_prefab\",\"targetDurationSec\":0,\"completionRule\":\"AllRequired\",\"nextStageId\":\"\",\"sortOrder\":10,\"enabled\":true}]," +
                "\"settlementStageStarterHeroes\":[{\"stageId\":\"stage_runtime\",\"heroId\":\"ren\",\"sortOrder\":10}]," +
                "\"settlementStageStarterEquipment\":[{\"stageId\":\"stage_runtime\",\"heroId\":\"ren\",\"itemId\":\"item_wooden_club\",\"equipmentSlot\":\"weapon\",\"sortOrder\":10}]}");
            var buildingsRepository = new BuildingsConfigRepository(buildings);

            Assert.That(buildingsRepository.TryGetSettlementStage("stage_runtime", out var stage), Is.True);
            Assert.That(stage.completionRule, Is.EqualTo("AllRequired"));
            Assert.That(buildingsRepository.TryGetBuildingLevel("building_hall", 0, out var hallLevel), Is.True);
            Assert.That(hallLevel.activeHeroLimit, Is.EqualTo(1));
            Assert.That(buildingsRepository.GetSettlementStageStarterHeroes("stage_runtime")[0].heroId, Is.EqualTo("ren"));
            Assert.That(buildingsRepository.GetSettlementStageStarterEquipment("stage_runtime")[0].itemId, Is.EqualTo("item_wooden_club"));
        }

        [Test]
        public void Validate_StageBootstrapRequiresEnabledHeroEquipmentAndMatchingSlot()
        {
            var buildings = Download(
                Sheet("SettlementStages",
                    Row("stage_id", "enabled"),
                    Row("stage_arrival", "TRUE"),
                    Row("stage_2", "TRUE")),
                Sheet("SettlementStageStarterHeroes",
                    Row("stage_id", "hero_id", "sort_order", "enabled"),
                    Row("stage_arrival", "ren", "10", "TRUE")),
                Sheet("SettlementStageStarterEquipment",
                    Row("stage_id", "hero_id", "item_id", "equipment_slot", "sort_order", "enabled"),
                    Row("stage_arrival", "ren", "item_wooden_club", "weapon", "10", "TRUE")));
            var heroes = Download(Sheet("Heroes", Row("HeroId", "Enabled"), Row("ren", "TRUE"), Row("disabled_hero", "FALSE")));
            var collection = Collection(
                Source("buildings_configs", "Buildings Configs", "stage-bootstrap.json", buildings),
                Source("heroes_configs", "Heroes Configs", "heroes.json", heroes),
                Source("items_configs", "Items Configs", "items.json", EmptyDownload(), ItemsRuntimeJson()));

            var validReport = ConfigCrossConfigValidator.Validate(collection);
            Assert.That(validReport.Success, Is.True, validReport.ToDisplayMessage());

            FindSheet(buildings, "SettlementStageStarterHeroes").rows[1].cells[1] = "disabled_hero";
            FindSheet(buildings, "SettlementStageStarterEquipment").rows[1].cells[3] = "armor";
            var invalidCollection = Collection(
                Source("buildings_configs", "Buildings Configs", "stage-bootstrap-invalid.json", buildings),
                Source("heroes_configs", "Heroes Configs", "heroes-invalid.json", heroes),
                Source("items_configs", "Items Configs", "items-invalid.json", EmptyDownload(), ItemsRuntimeJson()));
            var invalidReport = ConfigCrossConfigValidator.Validate(invalidCollection);
            var message = invalidReport.ToDisplayMessage();

            Assert.That(invalidReport.Success, Is.False);
            Assert.That(message, Does.Contain("disabled_hero").And.Contain("enabled Heroes.HeroId"));
            Assert.That(message, Does.Contain("equipment_slot").And.Contain("slot 'weapon'"));
        }

        [Test]
        public void RuntimeRegressionChecksExecutableDtosAfterDeserialization()
        {
            var activitiesDto = JsonUtility.FromJson<ActivitiesRuntimeConfigDto>(
                "{\"activities\":[{\"id\":\"work_safe\",\"notes\":\"starter_hero_available\"}]," +
                "\"rewards\":[{\"activityId\":\"work_safe\",\"targetId\":\"resource_wood\"}]," +
                "\"dangerEncounters\":[{\"dangerEncounterId\":\"danger_safe\",\"activityId\":\"work_safe\",\"enemyGroupId\":\"group_safe\",\"riskFormulaId\":\"formula_risk\"}]," +
                "\"notes\":\"enemy_ability_quick_jump\"}");
            var activities = new ActivitiesConfigRepository(activitiesDto);

            var enemiesDto = JsonUtility.FromJson<EnemiesRuntimeConfigDto>(
                "{\"enemies\":[{\"enemyId\":\"enemy_safe\",\"combatAbilityIds\":[\"enemy_ability_bite\"],\"notes\":\"enemy_ability_quick_jump\"}]," +
                "\"enemyAbilities\":[{\"abilityId\":\"enemy_ability_bite\",\"notes\":\"enemy_ability_quick_jump\"}]}");

            Assert.That(Array.Exists(activities.Activities, row => row.id == "starter_hero_available"), Is.False);
            Assert.That(Array.Exists(activities.Rewards, row => row.activityId == "starter_hero_available"), Is.False);
            Assert.That(activities.TryGetDangerEncounter("danger_safe", out var danger), Is.True);
            Assert.That(danger.riskFormulaId, Is.EqualTo("formula_risk"));
            Assert.That(Array.Exists(enemiesDto.enemies[0].combatAbilityIds, id => id == "enemy_ability_quick_jump"), Is.False);
            Assert.That(Array.Exists(enemiesDto.enemyAbilities, row => row.abilityId == "enemy_ability_quick_jump"), Is.False);
            Assert.That(typeof(MapRuntimeConfigDto).GetField("dangerEncounters"), Is.Null);
            Assert.That(typeof(CombatDetailConfigDto).GetField("intendedFirstResult"), Is.Null);
            Assert.That(typeof(RarityConfigDto).GetField("colorHex"), Is.Null);
        }

        private static ConfigSourceSettingsCollection Collection(params ConfigSourceSettings[] sources)
        {
            return new ConfigSourceSettingsCollection { sources = sources };
        }

        private static ConfigSourceSettings Source(string configId, string displayName, string fileName, ConfigSheetDownload download, string runtimeJson = null)
        {
            var outputPath = $"{TestRoot}/{fileName}";
            var runtimePath = $"{RuntimeTestRoot}/{fileName}.runtime.json";
            download.config_id = configId;
            download.display_name = displayName;
            WriteProjectFile(outputPath, JsonUtility.ToJson(download, true));
            if (!string.IsNullOrWhiteSpace(runtimeJson))
                WriteProjectFile(runtimePath, runtimeJson);

            return new ConfigSourceSettings
            {
                config_id = configId,
                display_name = displayName,
                output_json_path = outputPath,
                runtime_json_path = runtimePath
            };
        }

        private static ConfigSheetDownload ActivityCombatDownload(string enemyGroupId)
        {
            return Download(
                Sheet("CombatDetails",
                    Row("activity_id", "enemy_group_id", "combat_mode", "balance_intent", "completion_reward_rule"),
                    Row("combat_test", enemyGroupId, "Queue_1v1", "VictoryExpected", "ActivityRewards")));
        }

        private static ConfigSheetDownload ActivityRewardsDownload(params (string RewardType, string TargetId)[] rewards)
        {
            var rows = new List<ConfigSheetRow>
            {
                Row("reward_id", "activity_id", "reward_type", "target_id")
            };

            for (var index = 0; index < rewards.Length; index++)
                rows.Add(Row($"reward_{index}", "activity_test", rewards[index].RewardType, rewards[index].TargetId));

            return Download(Sheet("ActivityRewards", rows.ToArray()));
        }

        private static ConfigSheetDownload ActivityRequirementsDownload(string requirementType, string targetId)
        {
            return Download(
                Sheet("ActivityRequirements",
                    Row("activity_id", "req_type", "target_id", "value", "consume"),
                    Row("activity_test", requirementType, targetId, "1", "FALSE")));
        }

        private static ConfigSheetDownload LootEntriesDownload(string dropType, string targetId)
        {
            return Download(
                Sheet("LootTableEntries",
                    Row("loot_table_id", "entry_id", "drop_type", "target_id"),
                    Row("loot_test", "entry_test", dropType, targetId)));
        }

        private static ConfigSheetDownload LootRollModesDownload(string tableRollMode, string groupRollMode)
        {
            return Download(
                Sheet("LootTables",
                    Row("loot_table_id", "roll_mode"),
                    Row("loot_test", tableRollMode)),
                Sheet("LootGroups",
                    Row("loot_table_id", "roll_group", "roll_mode"),
                    Row("loot_test", "default", groupRollMode)));
        }

        private static ConfigSheetDownload ActivityIdsDownload(params string[] activityIds)
        {
            var rows = new List<ConfigSheetRow> { Row("id") };
            foreach (var activityId in activityIds)
                rows.Add(Row(activityId));

            return Download(Sheet("Activities", rows.ToArray()));
        }

        private static ConfigSheetDownload ActivityQuestsDownload(
            string questStepTargetId,
            string questRewardTargetId,
            string conditionType = "NewGame",
            string conditionTargetId = "",
            string questId = "quest_build_hut",
            string questEnabled = "TRUE")
        {
            var sheets = new List<ConfigDownloadedSheet>
            {
                Sheet("Activities",
                    Row("id", "location_id", "stat_profile_id"),
                    Row("work_pine_wood", "", "")),
                Sheet("Quests",
                    Row("quest_id", "name_id", "description_id", "category", "sort_order", "is_tutorial", "enabled"),
                    Row(questId, "quest_build_hut_name_id", "quest_build_hut_description_id", "StageObjective", "10", "TRUE", questEnabled)),
                Sheet("QuestStartConditions",
                    Row("quest_id", "condition_type", "target_id", "value"),
                    Row(questId, conditionType, conditionTargetId, "1")),
                Sheet("QuestSteps",
                    Row("quest_id", "step_id", "step_order", "objective_type", "target_id", "target_value", "description_id", "required"),
                    Row(questId, "step_collect_wood", "10", "ResourceCount", questStepTargetId, "8", "quest_build_hut_step_collect_wood_id", "TRUE"))
            };

            if (!string.IsNullOrWhiteSpace(questRewardTargetId))
            {
                sheets.Add(Sheet("QuestRewards",
                    Row("quest_id", "reward_type", "target_id", "min", "max", "grant_moment"),
                    Row(questId, "Resource", questRewardTargetId, "1", "1", "OnComplete")));
            }
            else
            {
                sheets.Add(Sheet("QuestRewards",
                    Row("quest_id", "reward_type", "target_id", "min", "max", "grant_moment")));
            }

            return Download(sheets.ToArray());
        }

        private static ConfigSheetDownload EnemiesDownload(string enemyLootId)
        {
            var sheets = new List<ConfigDownloadedSheet>
            {
                Sheet("Enemies",
                    Row("enemy_id"),
                    Row("enemy_rat")),
                Sheet("EnemyLevels",
                    Row("level"),
                    Row("1")),
                Sheet("EnemyGroups",
                    Row("enemy_group_id", "enemy_ref", "weight", "min_count", "max_count"),
                    Row("enemy_group_rats", "enemy_rat:1", "100", "1", "1"))
            };

            if (!string.IsNullOrWhiteSpace(enemyLootId))
            {
                sheets.Add(Sheet("EnemyLoot",
                    Row("loot_group_id", "enemy_id", "loot_id", "min_count", "max_count", "chance_percent", "quality_min", "quality_max"),
                    Row("loot_enemy_rat", "enemy_rat", enemyLootId, "1", "1", "100", "0", "0")));
            }

            return Download(sheets.ToArray());
        }

        private static ConfigSheetDownload StorageItemStatesDownload(string nameId)
        {
            return Download(
                Sheet("ItemStates",
                    Row("state_id", "storage_item_state_name_id"),
                    Row("on_storage", nameId)));
        }

        private static ConfigSheetDownload StorageBuildingsDownload(string buildingId, string level)
        {
            return Download(
                Sheet("StorageBuildings",
                    Row("building_id", "level"),
                    Row(buildingId, level)));
        }

        private static ConfigSheetDownload HeroesDownload(string nameId, string descriptionId)
        {
            return Download(
                Sheet("Heroes",
                    Row("HeroId"),
                    Row("aska")),
                Sheet("HeroUniqueSkills",
                    Row("HeroId", "SkillId", "NameId", "DescriptionId"),
                    Row("aska", "gatherer", nameId, descriptionId)));
        }

        private static ConfigSheetDownload StorageRuleWithValue(string value)
        {
            return Download(
                Sheet("StorageRules",
                    Row("storage_rule_id", "item_kind", "mode"),
                    Row(value, "resource", "stack")));
        }

        private static ConfigSheetDownload BuildingsIndexDownload(string buildingId, string nameId, string descriptionId, string levels)
        {
            return Download(
                Sheet("Index",
                    Row("building_id", "name_id", "description_id", "levels", "start_level", "visible_at_start", "clickable_requirement"),
                    Row(buildingId, nameId, descriptionId, levels, "0", "TRUE", "")),
                Sheet("SettlementStages",
                    Row("stage_id", "name_id", "description_id", "stage_prefab_id", "target_duration_sec", "completion_rule", "next_stage_id", "sort_order", "enabled"),
                    Row("stage_arrival", "stage_arrival_name_id", "stage_arrival_description_id", "stage_arrival_location", "1800", "AllRequired", "stage_2", "10", "TRUE"),
                    Row("stage_2", "stage_2_name_id", "stage_2_description_id", "stage_2_location", "0", "AllRequired", "", "20", "TRUE")));
        }

        private static ConfigSheetDownload BuildingsStagesDownload(string objectiveQuestId, string firstWeight, bool includeStage2Slot)
        {
            var slots = new List<ConfigSheetRow>
            {
                Row("stage_id", "slot_id", "building_id", "sort_order", "enabled"),
                Row("stage_arrival", "slot_hut", "building_hall", "10", "TRUE")
            };

            if (includeStage2Slot)
                slots.Add(Row("stage_2", "slot_stage_2", "building_hall", "10", "TRUE"));

            return Download(
                Sheet("Index",
                    Row("building_id", "name_id", "description_id", "levels", "start_level", "visible_at_start", "clickable_requirement"),
                    Row("building_hall", "building_hall_name_id", "building_hall_description_id", "1", "0", "TRUE", "")),
                Sheet("SettlementStages",
                    Row("stage_id", "name_id", "description_id", "stage_prefab_id", "target_duration_sec", "completion_rule", "next_stage_id", "sort_order", "enabled"),
                    Row("stage_arrival", "stage_arrival_name_id", "stage_arrival_description_id", "stage_arrival_location", "1800", "AllRequired", "stage_2", "10", "TRUE"),
                    Row("stage_2", "stage_2_name_id", "stage_2_description_id", "stage_2_location", "0", "AllRequired", "", "20", "TRUE")),
                Sheet("SettlementStageSlots", slots.ToArray()),
                Sheet("SettlementStageObjectives",
                    Row("stage_id", "quest_id", "weight_percent", "required", "sort_order"),
                    Row("stage_arrival", objectiveQuestId, firstWeight, "TRUE", "10")));
        }

        private static ConfigSheetDownload BuildingsActivitiesDownload(string startLevel, string clickableRequirement, string buildingActivityLevel, string buildingActivityId, string showIfCompleted, string hideIfCompleted, string buildingActivityBuildingId = "building_hall")
        {
            return Download(
                Sheet("Index",
                    Row("building_id", "name_id", "description_id", "levels", "start_level", "visible_at_start", "clickable_requirement"),
                    Row("building_hall", "building_hall_name_id", "building_hall_description_id", "1", startLevel, "TRUE", clickableRequirement)),
                Sheet("Hall",
                    Row("field", "value"),
                    Row("building_id", "building_hall"),
                    Row("level", "level_prefab_id", "source_activity_id", "active_hero_limit"),
                    Row("0", "building_hall_level_0", "", "1"),
                    Row("1", "building_hall_level_1", "build_hall", "1")),
                Sheet("BuildingActivities",
                    Row("building_id", "building_level", "activity_id", "sort_order", "show_if_activity_completed", "hide_if_activity_completed", "clickable_requirement", "enabled"),
                    Row(buildingActivityBuildingId, buildingActivityLevel, buildingActivityId, "10", showIfCompleted, hideIfCompleted, "", "TRUE")),
                Sheet("SettlementStages",
                    Row("stage_id", "name_id", "description_id", "stage_prefab_id", "target_duration_sec", "completion_rule", "next_stage_id", "sort_order", "enabled"),
                    Row("stage_arrival", "stage_arrival_name_id", "stage_arrival_description_id", "stage_arrival_location", "1800", "AllRequired", "stage_2", "10", "TRUE"),
                    Row("stage_2", "stage_2_name_id", "stage_2_description_id", "stage_2_location", "0", "AllRequired", "", "20", "TRUE")));
        }

        private static ConfigSheetDownload ItemsCurrenciesDownload(string currencyId, string nameId, string descriptionId)
        {
            return Download(
                Sheet("Валюты",
                    Row("currency_id", "icon_id", "name_id", "description_id"),
                    Row(currencyId, "icon_gold", nameId, descriptionId)));
        }

        private static ConfigSheetDownload ItemsResourcesDownload(string resourceId, string nameId, string descriptionId)
        {
            return Download(
                Sheet("Ресурсы",
                    Row("id", "name_id", "description_id", "icon_id", "kind", "rarity_id", "materials"),
                    Row(resourceId, nameId, descriptionId, "icon_resource", "resource", "", "")));
        }

        private static ConfigSheetDownload ItemsCraftReferenceDownload(string targetItemId, string requiredRecipeItemId, string craftEnabled)
        {
            return Download(
                Sheet("Ресурсы",
                    Row("id", "kind"),
                    Row("resource_pine_wood", "resource")),
                Sheet("Рецепты",
                    Row("id", "kind", "enabled"),
                    Row("recipe_old", "recipe", "FALSE")),
                Sheet("CraftDefinitions",
                    Row("craft_id", "target_item_id", "required_recipe_item_id", "enabled"),
                    Row("craft_test", targetItemId, requiredRecipeItemId, craftEnabled)));
        }

        private static ConfigSheetDownload EmptyDownload()
        {
            return Download(Sheet("README", Row("id")));
        }

        private static string ItemsRuntimeJson(string currencyId = "gold_id")
        {
            return "{\n" +
                   "  \"resources\": [{ \"id\": \"resource_pine_wood\", \"kind\": \"resource\" }],\n" +
                   "  \"equipmentWeapons\": [{ \"id\": \"item_wooden_club\", \"kind\": \"equipment\", \"equipmentSlot\": \"weapon\" }],\n" +
                   "  \"equipmentArmor\": [{ \"id\": \"item_simple_shield\", \"kind\": \"equipment\", \"equipmentSlot\": \"armor\" }],\n" +
                   "  \"recipes\": [{ \"id\": \"recipe_aska_bow\", \"kind\": \"recipe\" }],\n" +
                   "  \"consumables\": [{ \"id\": \"consumable_hunting_potion\", \"kind\": \"consumable\" }],\n" +
                   $"  \"currencies\": [{{ \"currencyId\": \"{currencyId}\" }}]\n" +
                   "}";
        }

        private static ConfigSheetDownload LocalisationDownload(params string[] ids)
        {
            var rows = new List<ConfigSheetRow> { Row("id", "Ru", "En", "Tr") };
            foreach (var id in ids)
                rows.Add(Row(id, id, id, id));

            return Download(Sheet("Localisation", rows.ToArray()));
        }

        private static ConfigSheetDownload Download(params ConfigDownloadedSheet[] sheets)
        {
            return new ConfigSheetDownload
            {
                source_type = "GoogleSheet",
                sheet_url = "https://docs.google.com/spreadsheets/d/test",
                downloaded_at_utc = "2026-07-05T00:00:00Z",
                sheets = sheets
            };
        }

        private static ConfigDownloadedSheet Sheet(string name, params ConfigSheetRow[] rows)
        {
            return new ConfigDownloadedSheet
            {
                sheet_name = name,
                rows = rows
            };
        }

        private static ConfigSheetRow Row(params string[] cells)
        {
            return new ConfigSheetRow { cells = cells };
        }

        private static ConfigSheetRow[] Append(ConfigSheetRow[] rows, params ConfigSheetRow[] appendedRows)
        {
            var list = new List<ConfigSheetRow>(rows);
            list.AddRange(appendedRows);
            return list.ToArray();
        }

        private static ConfigDownloadedSheet FindSheet(ConfigSheetDownload download, string sheetName)
        {
            foreach (var sheet in download.sheets)
            {
                if (string.Equals(sheet.sheet_name, sheetName, StringComparison.OrdinalIgnoreCase))
                    return sheet;
            }

            throw new InvalidOperationException($"Missing test sheet {sheetName}.");
        }

        private static void WriteProjectFile(string projectPath, string text)
        {
            var fullPath = FullProjectPath(projectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, text, ConfigPipelineUtilities.Utf8NoBom);
        }

        private static string FullProjectPath(string projectPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, projectPath.Replace('\\', '/')));
        }
    }
}
