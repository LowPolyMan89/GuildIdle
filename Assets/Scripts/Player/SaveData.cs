using System;
using GuildIdle.Core;

namespace GuildIdle.Player
{
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentSaveVersion = 5;

        public int saveVersion = CurrentSaveVersion;
        public string currentStageId;
        public CurrencySaveEntry[] currencies = Array.Empty<CurrencySaveEntry>();
        public ItemSaveEntry[] items = Array.Empty<ItemSaveEntry>();
        public ItemInstanceSaveData[] itemInstances = Array.Empty<ItemInstanceSaveData>();
        public EquipmentSlotSaveData[] equipmentSlots = Array.Empty<EquipmentSlotSaveData>();
        public string[] unlockedHeroes = Array.Empty<string>();
        public string[] acquiredHeroes = Array.Empty<string>();
        public HeroSaveData[] heroes = Array.Empty<HeroSaveData>();
        public QuestSaveData[] quests = Array.Empty<QuestSaveData>();
        public string[] unlockedBuildings = Array.Empty<string>();
        public BuildingLevelSaveEntry[] buildingLevels = Array.Empty<BuildingLevelSaveEntry>();
        public string[] unlockedLocations = Array.Empty<string>();
        public string[] completedActivities = Array.Empty<string>();
        public string[] availableActivities = Array.Empty<string>();
        public ActivityRuntimeSaveData activityRuntime = new ActivityRuntimeSaveData();
    }

    [Serializable]
    public sealed class CurrencySaveEntry
    {
        public string currencyId;
        public long amount;
    }

    [Serializable]
    public sealed class ItemSaveEntry
    {
        public string itemId;
        public int amount;
    }

    [Serializable]
    public sealed class ItemInstanceSaveData
    {
        public string instanceId;
        public string itemId;
        public string stateId;
    }

    [Serializable]
    public sealed class EquipmentSlotSaveData
    {
        public string heroId;
        public string equipmentSlot;
        public string itemInstanceId;
    }

    [Serializable]
    public sealed class HeroSlotSaveEntry
    {
        public int slotIndex;
        public string heroId;
    }

    [Serializable]
    public sealed class HeroSaveData
    {
        public string heroId;
        public int level;
        public long exp;
        public int fatigue;
        public int maxFatigue;
        public string currentActivityExecutionId;
        public HeroSkillSaveData[] skills = Array.Empty<HeroSkillSaveData>();
        public HeroEffectCounterSaveData[] effectCounters = Array.Empty<HeroEffectCounterSaveData>();
    }

    [Serializable]
    public sealed class HeroSkillSaveData
    {
        public string skillId;
        public int level;
        public long exp;
    }

    [Serializable]
    public sealed class HeroEffectCounterSaveData
    {
        public string effectId;
        public long value;
    }

    [Serializable]
    public sealed class QuestSaveData
    {
        public string questId;
        public bool completed;
        public bool rewardsGranted;
        public QuestStepSaveData[] steps = Array.Empty<QuestStepSaveData>();
    }

    [Serializable]
    public sealed class QuestStepSaveData
    {
        public string stepId;
        public int currentValue;
        public bool completed;
    }

    [Serializable]
    public sealed class BuildingLevelSaveEntry
    {
        public string buildingId;
        public int level;
    }
}
