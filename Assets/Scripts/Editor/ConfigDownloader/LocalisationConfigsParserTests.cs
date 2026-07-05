using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class LocalisationConfigsParserTests
    {
        private const string TestRawPath = "Temp/ConfigParserTests/localisation.raw.json";
        private const string TestRuntimePath = "Assets/StreamingAssets/Configs/localisation_configs.test.runtime.json";

        [TearDown]
        public void TearDown()
        {
            DeleteProjectFile(TestRawPath);
            DeleteProjectFile(TestRuntimePath);
            DeleteProjectFile(TestRuntimePath + ".tmp");
            DeleteProjectFile(TestRuntimePath + ".meta");
        }

        [Test]
        public void BuildRuntimeJson_ExportsStableShapeAndLowercaseLanguageKeys()
        {
            WriteRaw(CreateValidDownload());

            var report = new LocalisationConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"localisations\""));
            Assert.That(runtimeJson, Does.Contain("\"id\": \"hero.aska.name\""));
            Assert.That(runtimeJson, Does.Contain("\"ru\": \"Aska RU\""));
            Assert.That(runtimeJson, Does.Contain("\"en\": \"Aska EN\""));
            Assert.That(runtimeJson, Does.Contain("\"tr\": \"Aska TR\""));
            Assert.That(runtimeJson, Does.Not.Contain("\"Ru\""));
            Assert.That(runtimeJson, Does.Not.Contain("\"En\""));
            Assert.That(runtimeJson, Does.Not.Contain("\"Tr\""));
            Assert.That(runtimeJson, Does.Not.Contain("README"));
            Assert.That(runtimeJson, Does.Not.Contain("notes"));
            Assert.That(runtimeJson, Does.Not.Contain("cells"));
            Assert.That(runtimeJson, Does.Not.Contain("downloaded_at_utc"));
        }

        [Test]
        public void BuildRuntimeJson_IgnoresCompletelyEmptyRows()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Localisation").rows = Append(
                FindSheet(download, "Localisation").rows,
                Row("", "", "", ""));
            WriteRaw(download);

            var report = new LocalisationConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(CountOccurrences(runtimeJson, "\"id\":"), Is.EqualTo(3));
        }

        [Test]
        public void BuildRuntimeJson_PreservesCommasQuotesApostrophesAndNewlines()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Localisation").rows[1].cells[2] = "Hello, \"traveler\"\nIt's time: go / gather";
            WriteRaw(download);

            var report = new LocalisationConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("Hello, \\\"traveler\\\"\\nIt's time: go / gather"));
        }

        [Test]
        public void BuildRuntimeJson_ReportsDuplicateIds()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Localisation").rows = Append(
                FindSheet(download, "Localisation").rows,
                Row("hero.aska.name", "Duplicate RU", "Duplicate EN", "Duplicate TR"));
            WriteRaw(download);

            var report = new LocalisationConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("Duplicate id; first declared at row 2."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsIdWithMissingTranslations()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Localisation").rows = Append(
                FindSheet(download, "Localisation").rows,
                Row("item.bad.name", "", "Bad EN", ""));
            WriteRaw(download);

            var report = new LocalisationConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("Ru translation is required."));
            Assert.That(message, Does.Contain("Tr translation is required."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsTranslationWithoutId()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Localisation").rows = Append(
                FindSheet(download, "Localisation").rows,
                Row("", "Only RU", "", ""));
            WriteRaw(download);

            var report = new LocalisationConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("id is required when any translation is set."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsInvalidIds()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Localisation").rows = Append(
                FindSheet(download, "Localisation").rows,
                Row("bad id", "RU", "EN", "TR"),
                Row(".bad", "RU", "EN", "TR"),
                Row("bad..id", "RU", "EN", "TR"),
                Row("bad-id", "RU", "EN", "TR"));
            WriteRaw(download);

            var report = new LocalisationConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("id must not contain whitespace."));
            Assert.That(message, Does.Contain("id must not start or end with dot or underscore."));
            Assert.That(message, Does.Contain("id must not contain double dots."));
            Assert.That(message, Does.Contain("id may contain only latin letters, digits, dot, and underscore."));
        }

        [Test]
        public void BuildRuntimeJson_AcceptsCaseInsensitiveLanguageHeaders()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Localisation").rows[0].cells = new[] { "id", "ru", "EN", "tr" };
            WriteRaw(download);

            var report = new LocalisationConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"ru\": \"Aska RU\""));
            Assert.That(runtimeJson, Does.Contain("\"en\": \"Aska EN\""));
            Assert.That(runtimeJson, Does.Contain("\"tr\": \"Aska TR\""));
        }

        [Test]
        public void BuildRuntimeJson_MissingExternalRegistriesProduceWarningsOnly()
        {
            WriteRaw(CreateValidDownload());

            var report = new LocalisationConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.True, message);
            Assert.That(runtimeJson, Does.Contain("\"localisations\""));
            Assert.That(message, Does.Contain("Warning: Cross-config validation skipped: Buildings Configs registry is not available yet."));
            Assert.That(message, Does.Contain("Warning: Cross-config validation skipped: Items Configs registry is not available yet."));
        }

        [Test]
        public void ParseAndWrite_DoesNotOverwriteExistingRuntimeWhenValidationFails()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Localisation").rows[1].cells[2] = "";
            WriteRaw(download);
            WriteProjectFile(TestRuntimePath, "{\"previous\":true}\n");

            var report = new LocalisationConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.False);
            Assert.That(ReadProjectFile(TestRuntimePath), Is.EqualTo("{\"previous\":true}\n"));
        }

        [Test]
        public void ParseAndWrite_WritesRuntimeOnlyAfterSuccessfulValidation()
        {
            WriteRaw(CreateValidDownload());

            var report = new LocalisationConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(ReadProjectFile(TestRuntimePath), Does.Contain("\"localisations\""));
        }

        private static ConfigSourceSettings CreateSource()
        {
            return new ConfigSourceSettings
            {
                config_id = "localisation",
                output_json_path = TestRawPath,
                runtime_json_path = TestRuntimePath
            };
        }

        private static ConfigSheetDownload CreateValidDownload()
        {
            return new ConfigSheetDownload
            {
                config_id = "localisation",
                display_name = "GuildIdle - Localisation",
                source_type = "GoogleSheet",
                sheet_url = "https://docs.google.com/spreadsheets/d/test",
                downloaded_at_utc = "2026-07-05T00:00:00Z",
                sheets = new[]
                {
                    Sheet("Localisation",
                        Row("id", "Ru", "En", "Tr"),
                        Row("hero.aska.name", "Aska RU", "Aska EN", "Aska TR"),
                        Row("hero.aska.description", "Hunter RU", "Hunter EN", "Hunter TR"),
                        Row("gold_name_id", "Gold RU", "Gold EN", "Gold TR")),
                    Sheet("README",
                        Row("Section", "Description"),
                        Row("Notes", "This sheet must not be emitted."))
                }
            };
        }

        private static ConfigDownloadedSheet Sheet(string name, params ConfigSheetRow[] rows)
        {
            return new ConfigDownloadedSheet
            {
                sheet_name = name,
                rows = rows
            };
        }

        private static ConfigSheetRow Row(params string[] cells)
        {
            return new ConfigSheetRow { cells = cells };
        }

        private static ConfigSheetRow[] Append(ConfigSheetRow[] rows, params ConfigSheetRow[] appendedRows)
        {
            var list = new List<ConfigSheetRow>(rows);
            list.AddRange(appendedRows);
            return list.ToArray();
        }

        private static ConfigDownloadedSheet FindSheet(ConfigSheetDownload download, string sheetName)
        {
            foreach (var sheet in download.sheets)
            {
                if (string.Equals(sheet.sheet_name, sheetName, StringComparison.OrdinalIgnoreCase))
                    return sheet;
            }

            throw new InvalidOperationException($"Missing test sheet {sheetName}.");
        }

        private static int CountOccurrences(string value, string needle)
        {
            var count = 0;
            var index = 0;
            while (index >= 0)
            {
                index = value.IndexOf(needle, index, StringComparison.Ordinal);
                if (index < 0)
                    break;

                count++;
                index += needle.Length;
            }

            return count;
        }

        private static void WriteRaw(ConfigSheetDownload download)
        {
            WriteProjectFile(TestRawPath, JsonUtility.ToJson(download, true));
        }

        private static void WriteProjectFile(string projectPath, string text)
        {
            var fullPath = FullProjectPath(projectPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, text, ConfigPipelineUtilities.Utf8NoBom);
        }

        private static string ReadProjectFile(string projectPath)
        {
            return File.ReadAllText(FullProjectPath(projectPath), Encoding.UTF8);
        }

        private static void DeleteProjectFile(string projectPath)
        {
            var fullPath = FullProjectPath(projectPath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }

        private static string FullProjectPath(string projectPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, projectPath.Replace('\\', '/')));
        }
    }
}
