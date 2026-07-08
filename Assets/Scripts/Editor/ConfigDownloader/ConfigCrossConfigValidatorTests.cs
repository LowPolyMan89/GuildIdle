using System;
using System.Collections.Generic;
using System.IO;
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
        public void Validate_StorageBuildingsUsesBuildingsRegistry()
        {
            var collection = Collection(
                Source("storage_configs", "GuildIdle - Storage Configs", "storage.json", StorageBuildingsDownload("building_warehouse", "2")),
                Source("buildings_configs", "GuildIdle - Buildings Configs", "buildings.json", BuildingsIndexDownload("building_warehouse", "warehouse.name", "warehouse.description", "3")),
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload("warehouse.name", "warehouse.description")));

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
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload("building_hall_name_id", "building_hall_description_id")));

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
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload("building_hall_name_id", "building_hall_description_id")));

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
                Source("localisation", "GuildIdle - Localisation", "localisation.json", LocalisationDownload("building_hall_name_id", "building_hall_description_id")));

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
                    Row("activity_id", "enemy_group_id", "combat_mode", "intended_first_result", "completion_reward_rule"),
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

        private static ConfigSheetDownload ActivityIdsDownload(params string[] activityIds)
        {
            var rows = new List<ConfigSheetRow> { Row("id") };
            foreach (var activityId in activityIds)
                rows.Add(Row(activityId));

            return Download(Sheet("Activities", rows.ToArray()));
        }

        private static ConfigSheetDownload EnemiesDownload(string enemyLootId)
        {
            var sheets = new List<ConfigDownloadedSheet>
            {
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
                    Row(buildingId, nameId, descriptionId, levels, "0", "TRUE", "")));
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
                    Row("level", "level_prefab_id", "source_activity_id"),
                    Row("0", "building_hall_level_0", ""),
                    Row("1", "building_hall_level_1", "build_hall")),
                Sheet("BuildingActivities",
                    Row("building_id", "building_level", "activity_id", "sort_order", "show_if_activity_completed", "hide_if_activity_completed", "clickable_requirement", "enabled"),
                    Row(buildingActivityBuildingId, buildingActivityLevel, buildingActivityId, "10", showIfCompleted, hideIfCompleted, "", "TRUE")));
        }

        private static ConfigSheetDownload ItemsCurrenciesDownload(string currencyId, string nameId, string descriptionId)
        {
            return Download(
                Sheet("Р’Р°Р»СЋС‚С‹",
                    Row("currency_id", "icon_id", "name_id", "description_id"),
                    Row(currencyId, "icon_gold", nameId, descriptionId)));
        }

        private static ConfigSheetDownload ItemsResourcesDownload(string resourceId, string nameId, string descriptionId)
        {
            return Download(
                Sheet("Р РµСЃСѓСЂСЃС‹",
                    Row("id", "name_id", "description_id", "icon_id", "kind", "rarity_id", "materials"),
                    Row(resourceId, nameId, descriptionId, "icon_resource", "resource", "", "")));
        }

        private static ConfigSheetDownload EmptyDownload()
        {
            return Download(Sheet("README", Row("id")));
        }

        private static string ItemsRuntimeJson(string currencyId = "gold_id")
        {
            return "{\n" +
                   "  \"resources\": [{ \"id\": \"resource_pine_wood\", \"kind\": \"resource\" }],\n" +
                   "  \"equipmentWeapons\": [{ \"id\": \"item_wooden_club\", \"kind\": \"equipment\" }],\n" +
                   "  \"equipmentArmor\": [{ \"id\": \"item_simple_shield\", \"kind\": \"equipment\" }],\n" +
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
