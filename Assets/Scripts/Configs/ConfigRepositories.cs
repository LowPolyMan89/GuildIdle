using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildIdle.Configs
{
    public sealed class ItemsConfigRepository
    {
        private readonly Dictionary<string, IItemConfig> _itemsById = NewIndex<IItemConfig>();
        private readonly Dictionary<string, ResourceConfigDto> _resourcesById = NewIndex<ResourceConfigDto>();
        private readonly Dictionary<string, EquipmentWeaponConfigDto> _equipmentWeaponsById = NewIndex<EquipmentWeaponConfigDto>();
        private readonly Dictionary<string, EquipmentArmorConfigDto> _equipmentArmorById = NewIndex<EquipmentArmorConfigDto>();
        private readonly Dictionary<string, RecipeConfigDto> _recipesById = NewIndex<RecipeConfigDto>();
        private readonly Dictionary<string, ConsumableConfigDto> _consumablesById = NewIndex<ConsumableConfigDto>();
        private readonly Dictionary<string, CurrencyConfigDto> _currenciesById = NewIndex<CurrencyConfigDto>();

        public ResourceConfigDto[] Resources { get; }
        public EquipmentWeaponConfigDto[] EquipmentWeapons { get; }
        public EquipmentArmorConfigDto[] EquipmentArmor { get; }
        public RecipeConfigDto[] Recipes { get; }
        public ConsumableConfigDto[] Consumables { get; }
        public CurrencyConfigDto[] Currencies { get; }
        public ItemActionConfigDto[] ItemActions { get; }

        public int ItemCount => _itemsById.Count;
        public int CurrencyCount => _currenciesById.Count;

        public ItemsConfigRepository(ItemsRuntimeConfigDto dto)
        {
            dto ??= new ItemsRuntimeConfigDto();
            Resources = dto.resources ?? Array.Empty<ResourceConfigDto>();
            EquipmentWeapons = dto.equipmentWeapons ?? Array.Empty<EquipmentWeaponConfigDto>();
            EquipmentArmor = dto.equipmentArmor ?? Array.Empty<EquipmentArmorConfigDto>();
            Recipes = dto.recipes ?? Array.Empty<RecipeConfigDto>();
            Consumables = dto.consumables ?? Array.Empty<ConsumableConfigDto>();
            Currencies = dto.currencies ?? Array.Empty<CurrencyConfigDto>();
            ItemActions = dto.itemActions ?? Array.Empty<ItemActionConfigDto>();

            AddItems(Resources, item => item.id, "Items/resources", _resourcesById, _itemsById);
            AddItems(EquipmentWeapons, item => item.id, "Items/equipmentWeapons", _equipmentWeaponsById, _itemsById);
            AddItems(EquipmentArmor, item => item.id, "Items/equipmentArmor", _equipmentArmorById, _itemsById);
            AddItems(Recipes, item => item.id, "Items/recipes", _recipesById, _itemsById);
            AddItems(Consumables, item => item.id, "Items/consumables", _consumablesById, _itemsById);
            AddItems(Currencies, item => item.currencyId, "Items/currencies", _currenciesById);
        }

        public IItemConfig Get(string id)
        {
            if (TryGet(id, out var item))
                return item;

            LogMissing("Items", id);
            return null;
        }

        public bool TryGet(string id, out IItemConfig item)
        {
            item = null;
            return !string.IsNullOrWhiteSpace(id) && _itemsById.TryGetValue(id, out item);
        }

        public CurrencyConfigDto GetCurrency(string currencyId)
        {
            if (TryGetCurrency(currencyId, out var currency))
                return currency;

            LogMissing("Items/currencies", currencyId);
            return null;
        }

        public bool TryGetCurrency(string currencyId, out CurrencyConfigDto currency)
        {
            currency = null;
            return !string.IsNullOrWhiteSpace(currencyId) && _currenciesById.TryGetValue(currencyId, out currency);
        }

        public bool TryGetResource(string id, out ResourceConfigDto resource) => TryGetIndexed(_resourcesById, id, out resource);
        public bool TryGetEquipmentWeapon(string id, out EquipmentWeaponConfigDto weapon) => TryGetIndexed(_equipmentWeaponsById, id, out weapon);
        public bool TryGetEquipmentArmor(string id, out EquipmentArmorConfigDto armor) => TryGetIndexed(_equipmentArmorById, id, out armor);
        public bool TryGetRecipe(string id, out RecipeConfigDto recipe) => TryGetIndexed(_recipesById, id, out recipe);
        public bool TryGetConsumable(string id, out ConsumableConfigDto consumable) => TryGetIndexed(_consumablesById, id, out consumable);

        private static void AddItems<T>(IEnumerable<T> items, Func<T, string> idSelector, string group, Dictionary<string, T> index)
            where T : class
        {
            foreach (var item in items)
                AddUnique(index, idSelector(item), item, group);
        }

        private static void AddItems<T>(IEnumerable<T> items, Func<T, string> idSelector, string group, Dictionary<string, T> typedIndex, Dictionary<string, IItemConfig> itemIndex)
            where T : class, IItemConfig
        {
            foreach (var item in items)
            {
                var id = idSelector(item);
                AddUnique(typedIndex, id, item, group);
                if (typedIndex.TryGetValue(id, out _))
                    AddUnique(itemIndex, id, item, group);
            }
        }

        private static bool TryGetIndexed<T>(Dictionary<string, T> index, string id, out T value)
            where T : class
        {
            value = null;
            return !string.IsNullOrWhiteSpace(id) && index.TryGetValue(id, out value);
        }

        internal static Dictionary<string, T> NewIndex<T>() => new Dictionary<string, T>(StringComparer.Ordinal);

        internal static void AddUnique<T>(Dictionary<string, T> index, string id, T value, string group)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                Debug.LogError($"[Configs] Empty id in {group}.");
                return;
            }

            if (string.Equals(id, "item_gold", StringComparison.Ordinal))
                Debug.LogError($"[Configs] item_gold is forbidden legacy data in {group}; use gold_id as currency_id.");

            if (index.ContainsKey(id))
            {
                Debug.LogError($"[Configs] Duplicate id '{id}' in {group}. Keeping the first entry.");
                return;
            }

            index.Add(id, value);
        }

        internal static void AddGrouped<T>(Dictionary<string, List<T>> index, string id, T value)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            if (!index.TryGetValue(id, out var list))
            {
                list = new List<T>();
                index.Add(id, list);
            }

            list.Add(value);
        }

        internal static void LogMissing(string group, string id)
        {
            Debug.LogError($"[Configs] Missing id '{id}' in {group}.");
        }
    }

    public sealed class HeroesConfigRepository
    {
        private readonly Dictionary<string, HeroConfigDto> _heroesById = ItemsConfigRepository.NewIndex<HeroConfigDto>();

        public HeroConfigDto[] Heroes { get; }
        public HeroGrowthConfigDto[] HeroGrowth { get; }
        public HeroUniqueSkillConfigDto[] HeroUniqueSkills { get; }
        public HeroSkillEffectConfigDto[] HeroSkillEffects { get; }
        public int Count => _heroesById.Count;

        public HeroesConfigRepository(HeroesRuntimeConfigDto dto)
        {
            dto ??= new HeroesRuntimeConfigDto();
            Heroes = dto.heroes ?? Array.Empty<HeroConfigDto>();
            HeroGrowth = dto.heroGrowth ?? Array.Empty<HeroGrowthConfigDto>();
            HeroUniqueSkills = dto.heroUniqueSkills ?? Array.Empty<HeroUniqueSkillConfigDto>();
            HeroSkillEffects = dto.heroSkillEffects ?? Array.Empty<HeroSkillEffectConfigDto>();

            foreach (var hero in Heroes)
                ItemsConfigRepository.AddUnique(_heroesById, hero.heroId, hero, "Heroes/heroes");
        }

        public HeroConfigDto Get(string id)
        {
            if (TryGet(id, out var hero))
                return hero;

            ItemsConfigRepository.LogMissing("Heroes", id);
            return null;
        }

        public bool TryGet(string id, out HeroConfigDto hero)
        {
            hero = null;
            return !string.IsNullOrWhiteSpace(id) && _heroesById.TryGetValue(id, out hero);
        }
    }

    public sealed class ActivitiesConfigRepository
    {
        private readonly Dictionary<string, ActivityConfigDto> _activitiesById = ItemsConfigRepository.NewIndex<ActivityConfigDto>();
        private readonly Dictionary<string, List<ActivityRequirementConfigDto>> _requirementsByActivityId = new Dictionary<string, List<ActivityRequirementConfigDto>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ActivityRewardConfigDto>> _rewardsByActivityId = new Dictionary<string, List<ActivityRewardConfigDto>>(StringComparer.Ordinal);
        private readonly Dictionary<string, WorkDetailConfigDto> _workDetailsByActivityId = ItemsConfigRepository.NewIndex<WorkDetailConfigDto>();
        private readonly Dictionary<string, OrderDetailConfigDto> _orderDetailsByActivityId = ItemsConfigRepository.NewIndex<OrderDetailConfigDto>();
        private readonly Dictionary<string, ExploreDetailConfigDto> _exploreDetailsByActivityId = ItemsConfigRepository.NewIndex<ExploreDetailConfigDto>();
        private readonly Dictionary<string, EventDetailConfigDto> _eventDetailsByActivityId = ItemsConfigRepository.NewIndex<EventDetailConfigDto>();
        private readonly Dictionary<string, CombatDetailConfigDto> _combatDetailsByActivityId = ItemsConfigRepository.NewIndex<CombatDetailConfigDto>();

        public ActivityConfigDto[] Activities { get; }
        public WorkDetailConfigDto[] WorkDetails { get; }
        public OrderDetailConfigDto[] OrderDetails { get; }
        public EventDetailConfigDto[] EventDetails { get; }
        public ExploreDetailConfigDto[] ExploreDetails { get; }
        public CombatDetailConfigDto[] CombatDetails { get; }
        public ActivityRequirementConfigDto[] Requirements { get; }
        public ActivityRewardConfigDto[] Rewards { get; }
        public ActivityTriggerConfigDto[] Triggers { get; }
        public RarityConfigDto[] Rarities { get; }
        public SkillConfigDto[] Skills { get; }
        public SkillProgressionConfigDto[] SkillsProgression { get; }
        public EnumValueConfigDto[] EnumValues { get; }
        public int Count => _activitiesById.Count;

        public ActivitiesConfigRepository(ActivitiesRuntimeConfigDto dto)
        {
            dto ??= new ActivitiesRuntimeConfigDto();
            Activities = dto.activities ?? Array.Empty<ActivityConfigDto>();
            WorkDetails = dto.workDetails ?? Array.Empty<WorkDetailConfigDto>();
            OrderDetails = dto.orderDetails ?? Array.Empty<OrderDetailConfigDto>();
            EventDetails = dto.eventDetails ?? Array.Empty<EventDetailConfigDto>();
            ExploreDetails = dto.exploreDetails ?? Array.Empty<ExploreDetailConfigDto>();
            CombatDetails = dto.combatDetails ?? Array.Empty<CombatDetailConfigDto>();
            Requirements = dto.requirements ?? Array.Empty<ActivityRequirementConfigDto>();
            Rewards = dto.rewards ?? Array.Empty<ActivityRewardConfigDto>();
            Triggers = dto.triggers ?? Array.Empty<ActivityTriggerConfigDto>();
            Rarities = dto.rarities ?? Array.Empty<RarityConfigDto>();
            Skills = dto.skills ?? Array.Empty<SkillConfigDto>();
            SkillsProgression = dto.skillsProgression ?? Array.Empty<SkillProgressionConfigDto>();
            EnumValues = dto.enumValues ?? Array.Empty<EnumValueConfigDto>();

            foreach (var activity in Activities)
                ItemsConfigRepository.AddUnique(_activitiesById, activity.id, activity, "Activities/activities");
            foreach (var requirement in Requirements)
                ItemsConfigRepository.AddGrouped(_requirementsByActivityId, requirement.activityId, requirement);
            foreach (var reward in Rewards)
                ItemsConfigRepository.AddGrouped(_rewardsByActivityId, reward.activityId, reward);
            foreach (var detail in WorkDetails)
                ItemsConfigRepository.AddUnique(_workDetailsByActivityId, detail.activityId, detail, "Activities/workDetails");
            foreach (var detail in OrderDetails)
                ItemsConfigRepository.AddUnique(_orderDetailsByActivityId, detail.activityId, detail, "Activities/orderDetails");
            foreach (var detail in ExploreDetails)
                ItemsConfigRepository.AddUnique(_exploreDetailsByActivityId, detail.activityId, detail, "Activities/exploreDetails");
            foreach (var detail in EventDetails)
                ItemsConfigRepository.AddUnique(_eventDetailsByActivityId, detail.activityId, detail, "Activities/eventDetails");
            foreach (var detail in CombatDetails)
                ItemsConfigRepository.AddUnique(_combatDetailsByActivityId, detail.activityId, detail, "Activities/combatDetails");
        }

        public ActivityConfigDto Get(string id)
        {
            if (TryGet(id, out var activity))
                return activity;

            ItemsConfigRepository.LogMissing("Activities", id);
            return null;
        }

        public bool TryGet(string id, out ActivityConfigDto activity)
        {
            activity = null;
            return !string.IsNullOrWhiteSpace(id) && _activitiesById.TryGetValue(id, out activity);
        }

        public ActivityRequirementConfigDto[] GetRequirements(string activityId) => GetGroup(_requirementsByActivityId, activityId);
        public ActivityRewardConfigDto[] GetRewards(string activityId) => GetGroup(_rewardsByActivityId, activityId);
        public WorkDetailConfigDto GetWorkDetails(string activityId) => GetSingle(_workDetailsByActivityId, activityId, "Activities/workDetails");
        public OrderDetailConfigDto GetOrderDetails(string activityId) => GetSingle(_orderDetailsByActivityId, activityId, "Activities/orderDetails");
        public ExploreDetailConfigDto GetExploreDetails(string activityId) => GetSingle(_exploreDetailsByActivityId, activityId, "Activities/exploreDetails");
        public EventDetailConfigDto GetEventDetails(string activityId) => GetSingle(_eventDetailsByActivityId, activityId, "Activities/eventDetails");
        public CombatDetailConfigDto GetCombatDetails(string activityId) => GetSingle(_combatDetailsByActivityId, activityId, "Activities/combatDetails");

        public bool TryGetWorkDetails(string activityId, out WorkDetailConfigDto details) => TryGetSingle(_workDetailsByActivityId, activityId, out details);
        public bool TryGetOrderDetails(string activityId, out OrderDetailConfigDto details) => TryGetSingle(_orderDetailsByActivityId, activityId, out details);
        public bool TryGetExploreDetails(string activityId, out ExploreDetailConfigDto details) => TryGetSingle(_exploreDetailsByActivityId, activityId, out details);
        public bool TryGetEventDetails(string activityId, out EventDetailConfigDto details) => TryGetSingle(_eventDetailsByActivityId, activityId, out details);
        public bool TryGetCombatDetails(string activityId, out CombatDetailConfigDto details) => TryGetSingle(_combatDetailsByActivityId, activityId, out details);

        private static T[] GetGroup<T>(Dictionary<string, List<T>> index, string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !index.TryGetValue(id, out var list))
                return Array.Empty<T>();

            return list.ToArray();
        }

        private static T GetSingle<T>(Dictionary<string, T> index, string id, string group)
            where T : class
        {
            if (TryGetSingle(index, id, out var value))
                return value;

            ItemsConfigRepository.LogMissing(group, id);
            return null;
        }

        private static bool TryGetSingle<T>(Dictionary<string, T> index, string id, out T value)
            where T : class
        {
            value = null;
            return !string.IsNullOrWhiteSpace(id) && index.TryGetValue(id, out value);
        }
    }

    public sealed class BuildingsConfigRepository
    {
        private readonly Dictionary<string, BuildingConfigDto> _buildingsById = ItemsConfigRepository.NewIndex<BuildingConfigDto>();

        public BuildingConfigDto[] Buildings { get; }
        public BuildingLevelConfigDto[] BuildingLevels { get; }
        public BuildActionConfigDto[] BuildActions { get; }
        public BuildingActivityConfigDto[] BuildingActivities { get; }
        public BuildingCraftableConfigDto[] BuildingCraftables { get; }
        public int Count => _buildingsById.Count;

        public BuildingsConfigRepository(BuildingsRuntimeConfigDto dto)
        {
            dto ??= new BuildingsRuntimeConfigDto();
            Buildings = dto.buildings ?? Array.Empty<BuildingConfigDto>();
            BuildingLevels = dto.buildingLevels ?? Array.Empty<BuildingLevelConfigDto>();
            BuildActions = dto.buildActions ?? Array.Empty<BuildActionConfigDto>();
            BuildingActivities = dto.buildingActivities ?? Array.Empty<BuildingActivityConfigDto>();
            BuildingCraftables = dto.buildingCraftables ?? Array.Empty<BuildingCraftableConfigDto>();

            foreach (var building in Buildings)
                ItemsConfigRepository.AddUnique(_buildingsById, building.buildingId, building, "Buildings/buildings");
        }

        public BuildingConfigDto Get(string id)
        {
            if (TryGet(id, out var building))
                return building;

            ItemsConfigRepository.LogMissing("Buildings", id);
            return null;
        }

        public bool TryGet(string id, out BuildingConfigDto building)
        {
            building = null;
            return !string.IsNullOrWhiteSpace(id) && _buildingsById.TryGetValue(id, out building);
        }
    }

    public sealed class EnemiesConfigRepository
    {
        private readonly Dictionary<string, EnemyConfigDto> _enemiesById = ItemsConfigRepository.NewIndex<EnemyConfigDto>();
        private readonly Dictionary<string, EnemyGroupConfigDto> _enemyGroupsById = ItemsConfigRepository.NewIndex<EnemyGroupConfigDto>();
        private readonly Dictionary<string, List<EnemyLootConfigDto>> _enemyLootByGroupId = new Dictionary<string, List<EnemyLootConfigDto>>(StringComparer.Ordinal);

        public EnemyConfigDto[] Enemies { get; }
        public EnemyLevelConfigDto[] EnemyLevels { get; }
        public EnemyLootConfigDto[] EnemyLoot { get; }
        public EnemyAbilityConfigDto[] EnemyAbilities { get; }
        public CombatStatusConfigDto[] CombatStatuses { get; }
        public EnemyGroupConfigDto[] EnemyGroups { get; }
        public EnumValueConfigDto[] EnumValues { get; }
        public int Count => _enemiesById.Count;

        public EnemiesConfigRepository(EnemiesRuntimeConfigDto dto)
        {
            dto ??= new EnemiesRuntimeConfigDto();
            Enemies = dto.enemies ?? Array.Empty<EnemyConfigDto>();
            EnemyLevels = dto.enemyLevels ?? Array.Empty<EnemyLevelConfigDto>();
            EnemyLoot = dto.enemyLoot ?? Array.Empty<EnemyLootConfigDto>();
            EnemyAbilities = dto.enemyAbilities ?? Array.Empty<EnemyAbilityConfigDto>();
            CombatStatuses = dto.combatStatuses ?? Array.Empty<CombatStatusConfigDto>();
            EnemyGroups = dto.enemyGroups ?? Array.Empty<EnemyGroupConfigDto>();
            EnumValues = dto.enumValues ?? Array.Empty<EnumValueConfigDto>();

            foreach (var enemy in Enemies)
                ItemsConfigRepository.AddUnique(_enemiesById, enemy.enemyId, enemy, "Enemies/enemies");
            foreach (var group in EnemyGroups)
                ItemsConfigRepository.AddUnique(_enemyGroupsById, group.enemyGroupId, group, "Enemies/enemyGroups");
            foreach (var loot in EnemyLoot)
                ItemsConfigRepository.AddGrouped(_enemyLootByGroupId, loot.lootGroupId, loot);
        }

        public EnemyConfigDto Get(string id)
        {
            if (TryGet(id, out var enemy))
                return enemy;

            ItemsConfigRepository.LogMissing("Enemies", id);
            return null;
        }

        public bool TryGet(string id, out EnemyConfigDto enemy)
        {
            enemy = null;
            return !string.IsNullOrWhiteSpace(id) && _enemiesById.TryGetValue(id, out enemy);
        }

        public EnemyGroupConfigDto GetGroup(string enemyGroupId)
        {
            if (TryGetGroup(enemyGroupId, out var group))
                return group;

            ItemsConfigRepository.LogMissing("Enemies/enemyGroups", enemyGroupId);
            return null;
        }

        public bool TryGetGroup(string enemyGroupId, out EnemyGroupConfigDto group)
        {
            group = null;
            return !string.IsNullOrWhiteSpace(enemyGroupId) && _enemyGroupsById.TryGetValue(enemyGroupId, out group);
        }

        public EnemyLootConfigDto[] GetEnemyLoot(string lootGroupId)
        {
            if (string.IsNullOrWhiteSpace(lootGroupId) || !_enemyLootByGroupId.TryGetValue(lootGroupId, out var loot))
                return Array.Empty<EnemyLootConfigDto>();

            return loot.ToArray();
        }
    }

    public sealed class FormulasConfigRepository
    {
        private readonly Dictionary<string, HeroDerivedStatConfigDto> _heroDerivedStatsByFormulaId = ItemsConfigRepository.NewIndex<HeroDerivedStatConfigDto>();
        private readonly Dictionary<string, List<SkillStatWeightConfigDto>> _skillWeightsByProfileId = new Dictionary<string, List<SkillStatWeightConfigDto>>(StringComparer.Ordinal);

        public HeroDerivedStatConfigDto[] HeroDerivedStats { get; }
        public SkillStatWeightConfigDto[] SkillStatWeights { get; }
        public int Count => HeroDerivedStats.Length + SkillStatWeights.Length;

        public FormulasConfigRepository(FormulaRuntimeConfigDto dto)
        {
            dto ??= new FormulaRuntimeConfigDto();
            HeroDerivedStats = dto.heroDerivedStats ?? Array.Empty<HeroDerivedStatConfigDto>();
            SkillStatWeights = dto.skillStatWeights ?? Array.Empty<SkillStatWeightConfigDto>();

            foreach (var formula in HeroDerivedStats)
                ItemsConfigRepository.AddUnique(_heroDerivedStatsByFormulaId, formula.formulaId, formula, "Formulas/heroDerivedStats");
            foreach (var weight in SkillStatWeights)
                ItemsConfigRepository.AddGrouped(_skillWeightsByProfileId, weight.profileId, weight);
        }

        public HeroDerivedStatConfigDto GetHeroDerivedStat(string formulaId)
        {
            if (TryGetHeroDerivedStat(formulaId, out var formula))
                return formula;

            ItemsConfigRepository.LogMissing("Formulas/heroDerivedStats", formulaId);
            return null;
        }

        public bool TryGetHeroDerivedStat(string formulaId, out HeroDerivedStatConfigDto formula)
        {
            formula = null;
            return !string.IsNullOrWhiteSpace(formulaId) && _heroDerivedStatsByFormulaId.TryGetValue(formulaId, out formula);
        }

        public SkillStatWeightConfigDto[] GetSkillWeights(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId) || !_skillWeightsByProfileId.TryGetValue(profileId, out var weights))
                return Array.Empty<SkillStatWeightConfigDto>();

            return weights.ToArray();
        }
    }

    public sealed class LootConfigRepository
    {
        private readonly Dictionary<string, LootTableConfigDto> _lootTablesById = ItemsConfigRepository.NewIndex<LootTableConfigDto>();
        private readonly Dictionary<string, List<LootTableEntryConfigDto>> _entriesByTableId = new Dictionary<string, List<LootTableEntryConfigDto>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<LootGroupConfigDto>> _groupsByTableId = new Dictionary<string, List<LootGroupConfigDto>>(StringComparer.Ordinal);

        public LootTableConfigDto[] LootTables { get; }
        public LootTableEntryConfigDto[] LootTableEntries { get; }
        public LootGroupConfigDto[] LootGroups { get; }
        public EnumValueConfigDto[] EnumValues { get; }
        public int Count => _lootTablesById.Count;

        public LootConfigRepository(LootRuntimeConfigDto dto)
        {
            dto ??= new LootRuntimeConfigDto();
            LootTables = dto.lootTables ?? Array.Empty<LootTableConfigDto>();
            LootTableEntries = dto.lootTableEntries ?? Array.Empty<LootTableEntryConfigDto>();
            LootGroups = dto.lootGroups ?? Array.Empty<LootGroupConfigDto>();
            EnumValues = dto.enumValues ?? Array.Empty<EnumValueConfigDto>();

            foreach (var table in LootTables)
                ItemsConfigRepository.AddUnique(_lootTablesById, table.lootTableId, table, "Loot/lootTables");
            foreach (var entry in LootTableEntries)
                ItemsConfigRepository.AddGrouped(_entriesByTableId, entry.lootTableId, entry);
            foreach (var group in LootGroups)
                ItemsConfigRepository.AddGrouped(_groupsByTableId, group.lootTableId, group);
        }

        public LootTableConfigDto Get(string lootTableId)
        {
            if (TryGet(lootTableId, out var table))
                return table;

            ItemsConfigRepository.LogMissing("Loot/lootTables", lootTableId);
            return null;
        }

        public bool TryGet(string lootTableId, out LootTableConfigDto table)
        {
            table = null;
            return !string.IsNullOrWhiteSpace(lootTableId) && _lootTablesById.TryGetValue(lootTableId, out table);
        }

        public LootTableEntryConfigDto[] GetEntries(string lootTableId)
        {
            if (string.IsNullOrWhiteSpace(lootTableId) || !_entriesByTableId.TryGetValue(lootTableId, out var entries))
                return Array.Empty<LootTableEntryConfigDto>();

            return entries.ToArray();
        }

        public LootGroupConfigDto[] GetGroups(string lootTableId)
        {
            if (string.IsNullOrWhiteSpace(lootTableId) || !_groupsByTableId.TryGetValue(lootTableId, out var groups))
                return Array.Empty<LootGroupConfigDto>();

            return groups.ToArray();
        }
    }

    public sealed class MapConfigRepository
    {
        private readonly Dictionary<string, MapCellConfigDto> _cellsById = ItemsConfigRepository.NewIndex<MapCellConfigDto>();
        private readonly Dictionary<string, MapLocationConfigDto> _locationsById = ItemsConfigRepository.NewIndex<MapLocationConfigDto>();
        private readonly Dictionary<string, MapCellActivityConfigDto> _cellActivitiesByActivityId = ItemsConfigRepository.NewIndex<MapCellActivityConfigDto>();

        public MapCellConfigDto[] MapCells { get; }
        public MapLocationConfigDto[] MapLocations { get; }
        public MapExplorationLevelConfigDto[] MapExplorationLevels { get; }
        public MapCellActivityConfigDto[] MapCellActivities { get; }
        public DangerEncounterConfigDto[] DangerEncounters { get; }
        public EnumValueConfigDto[] EnumValues { get; }
        public int Count => MapCells.Length + MapLocations.Length;

        public MapConfigRepository(MapRuntimeConfigDto dto)
        {
            dto ??= new MapRuntimeConfigDto();
            MapCells = dto.mapCells ?? Array.Empty<MapCellConfigDto>();
            MapLocations = dto.mapLocations ?? Array.Empty<MapLocationConfigDto>();
            MapExplorationLevels = dto.mapExplorationLevels ?? Array.Empty<MapExplorationLevelConfigDto>();
            MapCellActivities = dto.mapCellActivities ?? Array.Empty<MapCellActivityConfigDto>();
            DangerEncounters = dto.dangerEncounters ?? Array.Empty<DangerEncounterConfigDto>();
            EnumValues = dto.enumValues ?? Array.Empty<EnumValueConfigDto>();

            foreach (var cell in MapCells)
                ItemsConfigRepository.AddUnique(_cellsById, cell.cellId, cell, "Map/mapCells");
            foreach (var location in MapLocations)
                ItemsConfigRepository.AddUnique(_locationsById, location.locationId, location, "Map/mapLocations");
            foreach (var activity in MapCellActivities)
                ItemsConfigRepository.AddUnique(_cellActivitiesByActivityId, activity.activityId, activity, "Map/mapCellActivities");
        }

        public MapCellConfigDto GetCell(string cellId)
        {
            if (TryGetCell(cellId, out var cell))
                return cell;

            ItemsConfigRepository.LogMissing("Map/mapCells", cellId);
            return null;
        }

        public bool TryGetCell(string cellId, out MapCellConfigDto cell)
        {
            cell = null;
            return !string.IsNullOrWhiteSpace(cellId) && _cellsById.TryGetValue(cellId, out cell);
        }

        public MapLocationConfigDto GetLocation(string locationId)
        {
            if (TryGetLocation(locationId, out var location))
                return location;

            ItemsConfigRepository.LogMissing("Map/mapLocations", locationId);
            return null;
        }

        public bool TryGetLocation(string locationId, out MapLocationConfigDto location)
        {
            location = null;
            return !string.IsNullOrWhiteSpace(locationId) && _locationsById.TryGetValue(locationId, out location);
        }

        public MapCellActivityConfigDto GetCellActivityByActivityId(string activityId)
        {
            if (TryGetCellActivityByActivityId(activityId, out var activity))
                return activity;

            ItemsConfigRepository.LogMissing("Map/mapCellActivities", activityId);
            return null;
        }

        public bool TryGetCellActivityByActivityId(string activityId, out MapCellActivityConfigDto activity)
        {
            activity = null;
            return !string.IsNullOrWhiteSpace(activityId) && _cellActivitiesByActivityId.TryGetValue(activityId, out activity);
        }
    }

    public sealed class StorageConfigRepository
    {
        private readonly Dictionary<string, StorageRuleConfigDto> _rulesById = ItemsConfigRepository.NewIndex<StorageRuleConfigDto>();
        private readonly Dictionary<string, ItemStateConfigDto> _statesById = ItemsConfigRepository.NewIndex<ItemStateConfigDto>();
        private readonly Dictionary<string, StorageBuildingConfigDto> _buildingsByKey = ItemsConfigRepository.NewIndex<StorageBuildingConfigDto>();

        public StorageRuleConfigDto[] StorageRules { get; }
        public StorageBuildingConfigDto[] StorageBuildings { get; }
        public ItemStateConfigDto[] ItemStates { get; }
        public EnumValueConfigDto[] EnumValues { get; }
        public int Count => StorageRules.Length + StorageBuildings.Length + ItemStates.Length;

        public StorageConfigRepository(StorageRuntimeConfigDto dto)
        {
            dto ??= new StorageRuntimeConfigDto();
            StorageRules = dto.storageRules ?? Array.Empty<StorageRuleConfigDto>();
            StorageBuildings = dto.storageBuildings ?? Array.Empty<StorageBuildingConfigDto>();
            ItemStates = dto.itemStates ?? Array.Empty<ItemStateConfigDto>();
            EnumValues = dto.enumValues ?? Array.Empty<EnumValueConfigDto>();

            foreach (var rule in StorageRules)
                ItemsConfigRepository.AddUnique(_rulesById, rule.storageRuleId, rule, "Storage/storageRules");
            foreach (var state in ItemStates)
                ItemsConfigRepository.AddUnique(_statesById, state.stateId, state, "Storage/itemStates");
            foreach (var building in StorageBuildings)
                ItemsConfigRepository.AddUnique(_buildingsByKey, BuildingKey(building.buildingId, building.level), building, "Storage/storageBuildings");
        }

        public StorageRuleConfigDto GetRule(string storageRuleId)
        {
            if (TryGetRule(storageRuleId, out var rule))
                return rule;

            ItemsConfigRepository.LogMissing("Storage/storageRules", storageRuleId);
            return null;
        }

        public bool TryGetRule(string storageRuleId, out StorageRuleConfigDto rule)
        {
            rule = null;
            return !string.IsNullOrWhiteSpace(storageRuleId) && _rulesById.TryGetValue(storageRuleId, out rule);
        }

        public ItemStateConfigDto GetItemState(string stateId)
        {
            if (TryGetItemState(stateId, out var state))
                return state;

            ItemsConfigRepository.LogMissing("Storage/itemStates", stateId);
            return null;
        }

        public bool TryGetItemState(string stateId, out ItemStateConfigDto state)
        {
            state = null;
            return !string.IsNullOrWhiteSpace(stateId) && _statesById.TryGetValue(stateId, out state);
        }

        public StorageBuildingConfigDto GetBuilding(string buildingId, int level)
        {
            if (TryGetBuilding(buildingId, level, out var building))
                return building;

            ItemsConfigRepository.LogMissing("Storage/storageBuildings", BuildingKey(buildingId, level));
            return null;
        }

        public bool TryGetBuilding(string buildingId, int level, out StorageBuildingConfigDto building)
        {
            building = null;
            return !string.IsNullOrWhiteSpace(buildingId) && _buildingsByKey.TryGetValue(BuildingKey(buildingId, level), out building);
        }

        private static string BuildingKey(string buildingId, int level) => $"{buildingId}:{level}";
    }

    public sealed class LocalisationConfigRepository
    {
        private readonly Dictionary<string, LocalisationEntryDto> _entriesById = ItemsConfigRepository.NewIndex<LocalisationEntryDto>();

        public LocalisationEntryDto[] Localisations { get; }
        public int Count => _entriesById.Count;

        public LocalisationConfigRepository(LocalisationRuntimeConfigDto dto)
        {
            dto ??= new LocalisationRuntimeConfigDto();
            Localisations = dto.localisations ?? Array.Empty<LocalisationEntryDto>();

            foreach (var entry in Localisations)
                ItemsConfigRepository.AddUnique(_entriesById, entry.id, entry, "Localisation/localisations");
        }

        public bool TryGet(string id, out LocalisationEntryDto entry)
        {
            entry = null;
            return !string.IsNullOrWhiteSpace(id) && _entriesById.TryGetValue(id, out entry);
        }
    }
}
