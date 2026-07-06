using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class LootConfigsParser : IConfigPipelineParser
    {
        private const string ConfigId = "loot_configs";
        private const string ForbiddenLegacyItemId = "item_gold";
        private const string GoldCurrencyId = "gold_id";
        private const string GoldDropType = "Gold";

        private static readonly string[] RequiredSheets =
        {
            "LootTables",
            "LootTableEntries",
            "LootGroups",
            "EnumValues"
        };

        private static readonly Dictionary<string, string[]> RequiredColumns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["LootTables"] = new[] { "loot_table_id", "table_type", "roll_mode", "roll_count_min", "roll_count_max", "enabled", "notes" },
            ["LootTableEntries"] = new[] { "loot_table_id", "entry_id", "drop_type", "target_id", "weight", "min", "max", "chance", "rarity_hint", "required_roll_group", "notes" },
            ["LootGroups"] = new[] { "loot_table_id", "roll_group", "roll_mode", "roll_count_min", "roll_count_max", "chance", "notes" },
            ["EnumValues"] = new[] { "enum_group", "value", "description" }
        };

        private static readonly Dictionary<string, string[]> RuntimeColumns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["LootTables"] = new[] { "loot_table_id", "table_type", "roll_mode", "roll_count_min", "roll_count_max", "enabled" },
            ["LootTableEntries"] = new[] { "loot_table_id", "entry_id", "drop_type", "target_id", "weight", "min", "max", "chance", "rarity_hint", "required_roll_group" },
            ["LootGroups"] = new[] { "loot_table_id", "roll_group", "roll_mode", "roll_count_min", "roll_count_max", "chance" },
            ["EnumValues"] = new[] { "enum_group", "value", "description" }
        };

        private static readonly Dictionary<string, string> RuntimeArrayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["LootTables"] = "lootTables",
            ["LootTableEntries"] = "lootTableEntries",
            ["LootGroups"] = "lootGroups",
            ["EnumValues"] = "enumValues"
        };

        private static readonly HashSet<string> NumberFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FieldKey("LootTables", "roll_count_min"),
            FieldKey("LootTables", "roll_count_max"),
            FieldKey("LootTableEntries", "weight"),
            FieldKey("LootTableEntries", "min"),
            FieldKey("LootTableEntries", "max"),
            FieldKey("LootTableEntries", "chance"),
            FieldKey("LootGroups", "roll_count_min"),
            FieldKey("LootGroups", "roll_count_max"),
            FieldKey("LootGroups", "chance")
        };

        private static readonly HashSet<string> BoolFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FieldKey("LootTables", "enabled")
        };

        private static readonly Dictionary<string, string> EnumFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [FieldKey("LootTables", "table_type")] = "LootTableType",
            [FieldKey("LootTables", "roll_mode")] = "RollMode",
            [FieldKey("LootTableEntries", "drop_type")] = "DropType",
            [FieldKey("LootTableEntries", "rarity_hint")] = "RarityId",
            [FieldKey("LootGroups", "roll_mode")] = "RollMode"
        };

        private static readonly HashSet<string> OptionalValueFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FieldKey("LootTableEntries", "rarity_hint"),
            FieldKey("LootTables", "notes"),
            FieldKey("LootTableEntries", "notes"),
            FieldKey("LootGroups", "notes")
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

            var context = new LootConfigContext(download, report);
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

        private sealed class LootConfigContext
        {
            private readonly ConfigSheetDownload _download;
            private readonly ConfigPipelineReport _report;
            private readonly Dictionary<string, ConfigSheetTable> _tables = new Dictionary<string, ConfigSheetTable>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, HashSet<string>> _enumValues = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _lootTableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _entryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _lootGroupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public LootConfigContext(ConfigSheetDownload download, ConfigPipelineReport report)
            {
                _download = download;
                _report = report;

                foreach (var sheet in download.sheets ?? Array.Empty<ConfigDownloadedSheet>())
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
                    if (sheet?.rows == null)
                        continue;

                    for (var rowIndex = 0; rowIndex < sheet.rows.Length; rowIndex++)
                    {
                        var cells = sheet.rows[rowIndex]?.cells;
                        if (cells == null)
                            continue;

                        for (var columnIndex = 0; columnIndex < cells.Length; columnIndex++)
                        {
                            var value = (cells[columnIndex] ?? string.Empty).Trim();
                            if (string.Equals(value, ForbiddenLegacyItemId, StringComparison.OrdinalIgnoreCase))
                                AddIssue(sheet.sheet_name, rowIndex + 1, string.Empty, value, "item_gold is a forbidden legacy id in Loot Configs.");
                        }
                    }
                }
            }

            public void CollectEnumValues()
            {
                if (!_tables.TryGetValue("EnumValues", out var table) ||
                    !table.HasColumn("enum_group") ||
                    !table.HasColumn("value"))
                {
                    return;
                }

                var enumValuePairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    var enumGroup = row.Get("enum_group");
                    var value = row.Get("value");
                    if (string.IsNullOrWhiteSpace(enumGroup) || string.IsNullOrWhiteSpace(value))
                        continue;

                    var key = $"{enumGroup}\n{value}";
                    if (!enumValuePairs.Add(key))
                    {
                        AddIssue("EnumValues", row.RowNumber, "value", value, $"Duplicate enum value in group '{enumGroup}'.");
                        continue;
                    }

                    if (!_enumValues.TryGetValue(enumGroup, out var values))
                    {
                        values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        _enumValues[enumGroup] = values;
                    }

                    values.Add(value);
                }
            }

            public void CollectIds()
            {
                CollectUniqueIds("LootTables", "loot_table_id", _lootTableIds, "loot_table_id");
                CollectUniqueIds("LootTableEntries", "entry_id", _entryIds, "entry_id");
                CollectLootGroupKeys();
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
                        foreach (var column in RuntimeColumns[sheetName])
                            runtimeRow[ConfigPipelineUtilities.ToCamelCase(column)] = ConvertRuntimeValue(sheetName, column, row.Get(column));

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
                        AddIssue(sheetName, row.RowNumber, column, id, $"Duplicate {displayName}; first declared at row {firstRow}.");
                    else
                        seen[id] = row.RowNumber;

                    ids.Add(id);
                }
            }

            private void CollectLootGroupKeys()
            {
                if (!_tables.TryGetValue("LootGroups", out var table) ||
                    !table.HasColumn("loot_table_id") ||
                    !table.HasColumn("roll_group"))
                {
                    return;
                }

                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    var lootTableId = row.Get("loot_table_id");
                    var rollGroup = row.Get("roll_group");
                    if (string.IsNullOrWhiteSpace(lootTableId) || string.IsNullOrWhiteSpace(rollGroup))
                        continue;

                    var key = LootGroupKey(lootTableId, rollGroup);
                    if (seen.TryGetValue(key, out var firstRow))
                        AddIssue("LootGroups", row.RowNumber, "roll_group", rollGroup, $"Duplicate loot_table_id + roll_group; first declared at row {firstRow}.");
                    else
                        seen[key] = row.RowNumber;

                    _lootGroupKeys.Add(key);
                }
            }

            private void ValidateRequiredValues(ConfigSheetTable table, ConfigSheetDataRow row)
            {
                foreach (var column in RequiredColumns[table.Name])
                {
                    if (OptionalValueFields.Contains(FieldKey(table.Name, column)))
                        continue;

                    var value = row.Get(column);
                    if (string.IsNullOrWhiteSpace(value))
                        AddIssue(table.Name, row.RowNumber, column, value, "Required value is missing.");
                }
            }

            private void ValidateTypedFields(ConfigSheetTable table, ConfigSheetDataRow row)
            {
                foreach (var column in RuntimeColumns[table.Name])
                {
                    var value = row.Get(column);
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    if (IsNumberField(table.Name, column) && !ConfigPipelineUtilities.TryParseNumber(value, out _))
                        AddIssue(table.Name, row.RowNumber, column, value, "Expected a number.");

                    if (IsBoolField(table.Name, column))
                        TryParseBool(row, column, required: true, out _);
                }
            }

            private void ValidateEnumReferences(ConfigSheetTable table, ConfigSheetDataRow row)
            {
                foreach (var column in RuntimeColumns[table.Name])
                {
                    if (!EnumFields.TryGetValue(FieldKey(table.Name, column), out var enumGroup))
                        continue;

                    var value = row.Get(column);
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    if (!_enumValues.TryGetValue(enumGroup, out var allowedValues) || !allowedValues.Contains(value))
                        AddIssue(table.Name, row.RowNumber, column, value, $"Value is not listed in EnumValues group '{enumGroup}'.");
                }
            }

            private void ValidateSheetRules(ConfigSheetTable table, ConfigSheetDataRow row)
            {
                if (string.Equals(table.Name, "LootTables", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateLootTable(row);
                    return;
                }

                if (string.Equals(table.Name, "LootTableEntries", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateLootTableEntry(row);
                    return;
                }

                if (string.Equals(table.Name, "LootGroups", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateLootGroup(row);
                    return;
                }

                if (string.Equals(table.Name, "EnumValues", StringComparison.OrdinalIgnoreCase))
                    ValidateEnumRow(row);
            }

            private void ValidateLootTable(ConfigSheetDataRow row)
            {
                ValidateNumberGreaterThanOrEqual(row, "roll_count_min", 0);
                ValidateNumberGreaterThanOrEqual(row, "roll_count_max", 0);
                ValidateLessOrEqual(row, "roll_count_min", "roll_count_max");
            }

            private void ValidateLootTableEntry(ConfigSheetDataRow row)
            {
                var lootTableId = row.Get("loot_table_id");
                if (!string.IsNullOrWhiteSpace(lootTableId) && !_lootTableIds.Contains(lootTableId))
                    AddIssue("LootTableEntries", row.RowNumber, "loot_table_id", lootTableId, "Referenced loot_table_id does not exist in LootTables.loot_table_id.");

                var requiredRollGroup = row.Get("required_roll_group");
                if (!string.IsNullOrWhiteSpace(lootTableId) &&
                    !string.IsNullOrWhiteSpace(requiredRollGroup) &&
                    !_lootGroupKeys.Contains(LootGroupKey(lootTableId, requiredRollGroup)))
                {
                    AddIssue("LootTableEntries", row.RowNumber, "required_roll_group", requiredRollGroup, "Referenced required_roll_group does not exist in LootGroups.roll_group for this loot_table_id.");
                }

                ValidateNumberGreaterThanOrEqual(row, "weight", 0);
                ValidateNumberGreaterThanOrEqual(row, "min", 0);
                ValidateNumberGreaterThanOrEqual(row, "max", 0);
                ValidateLessOrEqual(row, "min", "max");
                ValidatePercent(row, "chance");
                ValidateGoldTarget(row);
            }

            private void ValidateLootGroup(ConfigSheetDataRow row)
            {
                var lootTableId = row.Get("loot_table_id");
                if (!string.IsNullOrWhiteSpace(lootTableId) && !_lootTableIds.Contains(lootTableId))
                    AddIssue("LootGroups", row.RowNumber, "loot_table_id", lootTableId, "Referenced loot_table_id does not exist in LootTables.loot_table_id.");

                ValidateNumberGreaterThanOrEqual(row, "roll_count_min", 0);
                ValidateNumberGreaterThanOrEqual(row, "roll_count_max", 0);
                ValidateLessOrEqual(row, "roll_count_min", "roll_count_max");
                ValidatePercent(row, "chance");
            }

            private void ValidateEnumRow(ConfigSheetDataRow row)
            {
                if (string.IsNullOrWhiteSpace(row.Get("enum_group")))
                    AddIssue("EnumValues", row.RowNumber, "enum_group", row.Get("enum_group"), "Enum group is required.");

                if (string.IsNullOrWhiteSpace(row.Get("value")))
                    AddIssue("EnumValues", row.RowNumber, "value", row.Get("value"), "Enum value is required.");

                if (string.IsNullOrWhiteSpace(row.Get("description")))
                    AddIssue("EnumValues", row.RowNumber, "description", row.Get("description"), "Enum description is required.");
            }

            private void ValidateGoldTarget(ConfigSheetDataRow row)
            {
                var targetId = row.Get("target_id");
                if (!string.Equals(targetId, GoldCurrencyId, StringComparison.OrdinalIgnoreCase))
                    return;

                var dropType = row.Get("drop_type");
                if (!string.Equals(dropType, GoldDropType, StringComparison.OrdinalIgnoreCase))
                    AddIssue("LootTableEntries", row.RowNumber, "target_id", targetId, "gold_id is a currency_id and is allowed only when drop_type = Gold.");
            }

            private void ValidateNumberGreaterThanOrEqual(ConfigSheetDataRow row, string column, double minimum)
            {
                var raw = row.Get(column);
                if (!ConfigPipelineUtilities.TryParseNumber(raw, out var value))
                    return;

                if (value < minimum)
                    AddIssue(row.Table.Name, row.RowNumber, column, raw, $"{column} must be greater than or equal to {minimum.ToString(CultureInfo.InvariantCulture)}.");
            }

            private void ValidateLessOrEqual(ConfigSheetDataRow row, string minColumn, string maxColumn)
            {
                if (!ConfigPipelineUtilities.TryParseNumber(row.Get(minColumn), out var min) ||
                    !ConfigPipelineUtilities.TryParseNumber(row.Get(maxColumn), out var max))
                {
                    return;
                }

                if (min > max)
                    AddIssue(row.Table.Name, row.RowNumber, maxColumn, row.Get(maxColumn), $"{minColumn} must be <= {maxColumn}.");
            }

            private void ValidatePercent(ConfigSheetDataRow row, string column)
            {
                if (!ConfigPipelineUtilities.TryParseNumber(row.Get(column), out var value))
                    return;

                if (value < 0 || value > 100)
                    AddIssue(row.Table.Name, row.RowNumber, column, row.Get(column), $"{column} must be in range 0..100.");
            }

            private bool TryParseBool(ConfigSheetDataRow row, string column, bool required, out bool value)
            {
                value = false;
                var raw = row.Get(column);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    if (required)
                        AddIssue(row.Table.Name, row.RowNumber, column, raw, "Boolean value is required.");

                    return false;
                }

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

            private static string LootGroupKey(string lootTableId, string rollGroup)
            {
                return $"{lootTableId}\n{rollGroup}";
            }

            private void AddIssue(string sheet, int row, string column, string value, string message)
            {
                _report.Issues.Add(new ConfigValidationIssue(sheet, row, column, value, message));
            }
        }

        private static object ConvertRuntimeValue(string sheetName, string column, string value)
        {
            if (IsBoolField(sheetName, column))
                return string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);

            if (IsNumberField(sheetName, column) && ConfigPipelineUtilities.TryParseNumber(value, out var number))
                return number;

            return value ?? string.Empty;
        }

        private static bool IsNumberField(string sheetName, string column)
        {
            return NumberFields.Contains(FieldKey(sheetName, column));
        }

        private static bool IsBoolField(string sheetName, string column)
        {
            return BoolFields.Contains(FieldKey(sheetName, column));
        }

        private static string FieldKey(string sheetName, string column)
        {
            return ConfigPipelineUtilities.FieldKey(sheetName, column);
        }
    }
}
