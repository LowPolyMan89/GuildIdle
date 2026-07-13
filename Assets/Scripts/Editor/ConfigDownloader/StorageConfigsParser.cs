using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class StorageConfigsParser : IConfigPipelineParser
    {
        private const string ConfigId = "storage_configs";

        private static readonly string[] RequiredSheets =
        {
            "StorageRules",
            "StorageBuildings",
            "ItemStates",
            "Enums"
        };

        private static readonly Dictionary<string, string[]> RequiredColumns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["StorageRules"] = new[] { "storage_rule_id", "item_kind", "mode", "max_stack", "occupies_slot", "allow_quality", "allow_instance_id", "notes" },
            ["StorageBuildings"] = new[] { "building_id", "level", "slot_count", "resource_stack_bonus", "auto_sort_enabled", "filters_enabled", "notes" },
            ["ItemStates"] = new[] { "state_id", "storage_item_state_name_id", "available_for_craft", "available_for_sale", "available_for_order", "available_for_equip", "notes" },
            ["Enums"] = new[] { "enum_group", "value", "description" }
        };

        private static readonly Dictionary<string, string> RuntimeArrayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["StorageRules"] = "storageRules",
            ["StorageBuildings"] = "storageBuildings",
            ["ItemStates"] = "itemStates",
            ["Enums"] = "enumValues"
        };

        private static readonly HashSet<string> RuntimeExcludedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "notes"
        };

        private static readonly HashSet<string> IntegerFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FieldKey("StorageRules", "max_stack"),
            FieldKey("StorageBuildings", "level"),
            FieldKey("StorageBuildings", "slot_count"),
            FieldKey("StorageBuildings", "resource_stack_bonus")
        };

        private static readonly HashSet<string> BoolColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "occupies_slot",
            "allow_quality",
            "allow_instance_id",
            "auto_sort_enabled",
            "filters_enabled",
            "available_for_craft",
            "available_for_sale",
            "available_for_order",
            "available_for_equip"
        };

        private static readonly Dictionary<string, string> EnumColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["item_kind"] = "ItemKind",
            ["mode"] = "StorageMode"
        };

        private static readonly string[] BusyStateIds =
        {
            "equipped",
            "reserved_for_task",
            "in_task",
            "handed_to_order",
            "sold",
            "deleted"
        };

        public bool Supports(ConfigSourceSettings source)
        {
            return source != null && string.Equals(source.config_id, ConfigId, StringComparison.OrdinalIgnoreCase);
        }

        public ConfigPipelineReport ParseAndWrite(ConfigSourceSettings source)
        {
            var report = BuildRuntimeJson(source, out var runtimeJson);
            if (!report.Success)
                return report;

            if (!ConfigPipelineUtilities.TryValidateRuntimeOutputPath(source.runtime_json_path, out var fullPath, out var pathError))
            {
                report.ErrorMessage = pathError;
                return report;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                var tempPath = fullPath + ".tmp";
                File.WriteAllText(tempPath, runtimeJson, ConfigPipelineUtilities.Utf8NoBom);

                if (File.Exists(fullPath))
                    File.Replace(tempPath, fullPath, null);
                else
                    File.Move(tempPath, fullPath);

                AssetDatabase.ImportAsset(ConfigPaths.NormalizeProjectPath(source.runtime_json_path));
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                report.ErrorMessage = $"Could not write runtime JSON '{source.runtime_json_path}': {exception.Message}";
            }

            return report;
        }

        public ConfigPipelineReport Validate(ConfigSourceSettings source)
        {
            return BuildRuntimeJson(source, out _);
        }

        public ConfigPipelineReport BuildRuntimeJson(ConfigSourceSettings source, out string runtimeJson)
        {
            runtimeJson = null;
            var report = new ConfigPipelineReport();

            if (!ConfigPipelineUtilities.TryLoadDownload(source, report, out var download))
                return report;

            var context = new StorageConfigContext(download, report);
            context.ValidateSheetsAndColumns();
            context.ValidateForbiddenLegacyIds();
            context.CollectEnumValues();
            context.CollectIds();
            context.ValidateRows();

            if (!report.Success)
                return report;

            runtimeJson = ConfigRuntimeJsonWriter.Write(context.BuildRuntimeArrays());
            return report;
        }

        private sealed class StorageConfigContext
        {
            private readonly ConfigSheetDownload _download;
            private readonly ConfigPipelineReport _report;
            private readonly Dictionary<string, ConfigSheetTable> _tables = new Dictionary<string, ConfigSheetTable>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, HashSet<string>> _enumValues = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _storageRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _stateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public StorageConfigContext(ConfigSheetDownload download, ConfigPipelineReport report)
            {
                _download = download;
                _report = report;
                foreach (var sheet in download.sheets)
                {
                    if (sheet == null || string.IsNullOrWhiteSpace(sheet.sheet_name))
                        continue;

                    _tables[sheet.sheet_name] = new ConfigSheetTable(sheet);
                }
            }

            public void ValidateSheetsAndColumns()
            {
                foreach (var sheetName in RequiredSheets)
                {
                    if (!_tables.TryGetValue(sheetName, out var table))
                    {
                        AddIssue(sheetName, 0, string.Empty, string.Empty, "Required sheet is missing.");
                        continue;
                    }

                    if (table.Rows == 0)
                    {
                        AddIssue(sheetName, 1, string.Empty, string.Empty, "Required sheet has no header row.");
                        continue;
                    }

                    foreach (var column in RequiredColumns[sheetName])
                    {
                        if (!table.HasColumn(column))
                            AddIssue(sheetName, 1, column, string.Empty, "Required column is missing.");
                    }
                }
            }

            public void ValidateForbiddenLegacyIds()
            {
                foreach (var sheet in _download.sheets ?? Array.Empty<ConfigDownloadedSheet>())
                {
                    if (sheet == null ||
                        string.Equals(sheet.sheet_name, "README", StringComparison.OrdinalIgnoreCase) ||
                        sheet.rows == null)
                    {
                        continue;
                    }

                    for (var rowIndex = 0; rowIndex < sheet.rows.Length; rowIndex++)
                    {
                        var cells = sheet.rows[rowIndex]?.cells;
                        if (cells == null)
                            continue;

                        for (var columnIndex = 0; columnIndex < cells.Length; columnIndex++)
                        {
                            var value = (cells[columnIndex] ?? string.Empty).Trim();
                            if (string.Equals(value, "item_gold", StringComparison.OrdinalIgnoreCase))
                            {
                                AddIssue(sheet.sheet_name, rowIndex + 1, string.Empty, value, "item_gold is a forbidden legacy item id in Storage Configs.");
                            }
                        }
                    }
                }
            }

            public void CollectEnumValues()
            {
                if (!_tables.TryGetValue("Enums", out var table) ||
                    !table.HasColumn("enum_group") ||
                    !table.HasColumn("value"))
                {
                    return;
                }

                var enumValuePairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    var group = row.Get("enum_group");
                    var value = row.Get("value");
                    if (string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(value))
                        continue;

                    var pairKey = $"{group}\n{value}";
                    if (!enumValuePairs.Add(pairKey))
                    {
                        AddIssue("Enums", row.RowNumber, "value", value, $"Duplicate enum value in group '{group}'.");
                        continue;
                    }

                    if (!_enumValues.TryGetValue(group, out var values))
                    {
                        values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        _enumValues[group] = values;
                    }

                    values.Add(value);
                }
            }

            public void CollectIds()
            {
                CollectUniqueIds("StorageRules", "storage_rule_id", _storageRuleIds, "storage_rule_id");
                CollectUniqueIds("ItemStates", "state_id", _stateIds, "state_id");
                CollectUniqueBuildingKeys();
            }

            public void ValidateRows()
            {
                foreach (var sheetName in RequiredSheets)
                {
                    if (!_tables.TryGetValue(sheetName, out var table))
                        continue;

                    foreach (var row in table.DataRows)
                    {
                        ValidateRequiredValues(table, row);
                        ValidateTypedFields(table, row);
                        ValidateEnumReferences(table, row);
                        ValidateSheetRules(table, row);
                    }
                }

                ValidateItemStateSet();
            }

            public Dictionary<string, List<Dictionary<string, object>>> BuildRuntimeArrays()
            {
                var arrays = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.Ordinal);
                foreach (var sheetName in RequiredSheets)
                    arrays[RuntimeArrayNames[sheetName]] = new List<Dictionary<string, object>>();

                foreach (var sheetName in RequiredSheets)
                {
                    if (!_tables.TryGetValue(sheetName, out var table))
                        continue;

                    var runtimeName = RuntimeArrayNames[sheetName];
                    foreach (var row in table.DataRows)
                    {
                        var runtimeRow = new Dictionary<string, object>(StringComparer.Ordinal);
                        foreach (var column in RequiredColumns[sheetName])
                        {
                            if (RuntimeExcludedColumns.Contains(column))
                                continue;

                            var value = row.Get(column);
                            if (string.IsNullOrWhiteSpace(value))
                                continue;

                            runtimeRow[ConfigPipelineUtilities.ToCamelCase(column)] = ConvertRuntimeValue(sheetName, column, value);
                        }

                        if (runtimeRow.Count > 0)
                            arrays[runtimeName].Add(runtimeRow);
                    }
                }

                return arrays;
            }

            private void CollectUniqueIds(string sheetName, string column, HashSet<string> ids, string displayName)
            {
                if (!_tables.TryGetValue(sheetName, out var table) || !table.HasColumn(column))
                    return;

                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    var id = row.Get(column);
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        AddIssue(sheetName, row.RowNumber, column, id, $"{displayName} is required.");
                        continue;
                    }

                    if (seen.TryGetValue(id, out var firstRow))
                    {
                        AddIssue(sheetName, row.RowNumber, column, id, $"Duplicate {displayName}; first declared at row {firstRow}.");
                        continue;
                    }

                    seen[id] = row.RowNumber;
                    ids.Add(id);
                }
            }

            private void CollectUniqueBuildingKeys()
            {
                if (!_tables.TryGetValue("StorageBuildings", out var table) ||
                    !table.HasColumn("building_id") ||
                    !table.HasColumn("level"))
                {
                    return;
                }

                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    var buildingId = row.Get("building_id");
                    var level = row.Get("level");
                    if (string.IsNullOrWhiteSpace(buildingId) || string.IsNullOrWhiteSpace(level))
                        continue;

                    var key = $"{buildingId}\n{level}";
                    if (seen.TryGetValue(key, out var firstRow))
                    {
                        AddIssue("StorageBuildings", row.RowNumber, "level", level, $"Duplicate building_id + level pair; first declared at row {firstRow}.");
                        continue;
                    }

                    seen[key] = row.RowNumber;
                }
            }

            private void ValidateRequiredValues(ConfigSheetTable table, ConfigSheetDataRow row)
            {
                foreach (var column in RequiredColumns[table.Name])
                {
                    if (RuntimeExcludedColumns.Contains(column))
                        continue;

                    var value = row.Get(column);
                    if (string.IsNullOrWhiteSpace(value))
                        AddIssue(table.Name, row.RowNumber, column, value, "Required value is missing.");
                }
            }

            private void ValidateTypedFields(ConfigSheetTable table, ConfigSheetDataRow row)
            {
                foreach (var column in table.Headers)
                {
                    if (RuntimeExcludedColumns.Contains(column))
                        continue;

                    var value = row.Get(column);
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    if (IsIntegerField(table.Name, column) &&
                        !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    {
                        AddIssue(table.Name, row.RowNumber, column, value, "Expected an integer number.");
                    }

                    if (BoolColumns.Contains(column))
                        TryParseBool(row, column, out _);
                }
            }

            private void ValidateEnumReferences(ConfigSheetTable table, ConfigSheetDataRow row)
            {
                foreach (var column in table.Headers)
                {
                    if (!EnumColumns.TryGetValue(column, out var enumGroup))
                        continue;

                    var value = row.Get(column);
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    if (!_enumValues.TryGetValue(enumGroup, out var allowedValues))
                        continue;

                    if (!allowedValues.Contains(value))
                        AddIssue(table.Name, row.RowNumber, column, value, $"Value is not listed in Enums group '{enumGroup}'.");
                }
            }

            private void ValidateSheetRules(ConfigSheetTable table, ConfigSheetDataRow row)
            {
                if (string.Equals(table.Name, "StorageRules", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateStorageRule(row);
                    return;
                }

                if (string.Equals(table.Name, "StorageBuildings", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateStorageBuilding(row);
                    return;
                }

                if (string.Equals(table.Name, "ItemStates", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateItemState(row);
                    return;
                }

                if (string.Equals(table.Name, "Enums", StringComparison.OrdinalIgnoreCase))
                    ValidateEnumRow(row);
            }

            private void ValidateStorageRule(ConfigSheetDataRow row)
            {
                var itemKind = row.Get("item_kind");
                var mode = row.Get("mode");

                var hasMaxStack = long.TryParse(row.Get("max_stack"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxStack);
                if (hasMaxStack && maxStack <= 0)
                    AddIssue("StorageRules", row.RowNumber, "max_stack", row.Get("max_stack"), "max_stack must be greater than 0.");

                var hasAllowQuality = TryParseBool(row, "allow_quality", out var allowQuality);
                var hasAllowInstanceId = TryParseBool(row, "allow_instance_id", out var allowInstanceId);

                if (string.Equals(mode, "stack", StringComparison.OrdinalIgnoreCase))
                {
                    if (hasMaxStack && maxStack <= 1)
                        AddIssue("StorageRules", row.RowNumber, "max_stack", row.Get("max_stack"), "stack mode requires max_stack greater than 1.");

                    if (hasAllowInstanceId && allowInstanceId)
                        AddIssue("StorageRules", row.RowNumber, "allow_instance_id", row.Get("allow_instance_id"), "stack mode requires allow_instance_id to be FALSE.");
                }

                if (string.Equals(mode, "single", StringComparison.OrdinalIgnoreCase) &&
                    hasMaxStack &&
                    maxStack != 1)
                {
                    AddIssue("StorageRules", row.RowNumber, "max_stack", row.Get("max_stack"), "single mode requires max_stack to be 1.");
                }

                if (string.Equals(itemKind, "equipment", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(mode, "single", StringComparison.OrdinalIgnoreCase))
                        AddIssue("StorageRules", row.RowNumber, "mode", mode, "equipment storage requires mode to be single.");

                    if (hasMaxStack && maxStack != 1)
                        AddIssue("StorageRules", row.RowNumber, "max_stack", row.Get("max_stack"), "equipment storage requires max_stack to be 1.");

                    if (hasAllowQuality && !allowQuality)
                        AddIssue("StorageRules", row.RowNumber, "allow_quality", row.Get("allow_quality"), "equipment storage requires allow_quality to be TRUE.");

                    if (hasAllowInstanceId && !allowInstanceId)
                        AddIssue("StorageRules", row.RowNumber, "allow_instance_id", row.Get("allow_instance_id"), "equipment storage requires allow_instance_id to be TRUE.");
                }
            }

            private void ValidateStorageBuilding(ConfigSheetDataRow row)
            {
                ValidateIntegerMinimum(row, "level", 0, "level must be greater than or equal to 0.");
                ValidateIntegerMinimum(row, "slot_count", 1, "slot_count must be greater than 0.");
                ValidateIntegerMinimum(row, "resource_stack_bonus", 0, "resource_stack_bonus must be greater than or equal to 0.");
            }

            private void ValidateItemState(ConfigSheetDataRow row)
            {
                var stateId = row.Get("state_id");
                if (string.IsNullOrWhiteSpace(stateId))
                    return;

                if (string.Equals(stateId, "on_storage", StringComparison.OrdinalIgnoreCase))
                    ValidateOnStorageState(row);

                foreach (var busyStateId in BusyStateIds)
                {
                    if (string.Equals(stateId, busyStateId, StringComparison.OrdinalIgnoreCase))
                    {
                        ValidateUnavailableState(row);
                        return;
                    }
                }
            }

            private void ValidateEnumRow(ConfigSheetDataRow row)
            {
                if (string.IsNullOrWhiteSpace(row.Get("enum_group")))
                    AddIssue("Enums", row.RowNumber, "enum_group", row.Get("enum_group"), "Enum group is required.");

                if (string.IsNullOrWhiteSpace(row.Get("value")))
                    AddIssue("Enums", row.RowNumber, "value", row.Get("value"), "Enum value is required.");

                if (string.IsNullOrWhiteSpace(row.Get("description")))
                    AddIssue("Enums", row.RowNumber, "description", row.Get("description"), "Enum description is required.");
            }

            private void ValidateItemStateSet()
            {
                if (!_stateIds.Contains("on_storage"))
                    AddIssue("ItemStates", 0, "state_id", "on_storage", "Required state_id 'on_storage' is missing.");
            }

            private void ValidateOnStorageState(ConfigSheetDataRow row)
            {
                ValidateExpectedBool(row, "available_for_craft", true, "on_storage must be available for craft.");
                ValidateExpectedBool(row, "available_for_sale", true, "on_storage must be available for sale.");
                ValidateExpectedBool(row, "available_for_order", true, "on_storage must be available for order.");
                ValidateExpectedBool(row, "available_for_equip", true, "on_storage must be available for equip.");
            }

            private void ValidateUnavailableState(ConfigSheetDataRow row)
            {
                ValidateExpectedBool(row, "available_for_craft", false, "Busy or terminal states must not be available for craft.");
                ValidateExpectedBool(row, "available_for_sale", false, "Busy or terminal states must not be available for sale.");
                ValidateExpectedBool(row, "available_for_order", false, "Busy or terminal states must not be available for order.");
                ValidateExpectedBool(row, "available_for_equip", false, "Busy or terminal states must not be available for equip.");
            }

            private void ValidateExpectedBool(ConfigSheetDataRow row, string column, bool expected, string message)
            {
                if (!TryParseBool(row, column, out var actual))
                    return;

                if (actual != expected)
                    AddIssue(row.Table.Name, row.RowNumber, column, row.Get(column), message);
            }

            private void ValidateIntegerMinimum(ConfigSheetDataRow row, string column, long minimum, string message)
            {
                var value = row.Get(column);
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    return;

                if (parsed < minimum)
                    AddIssue(row.Table.Name, row.RowNumber, column, value, message);
            }

            private bool TryParseBool(ConfigSheetDataRow row, string column, out bool value)
            {
                value = false;
                var raw = row.Get(column);
                if (string.IsNullOrWhiteSpace(raw))
                    return false;

                if (string.Equals(raw, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase))
                {
                    value = true;
                    return true;
                }

                if (string.Equals(raw, "FALSE", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(raw, "0", StringComparison.OrdinalIgnoreCase))
                {
                    value = false;
                    return true;
                }

                AddIssue(row.Table.Name, row.RowNumber, column, raw, "Expected TRUE or FALSE.");
                return false;
            }

            private void AddIssue(string sheet, int row, string column, string value, string message)
            {
                _report.Issues.Add(new ConfigValidationIssue(sheet, row, column, value, message));
            }
        }

        private static object ConvertRuntimeValue(string sheetName, string column, string value)
        {
            if (BoolColumns.Contains(column))
                return string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);

            if (IsIntegerField(sheetName, column) &&
                long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            {
                return integer;
            }

            return value;
        }

        private static bool IsIntegerField(string sheetName, string column)
        {
            return IntegerFields.Contains(FieldKey(sheetName, column));
        }

        private static string FieldKey(string sheetName, string column)
        {
            return ConfigPipelineUtilities.FieldKey(sheetName, column);
        }
    }
}
