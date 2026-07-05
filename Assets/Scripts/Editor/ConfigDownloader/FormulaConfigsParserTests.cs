using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class FormulaConfigsParserTests
    {
        private const string TestRawPath = "Temp/ConfigParserTests/formula_configs.raw.json";
        private const string TestRuntimePath = "Assets/StreamingAssets/Configs/formula_configs.test.runtime.json";

        [TearDown]
        public void TearDown()
        {
            DeleteProjectFile(TestRawPath);
            DeleteProjectFile(TestRuntimePath);
            DeleteProjectFile(TestRuntimePath + ".tmp");
            DeleteProjectFile(TestRuntimePath + ".meta");
        }

        [Test]
        public void BuildRuntimeJson_ExportsStableArraysAndIgnoresDesignerColumns()
        {
            WriteRaw(CreateValidDownload());

            var report = new FormulaConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"heroDerivedStats\""));
            Assert.That(runtimeJson, Does.Contain("\"skillStatWeights\""));
            Assert.That(runtimeJson, Does.Contain("\"formulaId\": \"hero_melee_damage_min\""));
            Assert.That(runtimeJson, Does.Contain("\"primaryStatMultiplier\": 0.4"));
            Assert.That(runtimeJson, Does.Contain("\"weight\": 0.5"));
            Assert.That(runtimeJson, Does.Contain("\"enabled\": true"));
            Assert.That(runtimeJson, Does.Not.Contain("notes"));
            Assert.That(runtimeJson, Does.Not.Contain("expression_preview"));
            Assert.That(runtimeJson, Does.Not.Contain("cells"));
            Assert.That(runtimeJson, Does.Not.Contain("Display name"));
        }

        [Test]
        public void BuildRuntimeJson_ParsesDecimalCommaAndDecimalDotAsNumbers()
        {
            WriteRaw(CreateValidDownload());

            var report = new FormulaConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"primaryStatMultiplier\": 0.4"));
            Assert.That(runtimeJson, Does.Contain("\"weight\": 0.5"));
            Assert.That(runtimeJson, Does.Contain("\"baseValue\": 1.5"));
        }

        [Test]
        public void BuildRuntimeJson_ReportsFormulaTypeSpecificRequiredFields()
        {
            var download = CreateValidDownload();
            FindSheet(download, "HeroDerivedStats").rows[1].cells[5] = "";
            FindSheet(download, "HeroDerivedStats").rows[2].cells[10] = "";
            FindSheet(download, "HeroDerivedStats").rows[3].cells[13] = "0";
            WriteRaw(download);

            var report = new FormulaConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("HeroDerivedStats row 2 column 'primary_stat'"));
            Assert.That(message, Does.Contain("HeroDerivedStats row 3 column 'weapon_value_mode'"));
            Assert.That(message, Does.Contain("HeroDerivedStats row 4 column 'cap_value' value '0'"));
        }

        [Test]
        public void BuildRuntimeJson_WarnsWhenEnabledProfileWeightSumIsNotOne()
        {
            var download = CreateValidDownload();
            FindSheet(download, "SkillStatWeights").rows[2].cells[4] = "0.4";
            WriteRaw(download);

            var report = new FormulaConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var warnings = string.Join("\n", report.Warnings);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(warnings, Does.Contain("profile_gathering"));
            Assert.That(warnings, Does.Contain("expected 1.0"));
        }

        [Test]
        public void BuildRuntimeJson_ReportsSkillWeightValidationErrors()
        {
            var download = CreateValidDownload();
            var weights = FindSheet(download, "SkillStatWeights");
            weights.rows = Append(
                weights.rows,
                Row("profile_gathering", "skill_gathering", "Duplicate", "Strength", "0.1", "TRUE", "note"),
                Row("profile_bad", "skill_bad", "Bad stat", "Wisdom", "-1", "MAYBE", "note"));
            WriteRaw(download);

            var report = new FormulaConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("Duplicate profile_id + stat_id pair"));
            Assert.That(message, Does.Contain("stat_id is not an allowed hero stat id"));
            Assert.That(message, Does.Contain("weight must be greater than or equal to 0"));
            Assert.That(message, Does.Contain("Expected TRUE or FALSE"));
        }

        [Test]
        public void ParseAndWrite_DoesNotOverwriteExistingRuntimeWhenValidationFails()
        {
            var download = CreateValidDownload();
            FindSheet(download, "HeroDerivedStats").rows[1].cells[16] = "MAYBE";
            WriteRaw(download);
            WriteProjectFile(TestRuntimePath, "{\"previous\":true}\n");

            var report = new FormulaConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.False);
            Assert.That(ReadProjectFile(TestRuntimePath), Is.EqualTo("{\"previous\":true}\n"));
        }

        [Test]
        public void ParseAndWrite_WritesRuntimeOnlyAfterSuccessfulValidation()
        {
            WriteRaw(CreateValidDownload());

            var report = new FormulaConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(ReadProjectFile(TestRuntimePath), Does.Contain("\"heroDerivedStats\""));
        }

        private static ConfigSourceSettings CreateSource()
        {
            return new ConfigSourceSettings
            {
                config_id = "formula_configs",
                output_json_path = TestRawPath,
                runtime_json_path = TestRuntimePath
            };
        }

        private static ConfigSheetDownload CreateValidDownload()
        {
            return new ConfigSheetDownload
            {
                config_id = "formula_configs",
                display_name = "GuildIdle - Formula Configs",
                source_type = "GoogleSheet",
                sheet_url = "https://docs.google.com/spreadsheets/d/test",
                downloaded_at_utc = "2026-07-05T00:00:00Z",
                sheets = new[]
                {
                    Sheet("HeroDerivedStats",
                        Row("formula_id", "derived_stat_id", "Display name", "formula_type", "base_value", "primary_stat", "primary_stat_multiplier", "secondary_stat", "secondary_stat_multiplier", "level_multiplier", "weapon_value_mode", "min_value", "max_value", "cap_value", "value_type", "rounding", "enabled", "notes", "expression_preview"),
                        Row("hero_max_hp", "max_hp", "Max HP", "linear_stat_with_level", "50", "Endurance", "8", "", "", "2", "", "1", "", "", "number", "floor", "TRUE", "note", "preview"),
                        Row("hero_melee_damage_min", "melee_damage_min", "Damage min", "weapon_damage_linear_stat", "", "Strength", "0,4", "", "", "0", "weapon_damage_min", "0", "", "", "number", "floor", "TRUE", "note", "preview"),
                        Row("hero_crit_multiplier", "crit_multiplier", "Crit multiplier", "linear_stat_capped", "1,5", "Luck", "0.01", "", "", "0", "", "1", "", "2", "multiplier", "round_2", "TRUE", "note", "preview")),
                    Sheet("SkillStatWeights",
                        Row("profile_id", "skill_id", "Display name", "stat_id", "weight", "enabled", "notes"),
                        Row("profile_gathering", "skill_gathering", "Gathering", "Strength", "0.5", "TRUE", "note"),
                        Row("profile_gathering", "skill_gathering", "Gathering", "Agility", "0,5", "TRUE", "note"),
                        Row("profile_production", "skill_production", "Production", "Intelligence", "1,00", "TRUE", "note"))
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
