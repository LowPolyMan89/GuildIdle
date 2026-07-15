using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GuildIdle.Core;
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

        private const string ResourcesSheet = "Ресурсы";
        private const string EquipmentWeaponsSheet = "Снаряжение - оружие";
        private const string EquipmentArmorSheet = "Снаряжение - броня";
        private const string RecipesSheet = "Рецепты";
        private const string ConsumablesSheet = "Расходники";
        private const string CraftDefinitionsSheet = "CraftDefinitions";
        private const string CurrenciesSheet = "Валюты";

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

        private static bool IsTrue(string value)
        {
            return string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDisabled(string value)
        {
            return string.Equals(value, "FALSE", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "0", StringComparison.OrdinalIgnoreCase);
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

        private static bool HasAnyValue(LoadedConfig source, string sheetName, params string[] columns)
        {
            if (source == null || !source.TryGetTable(sheetName, out var table))
                return false;

            foreach (var column in columns)
            {
                if (HasAnyValue(table, column))
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

        private static IEnumerable<PackedRef> ParseActivityRequirementRefs(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                yield break;

            var refs = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var activityRef in refs)
            {
                var trimmed = activityRef.Trim();
                var parts = trimmed.Split(':');
                if (parts.Length < 1 || parts.Length > 2)
                    continue;

                var id = parts[0].Trim();
                if (!string.IsNullOrWhiteSpace(id))
                    yield return new PackedRef(id, parts.Length == 2 ? parts[1].Trim() : "1", trimmed);
            }
        }

        private static bool IsBuildActionId(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.StartsWith("build_", StringComparison.OrdinalIgnoreCase);
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
            public IReadOnlyCollection<ConfigDownloadedSheet> RawSheets => _rawSheets.Values;

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
            public HashSet<string> QuestIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> EnabledQuestIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                registry.CollectQuestIds();
                return registry;
            }

            private void CollectQuestIds()
            {
                if (!Source.TryGetTable("Quests", out var quests))
                    return;

                foreach (var row in quests.DataRows)
                {
                    var questId = row.Get("quest_id");
                    if (IsBlank(questId))
                        continue;

                    QuestIds.Add(questId);
                    if (IsTrue(row.Get("enabled")))
                        EnabledQuestIds.Add(questId);
                }
            }
        }

        private sealed class EnemiesRegistry
        {
            public LoadedConfig Source { get; }
            public HashSet<string> EnemyIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> EnemyLevelIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                CollectIds(source, "Enemies", "enemy_id", registry.EnemyIds);
                CollectIds(source, "EnemyLevels", "level", registry.EnemyLevelIds);
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
            public HashSet<string> EnabledRecipeIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> ConsumableIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> CurrencyIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> ItemKinds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> EnabledCraftDefinitionIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> EnabledItemIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                CollectItemSheet(source, RecipesSheet, registry.EnabledRecipeIds, registry);
                CollectItemSheet(source, ConsumablesSheet, registry.ConsumableIds, registry);
                CollectDeclaredItemKinds(source, registry);
                CollectEnabledIds(source, CraftDefinitionsSheet, "craft_id", registry.EnabledCraftDefinitionIds);
                CollectIds(source, CurrenciesSheet, "currency_id", registry.CurrencyIds);
                CollectIds(source, CurrenciesSheet, "currencyId", registry.CurrencyIds);
                CollectRuntimeItems(source, registry);
                return registry;
            }

            public bool ContainsAnyItem(string id)
            {
                return EnabledItemIds.Contains(id);
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
                return EnabledRecipeIds.Contains(id);
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
            public RuntimeCraftDefinition[] craftDefinitions;
            public RuntimeCurrency[] currencies;
        }

        [Serializable]
        private sealed class RuntimeItem
        {
            public string id;
            public string kind;
        }

        [Serializable]
        private sealed class RuntimeCurrency
        {
            public string currencyId;
        }

        [Serializable]
        private sealed class RuntimeCraftDefinition
        {
            public string craftId;
        }

        private sealed class FormulaRegistry
        {
            public LoadedConfig Source { get; }
            public HashSet<string> ProfileIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> FormulaIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                CollectIds(source, "HeroDerivedStats", "formula_id", registry.FormulaIds);
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
            public Dictionary<string, HashSet<long>> BuildingLevels { get; } = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> BuildingIdsBySheetName { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public List<ConfigSheetTable> BuildingLevelTables { get; } = new List<ConfigSheetTable>();
            public HashSet<string> BuildActionIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> StageIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> EnabledStageIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

                foreach (var sheet in source.RawSheets)
                    registry.CollectBuildingSheet(sheet);

                registry.CollectStageIds();

                return registry;
            }

            private void CollectStageIds()
            {
                if (!Source.TryGetTable("SettlementStages", out var stages))
                    return;

                foreach (var row in stages.DataRows)
                {
                    var stageId = row.Get("stage_id");
                    if (IsBlank(stageId))
                        continue;

                    StageIds.Add(stageId);
                    if (IsTrue(row.Get("enabled")))
                        EnabledStageIds.Add(stageId);
                }
            }

            public bool ContainsBuildingLevel(string buildingId, string levelText)
            {
                if (!BuildingIds.Contains(buildingId))
                    return false;

                if (!long.TryParse(levelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
                    return false;

                if (BuildingLevels.TryGetValue(buildingId, out var configuredLevels) && configuredLevels.Count > 0)
                    return configuredLevels.Contains(level);

                if (!BuildingMaxLevels.TryGetValue(buildingId, out var maxLevel))
                    return level >= 0;

                return level >= 0 && level <= maxLevel;
            }

            private void CollectBuildingSheet(ConfigDownloadedSheet sheet)
            {
                if (sheet == null ||
                    string.IsNullOrWhiteSpace(sheet.sheet_name) ||
                    string.Equals(sheet.sheet_name, "Index", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sheet.sheet_name, "README", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sheet.sheet_name, "BuildingActivities", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sheet.sheet_name, "SettlementStages", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sheet.sheet_name, "SettlementStageSlots", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sheet.sheet_name, "SettlementStageObjectives", StringComparison.OrdinalIgnoreCase) ||
                    sheet.sheet_name.StartsWith("Craftables -", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var rows = sheet.rows ?? Array.Empty<ConfigSheetRow>();
                var buildingId = FindTopBlockValue(rows, "building_id");
                if (IsBlank(buildingId))
                    return;

                BuildingIdsBySheetName[sheet.sheet_name] = buildingId;

                var headerRow = FindHeaderRow(rows, "level");
                if (headerRow < 0)
                    return;

                var levelRows = new ConfigSheetRow[rows.Length - headerRow];
                Array.Copy(rows, headerRow, levelRows, 0, levelRows.Length);
                BuildingLevelTables.Add(new ConfigSheetTable(
                    new ConfigDownloadedSheet
                    {
                        sheet_name = sheet.sheet_name,
                        rows = levelRows
                    },
                    headerRow));

                var levelColumn = FindColumn(rows[headerRow], "level");
                var sourceActivityColumn = FindColumn(rows[headerRow], "source_activity_id");
                if (levelColumn < 0)
                    return;

                if (!BuildingLevels.TryGetValue(buildingId, out var levels))
                {
                    levels = new HashSet<long>();
                    BuildingLevels[buildingId] = levels;
                }

                for (var rowIndex = headerRow + 1; rowIndex < rows.Length; rowIndex++)
                {
                    var levelText = Cell(rows[rowIndex], levelColumn);
                    if (long.TryParse(levelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
                        levels.Add(level);

                    var sourceActivityId = Cell(rows[rowIndex], sourceActivityColumn);
                    if (IsBuildActionId(sourceActivityId))
                        BuildActionIds.Add(sourceActivityId);
                }
            }

            private static string FindTopBlockValue(ConfigSheetRow[] rows, string key)
            {
                foreach (var row in rows)
                {
                    if (string.Equals(Cell(row, 0), key, StringComparison.OrdinalIgnoreCase))
                        return Cell(row, 1);
                }

                return string.Empty;
            }

            private static int FindHeaderRow(ConfigSheetRow[] rows, string requiredColumn)
            {
                for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
                {
                    if (FindColumn(rows[rowIndex], requiredColumn) >= 0)
                        return rowIndex;
                }

                return -1;
            }

            private static int FindColumn(ConfigSheetRow row, string column)
            {
                var cells = row?.cells ?? Array.Empty<string>();
                for (var index = 0; index < cells.Length; index++)
                {
                    if (string.Equals((cells[index] ?? string.Empty).Trim(), column, StringComparison.OrdinalIgnoreCase))
                        return index;
                }

                return -1;
            }

            private static string Cell(ConfigSheetRow row, int column)
            {
                if (row?.cells == null || column < 0 || column >= row.cells.Length)
                    return string.Empty;

                return (row.cells[column] ?? string.Empty).Trim();
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
                var kind = row.Get("kind");
                if (!IsBlank(kind))
                    registry.ItemKinds.Add(kind);

                if (table.HasColumn("enabled") && !IsTrue(row.Get("enabled")))
                    continue;

                var id = row.Get("id");
                if (!IsBlank(id))
                {
                    ids.Add(id);
                    registry.EnabledItemIds.Add(id);
                }
            }
        }

        private static void CollectEnabledIds(LoadedConfig source, string sheetName, string column, HashSet<string> ids)
        {
            if (!source.TryGetTable(sheetName, out var table) || !table.HasColumn(column))
                return;

            foreach (var row in table.DataRows)
            {
                if (table.HasColumn("enabled") && !IsTrue(row.Get("enabled")))
                    continue;

                var id = row.Get(column);
                if (!IsBlank(id))
                    ids.Add(id);
            }
        }

        private static void CollectDeclaredItemKinds(LoadedConfig source, ItemsRegistry registry)
        {
            if (source.TryGetTable(ResourcesSheet, out _))
                registry.ItemKinds.Add("resource");
            if (source.TryGetTable(EquipmentWeaponsSheet, out _) || source.TryGetTable(EquipmentArmorSheet, out _))
                registry.ItemKinds.Add("equipment");
            if (source.TryGetTable(RecipesSheet, out _))
                registry.ItemKinds.Add("recipe");
            if (source.TryGetTable(ConsumablesSheet, out _))
                registry.ItemKinds.Add("consumable");
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

            if (!source.TryGetTable(ResourcesSheet, out _))
                CollectRuntimeItemIds(runtime.resources, registry.ResourceIds, registry);
            if (!source.TryGetTable(EquipmentWeaponsSheet, out _))
                CollectRuntimeItemIds(runtime.equipmentWeapons, registry.EquipmentIds, registry);
            if (!source.TryGetTable(EquipmentArmorSheet, out _))
                CollectRuntimeItemIds(runtime.equipmentArmor, registry.EquipmentIds, registry);
            if (!source.TryGetTable(RecipesSheet, out _))
                CollectRuntimeItemIds(runtime.recipes, registry.EnabledRecipeIds, registry);
            if (!source.TryGetTable(ConsumablesSheet, out _))
                CollectRuntimeItemIds(runtime.consumables, registry.ConsumableIds, registry);
            if (!source.TryGetTable(CraftDefinitionsSheet, out _))
                CollectRuntimeCraftDefinitionIds(runtime.craftDefinitions, registry.EnabledCraftDefinitionIds);
            if (!source.TryGetTable(CurrenciesSheet, out _))
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
                    registry.EnabledItemIds.Add(row.id);
                }

                if (!IsBlank(row.kind))
                    registry.ItemKinds.Add(row.kind);

            }
        }

        private static void CollectRuntimeCraftDefinitionIds(RuntimeCraftDefinition[] rows, HashSet<string> ids)
        {
            foreach (var row in rows ?? Array.Empty<RuntimeCraftDefinition>())
            {
                if (row != null && !IsBlank(row.craftId))
                    ids.Add(row.craftId);
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
                if (!HasAnyValue(registry.Heroes.Source, "Heroes", "NameId", "DescriptionId") &&
                    !HasAnyValue(registry.Heroes.Source, "HeroUniqueSkills", "NameId", "DescriptionId"))
                {
                    return;
                }

                if (!TryGetRequiredRegistry(report, registry.Localisation, "Localisation"))
                    return;

                foreach (var sheetName in new[] { "Heroes", "HeroUniqueSkills" })
                {
                    if (!registry.Heroes.Source.TryGetTable(sheetName, out var table))
                        continue;

                    foreach (var row in table.DataRows)
                    {
                        ValidateIdSet(report, registry.Heroes.Source.DisplayName, sheetName, row, "NameId", registry.Localisation.LocalisationIds, "Localisation.id");
                        ValidateIdSet(report, registry.Heroes.Source.DisplayName, sheetName, row, "DescriptionId", registry.Localisation.LocalisationIds, "Localisation.id");
                    }
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
                ValidateDangerEncounters(activity, registry, report);
                ValidateLocalisation(activity, registry, report);
                ValidateQuests(activity, registry, report);
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

            private static void ValidateLocalisation(ActivityRegistry activity, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!HasAnyValue(activity.Source, "Activities", "name_id", "description_id") &&
                    !HasAnyValue(activity.Source, "Rarities", "name_id", "description_id") &&
                    !HasAnyValue(activity.Source, "Skills", "skill_name_id", "skill_description_id"))
                {
                    return;
                }

                if (!TryGetRequiredRegistry(report, registry.Localisation, "Localisation"))
                    return;

                foreach (var sheetName in new[] { "Activities", "Rarities", "Skills" })
                {
                    if (!activity.Source.TryGetTable(sheetName, out var table))
                        continue;

                    foreach (var row in table.DataRows)
                    {
                        if (string.Equals(sheetName, "Skills", StringComparison.OrdinalIgnoreCase))
                        {
                            ValidateIdSet(report, activity.Source.DisplayName, sheetName, row, "skill_name_id", registry.Localisation.LocalisationIds, "Localisation.id");
                            ValidateIdSet(report, activity.Source.DisplayName, sheetName, row, "skill_description_id", registry.Localisation.LocalisationIds, "Localisation.id");
                        }
                        else
                        {
                            ValidateIdSet(report, activity.Source.DisplayName, sheetName, row, "name_id", registry.Localisation.LocalisationIds, "Localisation.id");
                            ValidateIdSet(report, activity.Source.DisplayName, sheetName, row, "description_id", registry.Localisation.LocalisationIds, "Localisation.id");
                        }
                    }
                }
            }

            private static void ValidateDangerEncounters(ActivityRegistry activity, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!activity.Source.TryGetTable("DangerEncounters", out var table))
                    return;

                foreach (var row in table.DataRows)
                {
                    if (TryGetRequiredRegistry(report, registry.Activity, "Activity Configs"))
                        ValidateIdSet(report, activity.Source.DisplayName, "DangerEncounters", row, "activity_id", activity.ActivityIds, "Activity Configs / Activities.id");

                    if (TryGetRequiredRegistry(report, registry.Enemies, "Enemies Configs"))
                        ValidateIdSet(report, activity.Source.DisplayName, "DangerEncounters", row, "enemy_group_id", registry.Enemies.EnemyGroupIds, "Enemies Configs / EnemyGroups.enemy_group_id");

                    if (TryGetRequiredRegistry(report, registry.Formula, "Formula Configs"))
                        ValidateIdSet(report, activity.Source.DisplayName, "DangerEncounters", row, "risk_formula_id", registry.Formula.FormulaIds, "Formula Configs / HeroDerivedStats.formula_id");
                }
            }

            private static void ValidateRequirements(ActivityRegistry activity, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!activity.Source.TryGetTable("ActivityRequirements", out var table))
                    return;

                foreach (var row in table.DataRows)
                {
                    var requirementTypeRaw = row.Get("req_type");
                    if (!ActivityTypeParser.TryParseRequirementType(requirementTypeRaw, out var requirementType))
                    {
                        AddIssue(report, activity.Source.DisplayName, "ActivityRequirements", row.RowNumber, "req_type", requirementTypeRaw, $"Unknown req_type '{requirementTypeRaw}'.");
                        continue;
                    }

                    if (requirementType == RequirementTypeEnum.HeroLevel ||
                        requirementType == RequirementTypeEnum.HeroClass ||
                        requirementType == RequirementTypeEnum.ItemEquipped ||
                        requirementType == RequirementTypeEnum.QuestCompleted)
                    {
                        AddIssue(report, activity.Source.DisplayName, "ActivityRequirements", row.RowNumber, "req_type", requirementTypeRaw, $"Requirement type '{requirementTypeRaw}' is recognized but not supported by activity runtime.");
                        continue;
                    }

                    var targetId = row.Get("target_id");
                    if (IsBlank(targetId))
                        continue;

                    switch (requirementType)
                    {
                        case RequirementTypeEnum.SkillLevel:
                            ValidateIdSet(report, activity.Source.DisplayName, "ActivityRequirements", row, "target_id", activity.SkillIds, "Activity Configs / Skills.skill_id");
                            break;
                        case RequirementTypeEnum.Resource:
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateItemTarget(report, activity.Source.DisplayName, "ActivityRequirements", row, "target_id", registry.Items, ItemTargetKind.Resource, "Items Configs / Ресурсы.id");
                            break;
                        case RequirementTypeEnum.LocationUnlocked:
                            if (TryGetRequiredRegistry(report, registry.Map, "Map Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRequirements", row, "target_id", registry.Map.LocationIds, "Map Configs / MapLocations.location_id");
                            break;
                        case RequirementTypeEnum.BuildingLevel:
                        case RequirementTypeEnum.Building:
                            if (TryGetRequiredRegistry(report, registry.Buildings, "Buildings Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRequirements", row, "target_id", registry.Buildings.BuildingIds, "Buildings Configs / Index.building_id");
                            break;
                        case RequirementTypeEnum.ItemCount:
                        case RequirementTypeEnum.Item:
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateItemTarget(report, activity.Source.DisplayName, "ActivityRequirements", row, "target_id", registry.Items, ItemTargetKind.AnyItem, "Items Configs item/resource/recipe/consumable registry");
                            break;
                        case RequirementTypeEnum.Currency:
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRequirements", row, "target_id", registry.Items.CurrencyIds, "Items Configs / Валюты.currency_id");
                            break;
                        case RequirementTypeEnum.ActivityCompleted:
                            ValidateIdSet(report, activity.Source.DisplayName, "ActivityRequirements", row, "target_id", activity.ActivityIds, "Activity Configs / Activities.id");
                            break;
                        case RequirementTypeEnum.HeroAvailable:
                            if (TryGetRequiredRegistry(report, registry.Heroes, "Heroes Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRequirements", row, "target_id", registry.Heroes.HeroIds, "Heroes Configs / Heroes.HeroId");
                            break;
                        default:
                            AddIssue(report, activity.Source.DisplayName, "ActivityRequirements", row.RowNumber, "req_type", requirementTypeRaw, $"Requirement type '{requirementTypeRaw}' is not supported by activity runtime.");
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
                    var rewardTypeRaw = row.Get("reward_type");
                    if (!ActivityTypeParser.TryParseRewardType(rewardTypeRaw, out var rewardType))
                    {
                        AddIssue(report, activity.Source.DisplayName, "ActivityRewards", row.RowNumber, "reward_type", rewardTypeRaw, $"Unknown reward_type '{rewardTypeRaw}'.");
                        continue;
                    }

                    if (rewardType == RewardTypeEnum.HeroExp || rewardType == RewardTypeEnum.Reputation)
                    {
                        AddIssue(report, activity.Source.DisplayName, "ActivityRewards", row.RowNumber, "reward_type", rewardTypeRaw, $"Reward type '{rewardTypeRaw}' is recognized but not supported by activity runtime.");
                        continue;
                    }

                    var targetId = row.Get("target_id");
                    if (IsBlank(targetId) && rewardType != RewardTypeEnum.Gold)
                        continue;

                    switch (rewardType)
                    {
                        case RewardTypeEnum.Resource:
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateItemTarget(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Items, ItemTargetKind.Resource, "Items Configs / Ресурсы.id");
                            break;
                        case RewardTypeEnum.Item:
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateItemTarget(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Items, ItemTargetKind.AnyItem, "Items Configs item/recipe/consumable registry");
                            break;
                        case RewardTypeEnum.Equipment:
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateItemTarget(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Items, ItemTargetKind.Equipment, "Items Configs / Снаряжение.id");
                            break;
                        case RewardTypeEnum.Consumable:
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateItemTarget(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Items, ItemTargetKind.Consumable, "Items Configs / Расходники.id");
                            break;
                        case RewardTypeEnum.Recipe:
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateItemTarget(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Items, ItemTargetKind.Recipe, "Items Configs / Рецепты.id");
                            break;
                        case RewardTypeEnum.SkillExp:
                            ValidateIdSet(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", activity.SkillIds, "Activity Configs / Skills.skill_id");
                            break;
                        case RewardTypeEnum.Currency:
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Items.CurrencyIds, "Items Configs / Валюты.currency_id");
                            break;
                        case RewardTypeEnum.Gold:
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs") &&
                                !registry.Items.CurrencyIds.Contains(GoldCurrencyId))
                            {
                                AddIssue(report, activity.Source.DisplayName, "ActivityRewards", row.RowNumber, "reward_type", rewardTypeRaw, $"Gold reward requires '{GoldCurrencyId}' in Items Configs / Валюты.currency_id; target_id is ignored by runtime.");
                            }
                            break;
                        case RewardTypeEnum.LootTable:
                            if (TryGetRequiredRegistry(report, registry.Loot, "Loot Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Loot.LootTableIds, "Loot Configs / LootTables.loot_table_id");
                            break;
                        case RewardTypeEnum.Hero:
                            if (TryGetRequiredRegistry(report, registry.Heroes, "Heroes Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Heroes.HeroIds, "Heroes Configs / Heroes.HeroId");
                            break;
                        case RewardTypeEnum.UnlockBuilding:
                            if (TryGetRequiredRegistry(report, registry.Buildings, "Buildings Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Buildings.BuildingIds, "Buildings Configs / Index.building_id");
                            break;
                        case RewardTypeEnum.UnlockLocation:
                            if (TryGetRequiredRegistry(report, registry.Map, "Map Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "ActivityRewards", row, "target_id", registry.Map.LocationIds, "Map Configs / MapLocations.location_id");
                            break;
                        default:
                            AddIssue(report, activity.Source.DisplayName, "ActivityRewards", row.RowNumber, "reward_type", rewardTypeRaw, $"Reward type '{rewardTypeRaw}' is not supported by activity runtime.");
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

            private static void ValidateQuests(ActivityRegistry activity, ConfigRegistry registry, ConfigPipelineReport report)
            {
                ValidateQuestLocalisation(activity, registry, report);
                ValidateQuestStartConditions(activity, registry, report);
                ValidateQuestSteps(activity, registry, report);
                ValidateQuestRewards(activity, registry, report);
            }

            private static void ValidateQuestLocalisation(ActivityRegistry activity, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!activity.Source.TryGetTable("Quests", out var table))
                    return;

                if (!TryGetRequiredRegistry(report, registry.Localisation, "Localisation"))
                    return;

                foreach (var row in table.DataRows)
                {
                    ValidateIdSet(report, activity.Source.DisplayName, "Quests", row, "name_id", registry.Localisation.LocalisationIds, "Localisation.id");
                    ValidateIdSet(report, activity.Source.DisplayName, "Quests", row, "description_id", registry.Localisation.LocalisationIds, "Localisation.id");
                }
            }

            private static void ValidateQuestStartConditions(ActivityRegistry activity, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!activity.Source.TryGetTable("QuestStartConditions", out var table))
                    return;

                foreach (var row in table.DataRows)
                {
                    ValidateIdSet(report, activity.Source.DisplayName, "QuestStartConditions", row, "quest_id", activity.EnabledQuestIds, "Activity Configs / enabled Quests.quest_id");
                    var conditionType = row.Get("condition_type");
                    var targetId = row.Get("target_id");

                    switch (conditionType)
                    {
                        case "NewGame":
                            if (!IsBlank(targetId))
                                AddIssue(report, activity.Source.DisplayName, "QuestStartConditions", row.RowNumber, "target_id", targetId, "NewGame condition requires empty target_id.");
                            break;
                        case "ActivityFailed":
                        case "ActivityCompleted":
                            if (ValidateRequiredTargetId(report, activity.Source.DisplayName, "QuestStartConditions", row, conditionType))
                                ValidateIdSet(report, activity.Source.DisplayName, "QuestStartConditions", row, "target_id", activity.ActivityIds, "Activity Configs / Activities.id");
                            break;
                        case "StageEntered":
                            if (ValidateRequiredTargetId(report, activity.Source.DisplayName, "QuestStartConditions", row, conditionType) &&
                                TryGetRequiredRegistry(report, registry.Buildings, "Buildings Configs / SettlementStages"))
                            {
                                ValidateIdSet(report, activity.Source.DisplayName, "QuestStartConditions", row, "target_id", registry.Buildings.EnabledStageIds, "Buildings Configs / enabled SettlementStages.stage_id");
                            }
                            break;
                        case "BuildingLevel":
                            if (ValidateRequiredTargetId(report, activity.Source.DisplayName, "QuestStartConditions", row, conditionType) &&
                                TryGetRequiredRegistry(report, registry.Buildings, "Buildings Configs"))
                            {
                                ValidateIdSet(report, activity.Source.DisplayName, "QuestStartConditions", row, "target_id", registry.Buildings.BuildingIds, "Buildings Configs / Index.building_id");
                            }
                            break;
                        default:
                            AddIssue(report, activity.Source.DisplayName, "QuestStartConditions", row.RowNumber, "condition_type", conditionType, "Unknown QuestStartConditions.condition_type.");
                            break;
                    }
                }
            }

            private static bool ValidateRequiredTargetId(
                ConfigPipelineReport report,
                string sourceConfig,
                string sheet,
                ConfigSheetDataRow row,
                string conditionType)
            {
                var targetId = row.Get("target_id");
                if (!IsBlank(targetId))
                    return true;

                AddIssue(report, sourceConfig, sheet, row.RowNumber, "target_id", targetId, $"{conditionType} condition requires target_id.");
                return false;
            }

            private static void ValidateQuestSteps(ActivityRegistry activity, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!activity.Source.TryGetTable("QuestSteps", out var table))
                    return;

                foreach (var row in table.DataRows)
                {
                    ValidateIdSet(report, activity.Source.DisplayName, "QuestSteps", row, "quest_id", activity.EnabledQuestIds, "Activity Configs / enabled Quests.quest_id");
                    if (TryGetRequiredRegistry(report, registry.Localisation, "Localisation"))
                        ValidateIdSet(report, activity.Source.DisplayName, "QuestSteps", row, "description_id", registry.Localisation.LocalisationIds, "Localisation.id");

                    var targetId = row.Get("target_id");
                    if (IsBlank(targetId))
                        continue;

                    switch (row.Get("objective_type"))
                    {
                        case "ResourceCount":
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateItemTarget(report, activity.Source.DisplayName, "QuestSteps", row, "target_id", registry.Items, ItemTargetKind.Resource, "Items Configs / resources.id");
                            break;
                        case "ItemCount":
                            if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                                ValidateItemTarget(report, activity.Source.DisplayName, "QuestSteps", row, "target_id", registry.Items, ItemTargetKind.AnyItem, "Items Configs item/resource/recipe/consumable registry");
                            break;
                        case "BuildingLevel":
                            if (TryGetRequiredRegistry(report, registry.Buildings, "Buildings Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "QuestSteps", row, "target_id", registry.Buildings.BuildingIds, "Buildings Configs / Index.building_id");
                            break;
                        case "ActivityCompleted":
                            ValidateIdSet(report, activity.Source.DisplayName, "QuestSteps", row, "target_id", activity.ActivityIds, "Activity Configs / Activities.id");
                            break;
                        case "HeroAvailable":
                            if (TryGetRequiredRegistry(report, registry.Heroes, "Heroes Configs"))
                                ValidateIdSet(report, activity.Source.DisplayName, "QuestSteps", row, "target_id", registry.Heroes.HeroIds, "Heroes Configs / Heroes.HeroId");
                            break;
                        case "LocationUnlocked":
                            if (TryGetRequiredRegistry(report, registry.Map, "Map Configs"))
                                ValidateMapAccess(report, activity.Source.DisplayName, "QuestSteps", row, registry.Map);
                            break;
                    }
                }
            }

            private static void ValidateQuestRewards(ActivityRegistry activity, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!activity.Source.TryGetTable("QuestRewards", out var table))
                    return;

                foreach (var row in table.DataRows)
                {
                    ValidateIdSet(report, activity.Source.DisplayName, "QuestRewards", row, "quest_id", activity.EnabledQuestIds, "Activity Configs / enabled Quests.quest_id");
                    if (!IsBlank(row.Get("target_id")))
                        ValidateQuestRewardTarget(activity, registry, report, row);
                }
            }

            private static void ValidateQuestRewardTarget(ActivityRegistry activity, ConfigRegistry registry, ConfigPipelineReport report, ConfigSheetDataRow row)
            {
                var rewardTypeRaw = row.Get("reward_type");
                if (!ActivityTypeParser.TryParseRewardType(rewardTypeRaw, out var rewardType))
                {
                    AddIssue(report, activity.Source.DisplayName, "QuestRewards", row.RowNumber, "reward_type", rewardTypeRaw, $"Unknown reward_type '{rewardTypeRaw}'.");
                    return;
                }

                switch (rewardType)
                {
                    case RewardTypeEnum.Resource:
                        if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                            ValidateItemTarget(report, activity.Source.DisplayName, "QuestRewards", row, "target_id", registry.Items, ItemTargetKind.Resource, "Items Configs / resources.id");
                        break;
                    case RewardTypeEnum.Item:
                        if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                            ValidateItemTarget(report, activity.Source.DisplayName, "QuestRewards", row, "target_id", registry.Items, ItemTargetKind.AnyItem, "Items Configs item/recipe/consumable registry");
                        break;
                    case RewardTypeEnum.Equipment:
                        if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                            ValidateItemTarget(report, activity.Source.DisplayName, "QuestRewards", row, "target_id", registry.Items, ItemTargetKind.Equipment, "Items Configs equipment id");
                        break;
                    case RewardTypeEnum.Consumable:
                        if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                            ValidateItemTarget(report, activity.Source.DisplayName, "QuestRewards", row, "target_id", registry.Items, ItemTargetKind.Consumable, "Items Configs consumables.id");
                        break;
                    case RewardTypeEnum.Recipe:
                        if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                            ValidateItemTarget(report, activity.Source.DisplayName, "QuestRewards", row, "target_id", registry.Items, ItemTargetKind.Recipe, "Items Configs recipes.id");
                        break;
                    case RewardTypeEnum.SkillExp:
                        ValidateIdSet(report, activity.Source.DisplayName, "QuestRewards", row, "target_id", activity.SkillIds, "Activity Configs / Skills.skill_id");
                        break;
                    case RewardTypeEnum.Currency:
                        if (TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                            ValidateIdSet(report, activity.Source.DisplayName, "QuestRewards", row, "target_id", registry.Items.CurrencyIds, "Items Configs / currencies.currency_id");
                        break;
                    case RewardTypeEnum.Gold:
                        if (TryGetRequiredRegistry(report, registry.Items, "Items Configs") &&
                            !registry.Items.CurrencyIds.Contains(GoldCurrencyId))
                        {
                            AddIssue(report, activity.Source.DisplayName, "QuestRewards", row.RowNumber, "reward_type", rewardTypeRaw, $"Gold reward requires '{GoldCurrencyId}' in Items Configs / currencies.currency_id; target_id is ignored by runtime.");
                        }
                        break;
                    case RewardTypeEnum.LootTable:
                        if (TryGetRequiredRegistry(report, registry.Loot, "Loot Configs"))
                            ValidateIdSet(report, activity.Source.DisplayName, "QuestRewards", row, "target_id", registry.Loot.LootTableIds, "Loot Configs / LootTables.loot_table_id");
                        break;
                    case RewardTypeEnum.Hero:
                        if (TryGetRequiredRegistry(report, registry.Heroes, "Heroes Configs"))
                            ValidateIdSet(report, activity.Source.DisplayName, "QuestRewards", row, "target_id", registry.Heroes.HeroIds, "Heroes Configs / Heroes.HeroId");
                        break;
                    case RewardTypeEnum.UnlockBuilding:
                        if (TryGetRequiredRegistry(report, registry.Buildings, "Buildings Configs"))
                            ValidateIdSet(report, activity.Source.DisplayName, "QuestRewards", row, "target_id", registry.Buildings.BuildingIds, "Buildings Configs / Index.building_id");
                        break;
                    case RewardTypeEnum.UnlockLocation:
                        if (TryGetRequiredRegistry(report, registry.Map, "Map Configs"))
                            ValidateIdSet(report, activity.Source.DisplayName, "QuestRewards", row, "target_id", registry.Map.LocationIds, "Map Configs / MapLocations.location_id");
                        break;
                    case RewardTypeEnum.HeroExp:
                    case RewardTypeEnum.Reputation:
                        AddIssue(report, activity.Source.DisplayName, "QuestRewards", row.RowNumber, "reward_type", rewardTypeRaw, $"Reward type '{rewardTypeRaw}' is recognized but not supported by runtime.");
                        break;
                    default:
                        AddIssue(report, activity.Source.DisplayName, "QuestRewards", row.RowNumber, "reward_type", rewardTypeRaw, $"Reward type '{rewardTypeRaw}' is not supported by runtime.");
                        break;
                }
            }
        }

        private static class EnemiesCrossChecks
        {
            public static void Validate(ConfigRegistry registry, ConfigPipelineReport report)
            {
                var enemies = registry.Enemies;
                if (enemies == null)
                    return;

                ValidateEnemyGroups(enemies, report);
                ValidateEnemyLoot(enemies, registry, report);
                ValidateLocalisation(enemies, registry, report);
            }

            private static void ValidateEnemyGroups(EnemiesRegistry enemies, ConfigPipelineReport report)
            {
                if (!enemies.Source.TryGetTable("EnemyGroups", out var table))
                    return;

                foreach (var row in table.DataRows)
                {
                    foreach (var packedRef in ParsePackedRefs(row.Get("enemy_ref")))
                    {
                        if (!enemies.EnemyIds.Contains(packedRef.Id))
                            AddIssue(report, enemies.Source.DisplayName, "EnemyGroups", row.RowNumber, "enemy_ref", packedRef.Id, "enemy_ref references missing Enemies.enemy_id.");

                        if (!enemies.EnemyLevelIds.Contains(packedRef.Value))
                            AddIssue(report, enemies.Source.DisplayName, "EnemyGroups", row.RowNumber, "enemy_ref", packedRef.Raw, "enemy_ref references missing EnemyLevels.level.");
                    }
                }
            }

            private static void ValidateEnemyLoot(EnemiesRegistry enemies, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!enemies.Source.TryGetTable("EnemyLoot", out var table) ||
                    !HasAnyValue(table, "loot_id"))
                {
                    return;
                }

                if (!TryGetRequiredRegistry(report, registry.Items, "Items Configs"))
                    return;

                foreach (var row in table.DataRows)
                    ValidateLootLikeItemReference(report, enemies.Source.DisplayName, "EnemyLoot", row, "loot_id", registry.Items);
            }

            private static void ValidateLocalisation(EnemiesRegistry enemies, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!HasAnyValue(enemies.Source, "Enemies", "name_id", "description_id") &&
                    !HasAnyValue(enemies.Source, "EnemyAbilities", "name_id", "description_id") &&
                    !HasAnyValue(enemies.Source, "CombatStatuses", "name_id", "description_id"))
                {
                    return;
                }

                if (!TryGetRequiredRegistry(report, registry.Localisation, "Localisation"))
                    return;

                foreach (var sheetName in new[] { "Enemies", "EnemyAbilities", "CombatStatuses" })
                {
                    if (!enemies.Source.TryGetTable(sheetName, out var table))
                        continue;

                    foreach (var row in table.DataRows)
                    {
                        ValidateIdSet(report, enemies.Source.DisplayName, sheetName, row, "name_id", registry.Localisation.LocalisationIds, "Localisation.id");
                        ValidateIdSet(report, enemies.Source.DisplayName, sheetName, row, "description_id", registry.Localisation.LocalisationIds, "Localisation.id");
                    }
                }
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
                    foreach (var row in RuntimeRows(table))
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
                    foreach (var row in RuntimeRows(table))
                        ValidateIdSet(report, items.Source.DisplayName, table.Name, row, "rarity_id", registry.Activity.RarityIds, "Activity Configs / Rarities.rarity_id");
                }
            }

            private static void ValidateBuildings(ItemsRegistry items, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!TryGetRequiredRegistry(report, registry.Buildings, "Buildings Configs"))
                    return;

                foreach (var table in ItemTables(items.Source, includeCurrencies: false))
                {
                    foreach (var row in RuntimeRows(table))
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
                    foreach (var row in RuntimeRows(table))
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
                    foreach (var row in RuntimeRows(table))
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
                    foreach (var row in RuntimeRows(table))
                    {
                        var visibilityItemId = row.Get("visibility_item_id");
                        if (!IsBlank(visibilityItemId) && !items.ContainsAnyItem(visibilityItemId))
                            AddIssue(report, items.Source.DisplayName, table.Name, row.RowNumber, "visibility_item_id", visibilityItemId, "visibility_item_id does not exist in Items Configs item/recipe/consumable registry.");

                        var targetItemId = row.Get("target_item_id");
                        if (!IsBlank(targetItemId) && !items.ContainsAnyItem(targetItemId))
                            AddIssue(report, items.Source.DisplayName, table.Name, row.RowNumber, "target_item_id", targetItemId, "target_item_id does not exist in Items Configs item registry.");

                        var recipeItemId = row.Get("required_recipe_item_id");
                        if (!IsBlank(recipeItemId) && !items.ContainsRecipe(recipeItemId))
                            AddIssue(report, items.Source.DisplayName, table.Name, row.RowNumber, "required_recipe_item_id", recipeItemId, "required_recipe_item_id does not exist in Items Configs / Recipes.id registry.");
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
                    foreach (var actionId in items.EnabledCraftDefinitionIds)
                    {
                        if (registry.Activity.ActivityIds.Contains(actionId))
                            AddIssue(report, items.Source.DisplayName, CraftDefinitionsSheet, 0, "craft_id", actionId, "craft_id conflicts with Activity Configs / Activities.id.");
                    }
                }

                if (registry.Buildings != null)
                {
                    foreach (var actionId in items.EnabledCraftDefinitionIds)
                    {
                        if (registry.Buildings.BuildActionIds.Contains(actionId))
                            AddIssue(report, items.Source.DisplayName, CraftDefinitionsSheet, 0, "craft_id", actionId, "craft_id conflicts with Buildings Configs build action id.");
                    }
                }
            }

            private static IEnumerable<ConfigSheetDataRow> RuntimeRows(ConfigSheetTable table)
            {
                foreach (var row in table.DataRows)
                {
                    if (!table.HasColumn("enabled") || IsTrue(row.Get("enabled")))
                        yield return row;
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
                if (loot == null)
                    return;

                ValidateRollModes(loot, report, "LootTables");
                ValidateRollModes(loot, report, "LootGroups");

                if (!loot.Source.TryGetTable("LootTableEntries", out var table) ||
                    !HasAnyValue(table, "target_id"))
                    return;

                var hasItemsRegistry = TryGetRequiredRegistry(report, registry.Items, "Items Configs");

                foreach (var row in table.DataRows)
                {
                    var dropTypeRaw = row.Get("drop_type");
                    if (!ActivityTypeParser.TryParseDropType(dropTypeRaw, out var dropType))
                    {
                        AddIssue(report, loot.Source.DisplayName, "LootTableEntries", row.RowNumber, "drop_type", dropTypeRaw, $"Unknown drop_type '{dropTypeRaw}'.");
                        continue;
                    }

                    var targetId = row.Get("target_id");
                    if (IsBlank(targetId))
                        continue;

                    if (!hasItemsRegistry)
                        continue;

                    if (dropType == DropTypeEnum.Resource)
                    {
                        ValidateItemTarget(report, loot.Source.DisplayName, "LootTableEntries", row, "target_id", registry.Items, ItemTargetKind.Resource, "Items Configs / Ресурсы.id");
                        continue;
                    }

                    if (dropType == DropTypeEnum.Gold)
                    {
                        if (!string.Equals(targetId, GoldCurrencyId, StringComparison.OrdinalIgnoreCase))
                        {
                            AddIssue(report, loot.Source.DisplayName, "LootTableEntries", row.RowNumber, "target_id", targetId, $"Gold drop_type requires target_id '{GoldCurrencyId}'.");
                            continue;
                        }

                        ValidateIdSet(report, loot.Source.DisplayName, "LootTableEntries", row, "target_id", registry.Items.CurrencyIds, "Items Configs / Валюты.currency_id");
                        continue;
                    }

                    ValidateItemTarget(report, loot.Source.DisplayName, "LootTableEntries", row, "target_id", registry.Items, ItemTargetKind.AnyItem, "Items Configs item/recipe/consumable registry");
                }
            }

            private static void ValidateRollModes(LootRegistry loot, ConfigPipelineReport report, string tableName)
            {
                if (!loot.Source.TryGetTable(tableName, out var table))
                    return;

                foreach (var row in table.DataRows)
                {
                    var rollMode = row.Get("roll_mode");
                    if (!ActivityTypeParser.TryParseLootRollMode(rollMode, out _))
                        AddIssue(report, loot.Source.DisplayName, tableName, row.RowNumber, "roll_mode", rollMode, $"Unknown roll_mode '{rollMode}'.");
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
                ValidateIndexRules(buildings, report);
                ValidateSettlementStages(buildings, registry, report);
                ValidateBuildingLevelRows(buildings, registry, report);
                ValidateBuildingActivities(buildings, registry, report);
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

            private static void ValidateIndexRules(BuildingsRegistry buildings, ConfigPipelineReport report)
            {
                if (!buildings.Source.TryGetTable("Index", out var index))
                    return;

                foreach (var row in index.DataRows)
                {
                    var buildingId = row.Get("building_id");
                    if (IsBlank(buildingId))
                        continue;

                    if (IsBlank(row.Get("start_level")))
                    {
                        AddIssue(report, buildings.Source.DisplayName, "Index", row.RowNumber, "start_level", row.Get("start_level"), "start_level is required.");
                    }
                    else if (!buildings.ContainsBuildingLevel(buildingId, row.Get("start_level")))
                    {
                        AddIssue(report, buildings.Source.DisplayName, "Index", row.RowNumber, "start_level", row.Get("start_level"), "start_level does not exist in BuildingLevels for this building_id.");
                    }

                    ValidateBuildingLevelRef(report, buildings.Source.DisplayName, "Index", row.RowNumber, "clickable_requirement", row.Get("clickable_requirement"), buildings);
                }
            }

            private static void ValidateSettlementStages(BuildingsRegistry buildings, ConfigRegistry registry, ConfigPipelineReport report)
            {
                ValidateStageRows(buildings, registry, report);
                ValidateStageSlots(buildings, report);
                ValidateStageObjectives(buildings, registry, report);
                ValidateStage2IsEmpty(buildings, report);
            }

            private static void ValidateStageRows(BuildingsRegistry buildings, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!buildings.Source.TryGetTable("SettlementStages", out var table))
                    return;

                if (TryGetRequiredRegistry(report, registry.Localisation, "Localisation"))
                {
                    foreach (var row in table.DataRows)
                    {
                        ValidateIdSet(report, buildings.Source.DisplayName, "SettlementStages", row, "name_id", registry.Localisation.LocalisationIds, "Localisation.id");
                        ValidateIdSet(report, buildings.Source.DisplayName, "SettlementStages", row, "description_id", registry.Localisation.LocalisationIds, "Localisation.id");
                    }
                }

                foreach (var row in table.DataRows)
                {
                    if (IsDisabled(row.Get("enabled")))
                        continue;

                    var nextStageId = row.Get("next_stage_id");
                    if (!IsBlank(nextStageId) && !buildings.EnabledStageIds.Contains(nextStageId))
                        AddIssue(report, buildings.Source.DisplayName, "SettlementStages", row.RowNumber, "next_stage_id", nextStageId, "next_stage_id references missing enabled SettlementStages.stage_id.");
                }
            }

            private static void ValidateStageSlots(BuildingsRegistry buildings, ConfigPipelineReport report)
            {
                if (!buildings.Source.TryGetTable("SettlementStageSlots", out var table))
                    return;

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    if (IsDisabled(row.Get("enabled")))
                        continue;

                    var stageId = row.Get("stage_id");
                    var slotId = row.Get("slot_id");
                    var buildingId = row.Get("building_id");

                    if (!IsBlank(stageId) && !buildings.EnabledStageIds.Contains(stageId))
                        AddIssue(report, buildings.Source.DisplayName, "SettlementStageSlots", row.RowNumber, "stage_id", stageId, "stage_id references missing enabled SettlementStages.stage_id.");

                    if (!IsBlank(buildingId) && !buildings.BuildingIds.Contains(buildingId))
                        AddIssue(report, buildings.Source.DisplayName, "SettlementStageSlots", row.RowNumber, "building_id", buildingId, "building_id does not exist in Buildings Configs / Index.building_id.");

                    var key = $"{stageId}\n{slotId}";
                    if (!IsBlank(stageId) && !IsBlank(slotId) && !seen.Add(key))
                        AddIssue(report, buildings.Source.DisplayName, "SettlementStageSlots", row.RowNumber, "slot_id", slotId, "Duplicate stage_id + slot_id.");
                }
            }

            private static void ValidateStageObjectives(BuildingsRegistry buildings, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!buildings.Source.TryGetTable("SettlementStageObjectives", out var table))
                    return;

                if (!TryGetRequiredRegistry(report, registry.Activity, "Activity Configs / Quests"))
                    return;

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var requiredWeights = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in table.DataRows)
                {
                    var stageId = row.Get("stage_id");
                    var questId = row.Get("quest_id");

                    if (!IsBlank(stageId) && !buildings.EnabledStageIds.Contains(stageId))
                        AddIssue(report, buildings.Source.DisplayName, "SettlementStageObjectives", row.RowNumber, "stage_id", stageId, "stage_id references missing enabled SettlementStages.stage_id.");

                    ValidateIdSet(report, buildings.Source.DisplayName, "SettlementStageObjectives", row, "quest_id", registry.Activity.EnabledQuestIds, "Activity Configs / enabled Quests.quest_id");

                    var key = $"{stageId}\n{questId}";
                    if (!IsBlank(stageId) && !IsBlank(questId) && !seen.Add(key))
                        AddIssue(report, buildings.Source.DisplayName, "SettlementStageObjectives", row.RowNumber, "quest_id", questId, "Duplicate stage_id + quest_id.");

                    if (IsTrue(row.Get("required")) &&
                        long.TryParse(row.Get("weight_percent"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight))
                    {
                        requiredWeights.TryGetValue(stageId, out var total);
                        requiredWeights[stageId] = total + weight;
                    }
                }

                foreach (var pair in requiredWeights)
                {
                    if (pair.Value != 100L)
                        AddIssue(report, buildings.Source.DisplayName, "SettlementStageObjectives", 0, "weight_percent", pair.Key, "Required objective weight_percent total must be 100 for each stage.");
                }
            }

            private static void ValidateStage2IsEmpty(BuildingsRegistry buildings, ConfigPipelineReport report)
            {
                if (!buildings.StageIds.Contains("stage_2"))
                {
                    AddIssue(report, buildings.Source.DisplayName, "SettlementStages", 0, "stage_id", "stage_2", "stage_2 is required.");
                    return;
                }

                if (!buildings.EnabledStageIds.Contains("stage_2"))
                {
                    AddIssue(report, buildings.Source.DisplayName, "SettlementStages", 0, "enabled", "stage_2", "stage_2 must be enabled.");
                    return;
                }

                if (buildings.Source.TryGetTable("SettlementStageSlots", out var slots))
                {
                    foreach (var row in slots.DataRows)
                    {
                        if (IsDisabled(row.Get("enabled")))
                            continue;

                        if (string.Equals(row.Get("stage_id"), "stage_2", StringComparison.OrdinalIgnoreCase))
                            AddIssue(report, buildings.Source.DisplayName, "SettlementStageSlots", row.RowNumber, "stage_id", "stage_2", "stage_2 must not have slots.");
                    }
                }

                if (buildings.Source.TryGetTable("SettlementStageObjectives", out var objectives))
                {
                    foreach (var row in objectives.DataRows)
                    {
                        if (string.Equals(row.Get("stage_id"), "stage_2", StringComparison.OrdinalIgnoreCase))
                            AddIssue(report, buildings.Source.DisplayName, "SettlementStageObjectives", row.RowNumber, "stage_id", "stage_2", "stage_2 must not have objectives.");
                    }
                }
            }

            private static void ValidateBuildingLevelRows(BuildingsRegistry buildings, ConfigRegistry registry, ConfigPipelineReport report)
            {
                foreach (var table in buildings.BuildingLevelTables)
                {
                    if (!table.HasColumn("source_activity_id"))
                    {
                        continue;
                    }

                    foreach (var row in table.DataRows)
                    {
                        ValidateSourceActivity(buildings, registry, report, table, row);
                        ValidateActiveHeroLimit(buildings, report, table, row);
                        ValidateMaterials(buildings, registry, report, table, row);
                        ValidateRequirementActivities(buildings, registry, report, table, row);
                        ValidateRequirementSkills(buildings, registry, report, table, row);
                        ValidateBuildFormula(buildings, registry, report, table, row);
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
                    (registry.Items == null || !registry.Items.EnabledCraftDefinitionIds.Contains(sourceActivityId)))
                {
                    AddIssue(report, buildings.Source.DisplayName, table.Name, row.RowNumber, "source_activity_id", sourceActivityId, "source_activity_id does not exist in Activity Configs / Activities.id or unified action registry.");
                }
            }

            private static void ValidateActiveHeroLimit(BuildingsRegistry buildings, ConfigPipelineReport report, ConfigSheetTable table, ConfigSheetDataRow row)
            {
                buildings.BuildingIdsBySheetName.TryGetValue(table.Name, out var buildingId);
                var isHall = string.Equals(buildingId, "building_hall", StringComparison.OrdinalIgnoreCase);
                if (!table.HasColumn("active_hero_limit"))
                {
                    if (isHall)
                        AddIssue(report, buildings.Source.DisplayName, table.Name, row.RowNumber, "active_hero_limit", string.Empty, "active_hero_limit is required for building_hall levels.");

                    return;
                }

                var raw = row.Get("active_hero_limit");
                if (IsBlank(raw))
                {
                    if (isHall)
                        AddIssue(report, buildings.Source.DisplayName, table.Name, row.RowNumber, "active_hero_limit", raw, "active_hero_limit is required for building_hall levels.");

                    return;
                }

                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit))
                {
                    AddIssue(report, buildings.Source.DisplayName, table.Name, row.RowNumber, "active_hero_limit", raw, "active_hero_limit must be an integer greater than or equal to 0.");
                    return;
                }

                if (limit < 0)
                {
                    AddIssue(report, buildings.Source.DisplayName, table.Name, row.RowNumber, "active_hero_limit", raw, "active_hero_limit must be greater than or equal to 0.");
                    return;
                }

                if (isHall &&
                    (string.Equals(row.Get("level"), "0", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(row.Get("level"), "1", StringComparison.OrdinalIgnoreCase)) &&
                    limit != 1)
                {
                    AddIssue(report, buildings.Source.DisplayName, table.Name, row.RowNumber, "active_hero_limit", raw, "building_hall level 0 and 1 must have active_hero_limit = 1 for Stage 1.");
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

                foreach (var packedRef in ParseActivityRequirementRefs(row.Get("requirements_activities")))
                {
                    var exists = registry.Activity.ActivityIds.Contains(packedRef.Id) ||
                                 buildings.BuildActionIds.Contains(packedRef.Id) ||
                                 (registry.Items != null && registry.Items.EnabledCraftDefinitionIds.Contains(packedRef.Id));
                    if (!exists)
                        AddIssue(report, buildings.Source.DisplayName, table.Name, row.RowNumber, "requirements_activities", packedRef.Raw, "requirements_activities references missing Activity Configs / Activities.id or generated action id.");
                }
            }

            private static void ValidateRequirementSkills(BuildingsRegistry buildings, ConfigRegistry registry, ConfigPipelineReport report, ConfigSheetTable table, ConfigSheetDataRow row)
            {
                if (!HasAnyValue(table, "requirements_skills") && !HasAnyValue(table, "skill_id"))
                    return;

                if (!TryGetRequiredRegistry(report, registry.Activity, "Activity Configs / Skills"))
                    return;

                foreach (var packedRef in ParsePackedRefs(row.Get("requirements_skills")))
                {
                    if (!registry.Activity.SkillIds.Contains(packedRef.Id))
                        AddIssue(report, buildings.Source.DisplayName, table.Name, row.RowNumber, "requirements_skills", packedRef.Raw, "requirements_skills references missing Activity Configs / Skills.skill_id.");
                }

                ValidateIdSet(report, buildings.Source.DisplayName, table.Name, row, "skill_id", registry.Activity.SkillIds, "Activity Configs / Skills.skill_id");
            }

            private static void ValidateBuildFormula(BuildingsRegistry buildings, ConfigRegistry registry, ConfigPipelineReport report, ConfigSheetTable table, ConfigSheetDataRow row)
            {
                var formulaId = row.Get("build_formula_id");
                if (IsBlank(formulaId))
                    return;

                if (!TryGetRequiredRegistry(report, registry.Formula, "Formula Configs"))
                    return;

                ValidateIdSet(report, buildings.Source.DisplayName, table.Name, row, "build_formula_id", registry.Formula.FormulaIds, "Formula Configs / HeroDerivedStats.formula_id");
            }

            private static void ValidateBuildingActivities(BuildingsRegistry buildings, ConfigRegistry registry, ConfigPipelineReport report)
            {
                if (!buildings.Source.TryGetTable("BuildingActivities", out var table))
                    return;

                if (!TryGetRequiredRegistry(report, registry.Activity, "Activity Configs"))
                    return;

                foreach (var row in table.DataRows)
                {
                    if (string.Equals(row.Get("enabled"), "FALSE", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(row.Get("enabled"), "false", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(row.Get("enabled"), "0", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var buildingId = row.Get("building_id");
                    if (IsBlank(buildingId))
                        AddIssue(report, buildings.Source.DisplayName, "BuildingActivities", row.RowNumber, "building_id", buildingId, "building_id is required.");
                    else if (!buildings.BuildingIds.Contains(buildingId))
                        AddIssue(report, buildings.Source.DisplayName, "BuildingActivities", row.RowNumber, "building_id", buildingId, "building_id does not exist in Buildings Configs / Index.building_id.");

                    if (IsBlank(row.Get("building_level")))
                    {
                        AddIssue(report, buildings.Source.DisplayName, "BuildingActivities", row.RowNumber, "building_level", row.Get("building_level"), "building_level is required.");
                    }
                    else if (!IsBlank(buildingId) &&
                             !buildings.ContainsBuildingLevel(buildingId, row.Get("building_level")))
                    {
                        AddIssue(report, buildings.Source.DisplayName, "BuildingActivities", row.RowNumber, "building_level", row.Get("building_level"), "building_level does not exist in BuildingLevels for this building_id.");
                    }

                    if (IsBlank(row.Get("activity_id")))
                        AddIssue(report, buildings.Source.DisplayName, "BuildingActivities", row.RowNumber, "activity_id", row.Get("activity_id"), "activity_id is required.");
                    else
                        ValidateUnifiedAction(report, buildings.Source.DisplayName, "BuildingActivities", row, "activity_id", row.Get("activity_id"), registry, buildings);
                    ValidateUnifiedAction(report, buildings.Source.DisplayName, "BuildingActivities", row, "show_if_activity_completed", row.Get("show_if_activity_completed"), registry, buildings);
                    ValidateUnifiedAction(report, buildings.Source.DisplayName, "BuildingActivities", row, "hide_if_activity_completed", row.Get("hide_if_activity_completed"), registry, buildings);
                    ValidateBuildingLevelRef(report, buildings.Source.DisplayName, "BuildingActivities", row.RowNumber, "clickable_requirement", row.Get("clickable_requirement"), buildings);
                }
            }

            private static void ValidateUnifiedAction(ConfigPipelineReport report, string sourceConfig, string sheet, ConfigSheetDataRow row, string column, string value, ConfigRegistry registry, BuildingsRegistry buildings)
            {
                if (IsBlank(value))
                    return;

                var exists = registry.Activity != null && registry.Activity.ActivityIds.Contains(value) ||
                             buildings.BuildActionIds.Contains(value);
                if (!exists)
                    AddIssue(report, sourceConfig, sheet, row.RowNumber, column, value, "Referenced action does not exist in Activity Configs / Activities.id or generated Buildings buildActions.");
            }

            private static void ValidateBuildingLevelRef(ConfigPipelineReport report, string sourceConfig, string sheet, int rowNumber, string column, string value, BuildingsRegistry buildings)
            {
                if (IsBlank(value))
                    return;

                var parts = value.Split(':');
                if (parts.Length != 2 || IsBlank(parts[0]) || IsBlank(parts[1]))
                {
                    AddIssue(report, sourceConfig, sheet, rowNumber, column, value, $"{column} must use building_id:level.");
                    return;
                }

                if (!buildings.ContainsBuildingLevel(parts[0].Trim(), parts[1].Trim()))
                    AddIssue(report, sourceConfig, sheet, rowNumber, column, value, $"{column} references missing Buildings Configs building_id:level.");
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
                        var craftId = row.Get("craft_id");
                        if (!IsBlank(craftId) && !registry.Items.EnabledCraftDefinitionIds.Contains(craftId))
                            AddIssue(report, buildings.Source.DisplayName, table.Name, row.RowNumber, "craft_id", craftId, "craft_id does not exist in Items Configs / CraftDefinitions.craft_id registry.");
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
                        if (registry.Items.EnabledCraftDefinitionIds.Contains(actionId))
                            AddIssue(report, buildings.Source.DisplayName, "buildActions", 0, "id", actionId, "Generated Build action id conflicts with Items Configs / CraftDefinitions.craft_id.");
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

                if (!HasAnyValue(map.Source, "MapCells", "map_cell_name_id") &&
                    !HasAnyValue(map.Source, "MapLocations", "map_location_name_id"))
                {
                    return;
                }

                if (!TryGetRequiredRegistry(report, registry.Localisation, "Localisation"))
                    return;

                if (map.Source.TryGetTable("MapCells", out var cells))
                {
                    foreach (var row in cells.DataRows)
                        ValidateIdSet(report, map.Source.DisplayName, "MapCells", row, "map_cell_name_id", registry.Localisation.LocalisationIds, "Localisation.id");
                }

                if (map.Source.TryGetTable("MapLocations", out var locations))
                {
                    foreach (var row in locations.DataRows)
                        ValidateIdSet(report, map.Source.DisplayName, "MapLocations", row, "map_location_name_id", registry.Localisation.LocalisationIds, "Localisation.id");
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
            foreach (var sheetName in new[] { ResourcesSheet, EquipmentWeaponsSheet, EquipmentArmorSheet, RecipesSheet, ConsumablesSheet, CraftDefinitionsSheet })
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
