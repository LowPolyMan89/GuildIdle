using System;
using UnityEditor;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public interface IConfigPipelineParser
    {
        bool Supports(ConfigSourceSettings source);
        ConfigPipelineReport ParseAndWrite(ConfigSourceSettings source);
        ConfigPipelineReport Validate(ConfigSourceSettings source);
    }

    public static class ConfigPipelineOperations
    {
        private static readonly IConfigPipelineParser[] _parsers =
        {
            new HeroesConfigsParser(),
            new ActivityConfigsParser(),
            new EnemiesConfigsParser(),
            new StorageConfigsParser(),
            new MapConfigsParser(),
            new ItemsConfigsParser(),
            new FormulaConfigsParser(),
            new LootConfigsParser(),
            new BuildingsConfigsParser(),
            new LocalisationConfigsParser()
        };

        public static void ParseEnabled(ConfigSourceSettingsCollection collection)
        {
            RunEnabled(collection, "Parsing configs", Parse);
            ApplyCrossConfigValidation(collection);
            ConfigSourceSettingsStore.Save(collection);
        }

        public static void ValidateEnabled(ConfigSourceSettingsCollection collection)
        {
            RunEnabled(collection, "Validating configs", Validate);
            ApplyCrossConfigValidation(collection);
            ConfigSourceSettingsStore.Save(collection);
        }

        public static void CrossValidate(ConfigSourceSettingsCollection collection)
        {
            ApplyCrossConfigValidation(collection);
            ConfigSourceSettingsStore.Save(collection);
        }

        public static void Parse(ConfigSourceSettings source, ConfigSourceSettingsCollection collection)
        {
            Parse(source);
            ApplyCrossConfigValidation(collection);
        }

        public static void Validate(ConfigSourceSettings source, ConfigSourceSettingsCollection collection)
        {
            Validate(source);
            ApplyCrossConfigValidation(collection);
        }

        public static void Parse(ConfigSourceSettings source)
        {
            if (!TryGetParser(source, out var parser))
            {
                SetParseStatus(source, ConfigPipelineStatus.Unsupported, $"No parser registered for '{source?.config_id}'.");
                Debug.LogError($"Config parse failed for '{GetSourceName(source)}': {source?.error_message}");
                return;
            }

            var report = parser.ParseAndWrite(source);
            LogReport("parse", source, report);
            if (report.Success)
            {
                source.last_parse_status = ConfigPipelineStatus.Success;
                source.last_parse_time = DateTime.UtcNow.ToString("o");
                source.last_validation_status = ConfigPipelineStatus.Success;
                source.last_validation_time = source.last_parse_time;
                source.error_message = report.ToDisplayMessage();
            }
            else
            {
                source.last_parse_status = ClassifyFailureStatus(report);
                source.error_message = report.ToDisplayMessage();
            }
        }

        public static void Validate(ConfigSourceSettings source)
        {
            if (!TryGetParser(source, out var parser))
            {
                SetValidationStatus(source, ConfigPipelineStatus.Unsupported, $"No validator registered for '{source?.config_id}'.");
                Debug.LogError($"Config validation failed for '{GetSourceName(source)}': {source?.error_message}");
                return;
            }

            var report = parser.Validate(source);
            LogReport("validation", source, report);
            if (report.Success)
            {
                source.last_validation_status = ConfigPipelineStatus.Success;
                source.last_validation_time = DateTime.UtcNow.ToString("o");
                source.error_message = report.ToDisplayMessage();
            }
            else
            {
                source.last_validation_status = ClassifyFailureStatus(report);
                source.error_message = report.ToDisplayMessage();
            }
        }

        public static bool HasParser(ConfigSourceSettings source)
        {
            return TryGetParser(source, out _);
        }

        private static void RunEnabled(
            ConfigSourceSettingsCollection collection,
            string progressTitle,
            Action<ConfigSourceSettings> action)
        {
            if (collection?.sources == null)
                return;

            var enabledCount = 0;
            foreach (var source in collection.sources)
            {
                if (source != null && source.enabled)
                    enabledCount++;
            }

            try
            {
                var index = 0;
                foreach (var source in collection.sources)
                {
                    if (source == null || !source.enabled)
                        continue;

                    EditorUtility.DisplayProgressBar(
                        progressTitle,
                        $"{source.display_name} ({index + 1}/{enabledCount})",
                        enabledCount == 0 ? 1f : (float)index / enabledCount);
                    action(source);
                    index++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ConfigSourceSettingsStore.Save(collection);
        }

        private static bool TryGetParser(ConfigSourceSettings source, out IConfigPipelineParser parser)
        {
            parser = null;
            if (source == null)
                return false;

            foreach (var candidate in _parsers)
            {
                if (candidate.Supports(source))
                {
                    parser = candidate;
                    return true;
                }
            }

            return false;
        }

        private static string ClassifyFailureStatus(ConfigPipelineReport report)
        {
            if (report.Issues.Count > 0)
                return ConfigPipelineStatus.ValidationError;

            if (!string.IsNullOrWhiteSpace(report.ErrorMessage) &&
                report.ErrorMessage.StartsWith("Raw JSON is missing:", StringComparison.OrdinalIgnoreCase))
            {
                return ConfigPipelineStatus.MissingRaw;
            }

            if (!string.IsNullOrWhiteSpace(report.ErrorMessage) &&
                report.ErrorMessage.StartsWith("Could not write runtime JSON", StringComparison.OrdinalIgnoreCase))
            {
                return ConfigPipelineStatus.WriteError;
            }

            return ConfigPipelineStatus.ParseError;
        }

        private static void LogReport(string operation, ConfigSourceSettings source, ConfigPipelineReport report)
        {
            if (report == null)
            {
                Debug.LogError($"Config {operation} failed for '{GetSourceName(source)}': parser returned no report.");
                return;
            }

            var sourceName = GetSourceName(source);
            var message = report.ToDisplayMessage();
            if (!report.Success)
            {
                Debug.LogError(string.IsNullOrWhiteSpace(message)
                    ? $"Config {operation} failed for '{sourceName}'."
                    : $"Config {operation} failed for '{sourceName}':\n{message}");
                return;
            }

            if (report.Warnings.Count > 0)
            {
                Debug.LogWarning($"Config {operation} completed with warnings for '{sourceName}':\n{message}");
                return;
            }

            Debug.Log($"Config {operation} succeeded for '{sourceName}'.");
        }

        private static string GetSourceName(ConfigSourceSettings source)
        {
            if (source == null)
                return "<null>";

            return !string.IsNullOrWhiteSpace(source.display_name)
                ? source.display_name
                : source.config_id;
        }

        private static void SetParseStatus(ConfigSourceSettings source, string status, string message)
        {
            if (source == null)
                return;

            source.last_parse_status = status;
            source.error_message = message ?? string.Empty;
        }

        private static void SetValidationStatus(ConfigSourceSettings source, string status, string message)
        {
            if (source == null)
                return;

            source.last_validation_status = status;
            source.error_message = message ?? string.Empty;
        }

        private static void ApplyCrossConfigValidation(ConfigSourceSettingsCollection collection)
        {
            ConfigCrossConfigValidator.ApplyToSources(collection);
        }
    }
}
