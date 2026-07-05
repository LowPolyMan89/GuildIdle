using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    internal static class ConfigPipelineUtilities
    {
        public static bool TryLoadDownload(
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

        public static bool TryValidateRuntimeOutputPath(string runtimePath, out string fullPath, out string error)
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

        public static bool TryParseNumber(string value, out double number)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ||
                   double.TryParse((value ?? string.Empty).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        public static string ToCamelCase(string snakeCase)
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

        public static string FieldKey(string sheetName, string column)
        {
            return $"{sheetName}.{column}";
        }
    }

    internal sealed class ConfigSheetTable
    {
        private readonly Dictionary<string, int> _headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ConfigSheetDataRow> _dataRows = new List<ConfigSheetDataRow>();

        public string Name { get; }
        public IReadOnlyList<string> Headers { get; }
        public IReadOnlyList<ConfigSheetDataRow> DataRows => _dataRows;
        public int Rows { get; }

        public ConfigSheetTable(ConfigDownloadedSheet sheet)
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

                _dataRows.Add(new ConfigSheetDataRow(this, rows[index].cells, index + 1));
            }
        }

        public bool HasColumn(string column)
        {
            return _headerIndex.ContainsKey(column);
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

    internal sealed class ConfigSheetDataRow
    {
        private readonly string[] _cells;

        public ConfigSheetTable Table { get; }
        public int RowNumber { get; }

        public ConfigSheetDataRow(ConfigSheetTable table, string[] cells, int rowNumber)
        {
            Table = table;
            _cells = cells;
            RowNumber = rowNumber;
        }

        public string Get(string column)
        {
            return Table.Get(_cells, column);
        }
    }

    internal static class ConfigRuntimeJsonWriter
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
                case List<string> stringList:
                    WriteStringArray(builder, stringList);
                    break;
                default:
                    builder.Append('"').Append(Escape(Convert.ToString(value, CultureInfo.InvariantCulture))).Append('"');
                    break;
            }
        }

        private static void WriteStringArray(StringBuilder builder, List<string> values)
        {
            builder.Append('[');
            for (var index = 0; index < values.Count; index++)
            {
                if (index > 0)
                    builder.Append(", ");

                builder.Append('"').Append(Escape(values[index])).Append('"');
            }

            builder.Append(']');
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
