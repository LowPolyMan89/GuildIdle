using System;
using System.Collections.Generic;

namespace GuildIdle.Editor.ConfigDownloader
{
    [Serializable]
    public sealed class ConfigSourceSettingsCollection
    {
        public ConfigSourceSettings[] sources = Array.Empty<ConfigSourceSettings>();
    }

    [Serializable]
    public sealed class ConfigSourceSettings
    {
        public string config_id;
        public string display_name;
        public string sheet_url;
        public string source_type;
        public bool enabled;
        public string output_json_path;
        public string runtime_json_path;
        public string last_download_status;
        public string last_download_time;
        public string last_parse_status;
        public string last_parse_time;
        public string last_validation_status;
        public string last_validation_time;
        public string error_message;
    }

    [Serializable]
    public sealed class ConfigSheetDownload
    {
        public string config_id;
        public string display_name;
        public string source_type;
        public string sheet_url;
        public string downloaded_at_utc;
        public ConfigDownloadedSheet[] sheets = Array.Empty<ConfigDownloadedSheet>();
    }

    [Serializable]
    public sealed class ConfigDownloadedSheet
    {
        public string sheet_name;
        public ConfigSheetRow[] rows = Array.Empty<ConfigSheetRow>();
    }

    [Serializable]
    public sealed class ConfigSheetRow
    {
        public string[] cells = Array.Empty<string>();
    }

    public sealed class ConfigValidationIssue
    {
        public string Sheet { get; }
        public int Row { get; }
        public string Column { get; }
        public string Value { get; }
        public string Message { get; }

        public ConfigValidationIssue(string sheet, int row, string column, string value, string message)
        {
            Sheet = sheet ?? string.Empty;
            Row = row;
            Column = column ?? string.Empty;
            Value = value ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public override string ToString()
        {
            var rowText = Row > 0 ? $" row {Row}" : string.Empty;
            var columnText = string.IsNullOrWhiteSpace(Column) ? string.Empty : $" column '{Column}'";
            var valueText = string.IsNullOrWhiteSpace(Value) ? string.Empty : $" value '{Value}'";
            return $"{Sheet}{rowText}{columnText}{valueText}: {Message}";
        }
    }

    public sealed class ConfigPipelineReport
    {
        public bool Success => Issues.Count == 0 && string.IsNullOrWhiteSpace(ErrorMessage);
        public string ErrorMessage { get; set; }
        public List<ConfigValidationIssue> Issues { get; } = new List<ConfigValidationIssue>();
        public List<string> Warnings { get; } = new List<string>();

        public string ToDisplayMessage()
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(ErrorMessage))
                lines.Add(ErrorMessage);

            const int maxDisplayedIssues = 12;
            for (var index = 0; index < Issues.Count && index < maxDisplayedIssues; index++)
                lines.Add(Issues[index].ToString());

            if (Issues.Count > maxDisplayedIssues)
                lines.Add($"...and {Issues.Count - maxDisplayedIssues} more issues.");

            const int maxDisplayedWarnings = 12;
            for (var index = 0; index < Warnings.Count && index < maxDisplayedWarnings; index++)
                lines.Add($"Warning: {Warnings[index]}");

            if (Warnings.Count > maxDisplayedWarnings)
                lines.Add($"...and {Warnings.Count - maxDisplayedWarnings} more warnings.");

            return string.Join("\n", lines);
        }
    }

    public static class ConfigDownloadStatus
    {
        public const string NotDownloaded = "not_downloaded";
        public const string Success = "success";
        public const string AccessError = "access_error";
        public const string LinkError = "link_error";
        public const string FormatError = "format_error";
        public const string EmptyResponse = "empty_response";
    }

    public static class ConfigPipelineStatus
    {
        public const string NotRun = "not_run";
        public const string Success = "success";
        public const string Unsupported = "unsupported";
        public const string MissingRaw = "missing_raw";
        public const string MissingRuntime = "missing_runtime";
        public const string ParseError = "parse_error";
        public const string ValidationError = "validation_error";
        public const string WriteError = "write_error";
    }
}
