using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GuildIdle.Configs;
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
            Assert.That(runtimeJson, Does.Contain("\"settlementStageBuildings\""));
            Assert.That(runtimeJson, Does.Not.Contain("slotId"));
            Assert.That(runtimeJson, Does.Not.Contain("\"settlementStages\""));
            Assert.That(runtimeJson, Does.Not.Contain("\"settlementStageObjectives\""));
            Assert.That(runtimeJson, Does.Contain("\"settlementStageStarterHeroes\""));
            Assert.That(runtimeJson, Does.Contain("\"settlementStageStarterEquipment\""));
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
            Assert.That(runtimeJson, Does.Contain("\"activeHeroLimit\": 1"));
            Assert.That(runtimeJson, Does.Contain("\"activityId\": \"build_warehouse\""));
            Assert.That(runtimeJson, Does.Not.Contain("missing_disabled_activity"));
            Assert.That(runtimeJson, Does.Not.Contain("levelImageId"));
            Assert.That(runtimeJson, Does.Not.Contain("README"));
            Assert.That(runtimeJson, Does.Not.Contain("Title"));
            Assert.That(runtimeJson, Does.Not.Contain("notes"));
            Assert.That(runtimeJson, Does.Not.Contain("cells"));
        }

        [Test]
        public void BuildRuntimeJson_ExportsOnlyExplicitEnabledStageBootstrapRows()
        {
            var download = CreateValidDownload();
            FindSheet(download, "SettlementStageStarterHeroes").rows = Append(
                FindSheet(download, "SettlementStageStarterHeroes").rows,
                Row("stage_arrival", "aska", "20", "FALSE", "disabled draft"));
            FindSheet(download, "SettlementStageStarterEquipment").rows = Append(
                FindSheet(download, "SettlementStageStarterEquipment").rows,
                Row("stage_arrival", "aska", "item_unused_sword", "weapon", "20", "FALSE", "disabled draft"));
            WriteRaw(download);

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);
            var runtime = JsonUtility.FromJson<BuildingsRuntimeConfigDto>(runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtime.settlementStageStarterHeroes, Has.Length.EqualTo(1));
            Assert.That(runtime.settlementStageStarterHeroes[0].heroId, Is.EqualTo("ren"));
            Assert.That(runtime.settlementStageStarterEquipment, Has.Length.EqualTo(1));
            Assert.That(runtime.settlementStageStarterEquipment[0].itemId, Is.EqualTo("item_wooden_club"));
            Assert.That(runtimeJson, Does.Not.Contain("item_unused_sword"));
        }

        [Test]
        public void BuildRuntimeJson_RejectsInvalidStageBootstrapRows()
        {
            var download = CreateValidDownload();
            FindSheet(download, "SettlementStageStarterHeroes").rows = Append(
                FindSheet(download, "SettlementStageStarterHeroes").rows,
                Row("stage_arrival", "ren", "20", "TRUE", "duplicate"),
                Row("stage_2", "aska", "10", "TRUE", "stage 2 must stay empty"));
            FindSheet(download, "SettlementStageStarterEquipment").rows = Append(
                FindSheet(download, "SettlementStageStarterEquipment").rows,
                Row("stage_arrival", "aska", "item_unused_sword", "weapon", "20", "TRUE", "hero is not a starter"),
                Row("stage_2", "aska", "item_unused_sword", "weapon", "10", "TRUE", "stage 2 must stay empty"));
            WriteRaw(download);

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("Duplicate stage_id + hero_id"));
            Assert.That(message, Does.Contain("hero_id must be enabled in SettlementStageStarterHeroes"));
        }

        [Test]
        public void BuildRuntimeJson_RejectsDuplicateStageBuildingMembership()
        {
            var download = CreateValidDownload();
            var memberships = FindSheet(download, "SettlementStageBuildings");
            memberships.rows = Append(
                memberships.rows,
                Row("stage_arrival", "building_hall", "TRUE", "duplicate"));
            WriteRaw(download);

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("Duplicate stage_id + building_id"));
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
        public void BuildRuntimeJson_AllowsEmptyBuildFormulaWithZeroPointsForNonBuildLevel()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Underwood").rows[9].cells[3] = "";
            FindSheet(download, "Underwood").rows[9].cells[4] = "0";
            WriteRaw(download);

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Not.Contain("\"id\": \"combat_clear_hall_forest\""));
        }

        [Test]
        public void BuildRuntimeJson_RejectsLiteralZeroFatigueForNonBuildLevel()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Underwood").rows[9].cells[6] = "0";
            WriteRaw(download);

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("fatigue_cost must be empty for a non-build level."));
        }

        [Test]
        public void BuildRuntimeJson_AcceptsPlainActivityRequirementAndDefaultsCountToOne()
        {
            WriteRaw(CreateValidDownload());

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);
            Assert.That(report.Success, Is.True, report.ToDisplayMessage());

            var runtime = JsonUtility.FromJson<BuildingsRuntimeConfigDto>(runtimeJson);
            var underwoodLevel = Array.Find(runtime.buildingLevels, row =>
                row.buildingId == "building_underwood" && row.level == 1);

            Assert.That(underwoodLevel, Is.Not.Null);
            Assert.That(underwoodLevel.requirementsActivities, Has.Length.EqualTo(1));
            Assert.That(underwoodLevel.requirementsActivities[0].activityId, Is.EqualTo("combat_clear_hall_forest"));
            Assert.That(underwoodLevel.requirementsActivities[0].count, Is.EqualTo(1));
        }

        [Test]
        public void BuildRuntimeJson_CraftablesDoNotCreateCraftActions()
        {
            WriteRaw(CreateValidDownload());

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"craftId\": \"process_pine_plank\""));
            Assert.That(runtimeJson, Does.Not.Contain("\"type\": \"Craft\""));
            Assert.That(runtimeJson, Does.Not.Contain("\"type\": \"Process\""));
        }

        [Test]
        public void BuildRuntimeJson_RejectsDuplicateCraftableWithNormalizedBuildingLevel()
        {
            var download = CreateValidDownload();
            var craftables = FindSheet(download, "Craftables - Carpentry");
            craftables.rows = Append(
                craftables.rows,
                Row("building_carpentry", "01", "process_pine_plank", "20", "Materials", "TRUE", "duplicate"));
            WriteRaw(download);

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("Duplicate building_id + building_level + craft_id"));
        }

        [Test]
        public void BuildRuntimeJson_ExportsCanonicalBuildFields()
        {
            WriteRaw(CreateValidDownload());

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"buildFormulaId\": \"formula_build_default\""));
            Assert.That(runtimeJson, Does.Contain("\"skillId\": \"skill_construction\""));
            Assert.That(runtimeJson, Does.Contain("\"fatigueCost\": 0"));
            Assert.That(runtimeJson, Does.Not.Contain("\"durationSec\""));
            Assert.That(runtimeJson, Does.Not.Contain("\"craftSkillId\""));
            Assert.That(runtimeJson, Does.Not.Contain("\"mainStatId\""));
        }

        [Test]
        public void BuildRuntimeJson_RejectsPartialBuildFieldsAndFormulaZero()
        {
            var partial = CreateValidDownload();
            FindSheet(partial, "Underwood").rows[9].cells[5] = "skill_construction";
            WriteRaw(partial);

            var partialReport = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            Assert.That(partialReport.Success, Is.False);
            Assert.That(partialReport.ToDisplayMessage(), Does.Contain("build_formula_id is required for a declared build action."));

            var zeroFormula = CreateValidDownload();
            FindSheet(zeroFormula, "Underwood").rows[9].cells[3] = "0";
            WriteRaw(zeroFormula);

            var zeroReport = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            Assert.That(zeroReport.Success, Is.False);
            Assert.That(zeroReport.ToDisplayMessage(), Does.Contain("build_formula_id value 0 is not a valid formula reference."));
            Assert.That(zeroReport.ToDisplayMessage(), Does.Contain("source_activity_id must start with build_ for a declared build action."));
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
            FindSheet(download, "Hall").rows[10].cells[6] = "-1";
            FindSheet(download, "Hall").rows[10].cells[7] = "badpacked";
            FindSheet(download, "Craftables - Carpentry").rows = Append(
                FindSheet(download, "Craftables - Carpentry").rows,
                Row("building_carpentry", "2", "craft_missing_level", "-1", "", "MAYBE", "bad row"));
            WriteRaw(download);

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("levels must be greater than or equal to 0."));
            Assert.That(message, Does.Contain("fatigue_cost must be greater than or equal to 0"));
            Assert.That(message, Does.Contain("Expected materials format id:count; id:count."));
            Assert.That(message, Does.Contain("building_level does not exist in BuildingLevels for this building_id."));
            Assert.That(message, Does.Contain("sort_order must be greater than or equal to 0."));
            Assert.That(message, Does.Contain("ui_category is required."));
            Assert.That(message, Does.Contain("Expected TRUE or FALSE."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsActiveHeroLimitErrors()
        {
            var missing = CreateValidDownload();
            FindSheet(missing, "Hall").rows[9].cells[14] = "";
            WriteRaw(missing);

            var missingReport = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            Assert.That(missingReport.Success, Is.False);
            Assert.That(missingReport.ToDisplayMessage(), Does.Contain("active_hero_limit is required for active building_hall levels."));

            var negative = CreateValidDownload();
            FindSheet(negative, "Hall").rows[9].cells[14] = "-1";
            WriteRaw(negative);

            var negativeReport = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            Assert.That(negativeReport.Success, Is.False);
            Assert.That(negativeReport.ToDisplayMessage(), Does.Contain("active_hero_limit must be greater than or equal to 0."));

            var wrongStage1Value = CreateValidDownload();
            FindSheet(wrongStage1Value, "Hall").rows[10].cells[14] = "2";
            WriteRaw(wrongStage1Value);

            var wrongStage1Report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            Assert.That(wrongStage1Report.Success, Is.False);
            Assert.That(wrongStage1Report.ToDisplayMessage(), Does.Contain("building_hall level 0 and 1 must have active_hero_limit = 1"));
        }

        [Test]
        public void BuildRuntimeJson_AllowsMissingActiveHeroLimitColumnOnNonHallBuildings()
        {
            var download = CreateValidDownload();
            RemoveColumn(FindSheet(download, "Underwood"), "active_hero_limit");
            RemoveColumn(FindSheet(download, "Warehouse"), "active_hero_limit");
            RemoveColumn(FindSheet(download, "Carpentry"), "active_hero_limit");
            WriteRaw(download);

            var report = new BuildingsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"activeHeroLimit\": 1"));
        }

        [Test]
        public void ParseAndWrite_DoesNotOverwriteExistingRuntimeWhenValidationFails()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Underwood").rows[9].cells[3] = "0";
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
            Assert.That(ReadProjectFile(TestRuntimePath), Does.Contain("\"settlementStageBuildings\""));
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
                        Row("0", "building_hall_level_0", "", "", "0", "", "", "", "", "", "", "", "Ruined Hall", "note", "1"),
                        Row("1", "building_hall_level_1", "build_hall", "formula_build_default", "100", "skill_construction", "0", "resource_pine_wood:5", "combat_clear_hall_forest:1", "", "", "5", "Repair Hall", "note", "1")),
                    BuildingSheet("Underwood", "building_underwood", "building_underwood_name_id", "building_underwood_description_id", "building_underwood_small_icon_id", "combat_clear_hall_forest", "",
                        Row("0", "building_underwood_level_0", "", "", "0", "", "", "", "", "", "", "", "Overgrown forest", "note", ""),
                        Row("1", "building_underwood_level_1", "combat_clear_hall_forest", "", "0", "", "", "", "combat_clear_hall_forest", "", "", "", "Clear forest", "note", "")),
                    BuildingSheet("Warehouse", "building_warehouse", "building_warehouse_name_id", "building_warehouse_description_id", "building_warehouse_small_icon_id", "build_warehouse", "",
                        Row("0", "building_warehouse_level_0", "", "", "0", "", "", "", "", "building_hall:1", "", "", "Ruined Warehouse", "note", ""),
                        Row("1", "building_warehouse_level_1", "build_warehouse", "formula_build_default", "80", "skill_construction", "0", "resource_pine_wood:3", "", "building_hall:1", "", "5", "Repair Warehouse", "note", "")),
                    BuildingSheet("Carpentry", "building_carpentry", "building_carpentry_name_id", "building_carpentry_description_id", "building_carpentry_small_icon_id", "build_carpentry", "Craftables - Carpentry",
                        Row("0", "building_carpentry_level_0", "", "", "0", "", "", "", "", "building_hall:1", "", "", "Ruined Carpentry", "note", ""),
                        Row("1", "building_carpentry_level_1", "build_carpentry", "formula_build_default", "80", "skill_construction", "0", "resource_pine_wood:3", "", "building_hall:1", "", "5", "Repair Carpentry", "note", "")),
                    Sheet("BuildingActivities",
                        Row("building_id", "building_level", "activity_id", "sort_order", "show_if_activity_completed", "hide_if_activity_completed", "clickable_requirement", "enabled", "notes"),
                        Row("building_underwood", "0", "combat_clear_hall_forest", "10", "", "combat_clear_hall_forest", "", "TRUE", "note"),
                        Row("building_hall", "0", "build_hall", "10", "combat_clear_hall_forest", "build_hall", "", "TRUE", "note"),
                        Row("building_warehouse", "0", "build_warehouse", "20", "", "", "building_hall:1", "TRUE", "note"),
                        Row("building_carpentry", "0", "missing_disabled_activity", "30", "", "", "building_hall:1", "FALSE", "note")),
                    Sheet("SettlementStageBuildings",
                        Row("stage_id", "building_id", "enabled", "notes"),
                        Row("stage_arrival", "building_hall", "TRUE", "note"),
                        Row("stage_arrival", "building_underwood", "TRUE", "note")),
                    Sheet("SettlementStageStarterHeroes",
                        Row("stage_id", "hero_id", "sort_order", "enabled", "notes"),
                        Row("stage_arrival", "ren", "10", "TRUE", "note")),
                    Sheet("SettlementStageStarterEquipment",
                        Row("stage_id", "hero_id", "item_id", "equipment_slot", "sort_order", "enabled", "notes"),
                        Row("stage_arrival", "ren", "item_wooden_club", "weapon", "10", "TRUE", "note")),
                    Sheet("Craftables - Carpentry",
                        Row("building_id", "building_level", "craft_id", "sort_order", "ui_category", "enabled", "notes"),
                        Row("building_carpentry", "1", "process_pine_plank", "10", "Materials", "TRUE", "note")),
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
                Row("level", "level_prefab_id", "source_activity_id", "build_formula_id", "build_points_required", "skill_id", "fatigue_cost", "materials", "requirements_activities", "requirements_buildings", "requirements_skills", "skill_exp", "Title", "notes", "active_hero_limit")
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
                    "build_formula_id",
                    "build_points_required",
                    "skill_id",
                    "fatigue_cost",
                    "materials",
                    "requirements_activities",
                    "requirements_buildings",
                    "requirements_skills",
                    "skill_exp",
                    "Title",
                    "notes",
                    "active_hero_limit"),
                Row("0", "building_hall_level_0", "", "", "0", "", "", "", "", "", "", "", "Ruined Hall", "note", "1"),
                Row("1", "building_hall_level_1", "build_hall", "formula_build_default", "100", "skill_construction", "0", "resource_pine_wood:5", "combat_clear_hall_forest:1", "", "", "5", "Repair Hall", "note", "1"));
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

        private static void RemoveColumn(ConfigDownloadedSheet sheet, string column)
        {
            var headerRowIndex = -1;
            var columnIndex = -1;
            for (var rowIndex = 0; rowIndex < sheet.rows.Length; rowIndex++)
            {
                var cells = sheet.rows[rowIndex].cells;
                for (var cellIndex = 0; cellIndex < cells.Length; cellIndex++)
                {
                    if (!string.Equals(cells[cellIndex], column, StringComparison.Ordinal))
                        continue;

                    headerRowIndex = rowIndex;
                    columnIndex = cellIndex;
                    break;
                }

                if (columnIndex >= 0)
                    break;
            }

            if (columnIndex < 0)
                throw new InvalidOperationException($"Missing test column {column} in {sheet.sheet_name}.");

            for (var rowIndex = headerRowIndex; rowIndex < sheet.rows.Length; rowIndex++)
            {
                var cells = sheet.rows[rowIndex].cells;
                if (columnIndex >= cells.Length)
                    continue;

                var resized = new string[cells.Length - 1];
                for (var oldIndex = 0; oldIndex < cells.Length; oldIndex++)
                {
                    if (oldIndex == columnIndex)
                        continue;

                    var newIndex = oldIndex < columnIndex ? oldIndex : oldIndex - 1;
                    resized[newIndex] = cells[oldIndex];
                }

                sheet.rows[rowIndex].cells = resized;
            }
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
