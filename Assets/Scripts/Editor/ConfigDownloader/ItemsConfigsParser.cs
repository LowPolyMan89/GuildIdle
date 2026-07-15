using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class ItemsConfigsParser : IConfigPipelineParser
    {
        private const string ConfigId = "items_configs";
        private const string ResourcesSheet = "Ресурсы";
        private const string EquipmentWeaponsSheet = "Снаряжение - оружие";
        private const string EquipmentArmorSheet = "Снаряжение - броня";
        private const string RecipesSheet = "Рецепты";
        private const string CraftDefinitionsSheet = "CraftDefinitions";
        private const string ConsumablesSheet = "Расходники";
        private const string CurrenciesSheet = "Валюты";
        private const string ForbiddenLegacyItemId = "item_gold";
        private const string GoldCurrencyId = "gold_id";

        private static readonly string[] RequiredSheets =
        {
            ResourcesSheet,
            EquipmentWeaponsSheet,
            EquipmentArmorSheet,
            RecipesSheet,
            CraftDefinitionsSheet,
            ConsumablesSheet,
            CurrenciesSheet
        };

        private static readonly Dictionary<string, string[]> RequiredColumns = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourcesSheet] = new[]
            {
                "id", "Название", "name_id", "description_id", "icon_id", "kind", "subtype", "rarity_id",
                "tier", "craft_station_id", "craft_duration_sec", "craft_skill_id", "required_buildings",
                "required_skills", "visibility_item_id", "visibility_item_count", "consume_visibility_item",
                "hidden_until_visibility_item", "output_count", "materials", "source_activity_id", "notes",
                "skill_exp"
            },
            [EquipmentWeaponsSheet] = new[]
            {
                "id", "Название", "name_id", "description_id", "icon_id", "kind", "subtype", "equipment_slot",
                "rarity_id", "tier", "craft_station_id", "craft_duration_sec", "craft_skill_id",
                "craft_main_stat_id", "required_buildings", "required_skills", "visibility_item_id",
                "visibility_item_count", "consume_visibility_item", "hidden_until_visibility_item",
                "output_count", "materials", "source_activity_id", "notes", "skill_exp",
                "weapon_damage_min", "weapon_damage_max", "weapon_attack_interval", "attack_range", "damage_type"
            },
            [EquipmentArmorSheet] = new[]
            {
                "id", "Название", "name_id", "description_id", "icon_id", "kind", "subtype", "equipment_slot",
                "rarity_id", "tier", "craft_station_id", "craft_duration_sec", "craft_skill_id",
                "craft_main_stat_id", "required_buildings", "required_skills", "visibility_item_id",
                "visibility_item_count", "consume_visibility_item", "hidden_until_visibility_item",
                "output_count", "materials", "source_activity_id", "notes", "skill_exp",
                "physical_resist_bonus", "magic_resist_bonus", "max_hp_bonus"
            },
            [RecipesSheet] = new[]
            {
                "id", "Название", "name_id", "description_id", "icon_id", "kind", "rarity_id",
                "tier", "enabled", "notes"
            },
            [CraftDefinitionsSheet] = new[]
            {
                "craft_id", "target_item_id", "craft_station_id", "craft_duration_sec", "craft_skill_id",
                "required_buildings", "materials", "required_recipe_item_id", "required_recipe_item_count",
                "consume_recipe_item", "output_count", "enabled", "notes", "fatigue_cost", "skill_exp"
            },
            [ConsumablesSheet] = new[]
            {
                "id", "Название", "name_id", "description_id", "icon_id", "kind", "rarity_id",
                "use_place", "use_condition", "effects", "cooldown_seconds", "check_interval_seconds", "notes"
            },
            [CurrenciesSheet] = new[] { "currency_id", "icon_id", "name_id", "description_id", "notes" }
        };

        private static readonly HashSet<string> EquipmentSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "helmet", "armor", "boots", "weapon", "offhand", "accessory"
        };

        public bool Supports(ConfigSourceSettings source)
        {
            return source != null && string.Equals(source.config_id, ConfigId, StringComparison.OrdinalIgnoreCase);
        }

        public ConfigPipelineReport ParseAndWrite(ConfigSourceSettings source)
        {
            var report = BuildRuntimeJson(source, out var runtimeJson);
            if (!report.Success)
                return report;

            if (!ConfigPipelineUtilities.TryValidateRuntimeOutputPath(source.runtime_json_path, out var fullPath, out var pathError))
            {
                report.ErrorMessage = pathError;
                return report;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                var tempPath = fullPath + ".tmp";
                File.WriteAllText(tempPath, runtimeJson, ConfigPipelineUtilities.Utf8NoBom);

                if (File.Exists(fullPath))
                    File.Replace(tempPath, fullPath, null);
                else
                    File.Move(tempPath, fullPath);

                AssetDatabase.ImportAsset(ConfigPaths.NormalizeProjectPath(source.runtime_json_path));
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                report.ErrorMessage = $"Could not write runtime JSON '{source.runtime_json_path}': {exception.Message}";
            }

            return report;
        }

        public ConfigPipelineReport Validate(ConfigSourceSettings source)
        {
            return BuildRuntimeJson(source, out _);
        }

        public ConfigPipelineReport BuildRuntimeJson(ConfigSourceSettings source, out string runtimeJson)
        {
            runtimeJson = null;
            var report = new ConfigPipelineReport();

            if (!ConfigPipelineUtilities.TryLoadDownload(source, report, out var download))
                return report;

            var context = new ItemsConfigContext(download, report);
            context.ValidateSheetsAndColumns();
            context.ValidateForbiddenLegacyIds();
            context.CollectIds();
            context.ValidateRows();

            if (!report.Success)
                return report;

            runtimeJson = ConfigRuntimeJsonWriter.Write(context.BuildRuntimeArrays());
            return report;
        }

        private sealed class ItemsConfigContext
        {
            private readonly ConfigSheetDownload _download;
            private readonly ConfigPipelineReport _report;
            private readonly Dictionary<string, ConfigSheetTable> _tables = new Dictionary<string, ConfigSheetTable>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _itemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _enabledItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _enabledRecipeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public ItemsConfigContext(ConfigSheetDownload download, ConfigPipelineReport report)
            {
                _download = download;
                _report = report;

                foreach (var sheet in download.sheets ?? Array.Empty<ConfigDownloadedSheet>())
                {
                    if (sheet == null || string.IsNullOrWhiteSpace(sheet.sheet_name))
                        continue;

                    _tables[sheet.sheet_name] = new ConfigSheetTable(sheet);
                }
            }

            public void ValidateSheetsAndColumns()
            {
                foreach (var sheetName in RequiredSheets)
                {
                    if (!_tables.TryGetValue(sheetName, out var table))
                    {
                        AddIssue(sheetName, 0, string.Empty, string.Empty, "Required sheet is missing.");
                        continue;
                    }

                    if (table.Rows == 0)
                    {
                        AddIssue(sheetName, 1, string.Empty, string.Empty, "Required sheet has no header row.");
                        continue;
                    }

                    foreach (var column in RequiredColumns[sheetName])
                    {
                        if (!table.HasColumn(column))
                            AddIssue(sheetName, 1, column, string.Empty, "Required column is missing.");
                    }
                }
            }

            public void ValidateForbiddenLegacyIds()
            {
                foreach (var sheet in _download.sheets ?? Array.Empty<ConfigDownloadedSheet>())
                {
                    if (sheet?.rows == null)
                        continue;

                    for (var rowIndex = 0; rowIndex < sheet.rows.Length; rowIndex++)
                    {
                        var cells = sheet.rows[rowIndex]?.cells;
                        if (cells == null)
                            continue;

                        for (var columnIndex = 0; columnIndex < cells.Length; columnIndex++)
                        {
                            var value = (cells[columnIndex] ?? string.Empty).Trim();
                            if (string.Equals(value, ForbiddenLegacyItemId, StringComparison.OrdinalIgnoreCase))
                                AddIssue(sheet.sheet_name, rowIndex + 1, string.Empty, value, "item_gold is a forbidden legacy item id in Items Configs.");
                        }
                    }
                }
            }

            public void CollectIds()
            {
                var globalSeen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                CollectItemIds(ResourcesSheet, globalSeen);
                CollectItemIds(EquipmentWeaponsSheet, globalSeen);
                CollectItemIds(EquipmentArmorSheet, globalSeen);
                CollectItemIds(RecipesSheet, globalSeen);
                CollectItemIds(ConsumablesSheet, globalSeen);
                CollectCraftIds();
            }

            public void ValidateRows()
            {
                ValidateItemSheet(ResourcesSheet, "resource");
                ValidateItemSheet(EquipmentWeaponsSheet, "equipment");
                ValidateItemSheet(EquipmentArmorSheet, "equipment");
                ValidateItemSheet(RecipesSheet, "recipe");
                ValidateItemSheet(ConsumablesSheet, "consumable");
                ValidateCraftDefinitions();
                ValidateCurrencies();
            }

            public Dictionary<string, List<Dictionary<string, object>>> BuildRuntimeArrays()
            {
                var arrays = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.Ordinal)
                {
                    ["resources"] = BuildRows(ResourcesSheet, BuildResourceRow),
                    ["equipmentWeapons"] = BuildRows(EquipmentWeaponsSheet, BuildEquipmentWeaponRow),
                    ["equipmentArmor"] = BuildRows(EquipmentArmorSheet, BuildEquipmentArmorRow),
                    ["recipes"] = BuildEnabledRows(RecipesSheet, BuildRecipeRow),
                    ["craftDefinitions"] = BuildEnabledRows(CraftDefinitionsSheet, BuildCraftDefinitionRow),
                    ["consumables"] = BuildRows(ConsumablesSheet, BuildConsumableRow),
                    ["currencies"] = BuildRows(CurrenciesSheet, BuildCurrencyRow)
                };

                return arrays;
            }

            private void CollectItemIds(string sheetName, Dictionary<string, string> globalSeen)
            {
                if (!_tables.TryGetValue(sheetName, out var table) || !table.HasColumn("id"))
                    return;

                var sheetSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    var id = row.Get("id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        AddIssue(sheetName, row.RowNumber, "id", id, "id is required.");
                        continue;
                    }

                    if (string.Equals(id, GoldCurrencyId, StringComparison.OrdinalIgnoreCase))
                        AddIssue(sheetName, row.RowNumber, "id", id, "gold_id is a currency_id and must not be declared as an item id.");

                    if (sheetSeen.TryGetValue(id, out var firstSheetRow))
                        AddIssue(sheetName, row.RowNumber, "id", id, $"Duplicate id in sheet; first declared at row {firstSheetRow}.");
                    else
                        sheetSeen[id] = row.RowNumber;

                    if (globalSeen.TryGetValue(id, out var firstSheet))
                        AddIssue(sheetName, row.RowNumber, "id", id, $"Duplicate item id across item lists; first declared in {firstSheet}.");
                    else
                        globalSeen[id] = sheetName;

                    _itemIds.Add(id);

                    var isRecipe = string.Equals(sheetName, RecipesSheet, StringComparison.OrdinalIgnoreCase);
                    if (IsRuntimeEnabled(row))
                    {
                        _enabledItemIds.Add(id);
                        if (isRecipe)
                            _enabledRecipeIds.Add(id);
                    }
                }
            }

            private void CollectCraftIds()
            {
                if (!_tables.TryGetValue(CraftDefinitionsSheet, out var table) || !table.HasColumn("craft_id"))
                    return;

                var firstRows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    var craftId = row.Get("craft_id");
                    if (string.IsNullOrWhiteSpace(craftId))
                    {
                        AddIssue(CraftDefinitionsSheet, row.RowNumber, "craft_id", craftId, "craft_id is required.");
                        continue;
                    }

                    if (firstRows.TryGetValue(craftId, out var firstRow))
                        AddIssue(CraftDefinitionsSheet, row.RowNumber, "craft_id", craftId, $"Duplicate craft_id; first declared at row {firstRow}.");
                    else
                        firstRows[craftId] = row.RowNumber;

                }
            }

            private void ValidateItemSheet(string sheetName, string expectedKind)
            {
                if (!_tables.TryGetValue(sheetName, out var table))
                    return;

                foreach (var row in table.DataRows)
                {
                    ValidateCommonItemRow(row, expectedKind);
                    ValidateTypedItemFields(row);
                    ValidatePackedFields(row);

                    if (string.Equals(sheetName, EquipmentWeaponsSheet, StringComparison.OrdinalIgnoreCase))
                        ValidateWeapon(row);
                    else if (string.Equals(sheetName, EquipmentArmorSheet, StringComparison.OrdinalIgnoreCase))
                        ValidateArmor(row);
                    else if (string.Equals(sheetName, RecipesSheet, StringComparison.OrdinalIgnoreCase))
                        ValidateRecipe(row);
                    else if (string.Equals(sheetName, ConsumablesSheet, StringComparison.OrdinalIgnoreCase))
                        ValidateConsumable(row);
                }
            }

            private void ValidateCommonItemRow(ConfigSheetDataRow row, string expectedKind)
            {
                ValidateRequired(row, "id");
                ValidateRequired(row, "name_id");
                ValidateRequired(row, "description_id");
                ValidateRequired(row, "icon_id");

                var kind = row.Get("kind");
                if (string.IsNullOrWhiteSpace(kind))
                {
                    AddIssue(row.Table.Name, row.RowNumber, "kind", kind, "kind is required.");
                }
                else if (!string.Equals(kind, expectedKind, StringComparison.OrdinalIgnoreCase))
                {
                    AddIssue(row.Table.Name, row.RowNumber, "kind", kind, $"kind must be '{expectedKind}' for this sheet.");
                }

                if (row.Table.HasColumn("rarity_id"))
                    ValidateRequired(row, "rarity_id");

                if (row.Table.HasColumn("tier"))
                    ValidateNumberGreaterThan(row, "tier", 0, "tier must be greater than 0.");

                if (row.Table.HasColumn("visibility_item_id") && !string.IsNullOrWhiteSpace(row.Get("visibility_item_id")))
                    ValidateNumberGreaterThan(row, "visibility_item_count", 0, "visibility_item_count must be greater than 0 when visibility_item_id is set.");

            }

            private void ValidateTypedItemFields(ConfigSheetDataRow row)
            {
                foreach (var column in new[] { "consume_visibility_item", "hidden_until_visibility_item" })
                {
                    if (row.Table.HasColumn(column))
                        TryParseBool(row, column, required: false, out _);
                }

                foreach (var column in new[]
                         {
                             "tier", "craft_duration_sec", "visibility_item_count", "output_count", "skill_exp",
                             "weapon_damage_min", "weapon_damage_max", "weapon_attack_interval",
                             "physical_resist_bonus", "magic_resist_bonus", "max_hp_bonus", "cooldown_seconds",
                             "check_interval_seconds"
                         })
                {
                    if (!row.Table.HasColumn(column))
                        continue;

                    var value = row.Get(column);
                    if (!string.IsNullOrWhiteSpace(value) && !ConfigPipelineUtilities.TryParseNumber(value, out _))
                        AddIssue(row.Table.Name, row.RowNumber, column, value, "Expected a number.");
                }
            }

            private void ValidatePackedFields(ConfigSheetDataRow row)
            {
                ValidatePackedRefs(row, "materials", "id", "count");
                ValidatePackedRefs(row, "required_buildings", "building_id", "level");
                ValidatePackedRefs(row, "required_skills", "skill_id", "level");
            }

            private void ValidateWeapon(ConfigSheetDataRow row)
            {
                ValidateRequired(row, "equipment_slot");
                ValidateEquipmentSlot(row);

                if (TryParseNumber(row, "weapon_damage_min", out var damageMin) &&
                    TryParseNumber(row, "weapon_damage_max", out var damageMax) &&
                    damageMin > damageMax)
                {
                    AddIssue(EquipmentWeaponsSheet, row.RowNumber, "weapon_damage_max", row.Get("weapon_damage_max"), "weapon_damage_min must be <= weapon_damage_max.");
                }

                ValidateNumberGreaterThan(row, "weapon_attack_interval", 0, "weapon_attack_interval must be greater than 0.");
                ValidateRequired(row, "attack_range");
                ValidateRequired(row, "damage_type");
            }

            private void ValidateArmor(ConfigSheetDataRow row)
            {
                ValidateRequired(row, "equipment_slot");
                ValidateEquipmentSlot(row);
                ValidateNumberGreaterThanOrEqual(row, "physical_resist_bonus", 0, "physical_resist_bonus must be greater than or equal to 0.");
                ValidateNumberGreaterThanOrEqual(row, "magic_resist_bonus", 0, "magic_resist_bonus must be greater than or equal to 0.");
                ValidateNumberGreaterThanOrEqual(row, "max_hp_bonus", 0, "max_hp_bonus must be greater than or equal to 0.");
            }

            private void ValidateRecipe(ConfigSheetDataRow row)
            {
                TryParseBool(row, "enabled", required: true, out _);
            }

            private void ValidateConsumable(ConfigSheetDataRow row)
            {
                ValidateRequired(row, "use_place");
                ValidateRequired(row, "effects");
                ValidateNumberGreaterThanOrEqual(row, "cooldown_seconds", 0, "cooldown_seconds must be greater than or equal to 0.");
                ValidateNumberGreaterThan(row, "check_interval_seconds", 0, "check_interval_seconds must be greater than 0.");
            }

            private void ValidateEquipmentSlot(ConfigSheetDataRow row)
            {
                var slot = row.Get("equipment_slot");
                if (!string.IsNullOrWhiteSpace(slot) && !EquipmentSlots.Contains(slot))
                    AddIssue(row.Table.Name, row.RowNumber, "equipment_slot", slot, "equipment_slot is not a canonical equipment slot.");
            }

            private void ValidateCraftDefinitions()
            {
                if (!_tables.TryGetValue(CraftDefinitionsSheet, out var table))
                    return;

                foreach (var row in table.DataRows)
                {
                    foreach (var column in RequiredColumns[CraftDefinitionsSheet])
                    {
                        if (!string.Equals(column, "notes", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(column, "required_recipe_item_id", StringComparison.OrdinalIgnoreCase) &&
                            string.IsNullOrWhiteSpace(row.Get(column)))
                        {
                            AddIssue(CraftDefinitionsSheet, row.RowNumber, column, row.Get(column), $"{column} is required.");
                        }
                    }

                    foreach (var column in new[] { "consume_recipe_item", "enabled" })
                        TryParseBool(row, column, required: true, out _);

                    foreach (var column in new[] { "craft_duration_sec", "required_recipe_item_count", "output_count", "fatigue_cost", "skill_exp" })
                    {
                        var value = row.Get(column);
                        if (!string.IsNullOrWhiteSpace(value) && !ConfigPipelineUtilities.TryParseNumber(value, out _))
                            AddIssue(CraftDefinitionsSheet, row.RowNumber, column, value, "Expected a number.");
                    }

                    ValidateNumberGreaterThan(row, "craft_duration_sec", 0, "craft_duration_sec must be greater than 0.");
                    ValidateNumberGreaterThan(row, "output_count", 0, "output_count must be greater than 0.");
                    ValidateNumberGreaterThanOrEqual(row, "fatigue_cost", 0, "fatigue_cost must be greater than or equal to 0.");
                    ValidateNumberGreaterThanOrEqual(row, "skill_exp", 0, "skill_exp must be greater than or equal to 0.");
                    ValidatePackedRefs(row, "materials", "id", "count");
                    ValidatePackedRefs(row, "required_buildings", "building_id", "level");

                    // Disabled craft definitions are not exported. Their runtime references are
                    // intentionally ignored, while their own shape and scalar fields remain validated.
                    if (!IsRuntimeEnabled(row))
                        continue;

                    var targetItemId = row.Get("target_item_id");
                    if (!string.IsNullOrWhiteSpace(targetItemId) && !_enabledItemIds.Contains(targetItemId))
                        AddIssue(CraftDefinitionsSheet, row.RowNumber, "target_item_id", targetItemId, "Referenced target_item_id is not exported by Items Configs.");

                    var recipeItemId = row.Get("required_recipe_item_id");
                    if (string.IsNullOrWhiteSpace(recipeItemId))
                    {
                        if (GetBool(row, "consume_recipe_item"))
                            AddIssue(CraftDefinitionsSheet, row.RowNumber, "consume_recipe_item", row.Get("consume_recipe_item"), "consume_recipe_item requires required_recipe_item_id.");
                    }
                    else
                    {
                        if (!_enabledRecipeIds.Contains(recipeItemId))
                            AddIssue(CraftDefinitionsSheet, row.RowNumber, "required_recipe_item_id", recipeItemId, "Referenced required_recipe_item_id is not exported by enabled Recipes.id.");
                        ValidateNumberGreaterThan(row, "required_recipe_item_count", 0, "required_recipe_item_count must be greater than 0 when required_recipe_item_id is set.");
                    }
                }
            }

            private static bool IsRuntimeEnabled(ConfigSheetDataRow row)
            {
                return !row.Table.HasColumn("enabled") ||
                       string.Equals(row.Get("enabled"), "TRUE", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(row.Get("enabled"), "true", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(row.Get("enabled"), "1", StringComparison.OrdinalIgnoreCase);
            }

            private void ValidateCurrencies()
            {
                if (!_tables.TryGetValue(CurrenciesSheet, out var table))
                    return;

                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    ValidateRequired(row, "currency_id");
                    ValidateRequired(row, "icon_id");
                    ValidateRequired(row, "name_id");
                    ValidateRequired(row, "description_id");

                    var currencyId = row.Get("currency_id");
                    if (string.IsNullOrWhiteSpace(currencyId))
                        continue;

                    if (seen.TryGetValue(currencyId, out var firstRow))
                        AddIssue(CurrenciesSheet, row.RowNumber, "currency_id", currencyId, $"Duplicate currency_id; first declared at row {firstRow}.");
                    else
                        seen[currencyId] = row.RowNumber;

                    if (_itemIds.Contains(currencyId))
                        AddIssue(CurrenciesSheet, row.RowNumber, "currency_id", currencyId, "currency_id must not match an item id.");
                }
            }

            private List<Dictionary<string, object>> BuildRows(string sheetName, Func<ConfigSheetDataRow, Dictionary<string, object>> buildRow)
            {
                var rows = new List<Dictionary<string, object>>();
                if (!_tables.TryGetValue(sheetName, out var table))
                    return rows;

                foreach (var row in table.DataRows)
                    rows.Add(buildRow(row));

                return rows;
            }

            private List<Dictionary<string, object>> BuildEnabledRows(string sheetName, Func<ConfigSheetDataRow, Dictionary<string, object>> buildRow)
            {
                var rows = new List<Dictionary<string, object>>();
                if (!_tables.TryGetValue(sheetName, out var table))
                    return rows;

                foreach (var row in table.DataRows)
                {
                    if (GetBool(row, "enabled"))
                        rows.Add(buildRow(row));
                }

                return rows;
            }

            private Dictionary<string, object> BuildResourceRow(ConfigSheetDataRow row)
            {
                var values = BuildCraftableItemBase(row);
                return values;
            }

            private Dictionary<string, object> BuildEquipmentWeaponRow(ConfigSheetDataRow row)
            {
                var values = BuildCraftableItemBase(row);
                values["equipmentSlot"] = row.Get("equipment_slot");
                values["craftMainStatId"] = row.Get("craft_main_stat_id");
                values["weaponDamageMin"] = GetNumber(row, "weapon_damage_min");
                values["weaponDamageMax"] = GetNumber(row, "weapon_damage_max");
                values["weaponAttackInterval"] = GetNumber(row, "weapon_attack_interval");
                values["attackRange"] = row.Get("attack_range");
                values["damageType"] = row.Get("damage_type");
                MoveAfter(values, "equipmentSlot", "subtype");
                MoveAfter(values, "craftMainStatId", "craftSkillId");
                return values;
            }

            private Dictionary<string, object> BuildEquipmentArmorRow(ConfigSheetDataRow row)
            {
                var values = BuildCraftableItemBase(row);
                values["equipmentSlot"] = row.Get("equipment_slot");
                values["craftMainStatId"] = row.Get("craft_main_stat_id");
                values["physicalResistBonus"] = GetNumber(row, "physical_resist_bonus");
                values["magicResistBonus"] = GetNumber(row, "magic_resist_bonus");
                values["maxHpBonus"] = GetNumber(row, "max_hp_bonus");
                MoveAfter(values, "equipmentSlot", "subtype");
                MoveAfter(values, "craftMainStatId", "craftSkillId");
                return values;
            }

            private Dictionary<string, object> BuildRecipeRow(ConfigSheetDataRow row)
            {
                return new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["id"] = row.Get("id"),
                    ["nameId"] = row.Get("name_id"),
                    ["descriptionId"] = row.Get("description_id"),
                    ["iconId"] = row.Get("icon_id"),
                    ["kind"] = row.Get("kind"),
                    ["rarityId"] = row.Get("rarity_id"),
                    ["tier"] = GetNumber(row, "tier"),
                    ["enabled"] = GetBool(row, "enabled")
                };
            }

            private Dictionary<string, object> BuildCraftDefinitionRow(ConfigSheetDataRow row)
            {
                return new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["craftId"] = row.Get("craft_id"),
                    ["targetItemId"] = row.Get("target_item_id"),
                    ["craftStationId"] = row.Get("craft_station_id"),
                    ["craftDurationSec"] = GetNumber(row, "craft_duration_sec"),
                    ["craftSkillId"] = row.Get("craft_skill_id"),
                    ["requiredBuildings"] = ParseRequiredBuildings(row.Get("required_buildings")),
                    ["materials"] = ParseMaterials(row.Get("materials")),
                    ["requiredRecipeItemId"] = row.Get("required_recipe_item_id"),
                    ["requiredRecipeItemCount"] = GetNumber(row, "required_recipe_item_count"),
                    ["consumeRecipeItem"] = GetBool(row, "consume_recipe_item"),
                    ["outputCount"] = GetNumber(row, "output_count"),
                    ["fatigueCost"] = GetNumber(row, "fatigue_cost"),
                    ["skillExp"] = GetNumber(row, "skill_exp")
                };
            }

            private Dictionary<string, object> BuildConsumableRow(ConfigSheetDataRow row)
            {
                return new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["id"] = row.Get("id"),
                    ["nameId"] = row.Get("name_id"),
                    ["descriptionId"] = row.Get("description_id"),
                    ["iconId"] = row.Get("icon_id"),
                    ["kind"] = row.Get("kind"),
                    ["rarityId"] = row.Get("rarity_id"),
                    ["usePlace"] = row.Get("use_place"),
                    ["useCondition"] = row.Get("use_condition"),
                    ["effects"] = SplitSemicolonList(row.Get("effects")),
                    ["cooldownSeconds"] = GetNumber(row, "cooldown_seconds"),
                    ["checkIntervalSeconds"] = GetNumber(row, "check_interval_seconds")
                };
            }

            private Dictionary<string, object> BuildCurrencyRow(ConfigSheetDataRow row)
            {
                return new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["currencyId"] = row.Get("currency_id"),
                    ["iconId"] = row.Get("icon_id"),
                    ["nameId"] = row.Get("name_id"),
                    ["descriptionId"] = row.Get("description_id")
                };
            }

            private Dictionary<string, object> BuildCraftableItemBase(ConfigSheetDataRow row)
            {
                return new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["id"] = row.Get("id"),
                    ["nameId"] = row.Get("name_id"),
                    ["descriptionId"] = row.Get("description_id"),
                    ["iconId"] = row.Get("icon_id"),
                    ["kind"] = row.Get("kind"),
                    ["subtype"] = row.Get("subtype"),
                    ["rarityId"] = row.Get("rarity_id"),
                    ["tier"] = GetNumber(row, "tier"),
                    ["craftStationId"] = row.Get("craft_station_id"),
                    ["craftDurationSec"] = GetNumber(row, "craft_duration_sec"),
                    ["craftSkillId"] = row.Get("craft_skill_id"),
                    ["requiredBuildings"] = ParseRequiredBuildings(row.Get("required_buildings")),
                    ["requiredSkills"] = ParseRequiredSkills(row.Get("required_skills")),
                    ["visibilityItemId"] = row.Get("visibility_item_id"),
                    ["visibilityItemCount"] = GetNumber(row, "visibility_item_count"),
                    ["consumeVisibilityItem"] = GetBool(row, "consume_visibility_item"),
                    ["hiddenUntilVisibilityItem"] = GetBool(row, "hidden_until_visibility_item"),
                    ["outputCount"] = GetNumber(row, "output_count"),
                    ["materials"] = ParseMaterials(row.Get("materials")),
                    ["sourceActivityId"] = row.Get("source_activity_id"),
                    ["skillExp"] = GetNumber(row, "skill_exp")
                };
            }

            private void ValidatePackedRefs(ConfigSheetDataRow row, string column, string idName, string countName)
            {
                if (!row.Table.HasColumn(column))
                    return;

                var raw = row.Get(column);
                if (string.IsNullOrWhiteSpace(raw))
                    return;

                var refs = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var packedRef in refs)
                {
                    var parts = packedRef.Split(':');
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                    {
                        AddIssue(row.Table.Name, row.RowNumber, column, packedRef.Trim(), $"Expected {column} format {idName}:{countName}; {idName}:{countName}.");
                        continue;
                    }

                    var id = parts[0].Trim();
                    var count = parts[1].Trim();
                    if (string.Equals(id, ForbiddenLegacyItemId, StringComparison.OrdinalIgnoreCase))
                        AddIssue(row.Table.Name, row.RowNumber, column, id, "item_gold is a forbidden legacy item id in Items Configs.");

                    if (string.Equals(id, GoldCurrencyId, StringComparison.OrdinalIgnoreCase))
                        AddIssue(row.Table.Name, row.RowNumber, column, id, "gold_id is a currency_id and must not be used as an item/material reference.");

                    if (!long.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                        AddIssue(row.Table.Name, row.RowNumber, column, packedRef.Trim(), $"{countName} in packed reference must be an integer greater than 0.");
                }
            }

            private void ValidateRequired(ConfigSheetDataRow row, string column)
            {
                if (!row.Table.HasColumn(column))
                    return;

                var value = row.Get(column);
                if (string.IsNullOrWhiteSpace(value))
                    AddIssue(row.Table.Name, row.RowNumber, column, value, $"{column} is required.");
            }

            private void ValidateNumberGreaterThan(ConfigSheetDataRow row, string column, double minimum, string message)
            {
                if (!row.Table.HasColumn(column))
                    return;

                var raw = row.Get(column);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    AddIssue(row.Table.Name, row.RowNumber, column, raw, message);
                    return;
                }

                if (ConfigPipelineUtilities.TryParseNumber(raw, out var value) && value <= minimum)
                    AddIssue(row.Table.Name, row.RowNumber, column, raw, message);
            }

            private void ValidateNumberGreaterThanOrEqual(ConfigSheetDataRow row, string column, double minimum, string message)
            {
                if (!row.Table.HasColumn(column))
                    return;

                var raw = row.Get(column);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    AddIssue(row.Table.Name, row.RowNumber, column, raw, message);
                    return;
                }

                if (ConfigPipelineUtilities.TryParseNumber(raw, out var value) && value < minimum)
                    AddIssue(row.Table.Name, row.RowNumber, column, raw, message);
            }

            private bool TryParseNumber(ConfigSheetDataRow row, string column, out double value)
            {
                return ConfigPipelineUtilities.TryParseNumber(row.Get(column), out value);
            }

            private bool TryParseBool(ConfigSheetDataRow row, string column, bool required, out bool value)
            {
                value = false;
                var raw = row.Get(column);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    if (required)
                        AddIssue(row.Table.Name, row.RowNumber, column, raw, "Boolean value is required.");

                    return false;
                }

                if (string.Equals(raw, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase))
                {
                    value = true;
                    return true;
                }

                if (string.Equals(raw, "FALSE", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(raw, "0", StringComparison.OrdinalIgnoreCase))
                {
                    value = false;
                    return true;
                }

                AddIssue(row.Table.Name, row.RowNumber, column, raw, "Expected TRUE or FALSE.");
                return false;
            }

            private double GetNumber(ConfigSheetDataRow row, string column)
            {
                return ConfigPipelineUtilities.TryParseNumber(row.Get(column), out var number) ? number : 0d;
            }

            private bool GetBool(ConfigSheetDataRow row, string column)
            {
                TryParseBool(row, column, required: false, out var value);
                return value;
            }

            private static List<Dictionary<string, object>> ParseMaterials(string raw)
            {
                return ParsePackedObjects(raw, "id", "count");
            }

            private static List<Dictionary<string, object>> ParseRequiredBuildings(string raw)
            {
                return ParsePackedObjects(raw, "buildingId", "level");
            }

            private static List<Dictionary<string, object>> ParseRequiredSkills(string raw)
            {
                return ParsePackedObjects(raw, "skillId", "level");
            }

            private static List<Dictionary<string, object>> ParsePackedObjects(string raw, string idField, string countField)
            {
                var values = new List<Dictionary<string, object>>();
                if (string.IsNullOrWhiteSpace(raw))
                    return values;

                var refs = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var packedRef in refs)
                {
                    var parts = packedRef.Split(':');
                    if (parts.Length != 2)
                        continue;

                    var id = parts[0].Trim();
                    var count = parts[1].Trim();
                    if (string.IsNullOrWhiteSpace(id) ||
                        !long.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        continue;
                    }

                    values.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        [idField] = id,
                        [countField] = parsed
                    });
                }

                return values;
            }

            private static List<string> SplitSemicolonList(string raw)
            {
                var values = new List<string>();
                if (string.IsNullOrWhiteSpace(raw))
                    return values;

                var parts = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var value = part.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        values.Add(value);
                }

                return values;
            }

            private static void MoveAfter(Dictionary<string, object> values, string key, string previousKey)
            {
                if (!values.TryGetValue(key, out var value))
                    return;

                values.Remove(key);
                var reordered = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var pair in values)
                {
                    reordered[pair.Key] = pair.Value;
                    if (string.Equals(pair.Key, previousKey, StringComparison.Ordinal))
                        reordered[key] = value;
                }

                values.Clear();
                foreach (var pair in reordered)
                    values[pair.Key] = pair.Value;
            }

            private void AddIssue(string sheet, int row, string column, string value, string message)
            {
                _report.Issues.Add(new ConfigValidationIssue(sheet, row, column, value, message));
            }
        }

    }
}
