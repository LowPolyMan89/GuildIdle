using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public static class ConfigSourceSettingsStore
    {
        public const string SettingsPath = "Assets/Editor/ConfigSources/config_sources.json";
        private const string DefaultRawOutputFolder = "ConfigDownloads";
        private const string DefaultRuntimeOutputFolder = "Assets/StreamingAssets/Configs";
        private const string LegacyDefaultOutputFolder = "Assets/Editor/ConfigSources/Downloaded";

        public static ConfigSourceSettingsCollection LoadOrCreate()
        {
            ConfigSourceSettingsCollection collection = null;

            if (File.Exists(SettingsPath))
            {
                try
                {
                    collection = JsonUtility.FromJson<ConfigSourceSettingsCollection>(
                        File.ReadAllText(SettingsPath, Encoding.UTF8));
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Failed to read config source settings '{SettingsPath}': {exception.Message}");
                }
            }

            return MergeWithDefaults(collection);
        }

        public static void Save(ConfigSourceSettingsCollection collection)
        {
            if (collection == null)
                collection = MergeWithDefaults(null);

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(MergeWithDefaults(collection), true), ConfigPipelineUtilities.Utf8NoBom);
            AssetDatabase.ImportAsset(SettingsPath);
            AssetDatabase.Refresh();
        }

        private static ConfigSourceSettingsCollection MergeWithDefaults(ConfigSourceSettingsCollection existing)
        {
            var sources = new List<ConfigSourceSettings>();
            var existingById = new Dictionary<string, ConfigSourceSettings>(StringComparer.OrdinalIgnoreCase);
            var defaultIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                defaultIds.Add(defaultSource.config_id);
            }

            if (existing?.sources != null)
            {
                foreach (var source in existing.sources)
                {
                    if (source == null ||
                        string.IsNullOrWhiteSpace(source.config_id) ||
                        defaultIds.Contains(source.config_id))
                    {
                        continue;
                    }

                    ApplyCustomMissingDefaults(source);
                    sources.Add(source);
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
            {
                source.output_json_path = defaultSource.output_json_path;
            }
            else if (IsLegacyDefaultOutputPath(source.config_id, source.output_json_path))
            {
                source.output_json_path = defaultSource.output_json_path;
                source.last_download_status = ConfigDownloadStatus.NotDownloaded;
                source.last_download_time = string.Empty;
                source.error_message = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(source.runtime_json_path))
                source.runtime_json_path = defaultSource.runtime_json_path;

            if (string.IsNullOrWhiteSpace(source.last_download_status))
                source.last_download_status = ConfigDownloadStatus.NotDownloaded;

            if (string.IsNullOrWhiteSpace(source.last_parse_status))
                source.last_parse_status = ConfigPipelineStatus.NotRun;

            if (string.IsNullOrWhiteSpace(source.last_validation_status))
                source.last_validation_status = ConfigPipelineStatus.NotRun;
        }

        private static void ApplyCustomMissingDefaults(ConfigSourceSettings source)
        {
            if (string.IsNullOrWhiteSpace(source.display_name))
                source.display_name = source.config_id;

            if (string.IsNullOrWhiteSpace(source.source_type))
                source.source_type = "GoogleSheet";

            if (string.IsNullOrWhiteSpace(source.output_json_path))
                source.output_json_path = $"{DefaultRawOutputFolder}/{source.config_id}.json";

            if (string.IsNullOrWhiteSpace(source.runtime_json_path))
                source.runtime_json_path = $"{DefaultRuntimeOutputFolder}/{source.config_id}.runtime.json";

            if (string.IsNullOrWhiteSpace(source.last_download_status))
                source.last_download_status = ConfigDownloadStatus.NotDownloaded;

            if (string.IsNullOrWhiteSpace(source.last_parse_status))
                source.last_parse_status = ConfigPipelineStatus.NotRun;

            if (string.IsNullOrWhiteSpace(source.last_validation_status))
                source.last_validation_status = ConfigPipelineStatus.NotRun;
        }

        private static bool IsLegacyDefaultOutputPath(string configId, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(configId) || string.IsNullOrWhiteSpace(outputPath))
                return false;

            var expected = $"{LegacyDefaultOutputFolder}/{configId}.json";
            return string.Equals(outputPath.Replace('\\', '/'), expected, StringComparison.OrdinalIgnoreCase);
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
                output_json_path = $"{DefaultRawOutputFolder}/{id}.json",
                runtime_json_path = $"{DefaultRuntimeOutputFolder}/{id}.runtime.json",
                last_download_status = ConfigDownloadStatus.NotDownloaded,
                last_download_time = string.Empty,
                last_parse_status = ConfigPipelineStatus.NotRun,
                last_parse_time = string.Empty,
                last_validation_status = ConfigPipelineStatus.NotRun,
                last_validation_time = string.Empty,
                error_message = string.Empty
            };
        }
    }
}
