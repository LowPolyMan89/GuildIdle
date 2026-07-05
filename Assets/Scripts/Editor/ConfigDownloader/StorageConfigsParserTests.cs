using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class StorageConfigsParserTests
    {
        private const string TestRawPath = "Temp/ConfigParserTests/storage_configs.raw.json";
        private const string TestRuntimePath = "Assets/StreamingAssets/Configs/storage_configs.test.runtime.json";

        [TearDown]
        public void TearDown()
        {
            DeleteProjectFile(TestRawPath);
            DeleteProjectFile(TestRuntimePath);
            DeleteProjectFile(TestRuntimePath + ".tmp");
            DeleteProjectFile(TestRuntimePath + ".meta");
        }

        [Test]
        public void BuildRuntimeJson_UsesHeadersAndExcludesReadmeNotesAndRawCells()
        {
            WriteRaw(CreateValidDownload());

            var report = new StorageConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"storageRules\""));
            Assert.That(runtimeJson, Does.Contain("\"storageBuildings\""));
            Assert.That(runtimeJson, Does.Contain("\"itemStates\""));
            Assert.That(runtimeJson, Does.Contain("\"enumValues\""));
            Assert.That(runtimeJson, Does.Contain("\"storageRuleId\": \"storage_resource\""));
            Assert.That(runtimeJson, Does.Contain("\"buildingId\": \"building_warehouse\""));
            Assert.That(runtimeJson, Does.Contain("\"stateId\": \"on_storage\""));
            Assert.That(runtimeJson, Does.Contain("\"maxStack\": 100"));
            Assert.That(runtimeJson, Does.Contain("\"occupiesSlot\": true"));
            Assert.That(runtimeJson, Does.Contain("\"allowInstanceId\": false"));
            Assert.That(runtimeJson, Does.Not.Contain("README"));
            Assert.That(runtimeJson, Does.Not.Contain("notes"));
            Assert.That(runtimeJson, Does.Not.Contain("cells"));
        }

        [Test]
        public void BuildRuntimeJson_ExportsStorageItemStateNameId()
        {
            WriteRaw(CreateValidDownload());

            var report = new StorageConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"storageItemStateNameId\": \"storage_item_state_on_storage_name_id\""));
            Assert.That(runtimeJson, Does.Not.Contain("nameRu"));
        }

        [Test]
        public void BuildRuntimeJson_ReportsMissingSheetAndColumn()
        {
            var download = CreateValidDownload();
            RemoveSheet(download, "StorageBuildings");
            RemoveHeader(FindSheet(download, "StorageRules"), "mode");
            WriteRaw(download);

            var report = new StorageConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("StorageBuildings: Required sheet is missing."));
            Assert.That(message, Does.Contain("StorageRules row 1 column 'mode': Required column is missing."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsDuplicateIdsAndEnumPairs()
        {
            var download = CreateValidDownload();
            FindSheet(download, "StorageRules").rows = Append(
                FindSheet(download, "StorageRules").rows,
                Row("storage_resource", "resource", "stack", "100", "TRUE", "FALSE", "FALSE", "duplicate"));
            FindSheet(download, "StorageBuildings").rows = Append(
                FindSheet(download, "StorageBuildings").rows,
                Row("building_warehouse", "1", "40", "0", "FALSE", "FALSE", "duplicate"));
            FindSheet(download, "ItemStates").rows = Append(
                FindSheet(download, "ItemStates").rows,
                Row("on_storage", "storage_item_state_on_storage_name_id", "TRUE", "TRUE", "TRUE", "TRUE", "duplicate"));
            FindSheet(download, "Enums").rows = Append(
                FindSheet(download, "Enums").rows,
                Row("StorageMode", "stack", "duplicate"));
            WriteRaw(download);

            var report = new StorageConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("Duplicate storage_rule_id"));
            Assert.That(message, Does.Contain("Duplicate building_id + level pair"));
            Assert.That(message, Does.Contain("Duplicate state_id"));
            Assert.That(message, Does.Contain("Duplicate enum value in group 'StorageMode'."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsEnumBoolAndNumberErrors()
        {
            var download = CreateValidDownload();
            FindSheet(download, "StorageRules").rows[1].cells[1] = "currency";
            FindSheet(download, "StorageRules").rows[1].cells[2] = "bundle";
            FindSheet(download, "StorageRules").rows[1].cells[3] = "NaN";
            FindSheet(download, "StorageRules").rows[1].cells[4] = "MAYBE";
            FindSheet(download, "StorageBuildings").rows[1].cells[1] = "zero";
            WriteRaw(download);

            var report = new StorageConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("StorageRules row 2 column 'item_kind' value 'currency'"));
            Assert.That(message, Does.Contain("StorageRules row 2 column 'mode' value 'bundle'"));
            Assert.That(message, Does.Contain("StorageRules row 2 column 'max_stack' value 'NaN': Expected an integer number."));
            Assert.That(message, Does.Contain("StorageRules row 2 column 'occupies_slot' value 'MAYBE': Expected TRUE or FALSE."));
            Assert.That(message, Does.Contain("StorageBuildings row 2 column 'level' value 'zero': Expected an integer number."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsStorageRuleBusinessRules()
        {
            var download = CreateValidDownload();
            FindSheet(download, "StorageRules").rows[1].cells[3] = "1";
            FindSheet(download, "StorageRules").rows[1].cells[6] = "TRUE";
            FindSheet(download, "StorageRules").rows[4].cells[2] = "stack";
            FindSheet(download, "StorageRules").rows[4].cells[3] = "2";
            FindSheet(download, "StorageRules").rows[4].cells[5] = "FALSE";
            FindSheet(download, "StorageRules").rows[4].cells[6] = "FALSE";
            WriteRaw(download);

            var report = new StorageConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("stack mode requires max_stack greater than 1."));
            Assert.That(message, Does.Contain("stack mode requires allow_instance_id to be FALSE."));
            Assert.That(message, Does.Contain("equipment storage requires mode to be single."));
            Assert.That(message, Does.Contain("equipment storage requires max_stack to be 1."));
            Assert.That(message, Does.Contain("equipment storage requires allow_quality to be TRUE."));
            Assert.That(message, Does.Contain("equipment storage requires allow_instance_id to be TRUE."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsStorageBuildingRanges()
        {
            var download = CreateValidDownload();
            FindSheet(download, "StorageBuildings").rows[1].cells[1] = "0";
            FindSheet(download, "StorageBuildings").rows[1].cells[2] = "-1";
            FindSheet(download, "StorageBuildings").rows[1].cells[3] = "-1";
            WriteRaw(download);

            var report = new StorageConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("level must be greater than 0."));
            Assert.That(message, Does.Contain("slot_count must be greater than or equal to 0."));
            Assert.That(message, Does.Contain("resource_stack_bonus must be greater than or equal to 0."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsItemStateRules()
        {
            var download = CreateValidDownload();
            FindSheet(download, "ItemStates").rows[1].cells[2] = "FALSE";
            FindSheet(download, "ItemStates").rows[2].cells[3] = "TRUE";
            WriteRaw(download);

            var report = new StorageConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("on_storage must be available for craft."));
            Assert.That(message, Does.Contain("Busy or terminal states must not be available for sale."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsMissingOnStorageState()
        {
            var download = CreateValidDownload();
            FindSheet(download, "ItemStates").rows[1].cells[0] = "available";
            WriteRaw(download);

            var report = new StorageConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("Required state_id 'on_storage' is missing."));
        }

        [Test]
        public void BuildRuntimeJson_RejectsForbiddenLegacyItemGold()
        {
            var download = CreateValidDownload();
            FindSheet(download, "StorageRules").rows[1].cells[0] = "item_gold";
            WriteRaw(download);

            var report = new StorageConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("item_gold is a forbidden legacy item id in Storage Configs."));
        }

        [Test]
        public void BuildRuntimeJson_MissingExternalRegistriesProduceWarningsOnly()
        {
            WriteRaw(CreateValidDownload());

            var report = new StorageConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"storageRules\""));
            Assert.That(report.ToDisplayMessage(), Does.Contain("Warning: Cross-config validation skipped: Items Configs registry is not available yet."));
            Assert.That(report.ToDisplayMessage(), Does.Contain("Warning: Cross-config validation skipped: Buildings Configs registry is not available yet."));
            Assert.That(report.ToDisplayMessage(), Does.Contain("Warning: Cross-config validation skipped: Localisation registry is not available yet."));
        }

        [Test]
        public void ParseAndWrite_DoesNotOverwriteExistingRuntimeWhenValidationFails()
        {
            var download = CreateValidDownload();
            FindSheet(download, "StorageRules").rows[1].cells[3] = "bad_number";
            WriteRaw(download);

            WriteProjectFile(TestRuntimePath, "{\"previous\":true}\n");

            var report = new StorageConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.False);
            Assert.That(ReadProjectFile(TestRuntimePath), Is.EqualTo("{\"previous\":true}\n"));
        }

        [Test]
        public void ParseAndWrite_WritesRuntimeOnlyAfterSuccessfulValidation()
        {
            WriteRaw(CreateValidDownload());

            var report = new StorageConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(ReadProjectFile(TestRuntimePath), Does.Contain("\"storageRules\""));
        }

        private static ConfigSourceSettings CreateSource()
        {
            return new ConfigSourceSettings
            {
                config_id = "storage_configs",
                output_json_path = TestRawPath,
                runtime_json_path = TestRuntimePath
            };
        }

        private static ConfigSheetDownload CreateValidDownload()
        {
            return new ConfigSheetDownload
            {
                config_id = "storage_configs",
                display_name = "GuildIdle - Storage Configs",
                source_type = "GoogleSheet",
                sheet_url = "https://docs.google.com/spreadsheets/d/test",
                downloaded_at_utc = "2026-07-05T00:00:00Z",
                sheets = new[]
                {
                    Sheet("StorageRules",
                        Row("storage_rule_id", "item_kind", "mode", "max_stack", "occupies_slot", "allow_quality", "allow_instance_id", "notes"),
                        Row("storage_resource", "resource", "stack", "100", "TRUE", "FALSE", "FALSE", "Resources are stacked."),
                        Row("storage_consumable", "consumable", "stack", "20", "TRUE", "FALSE", "FALSE", "Consumables are stacked."),
                        Row("storage_recipe", "recipe", "stack", "20", "TRUE", "FALSE", "FALSE", "Recipes are stacked."),
                        Row("storage_equipment", "equipment", "single", "1", "TRUE", "TRUE", "TRUE", "Equipment is instanced.")),
                    Sheet("StorageBuildings",
                        Row("building_id", "level", "slot_count", "resource_stack_bonus", "auto_sort_enabled", "filters_enabled", "notes"),
                        Row("building_warehouse", "1", "40", "0", "FALSE", "FALSE", "MVP warehouse."),
                        Row("building_warehouse", "2", "60", "20", "TRUE", "FALSE", "Future level."),
                        Row("building_warehouse", "3", "90", "50", "TRUE", "TRUE", "Future level.")),
                    Sheet("ItemStates",
                        Row("state_id", "storage_item_state_name_id", "available_for_craft", "available_for_sale", "available_for_order", "available_for_equip", "notes"),
                        Row("on_storage", "storage_item_state_on_storage_name_id", "TRUE", "TRUE", "TRUE", "TRUE", "Free item."),
                        Row("equipped", "storage_item_state_equipped_name_id", "FALSE", "FALSE", "FALSE", "FALSE", "Equipped by hero."),
                        Row("reserved_for_task", "storage_item_state_reserved_for_task_name_id", "FALSE", "FALSE", "FALSE", "FALSE", "Reserved."),
                        Row("in_task", "storage_item_state_in_task_name_id", "FALSE", "FALSE", "FALSE", "FALSE", "In task."),
                        Row("handed_to_order", "storage_item_state_handed_to_order_name_id", "FALSE", "FALSE", "FALSE", "FALSE", "Handed to order."),
                        Row("sold", "storage_item_state_sold_name_id", "FALSE", "FALSE", "FALSE", "FALSE", "Sold."),
                        Row("deleted", "storage_item_state_deleted_name_id", "FALSE", "FALSE", "FALSE", "FALSE", "Deleted.")),
                    Sheet("Enums",
                        Row("enum_group", "value", "description"),
                        Row("StorageMode", "stack", "Stacked items."),
                        Row("StorageMode", "single", "Single items."),
                        Row("ItemKind", "resource", "Resource from Items Configs."),
                        Row("ItemKind", "consumable", "Consumable from Items Configs."),
                        Row("ItemKind", "recipe", "Recipe from Items Configs."),
                        Row("ItemKind", "equipment", "Equipment from Items Configs.")),
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

        private static void WriteRaw(ConfigSheetDownload download)
        {
            WriteProjectFile(TestRawPath, JsonUtility.ToJson(download, true));
        }

        private static void WriteProjectFile(string projectPath, string text)
        {
            var fullPath = FullProjectPath(projectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, text, Encoding.UTF8);
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
