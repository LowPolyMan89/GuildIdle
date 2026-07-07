using System;

namespace GuildIdle.Player
{
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentSaveVersion = 3;

        public int saveVersion = CurrentSaveVersion;
        public CurrencySaveEntry[] currencies = Array.Empty<CurrencySaveEntry>();
        public ItemSaveEntry[] items = Array.Empty<ItemSaveEntry>();
        public string[] unlockedHeroes = Array.Empty<string>();
        public string[] acquiredHeroes = Array.Empty<string>();
        public HeroSlotSaveEntry[] heroSlots = Array.Empty<HeroSlotSaveEntry>();
        public HeroSaveData[] heroes = Array.Empty<HeroSaveData>();
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
    }

    [Serializable]
    public sealed class HeroSkillSaveData
    {
        public string skillId;
        public int level;
        public long exp;
    }

    [Serializable]
    public sealed class BuildingLevelSaveEntry
    {
        public string buildingId;
        public int level;
    }

    [Serializable]
    public sealed class ActivityRuntimeSaveData
    {
        public ActivityExecutionSaveData[] executions = Array.Empty<ActivityExecutionSaveData>();
    }

    [Serializable]
    public sealed class ActivityExecutionSaveData
    {
        public string executionId;
        public string activityId;
        public string heroId;
        public int heroSlotIndex;
        public GuildIdle.Activities.ActivityRuntimeStatus status;
        public float elapsedSeconds;
        public int completedCycles;
        public long startedAtUnixSeconds;
    }
}
