using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace GuildIdle.Configs
{
    public sealed class CraftMaterialDescriptor
    {
        public string ItemId { get; }
        public int Count { get; }

        internal CraftMaterialDescriptor(string itemId, int count)
        {
            ItemId = itemId ?? string.Empty;
            Count = count;
        }
    }

    public sealed class CraftRequiredBuildingDescriptor
    {
        public string BuildingId { get; }
        public int Level { get; }

        internal CraftRequiredBuildingDescriptor(string buildingId, int level)
        {
            BuildingId = buildingId ?? string.Empty;
            Level = level;
        }
    }

    public sealed class CraftDefinitionDescriptor
    {
        public string CraftId { get; }
        public string TargetItemId { get; }
        public string CraftStationId { get; }
        public int CraftDurationSec { get; }
        public string CraftSkillId { get; }
        public IReadOnlyList<CraftRequiredBuildingDescriptor> RequiredBuildings { get; }
        public IReadOnlyList<CraftMaterialDescriptor> Materials { get; }
        public string RequiredRecipeItemId { get; }
        public int RequiredRecipeItemCount { get; }
        public bool ConsumeRecipeItem { get; }
        public int OutputCount { get; }
        public int FatigueCost { get; }
        public int SkillExp { get; }

        internal CraftDefinitionDescriptor(CraftDefinitionConfigDto source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            CraftId = source.craftId ?? string.Empty;
            TargetItemId = source.targetItemId ?? string.Empty;
            CraftStationId = source.craftStationId ?? string.Empty;
            CraftDurationSec = source.craftDurationSec;
            CraftSkillId = source.craftSkillId ?? string.Empty;
            RequiredBuildings = CopyRequiredBuildings(source.requiredBuildings);
            Materials = NormalizeMaterials(source.materials);
            RequiredRecipeItemId = source.requiredRecipeItemId ?? string.Empty;
            RequiredRecipeItemCount = source.requiredRecipeItemCount;
            ConsumeRecipeItem = source.consumeRecipeItem;
            OutputCount = source.outputCount;
            FatigueCost = source.fatigueCost;
            SkillExp = source.skillExp;
        }

        private static IReadOnlyList<CraftRequiredBuildingDescriptor> CopyRequiredBuildings(RequiredBuildingDto[] source)
        {
            var values = new List<CraftRequiredBuildingDescriptor>();
            foreach (var requirement in source ?? Array.Empty<RequiredBuildingDto>())
            {
                if (requirement != null)
                    values.Add(new CraftRequiredBuildingDescriptor(requirement.buildingId, requirement.level));
            }

            return new ReadOnlyCollection<CraftRequiredBuildingDescriptor>(values);
        }

        private static IReadOnlyList<CraftMaterialDescriptor> NormalizeMaterials(MaterialCostDto[] source)
        {
            var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var material in source ?? Array.Empty<MaterialCostDto>())
            {
                if (material == null)
                    continue;
                if (string.IsNullOrWhiteSpace(material.id))
                    throw new ArgumentException("Craft material item id must not be empty.", nameof(source));
                if (material.count <= 0)
                    throw new ArgumentOutOfRangeException(nameof(source), material.count, "Craft material count must be positive.");

                totals.TryGetValue(material.id, out var total);
                totals[material.id] = checked(total + material.count);
            }

            var values = new List<CraftMaterialDescriptor>(totals.Count);
            foreach (var pair in totals)
                values.Add(new CraftMaterialDescriptor(pair.Key, pair.Value));
            return new ReadOnlyCollection<CraftMaterialDescriptor>(values);
        }
    }

    public sealed class AvailableCraftDescriptor
    {
        public CraftDefinitionDescriptor Definition { get; }
        public string CraftId => Definition.CraftId;
        public string BuildingId { get; }
        public int BuildingLevel { get; }
        public int SortOrder { get; }
        public string UiCategory { get; }

        internal AvailableCraftDescriptor(CraftDefinitionDescriptor definition, BuildingCraftableConfigDto source)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            BuildingId = source?.buildingId ?? string.Empty;
            BuildingLevel = source?.buildingLevel ?? 0;
            SortOrder = source?.sortOrder ?? 0;
            UiCategory = source?.uiCategory ?? string.Empty;
        }
    }

    public sealed class CraftsConfigRepository
    {
        private static readonly IReadOnlyList<AvailableCraftDescriptor> EmptyAvailableCrafts =
            new ReadOnlyCollection<AvailableCraftDescriptor>(new List<AvailableCraftDescriptor>());

        private readonly Dictionary<string, CraftDefinitionDescriptor> _definitionsById =
            new Dictionary<string, CraftDefinitionDescriptor>(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<AvailableCraftDescriptor>> _availableByBuildingLevel =
            new Dictionary<string, IReadOnlyList<AvailableCraftDescriptor>>(StringComparer.Ordinal);

        public CraftsConfigRepository(ItemsConfigRepository items, BuildingsConfigRepository buildings)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            if (buildings == null)
                throw new ArgumentNullException(nameof(buildings));

            foreach (var source in items.CraftDefinitions)
            {
                if (source == null || string.IsNullOrWhiteSpace(source.craftId))
                    continue;
                if (_definitionsById.ContainsKey(source.craftId))
                {
                    Debug.LogError($"[Configs] Duplicate id '{source.craftId}' in Crafts/definitions.");
                    continue;
                }

                _definitionsById[source.craftId] = new CraftDefinitionDescriptor(source);
            }

            var grouped = new Dictionary<string, List<AvailableCraftDescriptor>>(StringComparer.Ordinal);
            foreach (var source in buildings.BuildingCraftables)
            {
                if (source == null || !source.enabled)
                    continue;
                if (!_definitionsById.TryGetValue(source.craftId ?? string.Empty, out var definition))
                {
                    Debug.LogError($"[Configs] Missing id '{source.craftId}' in Crafts/definitions.");
                    continue;
                }

                var key = BuildingLevelKey(source.buildingId, source.buildingLevel);
                if (!grouped.TryGetValue(key, out var values))
                {
                    values = new List<AvailableCraftDescriptor>();
                    grouped[key] = values;
                }
                values.Add(new AvailableCraftDescriptor(definition, source));
            }

            foreach (var pair in grouped)
            {
                pair.Value.Sort(CompareAvailableCrafts);
                _availableByBuildingLevel[pair.Key] = new ReadOnlyCollection<AvailableCraftDescriptor>(pair.Value);
            }
        }

        public CraftDefinitionDescriptor GetDefinition(string craftId)
        {
            if (TryGetDefinition(craftId, out var definition))
                return definition;

            ItemsConfigRepository.LogMissing("Crafts/definitions", craftId);
            return null;
        }

        public bool TryGetDefinition(string craftId, out CraftDefinitionDescriptor definition)
        {
            definition = null;
            return !string.IsNullOrWhiteSpace(craftId) && _definitionsById.TryGetValue(craftId, out definition);
        }

        public IReadOnlyList<AvailableCraftDescriptor> GetAvailableCrafts(string buildingId, int buildingLevel)
        {
            return !string.IsNullOrWhiteSpace(buildingId) && buildingLevel >= 0 &&
                   _availableByBuildingLevel.TryGetValue(BuildingLevelKey(buildingId, buildingLevel), out var values)
                ? values
                : EmptyAvailableCrafts;
        }

        private static int CompareAvailableCrafts(AvailableCraftDescriptor left, AvailableCraftDescriptor right)
        {
            var order = left.SortOrder.CompareTo(right.SortOrder);
            return order != 0
                ? order
                : string.Compare(left.CraftId, right.CraftId, StringComparison.Ordinal);
        }

        private static string BuildingLevelKey(string buildingId, int buildingLevel)
        {
            return $"{buildingId ?? string.Empty}\n{buildingLevel}";
        }
    }
}
