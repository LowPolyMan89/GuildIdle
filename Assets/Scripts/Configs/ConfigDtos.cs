using System;

namespace GuildIdle.Configs
{
    public interface IItemConfig
    {
        string Id { get; }
        string NameId { get; }
        string DescriptionId { get; }
        string IconId { get; }
        string Kind { get; }
    }

    [Serializable]
    public sealed class MaterialCostDto
    {
        public string id;
        public int count;
    }

    [Serializable]
    public sealed class RequiredBuildingDto
    {
        public string buildingId;
        public int level;
    }

    [Serializable]
    public sealed class RequiredSkillDto
    {
        public string skillId;
        public int level;
    }

    [Serializable]
    public sealed class RequiredActivityDto
    {
        public string activityId;
        public int count;
    }

    [Serializable]
    public sealed class HeroBaseStatsDto
    {
        public int strength;
        public int agility;
        public int intelligence;
        public int luck;
        public int endurance;
    }

    [Serializable]
    public sealed class ItemsRuntimeConfigDto
    {
        public ResourceConfigDto[] resources = Array.Empty<ResourceConfigDto>();
        public EquipmentWeaponConfigDto[] equipmentWeapons = Array.Empty<EquipmentWeaponConfigDto>();
        public EquipmentArmorConfigDto[] equipmentArmor = Array.Empty<EquipmentArmorConfigDto>();
        public RecipeConfigDto[] recipes = Array.Empty<RecipeConfigDto>();
        public CraftDefinitionConfigDto[] craftDefinitions = Array.Empty<CraftDefinitionConfigDto>();
        public ConsumableConfigDto[] consumables = Array.Empty<ConsumableConfigDto>();
        public CurrencyConfigDto[] currencies = Array.Empty<CurrencyConfigDto>();
    }

    [Serializable]
    public sealed class ResourceConfigDto : IItemConfig
    {
        public string id;
        public string nameId;
        public string descriptionId;
        public string iconId;
        public string kind;
        public string subtype;
        public string rarityId;
        public int tier;
        public string craftStationId;
        public int craftDurationSec;
        public string craftSkillId;
        public RequiredBuildingDto[] requiredBuildings = Array.Empty<RequiredBuildingDto>();
        public RequiredSkillDto[] requiredSkills = Array.Empty<RequiredSkillDto>();
        public string visibilityItemId;
        public int visibilityItemCount;
        public bool consumeVisibilityItem;
        public bool hiddenUntilVisibilityItem;
        public int outputCount;
        public MaterialCostDto[] materials = Array.Empty<MaterialCostDto>();
        public string sourceActivityId;
        public int skillExp;

        public string Id => id;
        public string NameId => nameId;
        public string DescriptionId => descriptionId;
        public string IconId => iconId;
        public string Kind => kind;
    }

    [Serializable]
    public sealed class EquipmentWeaponConfigDto : IItemConfig
    {
        public string id;
        public string nameId;
        public string descriptionId;
        public string iconId;
        public string kind;
        public string subtype;
        public string equipmentSlot;
        public string rarityId;
        public int tier;
        public string craftStationId;
        public int craftDurationSec;
        public string craftSkillId;
        public string craftMainStatId;
        public RequiredBuildingDto[] requiredBuildings = Array.Empty<RequiredBuildingDto>();
        public RequiredSkillDto[] requiredSkills = Array.Empty<RequiredSkillDto>();
        public string visibilityItemId;
        public int visibilityItemCount;
        public bool consumeVisibilityItem;
        public bool hiddenUntilVisibilityItem;
        public int outputCount;
        public MaterialCostDto[] materials = Array.Empty<MaterialCostDto>();
        public string sourceActivityId;
        public int skillExp;
        public int weaponDamageMin;
        public int weaponDamageMax;
        public float weaponAttackInterval;
        public string attackRange;
        public string damageType;

        public string Id => id;
        public string NameId => nameId;
        public string DescriptionId => descriptionId;
        public string IconId => iconId;
        public string Kind => kind;
    }

    [Serializable]
    public sealed class EquipmentArmorConfigDto : IItemConfig
    {
        public string id;
        public string nameId;
        public string descriptionId;
        public string iconId;
        public string kind;
        public string subtype;
        public string equipmentSlot;
        public string rarityId;
        public int tier;
        public string craftStationId;
        public int craftDurationSec;
        public string craftSkillId;
        public string craftMainStatId;
        public RequiredBuildingDto[] requiredBuildings = Array.Empty<RequiredBuildingDto>();
        public RequiredSkillDto[] requiredSkills = Array.Empty<RequiredSkillDto>();
        public string visibilityItemId;
        public int visibilityItemCount;
        public bool consumeVisibilityItem;
        public bool hiddenUntilVisibilityItem;
        public int outputCount;
        public MaterialCostDto[] materials = Array.Empty<MaterialCostDto>();
        public string sourceActivityId;
        public int skillExp;
        public int physicalResistBonus;
        public int magicResistBonus;
        public int maxHpBonus;

        public string Id => id;
        public string NameId => nameId;
        public string DescriptionId => descriptionId;
        public string IconId => iconId;
        public string Kind => kind;
    }

    [Serializable]
    public sealed class RecipeConfigDto : IItemConfig
    {
        public string id;
        public string nameId;
        public string descriptionId;
        public string iconId;
        public string kind;
        public string rarityId;
        public int tier;
        public bool enabled;

        public string Id => id;
        public string NameId => nameId;
        public string DescriptionId => descriptionId;
        public string IconId => iconId;
        public string Kind => kind;
    }

    [Serializable]
    public sealed class CraftDefinitionConfigDto
    {
        public string craftId;
        public string targetItemId;
        public string craftStationId;
        public int craftDurationSec;
        public string craftSkillId;
        public RequiredBuildingDto[] requiredBuildings = Array.Empty<RequiredBuildingDto>();
        public MaterialCostDto[] materials = Array.Empty<MaterialCostDto>();
        public string requiredRecipeItemId;
        public int requiredRecipeItemCount;
        public bool consumeRecipeItem;
        public int outputCount;
        public int fatigueCost;
        public int skillExp;
    }

    [Serializable]
    public sealed class ConsumableConfigDto : IItemConfig
    {
        public string id;
        public string nameId;
        public string descriptionId;
        public string iconId;
        public string kind;
        public string rarityId;
        public string usePlace;
        public string useCondition;
        public string[] effects = Array.Empty<string>();
        public int cooldownSeconds;
        public int checkIntervalSeconds;

        public string Id => id;
        public string NameId => nameId;
        public string DescriptionId => descriptionId;
        public string IconId => iconId;
        public string Kind => kind;
    }

    [Serializable]
    public sealed class CurrencyConfigDto
    {
        public string currencyId;
        public string iconId;
        public string nameId;
        public string descriptionId;
    }

    [Serializable]
    public sealed class HeroesRuntimeConfigDto
    {
        public HeroConfigDto[] heroes = Array.Empty<HeroConfigDto>();
        public HeroGrowthConfigDto[] heroGrowth = Array.Empty<HeroGrowthConfigDto>();
        public HeroUniqueSkillConfigDto[] heroUniqueSkills = Array.Empty<HeroUniqueSkillConfigDto>();
        public HeroSkillEffectConfigDto[] heroSkillEffects = Array.Empty<HeroSkillEffectConfigDto>();
    }

    [Serializable]
    public sealed class HeroConfigDto
    {
        public string heroId;
        public int sortOrder;
        public string rarityId;
        public string typeId;
        public bool enabled;
        public string[] professionIds = Array.Empty<string>();
        public string[] uniqueSkillIds = Array.Empty<string>();
        public string fullSpriteId;
        public string iconSpriteId;
        public string battleSpriteId;
        public string nameId;
        public string descriptionId;
        public HeroBaseStatsDto baseStats;
    }

    [Serializable]
    public sealed class HeroGrowthConfigDto
    {
        public string heroId;
        public int level;
        public int requiredSkillPoints;
        public int addStrength;
        public int addAgility;
        public int addIntelligence;
        public int addLuck;
        public int addEndurance;
    }

    [Serializable]
    public sealed class HeroUniqueSkillConfigDto
    {
        public string heroId;
        public string skillId;
        public string type;
        public string nameId;
        public string descriptionId;
        public string iconId;
        public bool enabled;
    }

    [Serializable]
    public sealed class HeroSkillEffectConfigDto
    {
        public string skillId;
        public string effectId;
        public string trigger;
        public string condition;
        public float chancePercent;
        public string interval;
        public string effect;
        public string target;
        public float value;
        public string stackMode;
        public int cooldownSeconds;
    }

    [Serializable]
    public sealed class ActivitiesRuntimeConfigDto
    {
        public ActivityConfigDto[] activities = Array.Empty<ActivityConfigDto>();
        public WorkDetailConfigDto[] workDetails = Array.Empty<WorkDetailConfigDto>();
        public OrderDetailConfigDto[] orderDetails = Array.Empty<OrderDetailConfigDto>();
        public EventDetailConfigDto[] eventDetails = Array.Empty<EventDetailConfigDto>();
        public ExploreDetailConfigDto[] exploreDetails = Array.Empty<ExploreDetailConfigDto>();
        public CombatDetailConfigDto[] combatDetails = Array.Empty<CombatDetailConfigDto>();
        public ActivityRequirementConfigDto[] requirements = Array.Empty<ActivityRequirementConfigDto>();
        public ActivityRewardConfigDto[] rewards = Array.Empty<ActivityRewardConfigDto>();
        public ActivityTriggerConfigDto[] triggers = Array.Empty<ActivityTriggerConfigDto>();
        public RarityConfigDto[] rarities = Array.Empty<RarityConfigDto>();
        public SkillConfigDto[] skills = Array.Empty<SkillConfigDto>();
        public SkillProgressionConfigDto[] skillsProgression = Array.Empty<SkillProgressionConfigDto>();
        public EnumValueConfigDto[] enumValues = Array.Empty<EnumValueConfigDto>();
        public DangerEncounterConfigDto[] dangerEncounters = Array.Empty<DangerEncounterConfigDto>();
    }

    [Serializable]
    public sealed class ActivityConfigDto
    {
        public string id;
        public string nameId;
        public string descriptionId;
        public string iconId;
        public string type;
        public string category;
        public string rarityId;
        public int tier;
        public string locationId;
        public string progressMode;
        public int durationSec;
        public int cycleSec;
        public int fatigueCost;
        public string mainSkillId;
        public bool isRepeatable;
        public bool offlineEnabled;
        public string statProfileId;
    }

    [Serializable]
    public sealed class WorkDetailConfigDto
    {
        public string activityId;
        public float successChance;
        public string toolType;
        public bool autoRepeat;
        public string failMode;
    }

    [Serializable]
    public sealed class OrderDetailConfigDto
    {
        public string activityId;
        public string orderSource;
        public string reputationId;
        public bool canRepeat;
        public int repeatCooldownSec;
        public bool consumeRequirementsOnStart;
    }

    [Serializable]
    public sealed class EventDetailConfigDto
    {
        public string activityId;
        public string eventKind;
        public string discoverConditionId;
        public bool startsCombat;
        public string encounterId;
        public bool oneTime;
        public bool hiddenUntilDiscovered;
    }

    [Serializable]
    public sealed class ExploreDetailConfigDto
    {
        public string activityId;
        public string unlockLocationId;
        public int discoveryPointsRequired;
        public int dangerLevel;
    }

    [Serializable]
    public sealed class CombatDetailConfigDto
    {
        public string activityId;
        public string enemyGroupId;
        public string combatMode;
        public string balanceIntent;
        public string completionRewardRule;
    }

    [Serializable]
    public sealed class ActivityRequirementConfigDto
    {
        public string activityId;
        public string reqType;
        public string targetId;
        public int value;
        public bool consume;
        public bool hidden;
        public string checkMoment;
    }

    [Serializable]
    public sealed class ActivityRewardConfigDto
    {
        public string activityId;
        public string rewardType;
        public string targetId;
        public int min;
        public int max;
        public float chance;
        public string grantMoment;
    }

    [Serializable]
    public sealed class ActivityTriggerConfigDto
    {
        public string activityId;
        public string triggerMoment;
        public string triggerType;
        public string targetId;
        public string value;
        public float chance;
        public bool onceOnly;
    }

    [Serializable]
    public sealed class RarityConfigDto
    {
        public string id;
        public string nameId;
        public string descriptionId;
        public string iconId;
        public float rewardMult;
        public float durationMult;
        public float fatigueMult;
        public int weight;
    }

    [Serializable]
    public sealed class SkillConfigDto
    {
        public string skillId;
        public string skillNameId;
        public string skillDescriptionId;
        public string skillIconId;
    }

    [Serializable]
    public sealed class SkillProgressionConfigDto
    {
        public int level;
        public int expToNextLevel;
        public int totalExpRequired;
    }

    [Serializable]
    public sealed class QuestRuntimeConfigDto
    {
        public StageConfigDto[] stages = Array.Empty<StageConfigDto>();
        public StageQuestConfigDto[] stageQuests = Array.Empty<StageQuestConfigDto>();
        public StoryQuestConfigDto[] storyQuests = Array.Empty<StoryQuestConfigDto>();
        public DailyQuestConfigDto[] dailyQuests = Array.Empty<DailyQuestConfigDto>();
        public QuestStartConditionConfigDto[] questStartConditions = Array.Empty<QuestStartConditionConfigDto>();
        public QuestStepConfigDto[] questSteps = Array.Empty<QuestStepConfigDto>();
        public QuestRewardConfigDto[] questRewards = Array.Empty<QuestRewardConfigDto>();
        public EnumValueConfigDto[] enumValues = Array.Empty<EnumValueConfigDto>();
    }

    [Serializable]
    public sealed class StageConfigDto
    {
        public string stageId;
        public string nameId;
        public string descriptionId;
        public string stagePrefabId;
        public int targetDurationSec;
        public string completionRule;
        public string nextStageId;
        public int sortOrder;
        public bool enabled;
    }

    [Serializable]
    public sealed class StageQuestConfigDto
    {
        public string stageId;
        public string questId;
        public int weightPercent;
        public bool required;
        public bool showInStageUi;
        public int sortOrder;
        public bool enabled;
    }

    [Serializable]
    public sealed class StoryQuestConfigDto
    {
        public string questId;
        public string nameId;
        public string descriptionId;
        public string iconId;
        public string journalCategory;
        public int sortOrder;
        public bool isTutorial;
        public bool enabled = true;
    }

    [Serializable]
    public sealed class DailyQuestConfigDto
    {
        public string questId;
        public string nameId;
        public string descriptionId;
        public string iconId;
        public string journalCategory;
        public string dailyPoolId;
        public int selectionWeight;
        public int sortOrder;
        public bool enabled = true;
    }

    public enum QuestDefinitionKind
    {
        Story,
        Daily
    }

    public sealed class QuestDefinition
    {
        public string QuestId { get; internal set; }
        public string NameId { get; internal set; }
        public string DescriptionId { get; internal set; }
        public string IconId { get; internal set; }
        public string JournalCategory { get; internal set; }
        public int SortOrder { get; internal set; }
        public bool IsTutorial { get; internal set; }
        public bool Enabled { get; internal set; }
        public QuestDefinitionKind Kind { get; internal set; }
        public string DailyPoolId { get; internal set; }
        public int SelectionWeight { get; internal set; }
    }

    [Serializable]
    public sealed class QuestStartConditionConfigDto
    {
        public string questId;
        public string conditionGroup;
        public string conditionType;
        public string targetId;
        public string compareOperator;
        public int value;
        public int sortOrder;
    }

    [Serializable]
    public sealed class QuestStepConfigDto
    {
        public string questId;
        public string stepId;
        public int stepOrder;
        public string objectiveType;
        public string targetId;
        public string compareOperator;
        public int targetValue;
        public string descriptionId;
        public bool required;
    }

    [Serializable]
    public sealed class QuestRewardConfigDto
    {
        public string questId;
        public string rewardId;
        public string rewardType;
        public string targetId;
        public int min;
        public int max;
        public float chance;
        public string grantMoment;
        public int sortOrder;
    }

    [Serializable]
    public sealed class BuildingsRuntimeConfigDto
    {
        public BuildingConfigDto[] buildings = Array.Empty<BuildingConfigDto>();
        public BuildingLevelConfigDto[] buildingLevels = Array.Empty<BuildingLevelConfigDto>();
        public BuildActionConfigDto[] buildActions = Array.Empty<BuildActionConfigDto>();
        public BuildingActivityConfigDto[] buildingActivities = Array.Empty<BuildingActivityConfigDto>();
        public BuildingCraftableConfigDto[] buildingCraftables = Array.Empty<BuildingCraftableConfigDto>();
        public SettlementStageSlotConfigDto[] settlementStageSlots = Array.Empty<SettlementStageSlotConfigDto>();
        public SettlementStageStarterHeroConfigDto[] settlementStageStarterHeroes = Array.Empty<SettlementStageStarterHeroConfigDto>();
        public SettlementStageStarterEquipmentConfigDto[] settlementStageStarterEquipment = Array.Empty<SettlementStageStarterEquipmentConfigDto>();
    }

    [Serializable]
    public sealed class BuildingConfigDto
    {
        public string buildingId;
        public string nameId;
        public string descriptionId;
        public string smallIconId;
        public int levels;
        public int unlockedByHallLevel;
        public bool mvpRequired;
        public int startLevel;
        public bool visibleAtStart;
        public string clickableRequirement;
    }

    [Serializable]
    public sealed class BuildingLevelConfigDto
    {
        public string buildingId;
        public int level;
        public string levelPrefabId;
        public string sourceActivityId;
        public string buildFormulaId;
        public int buildPointsRequired;
        public string skillId;
        public int fatigueCost;
        public MaterialCostDto[] materials = Array.Empty<MaterialCostDto>();
        public RequiredActivityDto[] requirementsActivities = Array.Empty<RequiredActivityDto>();
        public RequiredBuildingDto[] requirementsBuildings = Array.Empty<RequiredBuildingDto>();
        public RequiredSkillDto[] requirementsSkills = Array.Empty<RequiredSkillDto>();
        public int skillExp;
        public int activeHeroLimit;
    }

    [Serializable]
    public sealed class BuildActionConfigDto
    {
        public string id;
        public string type;
        public string targetBuildingId;
        public int targetLevel;
        public string buildFormulaId;
        public int buildPointsRequired;
        public string skillId;
        public int fatigueCost;
        public MaterialCostDto[] materials = Array.Empty<MaterialCostDto>();
        public RequiredActivityDto[] requirementsActivities = Array.Empty<RequiredActivityDto>();
        public RequiredBuildingDto[] requirementsBuildings = Array.Empty<RequiredBuildingDto>();
        public RequiredSkillDto[] requirementsSkills = Array.Empty<RequiredSkillDto>();
        public int skillExp;
    }

    [Serializable]
    public sealed class BuildingActivityConfigDto
    {
        public string buildingId;
        public int buildingLevel;
        public string activityId;
        public int sortOrder;
        public string showIfActivityCompleted;
        public string hideIfActivityCompleted;
        public string clickableRequirement;
    }

    [Serializable]
    public sealed class BuildingCraftableConfigDto
    {
        public string buildingId;
        public int buildingLevel;
        public string craftId;
        public int sortOrder;
        public string uiCategory;
        public bool enabled;
    }

    [Serializable]
    public sealed class SettlementStageSlotConfigDto
    {
        public string stageId;
        public string slotId;
        public string buildingId;
        public int sortOrder;
        public bool enabled;
    }

    [Serializable]
    public sealed class SettlementStageStarterHeroConfigDto
    {
        public string stageId;
        public string heroId;
        public int sortOrder;
    }

    [Serializable]
    public sealed class SettlementStageStarterEquipmentConfigDto
    {
        public string stageId;
        public string heroId;
        public string itemId;
        public string equipmentSlot;
        public int sortOrder;
    }

    [Serializable]
    public sealed class EnemiesRuntimeConfigDto
    {
        public EnemyConfigDto[] enemies = Array.Empty<EnemyConfigDto>();
        public EnemyLevelConfigDto[] enemyLevels = Array.Empty<EnemyLevelConfigDto>();
        public EnemyLootConfigDto[] enemyLoot = Array.Empty<EnemyLootConfigDto>();
        public EnemyAbilityConfigDto[] enemyAbilities = Array.Empty<EnemyAbilityConfigDto>();
        public CombatStatusConfigDto[] combatStatuses = Array.Empty<CombatStatusConfigDto>();
        public EnemyGroupConfigDto[] enemyGroups = Array.Empty<EnemyGroupConfigDto>();
        public EnumValueConfigDto[] enumValues = Array.Empty<EnumValueConfigDto>();
    }

    [Serializable]
    public sealed class EnemyConfigDto
    {
        public string enemyId;
        public string nameId;
        public string descriptionId;
        public string iconId;
        public string battleImageId;
        public string enemyType;
        public int combatExp;
        public int hp;
        public int damageMin;
        public int damageMax;
        public float attacksPerSecond;
        public string attackRange;
        public string damageType;
        public float critChancePercent;
        public float critDamageMultiplier;
        public float physicalResistPercent;
        public float magicResistPercent;
        public float dodgeChancePercent;
        public string[] combatAbilityIds = Array.Empty<string>();
        public string lootGroupId;
    }

    [Serializable]
    public sealed class EnemyLevelConfigDto
    {
        public int level;
        public float hpMultiplier;
        public float damageMultiplier;
        public float combatExpMultiplier;
        public float lootQuantityMultiplier;
        public float attackSpeedMultiplier;
    }

    [Serializable]
    public sealed class EnemyLootConfigDto
    {
        public string lootGroupId;
        public string enemyId;
        public string lootId;
        public int minCount;
        public int maxCount;
        public float chancePercent;
        public int qualityMin;
        public int qualityMax;
    }

    [Serializable]
    public sealed class EnemyAbilityConfigDto
    {
        public string abilityId;
        public string nameId;
        public string trigger;
        public float chancePercent;
        public string effects;
        public string target;
        public int cooldownSec;
    }

    [Serializable]
    public sealed class CombatStatusConfigDto
    {
        public string statusId;
        public string nameId;
        public string type;
        public int durationSec;
        public int tickIntervalSec;
        public int maxStacks;
        public string effectType;
        public string damageType;
        public int damageValue;
    }

    [Serializable]
    public sealed class EnemyGroupConfigDto
    {
        public string enemyGroupId;
        public string enemyRef;
        public int sortOrder;
        public int weight;
        public int minCount;
        public int maxCount;
    }

    [Serializable]
    public sealed class FormulaRuntimeConfigDto
    {
        public FormulaConfigDto[] formulas = Array.Empty<FormulaConfigDto>();
        public SkillStatWeightConfigDto[] skillStatWeights = Array.Empty<SkillStatWeightConfigDto>();
    }

    [Serializable]
    public sealed class FormulaConfigDto
    {
        public string formulaId;
        public string derivedStatId;
        public string formulaType;
        public float baseValue;
        public string primaryStat;
        public float primaryStatMultiplier;
        public string secondaryStat;
        public float secondaryStatMultiplier;
        public float levelMultiplier;
        public string weaponValueMode;
        public float minValue;
        public float maxValue;
        public float capValue;
        public string valueType;
        public string rounding;
        public bool enabled;
    }

    [Serializable]
    public sealed class SkillStatWeightConfigDto
    {
        public string profileId;
        public string skillId;
        public string statId;
        public float weight;
        public bool enabled;
    }

    [Serializable]
    public sealed class LootRuntimeConfigDto
    {
        public LootTableConfigDto[] lootTables = Array.Empty<LootTableConfigDto>();
        public LootTableEntryConfigDto[] lootTableEntries = Array.Empty<LootTableEntryConfigDto>();
        public LootGroupConfigDto[] lootGroups = Array.Empty<LootGroupConfigDto>();
        public EnumValueConfigDto[] enumValues = Array.Empty<EnumValueConfigDto>();
    }

    [Serializable]
    public sealed class LootTableConfigDto
    {
        public string lootTableId;
        public string tableType;
        public string rollMode;
        public int rollCountMin;
        public int rollCountMax;
        public bool enabled;
    }

    [Serializable]
    public sealed class LootTableEntryConfigDto
    {
        public string lootTableId;
        public string entryId;
        public string dropType;
        public string targetId;
        public int weight;
        public int min;
        public int max;
        public float chance;
        public string rarityHint;
        public string requiredRollGroup;
    }

    [Serializable]
    public sealed class LootGroupConfigDto
    {
        public string lootTableId;
        public string rollGroup;
        public string rollMode;
        public int rollCountMin;
        public int rollCountMax;
        public float chance;
    }

    [Serializable]
    public sealed class MapRuntimeConfigDto
    {
        public MapCellConfigDto[] mapCells = Array.Empty<MapCellConfigDto>();
        public MapLocationConfigDto[] mapLocations = Array.Empty<MapLocationConfigDto>();
        public MapExplorationLevelConfigDto[] mapExplorationLevels = Array.Empty<MapExplorationLevelConfigDto>();
        public MapCellActivityConfigDto[] mapCellActivities = Array.Empty<MapCellActivityConfigDto>();
        public EnumValueConfigDto[] enumValues = Array.Empty<EnumValueConfigDto>();
    }

    [Serializable]
    public sealed class MapCellConfigDto
    {
        public string cellId;
        public int q;
        public int r;
        public string mapCellNameId;
        public string stateDefault;
        public string terrainType;
        public string regionId;
        public string locationId;
        public int maxExplorationLevel;
        public int explorationDifficulty;
        public bool isBlocking;
        public string visualMarkerId;
    }

    [Serializable]
    public sealed class MapLocationConfigDto
    {
        public string locationId;
        public string mapLocationNameId;
        public string locationType;
        public int tier;
        public string regionId;
        public string defaultCellId;
        public bool visibleInWatchtower;
    }

    [Serializable]
    public sealed class MapExplorationLevelConfigDto
    {
        public int explorationLevel;
        public int pointsRequired;
    }

    [Serializable]
    public sealed class MapCellActivityConfigDto
    {
        public string cellId;
        public string locationId;
        public string activityId;
        public int revealAtExplorationLevel;
        public bool visibleInWatchtower;
    }

    [Serializable]
    public sealed class DangerEncounterConfigDto
    {
        public string dangerEncounterId;
        public string activityId;
        public float riskPercent;
        public string rollMoment;
        public string enemyGroupId;
        public string combatMode;
        public string lootSource;
        public string defeatLossRule;
        public string riskFormulaId;
    }

    [Serializable]
    public sealed class StorageRuntimeConfigDto
    {
        public StorageRuleConfigDto[] storageRules = Array.Empty<StorageRuleConfigDto>();
        public StorageBuildingConfigDto[] storageBuildings = Array.Empty<StorageBuildingConfigDto>();
        public ItemStateConfigDto[] itemStates = Array.Empty<ItemStateConfigDto>();
        public EnumValueConfigDto[] enumValues = Array.Empty<EnumValueConfigDto>();
    }

    [Serializable]
    public sealed class StorageRuleConfigDto
    {
        public string storageRuleId;
        public string itemKind;
        public string mode;
        public int maxStack;
        public bool occupiesSlot;
        public bool allowQuality;
        public bool allowInstanceId;
    }

    [Serializable]
    public sealed class StorageBuildingConfigDto
    {
        public string buildingId;
        public int level;
        public int slotCount;
        public float resourceStackBonus;
        public bool autoSortEnabled;
        public bool filtersEnabled;
    }

    [Serializable]
    public sealed class ItemStateConfigDto
    {
        public string stateId;
        public string storageItemStateNameId;
        public bool availableForCraft;
        public bool availableForSale;
        public bool availableForOrder;
        public bool availableForEquip;
    }

    [Serializable]
    public sealed class EnumValueConfigDto
    {
        public string enumGroup;
        public string value;
        public string description;
    }

    [Serializable]
    public sealed class LocalisationRuntimeConfigDto
    {
        public LocalisationEntryDto[] localisations = Array.Empty<LocalisationEntryDto>();
    }

    [Serializable]
    public sealed class LocalisationEntryDto
    {
        public string id;
        public string ru;
        public string en;
        public string tr;
    }
}
