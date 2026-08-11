using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GuildIdle.Core;
using UnityEditor;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class QuestConfigsSpreadsheetParser : IConfigPipelineParser
    {
        private const string ConfigId = "quest_configs";
        private static readonly string[] RequiredSheets =
        {
            "Stages", "StageQuests", "StoryQuests", "DailyQuests", "QuestStartConditions",
            "QuestSteps", "QuestRewards", "EnumValues"
        };

        private static readonly Dictionary<string, string[]> Columns = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Stages"] = new[] { "stage_id", "name_id", "description_id", "stage_prefab_id", "target_duration_sec", "completion_rule", "next_stage_id", "sort_order", "enabled", "notes" },
            ["StageQuests"] = new[] { "stage_id", "quest_id", "weight_percent", "required", "show_in_stage_ui", "sort_order", "enabled", "notes" },
            ["StoryQuests"] = new[] { "quest_id", "name_id", "description_id", "icon_id", "journal_category", "sort_order", "is_tutorial", "close_on_stage_complete", "enabled", "notes" },
            ["DailyQuests"] = new[] { "quest_id", "name_id", "description_id", "icon_id", "journal_category", "daily_pool_id", "selection_weight", "sort_order", "enabled", "notes" },
            ["QuestStartConditions"] = new[] { "quest_id", "condition_group", "condition_type", "target_id", "operator", "value", "sort_order", "notes" },
            ["QuestSteps"] = new[] { "quest_id", "step_id", "step_order", "objective_type", "target_id", "operator", "target_value", "description_id", "required", "notes" },
            ["QuestRewards"] = new[] { "quest_id", "reward_id", "reward_type", "target_id", "min", "max", "chance", "grant_moment", "sort_order", "notes" },
            ["EnumValues"] = new[] { "enum_group", "value", "description" }
        };

        private static readonly Dictionary<string, string> RuntimeNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Stages"] = "stages", ["StageQuests"] = "stageQuests", ["StoryQuests"] = "storyQuests",
            ["DailyQuests"] = "dailyQuests", ["QuestStartConditions"] = "questStartConditions",
            ["QuestSteps"] = "questSteps", ["QuestRewards"] = "questRewards", ["EnumValues"] = "enumValues"
        };

        private static readonly HashSet<string> IntegerColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            "target_duration_sec", "sort_order", "weight_percent", "selection_weight", "value",
            "step_order", "target_value", "min", "max"
        };

        private static readonly HashSet<string> BoolColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            "enabled", "required", "show_in_stage_ui", "is_tutorial", "close_on_stage_complete"
        };

        public bool Supports(ConfigSourceSettings source) =>
            source != null && string.Equals(source.config_id, ConfigId, StringComparison.OrdinalIgnoreCase);

        public ConfigPipelineReport ParseAndWrite(ConfigSourceSettings source)
        {
            var report = BuildRuntimeJson(source, out var json);
            if (!report.Success)
                return report;

            if (!ConfigPipelineUtilities.TryValidateRuntimeOutputPath(source.runtime_json_path, out var fullPath, out var error))
            {
                report.ErrorMessage = error;
                return report;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                var temp = fullPath + ".tmp";
                File.WriteAllText(temp, json, ConfigPipelineUtilities.Utf8NoBom);
                if (File.Exists(fullPath)) File.Replace(temp, fullPath, null); else File.Move(temp, fullPath);
                AssetDatabase.ImportAsset(ConfigPaths.NormalizeProjectPath(source.runtime_json_path));
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                report.ErrorMessage = $"Could not write runtime JSON '{source.runtime_json_path}': {exception.Message}";
            }

            return report;
        }

        public ConfigPipelineReport Validate(ConfigSourceSettings source) => BuildRuntimeJson(source, out _);

        public ConfigPipelineReport BuildRuntimeJson(ConfigSourceSettings source, out string runtimeJson)
        {
            runtimeJson = null;
            var report = new ConfigPipelineReport();
            if (!ConfigPipelineUtilities.TryLoadDownload(source, report, out var download))
                return report;

            var context = new Context(download, report);
            context.Validate();
            if (report.Success)
                runtimeJson = ConfigRuntimeJsonWriter.Write(context.BuildRuntimeArrays());
            return report;
        }

        private sealed class Context
        {
            private readonly ConfigPipelineReport _report;
            private readonly Dictionary<string, ConfigSheetTable> _tables = new Dictionary<string, ConfigSheetTable>(StringComparer.Ordinal);
            private readonly Dictionary<string, HashSet<string>> _enums = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            private readonly HashSet<string> _stageIds = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _enabledStageIds = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _storyIds = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _definitionIds = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> _enabledDefinitionIds = new HashSet<string>(StringComparer.Ordinal);

            public Context(ConfigSheetDownload download, ConfigPipelineReport report)
            {
                _report = report;
                foreach (var sheet in download.sheets ?? Array.Empty<ConfigDownloadedSheet>())
                {
                    if (sheet != null && !string.IsNullOrWhiteSpace(sheet.sheet_name))
                        _tables[sheet.sheet_name] = new ConfigSheetTable(sheet);
                }
            }

            public void Validate()
            {
                ValidateHeaders();
                CollectEnums();
                ValidateQuestStatusEnums();
                CollectStages();
                CollectDefinitions("StoryQuests", _storyIds);
                CollectDefinitions("DailyQuests", null);
                ValidateStages();
                ValidateStageQuests();
                ValidateDefinitions();
                ValidateConditions();
                ValidateSteps();
                ValidateRewards();
                ValidateStageTwo();
                ValidateStageCycles();
            }

            public Dictionary<string, List<Dictionary<string, object>>> BuildRuntimeArrays()
            {
                var result = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.Ordinal);
                foreach (var sheet in RequiredSheets)
                {
                    var rows = new List<Dictionary<string, object>>();
                    result[RuntimeNames[sheet]] = rows;
                    if (!_tables.TryGetValue(sheet, out var table))
                        continue;

                    foreach (var row in table.DataRows)
                    {
                        var value = new Dictionary<string, object>(StringComparer.Ordinal);
                        foreach (var column in Columns[sheet])
                        {
                            if (column == "notes")
                                continue;
                            var raw = row.Get(column);
                            if (string.IsNullOrWhiteSpace(raw))
                                continue;
                            value[ConfigPipelineUtilities.ToCamelCase(column == "operator" ? "compare_operator" : column)] = ConvertValue(column, raw);
                        }
                        if (value.Count > 0)
                            rows.Add(value);
                    }
                }
                return result;
            }

            private void ValidateHeaders()
            {
                foreach (var sheet in RequiredSheets)
                {
                    if (!_tables.TryGetValue(sheet, out var table))
                    {
                        Issue(sheet, 0, string.Empty, string.Empty, "Required sheet is missing.");
                        continue;
                    }
                    if (table.Rows == 0)
                    {
                        Issue(sheet, 1, string.Empty, string.Empty, "Required sheet has no header row.");
                        continue;
                    }
                    foreach (var column in Columns[sheet])
                    {
                        var exact = false;
                        foreach (var header in table.Headers)
                            exact |= string.Equals(header, column, StringComparison.Ordinal);
                        if (!exact)
                            Issue(sheet, 1, column, string.Empty, "Required exact column is missing.");
                    }
                }
            }

            private void CollectEnums()
            {
                if (!_tables.TryGetValue("EnumValues", out var table)) return;
                var pairs = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in table.DataRows)
                {
                    var group = row.Get("enum_group");
                    var value = row.Get("value");
                    if (string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(value))
                    {
                        Issue("EnumValues", row.RowNumber, "value", value, "enum_group and value are required.");
                        continue;
                    }
                    if (!pairs.Add(group + "\n" + value))
                        Issue("EnumValues", row.RowNumber, "value", value, $"Duplicate enum value in group '{group}'.");
                    if (!_enums.TryGetValue(group, out var values)) _enums[group] = values = new HashSet<string>(StringComparer.Ordinal);
                    values.Add(value);
                }
            }

            private void CollectStages()
            {
                if (!_tables.TryGetValue("Stages", out var table)) return;
                CollectIds(table, "stage_id", _stageIds, (row, id) => { if (ReadBool(row, "enabled", false)) _enabledStageIds.Add(id); });
            }

            private void ValidateQuestStatusEnums()
            {
                var required = new[] { "Active", "RewardPending", "Completed", "Closed", "Expired" };
                if (!_enums.TryGetValue("QuestInstanceStatus", out var values))
                {
                    Issue("EnumValues", 0, "enum_group", "QuestInstanceStatus", "QuestInstanceStatus enum group is required.");
                    return;
                }
                foreach (var value in required)
                    if (!values.Contains(value)) Issue("EnumValues", 0, "value", value, $"QuestInstanceStatus must declare '{value}'.");
            }

            private void CollectDefinitions(string sheet, HashSet<string> kindIds)
            {
                if (!_tables.TryGetValue(sheet, out var table)) return;
                var local = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in table.DataRows)
                {
                    var id = row.Get("quest_id");
                    if (string.IsNullOrWhiteSpace(id)) { Issue(sheet, row.RowNumber, "quest_id", id, "quest_id is required."); continue; }
                    if (!local.Add(id)) Issue(sheet, row.RowNumber, "quest_id", id, "Duplicate quest_id in sheet.");
                    if (!_definitionIds.Add(id)) Issue(sheet, row.RowNumber, "quest_id", id, "quest_id must be globally unique across StoryQuests and DailyQuests.");
                    kindIds?.Add(id);
                    if (ReadBool(row, "enabled", false)) _enabledDefinitionIds.Add(id);
                }
            }

            private void ValidateStages()
            {
                if (!_tables.TryGetValue("Stages", out var table)) return;
                foreach (var row in table.DataRows)
                {
                    Required(row, "name_id", "description_id", "stage_prefab_id", "completion_rule", "sort_order", "enabled");
                    NonNegativeInt(row, "target_duration_sec"); NonNegativeInt(row, "sort_order"); Bool(row, "enabled");
                    Enum(row, "completion_rule", "CompletionRule");
                    var next = row.Get("next_stage_id");
                    if (!string.IsNullOrWhiteSpace(next) && !_enabledStageIds.Contains(next))
                        Issue("Stages", row.RowNumber, "next_stage_id", next, "next_stage_id must reference an enabled Stages.stage_id.");
                }
            }

            private void ValidateStageQuests()
            {
                if (!_tables.TryGetValue("StageQuests", out var table)) return;
                var keys = new HashSet<string>(StringComparer.Ordinal);
                var requiredWeights = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var row in table.DataRows)
                {
                    Required(row, "stage_id", "quest_id", "weight_percent", "required", "show_in_stage_ui", "sort_order", "enabled");
                    var stage = row.Get("stage_id"); var quest = row.Get("quest_id");
                    if (!_enabledStageIds.Contains(stage)) Issue("StageQuests", row.RowNumber, "stage_id", stage, "stage_id must reference an enabled stage.");
                    if (!_storyIds.Contains(quest)) Issue("StageQuests", row.RowNumber, "quest_id", quest, "StageQuests may reference only StoryQuests.quest_id.");
                    if (!keys.Add(stage + "\n" + quest)) Issue("StageQuests", row.RowNumber, "quest_id", quest, "Duplicate stage_id + quest_id.");
                    NonNegativeInt(row, "weight_percent"); NonNegativeInt(row, "sort_order"); Bool(row, "required"); Bool(row, "show_in_stage_ui"); Bool(row, "enabled");
                    if (!ReadBool(row, "enabled", false)) continue;
                    var weight = ReadInt(row, "weight_percent");
                    if (ReadBool(row, "required", false)) requiredWeights[stage] = requiredWeights.TryGetValue(stage, out var total) ? total + weight : weight;
                    else if (weight != 0) Issue("StageQuests", row.RowNumber, "weight_percent", row.Get("weight_percent"), "Optional StageQuests must have weight_percent = 0.");
                }
                if (_tables.TryGetValue("Stages", out var stages))
                {
                    foreach (var row in stages.DataRows)
                    {
                        var id = row.Get("stage_id");
                        if (!ReadBool(row, "enabled", false) || string.IsNullOrWhiteSpace(row.Get("next_stage_id"))) continue;
                        if (!requiredWeights.TryGetValue(id, out var total) || total != 100)
                            Issue("StageQuests", 0, "weight_percent", id, "Enabled required StageQuests of each passable stage must total 100.");
                    }
                }
            }

            private void ValidateDefinitions()
            {
                foreach (var sheet in new[] { "StoryQuests", "DailyQuests" })
                {
                    if (!_tables.TryGetValue(sheet, out var table)) continue;
                    foreach (var row in table.DataRows)
                    {
                        Required(row, "name_id", "description_id", "icon_id", "journal_category", "sort_order", "enabled");
                        NonNegativeInt(row, "sort_order"); Bool(row, "enabled"); Enum(row, "journal_category", "QuestJournalCategory");
                        if (sheet == "StoryQuests")
                        {
                            Bool(row, "is_tutorial");
                            Bool(row, "close_on_stage_complete");
                            if (ReadBool(row, "close_on_stage_complete", false) && !HasEnabledStageQuest(row.Get("quest_id")))
                                Issue(sheet, row.RowNumber, "close_on_stage_complete", row.Get("close_on_stage_complete"), "close_on_stage_complete = TRUE requires at least one enabled StageQuests relation.");
                        }
                        else
                        {
                            NonNegativeInt(row, "selection_weight");
                            if (ReadBool(row, "enabled", false) && ReadInt(row, "selection_weight") <= 0)
                                Issue(sheet, row.RowNumber, "selection_weight", row.Get("selection_weight"), "Enabled DailyQuests require selection_weight > 0.");
                        }
                    }
                }
            }

            private bool HasEnabledStageQuest(string questId)
            {
                if (string.IsNullOrWhiteSpace(questId) || !_tables.TryGetValue("StageQuests", out var table)) return false;
                foreach (var row in table.DataRows)
                    if (string.Equals(row.Get("quest_id"), questId, StringComparison.Ordinal) && ReadBool(row, "enabled", false)) return true;
                return false;
            }

            private void ValidateConditions()
            {
                if (!_tables.TryGetValue("QuestStartConditions", out var table)) return;
                foreach (var row in table.DataRows)
                {
                    Required(row, "quest_id", "condition_group", "condition_type", "operator", "value", "sort_order");
                    DefinitionRef(row, "quest_id"); Enum(row, "condition_type", "ConditionType"); Enum(row, "operator", "CompareOperator");
                    NonNegativeInt(row, "value"); NonNegativeInt(row, "sort_order");
                    var type = row.Get("condition_type"); var target = row.Get("target_id");
                    if (type == "NewGame" && !string.IsNullOrWhiteSpace(target)) Issue("QuestStartConditions", row.RowNumber, "target_id", target, "NewGame requires empty target_id.");
                    if (type != "NewGame" && string.IsNullOrWhiteSpace(target)) Issue("QuestStartConditions", row.RowNumber, "target_id", target, $"{type} requires target_id.");
                    if (type == "QuestCompleted") DefinitionTarget(row);
                }
            }

            private void ValidateSteps()
            {
                if (!_tables.TryGetValue("QuestSteps", out var table)) return;
                var keys = new HashSet<string>(StringComparer.Ordinal);
                var required = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in table.DataRows)
                {
                    Required(row, "quest_id", "step_id", "step_order", "objective_type", "target_id", "operator", "target_value", "description_id", "required");
                    var quest = row.Get("quest_id"); DefinitionRef(row, "quest_id");
                    if (!keys.Add(quest + "\n" + row.Get("step_id"))) Issue("QuestSteps", row.RowNumber, "step_id", row.Get("step_id"), "Duplicate quest_id + step_id.");
                    Enum(row, "objective_type", "ObjectiveType"); Enum(row, "operator", "CompareOperator");
                    NonNegativeInt(row, "step_order"); NonNegativeInt(row, "target_value"); Bool(row, "required");
                    if (ReadBool(row, "required", false)) required.Add(quest);
                    if (row.Get("objective_type") == "QuestCompleted") DefinitionTarget(row);
                }
                foreach (var quest in _enabledDefinitionIds)
                    if (!required.Contains(quest)) Issue("QuestSteps", 0, "required", quest, "Every enabled quest definition must have at least one required step.");
            }

            private void ValidateRewards()
            {
                if (!_tables.TryGetValue("QuestRewards", out var table)) return;
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in table.DataRows)
                {
                    Required(row, "quest_id", "reward_id", "reward_type", "min", "max", "chance", "grant_moment", "sort_order");
                    DefinitionRef(row, "quest_id");
                    if (!keys.Add(row.Get("quest_id") + "\n" + row.Get("reward_id"))) Issue("QuestRewards", row.RowNumber, "reward_id", row.Get("reward_id"), "Duplicate quest_id + reward_id.");
                    if (!ActivityTypeParser.TryParseRewardType(row.Get("reward_type"), out _)) Issue("QuestRewards", row.RowNumber, "reward_type", row.Get("reward_type"), "Unknown RewardType registry value.");
                    NonNegativeInt(row, "min"); NonNegativeInt(row, "max"); NonNegativeInt(row, "sort_order"); DecimalRange(row, "chance", 0, 100); Enum(row, "grant_moment", "GrantMoment");
                    if (ReadInt(row, "min") > ReadInt(row, "max")) Issue("QuestRewards", row.RowNumber, "max", row.Get("max"), "max must be >= min.");
                }
            }

            private void ValidateStageTwo()
            {
                if (!_enabledStageIds.Contains("stage_2")) Issue("Stages", 0, "stage_id", "stage_2", "stage_2 must exist and be enabled.");
                if (_tables.TryGetValue("StageQuests", out var relations))
                    foreach (var row in relations.DataRows) if (row.Get("stage_id") == "stage_2") Issue("StageQuests", row.RowNumber, "stage_id", "stage_2", "stage_2 must not have StageQuests.");
            }

            private void ValidateStageCycles()
            {
                if (!_tables.TryGetValue("Stages", out var table)) return;
                var next = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var row in table.DataRows) if (ReadBool(row, "enabled", false) && !string.IsNullOrWhiteSpace(row.Get("next_stage_id"))) next[row.Get("stage_id")] = row.Get("next_stage_id");
                foreach (var start in next.Keys)
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal); var current = start;
                    while (next.TryGetValue(current, out current))
                        if (!seen.Add(current)) { Issue("Stages", 0, "next_stage_id", start, "next_stage_id graph contains a cycle."); break; }
                }
            }

            private void CollectIds(ConfigSheetTable table, string column, HashSet<string> ids, Action<ConfigSheetDataRow, string> onAdded)
            {
                foreach (var row in table.DataRows)
                {
                    var id = row.Get(column);
                    if (string.IsNullOrWhiteSpace(id)) { Issue(table.Name, row.RowNumber, column, id, $"{column} is required."); continue; }
                    if (!ids.Add(id)) Issue(table.Name, row.RowNumber, column, id, $"Duplicate {column}."); else onAdded?.Invoke(row, id);
                }
            }

            private void Required(ConfigSheetDataRow row, params string[] columns)
            {
                foreach (var column in columns) if (string.IsNullOrWhiteSpace(row.Get(column))) Issue(row.Table.Name, row.RowNumber, column, string.Empty, "Required value is missing.");
            }

            private void DefinitionRef(ConfigSheetDataRow row, string column)
            {
                var id = row.Get(column); if (!string.IsNullOrWhiteSpace(id) && !_definitionIds.Contains(id)) Issue(row.Table.Name, row.RowNumber, column, id, "Value must reference QuestDefinition.quest_id.");
            }

            private void DefinitionTarget(ConfigSheetDataRow row)
            {
                var id = row.Get("target_id");
                if (id.StartsWith("story:", StringComparison.Ordinal) || id.StartsWith("daily:", StringComparison.Ordinal) || !_definitionIds.Contains(id))
                    Issue(row.Table.Name, row.RowNumber, "target_id", id, "QuestCompleted target_id must reference QuestDefinition.quest_id, never an instance_id.");
            }

            private void Enum(ConfigSheetDataRow row, string column, string group)
            {
                var value = row.Get(column); if (!string.IsNullOrWhiteSpace(value) && (!_enums.TryGetValue(group, out var values) || !values.Contains(value))) Issue(row.Table.Name, row.RowNumber, column, value, $"Value is not listed in EnumValues group '{group}'.");
            }

            private void Bool(ConfigSheetDataRow row, string column)
            {
                var value = row.Get(column); if (!TryBool(value, out _)) Issue(row.Table.Name, row.RowNumber, column, value, "Expected TRUE or FALSE.");
            }

            private bool ReadBool(ConfigSheetDataRow row, string column, bool fallback) => TryBool(row.Get(column), out var value) ? value : fallback;
            private static bool TryBool(string value, out bool result)
            {
                if (string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase)) { result = true; return true; }
                if (string.Equals(value, "FALSE", StringComparison.OrdinalIgnoreCase)) { result = false; return true; }
                result = false; return false;
            }

            private void NonNegativeInt(ConfigSheetDataRow row, string column)
            {
                var value = row.Get(column); if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0) Issue(row.Table.Name, row.RowNumber, column, value, "Expected a non-negative integer.");
            }
            private int ReadInt(ConfigSheetDataRow row, string column) => int.TryParse(row.Get(column), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
            private void DecimalRange(ConfigSheetDataRow row, string column, double min, double max)
            {
                var raw = row.Get(column); if (!ConfigPipelineUtilities.TryParseFiniteNumber(raw, out var value) || value < min || value > max) Issue(row.Table.Name, row.RowNumber, column, raw, $"Expected a number from {min} to {max}.");
            }
            private object ConvertValue(string column, string raw)
            {
                if (BoolColumns.Contains(column) && TryBool(raw, out var boolean)) return boolean;
                if (IntegerColumns.Contains(column) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return integer;
                if (column == "chance" && ConfigPipelineUtilities.TryParseFiniteNumber(raw, out var number)) return number;
                return raw;
            }
            private void Issue(string sheet, int row, string column, string value, string message) => _report.Issues.Add(new ConfigValidationIssue(sheet, row, column, value, message));
        }
    }
}
