using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly Dictionary<string, CraftDefinitionConfigDto> _craftDefinitionsById = NewIndex<CraftDefinitionConfigDto>();
        private readonly Dictionary<string, ConsumableConfigDto> _consumablesById = NewIndex<ConsumableConfigDto>();
        private readonly Dictionary<string, CurrencyConfigDto> _currenciesById = NewIndex<CurrencyConfigDto>();

        public ResourceConfigDto[] Resources { get; }
        public EquipmentWeaponConfigDto[] EquipmentWeapons { get; }
        public EquipmentArmorConfigDto[] EquipmentArmor { get; }
        public RecipeConfigDto[] Recipes { get; }
        public CraftDefinitionConfigDto[] CraftDefinitions { get; }
        public ConsumableConfigDto[] Consumables { get; }
        public CurrencyConfigDto[] Currencies { get; }

        public int ItemCount => _itemsById.Count;
        public int CurrencyCount => _currenciesById.Count;

        public ItemsConfigRepository(ItemsRuntimeConfigDto dto)
        {
            dto ??= new ItemsRuntimeConfigDto();
            Resources = dto.resources ?? Array.Empty<ResourceConfigDto>();
            EquipmentWeapons = dto.equipmentWeapons ?? Array.Empty<EquipmentWeaponConfigDto>();
            EquipmentArmor = dto.equipmentArmor ?? Array.Empty<EquipmentArmorConfigDto>();
            Recipes = dto.recipes ?? Array.Empty<RecipeConfigDto>();
            CraftDefinitions = dto.craftDefinitions ?? Array.Empty<CraftDefinitionConfigDto>();
            Consumables = dto.consumables ?? Array.Empty<ConsumableConfigDto>();
            Currencies = dto.currencies ?? Array.Empty<CurrencyConfigDto>();

            AddItems(Resources, item => item.id, "Items/resources", _resourcesById, _itemsById);
            AddItems(EquipmentWeapons, item => item.id, "Items/equipmentWeapons", _equipmentWeaponsById, _itemsById);
            AddItems(EquipmentArmor, item => item.id, "Items/equipmentArmor", _equipmentArmorById, _itemsById);
            AddItems(Recipes, item => item.id, "Items/recipes", _recipesById, _itemsById);
            AddItems(CraftDefinitions, item => item.craftId, "Items/craftDefinitions", _craftDefinitionsById);
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
        public bool TryGetCraftDefinition(string craftId, out CraftDefinitionConfigDto definition) => TryGetIndexed(_craftDefinitionsById, craftId, out definition);
        public bool TryGetConsumable(string id, out ConsumableConfigDto consumable) => TryGetIndexed(_consumablesById, id, out consumable);

        public CraftDefinitionConfigDto GetCraftDefinition(string craftId)
        {
            if (TryGetCraftDefinition(craftId, out var definition))
                return definition;

            LogMissing("Items/craftDefinitions", craftId);
            return null;
        }

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
        private readonly Dictionary<string, List<HeroSkillEffectConfigDto>> _effectsByTrigger = new Dictionary<string, List<HeroSkillEffectConfigDto>>(StringComparer.OrdinalIgnoreCase);

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
            foreach (var effect in HeroSkillEffects)
                ItemsConfigRepository.AddGrouped(_effectsByTrigger, effect?.trigger, effect);
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

        public HeroSkillEffectConfigDto[] GetEffectsByTrigger(string trigger)
        {
            return string.IsNullOrWhiteSpace(trigger) || !_effectsByTrigger.TryGetValue(trigger, out var values)
                ? Array.Empty<HeroSkillEffectConfigDto>()
                : values.ToArray();
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
        private readonly Dictionary<string, DangerEncounterConfigDto> _dangerEncountersById = ItemsConfigRepository.NewIndex<DangerEncounterConfigDto>();
        private readonly Dictionary<string, List<DangerEncounterConfigDto>> _dangerEncountersByActivityId = new Dictionary<string, List<DangerEncounterConfigDto>>(StringComparer.Ordinal);

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
        public DangerEncounterConfigDto[] DangerEncounters { get; }
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
            DangerEncounters = dto.dangerEncounters ?? Array.Empty<DangerEncounterConfigDto>();

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
            foreach (var encounter in DangerEncounters)
            {
                ItemsConfigRepository.AddUnique(_dangerEncountersById, encounter.dangerEncounterId, encounter, "Activities/dangerEncounters");
                ItemsConfigRepository.AddGrouped(_dangerEncountersByActivityId, encounter.activityId, encounter);
            }
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
        public DangerEncounterConfigDto[] GetDangerEncounters(string activityId) => GetGroup(_dangerEncountersByActivityId, activityId);
        public WorkDetailConfigDto GetWorkDetails(string activityId) => GetSingle(_workDetailsByActivityId, activityId, "Activities/workDetails");
        public OrderDetailConfigDto GetOrderDetails(string activityId) => GetSingle(_orderDetailsByActivityId, activityId, "Activities/orderDetails");
        public ExploreDetailConfigDto GetExploreDetails(string activityId) => GetSingle(_exploreDetailsByActivityId, activityId, "Activities/exploreDetails");
        public EventDetailConfigDto GetEventDetails(string activityId) => GetSingle(_eventDetailsByActivityId, activityId, "Activities/eventDetails");
        public CombatDetailConfigDto GetCombatDetails(string activityId) => GetSingle(_combatDetailsByActivityId, activityId, "Activities/combatDetails");
        public DangerEncounterConfigDto GetDangerEncounter(string dangerEncounterId) => GetSingle(_dangerEncountersById, dangerEncounterId, "Activities/dangerEncounters");

        public bool TryGetWorkDetails(string activityId, out WorkDetailConfigDto details) => TryGetSingle(_workDetailsByActivityId, activityId, out details);
        public bool TryGetOrderDetails(string activityId, out OrderDetailConfigDto details) => TryGetSingle(_orderDetailsByActivityId, activityId, out details);
        public bool TryGetExploreDetails(string activityId, out ExploreDetailConfigDto details) => TryGetSingle(_exploreDetailsByActivityId, activityId, out details);
        public bool TryGetEventDetails(string activityId, out EventDetailConfigDto details) => TryGetSingle(_eventDetailsByActivityId, activityId, out details);
        public bool TryGetCombatDetails(string activityId, out CombatDetailConfigDto details) => TryGetSingle(_combatDetailsByActivityId, activityId, out details);
        public bool TryGetDangerEncounter(string dangerEncounterId, out DangerEncounterConfigDto encounter) => TryGetSingle(_dangerEncountersById, dangerEncounterId, out encounter);

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

    public sealed class QuestConfigRepository
    {
        private readonly Dictionary<string, StageConfigDto> _stagesById = ItemsConfigRepository.NewIndex<StageConfigDto>();
        private readonly Dictionary<string, QuestDefinition> _definitionsById = ItemsConfigRepository.NewIndex<QuestDefinition>();
        private readonly Dictionary<string, List<StageQuestConfigDto>> _stageQuestsByStageId = new Dictionary<string, List<StageQuestConfigDto>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<QuestStartConditionConfigDto>> _conditionsByQuestId = new Dictionary<string, List<QuestStartConditionConfigDto>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<QuestStepConfigDto>> _stepsByQuestId = new Dictionary<string, List<QuestStepConfigDto>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<QuestRewardConfigDto>> _rewardsByQuestId = new Dictionary<string, List<QuestRewardConfigDto>>(StringComparer.Ordinal);

        public StageConfigDto[] Stages { get; }
        public StageQuestConfigDto[] StageQuests { get; }
        public StoryQuestConfigDto[] StoryQuests { get; }
        public DailyQuestConfigDto[] DailyQuests { get; }
        public QuestStartConditionConfigDto[] QuestStartConditions { get; }
        public QuestStepConfigDto[] QuestSteps { get; }
        public QuestRewardConfigDto[] QuestRewards { get; }
        public EnumValueConfigDto[] EnumValues { get; }
        public QuestDefinition[] Definitions { get; }
        public int Count => _definitionsById.Count;

        public QuestConfigRepository(QuestRuntimeConfigDto dto)
        {
            dto ??= new QuestRuntimeConfigDto();
            Stages = dto.stages ?? Array.Empty<StageConfigDto>();
            StageQuests = dto.stageQuests ?? Array.Empty<StageQuestConfigDto>();
            StoryQuests = dto.storyQuests ?? Array.Empty<StoryQuestConfigDto>();
            DailyQuests = dto.dailyQuests ?? Array.Empty<DailyQuestConfigDto>();
            QuestStartConditions = dto.questStartConditions ?? Array.Empty<QuestStartConditionConfigDto>();
            QuestSteps = dto.questSteps ?? Array.Empty<QuestStepConfigDto>();
            QuestRewards = dto.questRewards ?? Array.Empty<QuestRewardConfigDto>();
            EnumValues = dto.enumValues ?? Array.Empty<EnumValueConfigDto>();

            foreach (var stage in Stages)
                ItemsConfigRepository.AddUnique(_stagesById, stage?.stageId, stage, "Quests/stages");
            foreach (var quest in StoryQuests)
                AddDefinition(ToDefinition(quest));
            foreach (var quest in DailyQuests)
                AddDefinition(ToDefinition(quest));
            foreach (var relation in StageQuests)
                ItemsConfigRepository.AddGrouped(_stageQuestsByStageId, relation?.stageId, relation);
            foreach (var condition in QuestStartConditions)
                ItemsConfigRepository.AddGrouped(_conditionsByQuestId, condition?.questId, condition);
            foreach (var step in QuestSteps)
                ItemsConfigRepository.AddGrouped(_stepsByQuestId, step?.questId, step);
            foreach (var reward in QuestRewards)
                ItemsConfigRepository.AddGrouped(_rewardsByQuestId, reward?.questId, reward);

            Sort(_stageQuestsByStageId, value => value.sortOrder);
            Sort(_conditionsByQuestId, value => value.sortOrder);
            Sort(_stepsByQuestId, value => value.stepOrder);
            Sort(_rewardsByQuestId, value => value.sortOrder);
            Definitions = _definitionsById.Values.OrderBy(value => value.SortOrder).ThenBy(value => value.QuestId, StringComparer.Ordinal).ToArray();
        }

        public bool TryGetStage(string stageId, out StageConfigDto stage) => TryGet(_stagesById, stageId, out stage);
        public bool TryGetDefinition(string questId, out QuestDefinition definition) => TryGet(_definitionsById, questId, out definition);
        public StageQuestConfigDto[] GetStageQuests(string stageId) => GetGroup(_stageQuestsByStageId, stageId);
        public QuestStartConditionConfigDto[] GetStartConditions(string questId) => GetGroup(_conditionsByQuestId, questId);
        public QuestStepConfigDto[] GetSteps(string questId) => GetGroup(_stepsByQuestId, questId);
        public QuestRewardConfigDto[] GetRewards(string questId) => GetGroup(_rewardsByQuestId, questId);

        private void AddDefinition(QuestDefinition definition)
        {
            if (definition != null)
                ItemsConfigRepository.AddUnique(_definitionsById, definition.QuestId, definition, "Quests/definitions");
        }

        private static QuestDefinition ToDefinition(StoryQuestConfigDto value) => value == null ? null : new QuestDefinition
        {
            QuestId = value.questId, NameId = value.nameId, DescriptionId = value.descriptionId, IconId = value.iconId,
            JournalCategory = value.journalCategory, SortOrder = value.sortOrder, IsTutorial = value.isTutorial,
            CloseOnStageComplete = value.closeOnStageComplete, Enabled = value.enabled, Kind = QuestDefinitionKind.Story
        };

        private static QuestDefinition ToDefinition(DailyQuestConfigDto value) => value == null ? null : new QuestDefinition
        {
            QuestId = value.questId, NameId = value.nameId, DescriptionId = value.descriptionId, IconId = value.iconId,
            JournalCategory = value.journalCategory, SortOrder = value.sortOrder, Enabled = value.enabled,
            Kind = QuestDefinitionKind.Daily, DailyPoolId = value.dailyPoolId, SelectionWeight = value.selectionWeight
        };

        private static bool TryGet<T>(Dictionary<string, T> index, string id, out T value) where T : class
        {
            value = null;
            return !string.IsNullOrWhiteSpace(id) && index.TryGetValue(id, out value);
        }

        private static T[] GetGroup<T>(Dictionary<string, List<T>> index, string id)
        {
            return !string.IsNullOrWhiteSpace(id) && index.TryGetValue(id, out var values) ? values.ToArray() : Array.Empty<T>();
        }

        private static void Sort<T>(Dictionary<string, List<T>> index, Func<T, int> getOrder)
        {
            foreach (var values in index.Values)
                values.Sort((left, right) => getOrder(left).CompareTo(getOrder(right)));
        }
    }

    public sealed class BuildingsConfigRepository
    {
        private readonly Dictionary<string, BuildingConfigDto> _buildingsById = ItemsConfigRepository.NewIndex<BuildingConfigDto>();
        private readonly Dictionary<string, BuildingLevelConfigDto> _buildingLevelsByIdAndLevel = ItemsConfigRepository.NewIndex<BuildingLevelConfigDto>();
        private readonly Dictionary<string, BuildActionConfigDto> _buildActionsById = ItemsConfigRepository.NewIndex<BuildActionConfigDto>();
        private readonly Dictionary<string, List<BuildingLevelConfigDto>> _levelsBySourceActivityId = new Dictionary<string, List<BuildingLevelConfigDto>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<SettlementStageStarterHeroConfigDto>> _starterHeroesByStageId = new Dictionary<string, List<SettlementStageStarterHeroConfigDto>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<SettlementStageStarterEquipmentConfigDto>> _starterEquipmentByStageId = new Dictionary<string, List<SettlementStageStarterEquipmentConfigDto>>(StringComparer.Ordinal);

        public BuildingConfigDto[] Buildings { get; }
        public BuildingLevelConfigDto[] BuildingLevels { get; }
        public BuildActionConfigDto[] BuildActions { get; }
        public BuildingActivityConfigDto[] BuildingActivities { get; }
        public BuildingCraftableConfigDto[] BuildingCraftables { get; }
        public SettlementStageSlotConfigDto[] SettlementStageSlots { get; }
        public SettlementStageStarterHeroConfigDto[] SettlementStageStarterHeroes { get; }
        public SettlementStageStarterEquipmentConfigDto[] SettlementStageStarterEquipment { get; }
        public int Count => _buildingsById.Count;

        public BuildingsConfigRepository(BuildingsRuntimeConfigDto dto)
        {
            dto ??= new BuildingsRuntimeConfigDto();
            Buildings = dto.buildings ?? Array.Empty<BuildingConfigDto>();
            BuildingLevels = dto.buildingLevels ?? Array.Empty<BuildingLevelConfigDto>();
            BuildActions = dto.buildActions ?? Array.Empty<BuildActionConfigDto>();
            BuildingActivities = dto.buildingActivities ?? Array.Empty<BuildingActivityConfigDto>();
            BuildingCraftables = dto.buildingCraftables ?? Array.Empty<BuildingCraftableConfigDto>();
            SettlementStageSlots = dto.settlementStageSlots ?? Array.Empty<SettlementStageSlotConfigDto>();
            SettlementStageStarterHeroes = dto.settlementStageStarterHeroes ?? Array.Empty<SettlementStageStarterHeroConfigDto>();
            SettlementStageStarterEquipment = dto.settlementStageStarterEquipment ?? Array.Empty<SettlementStageStarterEquipmentConfigDto>();

            foreach (var building in Buildings)
                ItemsConfigRepository.AddUnique(_buildingsById, building.buildingId, building, "Buildings/buildings");
            foreach (var level in BuildingLevels)
            {
                ItemsConfigRepository.AddUnique(_buildingLevelsByIdAndLevel, BuildingLevelKey(level.buildingId, level.level), level, "Buildings/buildingLevels");
                ItemsConfigRepository.AddGrouped(_levelsBySourceActivityId, level?.sourceActivityId, level);
            }
            foreach (var action in BuildActions)
                ItemsConfigRepository.AddUnique(_buildActionsById, action.id, action, "Buildings/buildActions");
            foreach (var starterHero in SettlementStageStarterHeroes)
                AddStageValue(_starterHeroesByStageId, starterHero?.stageId, starterHero);
            foreach (var starterEquipment in SettlementStageStarterEquipment)
                AddStageValue(_starterEquipmentByStageId, starterEquipment?.stageId, starterEquipment);

            SortStageValues(_starterHeroesByStageId, value => value.sortOrder);
            SortStageValues(_starterEquipmentByStageId, value => value.sortOrder);
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

        public BuildingLevelConfigDto GetBuildingLevel(string buildingId, int level)
        {
            if (TryGetBuildingLevel(buildingId, level, out var buildingLevel))
                return buildingLevel;

            ItemsConfigRepository.LogMissing("Buildings/buildingLevels", BuildingLevelKey(buildingId, level));
            return null;
        }

        public bool TryGetBuildingLevel(string buildingId, int level, out BuildingLevelConfigDto buildingLevel)
        {
            buildingLevel = null;
            return !string.IsNullOrWhiteSpace(buildingId) &&
                   level >= 0 &&
                   _buildingLevelsByIdAndLevel.TryGetValue(BuildingLevelKey(buildingId, level), out buildingLevel);
        }

        public BuildActionConfigDto GetBuildAction(string actionId)
        {
            if (TryGetBuildAction(actionId, out var action))
                return action;
            ItemsConfigRepository.LogMissing("Buildings/buildActions", actionId);
            return null;
        }

        public bool TryGetBuildAction(string actionId, out BuildActionConfigDto action)
        {
            action = null;
            return !string.IsNullOrWhiteSpace(actionId) && _buildActionsById.TryGetValue(actionId, out action);
        }

        public BuildingLevelConfigDto[] GetLevelsBySourceActivity(string activityId)
        {
            return string.IsNullOrWhiteSpace(activityId) || !_levelsBySourceActivityId.TryGetValue(activityId, out var levels)
                ? Array.Empty<BuildingLevelConfigDto>()
                : levels.ToArray();
        }

        public SettlementStageStarterHeroConfigDto[] GetSettlementStageStarterHeroes(string stageId)
        {
            return GetStageValues(_starterHeroesByStageId, stageId);
        }

        public SettlementStageStarterEquipmentConfigDto[] GetSettlementStageStarterEquipment(string stageId)
        {
            return GetStageValues(_starterEquipmentByStageId, stageId);
        }

        private static void AddStageValue<T>(Dictionary<string, List<T>> index, string stageId, T value)
            where T : class
        {
            if (value == null || string.IsNullOrWhiteSpace(stageId))
                return;

            if (!index.TryGetValue(stageId, out var values))
            {
                values = new List<T>();
                index[stageId] = values;
            }

            values.Add(value);
        }

        private static void SortStageValues<T>(Dictionary<string, List<T>> index, Func<T, int> getSortOrder)
        {
            foreach (var values in index.Values)
                values.Sort((left, right) => getSortOrder(left).CompareTo(getSortOrder(right)));
        }

        private static T[] GetStageValues<T>(Dictionary<string, List<T>> index, string stageId)
        {
            if (string.IsNullOrWhiteSpace(stageId) || !index.TryGetValue(stageId, out var values))
                return Array.Empty<T>();

            return values.ToArray();
        }

        private static string BuildingLevelKey(string buildingId, int level)
        {
            return $"{buildingId}:{level}";
        }
    }

    public sealed class EnemiesConfigRepository
    {
        private readonly Dictionary<string, EnemyConfigDto> _enemiesById = ItemsConfigRepository.NewIndex<EnemyConfigDto>();
        private readonly Dictionary<string, EnemyAbilityConfigDto> _enemyAbilitiesById = ItemsConfigRepository.NewIndex<EnemyAbilityConfigDto>();
        private readonly Dictionary<string, CombatStatusConfigDto> _combatStatusesById = ItemsConfigRepository.NewIndex<CombatStatusConfigDto>();
        private readonly Dictionary<string, List<EnemyGroupConfigDto>> _enemyGroupsById = new Dictionary<string, List<EnemyGroupConfigDto>>(StringComparer.Ordinal);
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
            foreach (var ability in EnemyAbilities)
                ItemsConfigRepository.AddUnique(_enemyAbilitiesById, ability.abilityId, ability, "Enemies/enemyAbilities");
            foreach (var status in CombatStatuses)
                ItemsConfigRepository.AddUnique(_combatStatusesById, status.statusId, status, "Enemies/combatStatuses");
            foreach (var group in EnemyGroups)
                ItemsConfigRepository.AddGrouped(_enemyGroupsById, group.enemyGroupId, group);
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

        public bool TryGetAbility(string id, out EnemyAbilityConfigDto ability)
        {
            ability = null;
            return !string.IsNullOrWhiteSpace(id) && _enemyAbilitiesById.TryGetValue(id, out ability);
        }

        public bool TryGetCombatStatus(string id, out CombatStatusConfigDto status)
        {
            status = null;
            return !string.IsNullOrWhiteSpace(id) && _combatStatusesById.TryGetValue(id, out status);
        }

        public EnemyGroupConfigDto[] GetGroup(string enemyGroupId)
        {
            if (TryGetGroup(enemyGroupId, out var group))
                return group;

            ItemsConfigRepository.LogMissing("Enemies/enemyGroups", enemyGroupId);
            return Array.Empty<EnemyGroupConfigDto>();
        }

        public bool TryGetGroup(string enemyGroupId, out EnemyGroupConfigDto[] group)
        {
            group = Array.Empty<EnemyGroupConfigDto>();
            if (string.IsNullOrWhiteSpace(enemyGroupId) || !_enemyGroupsById.TryGetValue(enemyGroupId, out var entries))
                return false;

            group = entries.OrderBy(entry => entry.sortOrder).ToArray();
            return true;
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
        private readonly Dictionary<string, FormulaConfigDto> _formulasById = ItemsConfigRepository.NewIndex<FormulaConfigDto>();
        private readonly Dictionary<string, List<SkillStatWeightConfigDto>> _skillWeightsByProfileId = new Dictionary<string, List<SkillStatWeightConfigDto>>(StringComparer.Ordinal);

        public FormulaConfigDto[] Formulas { get; }
        public SkillStatWeightConfigDto[] SkillStatWeights { get; }
        public int Count => Formulas.Length + SkillStatWeights.Length;

        public FormulasConfigRepository(FormulaRuntimeConfigDto dto)
        {
            dto ??= new FormulaRuntimeConfigDto();
            Formulas = dto.formulas ?? Array.Empty<FormulaConfigDto>();
            SkillStatWeights = dto.skillStatWeights ?? Array.Empty<SkillStatWeightConfigDto>();

            foreach (var formula in Formulas)
                ItemsConfigRepository.AddUnique(_formulasById, formula.formulaId, formula, "Formulas/formulas");
            foreach (var weight in SkillStatWeights)
                ItemsConfigRepository.AddGrouped(_skillWeightsByProfileId, weight.profileId, weight);
        }

        public FormulaConfigDto GetFormula(string formulaId)
        {
            if (TryGetFormula(formulaId, out var formula))
                return formula;

            ItemsConfigRepository.LogMissing("Formulas/formulas", formulaId);
            return null;
        }

        public bool TryGetFormula(string formulaId, out FormulaConfigDto formula)
        {
            formula = null;
            return !string.IsNullOrWhiteSpace(formulaId) && _formulasById.TryGetValue(formulaId, out formula);
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
        public EnumValueConfigDto[] EnumValues { get; }
        public int Count => MapCells.Length + MapLocations.Length;

        public MapConfigRepository(MapRuntimeConfigDto dto)
        {
            dto ??= new MapRuntimeConfigDto();
            MapCells = dto.mapCells ?? Array.Empty<MapCellConfigDto>();
            MapLocations = dto.mapLocations ?? Array.Empty<MapLocationConfigDto>();
            MapExplorationLevels = dto.mapExplorationLevels ?? Array.Empty<MapExplorationLevelConfigDto>();
            MapCellActivities = dto.mapCellActivities ?? Array.Empty<MapCellActivityConfigDto>();
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
        private readonly Dictionary<string, StorageRuleConfigDto> _rulesByItemKind = ItemsConfigRepository.NewIndex<StorageRuleConfigDto>();
        private readonly Dictionary<string, ItemStateConfigDto> _statesById = ItemsConfigRepository.NewIndex<ItemStateConfigDto>();
        private readonly Dictionary<string, ItemStateConfigDto> _workingStatesByAvailabilityMode = ItemsConfigRepository.NewIndex<ItemStateConfigDto>();
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
            {
                ItemsConfigRepository.AddUnique(_rulesById, rule.storageRuleId, rule, "Storage/storageRules");
                ItemsConfigRepository.AddUnique(_rulesByItemKind, rule.itemKind, rule, "Storage/storageRules/itemKind");
            }
            foreach (var state in ItemStates)
            {
                ItemsConfigRepository.AddUnique(_statesById, state.stateId, state, "Storage/itemStates");
                if (state != null && !string.Equals(state.availabilityMode, "unavailable", StringComparison.OrdinalIgnoreCase))
                    ItemsConfigRepository.AddUnique(_workingStatesByAvailabilityMode, state.availabilityMode, state, "Storage/itemStates/availabilityMode");
            }
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

        public bool TryGetRuleForItemKind(string itemKind, out StorageRuleConfigDto rule)
        {
            rule = null;
            return !string.IsNullOrWhiteSpace(itemKind) && _rulesByItemKind.TryGetValue(itemKind, out rule);
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

        public bool TryGetWorkingItemStateByAvailabilityMode(string availabilityMode, out ItemStateConfigDto state)
        {
            state = null;
            return !string.IsNullOrWhiteSpace(availabilityMode) && _workingStatesByAvailabilityMode.TryGetValue(availabilityMode, out state);
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
