using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class ActivityConfigsParser : IConfigPipelineParser
    {
        private const string ConfigId = "activity_configs";

        private static readonly string[] RequiredSheets =
        {
            "Activities",
            "WorkDetails",
            "OrderDetails",
            "EventDetails",
            "ExploreDetails",
            "CombatDetails",
            "ActivityRequirements",
            "ActivityRewards",
            "ActivityTriggers",
            "Rarities",
            "Skills",
            "SkillsProgression",
            "EnumValues"
        };

        private static readonly Dictionary<string, string[]> RequiredColumns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Activities"] = new[]
            {
                "id", "name_id", "description_id", "icon_id", "type", "category", "rarity_id", "tier",
                "location_id", "progress_mode", "duration_sec", "cycle_sec", "fatigue_cost", "main_skill_id",
                "is_repeatable", "offline_enabled", "enabled", "stat_profile_id"
            },
            ["WorkDetails"] = new[] { "activity_id", "success_chance", "tool_type", "auto_repeat", "fail_mode" },
            ["OrderDetails"] = new[] { "activity_id", "order_source", "reputation_id", "can_repeat", "repeat_cooldown_sec", "consume_requirements_on_start" },
            ["EventDetails"] = new[] { "activity_id", "event_kind", "discover_condition_id", "starts_combat", "encounter_id", "one_time", "hidden_until_discovered" },
            ["ExploreDetails"] = new[] { "activity_id", "unlock_location_id", "discovery_points_required", "danger_level" },
            ["CombatDetails"] = new[] { "activity_id", "enemy_group_id", "combat_mode", "intended_first_result", "completion_reward_rule" },
            ["ActivityRequirements"] = new[] { "activity_id", "req_type", "target_id", "value", "consume", "hidden", "check_moment" },
            ["ActivityRewards"] = new[] { "activity_id", "reward_type", "target_id", "min", "max", "chance", "grant_moment" },
            ["ActivityTriggers"] = new[] { "activity_id", "trigger_moment", "trigger_type", "target_id", "value", "chance", "once_only" },
            ["Rarities"] = new[] { "id", "name_id", "description_id", "icon_id", "color_hex", "reward_mult", "duration_mult", "fatigue_mult", "weight" },
            ["Skills"] = new[] { "skill_id", "skill_name_id", "skill_description_id", "skill_icon_id" },
            ["SkillsProgression"] = new[] { "level", "exp_to_next_level", "total_exp_required" },
            ["EnumValues"] = new[] { "enum_group", "value", "description" }
        };

        private static readonly Dictionary<string, string> RuntimeArrayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Activities"] = "activities",
            ["WorkDetails"] = "workDetails",
            ["OrderDetails"] = "orderDetails",
            ["EventDetails"] = "eventDetails",
            ["ExploreDetails"] = "exploreDetails",
            ["CombatDetails"] = "combatDetails",
            ["ActivityRequirements"] = "requirements",
            ["ActivityRewards"] = "rewards",
            ["ActivityTriggers"] = "triggers",
            ["Rarities"] = "rarities",
            ["Skills"] = "skills",
            ["SkillsProgression"] = "skillsProgression",
            ["EnumValues"] = "enumValues"
        };

        private static readonly HashSet<string> DesignerColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Название",
            "notes"
        };

        private static readonly HashSet<string> RuntimeExcludedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Название",
            "notes",
            "enabled"
        };

        private static readonly HashSet<string> IntegerFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FieldKey("Activities", "tier"),
            FieldKey("Activities", "duration_sec"),
            FieldKey("Activities", "cycle_sec"),
            FieldKey("Activities", "fatigue_cost"),
            FieldKey("OrderDetails", "repeat_cooldown_sec"),
            FieldKey("ExploreDetails", "discovery_points_required"),
            FieldKey("ExploreDetails", "danger_level"),
            FieldKey("ActivityRewards", "min"),
            FieldKey("ActivityRewards", "max"),
            FieldKey("Rarities", "weight"),
            FieldKey("SkillsProgression", "level"),
            FieldKey("SkillsProgression", "exp_to_next_level"),
            FieldKey("SkillsProgression", "total_exp_required")
        };

        private static readonly HashSet<string> NumberFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FieldKey("WorkDetails", "success_chance"),
            FieldKey("ActivityRequirements", "value"),
            FieldKey("ActivityRewards", "chance"),
            FieldKey("ActivityTriggers", "value"),
            FieldKey("ActivityTriggers", "chance"),
            FieldKey("Rarities", "reward_mult"),
            FieldKey("Rarities", "duration_mult"),
            FieldKey("Rarities", "fatigue_mult")
        };

        private static readonly HashSet<string> RequiredNumberFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FieldKey("ActivityRequirements", "value"),
            FieldKey("ActivityTriggers", "value")
        };

        private static readonly HashSet<string> BoolColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "enabled",
            "is_repeatable",
            "offline_enabled",
            "consume",
            "hidden",
            "auto_repeat",
            "can_repeat",
            "consume_requirements_on_start",
            "starts_combat",
            "one_time",
            "hidden_until_discovered",
            "once_only"
        };

        private static readonly HashSet<string> AllowedActivityTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Work",
            "Order",
            "Event",
            "Explore",
            "CombatTask"
        };

        private static readonly Dictionary<string, string> DetailSheetActivityTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WorkDetails"] = "Work",
            ["OrderDetails"] = "Order",
            ["EventDetails"] = "Event",
            ["ExploreDetails"] = "Explore",
            ["CombatDetails"] = "CombatTask"
        };

        private static readonly Dictionary<string, string> EnumColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["type"] = "ActivityType",
            ["category"] = "ActivityCategory",
            ["rarity_id"] = "RarityId",
            ["progress_mode"] = "ProgressMode",
            ["main_skill_id"] = "SkillId",
            ["fail_mode"] = "FailMode",
            ["req_type"] = "RequirementType",
            ["reward_type"] = "RewardType",
            ["trigger_type"] = "TriggerType",
            ["check_moment"] = "Moment",
            ["grant_moment"] = "Moment",
            ["trigger_moment"] = "Moment",
            ["id"] = "RarityId",
            ["skill_id"] = "SkillId"
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

            if (!TryValidateRuntimeOutputPath(source.runtime_json_path, out var fullPath, out var pathError))
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

            if (!TryLoadDownload(source, report, out var download))
                return report;

            var context = new ActivityConfigContext(download, report);
            context.ValidateSheetsAndColumns();
            context.CollectEnumValues();
            context.CollectActivityIds();
            context.ValidateRows();

            if (!report.Success)
                return report;

            runtimeJson = RuntimeJsonWriter.Write(context.BuildRuntimeArrays());
            return report;
        }

        private static bool TryLoadDownload(
            ConfigSourceSettings source,
            ConfigPipelineReport report,
            out ConfigSheetDownload download)
        {
            download = null;
            if (source == null)
            {
                report.ErrorMessage = "Source is empty.";
                return false;
            }

            if (!ConfigPaths.IsJsonPath(source.output_json_path))
            {
                report.ErrorMessage = "output_json_path must end with .json.";
                return false;
            }

            if (!ConfigPaths.TryGetProjectRelativeFullPath(
                    source.output_json_path,
                    out var rawFullPath,
                    out var pathError,
                    requireOutsideAssets: true))
            {
                report.ErrorMessage = pathError;
                return false;
            }

            if (!File.Exists(rawFullPath))
            {
                report.ErrorMessage = $"Raw JSON is missing: {source.output_json_path}";
                return false;
            }

            try
            {
                download = JsonUtility.FromJson<ConfigSheetDownload>(File.ReadAllText(rawFullPath, Encoding.UTF8));
            }
            catch (Exception exception)
            {
                report.ErrorMessage = $"Could not parse raw JSON '{source.output_json_path}': {exception.Message}";
                return false;
            }

            if (download?.sheets == null || download.sheets.Length == 0)
            {
                report.ErrorMessage = $"Raw JSON '{source.output_json_path}' contains no sheets.";
                return false;
            }

            return true;
        }

        private static bool TryValidateRuntimeOutputPath(string runtimePath, out string fullPath, out string error)
        {
            fullPath = null;
            error = null;

            if (!ConfigPaths.IsJsonPath(runtimePath))
            {
                error = "runtime_json_path must end with .json.";
                return false;
            }

            return ConfigPaths.TryGetProjectRelativeFullPath(
                runtimePath,
                out fullPath,
                out error,
                requireAssetsPath: true);
        }

        private sealed class ActivityConfigContext
        {
            private readonly ConfigPipelineReport _report;
            private readonly Dictionary<string, SheetTable> _tables = new Dictionary<string, SheetTable>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, HashSet<string>> _enumValues = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _enabledActivityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _allActivityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> _activityTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> _activityCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _rarityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _skillIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public ActivityConfigContext(ConfigSheetDownload download, ConfigPipelineReport report)
            {
                _report = report;
                foreach (var sheet in download.sheets)
                {
                    if (sheet == null || string.IsNullOrWhiteSpace(sheet.sheet_name))
                        continue;

                    _tables[sheet.sheet_name] = new SheetTable(sheet);
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
                if (!_tables.TryGetValue("EnumValues", out var table) ||
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

            public void CollectActivityIds()
            {
                CollectIdSet("Rarities", "id", _rarityIds);
                CollectIdSet("Skills", "skill_id", _skillIds);

                if (!_tables.TryGetValue("Activities", out var activities) ||
                    !activities.HasColumn("id") ||
                    !activities.HasColumn("enabled"))
                {
                    return;
                }

                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in activities.DataRows)
                {
                    var id = row.Get("id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        AddIssue("Activities", row.RowNumber, "id", id, "Activity id is required.");
                        continue;
                    }

                    if (seen.TryGetValue(id, out var firstRow))
                    {
                        AddIssue("Activities", row.RowNumber, "id", id, $"Duplicate activity id; first declared at row {firstRow}.");
                    }
                    else
                    {
                        seen[id] = row.RowNumber;
                    }

                    _allActivityIds.Add(id);
                    _activityTypes[id] = row.Get("type");
                    _activityCategories[id] = row.Get("category");

                    if (TryParseBool(row, "enabled", required: true, out var enabled) && enabled)
                        _enabledActivityIds.Add(id);
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
                        if (string.Equals(table.Name, "EnumValues", StringComparison.OrdinalIgnoreCase))
                            ValidateEnumValuesRow(row, enumValuePairs);

                        ValidateTypedFields(table, row);
                        ValidateSheetReferences(table, row);
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
                        if (ShouldSkipRuntimeRow(sheetName, row))
                            continue;

                        var runtimeRow = new Dictionary<string, object>(StringComparer.Ordinal);
                        foreach (var column in table.Headers)
                        {
                            if (RuntimeExcludedColumns.Contains(column))
                                continue;

                            var value = row.Get(column);
                            if (string.IsNullOrWhiteSpace(value))
                                continue;

                            runtimeRow[ToCamelCase(column)] = ConvertRuntimeValue(sheetName, column, value);
                        }

                        ApplyRuntimeDefaults(sheetName, runtimeRow);

                        if (runtimeRow.Count > 0)
                            arrays[runtimeName].Add(runtimeRow);
                    }
                }

                return arrays;
            }

            private static void ApplyRuntimeDefaults(string sheetName, Dictionary<string, object> runtimeRow)
            {
                if (!string.Equals(sheetName, "ActivityRequirements", StringComparison.OrdinalIgnoreCase))
                    return;

                if (!runtimeRow.ContainsKey("consume"))
                    runtimeRow["consume"] = false;

                if (!runtimeRow.ContainsKey("hidden"))
                    runtimeRow["hidden"] = false;
            }

            private void CollectIdSet(string sheetName, string column, HashSet<string> ids)
            {
                if (!_tables.TryGetValue(sheetName, out var table) || !table.HasColumn(column))
                    return;

                foreach (var row in table.DataRows)
                {
                    var id = row.Get(column);
                    if (!string.IsNullOrWhiteSpace(id))
                        ids.Add(id);
                }
            }

            private void ValidateTypedFields(SheetTable table, SheetDataRow row)
            {
                foreach (var column in table.Headers)
                {
                    if (DesignerColumns.Contains(column))
                        continue;

                    var value = row.Get(column);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        if (IsRequiredNumberField(table.Name, column))
                            AddIssue(table.Name, row.RowNumber, column, value, "Number value is required.");

                        continue;
                    }

                    if (BoolColumns.Contains(column))
                        TryParseBool(row, column, required: false, out _);

                    if (IsIntegerField(table.Name, column) && !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        AddIssue(table.Name, row.RowNumber, column, value, "Expected an integer number.");

                    if (IsNumberField(table.Name, column) && !TryParseNumber(value, out _))
                        AddIssue(table.Name, row.RowNumber, column, value, "Expected a number.");

                    ValidateEnum(table.Name, row, column, value);
                }
            }

            private void ValidateEnumValuesRow(SheetDataRow row, HashSet<string> enumValuePairs)
            {
                var enumGroup = row.Get("enum_group");
                var value = row.Get("value");

                if (string.IsNullOrWhiteSpace(enumGroup))
                    AddIssue("EnumValues", row.RowNumber, "enum_group", enumGroup, "Enum group is required.");

                if (string.IsNullOrWhiteSpace(value))
                    AddIssue("EnumValues", row.RowNumber, "value", value, "Enum value is required.");

                if (string.IsNullOrWhiteSpace(enumGroup) || string.IsNullOrWhiteSpace(value))
                    return;

                var key = $"{enumGroup}\n{value}";
                if (!enumValuePairs.Add(key))
                    AddIssue("EnumValues", row.RowNumber, "value", value, $"Duplicate enum value in group '{enumGroup}'.");
            }

            private void ValidateSheetReferences(SheetTable table, SheetDataRow row)
            {
                if (table.HasColumn("activity_id"))
                {
                    var activityId = row.Get("activity_id");
                    if (!string.IsNullOrWhiteSpace(activityId) && !_allActivityIds.Contains(activityId))
                        AddIssue(table.Name, row.RowNumber, "activity_id", activityId, "Referenced activity_id does not exist in Activities.id.");
                }

                if (string.Equals(table.Name, "Activities", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateActivityType(row);

                    var rarityId = row.Get("rarity_id");
                    if (!string.IsNullOrWhiteSpace(rarityId) && !_rarityIds.Contains(rarityId))
                        AddIssue(table.Name, row.RowNumber, "rarity_id", rarityId, "Referenced rarity_id does not exist in Rarities.id.");

                    var mainSkillId = row.Get("main_skill_id");
                    if (!string.IsNullOrWhiteSpace(mainSkillId) && !_skillIds.Contains(mainSkillId))
                        AddIssue(table.Name, row.RowNumber, "main_skill_id", mainSkillId, "Referenced main_skill_id does not exist in Skills.skill_id.");
                }

                ValidateDetailSheetActivityType(table, row);
            }

            private void ValidateActivityType(SheetDataRow row)
            {
                var activityType = row.Get("type");
                if (string.IsNullOrWhiteSpace(activityType))
                    return;

                if (AllowedActivityTypes.Contains(activityType))
                    return;

                if (string.Equals(activityType, "Build", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(activityType, "Craft", StringComparison.OrdinalIgnoreCase))
                {
                    AddIssue(
                        "Activities",
                        row.RowNumber,
                        string.Empty,
                        string.Empty,
                        $"type '{activityType}' is not allowed in Activity Configs. Use Buildings Configs or Items Configs instead.");
                    return;
                }

                AddIssue(
                    "Activities",
                    row.RowNumber,
                    "type",
                    activityType,
                    "Type is not allowed in Activity Configs.");
            }

            private void ValidateDetailSheetActivityType(SheetTable table, SheetDataRow row)
            {
                if (!DetailSheetActivityTypes.TryGetValue(table.Name, out var expectedType))
                    return;

                var activityId = row.Get("activity_id");
                if (string.IsNullOrWhiteSpace(activityId) ||
                    !_activityTypes.TryGetValue(activityId, out var actualType) ||
                    string.IsNullOrWhiteSpace(actualType))
                {
                    return;
                }

                if (string.Equals(table.Name, "CombatDetails", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateCombatDetailsActivity(row, activityId, actualType);
                    return;
                }

                if (!string.Equals(actualType, expectedType, StringComparison.OrdinalIgnoreCase))
                {
                    AddIssue(
                        table.Name,
                        row.RowNumber,
                        "activity_id",
                        activityId,
                        $"Referenced activity type is '{actualType}', but {table.Name} requires '{expectedType}'.");
                }
            }

            private void ValidateCombatDetailsActivity(SheetDataRow row, string activityId, string actualType)
            {
                var category = _activityCategories.TryGetValue(activityId, out var actualCategory)
                    ? actualCategory
                    : string.Empty;
                var isCombatTask = string.Equals(actualType, "CombatTask", StringComparison.OrdinalIgnoreCase);
                var isCombatOrder = string.Equals(actualType, "Order", StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(category, "CombatOrder", StringComparison.OrdinalIgnoreCase);

                if (isCombatTask || isCombatOrder)
                    return;

                AddIssue(
                    "CombatDetails",
                    row.RowNumber,
                    "activity_id",
                    activityId,
                    "CombatDetails requires activity type CombatTask or Order with category CombatOrder.");
            }

            private void ValidateEnum(string sheetName, SheetDataRow row, string column, string value)
            {
                if (!TryGetEnumGroup(sheetName, column, out var enumGroup))
                    return;

                if (!_enumValues.TryGetValue(enumGroup, out var allowedValues))
                    return;

                if (!allowedValues.Contains(value))
                    AddIssue(sheetName, row.RowNumber, column, value, $"Value is not listed in EnumValues group '{enumGroup}'.");
            }

            private bool TryGetEnumGroup(string sheetName, string column, out string enumGroup)
            {
                enumGroup = null;

                if (string.Equals(column, "id", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(sheetName, "Rarities", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.Equals(column, "skill_id", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(sheetName, "Skills", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return EnumColumns.TryGetValue(column, out enumGroup);
            }

            private bool ShouldSkipRuntimeRow(string sheetName, SheetDataRow row)
            {
                if (string.Equals(sheetName, "Activities", StringComparison.OrdinalIgnoreCase))
                    return TryParseBool(row, "enabled", required: false, out var enabled) && !enabled;

                if (row.HasColumn("activity_id"))
                {
                    var activityId = row.Get("activity_id");
                    return !string.IsNullOrWhiteSpace(activityId) &&
                           _allActivityIds.Contains(activityId) &&
                           !_enabledActivityIds.Contains(activityId);
                }

                return false;
            }

            private bool TryParseBool(SheetDataRow row, string column, bool required, out bool value)
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

            private void AddIssue(string sheet, int row, string column, string value, string message)
            {
                _report.Issues.Add(new ConfigValidationIssue(sheet, row, column, value, message));
            }
        }

        private sealed class SheetTable
        {
            private readonly Dictionary<string, int> _headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            private readonly List<SheetDataRow> _dataRows = new List<SheetDataRow>();

            public string Name { get; }
            public IReadOnlyList<string> Headers { get; }
            public IReadOnlyList<SheetDataRow> DataRows => _dataRows;
            public int Rows { get; }

            public SheetTable(ConfigDownloadedSheet sheet)
            {
                Name = sheet.sheet_name;
                var rows = sheet.rows ?? Array.Empty<ConfigSheetRow>();
                Rows = rows.Length;

                var headers = new List<string>();
                if (rows.Length > 0 && rows[0]?.cells != null)
                {
                    for (var index = 0; index < rows[0].cells.Length; index++)
                    {
                        var header = (rows[0].cells[index] ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(header))
                            continue;

                        headers.Add(header);
                        if (!_headerIndex.ContainsKey(header))
                            _headerIndex[header] = index;
                    }
                }

                Headers = headers;

                for (var index = 1; index < rows.Length; index++)
                {
                    if (rows[index]?.cells == null || IsEmpty(rows[index].cells))
                        continue;

                    _dataRows.Add(new SheetDataRow(this, rows[index].cells, index + 1));
                }
            }

            public bool HasColumn(string column)
            {
                return _headerIndex.ContainsKey(column);
            }

            public string Get(ConfigSheetRow row, string column)
            {
                if (!_headerIndex.TryGetValue(column, out var index) ||
                    row?.cells == null ||
                    index < 0 ||
                    index >= row.cells.Length)
                {
                    return string.Empty;
                }

                return (row.cells[index] ?? string.Empty).Trim();
            }

            public string Get(string[] cells, string column)
            {
                if (!_headerIndex.TryGetValue(column, out var index) ||
                    cells == null ||
                    index < 0 ||
                    index >= cells.Length)
                {
                    return string.Empty;
                }

                return (cells[index] ?? string.Empty).Trim();
            }

            private static bool IsEmpty(string[] cells)
            {
                foreach (var cell in cells)
                {
                    if (!string.IsNullOrWhiteSpace(cell))
                        return false;
                }

                return true;
            }
        }

        private sealed class SheetDataRow
        {
            private readonly string[] _cells;

            public SheetTable Table { get; }
            public int RowNumber { get; }

            public SheetDataRow(SheetTable table, string[] cells, int rowNumber)
            {
                Table = table;
                _cells = cells;
                RowNumber = rowNumber;
            }

            public bool HasColumn(string column)
            {
                return Table.HasColumn(column);
            }

            public string Get(string column)
            {
                return Table.Get(_cells, column);
            }
        }

        private static object ConvertRuntimeValue(string sheetName, string column, string value)
        {
            if (BoolColumns.Contains(column))
                return string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);

            if (IsIntegerField(sheetName, column) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                return integer;

            if (IsNumberField(sheetName, column) && TryParseNumber(value, out var number))
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

        private static bool IsRequiredNumberField(string sheetName, string column)
        {
            return RequiredNumberFields.Contains(FieldKey(sheetName, column));
        }

        private static string FieldKey(string sheetName, string column)
        {
            return $"{sheetName}.{column}";
        }

        private static bool TryParseNumber(string value, out double number)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ||
                   double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        private static string ToCamelCase(string snakeCase)
        {
            if (string.IsNullOrWhiteSpace(snakeCase))
                return string.Empty;

            var builder = new StringBuilder();
            var upperNext = false;
            foreach (var character in snakeCase)
            {
                if (character == '_')
                {
                    upperNext = true;
                    continue;
                }

                if (builder.Length == 0)
                {
                    builder.Append(char.ToLowerInvariant(character));
                    upperNext = false;
                    continue;
                }

                builder.Append(upperNext ? char.ToUpperInvariant(character) : character);
                upperNext = false;
            }

            return builder.ToString();
        }

        private static class RuntimeJsonWriter
        {
            public static string Write(Dictionary<string, List<Dictionary<string, object>>> arrays)
            {
                var builder = new StringBuilder();
                builder.Append("{\n");

                var arrayIndex = 0;
                foreach (var pair in arrays)
                {
                    if (arrayIndex > 0)
                        builder.Append(",\n");

                    builder.Append("  \"").Append(Escape(pair.Key)).Append("\": [");
                    if (pair.Value.Count > 0)
                        builder.Append('\n');

                    for (var rowIndex = 0; rowIndex < pair.Value.Count; rowIndex++)
                    {
                        if (rowIndex > 0)
                            builder.Append(",\n");

                        WriteObject(builder, pair.Value[rowIndex], "    ");
                    }

                    if (pair.Value.Count > 0)
                        builder.Append('\n').Append("  ");

                    builder.Append(']');
                    arrayIndex++;
                }

                builder.Append("\n}\n");
                return builder.ToString();
            }

            private static void WriteObject(StringBuilder builder, Dictionary<string, object> values, string indent)
            {
                builder.Append(indent).Append('{');

                var index = 0;
                foreach (var pair in values)
                {
                    if (index > 0)
                        builder.Append(',');

                    builder.Append('\n')
                        .Append(indent)
                        .Append("  \"")
                        .Append(Escape(pair.Key))
                        .Append("\": ");
                    WriteValue(builder, pair.Value);
                    index++;
                }

                if (values.Count > 0)
                    builder.Append('\n').Append(indent);

                builder.Append('}');
            }

            private static void WriteValue(StringBuilder builder, object value)
            {
                switch (value)
                {
                    case bool boolean:
                        builder.Append(boolean ? "true" : "false");
                        break;
                    case int integer:
                        builder.Append(integer.ToString(CultureInfo.InvariantCulture));
                        break;
                    case long longValue:
                        builder.Append(longValue.ToString(CultureInfo.InvariantCulture));
                        break;
                    case float floatValue:
                        builder.Append(floatValue.ToString(CultureInfo.InvariantCulture));
                        break;
                    case double doubleValue:
                        builder.Append(doubleValue.ToString(CultureInfo.InvariantCulture));
                        break;
                    default:
                        builder.Append('"').Append(Escape(Convert.ToString(value, CultureInfo.InvariantCulture))).Append('"');
                        break;
                }
            }

            private static string Escape(string value)
            {
                if (string.IsNullOrEmpty(value))
                    return string.Empty;

                var builder = new StringBuilder(value.Length + 8);
                foreach (var character in value)
                {
                    switch (character)
                    {
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            builder.Append(character);
                            break;
                    }
                }

                return builder.ToString();
            }
        }
    }
}
