using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class MapConfigsParser : IConfigPipelineParser
    {
        private const string ConfigId = "map_configs";

        private static readonly string[] RequiredSheets =
        {
            "MapCells",
            "MapLocations",
            "MapExplorationLevels",
            "MapCellActivities",
            "MapEnums"
        };

        private static readonly Dictionary<string, string[]> RequiredColumns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["MapCells"] = new[]
            {
                "cell_id", "q", "r", "map_cell_name_id", "state_default", "terrain_type", "region_id",
                "location_id", "max_exploration_level", "exploration_difficulty", "is_blocking",
                "visual_marker_id", "notes"
            },
            ["MapLocations"] = new[]
            {
                "location_id", "map_location_name_id", "location_type", "tier", "region_id",
                "default_cell_id", "visible_in_watchtower", "notes"
            },
            ["MapExplorationLevels"] = new[] { "exploration_level", "points_required", "notes" },
            ["MapCellActivities"] = new[]
            {
                "cell_id", "location_id", "activity_id", "reveal_at_exploration_level",
                "visible_in_watchtower", "notes"
            },
            ["MapEnums"] = new[] { "enum_group", "value", "description" }
        };

        private static readonly Dictionary<string, string[]> RuntimeColumns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["MapCells"] = new[]
            {
                "cell_id", "q", "r", "map_cell_name_id", "state_default", "terrain_type", "region_id",
                "location_id", "max_exploration_level", "exploration_difficulty", "is_blocking",
                "visual_marker_id"
            },
            ["MapLocations"] = new[]
            {
                "location_id", "map_location_name_id", "location_type", "tier", "region_id",
                "default_cell_id", "visible_in_watchtower"
            },
            ["MapExplorationLevels"] = new[] { "exploration_level", "points_required" },
            ["MapCellActivities"] = new[]
            {
                "cell_id", "location_id", "activity_id", "reveal_at_exploration_level",
                "visible_in_watchtower"
            },
            ["MapEnums"] = new[] { "enum_group", "value", "description" }
        };

        private static readonly Dictionary<string, string> RuntimeArrayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MapCells"] = "mapCells",
            ["MapLocations"] = "mapLocations",
            ["MapExplorationLevels"] = "mapExplorationLevels",
            ["MapCellActivities"] = "mapCellActivities",
            ["MapEnums"] = "enumValues"
        };

        private static readonly HashSet<string> OptionalValueFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FieldKey("MapCells", "visual_marker_id"),
            FieldKey("MapCells", "notes"),
            FieldKey("MapLocations", "notes"),
            FieldKey("MapExplorationLevels", "notes"),
            FieldKey("MapCellActivities", "notes")
        };

        private static readonly HashSet<string> IntegerFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FieldKey("MapCells", "q"),
            FieldKey("MapCells", "r"),
            FieldKey("MapCells", "max_exploration_level"),
            FieldKey("MapLocations", "tier"),
            FieldKey("MapExplorationLevels", "exploration_level"),
            FieldKey("MapExplorationLevels", "points_required"),
            FieldKey("MapCellActivities", "reveal_at_exploration_level")
        };

        private static readonly HashSet<string> NumberFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            FieldKey("MapCells", "exploration_difficulty")
        };

        private static readonly HashSet<string> BoolColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "is_blocking",
            "visible_in_watchtower"
        };

        private static readonly Dictionary<string, string> EnumColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [FieldKey("MapCells", "state_default")] = "MapCellState",
            [FieldKey("MapCells", "terrain_type")] = "TerrainType",
            [FieldKey("MapCells", "visual_marker_id")] = "MapVisualMarker",
            [FieldKey("MapLocations", "location_type")] = "LocationType"
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

            var context = new MapConfigContext(download, report);
            context.ValidateSheetsAndColumns();
            context.CollectEnumValues();
            context.CollectIds();
            context.ValidateRows();

            if (!report.Success)
                return report;

            runtimeJson = ConfigRuntimeJsonWriter.Write(context.BuildRuntimeArrays());
            return report;
        }

        private sealed class MapConfigContext
        {
            private readonly ConfigPipelineReport _report;
            private readonly Dictionary<string, ConfigSheetTable> _tables = new Dictionary<string, ConfigSheetTable>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, HashSet<string>> _enumValues = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _cellIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _locationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<long> _explorationLevels = new HashSet<long>();
            private readonly Dictionary<string, string> _cellLocationIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, long> _cellMaxExplorationLevels = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<long, ExplorationPointRow> _explorationPointRows = new Dictionary<long, ExplorationPointRow>();

            public MapConfigContext(ConfigSheetDownload download, ConfigPipelineReport report)
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

            public void CollectEnumValues()
            {
                if (!_tables.TryGetValue("MapEnums", out var table) ||
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
                        AddIssue("MapEnums", row.RowNumber, "value", value, $"Duplicate enum value in group '{group}'.");
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
                CollectMapCellIdsAndCoordinates();
                CollectUniqueIds("MapLocations", "location_id", _locationIds, "location_id");
                CollectExplorationLevels();
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

                ValidateExplorationPointProgression();
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

            private void CollectMapCellIdsAndCoordinates()
            {
                if (!_tables.TryGetValue("MapCells", out var table) ||
                    !table.HasColumn("cell_id"))
                {
                    return;
                }

                var seenIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var seenCoordinates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    var cellId = row.Get("cell_id");
                    if (string.IsNullOrWhiteSpace(cellId))
                    {
                        AddIssue("MapCells", row.RowNumber, "cell_id", cellId, "cell_id is required.");
                    }
                    else if (seenIds.TryGetValue(cellId, out var firstRow))
                    {
                        AddIssue("MapCells", row.RowNumber, "cell_id", cellId, $"Duplicate cell_id; first declared at row {firstRow}.");
                    }
                    else
                    {
                        seenIds[cellId] = row.RowNumber;
                        _cellIds.Add(cellId);
                    }

                    if (TryParseInteger(row, "q", out var q) && TryParseInteger(row, "r", out var r))
                    {
                        var coordinateKey = $"{q}\n{r}";
                        if (seenCoordinates.TryGetValue(coordinateKey, out var firstCoordinateRow))
                            AddIssue("MapCells", row.RowNumber, "r", row.Get("r"), $"Duplicate q + r coordinate pair; first declared at row {firstCoordinateRow}.");
                        else
                            seenCoordinates[coordinateKey] = row.RowNumber;
                    }

                    if (!string.IsNullOrWhiteSpace(cellId))
                    {
                        var locationId = row.Get("location_id");
                        if (!_cellLocationIds.ContainsKey(cellId))
                            _cellLocationIds[cellId] = locationId;

                        if (TryParseInteger(row, "max_exploration_level", out var maxExplorationLevel) &&
                            !_cellMaxExplorationLevels.ContainsKey(cellId))
                        {
                            _cellMaxExplorationLevels[cellId] = maxExplorationLevel;
                        }
                    }
                }
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

            private void CollectExplorationLevels()
            {
                if (!_tables.TryGetValue("MapExplorationLevels", out var table) ||
                    !table.HasColumn("exploration_level") ||
                    !table.HasColumn("points_required"))
                {
                    return;
                }

                var seen = new Dictionary<long, int>();
                foreach (var row in table.DataRows)
                {
                    if (!TryParseInteger(row, "exploration_level", out var level))
                        continue;

                    if (seen.TryGetValue(level, out var firstRow))
                    {
                        AddIssue("MapExplorationLevels", row.RowNumber, "exploration_level", row.Get("exploration_level"), $"Duplicate exploration_level; first declared at row {firstRow}.");
                        continue;
                    }

                    seen[level] = row.RowNumber;
                    _explorationLevels.Add(level);

                    if (TryParseInteger(row, "points_required", out var pointsRequired))
                        _explorationPointRows[level] = new ExplorationPointRow(row.RowNumber, pointsRequired);
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

                    if (IsIntegerField(table.Name, column) &&
                        !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    {
                        AddIssue(table.Name, row.RowNumber, column, value, "Expected an integer number.");
                    }

                    if (IsNumberField(table.Name, column) && !ConfigPipelineUtilities.TryParseNumber(value, out _))
                        AddIssue(table.Name, row.RowNumber, column, value, "Expected a number.");

                    if (BoolColumns.Contains(column))
                        TryParseBool(row, column, out _);
                }
            }

            private void ValidateEnumReferences(ConfigSheetTable table, ConfigSheetDataRow row)
            {
                foreach (var column in RuntimeColumns[table.Name])
                {
                    if (!EnumColumns.TryGetValue(FieldKey(table.Name, column), out var enumGroup))
                        continue;

                    var value = row.Get(column);
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    if (!_enumValues.TryGetValue(enumGroup, out var allowedValues) || !allowedValues.Contains(value))
                        AddIssue(table.Name, row.RowNumber, column, value, $"Value is not listed in MapEnums group '{enumGroup}'.");
                }
            }

            private void ValidateSheetRules(ConfigSheetTable table, ConfigSheetDataRow row)
            {
                if (string.Equals(table.Name, "MapCells", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateMapCell(row);
                    return;
                }

                if (string.Equals(table.Name, "MapLocations", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateMapLocation(row);
                    return;
                }

                if (string.Equals(table.Name, "MapExplorationLevels", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateMapExplorationLevel(row);
                    return;
                }

                if (string.Equals(table.Name, "MapCellActivities", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateMapCellActivity(row);
                    return;
                }

                if (string.Equals(table.Name, "MapEnums", StringComparison.OrdinalIgnoreCase))
                    ValidateMapEnum(row);
            }

            private void ValidateMapCell(ConfigSheetDataRow row)
            {
                var locationId = row.Get("location_id");
                if (!string.IsNullOrWhiteSpace(locationId) && !_locationIds.Contains(locationId))
                    AddIssue("MapCells", row.RowNumber, "location_id", locationId, "Referenced location_id does not exist in MapLocations.location_id.");

                if (TryParseInteger(row, "max_exploration_level", out var maxExplorationLevel))
                {
                    if (maxExplorationLevel < 0)
                        AddIssue("MapCells", row.RowNumber, "max_exploration_level", row.Get("max_exploration_level"), "max_exploration_level must be greater than or equal to 0.");

                    if (maxExplorationLevel > 0 && !_explorationLevels.Contains(maxExplorationLevel))
                        AddIssue("MapCells", row.RowNumber, "max_exploration_level", row.Get("max_exploration_level"), "max_exploration_level does not exist in MapExplorationLevels.exploration_level.");
                }

                if (TryParseNumber(row, "exploration_difficulty", out var explorationDifficulty) && explorationDifficulty < 0)
                    AddIssue("MapCells", row.RowNumber, "exploration_difficulty", row.Get("exploration_difficulty"), "exploration_difficulty must be greater than or equal to 0.");
            }

            private void ValidateMapLocation(ConfigSheetDataRow row)
            {
                if (TryParseInteger(row, "tier", out var tier) && tier <= 0)
                    AddIssue("MapLocations", row.RowNumber, "tier", row.Get("tier"), "tier must be greater than 0.");

                var defaultCellId = row.Get("default_cell_id");
                if (!string.IsNullOrWhiteSpace(defaultCellId) && !_cellIds.Contains(defaultCellId))
                    AddIssue("MapLocations", row.RowNumber, "default_cell_id", defaultCellId, "Referenced default_cell_id does not exist in MapCells.cell_id.");
            }

            private void ValidateMapExplorationLevel(ConfigSheetDataRow row)
            {
                if (TryParseInteger(row, "exploration_level", out var explorationLevel) && explorationLevel <= 0)
                    AddIssue("MapExplorationLevels", row.RowNumber, "exploration_level", row.Get("exploration_level"), "exploration_level must be greater than 0.");

                if (TryParseInteger(row, "points_required", out var pointsRequired) && pointsRequired <= 0)
                    AddIssue("MapExplorationLevels", row.RowNumber, "points_required", row.Get("points_required"), "points_required must be greater than 0.");
            }

            private void ValidateMapCellActivity(ConfigSheetDataRow row)
            {
                var cellId = row.Get("cell_id");
                var locationId = row.Get("location_id");

                if (!string.IsNullOrWhiteSpace(cellId) && !_cellIds.Contains(cellId))
                    AddIssue("MapCellActivities", row.RowNumber, "cell_id", cellId, "Referenced cell_id does not exist in MapCells.cell_id.");

                if (!string.IsNullOrWhiteSpace(locationId) && !_locationIds.Contains(locationId))
                    AddIssue("MapCellActivities", row.RowNumber, "location_id", locationId, "Referenced location_id does not exist in MapLocations.location_id.");

                if (!string.IsNullOrWhiteSpace(cellId) &&
                    !string.IsNullOrWhiteSpace(locationId) &&
                    _cellLocationIds.TryGetValue(cellId, out var actualLocationId) &&
                    !string.Equals(actualLocationId, locationId, StringComparison.OrdinalIgnoreCase))
                {
                    AddIssue("MapCellActivities", row.RowNumber, "location_id", locationId, "location_id does not match MapCells.location_id for this cell_id.");
                }

                if (TryParseInteger(row, "reveal_at_exploration_level", out var revealLevel))
                {
                    if (revealLevel <= 0)
                        AddIssue("MapCellActivities", row.RowNumber, "reveal_at_exploration_level", row.Get("reveal_at_exploration_level"), "reveal_at_exploration_level must be greater than 0.");

                    if (!_explorationLevels.Contains(revealLevel))
                        AddIssue("MapCellActivities", row.RowNumber, "reveal_at_exploration_level", row.Get("reveal_at_exploration_level"), "reveal_at_exploration_level does not exist in MapExplorationLevels.exploration_level.");

                    if (_cellMaxExplorationLevels.TryGetValue(cellId, out var maxExplorationLevel) && revealLevel > maxExplorationLevel)
                        AddIssue("MapCellActivities", row.RowNumber, "reveal_at_exploration_level", row.Get("reveal_at_exploration_level"), "reveal_at_exploration_level must not be greater than MapCells.max_exploration_level for this cell_id.");
                }
            }

            private void ValidateMapEnum(ConfigSheetDataRow row)
            {
                if (string.IsNullOrWhiteSpace(row.Get("enum_group")))
                    AddIssue("MapEnums", row.RowNumber, "enum_group", row.Get("enum_group"), "Enum group is required.");

                if (string.IsNullOrWhiteSpace(row.Get("value")))
                    AddIssue("MapEnums", row.RowNumber, "value", row.Get("value"), "Enum value is required.");

                if (string.IsNullOrWhiteSpace(row.Get("description")))
                    AddIssue("MapEnums", row.RowNumber, "description", row.Get("description"), "Enum description is required.");
            }

            private void ValidateExplorationPointProgression()
            {
                var levels = new List<long>(_explorationPointRows.Keys);
                levels.Sort();

                long? previousPoints = null;
                foreach (var level in levels)
                {
                    var row = _explorationPointRows[level];
                    if (previousPoints.HasValue && row.PointsRequired < previousPoints.Value)
                    {
                        AddIssue(
                            "MapExplorationLevels",
                            row.RowNumber,
                            "points_required",
                            row.PointsRequired.ToString(CultureInfo.InvariantCulture),
                            "points_required must not decrease as exploration_level increases.");
                    }

                    previousPoints = row.PointsRequired;
                }
            }

            private bool TryParseInteger(ConfigSheetDataRow row, string column, out long value)
            {
                value = 0;
                var raw = row.Get(column);
                return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            }

            private bool TryParseNumber(ConfigSheetDataRow row, string column, out double value)
            {
                return ConfigPipelineUtilities.TryParseNumber(row.Get(column), out value);
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

        private readonly struct ExplorationPointRow
        {
            public int RowNumber { get; }
            public long PointsRequired { get; }

            public ExplorationPointRow(int rowNumber, long pointsRequired)
            {
                RowNumber = rowNumber;
                PointsRequired = pointsRequired;
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

            if (IsNumberField(sheetName, column) && ConfigPipelineUtilities.TryParseNumber(value, out var number))
                return number;

            return value ?? string.Empty;
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
