using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class BuildingsConfigsParserTests
    {
        private const string TestRawPath = "Temp/ConfigParserTests/buildings_configs.raw.json";
        private const string TestRuntimePath = "Assets/StreamingAssets/Configs/buildings_configs.test.runtime.json";

        [TearDown]
        public void TearDown()
        {
            DeleteProjectFile(TestRawPath);
            DeleteProjectFile(TestRuntimePath);
            DeleteProjectFile(TestRuntimePath + ".tmp");
            DeleteProjectFile(TestRuntimePath + ".meta");
        }

        [Test]
        public void BuildRuntimeJson_UsesStableShapeTypesAndExcludesDesignerReadmeAndRawCells()
        {
            WriteRaw(CreateValidDownload());

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"buildings\""));
            Assert.That(runtimeJson, Does.Contain("\"buildingLevels\""));
            Assert.That(runtimeJson, Does.Contain("\"buildActions\""));
            Assert.That(runtimeJson, Does.Contain("\"buildingActivities\""));
            Assert.That(runtimeJson, Does.Contain("\"buildingCraftables\""));
            Assert.That(runtimeJson, Does.Contain("\"settlementStages\""));
            Assert.That(runtimeJson, Does.Contain("\"settlementStageSlots\""));
            Assert.That(runtimeJson, Does.Contain("\"settlementStageObjectives\""));
            Assert.That(runtimeJson, Does.Contain("\"buildingId\": \"building_hall\""));
            Assert.That(runtimeJson, Does.Contain("\"stageId\": \"stage_arrival\""));
            Assert.That(runtimeJson, Does.Contain("\"levels\": 1"));
            Assert.That(runtimeJson, Does.Contain("\"startLevel\": 0"));
            Assert.That(runtimeJson, Does.Contain("\"visibleAtStart\": true"));
            Assert.That(runtimeJson, Does.Contain("\"clickableRequirement\": \"building_hall:1\""));
            Assert.That(runtimeJson, Does.Contain("\"mvpRequired\": true"));
            Assert.That(runtimeJson, Does.Contain("\"materials\": ["));
            Assert.That(runtimeJson, Does.Contain("\"id\": \"resource_pine_wood\""));
            Assert.That(runtimeJson, Does.Contain("\"count\": 5"));
            Assert.That(runtimeJson, Does.Contain("\"requirementsActivities\": ["));
            Assert.That(runtimeJson, Does.Contain("\"activityId\": \"combat_clear_hall_forest\""));
            Assert.That(runtimeJson, Does.Contain("\"requirementsBuildings\": []"));
            Assert.That(runtimeJson, Does.Contain("\"level\": 0"));
            Assert.That(runtimeJson, Does.Contain("\"levelPrefabId\": \"building_underwood_level_0\""));
            Assert.That(runtimeJson, Does.Contain("\"activityId\": \"build_warehouse\""));
            Assert.That(runtimeJson, Does.Not.Contain("missing_disabled_activity"));
            Assert.That(runtimeJson, Does.Not.Contain("levelImageId"));
            Assert.That(runtimeJson, Does.Not.Contain("README"));
            Assert.That(runtimeJson, Does.Not.Contain("Title"));
            Assert.That(runtimeJson, Does.Not.Contain("notes"));
            Assert.That(runtimeJson, Does.Not.Contain("cells"));
        }

        [Test]
        public void BuildRuntimeJson_GeneratesBuildActionsOnlyForBuildSources()
        {
            WriteRaw(CreateValidDownload());

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"id\": \"build_hall\""));
            Assert.That(runtimeJson, Does.Contain("\"type\": \"Build\""));
            Assert.That(runtimeJson, Does.Contain("\"targetBuildingId\": \"building_hall\""));
            Assert.That(runtimeJson, Does.Contain("\"targetLevel\": 1"));
            Assert.That(runtimeJson, Does.Not.Contain("\"id\": \"combat_clear_hall_forest\""));
            Assert.That(runtimeJson, Does.Not.Contain("\"id\": \"starter_hero_available\""));
        }

        [Test]
        public void BuildRuntimeJson_CraftablesDoNotCreateCraftActions()
        {
            WriteRaw(CreateValidDownload());

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"itemId\": \"resource_pine_plank\""));
            Assert.That(runtimeJson, Does.Not.Contain("\"type\": \"Craft\""));
            Assert.That(runtimeJson, Does.Not.Contain("\"type\": \"Process\""));
        }

        [Test]
        public void BuildRuntimeJson_SupportsDecimalComma()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Hall").rows[10].cells[4] = "30,5";
            WriteRaw(download);

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"durationSec\": 30.5"));
        }

        [Test]
        public void BuildRuntimeJson_RejectsGoldIdAndItemGoldInCraftables()
        {
            var goldDownload = CreateValidDownload();
            FindSheet(goldDownload, "Craftables - Carpentry").rows[1].cells[2] = "gold_id";
            WriteRaw(goldDownload);

            var goldReport = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            Assert.That(goldReport.Success, Is.False);
            Assert.That(goldReport.ToDisplayMessage(), Does.Contain("gold_id is a currency_id and is forbidden in BuildingCraftables."));

            var itemGoldDownload = CreateValidDownload();
            FindSheet(itemGoldDownload, "Craftables - Carpentry").rows[1].cells[2] = "item_gold";
            WriteRaw(itemGoldDownload);

            var itemGoldReport = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            Assert.That(itemGoldReport.Success, Is.False);
            Assert.That(itemGoldReport.ToDisplayMessage(), Does.Contain("item_gold is a forbidden legacy id."));
        }

        [Test]
        public void BuildRuntimeJson_SupportsCompressedBuildingSheetExport()
        {
            var download = CreateValidDownload();
            ReplaceSheet(download, CreateCompressedHallSheet());
            WriteRaw(download);

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"id\": \"build_hall\""));
            Assert.That(runtimeJson, Does.Contain("\"levelPrefabId\": \"building_hall_level_1\""));
            Assert.That(runtimeJson, Does.Not.Contain("levelImageId"));
        }

        [Test]
        public void BuildRuntimeJson_RejectsLegacyLevelImageIdColumn()
        {
            var download = CreateValidDownload();
            var hall = FindSheet(download, "Hall");
            hall.rows[8].cells[1] = "level_image_id";
            hall.rows[9].cells[1] = "building_hall_level_0_image_id";
            WriteRaw(download);

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("level_image_id is deprecated; use level_prefab_id."));
            Assert.That(message, Does.Contain("level_prefab_id"));
        }

        [Test]
        public void BuildRuntimeJson_RejectsLevelPrefabIdValueWithPrefabIdSuffix()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Hall").rows[9].cells[1] = "building_hall_level_0_prefab_id";
            WriteRaw(download);

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("level_prefab_id must reference a prefab asset id and must not end with _prefab_id."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsLocalValidationErrors()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Index").rows[1].cells[5] = "-1";
            FindSheet(download, "Hall").rows[10].cells[7] = "BadStat";
            FindSheet(download, "Hall").rows[10].cells[8] = "badpacked";
            FindSheet(download, "Craftables - Carpentry").rows = Append(
                FindSheet(download, "Craftables - Carpentry").rows,
                Row("building_carpentry", "2", "item_missing_level", "-1", "", "MAYBE", "bad row"));
            WriteRaw(download);

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("levels must be greater than or equal to 0."));
            Assert.That(message, Does.Contain("main_stat_id must be one of Strength, Agility, Intelligence, Endurance, Luck"));
            Assert.That(message, Does.Contain("Expected materials format id:count; id:count."));
            Assert.That(message, Does.Contain("building_level does not exist in BuildingLevels for this building_id."));
            Assert.That(message, Does.Contain("sort_order must be greater than or equal to 0."));
            Assert.That(message, Does.Contain("ui_category is required."));
            Assert.That(message, Does.Contain("Expected TRUE or FALSE."));
        }

        [Test]
        public void BuildRuntimeJson_RequiresEnabledStage2()
        {
            var missingStage2 = CreateValidDownload();
            ReplaceSheet(missingStage2, Sheet("SettlementStages",
                Row("stage_id", "name_id", "description_id", "stage_prefab_id", "target_duration_sec", "completion_rule", "next_stage_id", "sort_order", "enabled", "notes"),
                Row("stage_arrival", "stage.arrival.name", "stage.arrival.desc", "stage_arrival_location", "1800", "AllRequired", "", "10", "TRUE", "note")));
            WriteRaw(missingStage2);

            var missingReport = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            Assert.That(missingReport.Success, Is.False);
            Assert.That(missingReport.ToDisplayMessage(), Does.Contain("stage_2 is required."));

            var disabledStage2 = CreateValidDownload();
            FindSheet(disabledStage2, "SettlementStages").rows[2].cells[8] = "FALSE";
            WriteRaw(disabledStage2);

            var disabledReport = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            Assert.That(disabledReport.Success, Is.False);
            Assert.That(disabledReport.ToDisplayMessage(), Does.Contain("stage_2 must be enabled."));
        }

        [Test]
        public void BuildRuntimeJson_RejectsRuntimeRowsThatReferenceDisabledStage()
        {
            var download = CreateValidDownload();
            var stages = FindSheet(download, "SettlementStages");
            stages.rows = Append(stages.rows, Row("stage_disabled", "stage.disabled.name", "stage.disabled.desc", "stage_disabled_location", "0", "AllRequired", "", "30", "FALSE", "note"));
            FindSheet(download, "SettlementStageSlots").rows = Append(
                FindSheet(download, "SettlementStageSlots").rows,
                Row("stage_disabled", "slot_disabled", "building_hall", "30", "TRUE", "note"));
            FindSheet(download, "SettlementStageObjectives").rows = Append(
                FindSheet(download, "SettlementStageObjectives").rows,
                Row("stage_disabled", "quest_disabled_stage", "100", "TRUE", "30", "note"));
            WriteRaw(download);

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("SettlementStageSlots row 4 column 'stage_id' value 'stage_disabled'"));
            Assert.That(message, Does.Contain("SettlementStageObjectives row 4 column 'stage_id' value 'stage_disabled'"));
            Assert.That(message, Does.Contain("missing enabled SettlementStages.stage_id"));
        }

        [Test]
        public void BuildRuntimeJson_RejectsActiveStageNextStageIdWhenTargetStageDisabled()
        {
            var download = CreateValidDownload();
            FindSheet(download, "SettlementStages").rows[1].cells[6] = "stage_disabled";
            FindSheet(download, "SettlementStages").rows = Append(
                FindSheet(download, "SettlementStages").rows,
                Row("stage_disabled", "stage.disabled.name", "stage.disabled.desc", "stage_disabled_location", "0", "AllRequired", "", "30", "FALSE", "note"));
            WriteRaw(download);

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("SettlementStages row 2 column 'next_stage_id' value 'stage_disabled'"));
            Assert.That(report.ToDisplayMessage(), Does.Contain("missing enabled SettlementStages.stage_id"));
        }

        [Test]
        public void ParseAndWrite_DoesNotOverwriteExistingRuntimeWhenValidationFails()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Craftables - Carpentry").rows[1].cells[2] = "item_gold";
            WriteRaw(download);
            WriteProjectFile(TestRuntimePath, "{\"previous\":true}\n");

            var report = new BuildingsConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.False);
            Assert.That(ReadProjectFile(TestRuntimePath), Is.EqualTo("{\"previous\":true}\n"));
        }

        [Test]
        public void ParseAndWrite_WritesRuntimeOnlyAfterSuccessfulValidation()
        {
            WriteRaw(CreateValidDownload());

            var report = new BuildingsConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(ReadProjectFile(TestRuntimePath), Does.Contain("\"buildingCraftables\""));
        }

        private static ConfigSourceSettings CreateSource()
        {
            return new ConfigSourceSettings
            {
                config_id = "buildings_configs",
                output_json_path = TestRawPath,
                runtime_json_path = TestRuntimePath
            };
        }

        private static ConfigSheetDownload CreateValidDownload()
        {
            return new ConfigSheetDownload
            {
                config_id = "buildings_configs",
                display_name = "GuildIdle - Buildings Configs",
                source_type = "GoogleSheet",
                sheet_url = "https://docs.google.com/spreadsheets/d/test",
                downloaded_at_utc = "2026-07-05T00:00:00Z",
                sheets = new[]
                {
                    Sheet("Index",
                        Row("building_id", "Title", "name_id", "description_id", "small_icon_id", "levels", "unlocked_by_hall_level", "mvp_required", "notes", "start_level", "visible_at_start", "clickable_requirement"),
                        Row("building_hall", "Hall", "building_hall_name_id", "building_hall_description_id", "building_hall_small_icon_id", "1", "0", "TRUE", "note", "0", "TRUE", ""),
                        Row("building_underwood", "Underwood", "building_underwood_name_id", "building_underwood_description_id", "building_underwood_small_icon_id", "1", "1", "TRUE", "note", "0", "TRUE", ""),
                        Row("building_warehouse", "Warehouse", "building_warehouse_name_id", "building_warehouse_description_id", "building_warehouse_small_icon_id", "1", "1", "TRUE", "note", "0", "TRUE", "building_hall:1"),
                        Row("building_carpentry", "Carpentry", "building_carpentry_name_id", "building_carpentry_description_id", "building_carpentry_small_icon_id", "1", "1", "TRUE", "note", "0", "TRUE", "building_hall:1")),
                    BuildingSheet("Hall", "building_hall", "building_hall_name_id", "building_hall_description_id", "building_hall_small_icon_id", "build_hall", "",
                        Row("0", "building_hall_level_0", "", "Ruined Hall", "0", "0", "", "", "", "", "", "", "note", ""),
                        Row("1", "building_hall_level_1", "build_hall", "Repair Hall", "30", "100", "skill_construction", "Intelligence", "resource_pine_wood:5", "combat_clear_hall_forest:1", "", "", "note", "5")),
                    BuildingSheet("Underwood", "building_underwood", "building_underwood_name_id", "building_underwood_description_id", "building_underwood_small_icon_id", "combat_clear_hall_forest", "",
                        Row("0", "building_underwood_level_0", "", "Overgrown forest", "0", "0", "", "", "", "", "", "", "note", ""),
                        Row("1", "building_underwood_level_1", "combat_clear_hall_forest", "Clear forest", "0", "0", "skill_combat", "Strength", "", "", "", "", "note", "")),
                    BuildingSheet("Warehouse", "building_warehouse", "building_warehouse_name_id", "building_warehouse_description_id", "building_warehouse_small_icon_id", "build_warehouse", "",
                        Row("0", "building_warehouse_level_0", "", "Ruined Warehouse", "0", "0", "", "", "", "", "building_hall:1", "", "note", ""),
                        Row("1", "building_warehouse_level_1", "build_warehouse", "Repair Warehouse", "30", "80", "skill_construction", "Intelligence", "resource_pine_wood:3", "", "building_hall:1", "", "note", "5")),
                    BuildingSheet("Carpentry", "building_carpentry", "building_carpentry_name_id", "building_carpentry_description_id", "building_carpentry_small_icon_id", "build_carpentry", "Craftables - Carpentry",
                        Row("0", "building_carpentry_level_0", "", "Ruined Carpentry", "0", "0", "", "", "", "", "building_hall:1", "", "note", ""),
                        Row("1", "building_carpentry_level_1", "build_carpentry", "Repair Carpentry", "30", "80", "skill_construction", "Intelligence", "resource_pine_wood:3", "", "building_hall:1", "", "note", "5")),
                    Sheet("BuildingActivities",
                        Row("building_id", "building_level", "activity_id", "sort_order", "show_if_activity_completed", "hide_if_activity_completed", "clickable_requirement", "enabled", "notes"),
                        Row("building_underwood", "0", "combat_clear_hall_forest", "10", "", "combat_clear_hall_forest", "", "TRUE", "note"),
                        Row("building_hall", "0", "build_hall", "10", "combat_clear_hall_forest", "build_hall", "", "TRUE", "note"),
                        Row("building_warehouse", "0", "build_warehouse", "20", "", "", "building_hall:1", "TRUE", "note"),
                        Row("building_carpentry", "0", "missing_disabled_activity", "30", "", "", "building_hall:1", "FALSE", "note")),
                    Sheet("SettlementStages",
                        Row("stage_id", "name_id", "description_id", "stage_prefab_id", "target_duration_sec", "completion_rule", "next_stage_id", "sort_order", "enabled", "notes"),
                        Row("stage_arrival", "stage.arrival.name", "stage.arrival.desc", "stage_arrival_location", "1800", "AllRequired", "stage_2", "10", "TRUE", "note"),
                        Row("stage_2", "stage.2.name", "stage.2.desc", "stage_2_location", "0", "AllRequired", "", "20", "TRUE", "note")),
                    Sheet("SettlementStageSlots",
                        Row("stage_id", "slot_id", "building_id", "sort_order", "enabled", "notes"),
                        Row("stage_arrival", "slot_hall", "building_hall", "10", "TRUE", "note"),
                        Row("stage_arrival", "slot_underwood", "building_underwood", "20", "TRUE", "note")),
                    Sheet("SettlementStageObjectives",
                        Row("stage_id", "quest_id", "weight_percent", "required", "sort_order", "notes"),
                        Row("stage_arrival", "quest_build_hall", "50", "TRUE", "10", "note"),
                        Row("stage_arrival", "quest_clear_underwood", "50", "TRUE", "20", "note")),
                    Sheet("Craftables - Carpentry",
                        Row("building_id", "building_level", "item_id", "sort_order", "ui_category", "enabled", "notes"),
                        Row("building_carpentry", "1", "resource_pine_plank", "10", "Materials", "TRUE", "note")),
                    Sheet("README", Row("Section", "Description"), Row("README", "This sheet must not be emitted."))
                }
            };
        }

        private static ConfigDownloadedSheet BuildingSheet(
            string name,
            string buildingId,
            string nameId,
            string descriptionId,
            string smallIconId,
            string sourceActivityId,
            string craftablesSheet,
            params ConfigSheetRow[] levelRows)
        {
            var rows = new List<ConfigSheetRow>
            {
                Row("building_id", buildingId),
                Row("Title", name),
                Row("name_id", nameId),
                Row("description_id", descriptionId),
                Row("small_icon_id", smallIconId),
                Row("source_activity_id", sourceActivityId),
                Row("craftables_sheet", craftablesSheet),
                Row("", ""),
                Row("level", "level_prefab_id", "source_activity_id", "Title", "duration_sec", "build_points_required", "craft_skill_id", "main_stat_id", "materials", "requirements_activities", "requirements_buildings", "requirements_skills", "notes", "skill_exp")
            };
            rows.AddRange(levelRows);
            return Sheet(name, rows.ToArray());
        }

        private static ConfigDownloadedSheet CreateCompressedHallSheet()
        {
            return Sheet("Hall",
                Row(
                    "field building_id Title name_id description_id small_icon_id source_activity_id craftables_sheet level",
                    "value building_hall Hall building_hall_name_id building_hall_description_id building_hall_small_icon_id build_hall level_prefab_id",
                    "source_activity_id",
                    "Title",
                    "duration_sec",
                    "build_points_required",
                    "craft_skill_id",
                    "main_stat_id",
                    "materials",
                    "requirements_activities",
                    "requirements_buildings",
                    "requirements_skills",
                    "notes",
                    "skill_exp"),
                Row("0", "building_hall_level_0", "", "Ruined Hall", "0", "0", "", "", "", "", "", "", "note", ""),
                Row("1", "building_hall_level_1", "build_hall", "Repair Hall", "30", "100", "skill_construction", "Intelligence", "resource_pine_wood:5", "combat_clear_hall_forest:1", "", "", "note", "5"));
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

        private static void ReplaceSheet(ConfigSheetDownload download, ConfigDownloadedSheet replacement)
        {
            for (var index = 0; index < download.sheets.Length; index++)
            {
                if (string.Equals(download.sheets[index].sheet_name, replacement.sheet_name, StringComparison.OrdinalIgnoreCase))
                {
                    download.sheets[index] = replacement;
                    return;
                }
            }

            throw new InvalidOperationException($"Missing test sheet {replacement.sheet_name}.");
        }

        private static void WriteRaw(ConfigSheetDownload download)
        {
            WriteProjectFile(TestRawPath, JsonUtility.ToJson(download, true));
        }

        private static void WriteProjectFile(string projectPath, string text)
        {
            var fullPath = FullProjectPath(projectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, text, ConfigPipelineUtilities.Utf8NoBom);
        }

        private static string ReadProjectFile(string projectPath)
        {
            return File.ReadAllText(FullProjectPath(projectPath), Encoding.UTF8);
        }

        private static void DeleteProjectFile(string projectPath)
        {
            var fullPath = FullProjectPath(projectPath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }

        private static string FullProjectPath(string projectPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, projectPath.Replace('\\', '/')));
        }
    }
}
