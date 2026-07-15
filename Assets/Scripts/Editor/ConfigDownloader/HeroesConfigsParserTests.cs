using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class HeroesConfigsParserTests
    {
        private const string TestRawPath = "Temp/ConfigParserTests/heroes_configs.raw.json";
        private const string TestRuntimePath = "Assets/StreamingAssets/Configs/heroes_configs.test.runtime.json";

        [TearDown]
        public void TearDown()
        {
            DeleteProjectFile(TestRawPath);
            DeleteProjectFile(TestRuntimePath);
            DeleteProjectFile(TestRuntimePath + ".tmp");
            DeleteProjectFile(TestRuntimePath + ".meta");
        }

        [Test]
        public void BuildRuntimeJson_ExportsHeroesGrowthSkillsAndEffects()
        {
            WriteRaw(CreateValidDownload());

            var report = new HeroesConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"heroes\""));
            Assert.That(runtimeJson, Does.Contain("\"heroGrowth\""));
            Assert.That(runtimeJson, Does.Contain("\"heroUniqueSkills\""));
            Assert.That(runtimeJson, Does.Contain("\"heroSkillEffects\""));
            Assert.That(runtimeJson, Does.Contain("\"heroId\": \"aska\""));
            Assert.That(runtimeJson, Does.Contain("\"uniqueSkillIds\": [\"gatherer\", \"melee_dodge\"]"));
            Assert.That(runtimeJson, Does.Contain("\"nameId\": \"hero.aska.name\""));
            Assert.That(runtimeJson, Does.Contain("\"strength\": 2"));
            Assert.That(runtimeJson, Does.Not.Contain("GrowthProfileId"));
            Assert.That(runtimeJson, Does.Not.Contain("Notes"));
            Assert.That(runtimeJson, Does.Not.Contain("cells"));
        }

        [Test]
        public void RuntimeGrowthDtoDoesNotDeclareOrSerializeLegacyFormulaField()
        {
            var dto = new GuildIdle.Configs.HeroGrowthConfigDto { heroId = "aska", level = 1, requiredSkillPoints = 0 };
            var json = JsonUtility.ToJson(dto);

            Assert.That(typeof(GuildIdle.Configs.HeroGrowthConfigDto).GetField("skillPointsFormulaId"), Is.Null);
            Assert.That(json, Does.Not.Contain("skillPointsFormulaId"));
        }

        [Test]
        public void BuildRuntimeJson_GeneratesGrowthFromProfileAndMilestones()
        {
            WriteRaw(CreateValidDownload());

            var report = new HeroesConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"heroId\": \"aska\",\n      \"level\": 2,\n      \"requiredSkillPoints\": 5,\n      \"addStrength\": 0,\n      \"addAgility\": 1"));
            Assert.That(runtimeJson, Does.Contain("\"heroId\": \"aska\",\n      \"level\": 10,\n      \"requiredSkillPoints\": 45,\n      \"addStrength\": 0,\n      \"addAgility\": 1,\n      \"addIntelligence\": 0,\n      \"addLuck\": 2"));
            Assert.That(runtimeJson, Does.Contain("\"heroId\": \"ren\",\n      \"level\": 3,\n      \"requiredSkillPoints\": 10,\n      \"addStrength\": 1"));
            Assert.That(CountOccurrences(runtimeJson, "\"level\":"), Is.EqualTo(14));
        }

        [Test]
        public void BuildRuntimeJson_ExportsUniqueSkillUiFields()
        {
            WriteRaw(CreateValidDownload());

            var report = new HeroesConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"skillId\": \"gatherer\""));
            Assert.That(runtimeJson, Does.Contain("\"nameId\": \"hero_skill.gatherer.name\""));
            Assert.That(runtimeJson, Does.Contain("\"descriptionId\": \"hero_skill.gatherer.description\""));
            Assert.That(runtimeJson, Does.Contain("\"iconId\": \"icon_skill_gatherer\""));
            Assert.That(runtimeJson, Does.Contain("\"enabled\": true"));
        }

        [Test]
        public void BuildRuntimeJson_ExportsSkillEffectMechanics()
        {
            WriteRaw(CreateValidDownload());

            var report = new HeroesConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"effectId\": \"gatherer_add_raspberry\""));
            Assert.That(runtimeJson, Does.Contain("\"trigger\": \"OnWorkCycleComplete\""));
            Assert.That(runtimeJson, Does.Contain("\"condition\": \"activity_category=Hunting\""));
            Assert.That(runtimeJson, Does.Contain("\"chancePercent\": 10"));
            Assert.That(runtimeJson, Does.Contain("\"interval\": 0"));
            Assert.That(runtimeJson, Does.Contain("\"effect\": \"AddItem\""));
            Assert.That(runtimeJson, Does.Contain("\"target\": \"resource_raspberry\""));
            Assert.That(runtimeJson, Does.Contain("\"value\": \"1\""));
            Assert.That(runtimeJson, Does.Contain("\"stackMode\": \"Independent\""));
            Assert.That(runtimeJson, Does.Contain("\"cooldownSeconds\": 0"));
        }

        [Test]
        public void BuildRuntimeJson_IgnoresEmptyRows()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Heroes").rows = Append(FindSheet(download, "Heroes").rows, Row("", "", "", "", ""));
            FindSheet(download, "HeroGrowthProfiles").rows = Append(FindSheet(download, "HeroGrowthProfiles").rows, Row("", "", "", "", ""));
            FindSheet(download, "HeroGrowthMilestones").rows = Append(FindSheet(download, "HeroGrowthMilestones").rows, Row("", "", "", "", ""));
            FindSheet(download, "HeroUniqueSkills").rows = Append(FindSheet(download, "HeroUniqueSkills").rows, Row("", "", "", "", ""));
            FindSheet(download, "HeroSkillEffects").rows = Append(FindSheet(download, "HeroSkillEffects").rows, Row("", "", "", "", ""));
            WriteRaw(download);

            var report = new HeroesConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(CountOccurrences(runtimeJson, "\"heroId\": \"aska\""), Is.GreaterThan(1));
            Assert.That(CountOccurrences(runtimeJson, "\"skillId\": \"gatherer\""), Is.EqualTo(2));
        }

        [Test]
        public void BuildRuntimeJson_ReportsRequiredAndTypedFieldErrors()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Heroes").rows[1].cells[3] = "";
            FindSheet(download, "Heroes").rows[1].cells[2] = "MAYBE";
            FindSheet(download, "HeroGrowthProfiles").rows[1].cells[1] = "0";
            FindSheet(download, "HeroGrowthMilestones").rows[1].cells[2] = "ReplaceProfile";
            FindSheet(download, "HeroUniqueSkills").rows[1].cells[5] = "";
            FindSheet(download, "HeroSkillEffects").rows[1].cells[4] = "150";
            WriteRaw(download);

            var report = new HeroesConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("RarityId is required"));
            Assert.That(message, Does.Contain("Expected TRUE or FALSE"));
            Assert.That(message, Does.Contain("Expected an integer greater than or equal to 1"));
            Assert.That(message, Does.Contain("Unsupported ApplyMode"));
            Assert.That(message, Does.Contain("IconId is required"));
            Assert.That(message, Does.Contain("Expected a number between 0 and 100"));
        }

        [Test]
        public void BuildRuntimeJson_ReportsMissingCrossReferences()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Heroes").rows[1].cells[5] = "missing_profile";
            FindSheet(download, "HeroGrowthMilestones").rows[1].cells[0] = "orphan_profile";
            FindSheet(download, "HeroUniqueSkills").rows[1].cells[0] = "missing_hero";
            FindSheet(download, "HeroSkillEffects").rows[1].cells[0] = "missing_skill";
            WriteRaw(download);

            var report = new HeroesConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("GrowthProfileId does not exist in HeroGrowthProfiles"));
            Assert.That(message, Does.Contain("HeroId does not exist in Heroes"));
            Assert.That(message, Does.Contain("SkillId does not exist in HeroUniqueSkills"));
        }

        [Test]
        public void BuildRuntimeJson_ReportsDuplicateEffectIdWithinSkill()
        {
            var download = CreateValidDownload();
            FindSheet(download, "HeroSkillEffects").rows = Append(
                FindSheet(download, "HeroSkillEffects").rows,
                Row("gatherer", "gatherer_add_raspberry", "OnWorkCycleComplete", "activity_category=Hunting", "10", "0", "AddItem", "resource_raspberry", "1", "Independent", "0", "duplicate"));
            WriteRaw(download);

            var report = new HeroesConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("Duplicate SkillId + EffectId"));
        }

        [Test]
        public void ParseAndWrite_DoesNotOverwriteExistingRuntimeWhenValidationFails()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Heroes").rows[1].cells[1] = "";
            WriteRaw(download);
            WriteProjectFile(TestRuntimePath, "{\"previous\":true}\n");

            var report = new HeroesConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.False);
            Assert.That(ReadProjectFile(TestRuntimePath), Is.EqualTo("{\"previous\":true}\n"));
        }

        [Test]
        public void ParseAndWrite_WritesRuntimeOnlyAfterSuccessfulValidation()
        {
            WriteRaw(CreateValidDownload());

            var report = new HeroesConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(ReadProjectFile(TestRuntimePath), Does.Contain("\"heroes\""));
        }

        private static ConfigSourceSettings CreateSource()
        {
            return new ConfigSourceSettings
            {
                config_id = "heroes_configs",
                output_json_path = TestRawPath,
                runtime_json_path = TestRuntimePath
            };
        }

        private static ConfigSheetDownload CreateValidDownload()
        {
            return new ConfigSheetDownload
            {
                config_id = "heroes_configs",
                display_name = "GuildIdle - Heroes Configs",
                source_type = "GoogleSheet",
                sheet_url = "https://docs.google.com/spreadsheets/d/test",
                downloaded_at_utc = "2026-07-06T00:00:00Z",
                sheets = new[]
                {
                    Sheet("Heroes",
                        Row("SortOrder", "HeroId", "Enabled", "RarityId", "TypeId", "GrowthProfileId", "ProfessionIds", "FullSpriteId", "IconSpriteId", "BattleSpriteId", "NameId", "DescriptionId", "BaseStrength", "BaseAgility", "BaseIntelligence", "BaseLuck", "BaseEndurance", "Notes"),
                        Row("1", "aska", "TRUE", "Rare", "scout", "aska_agility_growth", "ranger|herbalist", "hero_aska_full", "hero_aska_icon", "hero_aska_battle", "hero.aska.name", "hero.aska.description", "2", "8", "2", "4", "4", "note"),
                        Row("2", "ren", "TRUE", "Common", "adventurer", "ren_worker_growth", "guild_apprentice", "hero_ren_full", "hero_ren_icon", "hero_ren_battle", "hero.ren.name", "hero.ren.description", "4", "2", "2", "2", "5", "note")),
                    Sheet("HeroGrowthProfiles",
                        Row("GrowthProfileId", "MaxLevel", "AddStrengthEvery", "AddAgilityEvery", "AddIntelligenceEvery", "AddLuckEvery", "AddEnduranceEvery", "GenerationMode", "Notes"),
                        Row("aska_agility_growth", "10", "0", "1", "0", "5", "0", "PeriodicPlusMilestones", "note"),
                        Row("ren_worker_growth", "6", "3", "0", "0", "0", "3", "PeriodicPlusMilestones", "note")),
                    Sheet("HeroGrowthMilestones",
                        Row("GrowthProfileId", "Level", "ApplyMode", "RequiredSkillPointsOverride", "AddStrength", "AddAgility", "AddIntelligence", "AddLuck", "AddEndurance", "MilestoneId", "Comment"),
                        Row("aska_agility_growth", "10", "AddToProfile", "", "0", "0", "0", "1", "0", "aska_lvl_10", "comment"),
                        Row("ren_worker_growth", "6", "AddToProfile", "99", "1", "0", "0", "0", "1", "ren_lvl_6", "comment")),
                    Sheet("HeroUniqueSkills",
                        Row("HeroId", "SkillId", "Type", "NameId", "DescriptionId", "IconId", "Enabled", "Notes"),
                        Row("aska", "gatherer", "Peaceful", "hero_skill.gatherer.name", "hero_skill.gatherer.description", "icon_skill_gatherer", "TRUE", "note"),
                        Row("aska", "melee_dodge", "Combat", "hero_skill.melee_dodge.name", "hero_skill.melee_dodge.description", "icon_skill_melee_dodge", "TRUE", "note"),
                        Row("ren", "reliable_hands", "Peaceful", "hero_skill.reliable_hands.name", "hero_skill.reliable_hands.description", "icon_skill_reliable_hands", "TRUE", "note"),
                        Row("ren", "will_to_live", "Combat", "hero_skill.will_to_live.name", "hero_skill.will_to_live.description", "icon_skill_will_to_live", "TRUE", "note")),
                    Sheet("HeroSkillEffects",
                        Row("SkillId", "EffectId", "Trigger", "Condition", "ChancePercent", "Interval", "Effect", "Target", "Value", "StackMode", "CooldownSeconds", "Notes"),
                        Row("gatherer", "gatherer_add_raspberry", "OnWorkCycleComplete", "activity_category=Hunting", "10", "0", "AddItem", "resource_raspberry", "1", "Independent", "0", "note"),
                        Row("melee_dodge", "melee_dodge_attack", "OnIncomingAttack", "attack_range=Melee", "5", "0", "DodgeAttack", "self", "1", "Independent", "0", "note"),
                        Row("reliable_hands", "reliable_hands_extra_resource", "OnWorkCycleComplete", "activity_category=Gathering", "100", "5", "AddExtraBaseResource", "completed_work_base_resource", "1", "Independent", "0", "note"),
                        Row("will_to_live", "will_to_live_prevent_death", "OnLethalDamageTaken", "damage_would_kill=true", "25", "0", "PreventDeath", "self", "1", "Independent", "0", "note"))
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

        private static int CountOccurrences(string value, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }
    }
}
