using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class LootConfigsParserTests
    {
        private const string TestRawPath = "Temp/ConfigParserTests/loot_configs.raw.json";
        private const string TestRuntimePath = "Assets/StreamingAssets/Configs/loot_configs.test.runtime.json";

        [TearDown]
        public void TearDown()
        {
            DeleteProjectFile(TestRawPath);
            DeleteProjectFile(TestRuntimePath);
            DeleteProjectFile(TestRuntimePath + ".tmp");
            DeleteProjectFile(TestRuntimePath + ".meta");
        }

        [Test]
        public void BuildRuntimeJson_UsesStableShapeTypesAndExcludesHumanNotesReadmeAndRawCells()
        {
            WriteRaw(CreateValidDownload());

            var report = new LootConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"lootTables\""));
            Assert.That(runtimeJson, Does.Contain("\"lootTableEntries\""));
            Assert.That(runtimeJson, Does.Contain("\"lootGroups\""));
            Assert.That(runtimeJson, Does.Contain("\"enumValues\""));
            Assert.That(runtimeJson, Does.Contain("\"lootTableId\": \"hunting_rabbits_resources\""));
            Assert.That(runtimeJson, Does.Contain("\"rollCountMin\": 1"));
            Assert.That(runtimeJson, Does.Contain("\"enabled\": true"));
            Assert.That(runtimeJson, Does.Contain("\"chance\": 100"));
            Assert.That(runtimeJson, Does.Contain("\"rarityHint\": \"Common\""));
            Assert.That(runtimeJson, Does.Not.Contain("README"));
            Assert.That(runtimeJson, Does.Not.Contain("notes"));
            Assert.That(runtimeJson, Does.Not.Contain("cells"));
            Assert.That(runtimeJson, Does.Not.Contain("nameId"));
            Assert.That(runtimeJson, Does.Not.Contain("descriptionId"));
            Assert.That(runtimeJson, Does.Not.Contain("iconId"));
            Assert.That(runtimeJson, Does.Not.Contain("Title"));
        }

        [Test]
        public void BuildRuntimeJson_SupportsDecimalComma()
        {
            var download = CreateValidDownload();
            FindSheet(download, "LootTableEntries").rows[1].cells[5] = "12,5";
            WriteRaw(download);

            var report = new LootConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"weight\": 12.5"));
        }

        [Test]
        public void BuildRuntimeJson_MissingExternalRegistriesProduceWarningsOnly()
        {
            WriteRaw(CreateValidDownload());

            var report = new LootConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.True, message);
            Assert.That(runtimeJson, Does.Contain("\"lootTables\""));
            Assert.That(message, Does.Contain("Warning: Cross-config validation skipped: Items Configs registry is not available yet."));
            Assert.That(message, Does.Contain("Warning: Cross-config validation skipped: Currency registry is not available yet."));
            Assert.That(message, Does.Contain("Warning: Cross-config validation skipped: Activity Configs registry is not available yet."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsMissingSheetColumnAndEnumPairErrors()
        {
            var download = CreateValidDownload();
            RemoveSheet(download, "LootGroups");
            RemoveHeader(FindSheet(download, "LootTables"), "roll_mode");
            FindSheet(download, "EnumValues").rows = Append(
                FindSheet(download, "EnumValues").rows,
                Row("Missing group", "", "FreeTextValue", "description"),
                Row("Missing value", "DropType", "", "description"),
                Row("Missing description", "DropType", "Gem", ""),
                Row("Duplicate resource", "DropType", "Resource", "duplicate"));
            WriteRaw(download);

            var report = new LootConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("LootGroups: Required sheet is missing."));
            Assert.That(message, Does.Contain("LootTables row 1 column 'roll_mode': Required column is missing."));
            Assert.That(message, Does.Contain("EnumValues row 14 column 'enum_group'"));
            Assert.That(message, Does.Contain("EnumValues row 15 column 'value'"));
            Assert.That(message, Does.Contain("EnumValues row 16 column 'description'"));
            Assert.That(message, Does.Contain("Duplicate enum value in group 'DropType'."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsDuplicateIdsLocalLinksTypesEnumsAndRanges()
        {
            var download = CreateValidDownload();
            FindSheet(download, "LootTables").rows = Append(
                FindSheet(download, "LootTables").rows,
                Row("Duplicate table", "hunting_rabbits_resources", "BadType", "BadMode", "-1", "0", "MAYBE", "duplicate"));
            FindSheet(download, "LootGroups").rows = Append(
                FindSheet(download, "LootGroups").rows,
                Row("Duplicate group", "hunting_rabbits_resources", "default", "WeightedMany", "3", "1", "101", "duplicate"),
                Row("Missing table group", "missing_table", "default", "WeightedMany", "1", "1", "100", "missing"));
            FindSheet(download, "LootTableEntries").rows = Append(
                FindSheet(download, "LootTableEntries").rows,
                Row("Duplicate entry", "missing_table", "hunting_rabbit_thin_hide", "BadDrop", "resource_thin_hide", "-1", "4", "2", "150", "MissingRarity", "missing_group", "duplicate"));
            WriteRaw(download);

            var report = new LootConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(IssuesContain(report, "Duplicate loot_table_id"), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "Duplicate entry_id"), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "Duplicate loot_table_id + roll_group"), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "Referenced loot_table_id does not exist in LootTables.loot_table_id."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "Referenced required_roll_group does not exist in LootGroups.roll_group for this loot_table_id."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "Value is not listed in EnumValues group 'LootTableType'."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "Value is not listed in EnumValues group 'RollMode'."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "Value is not listed in EnumValues group 'DropType'."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "Value is not listed in EnumValues group 'RarityId'."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "Expected TRUE or FALSE."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "roll_count_min must be greater than or equal to 0."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "roll_count_min must be <= roll_count_max."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "chance must be in range 0..100."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "weight must be greater than or equal to 0."), Is.True, report.ToDisplayMessage());
            Assert.That(IssuesContain(report, "min must be <= max."), Is.True, report.ToDisplayMessage());
        }

        [Test]
        public void BuildRuntimeJson_AllowsGoldIdOnlyForGoldDropType()
        {
            var download = CreateValidDownload();
            FindSheet(download, "LootTableEntries").rows = Append(
                FindSheet(download, "LootTableEntries").rows,
                Row("Gold", "hunting_rabbits_resources", "hunting_rabbit_gold", "Gold", "gold_id", "50", "1", "3", "100", "", "default", "gold"));
            WriteRaw(download);

            var report = new LootConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"entryId\": \"hunting_rabbit_gold\""));
            Assert.That(runtimeJson, Does.Contain("\"targetId\": \"gold_id\""));
        }

        [Test]
        public void BuildRuntimeJson_RejectsGoldIdForNonGoldDropType()
        {
            var download = CreateValidDownload();
            FindSheet(download, "LootTableEntries").rows[1].cells[4] = "gold_id";
            WriteRaw(download);

            var report = new LootConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("gold_id is a currency_id and is allowed only when drop_type = Gold."));
        }

        [Test]
        public void BuildRuntimeJson_RejectsItemGoldAnywhere()
        {
            var download = CreateValidDownload();
            FindSheet(download, "LootTableEntries").rows[1].cells[4] = "item_gold";
            WriteRaw(download);

            var report = new LootConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("item_gold is a forbidden legacy id in Loot Configs."));
        }

        [Test]
        public void ParseAndWrite_DoesNotOverwriteExistingRuntimeWhenValidationFails()
        {
            var download = CreateValidDownload();
            FindSheet(download, "LootTableEntries").rows[1].cells[7] = "bad_number";
            WriteRaw(download);
            WriteProjectFile(TestRuntimePath, "{\"previous\":true}\n");

            var report = new LootConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.False);
            Assert.That(ReadProjectFile(TestRuntimePath), Is.EqualTo("{\"previous\":true}\n"));
        }

        [Test]
        public void ParseAndWrite_WritesRuntimeOnlyAfterSuccessfulValidation()
        {
            WriteRaw(CreateValidDownload());

            var report = new LootConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(ReadProjectFile(TestRuntimePath), Does.Contain("\"lootTables\""));
        }

        private static ConfigSourceSettings CreateSource()
        {
            return new ConfigSourceSettings
            {
                config_id = "loot_configs",
                output_json_path = TestRawPath,
                runtime_json_path = TestRuntimePath
            };
        }

        private static ConfigSheetDownload CreateValidDownload()
        {
            return new ConfigSheetDownload
            {
                config_id = "loot_configs",
                display_name = "GuildIdle - Loot Configs",
                source_type = "GoogleSheet",
                sheet_url = "https://docs.google.com/spreadsheets/d/test",
                downloaded_at_utc = "2026-07-05T00:00:00Z",
                sheets = new[]
                {
                    Sheet("LootTables",
                        Row("Title", "loot_table_id", "table_type", "roll_mode", "roll_count_min", "roll_count_max", "enabled", "notes"),
                        Row("Hunting rabbits", "hunting_rabbits_resources", "ResourceDrop", "WeightedMany", "1", "2", "TRUE", "designer note")),
                    Sheet("LootTableEntries",
                        Row("Title", "loot_table_id", "entry_id", "drop_type", "target_id", "weight", "min", "max", "chance", "rarity_hint", "required_roll_group", "notes"),
                        Row("Thin hide", "hunting_rabbits_resources", "hunting_rabbit_thin_hide", "Resource", "resource_thin_hide", "100", "1", "2", "100", "Common", "default", "designer note")),
                    Sheet("LootGroups",
                        Row("Title", "loot_table_id", "roll_group", "roll_mode", "roll_count_min", "roll_count_max", "chance", "notes"),
                        Row("Default", "hunting_rabbits_resources", "default", "WeightedMany", "1", "2", "100", "designer note")),
                    Sheet("EnumValues",
                        Row("Title", "enum_group", "value", "description"),
                        Row("LootTableType / ItemDrop", "LootTableType", "ItemDrop", "Item table."),
                        Row("LootTableType / ResourceDrop", "LootTableType", "ResourceDrop", "Resource table."),
                        Row("LootTableType / GoldDrop", "LootTableType", "GoldDrop", "Gold table."),
                        Row("LootTableType / Mixed", "LootTableType", "Mixed", "Mixed table."),
                        Row("RollMode / WeightedOne", "RollMode", "WeightedOne", "One weighted result."),
                        Row("RollMode / WeightedMany", "RollMode", "WeightedMany", "Many weighted results."),
                        Row("RollMode / GuaranteedAll", "RollMode", "GuaranteedAll", "All results."),
                        Row("DropType / Item", "DropType", "Item", "Item."),
                        Row("DropType / Resource", "DropType", "Resource", "Resource."),
                        Row("DropType / Gold", "DropType", "Gold", "Gold."),
                        Row("RarityId / Common", "RarityId", "Common", "Common."),
                        Row("RarityId / Rare", "RarityId", "Rare", "Rare.")),
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
