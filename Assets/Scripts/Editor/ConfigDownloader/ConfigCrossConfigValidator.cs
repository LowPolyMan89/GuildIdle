using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public static class ConfigCrossConfigValidator
    {
        private const string HeroesConfigId = "heroes_configs";
        private const string ActivityConfigId = "activity_configs";
        private const string EnemiesConfigId = "enemies_configs";
        private const string StorageConfigId = "storage_configs";
        private const string MapConfigId = "map_configs";
        private const string ItemsConfigId = "items_configs";
        private const string FormulaConfigId = "formula_configs";
        private const string LootConfigId = "loot_configs";
        private const string BuildingsConfigId = "buildings_configs";
        private const string LocalisationConfigId = "localisation";
        private const string LocalisationConfigIdAlias = "localisation_configs";

        private const string GoldCurrencyId = "gold_id";
        private const string ForbiddenLegacyItemId = "item_gold";

        private const string ResourcesSheet = "Р РµСЃСѓСЂСЃС‹";
        private const string EquipmentWeaponsSheet = "РЎРЅР°СЂСЏР¶РµРЅРёРµ - РѕСЂСѓР¶РёРµ";
        private const string EquipmentArmorSheet = "РЎРЅР°СЂСЏР¶РµРЅРёРµ - Р±СЂРѕРЅСЏ";
        private const string RecipesSheet = "Р РµС†РµРїС‚С‹";
        private const string ConsumablesSheet = "Р Р°СЃС…РѕРґРЅРёРєРё";
        private const string CurrenciesSheet = "Р’Р°Р»СЋС‚С‹";

        private static readonly HashSet<string> HeroStatIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Strength",
            "Agility",
            "Intelligence",
            "Endurance",
            "Luck"
        };

        public static ConfigPipelineReport Validate(ConfigSourceSettingsCollection collection)
        {
            var report = new ConfigPipelineReport();
            var registry = ConfigRegistry.Build(collection);

            ValidateForbiddenLegacyIdEverywhere(registry, report);
            HeroesCrossChecks.Validate(registry, report);
            ActivityCrossChecks.Validate(registry, report);
            EnemiesCrossChecks.Validate(registry, report);
            StorageCrossChecks.Validate(registry, report);
            ItemsCrossChecks.Validate(registry, report);
            FormulaCrossChecks.Validate(registry, report);
            LootCrossChecks.Validate(registry, report);
            BuildingsCrossChecks.Validate(registry, report);
            MapCrossChecks.Validate(registry, report);

            return report;
        }

        public static void ApplyToSources(ConfigSourceSettingsCollection collection)
        {
            var report = Validate(collection);
            var message = report.ToDisplayMessage();
            if (string.IsNullOrWhiteSpace(message))
                return;

            if (report.Issues.Count > 0)
            {
                Debug.LogError($"Cross-config validation failed:\n{message}");
                ApplyReportMessage(collection, message, ConfigPipelineStatus.ValidationError);
                return;
            }

            if (report.Warnings.Count > 0)
            {
                Debug.LogWarning($"Cross-config validation completed with warnings:\n{message}");
                ApplyReportMessage(collection, message, ConfigPipelineStatus.Success);
            }
        }

        private static void ValidateForbiddenLegacyIdEverywhere(ConfigRegistry registry, ConfigPipelineReport report)
        {
            foreach (var source in registry.Sources)
            {
                foreach (var table in source.Tables.Values)
                {
                    foreach (var row in table.DataRows)
                    {
                        foreach (var column in table.Headers)
                        {
                            var value = row.Get(column);
                            if (string.Equals(value, ForbiddenLegacyItemId, StringComparison.OrdinalIgnoreCase))
                                AddIssue(report, source.DisplayName, table.Name, row.RowNumber, column, value, "item_gold is a forbidden legacy id.");
                        }
                    }
                }
            }
        }

        private static void ApplyReportMessage(ConfigSourceSettingsCollection collection, string message, string validationStatus)
        {
            if (collection?.sources == null)
                return;

            foreach (var source in collection.sources)
            {
                if (source == null || !IsKnownConfig(source.config_id))
                    continue;

                source.last_validation_status = validationStatus;
                source.last_validation_time = DateTime.UtcNow.ToString("o");
                source.error_message = message;
            }
        }

        private static bool IsKnownConfig(string configId)
        {
            return IsConfig(configId, ActivityConfigId) ||
                   IsConfig(configId, HeroesConfigId) ||
                   IsConfig(configId, EnemiesConfigId) ||
                   IsConfig(configId, StorageConfigId) ||
                   IsConfig(configId, MapConfigId) ||
                   IsConfig(configId, ItemsConfigId) ||
                   IsConfig(configId, FormulaConfigId) ||
                   IsConfig(configId, LootConfigId) ||
                   IsConfig(configId, BuildingsConfigId) ||
                   IsLocalisationConfig(configId);
        }

        private static bool IsConfig(string actual, string expected)
        {
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLocalisationConfig(string configId)
        {
            return IsConfig(configId, LocalisationConfigId) || IsConfig(configId, LocalisationConfigIdAlias);
        }

        private static void AddMissingRegistryWarning(ConfigPipelineReport report, string registryName)
        {
            var warning = $"Cross-config validation skipped: {registryName} registry is not available yet.";
            foreach (var existing in report.Warnings)
            {
                if (string.Equals(existing, warning, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            report.Warnings.Add(warning);
        }

        private static void AddIssue(
            ConfigPipelineReport report,
            string sourceConfig,
            string sheet,
            int row,
            string column,
            string value,
            string message)
        {
            var displaySheet = string.IsNullOrWhiteSpace(sourceConfig)
                ? sheet
                : $"{sourceConfig} / {sheet}";
            report.Issues.Add(new ConfigValidationIssue(displaySheet, row, column, value, message));
        }

        private static bool TryGetRequiredRegistry<TRegistry>(
            ConfigPipelineReport report,
            TRegistry registry,
            string registryName)
            where TRegistry : class
        {
            if (registry != null)
                return true;

            AddMissingRegistryWarning(report, registryName);
            return false;
        }

        private static bool IsBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value);
        }

        private static bool HasAnyValue(ConfigSheetTable table, string column)
        {
            if (table == null || !table.HasColumn(column))
                return false;

            foreach (var row in table.DataRows)
            {
                if (!IsBlank(row.Get(column)))
                    return true;
            }

            return false;
        }

        private static IEnumerable<PackedRef> ParsePackedRefs(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                yield break;

            var refs = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var packedRef in refs)
            {
                var trimmed = packedRef.Trim();
                var parts = trimmed.Split(':');
                if (parts.Length != 2)
                    continue;

                var id = parts[0].Trim();
                var value = parts[1].Trim();
                if (!string.IsNullOrWhiteSpace(id))
                    yield return new PackedRef(id, value, trimmed);
            }
        }

        private static bool IsBuildActionId(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.StartsWith("build_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCraftOrProcessActionId(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   (value.StartsWith("craft_", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("process_", StringComparison.OrdinalIgnoreCase));
        }

        private readonly struct PackedRef
        {
            public string Id { get; }
            public string Value { get; }
            public string Raw { get; }

            public PackedRef(string id, string value, string raw)
            {
                Id = id ?? string.Empty;
                Value = value ?? string.Empty;
                Raw = raw ?? string.Empty;
            }
        }

        private sealed class ConfigRegistry
        {
            private readonly Dictionary<string, LoadedConfig> _sources = new Dictionary<string, LoadedConfig>(StringComparer.OrdinalIgnoreCase);

            public IReadOnlyCollection<LoadedConfig> Sources => _sources.Values;

            public HeroesRegistry Heroes { get; private set; }
            public ActivityRegistry Activity { get; private set; }
            public EnemiesRegistry Enemies { get; private set; }
            public StorageRegistry Storage { get; private set; }
            public MapRegistry Map { get; private set; }
            public ItemsRegistry Items { get; private set; }
            public FormulaRegistry Formula { get; private set; }
            public LootRegistry Loot { get; private set; }
            public BuildingsRegistry Buildings { get; private set; }
            public LocalisationRegistry Localisation { get; private set; }

            public static ConfigRegistry Build(ConfigSourceSettingsCollection collection)
            {
                var registry = new ConfigRegistry();
                if (collection?.sources == null)
                    return registry;

                foreach (var source in collection.sources)
                {
                    if (source == null)
                        continue;

                    var loadReport = new ConfigPipelineReport();
                    if (!ConfigPipelineUtilities.TryLoadDownload(source, loadReport, out var download))
                        continue;

                    var loaded = new LoadedConfig(source, download);
                    registry._sources[source.config_id ?? string.Empty] = loaded;

                    if (IsLocalisationConfig(source.config_id))
                        registry._sources[LocalisationConfigId] = loaded;
                }

                registry.Heroes = HeroesRegistry.TryBuild(registry.Get(HeroesConfigId));
                registry.Activity = ActivityRegistry.TryBuild(registry.Get(ActivityConfigId));
                registry.Enemies = EnemiesRegistry.TryBuild(registry.Get(EnemiesConfigId));
                registry.Storage = StorageRegistry.TryBuild(registry.Get(StorageConfigId));
                registry.Map = MapRegistry.TryBuild(registry.Get(MapConfigId));
                registry.Items = ItemsRegistry.TryBuild(registry.Get(ItemsConfigId));
                registry.Formula = FormulaRegistry.TryBuild(registry.Get(FormulaConfigId));
                registry.Loot = LootRegistry.TryBuild(registry.Get(LootConfigId));
                registry.Buildings = BuildingsRegistry.TryBuild(registry.Get(BuildingsConfigId));
                registry.Localisation = LocalisationRegistry.TryBuild(registry.Get(LocalisationConfigId));

                return registry;
            }

            private LoadedConfig Get(string configId)
            {
                _sources.TryGetValue(configId, out var source);
                return source;
            }
        }

        private sealed class LoadedConfig
        {
            private readonly Dictionary<string, ConfigDownloadedSheet> _rawSheets = new Dictionary<string, ConfigDownloadedSheet>(StringComparer.OrdinalIgnoreCase);

            public ConfigSourceSettings Source { get; }
            public string DisplayName { get; }
            public Dictionary<string, ConfigSheetTable> Tables { get; } = new Dictionary<string, ConfigSheetTable>(StringComparer.OrdinalIgnoreCase);

            public LoadedConfig(ConfigSourceSettings source, ConfigSheetDownload download)
            {
                Source = source;
                DisplayName = !string.IsNullOrWhiteSpace(download?.display_name)
                    ? download.display_name
                    : !string.IsNullOrWhiteSpace(source?.display_name)
                        ? source.display_name
                        : source?.config_id ?? string.Empty;

                foreach (var sheet in download?.sheets ?? Array.Empty<ConfigDownloadedSheet>())
                {
                    if (sheet == null || string.IsNullOrWhiteSpace(sheet.sheet_name))
                        continue;

                    Tables[sheet.sheet_name] = new ConfigSheetTable(sheet);
                    _rawSheets[sheet.sheet_name] = sheet;
                }
            }

            public bool TryGetTable(string sheetName, out ConfigSheetTable table)
            {
                return Tables.TryGetValue(sheetName, out table);
            }

            public bool TryGetRawSheet(string sheetName, out ConfigDownloadedSheet sheet)
            {
                return _rawSheets.TryGetValue(sheetName, out sheet);
            }

            public bool TryReadRuntimeJson(out string json)
            {
                json = null;
                if (Source == null ||
                    !ConfigPipelineUtilities.TryValidateRuntimeOutputPath(Source.runtime_json_path, out var fullPath, out _) ||
                    !File.Exists(fullPath))
                {
                    return false;
                }

                try
                {
                    json = File.ReadAllText(fullPath);
                    return !string.IsNullOrWhiteSpace(json);
                }
                catch
                {
                    json = null;
                    return false;
                }
            }
        }

        private sealed class HeroesRegistry
        {
            public LoadedConfig Source { get; }
            public HashSet<string> HeroIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            private HeroesRegistry(LoadedConfig source)
            {
                Source = source;
            }

            public static HeroesRegistry TryBuild(LoadedConfig source)
            {
                if (source == null)
                    return null;

                var registry = new HeroesRegistry(source);
                if (!source.TryGetRawSheet("Heroes", out var heroes))
                    return registry;

                var heroIdColumn = FindColumn(heroes, "HeroId", out var headerRow);
                if (heroIdColumn < 0)
                    return registry;

                var rows = heroes.rows ?? Array.Empty<ConfigSheetRow>();
                for (var rowIndex = headerRow + 1; rowIndex < rows.Length; rowIndex++)
                {
                    var heroId = Cell(rows[rowIndex], heroIdColumn);
                    if (!IsBlank(heroId))
                        registry.HeroIds.Add(heroId);
                }

                return registry;
            }

            private static int FindColumn(ConfigDownloadedSheet sheet, string column, out int headerRow)
            {
                var rows = sheet.rows ?? Array.Empty<ConfigSheetRow>();
                for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
                {
                    var cells = rows[rowIndex]?.cells ?? Array.Empty<string>();
                    for (var columnIndex = 0; columnIndex < cells.Length; columnIndex++)
                    {
                        if (string.Equals((cells[columnIndex] ?? string.Empty).Trim(), column, StringComparison.OrdinalIgnoreCase))
                        {
                            headerRow = rowIndex;
                            return columnIndex;
                        }
                    }
                }

                headerRow = -1;
                return -1;
            }

            private static string Cell(ConfigSheetRow row, int column)
            {
                if (row?.cells == null || column < 0 || column >= row.cells.Length)
                    return string.Empty;

                return (row.cells[column] ?? string.Empty).Trim();
            }
        }

        private sealed class ActivityRegistry
        {
            public LoadedConfig Source { get; }
            public HashSet<string> ActivityIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> SkillIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> RarityIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> ActivityTypes { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> ActivityCategories { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            private ActivityRegistry(LoadedConfig source)
            {
                Source = source;
            }

            public static ActivityRegistry TryBuild(LoadedConfig source)
            {
                if (source == null)
                    return null;

                var registry = new ActivityRegistry(source);
                if (source.TryGetTable("Activities", out var activities))
                {
                    foreach (var row in activities.DataRows)
                    {
                        var id = row.Get("id");
                        if (!IsBlank(id))
                        {
                            registry.ActivityIds.Add(id);
                            registry.ActivityTypes[id] = row.Get("type");
                            registry.ActivityCategories[id] = row.Get("category");
                        }
                    }
                }

                CollectIds(source, "Skills", "skill_id", registry.SkillIds);
                CollectIds(source, "Rarities", "id", registry.RarityIds);
                return registry;
            }
        }

        private sealed class EnemiesRegistry
        {
            public LoadedConfig Source { get; }
            public HashSet<string> EnemyGroupIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            private EnemiesRegistry(LoadedConfig source)
            {
                Source = source;
            }

            public static EnemiesRegistry TryBuild(LoadedConfig source)
            {
                if (source == null)
                    return null;

                var registry = new EnemiesRegistry(source);
                CollectIds(source, "EnemyGroups", "enemy_group_id", registry.EnemyGroupIds);
                return registry;
            }
        }

        private sealed class StorageRegistry
        {
            public LoadedConfig Source { get; }

            private StorageRegistry(LoadedConfig source)
            {
                Source = source;
            }

            public static StorageRegistry TryBuild(LoadedConfig source)
            {
                return source == null ? null : new StorageRegistry(source);
            }
        }

        private sealed class MapRegistry
        {
            public LoadedConfig Source { get; }
            public HashSet<string> CellIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> LocationIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            private MapRegistry(LoadedConfig source)
            {
                Source = source;
            }

            public static MapRegistry TryBuild(LoadedConfig source)
            {
                if (source == null)
                    return null;

                var registry = new MapRegistry(source);
                CollectIds(source, "MapCells", "cell_id", registry.CellIds);
                CollectIds(source, "MapLocations", "location_id", registry.LocationIds);
                return registry;
            }
        }

        private sealed class ItemsRegistry
        {
            public LoadedConfig Source { get; }
            public HashSet<string> ResourceIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> EquipmentIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> RecipeIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> ConsumableIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> CurrencyIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> ItemKinds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> ItemActionIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> AllItemIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            private ItemsRegistry(LoadedConfig source)
            {
                Source = source;
            }

            public static ItemsRegistry TryBuild(LoadedConfig source)
            {
                if (source == null)
                    return null;

                var registry = new ItemsRegistry(source);
                CollectItemSheet(source, ResourcesSheet, registry.ResourceIds, registry);
                CollectItemSheet(source, EquipmentWeaponsSheet, registry.EquipmentIds, registry);
                CollectItemSheet(source, EquipmentArmorSheet, registry.EquipmentIds, registry);
                CollectItemSheet(source, RecipesSheet, registry.RecipeIds, registry);
                CollectItemSheet(source, ConsumablesSheet, registry.ConsumableIds, registry);
                CollectIds(source, CurrenciesSheet, "currency_id", registry.CurrencyIds);
                CollectIds(source, CurrenciesSheet, "currencyId", registry.CurrencyIds);
                CollectRuntimeItems(source, registry);
                return registry;
            }

            public bool ContainsAnyItem(string id)
            {
                return AllItemIds.Contains(id);
            }

            public bool ContainsResource(string id)
            {
                return ResourceIds.Contains(id);
            }

            public bool ContainsCurrency(string id)
            {
                return CurrencyIds.Contains(id);
            }

            public bool ContainsEquipment(string id)
            {
                return EquipmentIds.Contains(id);
            }

            public bool ContainsRecipe(string id)
            {
                return RecipeIds.Contains(id);
            }

            public bool ContainsConsumable(string id)
            {
                return ConsumableIds.Contains(id);
            }
        }

        [Serializable]
        private sealed class ItemsRuntimeConfig
        {
            public RuntimeItem[] resources;
            public RuntimeItem[] equipmentWeapons;
            public RuntimeItem[] equipmentArmor;
            public RuntimeItem[] recipes;
            public RuntimeItem[] consumables;
            public RuntimeCurrency[] currencies;
        }

        [Serializable]
        private sealed class RuntimeItem
        {
            public string id;
            public string kind;
            public string sourceActivityId;
            public string craftStationId;
        }

        [Serializable]
        private sealed class RuntimeCurrency
        {
            public string currencyId;
        }

        private sealed class FormulaRegistry
        {
            public LoadedConfig Source { get; }
            public HashSet<string> ProfileIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            private FormulaRegistry(LoadedConfig source)
            {
                Source = source;
            }

            public static FormulaRegistry TryBuild(LoadedConfig source)
            {
                if (source == null)
                    return null;

                var registry = new FormulaRegistry(source);
                CollectIds(source, "SkillStatWeights", "profile_id", registry.ProfileIds);
                return registry;
            }
        }

        private sealed class LootRegistry
        {
            public LoadedConfig Source { get; }
            public HashSet<string> LootTableIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            private LootRegistry(LoadedConfig source)
            {
                Source = source;
            }

            public static LootRegistry TryBuild(LoadedConfig source)
            {
                if (source == null)
                    return null;

                var registry = new LootRegistry(source);
                CollectIds(source, "LootTables", "loot_table_id", registry.LootTableIds);
                return registry;
            }
        }

        private sealed class BuildingsRegistry
        {
            public LoadedConfig Source { get; }
            public HashSet<string> BuildingIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, long> BuildingMaxLevels { get; } = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> BuildActionIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            private BuildingsRegistry(LoadedConfig source)
            {
                Source = source;
            }

            public static BuildingsRegistry TryBuild(LoadedConfig source)
            {
                if (source == null)
                    return null;

                var registry = new BuildingsRegistry(source);
                if (source.TryGetTable("Index", out var index) &&
                    index.HasColumn("building_id"))
                {
                    foreach (var row in index.DataRows)
                    {
                        var buildingId = row.Get("building_id");
                        if (IsBlank(buildingId))
                            continue;

                        registry.BuildingIds.Add(buildingId);
                        if (long.TryParse(row.Get("levels"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var levels))
                            registry.BuildingMaxLevels[buildingId] = levels;
                    }
                }

                foreach (var table in source.Tables.Values)
                {
                    if (string.Equals(table.Name, "Index", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(table.Name, "README", StringComparison.OrdinalIgnoreCase) ||
                        table.Name.StartsWith("Craftables -", StringComparison.OrdinalIgnoreCase) ||
                        !table.HasColumn("source_activity_id"))
                    {
                        continue;
                    }

                    foreach (var row in table.DataRows)
                    {
                        var sourceActivityId = row.Get("source_activity_id");
                        if (IsBuildActionId(sourceActivityId))
                            registry.BuildActionIds.Add(sourceActivityId);
                    }
                }

                return registry;
            }

            public bool ContainsBuildingLevel(string buildingId, string levelText)
            {
                if (!BuildingIds.Contains(buildingId))
                    return false;

                if (!long.TryParse(levelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
                    return false;

                if (!BuildingMaxLevels.TryGetValue(buildingId, out var maxLevel))
                    return level > 0;

                return level > 0 && level <= maxLevel;
            }
        }

        private sealed class LocalisationRegistry
        {
            public LoadedConfig Source { get; }
            public HashSet<string> LocalisationIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            private LocalisationRegistry(LoadedConfig source)
            {
                Source = source;
            }

            public static LocalisationRegistry TryBuild(LoadedConfig source)
            {
                if (source == null)
                    return null;

                var registry = new LocalisationRegistry(source);
                foreach (var table in source.Tables.Values)
                {
                    if (!table.HasColumn("id"))
                        continue;

                    foreach (var row in table.DataRows)
                    {
                        var id = row.Get("id");
                        if (!IsBlank(id))
                            registry.LocalisationIds.Add(id);
                    }
                }

                return registry;
            }
        }

        private static void CollectIds(LoadedConfig source, string sheetName, string column, HashSet<string> ids)
        {
            if (source == null ||
                !source.TryGetTable(sheetName, out var table) ||
                !table.HasColumn(column))
            {
                return;
            }

            foreach (var row in table.DataRows)
            {
                var id = row.Get(column);
                if (!IsBlank(id))
                    ids.Add(id);
            }
        }

        private static void CollectItemSheet(LoadedConfig source, string sheetName, HashSet<string> ids, ItemsRegistry registry)
        {
            if (!source.TryGetTable(sheetName, out var table) || !table.HasColumn("id"))
                return;

            foreach (var row in table.DataRows)
            {
                var id = row.Get("id");
                if (!IsBlank(id))
                {
                    ids.Add(id);
                    registry.AllItemIds.Add(id);
                }

                var kind = row.Get("kind");
                if (!IsBlank(kind))
                    registry.ItemKinds.Add(kind);

                var sourceActivityId = row.Get("source_activity_id");
                var craftStationId = row.Get("craft_station_id");
                if (!IsBlank(sourceActivityId) && !IsBlank(craftStationId) && IsCraftOrProcessActionId(sourceActivityId))
                    registry.ItemActionIds.Add(sourceActivityId);
            }
        }

        private static void CollectRuntimeItems(LoadedConfig source, ItemsRegistry registry)
        {
            if (!source.TryReadRuntimeJson(out var json))
                return;

            ItemsRuntimeConfig runtime;
            try
            {
                runtime = JsonUtility.FromJson<ItemsRuntimeConfig>(json);
            }
            catch
            {
                return;
            }

            if (runtime == null)
                return;

            CollectRuntimeItemIds(runtime.resources, registry.ResourceIds, registry);
            CollectRuntimeItemIds(runtime.equipmentWeapons, registry.EquipmentIds, registry);
            CollectRuntimeItemIds(runtime.equipmentArmor, registry.EquipmentIds, registry);
            CollectRuntimeItemIds(runtime.recipes, registry.RecipeIds, registry);
            CollectRuntimeItemIds(runtime.consumables, registry.ConsumableIds, registry);
            CollectRuntimeCurrencyIds(runtime.currencies, registry.CurrencyIds);
        }

        private static void CollectRuntimeItemIds(RuntimeItem[] rows, HashSet<string> ids, ItemsRegistry registry)
        {
            foreach (var row in rows ?? Array.Empty<RuntimeItem>())
            {
                if (row == null)
                    continue;

                if (!IsBlank(row.id))
                {
                    ids.Add(row.id);
                    registry.AllItemIds.Add(row.id);
                }

                if (!IsBlank(row.kind))
                    registry.ItemKinds.Add(row.kind);

                if (!IsBlank(row.sourceActivityId) &&
                    !IsBlank(row.craftStationId) &&
                    IsCraftOrProcessActionId(row.sourceActivityId))
                {
                    registry.ItemActionIds.Add(row.sourceActivityId);
                }
            }
        }

        private static void CollectRuntimeCurrencyIds(RuntimeCurrency[] rows, HashSet<string> ids)
        {
            foreach (var row in rows ?? Array.Empty<RuntimeCurrency>())
            {
                if (row != null && !IsBlank(row.currencyId))
                    ids.Add(row.currencyId);
            }
        }

        private static class HeroesCrossChecks
        {
            public static void Validate(ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (registry.Heroes == null)
                    return;

                ValidateLocalisation(registry, report);
            }

            private static void ValidateLocalisation(ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!registry.Heroes.Source.TryGetTable("HeroUniqueSkills", out var table))
                    return;

                if (!HasAnyValue(table, "NameId") && !HasAnyValue(table, "DescriptionId"))
                    return;

                if (!TryGetRequiredRegistry(report, registry.Localisation, "Localisation"))
                    return;

                foreach (var row in table.DataRows)
                {
                    ValidateIdSet(report, registry.Heroes.Source.DisplayName, "HeroUniqueSkills", row, "NameId", registry.Localisation.LocalisationIds, "Localisation.id");
                    ValidateIdSet(report, registry.Heroes.Source.DisplayName, "HeroUniqueSkills", row, "DescriptionId", registry.Localisation.LocalisationIds, "Localisation.id");
                }
            }
        }

        private static class ActivityCrossChecks
        {
            public static void Validate(ConfigRegistry registry, ConfigPipelineReport report)
            {
                var activity = registry.Activity;
                if (activity == null)
                    return;

                ValidateActivities(activity, registry, report);
                ValidateRequirements(activity, registry, report);
                ValidateRewards(activity, registry, report);
                ValidateCombatDetails(activity, registry, report);
            }

            private static void ValidateActivities(ActivityRegistry activity, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!activity.Source.TryGetTable("Activities", out var table))
                    return;

                if (HasAnyValue(table, "stat_profile_id") &&
                    TryGetRequiredRegistry(report, registry.Formula, "Formula Configs"))
                {
                    foreach (var row in table.DataRows)
                        ValidateIdSet(report, activity.Source.DisplayName, "Activities", row, "stat_profile_id", registry.Formula.ProfileIds, "Formula Configs / SkillStatWeights.profile_id");
                }

                if (HasAnyValue(table, "location_id") &&
                    TryGetRequiredRegistry(report, registry.Map, "Map Configs"))
                {
                    foreach (var row in table.DataRows)
                        ValidateIdSet(report, activity.Source.DisplayName, "Activities", row, "location_id", registry.Map.LocationIds, "Map Configs / MapLocations.location_id");
                }
            }

            private static void ValidateRequirements(ActivityRegistry activity, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!activity.Source.TryGetTable("ActivityRequirements", out var table))
                    return;

                foreach (var row in table.DataRows)
                {
                    var targetId = row.Get("target_id");
                    if (IsBlank(targetId))
                        continue;

                    switch (row.Get("req_type"))
                    {
                        case "SkillLevel":
                            ValidateIdSet(report, activity.Source.DisplayName, "ActivityRequirements", row, "target_id", activity.SkillIds, "Activity Configs / Skills.skill_id");
                            break;
                        case "LocationUnlocked":
                            if (TryGetRequiredRegistry(report, registry.Map, "Map Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRequirements", row, "target_id", registry.Map.LocationIds, "Map Configs / MapLocations.location_id");
                            break;
                        case "BuildingLevel":
                            if (TryGetRequiredRegistry(report, registry.Buildings, "Buildings Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRequirements", row, "target_id", registry.Buildings.BuildingIds, "Buildings Configs / Index.building_id");
                            break;
                        case "ItemCount":
                        case "Item":
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateItemTarget(report, activity.Source.DisplayName, "ActivityRequirements", row, "target_id", registry.Items, ItemTargetKind.AnyItem, "Items Configs item/resource/recipe/consumable registry");
                            break;
                        case "Currency":
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRequirements", row, "target_id", registry.Items.CurrencyIds, "Items Configs / Валюты.currency_id");
                            break;
                        case "ActivityCompleted":
                            ValidateIdSet(report, activity.Source.DisplayName, "ActivityRequirements", row, "target_id", activity.ActivityIds, "Activity Configs / Activities.id");
                            break;
                        case "HeroAvailable":
                            if (TryGetRequiredRegistry(report, registry.Heroes, "Heroes Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRequirements", row, "target_id", registry.Heroes.HeroIds, "Heroes Configs / Heroes.HeroId");
                            break;
                    }
                }
            }

            private static void ValidateRewards(ActivityRegistry activity, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!activity.Source.TryGetTable("ActivityRewards", out var table))
                    return;

                foreach (var row in table.DataRows)
                {
                    var targetId = row.Get("target_id");
                    if (IsBlank(targetId))
                        continue;

                    switch (row.Get("reward_type"))
                    {
                        case "Resource":
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateItemTarget(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Items, ItemTargetKind.Resource, "Items Configs / Ресурсы.id");
                            break;
                        case "Item":
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateItemTarget(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Items, ItemTargetKind.AnyItem, "Items Configs item/recipe/consumable registry");
                            break;
                        case "Equipment":
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateItemTarget(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Items, ItemTargetKind.Equipment, "Items Configs / Снаряжение.id");
                            break;
                        case "Consumable":
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateItemTarget(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Items, ItemTargetKind.Consumable, "Items Configs / Расходники.id");
                            break;
                        case "Recipe":
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateItemTarget(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Items, ItemTargetKind.Recipe, "Items Configs / Рецепты.id");
                            break;
                        case "SkillExp":
                            ValidateIdSet(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", activity.SkillIds, "Activity Configs / Skills.skill_id");
                            break;
                        case "Currency":
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Items.CurrencyIds, "Items Configs / Валюты.currency_id");
                            break;
                        case "LootTable":
                            if (TryGetRequiredRegistry(report, registry.Loot, "Loot Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Loot.LootTableIds, "Loot Configs / LootTables.loot_table_id");
                            break;
                        case "Hero":
                            if (TryGetRequiredRegistry(report, registry.Heroes, "Heroes Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Heroes.HeroIds, "Heroes Configs / Heroes.HeroId");
                            break;
                        case "BuildingUnlock":
                            if (TryGetRequiredRegistry(report, registry.Buildings, "Buildings Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Buildings.BuildingIds, "Buildings Configs / Index.building_id");
                            break;
                        case "MapAccess":
                            if (TryGetRequiredRegistry(report, registry.Map, "Map Configs"))
                                ValidateMapAccess(report, activity.Source.DisplayName, "ActivityRewards", row, registry.Map);
                            break;
                    }
                }
            }

            private static void ValidateCombatDetails(ActivityRegistry activity, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!activity.Source.TryGetTable("CombatDetails", out var table) ||
                    !HasAnyValue(table, "enemy_group_id"))
                {
                    return;
                }

                if (!TryGetRequiredRegistry(report, registry.Enemies, "Enemies Configs"))
                    return;

                foreach (var row in table.DataRows)
                    ValidateIdSet(report, activity.Source.DisplayName, "CombatDetails", row, "enemy_group_id", registry.Enemies.EnemyGroupIds, "Enemies Configs / EnemyGroups.enemy_group_id");
            }
        }

        private static class EnemiesCrossChecks
        {
            public static void Validate(ConfigRegistry registry, ConfigPipelineReport report)
            {
                var enemies = registry.Enemies;
                if (enemies == null ||
                    !enemies.Source.TryGetTable("EnemyLoot", out var table) ||
                    !HasAnyValue(table, "loot_id"))
                {
                    return;
                }

                if (!TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                    return;

                foreach (var row in table.DataRows)
                    ValidateLootLikeItemReference(report, enemies.Source.DisplayName, "EnemyLoot", row, "loot_id", registry.Items);
            }
        }

        private static class StorageCrossChecks
        {
            public static void Validate(ConfigRegistry registry, ConfigPipelineReport report)
            {
                var storage = registry.Storage;
                if (storage == null)
                    return;

                ValidateStorageRules(storage, registry, report);
                ValidateStorageBuildings(storage, registry, report);
                ValidateItemStates(storage, registry, report);
                ValidateForbiddenStorageCurrency(storage, report);
            }

            private static void ValidateStorageRules(StorageRegistry storage, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!storage.Source.TryGetTable("StorageRules", out var table))
                    return;

                if (!TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                    return;

                foreach (var row in table.DataRows)
                {
                    var itemKind = row.Get("item_kind");
                    if (!IsBlank(itemKind) && !registry.Items.ItemKinds.Contains(itemKind))
                    {
                        AddIssue(report, storage.Source.DisplayName, "StorageRules", row.RowNumber, "item_kind", itemKind, "item_kind does not exist in Items Configs kind registry.");
                    }
                }

                foreach (var itemKind in registry.Items.ItemKinds)
                {
                    var hasStorageRule = false;
                    foreach (var row in table.DataRows)
                    {
                        if (string.Equals(row.Get("item_kind"), itemKind, StringComparison.OrdinalIgnoreCase))
                        {
                            hasStorageRule = true;
                            break;
                        }
                    }

                    if (!hasStorageRule)
                        AddIssue(report, storage.Source.DisplayName, "StorageRules", 0, "item_kind", itemKind, "Items Configs kind has no StorageRules.item_kind.");
                }
            }

            private static void ValidateStorageBuildings(StorageRegistry storage, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!storage.Source.TryGetTable("StorageBuildings", out var table) ||
                    !HasAnyValue(table, "building_id"))
                {
                    return;
                }

                if (!TryGetRequiredRegistry(report, registry.Buildings, "Buildings Configs"))
                    return;

                foreach (var row in table.DataRows)
                {
                    var buildingId = row.Get("building_id");
                    if (!IsBlank(buildingId) && !registry.Buildings.BuildingIds.Contains(buildingId))
                    {
                        AddIssue(report, storage.Source.DisplayName, "StorageBuildings", row.RowNumber, "building_id", buildingId, "building_id does not exist in Buildings Configs / Index.building_id.");
                        continue;
                    }

                    var level = row.Get("level");
                    if (!IsBlank(buildingId) &&
                        !IsBlank(level) &&
                        !registry.Buildings.ContainsBuildingLevel(buildingId, level))
                    {
                        AddIssue(report, storage.Source.DisplayName, "StorageBuildings", row.RowNumber, "level", level, "building_id + level does not exist in Buildings Configs.");
                    }
                }
            }

            private static void ValidateItemStates(StorageRegistry storage, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!storage.Source.TryGetTable("ItemStates", out var table) ||
                    !HasAnyValue(table, "storage_item_state_name_id"))
                {
                    return;
                }

                if (!TryGetRequiredRegistry(report, registry.Localisation, "Localisation"))
                    return;

                foreach (var row in table.DataRows)
                    ValidateIdSet(report, storage.Source.DisplayName, "ItemStates", row, "storage_item_state_name_id", registry.Localisation.LocalisationIds, "Localisation.id");
            }

            private static void ValidateForbiddenStorageCurrency(StorageRegistry storage, ConfigPipelineReport report)
            {
                foreach (var table in storage.Source.Tables.Values)
                {
                    if (string.Equals(table.Name, "Enums", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(table.Name, "README", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (var row in table.DataRows)
                    {
                        foreach (var column in table.Headers)
                        {
                            var value = row.Get(column);
                            if (string.Equals(value, GoldCurrencyId, StringComparison.OrdinalIgnoreCase))
                                AddIssue(report, storage.Source.DisplayName, table.Name, row.RowNumber, column, value, "gold_id is a currency_id and must not be used as a storage item.");
                        }
                    }
                }
            }
        }

        private static class ItemsCrossChecks
        {
            public static void Validate(ConfigRegistry registry, ConfigPipelineReport report)
            {
                var items = registry.Items;
                if (items == null)
                    return;

                ValidateLocalisation(items, registry, report);
                ValidateRarities(items, registry, report);
                ValidateBuildings(items, registry, report);
                ValidateSkills(items, registry, report);
                ValidateMaterials(items, report);
                ValidateVisibilityAndRecipes(items, report);
                ValidateConsumables(items, registry, report);
                ValidateActionConflicts(items, registry, report);
            }

            private static void ValidateLocalisation(ItemsRegistry items, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!TryGetRequiredRegistry(report, registry.Localisation, "Localisation"))
                    return;

                foreach (var table in ItemTables(items.Source, includeCurrencies: true))
                {
                    foreach (var row in table.DataRows)
                    {
                        ValidateIdSet(report, items.Source.DisplayName, table.Name, row, "name_id", registry.Localisation.LocalisationIds, "Localisation.id");
                        ValidateIdSet(report, items.Source.DisplayName, table.Name, row, "description_id", registry.Localisation.LocalisationIds, "Localisation.id");
                    }
                }
            }

            private static void ValidateRarities(ItemsRegistry items, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!TryGetRequiredRegistry(report, registry.Activity, "Activity Configs / Rarities"))
                    return;

                foreach (var table in ItemTables(items.Source, includeCurrencies: false))
                {
                    foreach (var row in table.DataRows)
                        ValidateIdSet(report, items.Source.DisplayName, table.Name, row, "rarity_id", registry.Activity.RarityIds, "Activity Configs / Rarities.rarity_id");
                }
            }

            private static void ValidateBuildings(ItemsRegistry items, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!TryGetRequiredRegistry(report, registry.Buildings, "Buildings Configs"))
                    return;

                foreach (var table in ItemTables(items.Source, includeCurrencies: false))
                {
                    foreach (var row in table.DataRows)
                    {
                        ValidateIdSet(report, items.Source.DisplayName, table.Name, row, "craft_station_id", registry.Buildings.BuildingIds, "Buildings Configs / Index.building_id");
                        foreach (var packedRef in ParsePackedRefs(row.Get("required_buildings")))
                        {
                            if (!registry.Buildings.ContainsBuildingLevel(packedRef.Id, packedRef.Value))
                                AddIssue(report, items.Source.DisplayName, table.Name, row.RowNumber, "required_buildings", packedRef.Raw, "required_buildings references missing Buildings Configs building_id:level.");
                        }
                    }
                }
            }

            private static void ValidateSkills(ItemsRegistry items, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!TryGetRequiredRegistry(report, registry.Activity, "Activity Configs / Skills"))
                    return;

                foreach (var table in ItemTables(items.Source, includeCurrencies: false))
                {
                    foreach (var row in table.DataRows)
                    {
                        ValidateIdSet(report, items.Source.DisplayName, table.Name, row, "craft_skill_id", registry.Activity.SkillIds, "Activity Configs / Skills.skill_id");
                        foreach (var packedRef in ParsePackedRefs(row.Get("required_skills")))
                        {
                            if (!registry.Activity.SkillIds.Contains(packedRef.Id))
                                AddIssue(report, items.Source.DisplayName, table.Name, row.RowNumber, "required_skills", packedRef.Raw, "required_skills references missing Activity Configs / Skills.skill_id.");
                        }
                    }
                }
            }

            private static void ValidateMaterials(ItemsRegistry items, ConfigPipelineReport report)
            {
                foreach (var table in ItemTables(items.Source, includeCurrencies: false))
                {
                    foreach (var row in table.DataRows)
                    {
                        foreach (var packedRef in ParsePackedRefs(row.Get("materials")))
                        {
                            if (string.Equals(packedRef.Id, GoldCurrencyId, StringComparison.OrdinalIgnoreCase))
                            {
                                AddIssue(report, items.Source.DisplayName, table.Name, row.RowNumber, "materials", packedRef.Id, "gold_id is a currency_id and must not be used as a material item.");
                                continue;
                            }

                            if (!items.ContainsAnyItem(packedRef.Id))
                                AddIssue(report, items.Source.DisplayName, table.Name, row.RowNumber, "materials", packedRef.Id, "materials references missing Items Configs item/resource/recipe/consumable id.");
                        }
                    }
                }
            }

            private static void ValidateVisibilityAndRecipes(ItemsRegistry items, ConfigPipelineReport report)
            {
                foreach (var table in ItemTables(items.Source, includeCurrencies: false))
                {
                    foreach (var row in table.DataRows)
                    {
                        var visibilityItemId = row.Get("visibility_item_id");
                        if (!IsBlank(visibilityItemId) && !items.ContainsAnyItem(visibilityItemId))
                            AddIssue(report, items.Source.DisplayName, table.Name, row.RowNumber, "visibility_item_id", visibilityItemId, "visibility_item_id does not exist in Items Configs item/recipe/consumable registry.");

                        var targetItemId = row.Get("target_item_id");
                        if (!IsBlank(targetItemId) && !items.ContainsAnyItem(targetItemId))
                            AddIssue(report, items.Source.DisplayName, table.Name, row.RowNumber, "target_item_id", targetItemId, "target_item_id does not exist in Items Configs item registry.");
                    }
                }
            }

            private static void ValidateConsumables(ItemsRegistry items, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!items.Source.TryGetTable(ConsumablesSheet, out var table))
                    return;

                foreach (var row in table.DataRows)
                {
                    var useCondition = row.Get("use_condition");
                    var activityId = ExtractValueAfterPrefix(useCondition, "activity_id=");
                    if (!IsBlank(activityId))
                    {
                        if (TryGetRequiredRegistry(report, registry.Activity, "Activity Configs") &&
                            !registry.Activity.ActivityIds.Contains(activityId))
                        {
                            AddIssue(report, items.Source.DisplayName, table.Name, row.RowNumber, "use_condition", activityId, "use_condition activity_id does not exist in Activity Configs / Activities.id.");
                        }
                    }

                    foreach (var effect in SplitSemicolon(row.Get("effects")))
                    {
                        var resourceId = ExtractModifyRewardResource(effect);
                        if (!IsBlank(resourceId) && !items.ResourceIds.Contains(resourceId))
                            AddIssue(report, items.Source.DisplayName, table.Name, row.RowNumber, "effects", resourceId, "effects references missing Items Configs / Ресурсы.id.");
                    }
                }
            }

            private static void ValidateActionConflicts(ItemsRegistry items, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (registry.Activity != null)
                {
                    foreach (var actionId in items.ItemActionIds)
                    {
                        if (registry.Activity.ActivityIds.Contains(actionId))
                            AddIssue(report, items.Source.DisplayName, "itemActions", 0, "id", actionId, "Generated item action id conflicts with Activity Configs / Activities.id.");
                    }
                }

                if (registry.Buildings != null)
                {
                    foreach (var actionId in items.ItemActionIds)
                    {
                        if (registry.Buildings.BuildActionIds.Contains(actionId))
                            AddIssue(report, items.Source.DisplayName, "itemActions", 0, "id", actionId, "Generated item action id conflicts with Buildings Configs build action id.");
                    }
                }
            }
        }

        private static class FormulaCrossChecks
        {
            public static void Validate(ConfigRegistry registry, ConfigPipelineReport report)
            {
                var formula = registry.Formula;
                if (formula == null)
                    return;

                if (formula.Source.TryGetTable("SkillStatWeights", out var weights) &&
                    HasAnyValue(weights, "skill_id"))
                {
                    if (TryGetRequiredRegistry(report, registry.Activity, "Activity Configs / Skills"))
                    {
                        foreach (var row in weights.DataRows)
                            ValidateIdSet(report, formula.Source.DisplayName, "SkillStatWeights", row, "skill_id", registry.Activity.SkillIds, "Activity Configs / Skills.skill_id");
                    }
                }

                if (formula.Source.TryGetTable("HeroDerivedStats", out var stats))
                {
                    foreach (var row in stats.DataRows)
                    {
                        ValidateOptionalIdSet(report, formula.Source.DisplayName, "HeroDerivedStats", row, "primary_stat", HeroStatIds, "known hero stat ids");
                        ValidateOptionalIdSet(report, formula.Source.DisplayName, "HeroDerivedStats", row, "secondary_stat", HeroStatIds, "known hero stat ids");
                    }
                }
            }
        }

        private static class LootCrossChecks
        {
            public static void Validate(ConfigRegistry registry, ConfigPipelineReport report)
            {
                var loot = registry.Loot;
                if (loot == null ||
                    !loot.Source.TryGetTable("LootTableEntries", out var table) ||
                    !HasAnyValue(table, "target_id"))
                {
                    return;
                }

                if (!TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                    return;

                foreach (var row in table.DataRows)
                {
                    var targetId = row.Get("target_id");
                    if (IsBlank(targetId))
                        continue;

                    if (string.Equals(row.Get("drop_type"), "Resource", StringComparison.OrdinalIgnoreCase))
                    {
                        ValidateItemTarget(report, loot.Source.DisplayName, "LootTableEntries", row, "target_id", registry.Items, ItemTargetKind.Resource, "Items Configs / Ресурсы.id");
                        continue;
                    }

                    if (string.Equals(row.Get("drop_type"), "Gold", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(row.Get("drop_type"), "Currency", StringComparison.OrdinalIgnoreCase))
                    {
                        ValidateIdSet(report, loot.Source.DisplayName, "LootTableEntries", row, "target_id", registry.Items.CurrencyIds, "Items Configs / Валюты.currency_id");
                        continue;
                    }

                    ValidateItemTarget(report, loot.Source.DisplayName, "LootTableEntries", row, "target_id", registry.Items, ItemTargetKind.AnyItem, "Items Configs item/recipe/consumable registry");
                }
            }
        }

        private static class BuildingsCrossChecks
        {
            public static void Validate(ConfigRegistry registry, ConfigPipelineReport report)
            {
                var buildings = registry.Buildings;
                if (buildings == null)
                    return;

                ValidateLocalisation(buildings, registry, report);
                ValidateBuildingLevelRows(buildings, registry, report);
                ValidateCraftables(buildings, registry, report);
                ValidateActionConflicts(buildings, registry, report);
            }

            private static void ValidateLocalisation(BuildingsRegistry buildings, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!buildings.Source.TryGetTable("Index", out var index))
                    return;

                if (!TryGetRequiredRegistry(report, registry.Localisation, "Localisation"))
                    return;

                foreach (var row in index.DataRows)
                {
                    ValidateIdSet(report, buildings.Source.DisplayName, "Index", row, "name_id", registry.Localisation.LocalisationIds, "Localisation.id");
                    ValidateIdSet(report, buildings.Source.DisplayName, "Index", row, "description_id", registry.Localisation.LocalisationIds, "Localisation.id");
                }
            }

            private static void ValidateBuildingLevelRows(BuildingsRegistry buildings, ConfigRegistry registry, ConfigPipelineReport report)
            {
                foreach (var table in buildings.Source.Tables.Values)
                {
                    if (string.Equals(table.Name, "Index", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(table.Name, "README", StringComparison.OrdinalIgnoreCase) ||
                        table.Name.StartsWith("Craftables -", StringComparison.OrdinalIgnoreCase) ||
                        !table.HasColumn("source_activity_id"))
                    {
                        continue;
                    }

                    foreach (var row in table.DataRows)
                    {
                        ValidateSourceActivity(buildings, registry, report, table, row);
                        ValidateMaterials(buildings, registry, report, table, row);
                        ValidateRequirementActivities(buildings, registry, report, table, row);
                        ValidateRequirementSkills(buildings, registry, report, table, row);
                    }
                }
            }

            private static void ValidateSourceActivity(BuildingsRegistry buildings, ConfigRegistry registry, ConfigPipelineReport report, ConfigSheetTable table, ConfigSheetDataRow row)
            {
                var sourceActivityId = row.Get("source_activity_id");
                if (IsBlank(sourceActivityId))
                    return;

                if (IsBuildActionId(sourceActivityId))
                    return;

                if (!TryGetRequiredRegistry(report, registry.Activity, "Activity Configs"))
                    return;

                if (!registry.Activity.ActivityIds.Contains(sourceActivityId) &&
                    (registry.Items == null || !registry.Items.ItemActionIds.Contains(sourceActivityId)))
                {
                    AddIssue(report, buildings.Source.DisplayName, table.Name, row.RowNumber, "source_activity_id", sourceActivityId, "source_activity_id does not exist in Activity Configs / Activities.id or unified action registry.");
                }
            }

            private static void ValidateMaterials(BuildingsRegistry buildings, ConfigRegistry registry, ConfigPipelineReport report, ConfigSheetTable table, ConfigSheetDataRow row)
            {
                if (!HasAnyValue(table, "materials"))
                    return;

                if (!TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                    return;

                foreach (var packedRef in ParsePackedRefs(row.Get("materials")))
                {
                    if (string.Equals(packedRef.Id, GoldCurrencyId, StringComparison.OrdinalIgnoreCase))
                    {
                        AddIssue(report, buildings.Source.DisplayName, table.Name, row.RowNumber, "materials", packedRef.Id, "gold_id is a currency_id and must not be used as a material item.");
                        continue;
                    }

                    if (!registry.Items.ContainsAnyItem(packedRef.Id))
                        AddIssue(report, buildings.Source.DisplayName, table.Name, row.RowNumber, "materials", packedRef.Id, "materials references missing Items Configs item/resource/recipe/consumable id.");
                }
            }

            private static void ValidateRequirementActivities(BuildingsRegistry buildings, ConfigRegistry registry, ConfigPipelineReport report, ConfigSheetTable table, ConfigSheetDataRow row)
            {
                if (!HasAnyValue(table, "requirements_activities"))
                    return;

                if (!TryGetRequiredRegistry(report, registry.Activity, "Activity Configs"))
                    return;

                foreach (var packedRef in ParsePackedRefs(row.Get("requirements_activities")))
                {
                    var exists = registry.Activity.ActivityIds.Contains(packedRef.Id) ||
                                 buildings.BuildActionIds.Contains(packedRef.Id) ||
                                 (registry.Items != null && registry.Items.ItemActionIds.Contains(packedRef.Id));
                    if (!exists)
                        AddIssue(report, buildings.Source.DisplayName, table.Name, row.RowNumber, "requirements_activities", packedRef.Raw, "requirements_activities references missing Activity Configs / Activities.id or generated action id.");
                }
            }

            private static void ValidateRequirementSkills(BuildingsRegistry buildings, ConfigRegistry registry, ConfigPipelineReport report, ConfigSheetTable table, ConfigSheetDataRow row)
            {
                if (!HasAnyValue(table, "requirements_skills") && !HasAnyValue(table, "craft_skill_id"))
                    return;

                if (!TryGetRequiredRegistry(report, registry.Activity, "Activity Configs / Skills"))
                    return;

                foreach (var packedRef in ParsePackedRefs(row.Get("requirements_skills")))
                {
                    if (!registry.Activity.SkillIds.Contains(packedRef.Id))
                        AddIssue(report, buildings.Source.DisplayName, table.Name, row.RowNumber, "requirements_skills", packedRef.Raw, "requirements_skills references missing Activity Configs / Skills.skill_id.");
                }

                ValidateIdSet(report, buildings.Source.DisplayName, table.Name, row, "craft_skill_id", registry.Activity.SkillIds, "Activity Configs / Skills.skill_id");
            }

            private static void ValidateCraftables(BuildingsRegistry buildings, ConfigRegistry registry, ConfigPipelineReport report)
            {
                var hasCraftables = false;
                foreach (var table in buildings.Source.Tables.Values)
                    hasCraftables |= table.Name.StartsWith("Craftables -", StringComparison.OrdinalIgnoreCase);

                if (!hasCraftables)
                    return;

                if (!TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                    return;

                foreach (var table in buildings.Source.Tables.Values)
                {
                    if (!table.Name.StartsWith("Craftables -", StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (var row in table.DataRows)
                    {
                        var itemId = row.Get("item_id");
                        if (string.Equals(itemId, GoldCurrencyId, StringComparison.OrdinalIgnoreCase))
                        {
                            AddIssue(report, buildings.Source.DisplayName, table.Name, row.RowNumber, "item_id", itemId, "gold_id is a currency_id and must not be used as a BuildingCraftables item.");
                            continue;
                        }

                        if (!IsBlank(itemId) && !registry.Items.ContainsAnyItem(itemId))
                            AddIssue(report, buildings.Source.DisplayName, table.Name, row.RowNumber, "item_id", itemId, "item_id does not exist in Items Configs item/resource/recipe/consumable registry.");
                    }
                }
            }

            private static void ValidateActionConflicts(BuildingsRegistry buildings, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (registry.Activity != null)
                {
                    foreach (var actionId in buildings.BuildActionIds)
                    {
                        if (registry.Activity.ActivityIds.Contains(actionId))
                            AddIssue(report, buildings.Source.DisplayName, "buildActions", 0, "id", actionId, "Generated Build action id conflicts with Activity Configs / Activities.id.");
                    }
                }

                if (registry.Items != null)
                {
                    foreach (var actionId in buildings.BuildActionIds)
                    {
                        if (registry.Items.ItemActionIds.Contains(actionId))
                            AddIssue(report, buildings.Source.DisplayName, "buildActions", 0, "id", actionId, "Generated Build action id conflicts with Items Configs itemActions.");
                    }
                }
            }
        }

        private static class MapCrossChecks
        {
            public static void Validate(ConfigRegistry registry, ConfigPipelineReport report)
            {
                var map = registry.Map;
                if (map == null)
                    return;

                ValidateDangerEncounters(map, registry, report);
            }

            private static void ValidateDangerEncounters(MapRegistry map, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!map.Source.TryGetTable("DangerEncounters", out var table))
                    return;

                if (HasAnyValue(table, "activity_id") &&
                    TryGetRequiredRegistry(report, registry.Activity, "Activity Configs"))
                {
                    foreach (var row in table.DataRows)
                        ValidateIdSet(report, map.Source.DisplayName, "DangerEncounters", row, "activity_id", registry.Activity.ActivityIds, "Activity Configs / Activities.id");
                }

                if (HasAnyValue(table, "enemy_group_id") &&
                    TryGetRequiredRegistry(report, registry.Enemies, "Enemies Configs"))
                {
                    foreach (var row in table.DataRows)
                        ValidateIdSet(report, map.Source.DisplayName, "DangerEncounters", row, "enemy_group_id", registry.Enemies.EnemyGroupIds, "Enemies Configs / EnemyGroups.enemy_group_id");
                }
            }
        }

        private enum ItemTargetKind
        {
            AnyItem,
            Resource,
            Equipment,
            Recipe,
            Consumable
        }

        private static void ValidateIdSet(
            ConfigPipelineReport report,
            string sourceConfig,
            string sheet,
            ConfigSheetDataRow row,
            string column,
            HashSet<string> ids,
            string expectedRegistry)
        {
            if (row == null || ids == null)
                return;

            var value = row.Get(column);
            if (IsBlank(value) || ids.Contains(value))
                return;

            AddIssue(report, sourceConfig, sheet, row.RowNumber, column, value, $"Referenced id does not exist in {expectedRegistry}.");
        }

        private static void ValidateOptionalIdSet(
            ConfigPipelineReport report,
            string sourceConfig,
            string sheet,
            ConfigSheetDataRow row,
            string column,
            HashSet<string> ids,
            string expectedRegistry)
        {
            ValidateIdSet(report, sourceConfig, sheet, row, column, ids, expectedRegistry);
        }

        private static void ValidateMapAccess(ConfigPipelineReport report, string sourceConfig, string sheet, ConfigSheetDataRow row, MapRegistry map)
        {
            var targetId = row.Get("target_id");
            if (IsBlank(targetId) || map.LocationIds.Contains(targetId) || map.CellIds.Contains(targetId))
                return;

            AddIssue(report, sourceConfig, sheet, row.RowNumber, "target_id", targetId, "Referenced id does not exist in Map Configs / MapLocations.location_id or MapCells.cell_id.");
        }

        private static void ValidateItemTarget(
            ConfigPipelineReport report,
            string sourceConfig,
            string sheet,
            ConfigSheetDataRow row,
            string column,
            ItemsRegistry items,
            ItemTargetKind kind,
            string expectedRegistry)
        {
            var id = row.Get(column);
            if (IsBlank(id))
                return;

            if (string.Equals(id, GoldCurrencyId, StringComparison.OrdinalIgnoreCase))
            {
                AddIssue(report, sourceConfig, sheet, row.RowNumber, column, id, "gold_id is a currency_id and must be resolved through a Currency/Gold type.");
                return;
            }

            var exists = false;
            switch (kind)
            {
                case ItemTargetKind.Resource:
                    exists = items.ContainsResource(id);
                    break;
                case ItemTargetKind.Equipment:
                    exists = items.ContainsEquipment(id);
                    break;
                case ItemTargetKind.Recipe:
                    exists = items.ContainsRecipe(id);
                    break;
                case ItemTargetKind.Consumable:
                    exists = items.ContainsConsumable(id);
                    break;
                default:
                    exists = items.ContainsAnyItem(id);
                    break;
            }

            if (!exists)
                AddIssue(report, sourceConfig, sheet, row.RowNumber, column, id, $"Referenced id does not exist in {expectedRegistry}.");
        }

        private static void ValidateLootLikeItemReference(
            ConfigPipelineReport report,
            string sourceConfig,
            string sheet,
            ConfigSheetDataRow row,
            string column,
            ItemsRegistry items)
        {
            var id = row.Get(column);
            if (IsBlank(id))
                return;

            if (string.Equals(id, GoldCurrencyId, StringComparison.OrdinalIgnoreCase))
            {
                if (!items.ContainsCurrency(id))
                    AddIssue(report, sourceConfig, sheet, row.RowNumber, column, id, "gold_id does not exist in Items Configs / Валюты.currency_id.");
                return;
            }

            if (items.ContainsAnyItem(id) || items.ContainsCurrency(id))
                return;

            AddIssue(report, sourceConfig, sheet, row.RowNumber, column, id, "loot_id does not exist in Items Configs item/resource/recipe/consumable registry or currency registry.");
            return;

        }

        private static IEnumerable<ConfigSheetTable> ItemTables(LoadedConfig source, bool includeCurrencies)
        {
            foreach (var sheetName in new[] { ResourcesSheet, EquipmentWeaponsSheet, EquipmentArmorSheet, RecipesSheet, ConsumablesSheet })
            {
                if (source.TryGetTable(sheetName, out var table))
                    yield return table;
            }

            if (includeCurrencies && source.TryGetTable(CurrenciesSheet, out var currencies))
                yield return currencies;
        }

        private static IEnumerable<string> SplitSemicolon(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                yield break;

            foreach (var part in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var value = part.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value;
            }
        }

        private static string ExtractValueAfterPrefix(string raw, string prefix)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var index = raw.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return string.Empty;

            var start = index + prefix.Length;
            var end = raw.IndexOfAny(new[] { ';', ' ', '\t', '\r', '\n' }, start);
            return end < 0 ? raw.Substring(start).Trim() : raw.Substring(start, end - start).Trim();
        }

        private static string ExtractModifyRewardResource(string effect)
        {
            if (string.IsNullOrWhiteSpace(effect) ||
                effect.IndexOf("ModifyReward", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return string.Empty;
            }

            foreach (var token in effect.Split(new[] { ' ', ':', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var value = token.Trim();
                if (value.StartsWith("resource_", StringComparison.OrdinalIgnoreCase))
                    return value;
            }

            return string.Empty;
        }
    }
}
