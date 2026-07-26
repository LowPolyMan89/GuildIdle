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
            Assert.That(runtimeJson, Does.Contain("\"craftDefinitions\""));
            Assert.That(runtimeJson, Does.Not.Contain("\"itemActions\""));
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
            Assert.That(runtimeJson, Does.Contain("\"effects\": [\"RestoreHealthFlat:25\"]"));
        }

        [Test]
        public void BuildRuntimeJson_ExportsOnlyEnabledConsumablesAndPreservesFractionalSeconds()
        {
            var download = CreateValidDownload();
            var consumables = FindSheet(download, "Расходники");
            consumables.rows = Append(
                consumables.rows,
                Row(
                    "consumable_disabled",
                    "Disabled",
                    "item.consumable_disabled.name",
                    "item.consumable_disabled.description",
                    "icon_consumable_disabled",
                    "consumable",
                    "Common",
                    "unsupported_place",
                    "unsupported_condition",
                    "UnsupportedEffect",
                    "1.25",
                    "disabled",
                    "0.25",
                    "FALSE"));
            WriteRaw(download);

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"cooldownSeconds\": 0.5"));
            Assert.That(runtimeJson, Does.Contain("\"checkIntervalSeconds\": 0.5"));
            Assert.That(runtimeJson, Does.Not.Contain("consumable_disabled"));

            var dto = JsonUtility.FromJson<GuildIdle.Configs.ItemsRuntimeConfigDto>(runtimeJson);
            Assert.That(dto.consumables, Has.Length.EqualTo(1));
            Assert.That(dto.consumables[0].cooldownSeconds, Is.EqualTo(0.5d));
            Assert.That(dto.consumables[0].checkIntervalSeconds, Is.EqualTo(0.5d));
            var provider = new GuildIdle.Combat.CombatConsumableDescriptorRepository(
                new GuildIdle.Configs.ItemsConfigRepository(dto),
                new GuildIdle.Configs.StorageConfigRepository(
                    new GuildIdle.Configs.StorageRuntimeConfigDto
                    {
                        storageRules = new[]
                        {
                            new GuildIdle.Configs.StorageRuleConfigDto
                            {
                                storageRuleId = "storage_consumable",
                                itemKind = "consumable",
                                mode = "stack",
                                maxStack = 20
                            }
                        }
                    }));
            Assert.That(provider.TryGet("consumable_disabled", out _), Is.False);
        }

        [Test]
        public void BuildRuntimeJson_RequiresEnabledColumnForConsumables()
        {
            var download = CreateValidDownload();
            var consumables = FindSheet(download, "Расходники");
            consumables.rows[0].cells[13] = "legacy_enabled";
            WriteRaw(download);

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("column 'enabled'").And.Contain("Required column is missing"));
        }

        [TestCase("kind", "resource")]
        [TestCase("use_place", "inventory")]
        [TestCase("use_condition", "HP_PERCENT<=40")]
        [TestCase("use_condition", "hp_percent<40")]
        [TestCase("use_condition", "hp_percent<=")]
        [TestCase("use_condition", "hp_percent<=101")]
        [TestCase("use_condition", "hp_percent<=NaN")]
        [TestCase("use_condition", "hp_percent<=40;hp_percent<=20")]
        [TestCase("effects", "restorehealthflat:25")]
        [TestCase("effects", "RestoreHealthFlat")]
        [TestCase("effects", "RestoreHealthFlat:25;RestoreHealthFlat:10")]
        [TestCase("effects", "RestoreHealthFlat:NaN")]
        [TestCase("effects", "RestoreHealthFlat:0")]
        [TestCase("cooldown_seconds", "-1")]
        [TestCase("cooldown_seconds", "Infinity")]
        [TestCase("check_interval_seconds", "0")]
        [TestCase("check_interval_seconds", "-1")]
        public void BuildRuntimeJson_RejectsInvalidEnabledCombatConsumable(string column, string value)
        {
            var download = CreateValidDownload();
            SetCell(FindSheet(download, "Расходники"), 1, column, value);
            WriteRaw(download);

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain($"column '{column}'"));
        }

        [Test]
        public void BuildRuntimeJson_UsesOnlyCraftDefinitionsForCraftActions()
        {
            WriteRaw(CreateValidDownload());

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"craftId\": \"process_pine_plank\""));
            Assert.That(runtimeJson, Does.Contain("\"craftId\": \"craft_copper_sword\""));
            Assert.That(runtimeJson, Does.Contain("\"craftId\": \"craft_simple_shield\""));
            Assert.That(runtimeJson, Does.Contain("\"craftId\": \"craft_aska_bow\""));
            Assert.That(runtimeJson, Does.Not.Contain("starter_hero_available"));
            Assert.That(runtimeJson, Does.Not.Contain("\"itemActions\""));
            Assert.That(typeof(GuildIdle.Configs.RecipeConfigDto).GetField("craftId"), Is.Null);
            Assert.That(typeof(GuildIdle.Configs.ItemsRuntimeConfigDto).GetField("itemActions"), Is.Null);

            var dto = JsonUtility.FromJson<GuildIdle.Configs.ItemsRuntimeConfigDto>(runtimeJson);
            var repository = new GuildIdle.Configs.ItemsConfigRepository(dto);
            Assert.That(repository.TryGetCraftDefinition("craft_aska_bow", out var definition), Is.True);
            Assert.That(definition.targetItemId, Is.EqualTo("item_aska_bow"));
        }

        [Test]
        public void BuildRuntimeJson_PreservesDuplicateCraftMaterialsAndIsReproducible()
        {
            var download = CreateValidDownload();
            FindSheet(download, "CraftDefinitions").rows[1].cells[6] =
                "resource_pine_wood:1;resource_pine_plank:2;resource_pine_wood:3";
            WriteRaw(download);

            var firstReport = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out var firstJson);
            var secondReport = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out var secondJson);

            Assert.That(firstReport.Success, Is.True, firstReport.ToDisplayMessage());
            Assert.That(secondReport.Success, Is.True, secondReport.ToDisplayMessage());
            Assert.That(secondJson, Is.EqualTo(firstJson));
            var dto = JsonUtility.FromJson<GuildIdle.Configs.ItemsRuntimeConfigDto>(firstJson);
            var repository = new GuildIdle.Configs.ItemsConfigRepository(dto);
            Assert.That(repository.TryGetCraftDefinition("process_pine_plank", out var definition), Is.True);
            Assert.That(definition.materials, Has.Length.EqualTo(3));
            Assert.That(definition.materials[0].id, Is.EqualTo("resource_pine_wood"));
            Assert.That(definition.materials[0].count, Is.EqualTo(1));
            Assert.That(definition.materials[2].id, Is.EqualTo("resource_pine_wood"));
            Assert.That(definition.materials[2].count, Is.EqualTo(3));
            Assert.That(definition.requiredRecipeItemId, Is.Empty);
            Assert.That(definition.requiredRecipeItemCount, Is.Zero);
            Assert.That(definition.consumeRecipeItem, Is.False);
        }

        [TestCase(";resource_pine_wood:1", 1, "")]
        [TestCase("resource_pine_wood:1;", 2, "")]
        [TestCase("resource_pine_wood:1;;resource_pine_plank:1", 2, "")]
        [TestCase("resource_pine_wood", 1, "resource_pine_wood")]
        [TestCase(":1", 1, ":1")]
        [TestCase("resource_pine_wood:", 1, "resource_pine_wood:")]
        [TestCase("resource_pine_wood:1:extra", 1, "resource_pine_wood:1:extra")]
        [TestCase("resource_pine_wood:no", 1, "resource_pine_wood:no")]
        [TestCase("resource_pine_wood:0", 1, "resource_pine_wood:0")]
        [TestCase("resource_pine_wood:-1", 1, "resource_pine_wood:-1")]
        [TestCase("resource_pine_wood:2147483648", 1, "resource_pine_wood:2147483648")]
        public void BuildRuntimeJson_RejectsMalformedCraftMaterialWithTokenContext(string materials, int tokenIndex, string rawToken)
        {
            var download = CreateValidDownload();
            FindSheet(download, "CraftDefinitions").rows[1].cells[6] = materials;
            WriteRaw(download);

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out _);
            var message = report.ToDisplayMessage();

            Assert.That(report.Success, Is.False);
            Assert.That(message, Does.Contain("CraftDefinitions row 2 column 'materials'"));
            Assert.That(message, Does.Contain("CraftDefinition 'process_pine_plank'"));
            Assert.That(message, Does.Contain($"token {tokenIndex}"));
            Assert.That(message, Does.Contain($"raw '{rawToken}'"));
        }

        [Test]
        public void BuildRuntimeJson_RejectsAggregatedCraftMaterialOverflow()
        {
            var download = CreateValidDownload();
            FindSheet(download, "CraftDefinitions").rows[1].cells[6] =
                "resource_pine_wood:2147483647;resource_pine_wood:1";
            WriteRaw(download);

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("token 2").And.Contain("exceeds Int32.MaxValue"));
        }

        [Test]
        public void BuildRuntimeJson_RejectsDuplicateCraftId()
        {
            var download = CreateValidDownload();
            var definitions = FindSheet(download, "CraftDefinitions");
            definitions.rows = Append(definitions.rows, definitions.rows[1]);
            WriteRaw(download);

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("Duplicate craft_id").And.Contain("first declared at row 2"));
        }

        [Test]
        public void BuildRuntimeJson_AllowsHeaderOnlyCraftDefinitionsAndRecipes()
        {
            var download = CreateValidDownload();
            var recipes = FindSheet(download, "\u0420\u0435\u0446\u0435\u043F\u0442\u044B");
            recipes.rows = new[] { recipes.rows[0] };
            var definitions = FindSheet(download, "CraftDefinitions");
            definitions.rows = new[] { definitions.rows[0] };
            WriteRaw(download);

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"recipes\": []"));
            Assert.That(runtimeJson, Does.Contain("\"craftDefinitions\": []"));
        }

        [Test]
        public void BuildRuntimeJson_RejectsRecipeCountWhenRecipeIdIsEmpty()
        {
            var download = CreateValidDownload();
            FindSheet(download, "CraftDefinitions").rows[1].cells[8] = "1";
            WriteRaw(download);

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("required_recipe_item_count must be 0"));
        }

        [Test]
        public void BuildRuntimeJson_AcceptsOnlyCanonicalEquipmentSlots()
        {
            var legacy = CreateValidDownload();
            FindSheet(legacy, "Снаряжение - оружие").rows[1].cells[7] = "weapon_slot";
            WriteRaw(legacy);
            var legacyReport = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            var unknown = CreateValidDownload();
            FindSheet(unknown, "Снаряжение - броня").rows[1].cells[7] = "cape";
            WriteRaw(unknown);
            var unknownReport = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(legacyReport.Success, Is.False);
            Assert.That(unknownReport.Success, Is.False);
            Assert.That(unknownReport.ToDisplayMessage(), Does.Contain("equipment_slot is not a canonical equipment slot."));
        }

        [TestCase("helmet")]
        [TestCase("armor")]
        [TestCase("boots")]
        [TestCase("weapon")]
        [TestCase("offhand")]
        [TestCase("accessory")]
        public void BuildRuntimeJson_AcceptsCanonicalEquipmentSlot(string slot)
        {
            var download = CreateValidDownload();
            FindSheet(download, "Снаряжение - броня").rows[1].cells[7] = slot;
            WriteRaw(download);

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
        }

        [Test]
        public void BuildRuntimeJson_RejectsEnabledCraftReferencingDisabledRecipe()
        {
            var download = CreateValidDownload();
            AddDisabledRecipeAndCraft(download, targetItemId: "resource_pine_wood", requiredRecipeItemId: "recipe_old", craftEnabled: "TRUE");
            WriteRaw(download);

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("required_recipe_item_id").And.Contain("not exported by enabled Recipes.id"));
        }

        [Test]
        public void BuildRuntimeJson_RejectsEnabledCraftTargetThatIsNotRuntimeEnabled()
        {
            var download = CreateValidDownload();
            AddDisabledRecipeAndCraft(download, targetItemId: "recipe_old", requiredRecipeItemId: "", craftEnabled: "TRUE");
            WriteRaw(download);

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out _);

            Assert.That(report.Success, Is.False);
            Assert.That(report.ToDisplayMessage(), Does.Contain("target_item_id").And.Contain("runtime Items Configs item registry"));
        }

        [Test]
        public void BuildRuntimeJson_AllowsCraftReferencesFromSheetWithoutCanonicalEnabledColumn()
        {
            WriteRaw(CreateValidDownload());

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Contain("\"targetItemId\": \"resource_pine_plank\""));
            Assert.That(runtimeJson, Does.Contain("\"id\": \"resource_pine_wood\""));
        }

        [Test]
        public void BuildRuntimeJson_IgnoresRuntimeReferencesOfDisabledCraft()
        {
            var download = CreateValidDownload();
            AddDisabledRecipeAndCraft(
                download,
                targetItemId: "recipe_old",
                requiredRecipeItemId: "recipe_old",
                craftEnabled: "FALSE",
                materials: "recipe_old:1");
            WriteRaw(download);

            var report = new ItemsConfigsParser().BuildRuntimeJson(CreateSource(), out var runtimeJson);

            Assert.That(report.Success, Is.True, report.ToDisplayMessage());
            Assert.That(runtimeJson, Does.Not.Contain("recipe_old"));
            Assert.That(runtimeJson, Does.Not.Contain("craft_test"));
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
            Assert.That(ReadProjectFile(TestRuntimePath), Does.Contain("\"craftDefinitions\""));
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
                        Row("item_wooden_club", "Деревянная дубинка", "item.item_wooden_club.name", "item.item_wooden_club.description", "icon_item_wooden_club", "equipment", "weapon", "weapon", "Common", "1", "", "", "", "Strength", "", "", "", "", "FALSE", "FALSE", "1", "", "", "note", "", "2", "4", "1,20", "Melee", "Physical"),
                        Row("item_copper_sword", "Медный меч", "item.item_copper_sword.name", "item.item_copper_sword.description", "icon_item_copper_sword", "equipment", "weapon", "weapon", "Common", "1", "building_forge", "30", "skill_production", "Strength", "building_forge:1", "", "", "", "FALSE", "FALSE", "1", "resource_copper_ingot:2; resource_pine_plank:1", "craft_copper_sword", "note", "10,00", "5", "10", "1,00", "Melee", "Physical"),
                        Row("item_aska_bow", "Лук Аськи", "item.item_aska_bow.name", "item.item_aska_bow.description", "icon_item_aska_bow", "equipment", "weapon", "weapon", "Uncommon", "1", "building_carpentry", "30", "skill_crafting", "Agility", "building_carpentry:1", "", "recipe_aska_bow", "1", "TRUE", "TRUE", "1", "resource_pine_plank:2", "craft_aska_bow", "note", "10,00", "4", "8", "1,10", "Ranged", "Physical")),
                    Sheet("Снаряжение - броня",
                        Row("id", "Название", "name_id", "description_id", "icon_id", "kind", "subtype", "equipment_slot", "rarity_id", "tier", "craft_station_id", "craft_duration_sec", "craft_skill_id", "craft_main_stat_id", "required_buildings", "required_skills", "visibility_item_id", "visibility_item_count", "consume_visibility_item", "hidden_until_visibility_item", "output_count", "materials", "source_activity_id", "notes", "skill_exp", "physical_resist_bonus", "magic_resist_bonus", "max_hp_bonus"),
                        Row("item_simple_shield", "Простой щит", "item.item_simple_shield.name", "item.item_simple_shield.description", "icon_item_simple_shield", "equipment", "shield", "offhand", "Common", "1", "building_forge", "30", "skill_production", "Endurance", "building_forge:1", "", "", "", "FALSE", "FALSE", "1", "resource_pine_plank:2; resource_copper_ingot:1", "craft_simple_shield", "note", "10", "5", "0", "10")),
                    Sheet("Рецепты",
                        Row("id", "Название", "name_id", "description_id", "icon_id", "kind", "rarity_id", "tier", "enabled", "notes"),
                        Row("recipe_aska_bow", "Рецепт: Лук Аськи", "item.recipe_aska_bow.name", "item.recipe_aska_bow.description", "icon_recipe_aska_bow", "recipe", "Uncommon", "1", "TRUE", "note")),
                    Sheet("CraftDefinitions",
                        Row("craft_id", "target_item_id", "craft_station_id", "craft_duration_sec", "craft_skill_id", "required_buildings", "materials", "required_recipe_item_id", "required_recipe_item_count", "consume_recipe_item", "output_count", "enabled", "notes", "fatigue_cost", "skill_exp"),
                        Row("process_pine_plank", "resource_pine_plank", "building_carpentry", "30", "skill_processing", "building_carpentry:1", "resource_pine_wood:2", "", "0", "FALSE", "1", "TRUE", "note", "1", "10"),
                        Row("craft_copper_sword", "item_copper_sword", "building_forge", "30", "skill_production", "building_forge:1", "resource_copper_ingot:2;resource_pine_plank:1", "", "0", "FALSE", "1", "TRUE", "note", "1", "10"),
                        Row("craft_simple_shield", "item_simple_shield", "building_forge", "30", "skill_production", "building_forge:1", "resource_pine_plank:2;resource_copper_ingot:1", "", "0", "FALSE", "1", "TRUE", "note", "1", "10"),
                        Row("craft_aska_bow", "item_aska_bow", "building_carpentry", "30", "skill_crafting", "building_carpentry:1", "resource_pine_plank:2", "recipe_aska_bow", "1", "TRUE", "1", "TRUE", "note", "1", "10")),
                    Sheet("Расходники",
                        Row("id", "Название", "name_id", "description_id", "icon_id", "kind", "rarity_id", "use_place", "use_condition", "effects", "cooldown_seconds", "notes", "check_interval_seconds", "enabled"),
                        Row("consumable_hunting_potion", "Зелье охоты", "item.consumable_hunting_potion.name", "item.consumable_hunting_potion.description", "icon_consumable_hunting_potion", "consumable", "Common", "combat", "hp_percent<=40", "RestoreHealthFlat:25", "0.5", "note", "0.5", "TRUE")),
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

        private static ConfigSheetRow[] Append(ConfigSheetRow[] rows, params ConfigSheetRow[] appendedRows)
        {
            var values = new List<ConfigSheetRow>(rows);
            values.AddRange(appendedRows);
            return values.ToArray();
        }

        private static void AddDisabledRecipeAndCraft(
            ConfigSheetDownload download,
            string targetItemId,
            string requiredRecipeItemId,
            string craftEnabled,
            string materials = "resource_pine_wood:1")
        {
            var recipes = FindSheet(download, "Рецепты");
            recipes.rows = Append(
                recipes.rows,
                Row("recipe_old", "Old recipe", "item.recipe_old.name", "item.recipe_old.description", "icon_recipe_old", "recipe", "Common", "1", "FALSE", "disabled"));

            var craftDefinitions = FindSheet(download, "CraftDefinitions");
            var hasRequiredRecipe = !string.IsNullOrWhiteSpace(requiredRecipeItemId);
            craftDefinitions.rows = Append(
                craftDefinitions.rows,
                Row(
                    "craft_test",
                    targetItemId,
                    "building_carpentry",
                    "10",
                    "skill_crafting",
                    "building_carpentry:1",
                    materials,
                    requiredRecipeItemId,
                    hasRequiredRecipe ? "1" : "0",
                    hasRequiredRecipe ? "TRUE" : "FALSE",
                    "1",
                    craftEnabled,
                    "test",
                    "1",
                    "1"));
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

        private static void SetCell(
            ConfigDownloadedSheet sheet,
            int dataRowIndex,
            string column,
            string value)
        {
            var columnIndex = Array.IndexOf(sheet.rows[0].cells, column);
            if (columnIndex < 0)
                throw new InvalidOperationException($"Missing test column {column}.");

            sheet.rows[dataRowIndex].cells[columnIndex] = value;
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
