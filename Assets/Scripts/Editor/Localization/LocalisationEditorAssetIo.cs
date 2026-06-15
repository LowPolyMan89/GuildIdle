using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GuildIdle;
using UnityEditor;
using UnityEngine;

namespace GuildIdle.Editor
{
    public sealed class LocalisationTableRecord
    {
        public string Id;
        public string Path;
        public LocalisationConfig Config;

        public int Count => Config?.Texts != null ? Config.Texts.Length : 0;
    }

    public sealed class LocalisationTextRecord
    {
        public LocalisationTableRecord Table;
        public LocalisationText Text;
        public int Index;

        public string Id => Text != null ? Text.Id : string.Empty;
    }

    public static class LocalisationEditorAssetIo
    {
        public const string DefaultNewTextPrefix = "new_text";
        public const string DefaultNewTablePrefix = "new_table";
        public const string KeyPattern = "^[a-z][a-z0-9_]*$";

        private static readonly Regex _keyRegex = new Regex(KeyPattern, RegexOptions.Compiled);

        public static List<LocalisationTableRecord> LoadTables()
        {
            Directory.CreateDirectory(LocalisationBuilder.SourceDirectory);

            var tables = new List<LocalisationTableRecord>();
            var files = Directory.GetFiles(LocalisationBuilder.SourceDirectory, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var unityPath = ToUnityPath(file);
                if (IsRuntimePath(unityPath))
                    continue;

                if (!TryReadConfig(unityPath, out var config, out var error))
                {
                    Debug.LogError(error);
                    config = new LocalisationConfig { Texts = Array.Empty<LocalisationText>() };
                }

                EnsureLanguageValues(config);
                tables.Add(new LocalisationTableRecord
                {
                    Id = Path.GetFileNameWithoutExtension(unityPath),
                    Path = unityPath,
                    Config = config
                });
            }

            return tables;
        }

        public static List<LocalisationTextRecord> GetTextRecords(LocalisationTableRecord table)
        {
            var records = new List<LocalisationTextRecord>();
            if (table?.Config?.Texts == null)
                return records;

            for (var i = 0; i < table.Config.Texts.Length; i++)
            {
                var text = table.Config.Texts[i];
                if (text == null)
                {
                    text = CreateDefaultEntry(CreateUniqueId(new[] { table }));
                    table.Config.Texts[i] = text;
                }

                EnsureLanguageValues(text);
                records.Add(new LocalisationTextRecord
                {
                    Table = table,
                    Text = text,
                    Index = i
                });
            }

            return records;
        }

        public static LocalisationText CreateDefaultEntry(string id)
        {
            return new LocalisationText
            {
                Id = id,
                Lang = CreateEmptyLanguageValues()
            };
        }

        public static LocalisationTableRecord CreateDefaultTable(string id)
        {
            return new LocalisationTableRecord
            {
                Id = id,
                Path = $"{LocalisationBuilder.SourceDirectory}/{id}.json",
                Config = new LocalisationConfig { Texts = Array.Empty<LocalisationText>() }
            };
        }

        public static string CreateUniqueTableId(IEnumerable<LocalisationTableRecord> tables)
        {
            var existingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var table in tables)
            {
                if (!string.IsNullOrWhiteSpace(table?.Id))
                    existingIds.Add(table.Id);
            }

            var index = 1;
            string id;
            do
            {
                id = $"{DefaultNewTablePrefix}_{index}";
                index++;
            }
            while (existingIds.Contains(id) || File.Exists($"{LocalisationBuilder.SourceDirectory}/{id}.json"));

            return id;
        }

        public static string CreateUniqueId(IEnumerable<LocalisationTableRecord> tables)
        {
            var existingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var table in tables)
            {
                if (table?.Config?.Texts == null)
                    continue;

                foreach (var text in table.Config.Texts)
                {
                    if (!string.IsNullOrWhiteSpace(text?.Id))
                        existingIds.Add(text.Id);
                }
            }

            var index = 1;
            string id;
            do
            {
                id = $"{DefaultNewTextPrefix}_{index}";
                index++;
            }
            while (existingIds.Contains(id));

            return id;
        }

        public static bool IsValidKeyFormat(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && _keyRegex.IsMatch(id);
        }

        public static bool IsValidTableIdFormat(string id)
        {
            return IsValidKeyFormat(id);
        }

        public static string GetValue(LocalisationText text, int languageIndex)
        {
            EnsureLanguageValues(text);
            if (text == null || languageIndex < 0 || languageIndex >= text.Lang.Length || text.Lang[languageIndex] == null)
                return string.Empty;

            return text.Lang[languageIndex].Value ?? string.Empty;
        }

        public static void SetValue(LocalisationText text, int languageIndex, string value)
        {
            EnsureLanguageValues(text);
            if (text == null || languageIndex < 0 || languageIndex >= text.Lang.Length)
                return;

            if (text.Lang[languageIndex] == null)
                text.Lang[languageIndex] = new LocalisationValue();

            text.Lang[languageIndex].Value = value ?? string.Empty;
        }

        public static ConfigValidationReport ValidateTables(IEnumerable<LocalisationTableRecord> tables)
        {
            var report = new ConfigValidationReport();
            var tableIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var table in tables)
            {
                if (table == null)
                    continue;

                if (string.IsNullOrWhiteSpace(table.Id))
                {
                    report.AddError($"Localisation table '{table.Path}' has empty id.");
                }
                else if (!IsValidTableIdFormat(table.Id))
                {
                    report.AddError($"Localisation table '{table.Id}' must match {KeyPattern}.");
                }
                else if (tableIds.TryGetValue(table.Id, out var existingPath))
                {
                    report.AddError($"Duplicate localisation table '{table.Id}' in '{table.Path}'; already used in '{existingPath}'.");
                }
                else
                {
                    tableIds.Add(table.Id, table.Path);
                }

                if (table.Config?.Texts == null)
                {
                    report.AddError($"Localisation table '{table.Path}' has no Texts array.");
                    continue;
                }

                for (var i = 0; i < table.Config.Texts.Length; i++)
                {
                    var text = table.Config.Texts[i];
                    ValidateText(report, table, text, i, ids);
                }
            }

            return report;
        }

        public static ConfigValidationReport ValidateEntry(IEnumerable<LocalisationTableRecord> tables, LocalisationText entry)
        {
            var report = ValidateTables(tables);
            if (entry == null)
                report.AddError("Selected localisation entry is missing.");

            return report;
        }

        public static string SaveTable(LocalisationTableRecord table)
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table));

            if (string.IsNullOrWhiteSpace(table.Path))
                throw new InvalidOperationException("Localisation table path is empty.");

            EnsureLanguageValues(table.Config);
            Directory.CreateDirectory(Path.GetDirectoryName(table.Path));
            File.WriteAllText(table.Path, JsonUtility.ToJson(table.Config, true), Encoding.UTF8);
            AssetDatabase.ImportAsset(table.Path);
            AssetDatabase.Refresh();

            LocalisationBuilder.BuildLocalisation();
            LocalisationModel.Reload();

            return table.Path;
        }

        public static void AddEntry(LocalisationTableRecord table, LocalisationText entry)
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table));

            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            EnsureConfig(table);
            var texts = new List<LocalisationText>(table.Config.Texts ?? Array.Empty<LocalisationText>());
            texts.Add(entry);
            table.Config.Texts = texts.ToArray();
        }

        public static void RemoveEntry(LocalisationTableRecord table, LocalisationText entry)
        {
            if (table?.Config?.Texts == null || entry == null)
                return;

            var texts = new List<LocalisationText>(table.Config.Texts);
            texts.Remove(entry);
            table.Config.Texts = texts.ToArray();
        }

        public static LocalisationText DuplicateEntry(LocalisationText source, string id)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var json = JsonUtility.ToJson(source, false);
            var copy = JsonUtility.FromJson<LocalisationText>(json);
            copy.Id = id;
            EnsureLanguageValues(copy);
            return copy;
        }

        public static bool TryReadConfig(string unityPath, out LocalisationConfig config, out string error)
        {
            config = null;
            error = null;

            try
            {
                config = JsonUtility.FromJson<LocalisationConfig>(File.ReadAllText(unityPath, Encoding.UTF8));
            }
            catch (Exception exception)
            {
                error = $"Could not read localisation table '{unityPath}': {exception.Message}";
                return false;
            }

            if (config == null)
                config = new LocalisationConfig();

            if (config.Texts == null)
                config.Texts = Array.Empty<LocalisationText>();

            return true;
        }

        private static void ValidateText(
            ConfigValidationReport report,
            LocalisationTableRecord table,
            LocalisationText text,
            int index,
            Dictionary<string, string> ids)
        {
            var source = $"{table.Id}[{index}]";
            if (text == null)
            {
                report.AddError($"{source} is null.");
                return;
            }

            if (string.IsNullOrWhiteSpace(text.Id))
            {
                report.AddError($"{source} has empty Id.");
            }
            else if (!IsValidKeyFormat(text.Id))
            {
                report.AddError($"{source} Id '{text.Id}' must match {KeyPattern}.");
            }
            else if (ids.TryGetValue(text.Id, out var existingSource))
            {
                report.AddError($"Duplicate localisation Id '{text.Id}' in {source}; already used in {existingSource}.");
            }
            else
            {
                ids.Add(text.Id, source);
            }

            if (text.Lang == null || text.Lang.Length != LocalisationModel.Languages.Length)
            {
                report.AddError($"{source} '{text.Id}' must contain exactly {LocalisationModel.Languages.Length} Lang values.");
                return;
            }

            for (var i = 0; i < text.Lang.Length; i++)
            {
                if (text.Lang[i] == null)
                    report.AddError($"{source} '{text.Id}' has null Lang[{i}].");
            }
        }

        private static void EnsureConfig(LocalisationTableRecord table)
        {
            if (table.Config == null)
                table.Config = new LocalisationConfig();

            if (table.Config.Texts == null)
                table.Config.Texts = Array.Empty<LocalisationText>();
        }

        private static void EnsureLanguageValues(LocalisationConfig config)
        {
            if (config?.Texts == null)
                return;

            foreach (var text in config.Texts)
                EnsureLanguageValues(text);
        }

        private static void EnsureLanguageValues(LocalisationText text)
        {
            if (text == null)
                return;

            var languages = LocalisationModel.Languages;
            if (text.Lang == null)
            {
                text.Lang = CreateEmptyLanguageValues();
                return;
            }

            if (text.Lang.Length != languages.Length)
            {
                var next = CreateEmptyLanguageValues();
                Array.Copy(text.Lang, next, Math.Min(text.Lang.Length, next.Length));
                text.Lang = next;
            }

            for (var i = 0; i < text.Lang.Length; i++)
            {
                if (text.Lang[i] == null)
                    text.Lang[i] = new LocalisationValue();

                if (text.Lang[i].Value == null)
                    text.Lang[i].Value = string.Empty;
            }
        }

        private static LocalisationValue[] CreateEmptyLanguageValues()
        {
            var languages = LocalisationModel.Languages;
            var values = new LocalisationValue[languages.Length];
            for (var i = 0; i < values.Length; i++)
                values[i] = new LocalisationValue { Value = string.Empty };

            return values;
        }

        private static bool IsRuntimePath(string unityPath)
        {
            return unityPath == LocalisationBuilder.OutputPath ||
                   unityPath.StartsWith(LocalisationBuilder.ResourcesDirectory + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string ToUnityPath(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
