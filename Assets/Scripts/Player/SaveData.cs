using System;
using GuildIdle.Core;

namespace GuildIdle.Player
{
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentSaveVersion = 6;

        public int saveVersion = CurrentSaveVersion;
        public string currentStageId;
        public CurrencySaveEntry[] currencies = Array.Empty<CurrencySaveEntry>();
        public ItemSaveEntry[] items = Array.Empty<ItemSaveEntry>();
        public ItemInstanceSaveData[] itemInstances = Array.Empty<ItemInstanceSaveData>();
        public EquipmentSlotSaveData[] equipmentSlots = Array.Empty<EquipmentSlotSaveData>();
        public string[] unlockedHeroes = Array.Empty<string>();
        public string[] acquiredHeroes = Array.Empty<string>();
        public HeroSaveData[] heroes = Array.Empty<HeroSaveData>();
        public QuestInstanceSaveData[] questInstances = Array.Empty<QuestInstanceSaveData>();
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
    public static class QuestInstanceStatus
    {
        public const string Active = "Active";
        public const string Completed = "Completed";
        public const string Expired = "Expired";

        public static bool IsValid(string value) =>
            string.Equals(value, Active, StringComparison.Ordinal) ||
            string.Equals(value, Completed, StringComparison.Ordinal) ||
            string.Equals(value, Expired, StringComparison.Ordinal);
    }

    [Serializable]
    public sealed class QuestInstanceSaveData
    {
        public string instanceId;
        public string questId;
        public string cycleId;
        public string status = QuestInstanceStatus.Active;
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
