using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class LocalisationConfigsParser : IConfigPipelineParser
    {
        private const string ConfigId = "localisation";
        private const string ConfigIdAlias = "localisation_configs";
        private const string LocalisationSheet = "Localisation";
        private const string ReadmeSheet = "README";

        private static readonly string[] RequiredColumns =
        {
            "id",
            "Ru",
            "En",
            "Tr"
        };

        private static readonly string[] DeferredRegistryNames =
        {
            "Buildings Configs",
            "Items Configs",
            "Activity Configs",
            "Enemies Configs",
            "Map Configs",
            "Storage Configs",
            "Skills",
            "Currency"
        };

        private static readonly Regex IdRegex = new Regex(
            "^[A-Za-z0-9._]+$",
            RegexOptions.Compiled);

        public bool Supports(ConfigSourceSettings source)
        {
            return source != null &&
                   (string.Equals(source.config_id, ConfigId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(source.config_id, ConfigIdAlias, StringComparison.OrdinalIgnoreCase));
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

            var context = new LocalisationConfigContext(download, report);
            context.ValidateSheetsAndColumns();
            context.ValidateRows();
            AddDeferredCrossConfigWarnings(report);

            if (!report.Success)
                return report;

            runtimeJson = ConfigRuntimeJsonWriter.Write(context.BuildRuntimeArrays());
            return report;
        }

        private static void AddDeferredCrossConfigWarnings(ConfigPipelineReport report)
        {
            foreach (var registryName in DeferredRegistryNames)
                report.Warnings.Add($"Cross-config validation skipped: {registryName} registry is not available yet.");
        }

        private sealed class LocalisationConfigContext
        {
            private readonly ConfigPipelineReport _report;
            private readonly Dictionary<string, ConfigSheetTable> _tables = new Dictionary<string, ConfigSheetTable>(StringComparer.OrdinalIgnoreCase);
            private ConfigSheetTable _table;

            public LocalisationConfigContext(ConfigSheetDownload download, ConfigPipelineReport report)
            {
                _report = report;
                foreach (var sheet in download.sheets ?? Array.Empty<ConfigDownloadedSheet>())
                {
                    if (sheet == null || string.IsNullOrWhiteSpace(sheet.sheet_name))
                        continue;

                    if (string.Equals(sheet.sheet_name, ReadmeSheet, StringComparison.OrdinalIgnoreCase))
                        continue;

                    _tables[sheet.sheet_name] = new ConfigSheetTable(sheet);
                }
            }

            public void ValidateSheetsAndColumns()
            {
                if (!_tables.TryGetValue(LocalisationSheet, out _table))
                {
                    if (_tables.Count == 1)
                    {
                        foreach (var pair in _tables)
                        {
                            _table = pair.Value;
                            break;
                        }
                    }
                    else
                    {
                        AddIssue(LocalisationSheet, 0, string.Empty, string.Empty, "Required sheet is missing.");
                        return;
                    }
                }

                if (_table.Rows == 0)
                {
                    AddIssue(_table.Name, 1, string.Empty, string.Empty, "Required sheet has no header row.");
                    return;
                }

                foreach (var column in RequiredColumns)
                {
                    if (!_table.HasColumn(column))
                        AddIssue(_table.Name, 1, column, string.Empty, "Required column is missing.");
                }
            }

            public void ValidateRows()
            {
                if (_table == null || !HasRequiredColumns())
                    return;

                var seenIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in _table.DataRows)
                {
                    var id = row.Get("id");
                    var ru = row.Get("Ru");
                    var en = row.Get("En");
                    var tr = row.Get("Tr");

                    var hasId = !string.IsNullOrWhiteSpace(id);
                    var hasTranslation = !string.IsNullOrWhiteSpace(ru) ||
                                         !string.IsNullOrWhiteSpace(en) ||
                                         !string.IsNullOrWhiteSpace(tr);

                    if (!hasId && !hasTranslation)
                        continue;

                    if (!hasId)
                    {
                        AddIssue(_table.Name, row.RowNumber, "id", id, "id is required when any translation is set.");
                        continue;
                    }

                    ValidateId(row, id);
                    ValidateRequiredTranslation(row, "Ru", ru);
                    ValidateRequiredTranslation(row, "En", en);
                    ValidateRequiredTranslation(row, "Tr", tr);

                    if (seenIds.TryGetValue(id, out var firstRow))
                        AddIssue(_table.Name, row.RowNumber, "id", id, $"Duplicate id; first declared at row {firstRow}.");
                    else
                        seenIds[id] = row.RowNumber;
                }
            }

            public Dictionary<string, List<Dictionary<string, object>>> BuildRuntimeArrays()
            {
                return new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.Ordinal)
                {
                    ["localisations"] = BuildLocalisations()
                };
            }

            private List<Dictionary<string, object>> BuildLocalisations()
            {
                var rows = new List<Dictionary<string, object>>();
                if (_table == null || !HasRequiredColumns())
                    return rows;

                foreach (var row in _table.DataRows)
                {
                    var id = row.Get("id");
                    var ru = row.Get("Ru");
                    var en = row.Get("En");
                    var tr = row.Get("Tr");
                    if (string.IsNullOrWhiteSpace(id) &&
                        string.IsNullOrWhiteSpace(ru) &&
                        string.IsNullOrWhiteSpace(en) &&
                        string.IsNullOrWhiteSpace(tr))
                    {
                        continue;
                    }

                    rows.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["id"] = id,
                        ["ru"] = ru,
                        ["en"] = en,
                        ["tr"] = tr
                    });
                }

                return rows;
            }

            private bool HasRequiredColumns()
            {
                foreach (var column in RequiredColumns)
                {
                    if (!_table.HasColumn(column))
                        return false;
                }

                return true;
            }

            private void ValidateId(ConfigSheetDataRow row, string id)
            {
                if (id.IndexOf(' ') >= 0 || id.IndexOf('\t') >= 0 || id.IndexOf('\n') >= 0 || id.IndexOf('\r') >= 0)
                    AddIssue(_table.Name, row.RowNumber, "id", id, "id must not contain whitespace.");

                if (id.StartsWith(".", StringComparison.Ordinal) ||
                    id.StartsWith("_", StringComparison.Ordinal) ||
                    id.EndsWith(".", StringComparison.Ordinal) ||
                    id.EndsWith("_", StringComparison.Ordinal))
                {
                    AddIssue(_table.Name, row.RowNumber, "id", id, "id must not start or end with dot or underscore.");
                }

                if (id.Contains(".."))
                    AddIssue(_table.Name, row.RowNumber, "id", id, "id must not contain double dots.");

                if (!IdRegex.IsMatch(id))
                    AddIssue(_table.Name, row.RowNumber, "id", id, "id may contain only latin letters, digits, dot, and underscore.");
            }

            private void ValidateRequiredTranslation(ConfigSheetDataRow row, string column, string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    AddIssue(_table.Name, row.RowNumber, column, value, $"{column} translation is required.");
            }

            private void AddIssue(string sheet, int row, string column, string value, string message)
            {
                _report.Issues.Add(new ConfigValidationIssue(sheet, row, column, value, message));
            }
        }
    }
}
