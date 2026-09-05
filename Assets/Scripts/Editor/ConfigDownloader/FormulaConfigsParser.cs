using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class FormulaConfigsParser : IConfigPipelineParser
    {
        private const string ConfigId = "formula_configs";
        private const string HeroDerivedStatsSheet = "HeroDerivedStats";
        private const string SkillStatWeightsSheet = "SkillStatWeights";

        private static readonly string[] RequiredSheets =
        {
            HeroDerivedStatsSheet,
            SkillStatWeightsSheet
        };

        private static readonly Dictionary<string, string[]> RequiredColumns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [HeroDerivedStatsSheet] = new[]
            {
                "formula_id", "derived_stat_id", "formula_type", "base_value", "primary_stat",
                "primary_stat_multiplier", "secondary_stat", "secondary_stat_multiplier", "level_multiplier",
                "weapon_value_mode", "min_value", "max_value", "cap_value", "value_type", "rounding",
                "enabled", "notes", "expression_preview"
            },
            [SkillStatWeightsSheet] = new[]
            {
                "profile_id", "skill_id", "stat_id", "weight", "enabled", "notes"
            }
        };

        private static readonly HashSet<string> StatIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Strength",
            "Agility",
            "Intelligence",
            "Endurance",
            "Luck"
        };

        private static readonly HashSet<string> FormulaTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "linear_stat_with_level",
            "weapon_damage_linear_stat",
            "inverse_interval_stat",
            "linear_stat_capped",
            "mixed_linear_stat_capped",
            "constant",
            "linear_stats_with_skill_level",
            "context_base_minus_stats_and_skill_level"
        };

        private static readonly HashSet<string> WeaponValueModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "weapon_damage_min",
            "weapon_damage_max",
            "weapon_attack_interval"
        };

        private static readonly HashSet<string> ValueTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "number",
            "seconds",
            "percent",
            "multiplier"
        };

        private static readonly HashSet<string> Roundings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "floor",
            "round_2"
        };

        private static readonly string[] HeroNumericColumns =
        {
            "base_value",
            "primary_stat_multiplier",
            "secondary_stat_multiplier",
            "level_multiplier",
            "min_value",
            "max_value",
            "cap_value"
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
                ConfigPipelineUtilities.WriteRuntimeJson(fullPath, runtimeJson);

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

            var context = new FormulaConfigContext(download, report);
            context.ValidateSheetsAndColumns();
            context.ValidateHeroDerivedStats();
            context.ValidateSkillStatWeights();

            if (!report.Success)
                return report;

            runtimeJson = ConfigRuntimeJsonWriter.Write(context.BuildRuntimeArrays());
            return report;
        }

        private sealed class FormulaConfigContext
        {
            private readonly ConfigPipelineReport _report;
            private readonly Dictionary<string, ConfigSheetTable> _tables = new Dictionary<string, ConfigSheetTable>(StringComparer.OrdinalIgnoreCase);

            public FormulaConfigContext(ConfigSheetDownload download, ConfigPipelineReport report)
            {
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

            public void ValidateHeroDerivedStats()
            {
                if (!_tables.TryGetValue(HeroDerivedStatsSheet, out var table))
                    return;

                var formulaIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    ValidateRequired(row, "formula_id");
                    ValidateRequired(row, "derived_stat_id");
                    ValidateRequired(row, "formula_type");
                    ValidateRequired(row, "value_type");
                    ValidateRequired(row, "rounding");

                    var formulaId = row.Get("formula_id");
                    if (!string.IsNullOrWhiteSpace(formulaId))
                    {
                        if (formulaIds.TryGetValue(formulaId, out var firstRow))
                            AddIssue(HeroDerivedStatsSheet, row.RowNumber, "formula_id", formulaId, $"Duplicate formula_id; first declared at row {firstRow}.");
                        else
                            formulaIds[formulaId] = row.RowNumber;
                    }

                    ValidateHeroTypedFields(row);
                    ValidateHeroEnums(row);
                    ValidateHeroBounds(row);
                    ValidateFormulaTypeRules(row);
                }
            }

            public void ValidateSkillStatWeights()
            {
                if (!_tables.TryGetValue(SkillStatWeightsSheet, out var table))
                    return;

                var profileStatPairs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var enabledWeightSums = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var profileRows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var row in table.DataRows)
                {
                    ValidateRequired(row, "profile_id");
                    ValidateRequired(row, "skill_id");

                    var profileId = row.Get("profile_id");
                    var statId = row.Get("stat_id");
                    var pairKey = $"{profileId}\n{statId}";
                    if (!string.IsNullOrWhiteSpace(profileId) && !string.IsNullOrWhiteSpace(statId))
                    {
                        if (profileStatPairs.TryGetValue(pairKey, out var firstRow))
                            AddIssue(SkillStatWeightsSheet, row.RowNumber, "stat_id", statId, $"Duplicate profile_id + stat_id pair; first declared at row {firstRow}.");
                        else
                            profileStatPairs[pairKey] = row.RowNumber;
                    }

                    if (!string.IsNullOrWhiteSpace(profileId) && !profileRows.ContainsKey(profileId))
                        profileRows[profileId] = row.RowNumber;

                    if (string.IsNullOrWhiteSpace(statId))
                    {
                        AddIssue(SkillStatWeightsSheet, row.RowNumber, "stat_id", statId, "stat_id is required.");
                    }
                    else if (!StatIds.Contains(statId))
                    {
                        AddIssue(SkillStatWeightsSheet, row.RowNumber, "stat_id", statId, "stat_id is not an allowed hero stat id.");
                    }

                    var hasWeight = TryParseRequiredNumber(row, "weight", out var weight);
                    if (hasWeight && weight < 0d)
                        AddIssue(SkillStatWeightsSheet, row.RowNumber, "weight", row.Get("weight"), "weight must be greater than or equal to 0.");

                    var hasEnabled = TryParseBool(row, "enabled", required: true, out var enabled);
                    if (hasWeight && hasEnabled && enabled && !string.IsNullOrWhiteSpace(profileId))
                    {
                        enabledWeightSums.TryGetValue(profileId, out var sum);
                        enabledWeightSums[profileId] = sum + weight;
                    }
                }

                foreach (var pair in profileRows)
                {
                    enabledWeightSums.TryGetValue(pair.Key, out var sum);
                    if (sum <= 0d)
                    {
                        AddIssue(SkillStatWeightsSheet, pair.Value, "profile_id", pair.Key, "Enabled weight sum for profile_id must be greater than 0.");
                        continue;
                    }

                    if (Math.Abs(sum - 1d) > 0.0001d)
                        _report.Warnings.Add($"SkillStatWeights profile_id '{pair.Key}' enabled weight sum is {sum.ToString(CultureInfo.InvariantCulture)}, expected 1.0.");
                }
            }

            public Dictionary<string, List<Dictionary<string, object>>> BuildRuntimeArrays()
            {
                return new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.Ordinal)
                {
                    ["formulas"] = BuildHeroDerivedStats(),
                    ["skillStatWeights"] = BuildSkillStatWeights()
                };
            }

            private List<Dictionary<string, object>> BuildHeroDerivedStats()
            {
                var rows = new List<Dictionary<string, object>>();
                if (!_tables.TryGetValue(HeroDerivedStatsSheet, out var table))
                    return rows;

                foreach (var row in table.DataRows)
                {
                    rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["formulaId"] = row.Get("formula_id"),
                        ["derivedStatId"] = row.Get("derived_stat_id"),
                        ["formulaType"] = row.Get("formula_type"),
                        ["baseValue"] = GetNumber(row, "base_value"),
                        ["primaryStat"] = row.Get("primary_stat"),
                        ["primaryStatMultiplier"] = GetNumber(row, "primary_stat_multiplier"),
                        ["secondaryStat"] = row.Get("secondary_stat"),
                        ["secondaryStatMultiplier"] = GetNumber(row, "secondary_stat_multiplier"),
                        ["levelMultiplier"] = GetNumber(row, "level_multiplier"),
                        ["weaponValueMode"] = row.Get("weapon_value_mode"),
                        ["minValue"] = GetNumber(row, "min_value"),
                        ["maxValue"] = GetNumber(row, "max_value"),
                        ["capValue"] = GetNumber(row, "cap_value"),
                        ["valueType"] = row.Get("value_type"),
                        ["rounding"] = row.Get("rounding"),
                        ["enabled"] = GetBool(row, "enabled")
                    });
                }

                return rows;
            }

            private List<Dictionary<string, object>> BuildSkillStatWeights()
            {
                var rows = new List<Dictionary<string, object>>();
                if (!_tables.TryGetValue(SkillStatWeightsSheet, out var table))
                    return rows;

                foreach (var row in table.DataRows)
                {
                    rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["profileId"] = row.Get("profile_id"),
                        ["skillId"] = row.Get("skill_id"),
                        ["statId"] = row.Get("stat_id"),
                        ["weight"] = GetNumber(row, "weight"),
                        ["enabled"] = GetBool(row, "enabled")
                    });
                }

                return rows;
            }

            private void ValidateHeroTypedFields(ConfigSheetDataRow row)
            {
                TryParseBool(row, "enabled", required: true, out _);
                foreach (var column in HeroNumericColumns)
                {
                    var value = row.Get(column);
                    if (!string.IsNullOrWhiteSpace(value) && !TryParseFiniteNumber(value, out _))
                        AddIssue(HeroDerivedStatsSheet, row.RowNumber, column, value, "Expected a number.");
                }
            }

            private void ValidateHeroEnums(ConfigSheetDataRow row)
            {
                ValidateOptionalSet(row, "primary_stat", StatIds, "primary_stat is not an allowed hero stat id.");
                ValidateOptionalSet(row, "secondary_stat", StatIds, "secondary_stat is not an allowed hero stat id.");
                ValidateOptionalSet(row, "weapon_value_mode", WeaponValueModes, "weapon_value_mode is not an allowed weapon value id.");
                ValidateRequiredSet(row, "formula_type", FormulaTypes, "formula_type is not allowed.");
                ValidateRequiredSet(row, "value_type", ValueTypes, "value_type is not allowed.");
                ValidateRequiredSet(row, "rounding", Roundings, "rounding is not allowed.");

                if (string.IsNullOrWhiteSpace(row.Get("secondary_stat")) &&
                    HasNumberValue(row, "secondary_stat_multiplier", out var multiplier) &&
                    Math.Abs(multiplier) > 0.0000001d)
                {
                    AddIssue(HeroDerivedStatsSheet, row.RowNumber, "secondary_stat_multiplier", row.Get("secondary_stat_multiplier"), "secondary_stat_multiplier must be 0 when secondary_stat is empty.");
                }
            }

            private void ValidateHeroBounds(ConfigSheetDataRow row)
            {
                var hasMin = HasNumberValue(row, "min_value", out var minValue);
                var hasMax = HasNumberValue(row, "max_value", out var maxValue);
                var hasCap = HasNumberValue(row, "cap_value", out var capValue);

                if (hasMin && hasMax && minValue > maxValue)
                    AddIssue(HeroDerivedStatsSheet, row.RowNumber, "max_value", row.Get("max_value"), "min_value must not be greater than max_value.");

                if (hasCap && hasMin && capValue < minValue)
                    AddIssue(HeroDerivedStatsSheet, row.RowNumber, "cap_value", row.Get("cap_value"), "cap_value must be greater than or equal to min_value.");
            }

            private void ValidateFormulaTypeRules(ConfigSheetDataRow row)
            {
                var formulaType = row.Get("formula_type");
                if (string.IsNullOrWhiteSpace(formulaType) || !FormulaTypes.Contains(formulaType))
                    return;

                if (string.Equals(formulaType, "constant", StringComparison.OrdinalIgnoreCase))
                {
                    RequireNumber(row, "base_value");
                    return;
                }

                if (string.Equals(formulaType, "linear_stats_with_skill_level", StringComparison.OrdinalIgnoreCase))
                {
                    RequireNumber(row, "base_value");
                    RequireString(row, "primary_stat");
                    RequireNumber(row, "primary_stat_multiplier");
                    RequireString(row, "secondary_stat");
                    RequireNumber(row, "secondary_stat_multiplier");
                    RequireNumber(row, "level_multiplier");
                    return;
                }

                if (string.Equals(formulaType, "context_base_minus_stats_and_skill_level", StringComparison.OrdinalIgnoreCase))
                {
                    RequireString(row, "primary_stat");
                    RequireNumber(row, "primary_stat_multiplier");
                    RequireString(row, "secondary_stat");
                    RequireNumber(row, "secondary_stat_multiplier");
                    RequireNumber(row, "level_multiplier");
                    RequireNumber(row, "min_value");
                    return;
                }

                if (string.Equals(formulaType, "linear_stat_with_level", StringComparison.OrdinalIgnoreCase))
                {
                    RequireNumber(row, "base_value");
                    RequireString(row, "primary_stat");
                    RequireNumber(row, "primary_stat_multiplier");
                    RequireNumber(row, "level_multiplier");
                    return;
                }

                if (string.Equals(formulaType, "weapon_damage_linear_stat", StringComparison.OrdinalIgnoreCase))
                {
                    RequireString(row, "weapon_value_mode");
                    RequireString(row, "primary_stat");
                    RequireNumber(row, "primary_stat_multiplier");
                    return;
                }

                if (string.Equals(formulaType, "inverse_interval_stat", StringComparison.OrdinalIgnoreCase))
                {
                    RequireString(row, "weapon_value_mode");
                    RequireString(row, "primary_stat");
                    RequireNumber(row, "primary_stat_multiplier");
                    RequireNumberGreaterThan(row, "min_value", 0d);
                    return;
                }

                if (string.Equals(formulaType, "linear_stat_capped", StringComparison.OrdinalIgnoreCase))
                {
                    RequireNumber(row, "base_value");
                    RequireString(row, "primary_stat");
                    RequireNumber(row, "primary_stat_multiplier");
                    RequireNumberGreaterThan(row, "cap_value", 0d);
                    return;
                }

                if (string.Equals(formulaType, "mixed_linear_stat_capped", StringComparison.OrdinalIgnoreCase))
                {
                    RequireString(row, "primary_stat");
                    RequireNumber(row, "primary_stat_multiplier");
                    RequireString(row, "secondary_stat");
                    RequireNumber(row, "secondary_stat_multiplier");
                    RequireNumberGreaterThan(row, "cap_value", 0d);
                }
            }

            private void ValidateRequired(ConfigSheetDataRow row, string column)
            {
                var value = row.Get(column);
                if (string.IsNullOrWhiteSpace(value))
                    AddIssue(row.Table.Name, row.RowNumber, column, value, $"{column} is required.");
            }

            private void ValidateOptionalSet(ConfigSheetDataRow row, string column, HashSet<string> allowedValues, string message)
            {
                var value = row.Get(column);
                if (!string.IsNullOrWhiteSpace(value) && !allowedValues.Contains(value))
                    AddIssue(row.Table.Name, row.RowNumber, column, value, message);
            }

            private void ValidateRequiredSet(ConfigSheetDataRow row, string column, HashSet<string> allowedValues, string message)
            {
                var value = row.Get(column);
                if (!string.IsNullOrWhiteSpace(value) && !allowedValues.Contains(value))
                    AddIssue(row.Table.Name, row.RowNumber, column, value, message);
            }

            private void RequireString(ConfigSheetDataRow row, string column)
            {
                var value = row.Get(column);
                if (string.IsNullOrWhiteSpace(value))
                    AddIssue(row.Table.Name, row.RowNumber, column, value, $"{column} is required for formula_type '{row.Get("formula_type")}'.");
            }

            private void RequireNumber(ConfigSheetDataRow row, string column)
            {
                var value = row.Get(column);
                if (string.IsNullOrWhiteSpace(value))
                {
                    AddIssue(row.Table.Name, row.RowNumber, column, value, $"{column} is required for formula_type '{row.Get("formula_type")}'.");
                    return;
                }

                if (!TryParseFiniteNumber(value, out _))
                    AddIssue(row.Table.Name, row.RowNumber, column, value, "Expected a number.");
            }

            private void RequireNumberGreaterThan(ConfigSheetDataRow row, string column, double minimum)
            {
                var value = row.Get(column);
                if (string.IsNullOrWhiteSpace(value))
                {
                    AddIssue(row.Table.Name, row.RowNumber, column, value, $"{column} is required for formula_type '{row.Get("formula_type")}'.");
                    return;
                }

                if (!TryParseFiniteNumber(value, out var number))
                {
                    AddIssue(row.Table.Name, row.RowNumber, column, value, "Expected a number.");
                    return;
                }

                if (number <= minimum)
                    AddIssue(row.Table.Name, row.RowNumber, column, value, $"{column} must be greater than {minimum.ToString(CultureInfo.InvariantCulture)} for formula_type '{row.Get("formula_type")}'.");
            }

            private bool TryParseRequiredNumber(ConfigSheetDataRow row, string column, out double value)
            {
                value = 0d;
                var raw = row.Get(column);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    AddIssue(row.Table.Name, row.RowNumber, column, raw, $"{column} is required.");
                    return false;
                }

                if (TryParseFiniteNumber(raw, out value))
                    return true;

                AddIssue(row.Table.Name, row.RowNumber, column, raw, "Expected a number.");
                return false;
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

            private bool HasNumberValue(ConfigSheetDataRow row, string column, out double value)
            {
                value = 0d;
                var raw = row.Get(column);
                return !string.IsNullOrWhiteSpace(raw) && TryParseFiniteNumber(raw, out value);
            }

            private double GetNumber(ConfigSheetDataRow row, string column)
            {
                return TryParseFiniteNumber(row.Get(column), out var number) ? number : 0d;
            }

            private bool GetBool(ConfigSheetDataRow row, string column)
            {
                TryParseBool(row, column, required: false, out var value);
                return value;
            }

            private static bool TryParseFiniteNumber(string raw, out double value)
            {
                return ConfigPipelineUtilities.TryParseFiniteNumber(raw, out value);
            }

            private void AddIssue(string sheet, int row, string column, string value, string message)
            {
                _report.Issues.Add(new ConfigValidationIssue(sheet, row, column, value, message));
            }
        }
    }
}
