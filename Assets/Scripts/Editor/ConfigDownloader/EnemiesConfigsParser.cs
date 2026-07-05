using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class EnemiesConfigsParser : IConfigPipelineParser
    {
        private const string ConfigId = "enemies_configs";

        private static readonly string[] RequiredSheets =
        {
            "Enemies",
            "EnemyLevels",
            "EnemyLoot",
            "EnemyAbilities",
            "CombatStatuses",
            "EnemyGroups",
            "Enums"
        };

        private static readonly Dictionary<string, string[]> RequiredColumns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Enemies"] = new[]
            {
                "enemy_id", "name_id", "description_id", "icon_id", "battle_image_id", "enemy_type",
                "combat_exp", "hp", "damage_min", "damage_max", "attack_speed", "attack_range", "damage_type",
                "crit_chance_percent", "physical_resist_percent", "magic_resist_percent", "dodge_chance_percent",
                "combat_ability_ids", "loot_group_id", "notes"
            },
            ["EnemyLevels"] = new[] { "level", "hp_multiplier", "damage_multiplier", "combat_exp_multiplier", "loot_quantity_multiplier", "attack_speed_multiplier", "notes" },
            ["EnemyLoot"] = new[] { "loot_group_id", "enemy_id", "loot_id", "min_count", "max_count", "chance_percent", "quality_min", "quality_max", "notes" },
            ["EnemyAbilities"] = new[] { "ability_id", "name_id", "trigger", "conditions", "chance_percent", "effects", "target", "cooldown_sec", "notes" },
            ["CombatStatuses"] = new[] { "status_id", "name_id", "type", "duration_sec", "tick_interval_sec", "max_stacks", "effect_type", "damage_type", "damage_value", "stat_id", "stat_modifier_value", "notes" },
            ["EnemyGroups"] = new[] { "enemy_group_id", "enemy_ref", "weight", "min_count", "max_count", "notes" },
            ["Enums"] = new[] { "enum_group", "value", "description" }
        };

        private static readonly Dictionary<string, string> RuntimeArrayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Enemies"] = "enemies",
            ["EnemyLevels"] = "enemyLevels",
            ["EnemyLoot"] = "enemyLoot",
            ["EnemyAbilities"] = "enemyAbilities",
            ["CombatStatuses"] = "combatStatuses",
            ["EnemyGroups"] = "enemyGroups",
            ["Enums"] = "enumValues"
        };

        private static readonly HashSet<string> RuntimeExcludedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "notes"
        };

        private static readonly HashSet<string> IntegerFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FieldKey("Enemies", "combat_exp"),
            FieldKey("Enemies", "hp"),
            FieldKey("Enemies", "damage_min"),
            FieldKey("Enemies", "damage_max"),
            FieldKey("EnemyLevels", "level"),
            FieldKey("EnemyLoot", "min_count"),
            FieldKey("EnemyLoot", "max_count"),
            FieldKey("EnemyLoot", "quality_min"),
            FieldKey("EnemyLoot", "quality_max"),
            FieldKey("CombatStatuses", "max_stacks"),
            FieldKey("EnemyGroups", "min_count"),
            FieldKey("EnemyGroups", "max_count")
        };

        private static readonly HashSet<string> NumberFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FieldKey("Enemies", "attack_speed"),
            FieldKey("Enemies", "crit_chance_percent"),
            FieldKey("Enemies", "physical_resist_percent"),
            FieldKey("Enemies", "magic_resist_percent"),
            FieldKey("Enemies", "dodge_chance_percent"),
            FieldKey("EnemyLevels", "hp_multiplier"),
            FieldKey("EnemyLevels", "damage_multiplier"),
            FieldKey("EnemyLevels", "combat_exp_multiplier"),
            FieldKey("EnemyLevels", "loot_quantity_multiplier"),
            FieldKey("EnemyLevels", "attack_speed_multiplier"),
            FieldKey("EnemyLoot", "chance_percent"),
            FieldKey("EnemyAbilities", "chance_percent"),
            FieldKey("EnemyAbilities", "cooldown_sec"),
            FieldKey("CombatStatuses", "duration_sec"),
            FieldKey("CombatStatuses", "tick_interval_sec"),
            FieldKey("CombatStatuses", "damage_value"),
            FieldKey("CombatStatuses", "stat_modifier_value"),
            FieldKey("EnemyGroups", "weight")
        };

        private static readonly Dictionary<string, string> EnumColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["enemy_type"] = "enemy_type",
            ["attack_range"] = "attack_range",
            ["damage_type"] = "damage_type"
        };

        private static readonly HashSet<string> OptionalValueFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FieldKey("Enemies", "combat_ability_ids"),
            FieldKey("Enemies", "loot_group_id"),
            FieldKey("EnemyAbilities", "conditions"),
            FieldKey("CombatStatuses", "stat_id"),
            FieldKey("CombatStatuses", "stat_modifier_value"),
            FieldKey("Enums", "description")
        };

        private static readonly Regex ApplyStatusRegex = new Regex(
            @"ApplyStatus\s*:\s*([A-Za-z0-9_.-]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
                File.WriteAllText(tempPath, runtimeJson, Encoding.UTF8);

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

            var context = new EnemiesConfigContext(download, report);
            context.ValidateSheetsAndColumns();
            context.CollectEnumValues();
            context.CollectIds();
            context.ValidateRows();

            if (!report.Success)
                return report;

            runtimeJson = ConfigRuntimeJsonWriter.Write(context.BuildRuntimeArrays());
            return report;
        }

        private sealed class EnemiesConfigContext
        {
            private readonly ConfigPipelineReport _report;
            private readonly Dictionary<string, ConfigSheetTable> _tables = new Dictionary<string, ConfigSheetTable>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, HashSet<string>> _enumValues = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _enemyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _abilityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _statusIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _lootGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _enemyGroupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public EnemiesConfigContext(ConfigSheetDownload download, ConfigPipelineReport report)
            {
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

            public void CollectEnumValues()
            {
                if (!_tables.TryGetValue("Enums", out var table) ||
                    !table.HasColumn("enum_group") ||
                    !table.HasColumn("value"))
                {
                    return;
                }

                foreach (var row in table.DataRows)
                {
                    var group = row.Get("enum_group");
                    var value = row.Get("value");
                    if (string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(value))
                        continue;

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
                CollectUniqueIds("Enemies", "enemy_id", _enemyIds, "enemy id");
                CollectUniqueIds("EnemyAbilities", "ability_id", _abilityIds, "ability id");
                CollectUniqueIds("CombatStatuses", "status_id", _statusIds, "status id");
                CollectUniqueIds("EnemyGroups", "enemy_group_id", _enemyGroupIds, "enemy_group_id");

                if (_tables.TryGetValue("EnemyLoot", out var lootTable) && lootTable.HasColumn("loot_group_id"))
                {
                    foreach (var row in lootTable.DataRows)
                    {
                        var lootGroupId = row.Get("loot_group_id");
                        if (!string.IsNullOrWhiteSpace(lootGroupId))
                            _lootGroupIds.Add(lootGroupId);
                    }
                }
            }

            public void ValidateRows()
            {
                var enumValuePairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var sheetName in RequiredSheets)
                {
                    if (!_tables.TryGetValue(sheetName, out var table))
                        continue;

                    foreach (var row in table.DataRows)
                    {
                        if (string.Equals(table.Name, "Enums", StringComparison.OrdinalIgnoreCase))
                            ValidateEnumRow(row, enumValuePairs);

                        ValidateRequiredValues(table, row);
                        ValidateTypedFields(table, row);
                        ValidateReferences(table, row);
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
                        foreach (var column in table.Headers)
                        {
                            if (RuntimeExcludedColumns.Contains(column))
                                continue;

                            var value = row.Get(column);
                            if (IsCombatAbilityIdsColumn(sheetName, column))
                            {
                                runtimeRow["combatAbilityIds"] = SplitIdsToList(value);
                                continue;
                            }

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

            private void ValidateRequiredValues(ConfigSheetTable table, ConfigSheetDataRow row)
            {
                foreach (var column in RequiredColumns[table.Name])
                {
                    if (RuntimeExcludedColumns.Contains(column) || OptionalValueFields.Contains(FieldKey(table.Name, column)))
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

                    if (IsIntegerField(table.Name, column) && !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        AddIssue(table.Name, row.RowNumber, column, value, "Expected an integer number.");

                    if (IsNumberField(table.Name, column) && !ConfigPipelineUtilities.TryParseNumber(value, out _))
                        AddIssue(table.Name, row.RowNumber, column, value, "Expected a number.");

                    ValidateEnum(table.Name, row, column, value);
                }
            }

            private void ValidateEnumRow(ConfigSheetDataRow row, HashSet<string> enumValuePairs)
            {
                var enumGroup = row.Get("enum_group");
                var value = row.Get("value");

                if (string.IsNullOrWhiteSpace(enumGroup))
                    AddIssue("Enums", row.RowNumber, "enum_group", enumGroup, "Enum group is required.");

                if (string.IsNullOrWhiteSpace(value))
                    AddIssue("Enums", row.RowNumber, "value", value, "Enum value is required.");

                if (string.IsNullOrWhiteSpace(enumGroup) || string.IsNullOrWhiteSpace(value))
                    return;

                var key = $"{enumGroup}\n{value}";
                if (!enumValuePairs.Add(key))
                    AddIssue("Enums", row.RowNumber, "value", value, $"Duplicate enum value in group '{enumGroup}'.");
            }

            private void ValidateReferences(ConfigSheetTable table, ConfigSheetDataRow row)
            {
                if (string.Equals(table.Name, "Enemies", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateEnemyRow(row);
                    return;
                }

                if (string.Equals(table.Name, "EnemyLoot", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateEnemyLootRow(row);
                    return;
                }

                if (string.Equals(table.Name, "EnemyAbilities", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateEnemyAbilityRow(row);
                    return;
                }

                if (string.Equals(table.Name, "EnemyGroups", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateEnemyGroupRow(row);
                }
            }

            private void ValidateEnemyRow(ConfigSheetDataRow row)
            {
                foreach (var abilityId in SplitIdsToList(row.Get("combat_ability_ids")))
                {
                    if (!_abilityIds.Contains(abilityId))
                        AddIssue("Enemies", row.RowNumber, "combat_ability_ids", abilityId, "Referenced ability_id does not exist in EnemyAbilities.ability_id.");
                }

                var lootGroupId = row.Get("loot_group_id");
                if (!string.IsNullOrWhiteSpace(lootGroupId) && !_lootGroupIds.Contains(lootGroupId))
                    AddIssue("Enemies", row.RowNumber, "loot_group_id", lootGroupId, "Referenced loot_group_id does not exist in EnemyLoot.loot_group_id.");

                ValidateLessOrEqual(row, "damage_min", "damage_max");
            }

            private void ValidateEnemyLootRow(ConfigSheetDataRow row)
            {
                var enemyId = row.Get("enemy_id");
                if (!string.IsNullOrWhiteSpace(enemyId) && !_enemyIds.Contains(enemyId))
                    AddIssue("EnemyLoot", row.RowNumber, "enemy_id", enemyId, "Referenced enemy_id does not exist in Enemies.enemy_id.");

                ValidateLessOrEqual(row, "min_count", "max_count");
                ValidatePercent(row, "chance_percent");
            }

            private void ValidateEnemyAbilityRow(ConfigSheetDataRow row)
            {
                ValidatePercent(row, "chance_percent");

                foreach (Match match in ApplyStatusRegex.Matches(row.Get("effects")))
                {
                    var statusId = match.Groups[1].Value;
                    if (!_statusIds.Contains(statusId))
                        AddIssue("EnemyAbilities", row.RowNumber, "effects", statusId, "ApplyStatus references missing CombatStatuses.status_id.");
                }
            }

            private void ValidateEnemyGroupRow(ConfigSheetDataRow row)
            {
                ValidateEnemyRef(row);
                ValidateLessOrEqual(row, "min_count", "max_count");

                var weight = row.Get("weight");
                if (ConfigPipelineUtilities.TryParseNumber(weight, out var parsedWeight) && parsedWeight <= 0)
                    AddIssue("EnemyGroups", row.RowNumber, "weight", weight, "Weight must be greater than 0.");
            }

            private void ValidateEnemyRef(ConfigSheetDataRow row)
            {
                var enemyRef = row.Get("enemy_ref");
                if (string.IsNullOrWhiteSpace(enemyRef))
                    return;

                var parts = enemyRef.Split(':');
                if (parts.Length != 2 ||
                    string.IsNullOrWhiteSpace(parts[0]) ||
                    string.IsNullOrWhiteSpace(parts[1]))
                {
                    AddIssue("EnemyGroups", row.RowNumber, "enemy_ref", enemyRef, "Expected enemy_ref format enemy_id:level.");
                    return;
                }

                var enemyId = parts[0].Trim();
                var levelText = parts[1].Trim();
                if (!_enemyIds.Contains(enemyId))
                    AddIssue("EnemyGroups", row.RowNumber, "enemy_ref", enemyRef, "Referenced enemy_id does not exist in Enemies.enemy_id.");

                if (!long.TryParse(levelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) || level <= 0)
                    AddIssue("EnemyGroups", row.RowNumber, "enemy_ref", enemyRef, "Level in enemy_ref must be an integer greater than 0.");
            }

            private void ValidateLessOrEqual(ConfigSheetDataRow row, string minColumn, string maxColumn)
            {
                var minText = row.Get(minColumn);
                var maxText = row.Get(maxColumn);
                if (!ConfigPipelineUtilities.TryParseNumber(minText, out var min) ||
                    !ConfigPipelineUtilities.TryParseNumber(maxText, out var max))
                {
                    return;
                }

                if (min > max)
                    AddIssue(row.Table.Name, row.RowNumber, maxColumn, maxText, $"{minColumn} must be <= {maxColumn}.");
            }

            private void ValidatePercent(ConfigSheetDataRow row, string column)
            {
                var value = row.Get(column);
                if (!ConfigPipelineUtilities.TryParseNumber(value, out var percent))
                    return;

                if (percent < 0 || percent > 100)
                    AddIssue(row.Table.Name, row.RowNumber, column, value, "Percent value must be in range 0..100.");
            }

            private void ValidateEnum(string sheetName, ConfigSheetDataRow row, string column, string value)
            {
                if (!EnumColumns.TryGetValue(column, out var enumGroup))
                    return;

                if (!_enumValues.TryGetValue(enumGroup, out var allowedValues))
                    return;

                if (!allowedValues.Contains(value))
                    AddIssue(sheetName, row.RowNumber, column, value, $"Value is not listed in Enums group '{enumGroup}'.");
            }

            private static bool IsCombatAbilityIdsColumn(string sheetName, string column)
            {
                return string.Equals(sheetName, "Enemies", StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(column, "combat_ability_ids", StringComparison.OrdinalIgnoreCase);
            }

            private static List<string> SplitIdsToList(string value)
            {
                var ids = new List<string>();
                if (string.IsNullOrWhiteSpace(value))
                    return ids;

                var parts = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var id = part.Trim();
                    if (!string.IsNullOrWhiteSpace(id))
                        ids.Add(id);
                }

                return ids;
            }

            private void AddIssue(string sheet, int row, string column, string value, string message)
            {
                _report.Issues.Add(new ConfigValidationIssue(sheet, row, column, value, message));
            }
        }

        private static object ConvertRuntimeValue(string sheetName, string column, string value)
        {
            if (IsIntegerField(sheetName, column) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                return integer;

            if (IsNumberField(sheetName, column) && ConfigPipelineUtilities.TryParseNumber(value, out var number))
                return number;

            return value;
        }

        private static bool IsIntegerField(string sheetName, string column)
        {
            return IntegerFields.Contains(FieldKey(sheetName, column));
        }

        private static bool IsNumberField(string sheetName, string column)
        {
            return NumberFields.Contains(FieldKey(sheetName, column));
        }

        private static string FieldKey(string sheetName, string column)
        {
            return ConfigPipelineUtilities.FieldKey(sheetName, column);
        }
    }
}
