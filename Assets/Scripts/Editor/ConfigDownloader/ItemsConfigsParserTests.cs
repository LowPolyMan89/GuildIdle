using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class ItemsConfigsParserTests
    {
        private const string TestRawPath = "Temp/ConfigParserTests/items_configs.raw.json";
        private const string TestRuntimePath = "Assets/StreamingAssets/Configs/items_configs.test.runtime.json";

        [TearDown]
        public void TearDown()
        {
            DeleteProjectFile(TestRawPath);
            DeleteProjectFile(TestRuntimePath);
            DeleteProjectFile(TestRuntimePath + ".tmp");
            DeleteProjectFile(TestRuntimePath + ".meta");
        }

        [Test]
        public void BuildRuntimeJson_UsesHeadersAndExcludesDesignerReadmeAndRawCells()
        {
            WriteRaw(CreateValidDownload());

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"resources\""));
            Assert.That(runtimeJson, Does.Contain("\"equipmentWeapons\""));
            Assert.That(runtimeJson, Does.Contain("\"equipmentArmor\""));
            Assert.That(runtimeJson, Does.Contain("\"recipes\""));
            Assert.That(runtimeJson, Does.Contain("\"consumables\""));
            Assert.That(runtimeJson, Does.Contain("\"currencies\""));
            Assert.That(runtimeJson, Does.Contain("\"itemActions\""));
            Assert.That(runtimeJson, Does.Contain("\"id\": \"resource_pine_plank\""));
            Assert.That(runtimeJson, Does.Contain("\"nameId\": \"item.resource_pine_plank.name\""));
            Assert.That(runtimeJson, Does.Contain("\"tier\": 1"));
            Assert.That(runtimeJson, Does.Contain("\"consumeVisibilityItem\": true"));
            Assert.That(runtimeJson, Does.Contain("\"weaponAttackInterval\": 1.2"));
            Assert.That(runtimeJson, Does.Not.Contain("README"));
            Assert.That(runtimeJson, Does.Not.Contain("Название"));
            Assert.That(runtimeJson, Does.Not.Contain("notes"));
            Assert.That(runtimeJson, Does.Not.Contain("cells"));
        }

        [Test]
        public void BuildRuntimeJson_ExportsPackedRefsAndConsumableEffectsAsArrays()
        {
            WriteRaw(CreateValidDownload());

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"materials\": ["));
            Assert.That(runtimeJson, Does.Contain("\"id\": \"resource_copper_ingot\""));
            Assert.That(runtimeJson, Does.Contain("\"count\": 2"));
            Assert.That(runtimeJson, Does.Contain("\"requiredBuildings\": ["));
            Assert.That(runtimeJson, Does.Contain("\"buildingId\": \"building_forge\""));
            Assert.That(runtimeJson, Does.Contain("\"requiredSkills\": []"));
            Assert.That(runtimeJson, Does.Contain("\"effects\": [\"ModifyRisk: hunting_combat_risk -10%\", \"ModifyReward: resource_thin_hide +1\"]"));
        }

        [Test]
        public void BuildRuntimeJson_GeneratesItemActionsOnlyForProductionRows()
        {
            WriteRaw(CreateValidDownload());

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"id\": \"process_pine_plank\""));
            Assert.That(runtimeJson, Does.Contain("\"type\": \"Process\""));
            Assert.That(runtimeJson, Does.Contain("\"id\": \"craft_copper_sword\""));
            Assert.That(runtimeJson, Does.Contain("\"id\": \"craft_simple_shield\""));
            Assert.That(runtimeJson, Does.Contain("\"id\": \"craft_aska_bow\""));
            Assert.That(runtimeJson, Does.Not.Contain("\"id\": \"starter_hero_available\""));
        }

        [Test]
        public void BuildRuntimeJson_AllowsGoldIdAsCurrencyButRejectsItAsItemOrMaterial()
        {
            WriteRaw(CreateValidDownload());

            var validReport = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(validReport.Success, Is.True, validReport.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"currencyId\": \"gold_id\""));

            var itemDownload = CreateValidDownload();
            FindSheet(itemDownload, "Ресурсы").rows[1].cells[0] = "gold_id";
            WriteRaw(itemDownload);

            var itemReport = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            Assert.That(itemReport.Success, Is.False);
            Assert.That(itemReport.ToDisplayMessage(), Does.Contain("gold_id is a currency_id and must not be declared as an item id."));

            var materialDownload = CreateValidDownload();
            FindSheet(materialDownload, "Снаряжение - оружие").rows[2].cells[21] = "gold_id:1";
            WriteRaw(materialDownload);

            var materialReport = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            Assert.That(materialReport.Success, Is.False);
            Assert.That(materialReport.ToDisplayMessage(), Does.Contain("gold_id is a currency_id and must not be used as an item/material reference."));
        }

        [Test]
        public void BuildRuntimeJson_RejectsForbiddenLegacyItemGold()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Снаряжение - оружие").rows[2].cells[21] = "item_gold:1";
            WriteRaw(download);

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("item_gold is a forbidden legacy item id in Items Configs."));
        }

        [Test]
        public void ParseAndWrite_DoesNotOverwriteExistingRuntimeWhenValidationFails()
        {
            var download = CreateValidDownload();
            FindSheet(download, "Снаряжение - оружие").rows[2].cells[27] = "0";
            WriteRaw(download);
            WriteProjectFile(TestRuntimePath, "{\"previous\":true}\n");

            var report = new ItemsConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.False);
            Assert.That(ReadProjectFile(TestRuntimePath), Is.EqualTo("{\"previous\":true}\n"));
        }

        [Test]
        public void ParseAndWrite_WritesRuntimeOnlyAfterSuccessfulValidation()
        {
            WriteRaw(CreateValidDownload());

            var report = new ItemsConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(ReadProjectFile(TestRuntimePath), Does.Contain("\"itemActions\""));
        }

        [Test]
        public void ParseAndWrite_WritesRuntimeJsonWithoutUtf8Bom()
        {
            WriteRaw(CreateValidDownload());

            var report = new ItemsConfigsParser().ParseAndWrite(CreateSource());

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            var bytes = File.ReadAllBytes(FullProjectPath(TestRuntimePath));
            Assert.That(bytes.Length, Is.GreaterThan(3));
            Assert.That(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, Is.False);
        }

        private static ConfigSourceSettings CreateSource()
        {
            return new ConfigSourceSettings
            {
                config_id = "items_configs",
                output_json_path = TestRawPath,
                runtime_json_path = TestRuntimePath
            };
        }

        private static ConfigSheetDownload CreateValidDownload()
        {
            return new ConfigSheetDownload
            {
                config_id = "items_configs",
                display_name = "GuildIdle - Items Configs",
                source_type = "GoogleSheet",
                sheet_url = "https://docs.google.com/spreadsheets/d/test",
                downloaded_at_utc = "2026-07-05T00:00:00Z",
                sheets = new[]
                {
                    Sheet("Ресурсы",
                        Row("id", "Название", "name_id", "description_id", "icon_id", "kind", "subtype", "rarity_id", "tier", "craft_station_id", "craft_duration_sec", "craft_skill_id", "required_buildings", "required_skills", "visibility_item_id", "visibility_item_count", "consume_visibility_item", "hidden_until_visibility_item", "output_count", "materials", "source_activity_id", "notes", "skill_exp"),
                        Row("resource_pine_wood", "Сосновая древесина", "item.resource_pine_wood.name", "item.resource_pine_wood.description", "icon_resource_pine_wood", "resource", "wood", "Common", "1", "", "", "", "", "", "", "", "", "", "", "", "work_pine_wood", "note", ""),
                        Row("resource_pine_plank", "Сосновая доска", "item.resource_pine_plank.name", "item.resource_pine_plank.description", "icon_resource_pine_plank", "resource", "processed", "Common", "1", "building_carpentry", "30", "skill_processing", "building_carpentry:1", "", "", "", "FALSE", "FALSE", "1", "resource_pine_wood:2", "process_pine_plank", "note", "10")),
                    Sheet("Снаряжение - оружие",
                        Row("id", "Название", "name_id", "description_id", "icon_id", "kind", "subtype", "equipment_slot", "rarity_id", "tier", "craft_station_id", "craft_duration_sec", "craft_skill_id", "craft_main_stat_id", "required_buildings", "required_skills", "visibility_item_id", "visibility_item_count", "consume_visibility_item", "hidden_until_visibility_item", "output_count", "materials", "source_activity_id", "notes", "skill_exp", "weapon_damage_min", "weapon_damage_max", "weapon_attack_interval", "attack_range", "damage_type"),
                        Row("item_wooden_club", "Деревянная дубинка", "item.item_wooden_club.name", "item.item_wooden_club.description", "icon_item_wooden_club", "equipment", "weapon", "weapon", "Common", "1", "", "", "", "Strength", "", "", "", "", "FALSE", "FALSE", "1", "", "starter_hero_available", "note", "", "2", "4", "1,20", "Melee", "Physical"),
                        Row("item_copper_sword", "Медный меч", "item.item_copper_sword.name", "item.item_copper_sword.description", "icon_item_copper_sword", "equipment", "weapon", "weapon", "Common", "1", "building_forge", "30", "skill_production", "Strength", "building_forge:1", "", "", "", "FALSE", "FALSE", "1", "resource_copper_ingot:2; resource_pine_plank:1", "craft_copper_sword", "note", "10,00", "5", "10", "1,00", "Melee", "Physical"),
                        Row("item_aska_bow", "Лук Аськи", "item.item_aska_bow.name", "item.item_aska_bow.description", "icon_item_aska_bow", "equipment", "weapon", "weapon", "Uncommon", "1", "building_carpentry", "30", "skill_crafting", "Agility", "building_carpentry:1", "", "recipe_aska_bow", "1", "TRUE", "TRUE", "1", "resource_pine_plank:2", "craft_aska_bow", "note", "10,00", "4", "8", "1,10", "Ranged", "Physical")),
                    Sheet("Снаряжение - броня",
                        Row("id", "Название", "name_id", "description_id", "icon_id", "kind", "subtype", "equipment_slot", "rarity_id", "tier", "craft_station_id", "craft_duration_sec", "craft_skill_id", "craft_main_stat_id", "required_buildings", "required_skills", "visibility_item_id", "visibility_item_count", "consume_visibility_item", "hidden_until_visibility_item", "output_count", "materials", "source_activity_id", "notes", "skill_exp", "physical_resist_bonus", "magic_resist_bonus", "max_hp_bonus"),
                        Row("item_simple_shield", "Простой щит", "item.item_simple_shield.name", "item.item_simple_shield.description", "icon_item_simple_shield", "equipment", "shield", "offhand", "Common", "1", "building_forge", "30", "skill_production", "Endurance", "building_forge:1", "", "", "", "FALSE", "FALSE", "1", "resource_pine_plank:2; resource_copper_ingot:1", "craft_simple_shield", "note", "10", "5", "0", "10")),
                    Sheet("Рецепты",
                        Row("id", "Название", "name_id", "description_id", "icon_id", "kind", "target_item_id", "rarity_id", "craft_id", "craft_station_id", "craft_duration_sec", "craft_skill_id", "required_buildings", "materials", "consume_on_craft", "hidden_until_recipe", "notes"),
                        Row("recipe_aska_bow", "Рецепт: Лук Аськи", "item.recipe_aska_bow.name", "item.recipe_aska_bow.description", "icon_recipe_aska_bow", "recipe", "item_aska_bow", "Uncommon", "craft_aska_bow", "building_carpentry", "30", "skill_crafting", "building_carpentry:1", "resource_pine_plank:2", "TRUE", "TRUE", "note")),
                    Sheet("Расходники",
                        Row("id", "Название", "name_id", "description_id", "icon_id", "kind", "rarity_id", "use_place", "use_condition", "effects", "cooldown_seconds", "notes"),
                        Row("consumable_hunting_potion", "Зелье охоты", "item.consumable_hunting_potion.name", "item.consumable_hunting_potion.description", "icon_consumable_hunting_potion", "consumable", "Common", "work", "activity_id=hunt_rabbits", "ModifyRisk: hunting_combat_risk -10%; ModifyReward: resource_thin_hide +1", "0", "note")),
                    Sheet("Валюты",
                        Row("currency_id", "icon_id", "name_id", "description_id", "notes"),
                        Row("gold_id", "gold_icon", "gold_name_id", "gold_description_id", "note")),
                    Sheet("README", Row("Раздел", "Описание"), Row("Источник", "This sheet must not be emitted"))
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
