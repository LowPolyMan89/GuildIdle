using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

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
        public string last_download_status;
        public string last_download_time;
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
        public ConfigSheetRow[] rows = Array.Empty<ConfigSheetRow>();
    }

    [Serializable]
    public sealed class ConfigSheetRow
    {
        public string[] cells = Array.Empty<string>();
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

    public static class ConfigSourceSettingsStore
    {
        public const string SettingsPath = "Assets/Editor/ConfigSources/config_sources.json";
        private const string DefaultOutputFolder = "Assets/Editor/ConfigSources/Downloaded";

        public static ConfigSourceSettingsCollection LoadOrCreate()
        {
            ConfigSourceSettingsCollection collection = null;

            if (File.Exists(SettingsPath))
            {
                try
                {
                    collection = JsonUtility.FromJson<ConfigSourceSettingsCollection>(File.ReadAllText(SettingsPath, Encoding.UTF8));
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Failed to read config source settings '{SettingsPath}': {exception.Message}");
                }
            }

            collection = MergeWithDefaults(collection);
            Save(collection);
            return collection;
        }

        public static void Save(ConfigSourceSettingsCollection collection)
        {
            if (collection == null)
                collection = MergeWithDefaults(null);

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(collection, true), Encoding.UTF8);
            AssetDatabase.ImportAsset(SettingsPath);
            AssetDatabase.Refresh();
        }

        private static ConfigSourceSettingsCollection MergeWithDefaults(ConfigSourceSettingsCollection existing)
        {
            var sources = new List<ConfigSourceSettings>();
            var existingById = new Dictionary<string, ConfigSourceSettings>(StringComparer.OrdinalIgnoreCase);

            if (existing?.sources != null)
            {
                foreach (var source in existing.sources)
                {
                    if (source == null || string.IsNullOrWhiteSpace(source.config_id))
                        continue;

                    existingById[source.config_id] = source;
                }
            }

            foreach (var defaultSource in CreateDefaultSources())
            {
                if (existingById.TryGetValue(defaultSource.config_id, out var source))
                {
                    ApplyMissingDefaults(source, defaultSource);
                    sources.Add(source);
                }
                else
                {
                    sources.Add(defaultSource);
                }
            }

            return new ConfigSourceSettingsCollection { sources = sources.ToArray() };
        }

        private static void ApplyMissingDefaults(ConfigSourceSettings source, ConfigSourceSettings defaultSource)
        {
            if (string.IsNullOrWhiteSpace(source.display_name))
                source.display_name = defaultSource.display_name;

            if (string.IsNullOrWhiteSpace(source.sheet_url))
                source.sheet_url = defaultSource.sheet_url;

            if (string.IsNullOrWhiteSpace(source.source_type))
                source.source_type = defaultSource.source_type;

            if (string.IsNullOrWhiteSpace(source.output_json_path))
                source.output_json_path = defaultSource.output_json_path;

            if (string.IsNullOrWhiteSpace(source.last_download_status))
                source.last_download_status = ConfigDownloadStatus.NotDownloaded;
        }

        private static ConfigSourceSettings[] CreateDefaultSources()
        {
            return new[]
            {
                CreateSource("activity_configs", "GuildIdle - Activity Configs", "https://docs.google.com/spreadsheets/d/10MOjF_GtcZcji0yv-dk69LtW3kfg6HqC2dyrdy9oa3w"),
                CreateSource("enemies_configs", "GuildIdle - Enemies Configs", "https://docs.google.com/spreadsheets/d/1hjKKBn81MF6rn6dUECc6MQZoJ-q6TquicYd6CED8gQ4"),
                CreateSource("storage_configs", "GuildIdle - Storage Configs", "https://docs.google.com/spreadsheets/d/1hSoVEQGuNQOpzQEtIWC95OthajpCXUjjbJe4AxA08-U"),
                CreateSource("map_configs", "GuildIdle - Map Configs", "https://docs.google.com/spreadsheets/d/1dc3A4rq8rTh8wgMYk8Jze-MyJY986F76K35X5qqX1mo"),
                CreateSource("localisation", "GuildIdle - Localisation", "https://docs.google.com/spreadsheets/d/1j1cPqDSoFHRlxAyeL6T9ma15D29IY9rmubP1y09uVEk"),
                CreateSource("items_configs", "GuildIdle - Items Configs", "https://docs.google.com/spreadsheets/d/1URJdSKzwWwhAZgviDkdYNgdHR8IqCVfCoq77GvYY4iw"),
                CreateSource("formula_configs", "GuildIdle - Formula Configs", "https://docs.google.com/spreadsheets/d/1WwMrY9HtVyqWiKI_wBIh4M7zikh4ocMVbj2LA4bKCT0"),
                CreateSource("loot_configs", "GuildIdle - Loot Configs", "https://docs.google.com/spreadsheets/d/1Y1YePRz3EXU_Vs5ibbAe81v9n0Ef4_k1Hgja57rSGV8"),
                CreateSource("buildings_configs", "GuildIdle - Buildings Configs", "https://docs.google.com/spreadsheets/d/1oHtDe-dr3-qDds4ZiUYjYBwpafwyr7X11FGJ5Qp1A7k")
            };
        }

        private static ConfigSourceSettings CreateSource(string id, string name, string url)
        {
            return new ConfigSourceSettings
            {
                config_id = id,
                display_name = name,
                sheet_url = url,
                source_type = "GoogleSheet",
                enabled = true,
                output_json_path = $"{DefaultOutputFolder}/{id}.json",
                last_download_status = ConfigDownloadStatus.NotDownloaded,
                last_download_time = string.Empty,
                error_message = string.Empty
            };
        }
    }

    public static class GoogleSheetConfigDownloader
    {
        public static void DownloadEnabled(ConfigSourceSettingsCollection collection)
        {
            if (collection?.sources == null)
                return;

            foreach (var source in collection.sources)
            {
                if (source != null && source.enabled)
                    Download(source);
            }

            ConfigSourceSettingsStore.Save(collection);
        }

        public static void Download(ConfigSourceSettings source)
        {
            if (source == null)
                return;

            source.error_message = string.Empty;

            if (!IsSupportedSourceType(source.source_type))
            {
                Fail(source, ConfigDownloadStatus.FormatError, $"Unsupported source_type '{source.source_type}'.");
                return;
            }

            if (!TryCreateCsvExportUrl(source.sheet_url, out var exportUrl, out var linkError))
            {
                Fail(source, ConfigDownloadStatus.LinkError, linkError);
                return;
            }

            if (!TryValidateOutputPath(source.output_json_path, out var outputError))
            {
                Fail(source, ConfigDownloadStatus.FormatError, outputError);
                return;
            }

            using (var request = UnityWebRequest.Get(exportUrl))
            {
                request.timeout = 30;
                request.SendWebRequest();

                while (!request.isDone)
                {
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    var status = request.responseCode == 404 ? ConfigDownloadStatus.LinkError : ConfigDownloadStatus.AccessError;
                    Fail(source, status, $"Request failed ({request.responseCode}): {request.error}");
                    return;
                }

                var responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                if (string.IsNullOrWhiteSpace(responseText))
                {
                    Fail(source, ConfigDownloadStatus.EmptyResponse, "Google Sheets returned an empty response.");
                    return;
                }

                if (LooksLikeAccessDenied(responseText))
                {
                    Fail(source, ConfigDownloadStatus.AccessError, "Google Sheets returned a sign-in or access denied page.");
                    return;
                }

                if (LooksLikeHtml(responseText))
                {
                    Fail(source, ConfigDownloadStatus.FormatError, "Google Sheets returned HTML instead of CSV data.");
                    return;
                }

                if (!CsvParser.TryParse(responseText, out var rows, out var parseError))
                {
                    Fail(source, ConfigDownloadStatus.FormatError, parseError);
                    return;
                }

                if (rows.Count == 0)
                {
                    Fail(source, ConfigDownloadStatus.EmptyResponse, "Google Sheets returned no rows.");
                    return;
                }

                SaveDownload(source, rows);
            }
        }

        private static bool IsSupportedSourceType(string sourceType)
        {
            return string.Equals(sourceType, "GoogleSheet", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryCreateCsvExportUrl(string sheetUrl, out string exportUrl, out string error)
        {
            exportUrl = null;
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
                    var spreadsheetId = segments[i + 2];
                    if (string.IsNullOrWhiteSpace(spreadsheetId))
                        break;

                    exportUrl = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=csv";
                    return true;
                }
            }

            error = $"sheet_url is not a Google Sheets document URL: {sheetUrl}";
            return false;
        }

        private static bool TryValidateOutputPath(string outputPath, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                error = "output_json_path is empty.";
                return false;
            }

            var normalized = outputPath.Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                error = "output_json_path must be under Assets/ and project-relative.";
                return false;
            }

            if (!normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                error = "output_json_path must end with .json.";
                return false;
            }

            return true;
        }

        private static bool LooksLikeAccessDenied(string text)
        {
            return text.IndexOf("accounts.google.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Sign in", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("You need access", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("Access denied", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeHtml(string text)
        {
            var trimmed = text.TrimStart();
            return trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
        }

        private static void SaveDownload(ConfigSourceSettings source, List<ConfigSheetRow> rows)
        {
            var now = DateTime.UtcNow.ToString("o");
            var download = new ConfigSheetDownload
            {
                config_id = source.config_id,
                display_name = source.display_name,
                source_type = source.source_type,
                sheet_url = source.sheet_url,
                downloaded_at_utc = now,
                rows = rows.ToArray()
            };

            var outputPath = source.output_json_path.Replace('\\', '/');
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, JsonUtility.ToJson(download, true), Encoding.UTF8);
            AssetDatabase.ImportAsset(outputPath);
            AssetDatabase.Refresh();

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

    public static class CsvParser
    {
        public static bool TryParse(string csv, out List<ConfigSheetRow> rows, out string error)
        {
            rows = new List<ConfigSheetRow>();
            error = null;

            var currentRow = new List<string>();
            var currentCell = new StringBuilder();
            var inQuotes = false;

            for (var index = 0; index < csv.Length; index++)
            {
                var character = csv[index];

                if (inQuotes)
                {
                    if (character == '"')
                    {
                        if (index + 1 < csv.Length && csv[index + 1] == '"')
                        {
                            currentCell.Append('"');
                            index++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        currentCell.Append(character);
                    }

                    continue;
                }

                if (character == '"')
                {
                    if (currentCell.Length == 0)
                    {
                        inQuotes = true;
                        continue;
                    }

                    error = "Unexpected quote in unquoted CSV field.";
                    return false;
                }

                if (character == ',')
                {
                    currentRow.Add(currentCell.ToString());
                    currentCell.Length = 0;
                    continue;
                }

                if (character == '\n')
                {
                    AddRow(rows, currentRow, currentCell);
                    continue;
                }

                if (character == '\r')
                {
                    if (index + 1 < csv.Length && csv[index + 1] == '\n')
                        index++;

                    AddRow(rows, currentRow, currentCell);
                    continue;
                }

                currentCell.Append(character);
            }

            if (inQuotes)
            {
                error = "CSV contains an unclosed quoted field.";
                return false;
            }

            if (currentCell.Length > 0 || currentRow.Count > 0)
                AddRow(rows, currentRow, currentCell);

            return true;
        }

        private static void AddRow(List<ConfigSheetRow> rows, List<string> currentRow, StringBuilder currentCell)
        {
            currentRow.Add(currentCell.ToString());
            currentCell.Length = 0;

            var hasValue = false;
            foreach (var cell in currentRow)
            {
                if (!string.IsNullOrWhiteSpace(cell))
                {
                    hasValue = true;
                    break;
                }
            }

            if (hasValue)
                rows.Add(new ConfigSheetRow { cells = currentRow.ToArray() });

            currentRow.Clear();
        }
    }

    public sealed class ConfigDownloaderWindow : EditorWindow
    {
        private ConfigSourceSettingsCollection _settings;
        private Vector2 _scroll;
        private bool _dirty;

        [MenuItem("Tools/Configs/Config Downloader")]
        public static void Open()
        {
            var window = GetWindow<ConfigDownloaderWindow>();
            window.titleContent = new GUIContent("Config Downloader");
            window.minSize = new Vector2(860f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            LoadSettings();
        }

        private void OnGUI()
        {
            if (_settings == null)
                LoadSettings();

            DrawToolbar();

            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox("Editor-only Google Sheets downloader. Runtime/WebGL builds do not call Google Drive or Google Sheets.", MessageType.Info);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_settings?.sources != null)
            {
                foreach (var source in _settings.sources)
                    DrawSource(source);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Reload Settings", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                LoadSettings();

            EditorGUI.BeginDisabledGroup(!_dirty);
            if (GUILayout.Button("Save Settings", EditorStyles.toolbarButton, GUILayout.Width(105f)))
                SaveSettings();
            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Download Enabled", EditorStyles.toolbarButton, GUILayout.Width(135f)))
            {
                GoogleSheetConfigDownloader.DownloadEnabled(_settings);
                _dirty = false;
                Repaint();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSource(ConfigSourceSettings source)
        {
            if (source == null)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            source.enabled = DrawChangedToggle("Enabled", source.enabled, GUILayout.Width(80f));
            EditorGUILayout.LabelField(source.display_name, EditorStyles.boldLabel);

            GUILayout.FlexibleSpace();
            DrawStatus(source.last_download_status);

            if (GUILayout.Button("Download", GUILayout.Width(90f)))
            {
                GoogleSheetConfigDownloader.Download(source);
                ConfigSourceSettingsStore.Save(_settings);
                _dirty = false;
                Repaint();
            }

            EditorGUILayout.EndHorizontal();

            source.config_id = DrawChangedTextField("config_id", source.config_id);
            source.display_name = DrawChangedTextField("display_name", source.display_name);
            source.sheet_url = DrawChangedTextField("sheet_url", source.sheet_url);
            source.source_type = DrawChangedTextField("source_type", source.source_type);
            source.output_json_path = DrawChangedTextField("output_json_path", source.output_json_path);
            EditorGUILayout.LabelField("last_download_time", string.IsNullOrWhiteSpace(source.last_download_time) ? "-" : source.last_download_time);

            if (!string.IsNullOrWhiteSpace(source.error_message))
            {
                var previousColor = GUI.color;
                GUI.color = new Color(1f, 0.55f, 0.45f);
                EditorGUILayout.TextField("error_message", source.error_message);
                GUI.color = previousColor;
            }

            EditorGUILayout.EndVertical();
        }

        private string DrawChangedTextField(string label, string value)
        {
            EditorGUI.BeginChangeCheck();
            var nextValue = EditorGUILayout.TextField(label, value ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
                _dirty = true;

            return nextValue;
        }

        private bool DrawChangedToggle(string label, bool value, params GUILayoutOption[] options)
        {
            EditorGUI.BeginChangeCheck();
            var nextValue = EditorGUILayout.ToggleLeft(label, value, options);
            if (EditorGUI.EndChangeCheck())
                _dirty = true;

            return nextValue;
        }

        private static void DrawStatus(string status)
        {
            var previousColor = GUI.color;
            GUI.color = GetStatusColor(status);
            GUILayout.Label(string.IsNullOrWhiteSpace(status) ? ConfigDownloadStatus.NotDownloaded : status, GUILayout.Width(115f));
            GUI.color = previousColor;
        }

        private static Color GetStatusColor(string status)
        {
            if (status == ConfigDownloadStatus.Success)
                return new Color(0.45f, 0.9f, 0.45f);

            if (status == ConfigDownloadStatus.NotDownloaded || string.IsNullOrWhiteSpace(status))
                return Color.white;

            return new Color(1f, 0.55f, 0.45f);
        }

        private void LoadSettings()
        {
            _settings = ConfigSourceSettingsStore.LoadOrCreate();
            _dirty = false;
        }

        private void SaveSettings()
        {
            ConfigSourceSettingsStore.Save(_settings);
            _dirty = false;
        }
    }
}
