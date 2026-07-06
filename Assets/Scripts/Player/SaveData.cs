using System;

namespace GuildIdle.Player
{
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentSaveVersion = 1;

        public int saveVersion = CurrentSaveVersion;
        public CurrencySaveEntry[] currencies = Array.Empty<CurrencySaveEntry>();
        public ItemSaveEntry[] items = Array.Empty<ItemSaveEntry>();
        public string[] unlockedHeroes = Array.Empty<string>();
        public string[] acquiredHeroes = Array.Empty<string>();
        public HeroSlotSaveEntry[] heroSlots = Array.Empty<HeroSlotSaveEntry>();
        public string[] unlockedBuildings = Array.Empty<string>();
        public BuildingLevelSaveEntry[] buildingLevels = Array.Empty<BuildingLevelSaveEntry>();
        public string[] unlockedLocations = Array.Empty<string>();
        public string[] completedActivities = Array.Empty<string>();
        public string[] availableActivities = Array.Empty<string>();
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
    public sealed class BuildingLevelSaveEntry
    {
        public string buildingId;
        public int level;
    }
}
