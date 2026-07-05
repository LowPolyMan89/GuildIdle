using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class MapConfigsParserTests
    {
        private const string TestRawPath = "Temp/ConfigParserTests/map_configs.raw.json";
        private const string TestRuntimePath = "Assets/StreamingAssets/Configs/map_configs.test.runtime.json";

        [TearDown]
        public void TearDown()
        {
            DeleteProjectFile(TestRawPath);
            DeleteProjectFile(TestRuntimePath);
            DeleteProjectFile(TestRuntimePath + ".tmp");
            DeleteProjectFile(TestRuntimePath + ".meta");
        }

        [Test]
        public void BuildRuntimeJson_UsesStableShapeTypesAndExcludesReadmeNotesAndRawCells()
        {
            WriteRaw(CreateValidDownload());

            var report = new MapConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"mapCells\""));
            Assert.That(runtimeJson, Does.Contain("\"mapLocations\""));
            Assert.That(runtimeJson, Does.Contain("\"mapExplorationLevels\""));
            Assert.That(runtimeJson, Does.Contain("\"mapCellActivities\""));
            Assert.That(runtimeJson, Does.Contain("\"dangerEncounters\""));
            Assert.That(runtimeJson, Does.Contain("\"enumValues\""));
            Assert.That(runtimeJson, Does.Contain("\"cellId\": \"cell_village_0_0\""));
            Assert.That(runtimeJson, Does.Contain("\"q\": 0"));
            Assert.That(runtimeJson, Does.Contain("\"explorationDifficulty\": 1.5"));
            Assert.That(runtimeJson, Does.Contain("\"isBlocking\": false"));
            Assert.That(runtimeJson, Does.Contain("\"visibleInWatchtower\": true"));
            Assert.That(runtimeJson, Does.Contain("\"riskPercent\": 25"));
            Assert.That(runtimeJson, Does.Contain("\"visualMarkerId\": \"\""));
            Assert.That(runtimeJson, Does.Not.Contain("README"));
            Assert.That(runtimeJson, Does.Not.Contain("notes"));
            Assert.That(runtimeJson, Does.Not.Contain("cells"));
        }

        [Test]
        public void BuildRuntimeJson_ExportsMapNameIds()
        {
            WriteRaw(CreateValidDownload());

            var report = new MapConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"mapCellNameId\": \"map_cell_village_0_0_name_id\""));
            Assert.That(runtimeJson, Does.Contain("\"mapLocationNameId\": \"map_location_village_name_id\""));
            Assert.That(runtimeJson, Does.Not.Contain("nameRu"));
        }

        [Test]
        public void BuildRuntimeJson_MissingExternalRegistriesProduceWarningsOnly()
        {
            WriteRaw(CreateValidDownload());

            var report = new MapConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.True, message);
            Assert.That(runtimeJson, Does.Contain("\"mapCells\""));
            Assert.That(message, Does.Contain("Warning: Cross-config validation skipped: Activity Configs registry is not available yet."));
            Assert.That(message, Does.Contain("Warning: Cross-config validation skipped: Enemies Configs registry is not available yet."));
            Assert.That(message, Does.Contain("Warning: Cross-config validation skipped: Localisation registry is not available yet."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsMissingSheetAndColumn()
        {
            var download = CreateValidDownload();
            RemoveSheet(download, "DangerEncounters");
            RemoveHeader(FindSheet(download, "MapCells"), "terrain_type");
            WriteRaw(download);

            var report = new MapConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("DangerEncounters: Required sheet is missing."));
            Assert.That(message, Does.Contain("MapCells row 1 column 'terrain_type': Required column is missing."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsDuplicateIdsCoordinatesAndEnumPairs()
        {
            var download = CreateValidDownload();
            FindSheet(download, "MapCells").rows = Append(
                FindSheet(download, "MapCells").rows,
                Row("cell_village_0_0", "0", "0", "map_cell_dup_name_id", "Explored", "Village", "region_village", "village", "0", "0", "FALSE", "", "duplicate"));
            FindSheet(download, "MapLocations").rows = Append(
                FindSheet(download, "MapLocations").rows,
                Row("village", "map_location_dup_name_id", "Settlement", "1", "region_village", "cell_village_0_0", "TRUE", "duplicate"));
            FindSheet(download, "DangerEncounters").rows = Append(
                FindSheet(download, "DangerEncounters").rows,
                Row("danger_hunt_rabbits", "hunt_rabbits", "25", "OnActivityComplete", "enemy_group_hunting_rabbits", "1", "3", "Queue_1v1", "ActivityLootToCombatBag", "CombatDefeatLootLoss25To50", "duplicate"));
            FindSheet(download, "MapEnums").rows = Append(
                FindSheet(download, "MapEnums").rows,
                Row("MapCellState", "Explored", "duplicate"));
            WriteRaw(download);

            var report = new MapConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(IssuesContain(report, "Duplicate cell_id"), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "Duplicate q + r coordinate pair"), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "Duplicate location_id"), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "Duplicate danger_encounter_id"), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "Duplicate enum value in group 'MapCellState'."), Is.True, report.ToDisplayMessage());
        }

        [Test]
        public void BuildRuntimeJson_ReportsInvalidLocalReferences()
        {
            var download = CreateValidDownload();
            FindSheet(download, "MapCells").rows[1].cells[7] = "missing_location";
            FindSheet(download, "MapLocations").rows[1].cells[5] = "missing_cell";
            FindSheet(download, "MapCellActivities").rows[1].cells[1] = "village";
            WriteRaw(download);

            var report = new MapConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(IssuesContain(report, "Referenced location_id does not exist in MapLocations.location_id."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "Referenced default_cell_id does not exist in MapCells.cell_id."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "location_id does not match MapCells.location_id for this cell_id."), Is.True, report.ToDisplayMessage());
        }

        [Test]
        public void BuildRuntimeJson_ReportsTypeEnumAndRangeErrors()
        {
            var download = CreateValidDownload();
            FindSheet(download, "MapCells").rows[1].cells[1] = "0.5";
            FindSheet(download, "MapCells").rows[1].cells[4] = "Foggy";
            FindSheet(download, "MapCells").rows[1].cells[10] = "MAYBE";
            FindSheet(download, "MapLocations").rows[1].cells[3] = "0";
            FindSheet(download, "MapExplorationLevels").rows[2].cells[1] = "40";
            FindSheet(download, "MapCellActivities").rows[1].cells[3] = "3";
            FindSheet(download, "DangerEncounters").rows[1].cells[2] = "150";
            FindSheet(download, "DangerEncounters").rows[1].cells[5] = "4";
            FindSheet(download, "DangerEncounters").rows[1].cells[6] = "2";
            WriteRaw(download);

            var report = new MapConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(IssuesContain(report, "Expected an integer number."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "Value is not listed in MapEnums group 'MapCellState'."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "Expected TRUE or FALSE."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "tier must be greater than 0."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "points_required must not decrease as exploration_level increases."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "reveal_at_exploration_level does not exist in MapExplorationLevels.exploration_level."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "risk_percent must be in range 0..100."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "min_enemies must be <= max_enemies."), Is.True, report.ToDisplayMessage());
        }

        [Test]
        public void ParseAndWrite_DoesNotOverwriteExistingRuntimeWhenValidationFails()
        {
            var download = CreateValidDownload();
            FindSheet(download, "MapCells").rows[1].cells[1] = "bad_number";
            WriteRaw(download);

            WriteProjectFile(TestRuntimePath, "{\"previous\":true}\n");

            var report = new MapConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.False);
            Assert.That(ReadProjectFile(TestRuntimePath), Is.EqualTo("{\"previous\":true}\n"));
        }

        [Test]
        public void ParseAndWrite_WritesRuntimeOnlyAfterSuccessfulValidation()
        {
            WriteRaw(CreateValidDownload());

            var report = new MapConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(ReadProjectFile(TestRuntimePath), Does.Contain("\"mapCells\""));
        }

        private static ConfigSourceSettings CreateSource()
        {
            return new ConfigSourceSettings
            {
                config_id = "map_configs",
                output_json_path = TestRawPath,
                runtime_json_path = TestRuntimePath
            };
        }

        private static ConfigSheetDownload CreateValidDownload()
        {
            return new ConfigSheetDownload
            {
                config_id = "map_configs",
                display_name = "GuildIdle - Map Configs",
                source_type = "GoogleSheet",
                sheet_url = "https://docs.google.com/spreadsheets/d/test",
                downloaded_at_utc = "2026-07-05T00:00:00Z",
                sheets = new[]
                {
                    Sheet("MapCells",
                        Row("cell_id", "q", "r", "map_cell_name_id", "state_default", "terrain_type", "region_id", "location_id", "max_exploration_level", "exploration_difficulty", "is_blocking", "visual_marker_id", "notes"),
                        Row("cell_village_0_0", "0", "0", "map_cell_village_0_0_name_id", "Explored", "Village", "region_village", "village", "0", "0", "FALSE", "", "hub"),
                        Row("cell_fields_1", "1", "0", "map_cell_fields_1_name_id", "Unexplored", "Fields", "region_near", "fields_1", "2", "1.5", "FALSE", "poi_gathering", "node")),
                    Sheet("MapLocations",
                        Row("location_id", "map_location_name_id", "location_type", "tier", "region_id", "default_cell_id", "visible_in_watchtower", "notes"),
                        Row("village", "map_location_village_name_id", "Settlement", "1", "region_village", "cell_village_0_0", "TRUE", "hub"),
                        Row("fields_1", "map_location_fields_1_name_id", "ResourceNode", "1", "region_near", "cell_fields_1", "TRUE", "resource")),
                    Sheet("MapExplorationLevels",
                        Row("exploration_level", "points_required", "notes"),
                        Row("1", "50", "first"),
                        Row("2", "60", "second")),
                    Sheet("MapCellActivities",
                        Row("cell_id", "location_id", "activity_id", "reveal_at_exploration_level", "visible_in_watchtower", "notes"),
                        Row("cell_fields_1", "fields_1", "work_flax", "1", "TRUE", "visible")),
                    Sheet("DangerEncounters",
                        Row("danger_encounter_id", "activity_id", "risk_percent", "roll_moment", "enemy_group_id", "min_enemies", "max_enemies", "combat_mode", "loot_source", "defeat_loss_rule", "notes"),
                        Row("danger_hunt_rabbits", "hunt_rabbits", "25", "OnActivityComplete", "enemy_group_hunting_rabbits", "1", "3", "Queue_1v1", "ActivityLootToCombatBag", "CombatDefeatLootLoss25To50", "risk")),
                    Sheet("MapEnums",
                        Row("enum_group", "value", "description"),
                        Row("MapCellState", "Unexplored", "Hidden cell."),
                        Row("MapCellState", "Explored", "Known cell."),
                        Row("LocationType", "Settlement", "Hub."),
                        Row("LocationType", "ResourceNode", "Resource node."),
                        Row("TerrainType", "Village", "Village terrain."),
                        Row("TerrainType", "Fields", "Fields terrain."),
                        Row("MapVisualMarker", "poi_gathering", "Gathering marker."),
                        Row("CombatMode", "Queue_1v1", "One versus one queue."),
                        Row("DangerRollMoment", "OnActivityComplete", "Roll on complete."),
                        Row("DangerLootSource", "ActivityLootToCombatBag", "Move loot to combat bag."),
                        Row("DangerDefeatLossRule", "CombatDefeatLootLoss25To50", "Lose part of combat bag.")),
                    Sheet("README", Row("README"), Row("This sheet must not be emitted"))
                }
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

        private static void RemoveSheet(ConfigSheetDownload download, string sheetName)
        {
            var sheets = new List<ConfigDownloadedSheet>(download.sheets);
            sheets.RemoveAll(sheet => string.Equals(sheet.sheet_name, sheetName, StringComparison.OrdinalIgnoreCase));
            download.sheets = sheets.ToArray();
        }

        private static void RemoveHeader(ConfigDownloadedSheet sheet, string header)
        {
            var cells = new List<string>(sheet.rows[0].cells);
            cells.Remove(header);
            sheet.rows[0].cells = cells.ToArray();
        }

        private static bool IssuesContain(ConfigPipelineReport report, string text)
        {
            foreach (var issue in report.Issues)
            {
                if (issue.ToString().Contains(text))
                    return true;
            }

            return false;
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
