using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class BuildingsConfigsParser : IConfigPipelineParser
    {
        private const string ConfigId = "buildings_configs";
        private const string IndexSheet = "Index";
        private const string ReadmeSheet = "README";
        private const string BuildingActivitiesSheet = "BuildingActivities";
        private const string SettlementStagesSheet = "SettlementStages";
        private const string SettlementStageSlotsSheet = "SettlementStageSlots";
        private const string SettlementStageObjectivesSheet = "SettlementStageObjectives";
        private const string SettlementStageStarterHeroesSheet = "SettlementStageStarterHeroes";
        private const string SettlementStageStarterEquipmentSheet = "SettlementStageStarterEquipment";
        private const string CraftablesSheetPrefix = "Craftables -";
        private const string ForbiddenLegacyItemId = "item_gold";
        private const string GoldCurrencyId = "gold_id";
        private const string LevelPrefabColumn = "level_prefab_id";
        private const string LegacyLevelImageColumn = "level_image_id";

        private static readonly string[] IndexRequiredColumns =
        {
            "building_id",
            "name_id",
            "description_id",
            "small_icon_id",
            "levels",
            "unlocked_by_hall_level",
            "mvp_required",
            "start_level",
            "visible_at_start",
            "clickable_requirement"
        };

        private static readonly string[] LevelRequiredColumns =
        {
            "level",
            LevelPrefabColumn,
            "source_activity_id",
            "build_formula_id",
            "build_points_required",
            "skill_id",
            "fatigue_cost",
            "materials",
            "requirements_activities",
            "requirements_buildings",
            "requirements_skills",
            "skill_exp"
        };

        private static readonly string[] CraftablesRequiredColumns =
        {
            "building_id",
            "building_level",
            "craft_id",
            "sort_order",
            "ui_category",
            "enabled"
        };

        private static readonly string[] BuildingActivitiesRequiredColumns =
        {
            "building_id",
            "building_level",
            "activity_id",
            "sort_order",
            "show_if_activity_completed",
            "hide_if_activity_completed",
            "clickable_requirement",
            "enabled"
        };

        private static readonly string[] SettlementStagesRequiredColumns =
        {
            "stage_id",
            "name_id",
            "description_id",
            "stage_prefab_id",
            "target_duration_sec",
            "completion_rule",
            "next_stage_id",
            "sort_order",
            "enabled"
        };

        private static readonly string[] SettlementStageSlotsRequiredColumns =
        {
            "stage_id",
            "slot_id",
            "building_id",
            "sort_order",
            "enabled"
        };

        private static readonly string[] SettlementStageObjectivesRequiredColumns =
        {
            "stage_id",
            "quest_id",
            "weight_percent",
            "required",
            "sort_order"
        };

        private static readonly string[] SettlementStageStarterHeroesRequiredColumns =
        {
            "stage_id",
            "hero_id",
            "sort_order",
            "enabled"
        };

        private static readonly string[] SettlementStageStarterEquipmentRequiredColumns =
        {
            "stage_id",
            "hero_id",
            "item_id",
            "equipment_slot",
            "sort_order",
            "enabled"
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

            var context = new BuildingsConfigContext(download, report);
            context.ValidateIndex();
            context.ParseBuildingSheets();
            context.ValidateBuildingLevels();
            context.ValidateBuildingIndexLevelReferences();
            context.ValidateBuildingActivities();
            context.ValidateCraftables();
            context.ValidateSettlementStages();

            if (!report.Success)
                return report;

            runtimeJson = ConfigRuntimeJsonWriter.Write(context.BuildRuntimeArrays());
            return report;
        }

        private sealed class BuildingsConfigContext
        {
            private readonly ConfigSheetDownload _download;
            private readonly ConfigPipelineReport _report;
            private readonly Dictionary<string, ConfigDownloadedSheet> _sheets = new Dictionary<string, ConfigDownloadedSheet>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, ConfigSheetTable> _tables = new Dictionary<string, ConfigSheetTable>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, BuildingIndexRow> _buildings = new Dictionary<string, BuildingIndexRow>(StringComparer.OrdinalIgnoreCase);
            private readonly List<BuildingLevelRow> _levels = new List<BuildingLevelRow>();
            private readonly Dictionary<string, BuildingLevelRow> _levelKeys = new Dictionary<string, BuildingLevelRow>(StringComparer.OrdinalIgnoreCase);
            private readonly List<ConfigSheetTable> _craftableTables = new List<ConfigSheetTable>();
            private readonly List<ConfigSheetDataRow> _buildingActivities = new List<ConfigSheetDataRow>();
            private readonly List<ConfigSheetDataRow> _settlementStages = new List<ConfigSheetDataRow>();
            private readonly List<ConfigSheetDataRow> _settlementStageSlots = new List<ConfigSheetDataRow>();
            private readonly List<ConfigSheetDataRow> _settlementStageObjectives = new List<ConfigSheetDataRow>();
            private readonly List<ConfigSheetDataRow> _settlementStageStarterHeroes = new List<ConfigSheetDataRow>();
            private readonly List<ConfigSheetDataRow> _settlementStageStarterEquipment = new List<ConfigSheetDataRow>();
            private readonly HashSet<string> _stageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _enabledStageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public BuildingsConfigContext(ConfigSheetDownload download, ConfigPipelineReport report)
            {
                _download = download;
                _report = report;

                foreach (var sheet in download.sheets ?? Array.Empty<ConfigDownloadedSheet>())
                {
                    if (sheet == null || string.IsNullOrWhiteSpace(sheet.sheet_name))
                        continue;

                    _sheets[sheet.sheet_name] = sheet;
                    _tables[sheet.sheet_name] = new ConfigSheetTable(sheet);
                }
            }

            public void ValidateIndex()
            {
                if (!_tables.TryGetValue(IndexSheet, out var table))
                {
                    AddIssue(IndexSheet, 0, string.Empty, string.Empty, "Required sheet is missing.");
                    return;
                }

                if (table.Rows == 0)
                {
                    AddIssue(IndexSheet, 1, string.Empty, string.Empty, "Required sheet has no header row.");
                    return;
                }

                ValidateRequiredColumns(table, IndexRequiredColumns);
                if (!HasRequiredColumns(table, IndexRequiredColumns))
                    return;

                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    var buildingId = row.Get("building_id");
                    if (string.IsNullOrWhiteSpace(buildingId))
                    {
                        AddIssue(IndexSheet, row.RowNumber, "building_id", buildingId, "building_id is required.");
                        continue;
                    }

                    if (seen.TryGetValue(buildingId, out var firstRow))
                        AddIssue(IndexSheet, row.RowNumber, "building_id", buildingId, $"Duplicate building_id; first declared at row {firstRow}.");
                    else
                        seen[buildingId] = row.RowNumber;

                    ValidateRequired(row, "name_id");
                    ValidateRequired(row, "description_id");
                    ValidateRequired(row, "small_icon_id");
                    ValidateNumberGreaterThanOrEqual(row, "levels", 0d, "levels must be greater than or equal to 0.");
                    ValidateNumberGreaterThanOrEqual(row, "unlocked_by_hall_level", 0d, "unlocked_by_hall_level must be greater than or equal to 0.");
                    TryParseBool(row, "mvp_required", required: true, out var mvpRequired);
                    TryParseBool(row, "visible_at_start", required: true, out var visibleAtStart);

                    var levels = GetNumber(row, "levels");
                    var startLevel = 0L;
                    if (TryParseRequiredWholeNumber(row, "start_level", out startLevel))
                    {
                        if (startLevel < 0)
                            AddIssue(IndexSheet, row.RowNumber, "start_level", row.Get("start_level"), "start_level must be greater than or equal to 0.");
                        else if (startLevel > levels)
                            AddIssue(IndexSheet, row.RowNumber, "start_level", row.Get("start_level"), "start_level must not be greater than Index.levels.");
                    }

                    _buildings[buildingId] = new BuildingIndexRow(
                        buildingId,
                        row.Get("name_id"),
                        row.Get("description_id"),
                        row.Get("small_icon_id"),
                        levels,
                        GetNumber(row, "unlocked_by_hall_level"),
                        mvpRequired,
                        startLevel,
                        visibleAtStart,
                        row.Get("clickable_requirement"),
                        row.RowNumber);
                }
            }

            public void ParseBuildingSheets()
            {
                var sheetsByBuildingId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var sheet in _download.sheets ?? Array.Empty<ConfigDownloadedSheet>())
                {
                    if (sheet == null ||
                        string.IsNullOrWhiteSpace(sheet.sheet_name) ||
                        IsIgnoredSheet(sheet.sheet_name) ||
                        IsSettlementStageSheet(sheet.sheet_name) ||
                        IsCraftablesSheet(sheet.sheet_name))
                    {
                        continue;
                    }

                    var buildingSheet = new BuildingSheet(sheet);
                    if (string.IsNullOrWhiteSpace(buildingSheet.BuildingId))
                    {
                        AddIssue(sheet.sheet_name, 1, "building_id", string.Empty, "Building sheet must have a top key/value block with building_id.");
                        continue;
                    }

                    if (!_buildings.TryGetValue(buildingSheet.BuildingId, out var indexRow))
                    {
                        AddIssue(sheet.sheet_name, 1, "building_id", buildingSheet.BuildingId, "building_id does not exist in Index.building_id.");
                    }
                    else
                    {
                        ValidateTopBlockMatchesIndex(buildingSheet, indexRow);
                    }

                    if (sheetsByBuildingId.TryGetValue(buildingSheet.BuildingId, out var firstSheet))
                        AddIssue(sheet.sheet_name, 1, "building_id", buildingSheet.BuildingId, $"Duplicate building sheet for building_id; first declared in {firstSheet}.");
                    else
                        sheetsByBuildingId[buildingSheet.BuildingId] = sheet.sheet_name;

                    if (!string.IsNullOrWhiteSpace(buildingSheet.CraftablesSheet) &&
                        !_sheets.ContainsKey(buildingSheet.CraftablesSheet))
                    {
                        AddIssue(sheet.sheet_name, 1, "craftables_sheet", buildingSheet.CraftablesSheet, "Referenced craftables_sheet does not exist.");
                    }

                    ValidateForbiddenLegacyColumns(buildingSheet.Table);
                    ValidateRequiredColumns(buildingSheet.Table, LevelRequiredColumns);
                    foreach (var row in buildingSheet.Table.DataRows)
                        _levels.Add(new BuildingLevelRow(buildingSheet.BuildingId, row));
                }

                foreach (var building in _buildings.Values)
                {
                    if (!sheetsByBuildingId.ContainsKey(building.BuildingId))
                        AddIssue(IndexSheet, building.RowNumber, "building_id", building.BuildingId, "No building sheet found for this building_id.");
                }
            }

            public void ValidateBuildingLevels()
            {
                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var level in _levels)
                {
                    var row = level.Row;
                    var levelValue = row.Get("level");
                    if (string.IsNullOrWhiteSpace(level.BuildingId))
                        AddIssue(row.Table.Name, row.RowNumber, "building_id", level.BuildingId, "building_id is required.");

                    if (!TryParseRequiredWholeNumber(row, "level", out var parsedLevel) || parsedLevel < 0)
                    {
                        AddIssue(row.Table.Name, row.RowNumber, "level", levelValue, "level must be a number greater than or equal to 0.");
                    }
                    else
                    {
                        var key = LevelKey(level.BuildingId, parsedLevel);
                        if (seen.TryGetValue(key, out var firstRow))
                            AddIssue(row.Table.Name, row.RowNumber, "level", levelValue, $"Duplicate buildingId + level; first declared at row {firstRow}.");
                        else
                            seen[key] = row.RowNumber;

                        if (_buildings.TryGetValue(level.BuildingId, out var indexRow) && parsedLevel > indexRow.Levels)
                            AddIssue(row.Table.Name, row.RowNumber, "level", levelValue, "level must not be greater than Index.levels for this buildingId.");

                        _levelKeys[key] = level;
                    }

                    ValidatePrefabId(row, LevelPrefabColumn);
                    ValidateNumberGreaterThanOrEqual(row, "build_points_required", 0d, "build_points_required must be greater than or equal to 0.");
                    ValidateOptionalNumberGreaterThanOrEqual(row, "fatigue_cost", 0d, "fatigue_cost must be greater than or equal to 0.");
                    ValidateOptionalNumberGreaterThanOrEqual(row, "skill_exp", 0d, "skill_exp must be greater than or equal to 0.");
                    ValidatePackedRefs(row, "materials", "id", "count");
                    ValidateActivityRequirements(row);
                    ValidatePackedRefs(row, "requirements_buildings", "building_id", "level");
                    ValidatePackedRefs(row, "requirements_skills", "skill_id", "level");
                    ValidateActiveHeroLimit(level);
                    ValidateBuildSourceRules(level);
                }
            }

            public void ValidateBuildingIndexLevelReferences()
            {
                foreach (var building in _buildings.Values)
                {
                    if (!_levelKeys.ContainsKey(LevelKey(building.BuildingId, building.StartLevel)))
                        AddIssue(IndexSheet, building.RowNumber, "start_level", building.StartLevel.ToString(CultureInfo.InvariantCulture), "start_level does not exist in BuildingLevels for this building_id.");

                    ValidateOptionalBuildingLevelRef(IndexSheet, building.RowNumber, "clickable_requirement", building.ClickableRequirement);
                }
            }

            public void ValidateCraftables()
            {
                foreach (var sheet in _download.sheets ?? Array.Empty<ConfigDownloadedSheet>())
                {
                    if (sheet == null || !IsCraftablesSheet(sheet.sheet_name))
                        continue;

                    var table = new ConfigSheetTable(sheet);
                    _craftableTables.Add(table);

                    ValidateRequiredColumns(table, CraftablesRequiredColumns);
                    if (!HasRequiredColumns(table, CraftablesRequiredColumns))
                        continue;

                    var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var row in table.DataRows)
                    {
                        ValidateRequired(row, "building_id");
                        ValidateRequired(row, "craft_id");
                        ValidateRequired(row, "ui_category");
                        ValidateNumberGreaterThanOrEqual(row, "sort_order", 0d, "sort_order must be greater than or equal to 0.");
                        TryParseBool(row, "enabled", required: true, out _);

                        var buildingId = row.Get("building_id");
                        if (!string.IsNullOrWhiteSpace(buildingId) && !_buildings.ContainsKey(buildingId))
                            AddIssue(table.Name, row.RowNumber, "building_id", buildingId, "building_id does not exist in Index.building_id.");

                        if (TryParseRequiredWholeNumber(row, "building_level", out var buildingLevel))
                        {
                            if (buildingLevel < 0)
                                AddIssue(table.Name, row.RowNumber, "building_level", row.Get("building_level"), "building_level must be a number greater than or equal to 0.");

                            if (!string.IsNullOrWhiteSpace(buildingId) &&
                                !_levelKeys.ContainsKey(LevelKey(buildingId, buildingLevel)))
                            {
                                AddIssue(table.Name, row.RowNumber, "building_level", row.Get("building_level"), "building_level does not exist in BuildingLevels for this building_id.");
                            }
                        }

                        var craftId = row.Get("craft_id");
                        var key = $"{buildingId}\n{row.Get("building_level")}\n{craftId}";
                        if (seen.TryGetValue(key, out var firstRow))
                            AddIssue(table.Name, row.RowNumber, "craft_id", craftId, $"Duplicate building_id + building_level + craft_id; first declared at row {firstRow}.");
                        else
                            seen[key] = row.RowNumber;
                    }
                }
            }

            public void ValidateBuildingActivities()
            {
                if (!_tables.TryGetValue(BuildingActivitiesSheet, out var table))
                {
                    AddIssue(BuildingActivitiesSheet, 0, string.Empty, string.Empty, "Required sheet is missing.");
                    return;
                }

                ValidateRequiredColumns(table, BuildingActivitiesRequiredColumns);
                if (!HasRequiredColumns(table, BuildingActivitiesRequiredColumns))
                    return;

                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    if (!TryParseBool(row, "enabled", required: true, out var enabled) || !enabled)
                        continue;

                    ValidateRequired(row, "building_id");
                    ValidateRequired(row, "activity_id");
                    ValidateNumberGreaterThanOrEqual(row, "sort_order", 0d, "sort_order must be greater than or equal to 0.");

                    var buildingId = row.Get("building_id");
                    if (!string.IsNullOrWhiteSpace(buildingId) && !_buildings.ContainsKey(buildingId))
                        AddIssue(table.Name, row.RowNumber, "building_id", buildingId, "building_id does not exist in Index.building_id.");

                    if (TryParseRequiredWholeNumber(row, "building_level", out var buildingLevel))
                    {
                        if (buildingLevel < 0)
                            AddIssue(table.Name, row.RowNumber, "building_level", row.Get("building_level"), "building_level must be a number greater than or equal to 0.");

                        if (!string.IsNullOrWhiteSpace(buildingId) &&
                            !_levelKeys.ContainsKey(LevelKey(buildingId, buildingLevel)))
                        {
                            AddIssue(table.Name, row.RowNumber, "building_level", row.Get("building_level"), "building_level does not exist in BuildingLevels for this building_id.");
                        }
                    }

                    ValidateOptionalBuildingLevelRef(table.Name, row.RowNumber, "clickable_requirement", row.Get("clickable_requirement"));

                    var key = $"{buildingId}\n{row.Get("building_level")}\n{row.Get("activity_id")}";
                    if (seen.TryGetValue(key, out var firstRow))
                        AddIssue(table.Name, row.RowNumber, "activity_id", row.Get("activity_id"), $"Duplicate building_id + building_level + activity_id; first declared at row {firstRow}.");
                    else
                        seen[key] = row.RowNumber;

                    _buildingActivities.Add(row);
                }
            }

            public void ValidateSettlementStages()
            {
                ValidateSettlementStageSlotsTable();
                ValidateSettlementStageStarterHeroesTable();
                ValidateSettlementStageStarterEquipmentTable();
            }

            public Dictionary<string, List<Dictionary<string, object>>> BuildRuntimeArrays()
            {
                return new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.Ordinal)
                {
                    ["buildings"] = BuildBuildings(),
                    ["buildingLevels"] = BuildBuildingLevels(),
                    ["buildActions"] = BuildBuildActions(),
                    ["buildingActivities"] = BuildBuildingActivities(),
                    ["buildingCraftables"] = BuildBuildingCraftables(),
                    ["settlementStageSlots"] = BuildSettlementStageSlots(),
                    ["settlementStageStarterHeroes"] = BuildSettlementStageStarterHeroes(),
                    ["settlementStageStarterEquipment"] = BuildSettlementStageStarterEquipment()
                };
            }

            private List<Dictionary<string, object>> BuildBuildings()
            {
                var rows = new List<Dictionary<string, object>>();
                foreach (var building in _buildings.Values)
                {
                    rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["buildingId"] = building.BuildingId,
                        ["nameId"] = building.NameId,
                        ["descriptionId"] = building.DescriptionId,
                        ["smallIconId"] = building.SmallIconId,
                        ["levels"] = building.Levels,
                        ["unlockedByHallLevel"] = building.UnlockedByHallLevel,
                        ["mvpRequired"] = building.MvpRequired,
                        ["startLevel"] = building.StartLevel,
                        ["visibleAtStart"] = building.VisibleAtStart,
                        ["clickableRequirement"] = building.ClickableRequirement
                    });
                }

                return rows;
            }

            private List<Dictionary<string, object>> BuildSettlementStages()
            {
                var rows = new List<Dictionary<string, object>>();
                foreach (var row in _settlementStages)
                {
                    rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["stageId"] = row.Get("stage_id"),
                        ["nameId"] = row.Get("name_id"),
                        ["descriptionId"] = row.Get("description_id"),
                        ["stagePrefabId"] = row.Get("stage_prefab_id"),
                        ["targetDurationSec"] = GetNumber(row, "target_duration_sec"),
                        ["completionRule"] = row.Get("completion_rule"),
                        ["nextStageId"] = row.Get("next_stage_id"),
                        ["sortOrder"] = GetNumber(row, "sort_order"),
                        ["enabled"] = GetBool(row, "enabled")
                    });
                }

                return rows;
            }

            private List<Dictionary<string, object>> BuildSettlementStageSlots()
            {
                var rows = new List<Dictionary<string, object>>();
                foreach (var row in _settlementStageSlots)
                {
                    rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["stageId"] = row.Get("stage_id"),
                        ["slotId"] = row.Get("slot_id"),
                        ["buildingId"] = row.Get("building_id"),
                        ["sortOrder"] = GetNumber(row, "sort_order"),
                        ["enabled"] = GetBool(row, "enabled")
                    });
                }

                return rows;
            }

            private List<Dictionary<string, object>> BuildSettlementStageObjectives()
            {
                var rows = new List<Dictionary<string, object>>();
                foreach (var row in _settlementStageObjectives)
                {
                    rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["stageId"] = row.Get("stage_id"),
                        ["questId"] = row.Get("quest_id"),
                        ["weightPercent"] = GetNumber(row, "weight_percent"),
                        ["required"] = GetBool(row, "required"),
                        ["sortOrder"] = GetNumber(row, "sort_order")
                    });
                }

                return rows;
            }

            private List<Dictionary<string, object>> BuildSettlementStageStarterHeroes()
            {
                var rows = new List<Dictionary<string, object>>();
                foreach (var row in _settlementStageStarterHeroes)
                {
                    rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["stageId"] = row.Get("stage_id"),
                        ["heroId"] = row.Get("hero_id"),
                        ["sortOrder"] = GetNumber(row, "sort_order")
                    });
                }

                return rows;
            }

            private List<Dictionary<string, object>> BuildSettlementStageStarterEquipment()
            {
                var rows = new List<Dictionary<string, object>>();
                foreach (var row in _settlementStageStarterEquipment)
                {
                    rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["stageId"] = row.Get("stage_id"),
                        ["heroId"] = row.Get("hero_id"),
                        ["itemId"] = row.Get("item_id"),
                        ["equipmentSlot"] = row.Get("equipment_slot"),
                        ["sortOrder"] = GetNumber(row, "sort_order")
                    });
                }

                return rows;
            }

            private void ValidateSettlementStagesTable()
            {
                if (!_tables.TryGetValue(SettlementStagesSheet, out var table))
                {
                    AddIssue(SettlementStagesSheet, 0, string.Empty, string.Empty, "Required sheet is missing.");
                    return;
                }

                ValidateRequiredColumns(table, SettlementStagesRequiredColumns);
                if (!HasRequiredColumns(table, SettlementStagesRequiredColumns))
                    return;

                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    var stageId = row.Get("stage_id");
                    if (string.IsNullOrWhiteSpace(stageId))
                    {
                        AddIssue(SettlementStagesSheet, row.RowNumber, "stage_id", stageId, "stage_id is required.");
                    }
                    else if (seen.TryGetValue(stageId, out var firstRow))
                    {
                        AddIssue(SettlementStagesSheet, row.RowNumber, "stage_id", stageId, $"Duplicate stage_id; first declared at row {firstRow}.");
                    }
                    else
                    {
                        seen[stageId] = row.RowNumber;
                        _stageIds.Add(stageId);
                    }

                    ValidateRequired(row, "name_id");
                    ValidateRequired(row, "description_id");
                    ValidateRequired(row, "stage_prefab_id");
                    ValidateNumberGreaterThanOrEqual(row, "target_duration_sec", 0d, "target_duration_sec must be greater than or equal to 0.");
                    ValidateNumberGreaterThanOrEqual(row, "sort_order", 0d, "sort_order must be greater than or equal to 0.");
                    TryParseBool(row, "enabled", required: true, out var enabled);

                    var completionRule = row.Get("completion_rule");
                    if (string.IsNullOrWhiteSpace(completionRule))
                        AddIssue(SettlementStagesSheet, row.RowNumber, "completion_rule", completionRule, "completion_rule is required.");
                    else if (!string.Equals(completionRule, "AllRequired", StringComparison.OrdinalIgnoreCase))
                        AddIssue(SettlementStagesSheet, row.RowNumber, "completion_rule", completionRule, "completion_rule must be AllRequired for Stage 1 MVP.");

                    if (enabled)
                    {
                        if (!string.IsNullOrWhiteSpace(stageId))
                            _enabledStageIds.Add(stageId);

                        _settlementStages.Add(row);
                    }
                }

                foreach (var row in _settlementStages)
                {
                    var nextStageId = row.Get("next_stage_id");
                    if (!string.IsNullOrWhiteSpace(nextStageId) && !_enabledStageIds.Contains(nextStageId))
                        AddIssue(SettlementStagesSheet, row.RowNumber, "next_stage_id", nextStageId, "next_stage_id references missing enabled SettlementStages.stage_id.");
                }
            }

            private void ValidateSettlementStageSlotsTable()
            {
                if (!_tables.TryGetValue(SettlementStageSlotsSheet, out var table))
                {
                    AddIssue(SettlementStageSlotsSheet, 0, string.Empty, string.Empty, "Required sheet is missing.");
                    return;
                }

                ValidateRequiredColumns(table, SettlementStageSlotsRequiredColumns);
                if (!HasRequiredColumns(table, SettlementStageSlotsRequiredColumns))
                    return;

                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    if (!TryParseBool(row, "enabled", required: true, out var enabled) || !enabled)
                        continue;

                    var stageId = row.Get("stage_id");
                    var slotId = row.Get("slot_id");
                    var buildingId = row.Get("building_id");

                    ValidateRequired(row, "stage_id");
                    ValidateRequired(row, "slot_id");
                    ValidateRequired(row, "building_id");
                    ValidateNumberGreaterThanOrEqual(row, "sort_order", 0d, "sort_order must be greater than or equal to 0.");

                    if (!string.IsNullOrWhiteSpace(buildingId) && !_buildings.ContainsKey(buildingId))
                        AddIssue(SettlementStageSlotsSheet, row.RowNumber, "building_id", buildingId, "building_id does not exist in Index.building_id.");

                    var key = $"{stageId}\n{slotId}";
                    if (seen.TryGetValue(key, out var firstRow))
                        AddIssue(SettlementStageSlotsSheet, row.RowNumber, "slot_id", slotId, $"Duplicate stage_id + slot_id; first declared at row {firstRow}.");
                    else
                        seen[key] = row.RowNumber;

                    _settlementStageSlots.Add(row);
                }
            }

            private void ValidateSettlementStageObjectivesTable()
            {
                if (!_tables.TryGetValue(SettlementStageObjectivesSheet, out var table))
                {
                    AddIssue(SettlementStageObjectivesSheet, 0, string.Empty, string.Empty, "Required sheet is missing.");
                    return;
                }

                ValidateRequiredColumns(table, SettlementStageObjectivesRequiredColumns);
                if (!HasRequiredColumns(table, SettlementStageObjectivesRequiredColumns))
                    return;

                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var requiredWeights = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    var stageId = row.Get("stage_id");
                    var questId = row.Get("quest_id");
                    ValidateRequired(row, "stage_id");
                    ValidateRequired(row, "quest_id");
                    ValidateNumberGreaterThanOrEqual(row, "weight_percent", 0d, "weight_percent must be greater than or equal to 0.");
                    ValidateNumberGreaterThanOrEqual(row, "sort_order", 0d, "sort_order must be greater than or equal to 0.");
                    TryParseBool(row, "required", required: true, out var required);

                    if (!string.IsNullOrWhiteSpace(stageId) && !_enabledStageIds.Contains(stageId))
                        AddIssue(SettlementStageObjectivesSheet, row.RowNumber, "stage_id", stageId, "stage_id references missing enabled SettlementStages.stage_id.");

                    var key = $"{stageId}\n{questId}";
                    if (seen.TryGetValue(key, out var firstRow))
                        AddIssue(SettlementStageObjectivesSheet, row.RowNumber, "quest_id", questId, $"Duplicate stage_id + quest_id; first declared at row {firstRow}.");
                    else
                        seen[key] = row.RowNumber;

                    if (required && long.TryParse(row.Get("weight_percent"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight))
                    {
                        requiredWeights.TryGetValue(stageId, out var total);
                        requiredWeights[stageId] = total + weight;
                    }

                    _settlementStageObjectives.Add(row);
                }

                foreach (var pair in requiredWeights)
                {
                    if (pair.Value != 100L)
                        AddIssue(SettlementStageObjectivesSheet, 0, "weight_percent", pair.Key, "Required objective weight_percent total must be 100 for each stage.");
                }
            }

            private void ValidateSettlementStageStarterHeroesTable()
            {
                if (!_tables.TryGetValue(SettlementStageStarterHeroesSheet, out var table))
                {
                    AddIssue(SettlementStageStarterHeroesSheet, 0, string.Empty, string.Empty, "Required sheet is missing.");
                    return;
                }

                ValidateRequiredColumns(table, SettlementStageStarterHeroesRequiredColumns);
                if (!HasRequiredColumns(table, SettlementStageStarterHeroesRequiredColumns))
                    return;

                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    if (!TryParseBool(row, "enabled", required: true, out var enabled) || !enabled)
                        continue;

                    var stageId = row.Get("stage_id");
                    var heroId = row.Get("hero_id");
                    ValidateRequired(row, "stage_id");
                    ValidateRequired(row, "hero_id");
                    ValidateNumberGreaterThanOrEqual(row, "sort_order", 0d, "sort_order must be greater than or equal to 0.");

                    var key = $"{stageId}\n{heroId}";
                    if (seen.TryGetValue(key, out var firstRow))
                        AddIssue(SettlementStageStarterHeroesSheet, row.RowNumber, "hero_id", heroId, $"Duplicate stage_id + hero_id; first declared at row {firstRow}.");
                    else
                        seen[key] = row.RowNumber;

                    _settlementStageStarterHeroes.Add(row);
                }
            }

            private void ValidateSettlementStageStarterEquipmentTable()
            {
                if (!_tables.TryGetValue(SettlementStageStarterEquipmentSheet, out var table))
                {
                    AddIssue(SettlementStageStarterEquipmentSheet, 0, string.Empty, string.Empty, "Required sheet is missing.");
                    return;
                }

                ValidateRequiredColumns(table, SettlementStageStarterEquipmentRequiredColumns);
                if (!HasRequiredColumns(table, SettlementStageStarterEquipmentRequiredColumns))
                    return;

                var starterHeroKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var starterHero in _settlementStageStarterHeroes)
                    starterHeroKeys.Add($"{starterHero.Get("stage_id")}\n{starterHero.Get("hero_id")}");

                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    if (!TryParseBool(row, "enabled", required: true, out var enabled) || !enabled)
                        continue;

                    var stageId = row.Get("stage_id");
                    var heroId = row.Get("hero_id");
                    var itemId = row.Get("item_id");
                    var equipmentSlot = row.Get("equipment_slot");
                    ValidateRequired(row, "stage_id");
                    ValidateRequired(row, "hero_id");
                    ValidateRequired(row, "item_id");
                    ValidateRequired(row, "equipment_slot");
                    ValidateNumberGreaterThanOrEqual(row, "sort_order", 0d, "sort_order must be greater than or equal to 0.");

                    if (!string.IsNullOrWhiteSpace(stageId) && !string.IsNullOrWhiteSpace(heroId) &&
                        !starterHeroKeys.Contains($"{stageId}\n{heroId}"))
                    {
                        AddIssue(SettlementStageStarterEquipmentSheet, row.RowNumber, "hero_id", heroId, "hero_id must be enabled in SettlementStageStarterHeroes for the same stage_id.");
                    }

                    var key = $"{stageId}\n{heroId}\n{equipmentSlot}";
                    if (seen.TryGetValue(key, out var firstRow))
                        AddIssue(SettlementStageStarterEquipmentSheet, row.RowNumber, "equipment_slot", equipmentSlot, $"Duplicate stage_id + hero_id + equipment_slot; first declared at row {firstRow}.");
                    else
                        seen[key] = row.RowNumber;

                    _settlementStageStarterEquipment.Add(row);
                }
            }

            private void ValidateStage2IsEmpty()
            {
                if (!_stageIds.Contains("stage_2"))
                {
                    AddIssue(SettlementStagesSheet, 0, "stage_id", "stage_2", "stage_2 is required.");
                    return;
                }

                if (!_enabledStageIds.Contains("stage_2"))
                {
                    AddIssue(SettlementStagesSheet, 0, "enabled", "stage_2", "stage_2 must be enabled.");
                    return;
                }

                foreach (var row in _settlementStageSlots)
                {
                    if (string.Equals(row.Get("stage_id"), "stage_2", StringComparison.OrdinalIgnoreCase))
                        AddIssue(SettlementStageSlotsSheet, row.RowNumber, "stage_id", "stage_2", "stage_2 must not have slots.");
                }

                foreach (var row in _settlementStageObjectives)
                {
                    if (string.Equals(row.Get("stage_id"), "stage_2", StringComparison.OrdinalIgnoreCase))
                        AddIssue(SettlementStageObjectivesSheet, row.RowNumber, "stage_id", "stage_2", "stage_2 must not have objectives.");
                }

                foreach (var row in _settlementStageStarterHeroes)
                {
                    if (string.Equals(row.Get("stage_id"), "stage_2", StringComparison.OrdinalIgnoreCase))
                        AddIssue(SettlementStageStarterHeroesSheet, row.RowNumber, "stage_id", "stage_2", "stage_2 must not have starter heroes.");
                }

                foreach (var row in _settlementStageStarterEquipment)
                {
                    if (string.Equals(row.Get("stage_id"), "stage_2", StringComparison.OrdinalIgnoreCase))
                        AddIssue(SettlementStageStarterEquipmentSheet, row.RowNumber, "stage_id", "stage_2", "stage_2 must not have starter equipment.");
                }
            }

            private List<Dictionary<string, object>> BuildBuildingLevels()
            {
                var rows = new List<Dictionary<string, object>>();
                foreach (var level in _levels)
                {
                    var row = level.Row;
                    rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["buildingId"] = level.BuildingId,
                        ["level"] = GetNumber(row, "level"),
                        ["levelPrefabId"] = row.Get(LevelPrefabColumn),
                        ["sourceActivityId"] = row.Get("source_activity_id"),
                        ["buildFormulaId"] = row.Get("build_formula_id"),
                        ["buildPointsRequired"] = GetNumber(row, "build_points_required"),
                        ["skillId"] = row.Get("skill_id"),
                        ["fatigueCost"] = GetNumber(row, "fatigue_cost"),
                        ["materials"] = ParseMaterials(row.Get("materials")),
                        ["requirementsActivities"] = ParseActivityRequirements(row.Get("requirements_activities")),
                        ["requirementsBuildings"] = ParseBuildingRequirements(row.Get("requirements_buildings")),
                        ["requirementsSkills"] = ParseSkillRequirements(row.Get("requirements_skills")),
                        ["skillExp"] = GetNumber(row, "skill_exp"),
                        ["activeHeroLimit"] = GetNumber(row, "active_hero_limit")
                    });
                }

                return rows;
            }

            private List<Dictionary<string, object>> BuildBuildActions()
            {
                var rows = new List<Dictionary<string, object>>();
                foreach (var level in _levels)
                {
                    if (!CreatesBuildAction(level))
                        continue;

                    var row = level.Row;
                    rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["id"] = row.Get("source_activity_id"),
                        ["type"] = "Build",
                        ["targetBuildingId"] = level.BuildingId,
                        ["targetLevel"] = GetNumber(row, "level"),
                        ["buildFormulaId"] = row.Get("build_formula_id"),
                        ["buildPointsRequired"] = GetNumber(row, "build_points_required"),
                        ["skillId"] = row.Get("skill_id"),
                        ["fatigueCost"] = GetNumber(row, "fatigue_cost"),
                        ["materials"] = ParseMaterials(row.Get("materials")),
                        ["requirementsActivities"] = ParseActivityRequirements(row.Get("requirements_activities")),
                        ["requirementsBuildings"] = ParseBuildingRequirements(row.Get("requirements_buildings")),
                        ["requirementsSkills"] = ParseSkillRequirements(row.Get("requirements_skills")),
                        ["skillExp"] = GetNumber(row, "skill_exp")
                    });
                }

                return rows;
            }

            private List<Dictionary<string, object>> BuildBuildingActivities()
            {
                var rows = new List<Dictionary<string, object>>();
                foreach (var row in _buildingActivities)
                {
                    rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["buildingId"] = row.Get("building_id"),
                        ["buildingLevel"] = GetNumber(row, "building_level"),
                        ["activityId"] = row.Get("activity_id"),
                        ["sortOrder"] = GetNumber(row, "sort_order"),
                        ["showIfActivityCompleted"] = row.Get("show_if_activity_completed"),
                        ["hideIfActivityCompleted"] = row.Get("hide_if_activity_completed"),
                        ["clickableRequirement"] = row.Get("clickable_requirement")
                    });
                }

                return rows;
            }

            private List<Dictionary<string, object>> BuildBuildingCraftables()
            {
                var rows = new List<Dictionary<string, object>>();
                foreach (var table in _craftableTables)
                {
                    foreach (var row in table.DataRows)
                    {
                        rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["buildingId"] = row.Get("building_id"),
                            ["buildingLevel"] = GetNumber(row, "building_level"),
                            ["craftId"] = row.Get("craft_id"),
                            ["sortOrder"] = GetNumber(row, "sort_order"),
                            ["uiCategory"] = row.Get("ui_category"),
                            ["enabled"] = GetBool(row, "enabled")
                        });
                    }
                }

                return rows;
            }

            private void ValidateTopBlockMatchesIndex(BuildingSheet sheet, BuildingIndexRow indexRow)
            {
                ValidateMatchingMetadata(sheet, "name_id", sheet.NameId, indexRow.NameId);
                ValidateMatchingMetadata(sheet, "description_id", sheet.DescriptionId, indexRow.DescriptionId);
                ValidateMatchingMetadata(sheet, "small_icon_id", sheet.SmallIconId, indexRow.SmallIconId);
            }

            private void ValidateMatchingMetadata(BuildingSheet sheet, string column, string actual, string expected)
            {
                if (string.IsNullOrWhiteSpace(actual))
                    return;

                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    AddIssue(sheet.Table.Name, 1, column, actual, $"Top block {column} contradicts Index for this building_id.");
            }

            private void ValidateBuildSourceRules(BuildingLevelRow level)
            {
                var row = level.Row;
                var sourceActivityId = row.Get("source_activity_id");
                if (!HasBuildIntent(row))
                {
                    ValidateNonBuildFields(row);
                    return;
                }

                if (!IsBuildSource(sourceActivityId))
                    AddIssue(row.Table.Name, row.RowNumber, "source_activity_id", sourceActivityId, "source_activity_id must start with build_ for a declared build action.");

                var buildFormulaId = row.Get("build_formula_id");
                if (string.IsNullOrWhiteSpace(buildFormulaId))
                    AddIssue(row.Table.Name, row.RowNumber, "build_formula_id", buildFormulaId, "build_formula_id is required for a declared build action.");
                else if (ConfigPipelineUtilities.TryParseNumber(buildFormulaId, out var numericFormulaId) && Math.Abs(numericFormulaId) < 0.0000001d)
                    AddIssue(row.Table.Name, row.RowNumber, "build_formula_id", buildFormulaId, "build_formula_id value 0 is not a valid formula reference.");

                if (!TryParseNumber(row, "build_points_required", out var buildPoints) || buildPoints <= 0d)
                    AddIssue(row.Table.Name, row.RowNumber, "build_points_required", row.Get("build_points_required"), "build_points_required must be greater than 0 for a declared build action.");

                if (string.IsNullOrWhiteSpace(row.Get("skill_id")))
                    AddIssue(row.Table.Name, row.RowNumber, "skill_id", row.Get("skill_id"), "skill_id is required for a declared build action.");

                if (!TryParseNumber(row, "fatigue_cost", out var fatigueCost) || fatigueCost < 0d)
                    AddIssue(row.Table.Name, row.RowNumber, "fatigue_cost", row.Get("fatigue_cost"), "fatigue_cost must be a number greater than or equal to 0 for a declared build action.");
            }

            private void ValidateNonBuildFields(ConfigSheetDataRow row)
            {
                foreach (var column in new[] { "build_formula_id", "materials", "fatigue_cost" })
                {
                    var value = row.Get(column);
                    if (!string.IsNullOrWhiteSpace(value))
                        AddIssue(row.Table.Name, row.RowNumber, column, value, $"{column} must be empty for a non-build level.");
                }
            }

            private void ValidateActiveHeroLimit(BuildingLevelRow level)
            {
                var row = level.Row;
                var raw = row.Get("active_hero_limit");
                if (string.IsNullOrWhiteSpace(raw))
                {
                    if (string.Equals(level.BuildingId, "building_hall", StringComparison.OrdinalIgnoreCase))
                        AddIssue(row.Table.Name, row.RowNumber, "active_hero_limit", raw, "active_hero_limit is required for active building_hall levels.");

                    return;
                }

                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit))
                {
                    AddIssue(row.Table.Name, row.RowNumber, "active_hero_limit", raw, "active_hero_limit must be an integer greater than or equal to 0.");
                    return;
                }

                if (limit < 0)
                {
                    AddIssue(row.Table.Name, row.RowNumber, "active_hero_limit", raw, "active_hero_limit must be greater than or equal to 0.");
                    return;
                }

                if (string.Equals(level.BuildingId, "building_hall", StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(row.Get("level"), "0", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(row.Get("level"), "1", StringComparison.OrdinalIgnoreCase)) &&
                    limit != 1)
                {
                    AddIssue(row.Table.Name, row.RowNumber, "active_hero_limit", raw, "building_hall level 0 and 1 must have active_hero_limit = 1 for Stage 1.");
                }
            }

            private bool CreatesBuildAction(BuildingLevelRow level)
            {
                return HasBuildIntent(level.Row);
            }

            private bool HasBuildIntent(ConfigSheetDataRow row)
            {
                if (IsBuildSource(row.Get("source_activity_id")) ||
                    !string.IsNullOrWhiteSpace(row.Get("build_formula_id")) ||
                    !string.IsNullOrWhiteSpace(row.Get("skill_id")) ||
                    !string.IsNullOrWhiteSpace(row.Get("materials")))
                {
                    return true;
                }

                if (TryParseNumber(row, "build_points_required", out var buildPoints) && buildPoints > 0d)
                    return true;

                if (TryParseNumber(row, "fatigue_cost", out var fatigueCost) && fatigueCost > 0d)
                    return true;

                return TryParseNumber(row, "skill_exp", out var skillExp) && skillExp > 0d;
            }

            private void ValidatePackedRefs(ConfigSheetDataRow row, string column, string idName, string countName)
            {
                var raw = row.Get(column);
                if (string.IsNullOrWhiteSpace(raw))
                    return;

                var refs = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var packedRef in refs)
                {
                    var parts = packedRef.Split(':');
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                    {
                        AddIssue(row.Table.Name, row.RowNumber, column, packedRef.Trim(), $"Expected {column} format {idName}:{countName}; {idName}:{countName}.");
                        continue;
                    }

                    var id = parts[0].Trim();
                    var count = parts[1].Trim();
                    if (string.Equals(id, ForbiddenLegacyItemId, StringComparison.OrdinalIgnoreCase))
                        AddIssue(row.Table.Name, row.RowNumber, column, id, "item_gold is a forbidden legacy id.");

                    if (string.Equals(column, "materials", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(id, GoldCurrencyId, StringComparison.OrdinalIgnoreCase))
                    {
                        AddIssue(row.Table.Name, row.RowNumber, column, id, "gold_id is a currency_id and must not be used as a material reference.");
                    }

                    if (!long.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        AddIssue(row.Table.Name, row.RowNumber, column, packedRef.Trim(), $"{countName} in packed reference must be an integer.");
                        continue;
                    }

                    var minimum = string.Equals(column, "requirements_buildings", StringComparison.OrdinalIgnoreCase) ? 0L : 1L;
                    if (parsed < minimum)
                    {
                        var message = minimum == 0L
                            ? $"{countName} in packed reference must be an integer greater than or equal to 0."
                            : $"{countName} in packed reference must be an integer greater than 0.";
                        AddIssue(row.Table.Name, row.RowNumber, column, packedRef.Trim(), message);
                    }
                }
            }

            private void ValidateActivityRequirements(ConfigSheetDataRow row)
            {
                var raw = row.Get("requirements_activities");
                if (string.IsNullOrWhiteSpace(raw))
                    return;

                var refs = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var activityRef in refs)
                {
                    var trimmed = activityRef.Trim();
                    var parts = trimmed.Split(':');
                    if (parts.Length == 1 && !string.IsNullOrWhiteSpace(parts[0]))
                        continue;

                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                    {
                        AddIssue(row.Table.Name, row.RowNumber, "requirements_activities", trimmed, "Expected requirements_activities format activity_id or activity_id:count; separate multiple values with semicolons.");
                        continue;
                    }

                    if (!long.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count <= 0L)
                    {
                        AddIssue(row.Table.Name, row.RowNumber, "requirements_activities", trimmed, "count in activity requirement must be an integer greater than 0.");
                    }
                }
            }

            private void ValidateRequiredColumns(ConfigSheetTable table, string[] requiredColumns)
            {
                foreach (var column in requiredColumns)
                {
                    if (!table.HasColumn(column))
                        AddIssue(table.Name, 1, column, string.Empty, "Required column is missing.");
                }
            }

            private void ValidateForbiddenLegacyColumns(ConfigSheetTable table)
            {
                if (table.HasColumn(LegacyLevelImageColumn))
                    AddIssue(table.Name, 1, LegacyLevelImageColumn, string.Empty, "level_image_id is deprecated; use level_prefab_id.");
            }

            private static bool HasRequiredColumns(ConfigSheetTable table, string[] requiredColumns)
            {
                foreach (var column in requiredColumns)
                {
                    if (!table.HasColumn(column))
                        return false;
                }

                return true;
            }

            private void ValidateRequired(ConfigSheetDataRow row, string column)
            {
                var value = row.Get(column);
                if (string.IsNullOrWhiteSpace(value))
                    AddIssue(row.Table.Name, row.RowNumber, column, value, $"{column} is required.");
            }

            private void ValidatePrefabId(ConfigSheetDataRow row, string column)
            {
                if (!row.Table.HasColumn(column))
                    return;

                var value = row.Get(column);
                if (string.IsNullOrWhiteSpace(value))
                {
                    AddIssue(row.Table.Name, row.RowNumber, column, value, $"{column} is required.");
                    return;
                }

                if (value.EndsWith("_prefab_id", StringComparison.OrdinalIgnoreCase))
                    AddIssue(row.Table.Name, row.RowNumber, column, value, $"{column} must reference a prefab asset id and must not end with _prefab_id.");
            }

            private void ValidateOptionalBuildingLevelRef(string sheet, int rowNumber, string column, string raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    return;

                var parts = raw.Split(':');
                if (parts.Length != 2 ||
                    string.IsNullOrWhiteSpace(parts[0]) ||
                    string.IsNullOrWhiteSpace(parts[1]) ||
                    !long.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) ||
                    level < 0)
                {
                    AddIssue(sheet, rowNumber, column, raw, $"{column} must use building_id:level with level greater than or equal to 0.");
                    return;
                }

                var buildingId = parts[0].Trim();
                if (!_levelKeys.ContainsKey(LevelKey(buildingId, level)))
                    AddIssue(sheet, rowNumber, column, raw, $"{column} references missing Buildings Configs building_id:level.");
            }

            private void ValidateNumberGreaterThan(ConfigSheetDataRow row, string column, double minimum, string message)
            {
                var raw = row.Get(column);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    AddIssue(row.Table.Name, row.RowNumber, column, raw, message);
                    return;
                }

                if (!TryParseFiniteNumber(raw, out var value))
                {
                    AddIssue(row.Table.Name, row.RowNumber, column, raw, "Expected a number.");
                    return;
                }

                if (value <= minimum)
                    AddIssue(row.Table.Name, row.RowNumber, column, raw, message);
            }

            private void ValidateNumberGreaterThanOrEqual(ConfigSheetDataRow row, string column, double minimum, string message)
            {
                var raw = row.Get(column);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    AddIssue(row.Table.Name, row.RowNumber, column, raw, message);
                    return;
                }

                if (!TryParseFiniteNumber(raw, out var value))
                {
                    AddIssue(row.Table.Name, row.RowNumber, column, raw, "Expected a number.");
                    return;
                }

                if (value < minimum)
                    AddIssue(row.Table.Name, row.RowNumber, column, raw, message);
            }

            private void ValidateOptionalNumberGreaterThanOrEqual(ConfigSheetDataRow row, string column, double minimum, string message)
            {
                var raw = row.Get(column);
                if (string.IsNullOrWhiteSpace(raw))
                    return;

                if (!TryParseFiniteNumber(raw, out var value))
                {
                    AddIssue(row.Table.Name, row.RowNumber, column, raw, "Expected a number.");
                    return;
                }

                if (value < minimum)
                    AddIssue(row.Table.Name, row.RowNumber, column, raw, message);
            }

            private bool TryParseRequiredWholeNumber(ConfigSheetDataRow row, string column, out long value)
            {
                value = 0L;
                var raw = row.Get(column);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    AddIssue(row.Table.Name, row.RowNumber, column, raw, $"{column} is required.");
                    return false;
                }

                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    return true;

                if (ConfigPipelineUtilities.TryParseNumber(raw, out var number) && Math.Abs(number % 1d) < 0.0000001d)
                {
                    value = (long)number;
                    return true;
                }

                AddIssue(row.Table.Name, row.RowNumber, column, raw, "Expected a whole number.");
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

            private double GetNumber(ConfigSheetDataRow row, string column)
            {
                return TryParseFiniteNumber(row.Get(column), out var number) ? number : 0d;
            }

            private bool GetBool(ConfigSheetDataRow row, string column)
            {
                TryParseBool(row, column, required: false, out var value);
                return value;
            }

            private bool TryParseNumber(ConfigSheetDataRow row, string column, out double value)
            {
                return TryParseFiniteNumber(row.Get(column), out value);
            }

            private static bool TryParseFiniteNumber(string raw, out double value)
            {
                return ConfigPipelineUtilities.TryParseFiniteNumber(raw, out value);
            }

            private static List<Dictionary<string, object>> ParseMaterials(string raw)
            {
                return ParsePackedObjects(raw, "id", "count");
            }

            private static List<Dictionary<string, object>> ParseActivityRequirements(string raw)
            {
                var values = new List<Dictionary<string, object>>();
                if (string.IsNullOrWhiteSpace(raw))
                    return values;

                var refs = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var activityRef in refs)
                {
                    var parts = activityRef.Trim().Split(':');
                    var id = parts[0].Trim();
                    var count = 1L;
                    if (parts.Length == 2 && !long.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
                        continue;

                    if (parts.Length > 2 || string.IsNullOrWhiteSpace(id))
                        continue;

                    values.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["activityId"] = id,
                        ["count"] = count
                    });
                }

                return values;
            }

            private static List<Dictionary<string, object>> ParseBuildingRequirements(string raw)
            {
                return ParsePackedObjects(raw, "buildingId", "level");
            }

            private static List<Dictionary<string, object>> ParseSkillRequirements(string raw)
            {
                return ParsePackedObjects(raw, "skillId", "level");
            }

            private static List<Dictionary<string, object>> ParsePackedObjects(string raw, string idField, string countField)
            {
                var values = new List<Dictionary<string, object>>();
                if (string.IsNullOrWhiteSpace(raw))
                    return values;

                var refs = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var packedRef in refs)
                {
                    var parts = packedRef.Split(':');
                    if (parts.Length != 2)
                        continue;

                    var id = parts[0].Trim();
                    var count = parts[1].Trim();
                    if (string.IsNullOrWhiteSpace(id) ||
                        !long.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        continue;
                    }

                    values.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        [idField] = id,
                        [countField] = parsed
                    });
                }

                return values;
            }

            private static bool IsIgnoredSheet(string sheetName)
            {
                return string.Equals(sheetName, IndexSheet, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(sheetName, ReadmeSheet, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(sheetName, BuildingActivitiesSheet, StringComparison.OrdinalIgnoreCase) ||
                       IsSettlementStageSheet(sheetName);
            }

            private static bool IsSettlementStageSheet(string sheetName)
            {
                return string.Equals(sheetName, SettlementStageSlotsSheet, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(sheetName, SettlementStageStarterHeroesSheet, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(sheetName, SettlementStageStarterEquipmentSheet, StringComparison.OrdinalIgnoreCase);
            }

            private static bool IsCraftablesSheet(string sheetName)
            {
                return !string.IsNullOrWhiteSpace(sheetName) &&
                       sheetName.StartsWith(CraftablesSheetPrefix, StringComparison.OrdinalIgnoreCase);
            }

            private static bool IsBuildSource(string sourceActivityId)
            {
                return !string.IsNullOrWhiteSpace(sourceActivityId) &&
                       sourceActivityId.StartsWith("build_", StringComparison.OrdinalIgnoreCase);
            }

            private static string LevelKey(string buildingId, long level)
            {
                return $"{buildingId}\n{level.ToString(CultureInfo.InvariantCulture)}";
            }

            private void AddIssue(string sheet, int row, string column, string value, string message)
            {
                _report.Issues.Add(new ConfigValidationIssue(sheet, row, column, value, message));
            }
        }

        private sealed class BuildingSheet
        {
            public string BuildingId { get; private set; }
            public string NameId { get; private set; }
            public string DescriptionId { get; private set; }
            public string SmallIconId { get; private set; }
            public string SourceActivityId { get; private set; }
            public string CraftablesSheet { get; private set; }
            public ConfigSheetTable Table { get; }

            public BuildingSheet(ConfigDownloadedSheet sheet)
            {
                var rows = sheet.rows ?? Array.Empty<ConfigSheetRow>();
                if (TryCreateTableFromCompressedSheet(sheet, rows, out var table, out var metadata))
                {
                    Table = table;
                    ApplyMetadata(metadata);
                    return;
                }

                Table = CreateTableFromKeyValueSheet(sheet, rows, out metadata);
                ApplyMetadata(metadata);
            }

            private void ApplyMetadata(Dictionary<string, string> metadata)
            {
                BuildingId = Get(metadata, "building_id");
                NameId = Get(metadata, "name_id");
                DescriptionId = Get(metadata, "description_id");
                SmallIconId = Get(metadata, "small_icon_id");
                SourceActivityId = Get(metadata, "source_activity_id");
                CraftablesSheet = Get(metadata, "craftables_sheet");
            }

            private static bool TryCreateTableFromCompressedSheet(
                ConfigDownloadedSheet sheet,
                ConfigSheetRow[] rows,
                out ConfigSheetTable table,
                out Dictionary<string, string> metadata)
            {
                table = null;
                metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (rows.Length == 0 || rows[0]?.cells == null || rows[0].cells.Length < 2)
                    return false;

                var firstCell = (rows[0].cells[0] ?? string.Empty).Trim();
                var secondCell = (rows[0].cells[1] ?? string.Empty).Trim();
                if (!firstCell.StartsWith("field ", StringComparison.OrdinalIgnoreCase) ||
                    !secondCell.StartsWith("value ", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                metadata = ParseCompressedMetadata(firstCell, secondCell);
                var normalizedRows = new List<ConfigSheetRow>();
                var headers = new List<string> { "level", LevelPrefabColumn };
                for (var index = 2; index < rows[0].cells.Length; index++)
                {
                    var header = (rows[0].cells[index] ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(header))
                        headers.Add(header);
                }

                normalizedRows.Add(new ConfigSheetRow { cells = headers.ToArray() });
                for (var rowIndex = 1; rowIndex < rows.Length; rowIndex++)
                    normalizedRows.Add(rows[rowIndex]);

                table = new ConfigSheetTable(new ConfigDownloadedSheet
                {
                    sheet_name = sheet.sheet_name,
                    rows = normalizedRows.ToArray()
                });
                return true;
            }

            private static ConfigSheetTable CreateTableFromKeyValueSheet(
                ConfigDownloadedSheet sheet,
                ConfigSheetRow[] rows,
                out Dictionary<string, string> metadata)
            {
                metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var headerRowIndex = -1;
                for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
                {
                    var cells = rows[rowIndex]?.cells ?? Array.Empty<string>();
                    if (ContainsCell(cells, "level") &&
                        (ContainsCell(cells, LevelPrefabColumn) || ContainsCell(cells, LegacyLevelImageColumn)))
                    {
                        headerRowIndex = rowIndex;
                        break;
                    }

                    if (cells.Length >= 2)
                    {
                        var key = (cells[0] ?? string.Empty).Trim();
                        var value = (cells[1] ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(key) && !metadata.ContainsKey(key))
                            metadata[key] = value;
                    }
                }

                var normalizedRows = new List<ConfigSheetRow>();
                if (headerRowIndex >= 0)
                {
                    for (var rowIndex = headerRowIndex; rowIndex < rows.Length; rowIndex++)
                        normalizedRows.Add(rows[rowIndex]);
                }

                return new ConfigSheetTable(new ConfigDownloadedSheet
                {
                    sheet_name = sheet.sheet_name,
                    rows = normalizedRows.ToArray()
                });
            }

            private static Dictionary<string, string> ParseCompressedMetadata(string fieldCell, string valueCell)
            {
                var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var valueTokens = SplitTokens(valueCell);
                var buildingIdIndex = FindToken(valueTokens, token => token.StartsWith("building_", StringComparison.OrdinalIgnoreCase) && !token.EndsWith("_name_id", StringComparison.OrdinalIgnoreCase));
                var nameIdIndex = FindToken(valueTokens, token => token.EndsWith("_name_id", StringComparison.OrdinalIgnoreCase));
                var descriptionIdIndex = FindToken(valueTokens, token => token.EndsWith("_description_id", StringComparison.OrdinalIgnoreCase));
                var smallIconIdIndex = FindToken(valueTokens, token => token.EndsWith("_small_icon_id", StringComparison.OrdinalIgnoreCase));
                var levelPrefabIdIndex = FindToken(valueTokens, token => string.Equals(token, LevelPrefabColumn, StringComparison.OrdinalIgnoreCase));
                if (levelPrefabIdIndex < 0)
                    levelPrefabIdIndex = FindToken(valueTokens, token => string.Equals(token, LegacyLevelImageColumn, StringComparison.OrdinalIgnoreCase));

                if (buildingIdIndex >= 0)
                    metadata["building_id"] = valueTokens[buildingIdIndex];
                if (nameIdIndex >= 0)
                    metadata["name_id"] = valueTokens[nameIdIndex];
                if (descriptionIdIndex >= 0)
                    metadata["description_id"] = valueTokens[descriptionIdIndex];
                if (smallIconIdIndex >= 0)
                    metadata["small_icon_id"] = valueTokens[smallIconIdIndex];

                if (smallIconIdIndex >= 0 && levelPrefabIdIndex > smallIconIdIndex)
                {
                    var remaining = new List<string>();
                    for (var index = smallIconIdIndex + 1; index < levelPrefabIdIndex; index++)
                        remaining.Add(valueTokens[index]);

                    if (remaining.Count > 0)
                    {
                        metadata["source_activity_id"] = remaining[0];
                        if (remaining.Count > 1)
                            metadata["craftables_sheet"] = string.Join(" ", remaining.GetRange(1, remaining.Count - 1).ToArray());
                    }
                }

                return metadata;
            }

            private static List<string> SplitTokens(string value)
            {
                var values = new List<string>();
                if (string.IsNullOrWhiteSpace(value))
                    return values;

                var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (!string.Equals(part, "value", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(part, "field", StringComparison.OrdinalIgnoreCase))
                    {
                        values.Add(part.Trim());
                    }
                }

                return values;
            }

            private static int FindToken(List<string> tokens, Predicate<string> predicate)
            {
                for (var index = 0; index < tokens.Count; index++)
                {
                    if (predicate(tokens[index]))
                        return index;
                }

                return -1;
            }

            private static bool ContainsCell(string[] cells, string value)
            {
                foreach (var cell in cells)
                {
                    if (string.Equals((cell ?? string.Empty).Trim(), value, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }

            private static string Get(Dictionary<string, string> metadata, string key)
            {
                return metadata.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
            }
        }

        private readonly struct BuildingIndexRow
        {
            public string BuildingId { get; }
            public string NameId { get; }
            public string DescriptionId { get; }
            public string SmallIconId { get; }
            public double Levels { get; }
            public double UnlockedByHallLevel { get; }
            public bool MvpRequired { get; }
            public long StartLevel { get; }
            public bool VisibleAtStart { get; }
            public string ClickableRequirement { get; }
            public int RowNumber { get; }

            public BuildingIndexRow(string buildingId, string nameId, string descriptionId, string smallIconId, double levels, double unlockedByHallLevel, bool mvpRequired, long startLevel, bool visibleAtStart, string clickableRequirement, int rowNumber)
            {
                BuildingId = buildingId ?? string.Empty;
                NameId = nameId ?? string.Empty;
                DescriptionId = descriptionId ?? string.Empty;
                SmallIconId = smallIconId ?? string.Empty;
                Levels = levels;
                UnlockedByHallLevel = unlockedByHallLevel;
                MvpRequired = mvpRequired;
                StartLevel = startLevel;
                VisibleAtStart = visibleAtStart;
                ClickableRequirement = clickableRequirement ?? string.Empty;
                RowNumber = rowNumber;
            }
        }

        private readonly struct BuildingLevelRow
        {
            public string BuildingId { get; }
            public ConfigSheetDataRow Row { get; }

            public BuildingLevelRow(string buildingId, ConfigSheetDataRow row)
            {
                BuildingId = buildingId ?? string.Empty;
                Row = row;
            }
        }
    }
}
