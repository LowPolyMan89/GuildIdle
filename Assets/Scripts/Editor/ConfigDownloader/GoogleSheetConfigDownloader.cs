using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace GuildIdle.Editor.ConfigDownloader
{
    public static class GoogleSheetConfigDownloader
    {
        private static readonly Regex _sheetCaptionRegex = new Regex(
            "docs-sheet-tab-caption\">([^<]+)",
            RegexOptions.Compiled);

        public static void DownloadEnabled(ConfigSourceSettingsCollection collection)
        {
            if (collection?.sources == null)
                return;

            var enabledSources = new List<ConfigSourceSettings>();
            foreach (var source in collection.sources)
            {
                if (source != null && source.enabled)
                    enabledSources.Add(source);
            }

            try
            {
                for (var index = 0; index < enabledSources.Count; index++)
                {
                    var source = enabledSources[index];
                    var progress = enabledSources.Count == 0 ? 1f : (float)index / enabledSources.Count;
                    EditorUtility.DisplayProgressBar(
                        "Downloading configs",
                        $"Downloading {source.display_name} ({index + 1}/{enabledSources.Count})",
                        progress);
                    Download(source, false);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ConfigSourceSettingsStore.Save(collection);
        }

        public static void Download(ConfigSourceSettings source)
        {
            Download(source, true);
        }

        private static void Download(ConfigSourceSettings source, bool showProgress)
        {
            if (source == null)
                return;

            try
            {
                if (showProgress)
                    EditorUtility.DisplayProgressBar("Downloading config", $"Downloading {source.display_name}", 0.25f);

                source.error_message = string.Empty;

                if (!IsSupportedSourceType(source.source_type))
                {
                    Fail(source, ConfigDownloadStatus.FormatError, $"Unsupported source_type '{source.source_type}'.");
                    return;
                }

                if (!TryGetSpreadsheetId(source.sheet_url, out var spreadsheetId, out var linkError))
                {
                    Fail(source, ConfigDownloadStatus.LinkError, linkError);
                    return;
                }

                if (!TryValidateRawOutputPath(source.output_json_path, out var outputError))
                {
                    Fail(source, ConfigDownloadStatus.FormatError, outputError);
                    return;
                }

                if (!TryDownloadSheetNames(spreadsheetId, out var sheetNames, out var status, out var sheetError))
                {
                    Fail(source, status, sheetError);
                    return;
                }

                var sheets = new List<ConfigDownloadedSheet>();
                var totalRowCount = 0;
                for (var index = 0; index < sheetNames.Count; index++)
                {
                    var sheetName = sheetNames[index];
                    if (showProgress)
                    {
                        var progress = 0.25f + (0.7f * index / Math.Max(1, sheetNames.Count));
                        EditorUtility.DisplayProgressBar("Downloading config", $"Downloading {source.display_name}: {sheetName}", progress);
                    }

                    if (!TryDownloadSheetRows(spreadsheetId, sheetName, out var rows, out status, out sheetError))
                    {
                        Fail(source, status, $"{sheetName}: {sheetError}");
                        return;
                    }

                    sheets.Add(new ConfigDownloadedSheet
                    {
                        sheet_name = sheetName,
                        rows = rows.ToArray()
                    });
                    totalRowCount += rows.Count;
                }

                if (sheets.Count == 0)
                {
                    Fail(source, ConfigDownloadStatus.EmptyResponse, "Google Sheets returned no sheets.");
                    return;
                }

                if (totalRowCount == 0)
                {
                    Fail(source, ConfigDownloadStatus.EmptyResponse, "Google Sheets returned no data rows in any sheet.");
                    return;
                }

                SaveDownload(source, sheets);
            }
            finally
            {
                if (showProgress)
                    EditorUtility.ClearProgressBar();
            }
        }

        private static bool IsSupportedSourceType(string sourceType)
        {
            return string.Equals(sourceType, "GoogleSheet", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryDownloadSheetNames(
            string spreadsheetId,
            out List<string> sheetNames,
            out string status,
            out string error)
        {
            sheetNames = new List<string>();
            status = null;
            error = null;

            var url = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/edit";
            if (!TryDownloadText(url, out var html, out var responseCode, out var requestError))
            {
                status = responseCode == 404 ? ConfigDownloadStatus.LinkError : ConfigDownloadStatus.AccessError;
                error = $"Could not read spreadsheet metadata ({responseCode}): {requestError}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                status = ConfigDownloadStatus.EmptyResponse;
                error = "Google Sheets returned an empty metadata response.";
                return false;
            }

            foreach (Match match in _sheetCaptionRegex.Matches(html))
            {
                if (!match.Success || match.Groups.Count < 2)
                    continue;

                var sheetName = WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
                if (!string.IsNullOrWhiteSpace(sheetName) && !sheetNames.Contains(sheetName))
                    sheetNames.Add(sheetName);
            }

            if (sheetNames.Count > 0)
                return true;

            if (HtmlLooksLikeAccessDenied(html))
            {
                status = ConfigDownloadStatus.AccessError;
                error = "Google Sheets returned a sign-in or access denied page.";
                return false;
            }

            status = LooksLikeHtml(html) ? ConfigDownloadStatus.FormatError : ConfigDownloadStatus.EmptyResponse;
            error = "Could not discover Google Sheets tabs.";
            return false;
        }

        private static bool TryDownloadSheetRows(
            string spreadsheetId,
            string sheetName,
            out List<ConfigSheetRow> rows,
            out string status,
            out string error)
        {
            rows = null;
            status = null;
            error = null;

            var url = CreateCsvExportUrl(spreadsheetId, sheetName);
            if (!TryDownloadText(url, out var responseText, out var responseCode, out var requestError))
            {
                status = responseCode == 404 ? ConfigDownloadStatus.LinkError : ConfigDownloadStatus.AccessError;
                error = $"Request failed ({responseCode}): {requestError}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(responseText))
            {
                rows = new List<ConfigSheetRow>();
                return true;
            }

            if (LooksLikeHtml(responseText))
            {
                status = HtmlLooksLikeAccessDenied(responseText)
                    ? ConfigDownloadStatus.AccessError
                    : ConfigDownloadStatus.FormatError;
                error = status == ConfigDownloadStatus.AccessError
                    ? "Google Sheets returned a sign-in or access denied page."
                    : "Google Sheets returned HTML instead of CSV data.";
                return false;
            }

            if (!CsvParser.TryParse(responseText, out rows, out var parseError))
            {
                status = ConfigDownloadStatus.FormatError;
                error = parseError;
                return false;
            }

            return true;
        }

        private static bool TryDownloadText(string url, out string text, out long responseCode, out string error)
        {
            text = null;
            responseCode = 0;
            error = null;

            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 30;
                request.SendWebRequest();

                while (!request.isDone)
                {
                }

                responseCode = request.responseCode;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    error = request.error;
                    return false;
                }

                text = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                return true;
            }
        }

        private static string CreateCsvExportUrl(string spreadsheetId, string sheetName)
        {
            return $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/gviz/tq?tqx=out:csv&sheet={Uri.EscapeDataString(sheetName)}";
        }

        private static bool TryGetSpreadsheetId(string sheetUrl, out string spreadsheetId, out string error)
        {
            spreadsheetId = null;
            error = null;

            if (string.IsNullOrWhiteSpace(sheetUrl))
            {
                error = "sheet_url is empty.";
                return false;
            }

            if (!Uri.TryCreate(sheetUrl, UriKind.Absolute, out var uri))
            {
                error = $"sheet_url is not a valid absolute URL: {sheetUrl}";
                return false;
            }

            if (!string.Equals(uri.Host, "docs.google.com", StringComparison.OrdinalIgnoreCase))
            {
                error = $"sheet_url host must be docs.google.com: {sheetUrl}";
                return false;
            }

            var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length - 2; i++)
            {
                if (segments[i] == "spreadsheets" && segments[i + 1] == "d")
                {
                    spreadsheetId = segments[i + 2];
                    if (string.IsNullOrWhiteSpace(spreadsheetId))
                        break;

                    return true;
                }
            }

            error = $"sheet_url is not a Google Sheets document URL: {sheetUrl}";
            return false;
        }

        private static bool TryValidateRawOutputPath(string outputPath, out string error)
        {
            error = null;

            if (!ConfigPaths.IsJsonPath(outputPath))
            {
                error = "output_json_path must end with .json.";
                return false;
            }

            return ConfigPaths.TryGetProjectRelativeFullPath(
                outputPath,
                out _,
                out error,
                requireOutsideAssets: true);
        }

        private static bool HtmlLooksLikeAccessDenied(string text)
        {
            return text.IndexOf("accounts.google.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("You need access", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Access denied", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeHtml(string text)
        {
            var trimmed = text.TrimStart();
            return trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
        }

        private static void SaveDownload(ConfigSourceSettings source, List<ConfigDownloadedSheet> sheets)
        {
            var now = DateTime.UtcNow.ToString("o");
            var download = new ConfigSheetDownload
            {
                config_id = source.config_id,
                display_name = source.display_name,
                source_type = source.source_type,
                sheet_url = source.sheet_url,
                downloaded_at_utc = now,
                sheets = sheets.ToArray()
            };

            var outputPath = ConfigPaths.NormalizeProjectPath(source.output_json_path);
            if (!ConfigPaths.TryGetProjectRelativeFullPath(outputPath, out var fullPath, out var error, requireOutsideAssets: true))
            {
                Fail(source, ConfigDownloadStatus.FormatError, error);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, JsonUtility.ToJson(download, true), ConfigPipelineUtilities.Utf8NoBom);

            source.last_download_status = ConfigDownloadStatus.Success;
            source.last_download_time = now;
            source.error_message = string.Empty;
        }

        private static void Fail(ConfigSourceSettings source, string status, string message)
        {
            source.last_download_status = status;
            source.error_message = message ?? string.Empty;
            Debug.LogError($"Config download failed for '{source.config_id}': {source.error_message}");
        }
    }
}
