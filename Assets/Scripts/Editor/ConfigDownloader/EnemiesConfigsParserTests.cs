using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class EnemiesConfigsParserTests
    {
        private const string TestRawPath = "Temp/ConfigParserTests/enemies_configs.raw.json";
        private const string TestActivityRawPath = "Temp/ConfigParserTests/activity_configs.cross.raw.json";
        private const string TestRuntimePath = "Assets/StreamingAssets/Configs/enemies_configs.test.runtime.json";

        [TearDown]
        public void TearDown()
        {
            DeleteProjectFile(TestRawPath);
            DeleteProjectFile(TestActivityRawPath);
            DeleteProjectFile(TestRuntimePath);
            DeleteProjectFile(TestRuntimePath + ".tmp");
            DeleteProjectFile(TestRuntimePath + ".meta");
        }

        [Test]
        public void BuildRuntimeJson_UsesHeadersAndExcludesReadmeNotesAndRawCells()
        {
            WriteRaw(CreateValidDownload());

            var report = new EnemiesConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"enemies\""));
            Assert.That(runtimeJson, Does.Contain("\"enemyLevels\""));
            Assert.That(runtimeJson, Does.Contain("\"enemyLoot\""));
            Assert.That(runtimeJson, Does.Contain("\"enemyAbilities\""));
            Assert.That(runtimeJson, Does.Contain("\"combatStatuses\""));
            Assert.That(runtimeJson, Does.Contain("\"enemyGroups\""));
            Assert.That(runtimeJson, Does.Contain("\"enumValues\""));
            Assert.That(runtimeJson, Does.Contain("\"descriptionId\": \"enemy.rat.desc\""));
            Assert.That(runtimeJson, Does.Contain("\"attackSpeed\": 1.2"));
            Assert.That(runtimeJson, Does.Contain("\"combatExp\": 3"));
            Assert.That(runtimeJson, Does.Contain("\"combatAbilityIds\": [\"enemy_ability_bite\"]"));
            Assert.That(runtimeJson, Does.Not.Contain("README"));
            Assert.That(runtimeJson, Does.Not.Contain("notes"));
            Assert.That(runtimeJson, Does.Not.Contain("cells"));
        }

        [Test]
        public void BuildRuntimeJson_ExportsCombatAbilityIdsAsArray()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Enemies").rows[1].cells[17] = "enemy_ability_bite; enemy_ability_scratch";
            FindSheet(download, "Enemies").rows = Append(
                FindSheet(download, "Enemies").rows,
                Row("enemy_no_ability", "enemy.none.name", "enemy.none.desc", "icon_none", "battle_none", "animal", "1", "10", "1", "1", "1", "Melee", "Physical", "0", "0", "0", "0", "", "", "empty ability list"));
            FindSheet(download, "EnemyAbilities").rows = Append(
                FindSheet(download, "EnemyAbilities").rows,
                Row("enemy_ability_scratch", "ability.scratch.name", "OnAttackHit", "", "10", "ApplyStatus: poison_weak", "enemy", "1", "extra"));
            WriteRaw(download);

            var report = new EnemiesConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"combatAbilityIds\": [\"enemy_ability_bite\", \"enemy_ability_scratch\"]"));
            Assert.That(runtimeJson, Does.Contain("\"combatAbilityIds\": []"));
        }

        [Test]
        public void BuildRuntimeJson_ReportsMissingSheetAndColumn()
        {
            var download = CreateValidDownload();
            RemoveSheet(download, "EnemyLevels");
            RemoveHeader(FindSheet(download, "Enemies"), "description_id");
            WriteRaw(download);

            var report = new EnemiesConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("EnemyLevels: Required sheet is missing."));
            Assert.That(message, Does.Contain("Enemies row 1 column 'description_id': Required column is missing."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsDuplicateIdsAndEnumPairs()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Enemies").rows = Append(
                FindSheet(download, "Enemies").rows,
                Row("enemy_rat", "enemy.rat.name.dup", "enemy.rat.desc.dup", "icon_rat", "battle_rat", "animal", "1", "10", "1", "2", "1", "Melee", "Physical", "0", "0", "0", "0", "", "", "duplicate"));
            FindSheet(download, "EnemyAbilities").rows = Append(
                FindSheet(download, "EnemyAbilities").rows,
                Row("enemy_ability_bite", "ability.dup.name", "OnAttackHit", "", "10", "ApplyStatus: poison_weak", "enemy", "1", "duplicate"));
            FindSheet(download, "CombatStatuses").rows = Append(
                FindSheet(download, "CombatStatuses").rows,
                Row("poison_weak", "status.dup.name", "poison", "5", "1", "1", "DamageOverTime", "Poison", "1", "", "", "duplicate"));
            FindSheet(download, "EnemyGroups").rows = Append(
                FindSheet(download, "EnemyGroups").rows,
                Row("enemy_group_rats", "enemy_rat:1", "1", "1", "1", "duplicate"));
            FindSheet(download, "Enums").rows = Append(
                FindSheet(download, "Enums").rows,
                Row("enemy_type", "animal", "duplicate"));
            WriteRaw(download);

            var report = new EnemiesConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("Duplicate enemy id"));
            Assert.That(message, Does.Contain("Duplicate ability id"));
            Assert.That(message, Does.Contain("Duplicate status id"));
            Assert.That(message, Does.Contain("Duplicate enemy_group_id"));
            Assert.That(message, Does.Contain("Duplicate enum value in group 'enemy_type'."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsBadEnemyRefAndReferences()
        {
            var download = CreateValidDownload();
            FindSheet(download, "EnemyGroups").rows[1].cells[1] = "missing_enemy:0";
            FindSheet(download, "Enemies").rows[1].cells[17] = "missing_ability";
            FindSheet(download, "Enemies").rows[1].cells[18] = "missing_loot_group";
            FindSheet(download, "EnemyLoot").rows[1].cells[1] = "missing_enemy";
            FindSheet(download, "EnemyAbilities").rows[1].cells[5] = "ApplyStatus: missing_status";
            WriteRaw(download);

            var report = new EnemiesConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("Referenced enemy_id does not exist in Enemies.enemy_id."));
            Assert.That(message, Does.Contain("Level in enemy_ref must be an integer greater than 0."));
            Assert.That(message, Does.Contain("Referenced ability_id does not exist in EnemyAbilities.ability_id."));
            Assert.That(message, Does.Contain("Referenced loot_group_id does not exist in EnemyLoot.loot_group_id."));
            Assert.That(message, Does.Contain("ApplyStatus references missing CombatStatuses.status_id."));
        }

        [Test]
        public void BuildRuntimeJson_ReportsInvalidRangesChanceEnumAndNumbers()
        {
            var download = CreateValidDownload();
            FindSheet(download, "EnemyLoot").rows[1].cells[3] = "5";
            FindSheet(download, "EnemyLoot").rows[1].cells[4] = "2";
            FindSheet(download, "EnemyLoot").rows[1].cells[5] = "150";
            FindSheet(download, "EnemyGroups").rows[1].cells[2] = "0";
            FindSheet(download, "Enemies").rows[1].cells[5] = "dragon";
            FindSheet(download, "Enemies").rows[1].cells[6] = "NaN";
            FindSheet(download, "EnemyAbilities").rows[1].cells[7] = "cooldown";
            WriteRaw(download);

            var report = new EnemiesConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("min_count must be <= max_count."));
            Assert.That(message, Does.Contain("Percent value must be in range 0..100."));
            Assert.That(message, Does.Contain("Weight must be greater than 0."));
            Assert.That(message, Does.Contain("Value is not listed in Enums group 'enemy_type'."));
            Assert.That(message, Does.Contain("Expected an integer number."));
            Assert.That(message, Does.Contain("Expected a number."));
        }

        [Test]
        public void ParseAndWrite_DoesNotOverwriteExistingRuntimeWhenValidationFails()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Enemies").rows[1].cells[6] = "bad_number";
            WriteRaw(download);

            WriteProjectFile(TestRuntimePath, "{\"previous\":true}\n");

            var report = new EnemiesConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.False);
            Assert.That(ReadProjectFile(TestRuntimePath), Is.EqualTo("{\"previous\":true}\n"));
        }

        [Test]
        public void ParseAndWrite_WritesRuntimeOnlyAfterSuccessfulValidation()
        {
            WriteRaw(CreateValidDownload());

            var report = new EnemiesConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(ReadProjectFile(TestRuntimePath), Does.Contain("\"enemies\""));
        }

        [Test]
        public void CrossConfigValidator_ReportsMissingActivityEnemyGroupReference()
        {
            WriteProjectFile(TestActivityRawPath, JsonUtility.ToJson(CreateActivityDownload("missing_enemy_group"), true));
            WriteRaw(CreateValidDownload());

            var report = ConfigCrossConfigValidator.Validate(new ConfigSourceSettingsCollection
            {
                sources = new[]
                {
                    CreateActivitySource(),
                    CreateSource()
                }
            });

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("Activity Configs / CombatDetails row 2 column 'enemy_group_id' value 'missing_enemy_group'"));
        }

        private static ConfigSourceSettings CreateSource()
        {
            return new ConfigSourceSettings
            {
                config_id = "enemies_configs",
                output_json_path = TestRawPath,
                runtime_json_path = TestRuntimePath
            };
        }

        private static ConfigSourceSettings CreateActivitySource()
        {
            return new ConfigSourceSettings
            {
                config_id = "activity_configs",
                output_json_path = TestActivityRawPath,
                runtime_json_path = "Assets/StreamingAssets/Configs/activity_configs.test.runtime.json"
            };
        }

        private static ConfigSheetDownload CreateValidDownload()
        {
            return new ConfigSheetDownload
            {
                config_id = "enemies_configs",
                display_name = "GuildIdle - Enemies Configs",
                source_type = "GoogleSheet",
                sheet_url = "https://docs.google.com/spreadsheets/d/test",
                downloaded_at_utc = "2026-07-05T00:00:00Z",
                sheets = new[]
                {
                    Sheet("Enemies",
                        Row("enemy_id", "name_id", "description_id", "icon_id", "battle_image_id", "enemy_type", "combat_exp", "hp", "damage_min", "damage_max", "attack_speed", "attack_range", "damage_type", "crit_chance_percent", "physical_resist_percent", "magic_resist_percent", "dodge_chance_percent", "combat_ability_ids", "loot_group_id", "notes"),
                        Row("enemy_rat", "enemy.rat.name", "enemy.rat.desc", "icon_rat", "battle_rat", "animal", "3", "20", "1", "3", "1,2", "Melee", "Physical", "0", "0", "0", "2", "enemy_ability_bite", "loot_enemy_rat", "designer note")),
                    Sheet("EnemyLevels",
                        Row("level", "hp_multiplier", "damage_multiplier", "combat_exp_multiplier", "loot_quantity_multiplier", "attack_speed_multiplier", "notes"),
                        Row("1", "1", "1", "1", "1", "1", "note")),
                    Sheet("EnemyLoot",
                        Row("loot_group_id", "enemy_id", "loot_id", "min_count", "max_count", "chance_percent", "quality_min", "quality_max", "notes"),
                        Row("loot_enemy_rat", "enemy_rat", "gold_id", "1", "3", "100", "0", "0", "currency")),
                    Sheet("EnemyAbilities",
                        Row("ability_id", "name_id", "trigger", "conditions", "chance_percent", "effects", "target", "cooldown_sec", "notes"),
                        Row("enemy_ability_bite", "ability.bite.name", "OnAttackHit", "", "15", "ApplyStatus: poison_weak", "enemy", "6", "note")),
                    Sheet("CombatStatuses",
                        Row("status_id", "name_id", "type", "duration_sec", "tick_interval_sec", "max_stacks", "effect_type", "damage_type", "damage_value", "stat_id", "stat_modifier_value", "notes"),
                        Row("poison_weak", "status.poison.name", "poison", "10", "2", "5", "DamageOverTime", "Poison", "2", "", "", "note")),
                    Sheet("EnemyGroups",
                        Row("enemy_group_id", "enemy_ref", "weight", "min_count", "max_count", "notes"),
                        Row("enemy_group_rats", "enemy_rat:1", "100", "1", "3", "note")),
                    Sheet("Enums",
                        Row("enum_group", "value", "description"),
                        Row("enemy_type", "animal", "Animal enemies"),
                        Row("attack_range", "Melee", "Melee attack"),
                        Row("damage_type", "Physical", "Physical damage"),
                        Row("damage_type", "Poison", "Poison damage")),
                    Sheet("README", Row("README"), Row("This sheet must not be emitted"))
                }
            };
        }

        private static ConfigSheetDownload CreateActivityDownload(string enemyGroupId)
        {
            return new ConfigSheetDownload
            {
                config_id = "activity_configs",
                display_name = "GuildIdle - Activity Configs",
                source_type = "GoogleSheet",
                sheet_url = "https://docs.google.com/spreadsheets/d/test",
                downloaded_at_utc = "2026-07-05T00:00:00Z",
                sheets = new[]
                {
                    Sheet("CombatDetails",
                        Row("activity_id", "enemy_group_id", "combat_mode", "intended_first_result", "completion_reward_rule", "notes"),
                        Row("combat_test", enemyGroupId, "Queue_1v1", "VictoryExpected", "ActivityRewards", "note"))
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

        private static void RemoveSheet(ConfigSheetDownload download, string sheetName)
        {
            var sheets = new List<ConfigDownloadedSheet>(download.sheets);
            sheets.RemoveAll(sheet => string.Equals(sheet.sheet_name, sheetName, StringComparison.OrdinalIgnoreCase));
            download.sheets = sheets.ToArray();
        }

        private static void RemoveHeader(ConfigDownloadedSheet sheet, string header)
        {
            var cells = new List<string>(sheet.rows[0].cells);
            cells.Remove(header);
            sheet.rows[0].cells = cells.ToArray();
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
