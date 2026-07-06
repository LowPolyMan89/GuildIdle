using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Player
{
    public sealed class PlayerState
    {
        private const string StarterActivityId = "starter_hero_available";
        private const string StarterHeroId = "ren";
        private const string StarterEquipmentId = "item_wooden_club";
        private const int FirstHeroSlotIndex = 0;

        private readonly Dictionary<string, long> _currencies = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _items = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _unlockedHeroes = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _acquiredHeroes = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<int, string> _heroSlots = new Dictionary<int, string>();
        private readonly HashSet<string> _unlockedBuildings = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _buildingLevels = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _unlockedLocations = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _completedActivities = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _availableActivities = new HashSet<string>(StringComparer.Ordinal);

        public PlayerState(SaveData saveData)
        {
            Load(saveData);
        }

        public static PlayerState CreateDefault()
        {
            var state = new PlayerState(new SaveData());
            state.ApplyDefaultBootstrap();
            return state;
        }

        public SaveData ToSaveData()
        {
            return new SaveData
            {
                saveVersion = SaveData.CurrentSaveVersion,
                currencies = BuildCurrencyEntries(),
                items = BuildItemEntries(),
                unlockedHeroes = BuildSortedArray(_unlockedHeroes),
                acquiredHeroes = BuildSortedArray(_acquiredHeroes),
                heroSlots = BuildHeroSlotEntries(),
                unlockedBuildings = BuildSortedArray(_unlockedBuildings),
                buildingLevels = BuildBuildingLevelEntries(),
                unlockedLocations = BuildSortedArray(_unlockedLocations),
                completedActivities = BuildSortedArray(_completedActivities),
                availableActivities = BuildSortedArray(_availableActivities)
            };
        }

        public bool HasHero(string heroId)
        {
            if (!ValidateHeroId(heroId))
                return false;

            return _acquiredHeroes.Contains(heroId);
        }

        public bool IsHeroUnlocked(string heroId)
        {
            if (!ValidateHeroId(heroId))
                return false;

            return _unlockedHeroes.Contains(heroId);
        }

        public bool AddHero(string heroId)
        {
            if (!ValidateHeroId(heroId))
                return false;

            _unlockedHeroes.Add(heroId);
            return _acquiredHeroes.Add(heroId);
        }

        public string GetHeroInSlot(int slotIndex)
        {
            return _heroSlots.TryGetValue(slotIndex, out var heroId) ? heroId : null;
        }

        public bool SetHeroSlot(int slotIndex, string heroId)
        {
            if (slotIndex < 0)
            {
                Debug.LogError($"[PlayerState] Invalid hero slot index '{slotIndex}'.");
                return false;
            }

            if (!ValidateHeroId(heroId))
                return false;

            if (!HasHero(heroId))
            {
                Debug.LogError($"[PlayerState] Cannot assign hero '{heroId}' to slot {slotIndex}: hero is not acquired.");
                return false;
            }

            _heroSlots[slotIndex] = heroId;
            return true;
        }

        public int GetItem(string itemId)
        {
            return ValidateItemId(itemId) ? GetItemAmount(itemId) : 0;
        }

        public bool HasItem(string itemId, int amount)
        {
            if (!ValidateItemId(itemId))
                return false;

            return amount <= 0 || GetItemAmount(itemId) >= amount;
        }

        public bool AddItem(string itemId, int amount)
        {
            if (!ValidateItemId(itemId) || !ValidatePositiveAmount(amount, "item"))
                return false;

            var current = GetItemAmount(itemId);
            _items[itemId] = AddClamped(current, amount);
            return true;
        }

        public bool SpendItem(string itemId, int amount)
        {
            if (!ValidateItemId(itemId) || !ValidatePositiveAmount(amount, "item"))
                return false;

            var current = GetItemAmount(itemId);
            if (current < amount)
                return false;

            var next = current - amount;
            if (next == 0)
                _items.Remove(itemId);
            else
                _items[itemId] = next;

            return true;
        }

        public long GetCurrency(string currencyId)
        {
            return ValidateCurrencyId(currencyId) ? GetCurrencyAmount(currencyId) : 0L;
        }

        public bool AddCurrency(string currencyId, long amount)
        {
            if (!ValidateCurrencyId(currencyId) || !ValidatePositiveAmount(amount, "currency"))
                return false;

            var current = GetCurrencyAmount(currencyId);
            _currencies[currencyId] = AddClamped(current, amount);
            return true;
        }

        public bool SpendCurrency(string currencyId, long amount)
        {
            if (!ValidateCurrencyId(currencyId) || !ValidatePositiveAmount(amount, "currency"))
                return false;

            var current = GetCurrencyAmount(currencyId);
            if (current < amount)
                return false;

            var next = current - amount;
            if (next == 0)
                _currencies.Remove(currencyId);
            else
                _currencies[currencyId] = next;

            return true;
        }

        public bool IsBuildingUnlocked(string buildingId)
        {
            if (!ValidateBuildingId(buildingId))
                return false;

            return _unlockedBuildings.Contains(buildingId);
        }

        public bool UnlockBuilding(string buildingId)
        {
            if (!ValidateBuildingId(buildingId))
                return false;

            var added = _unlockedBuildings.Add(buildingId);
            if (!_buildingLevels.ContainsKey(buildingId))
                _buildingLevels[buildingId] = 1;

            return added;
        }

        public int GetBuildingLevel(string buildingId)
        {
            if (!ValidateBuildingId(buildingId))
                return 0;

            return _buildingLevels.TryGetValue(buildingId, out var level) ? level : 0;
        }

        public bool SetBuildingLevel(string buildingId, int level)
        {
            if (!ValidateBuildingId(buildingId))
                return false;

            if (!_unlockedBuildings.Contains(buildingId))
            {
                Debug.LogError($"[PlayerState] Cannot set level for locked building '{buildingId}'.");
                return false;
            }

            if (!ValidateBuildingLevel(buildingId, level))
                return false;

            _buildingLevels[buildingId] = level;
            return true;
        }

        public bool IsLocationUnlocked(string locationId)
        {
            if (!ValidateLocationId(locationId))
                return false;

            return _unlockedLocations.Contains(locationId);
        }

        public bool UnlockLocation(string locationId)
        {
            if (!ValidateLocationId(locationId))
                return false;

            return _unlockedLocations.Add(locationId);
        }

        public bool IsActivityCompleted(string activityId)
        {
            if (!ValidateActivityId(activityId))
                return false;

            return _completedActivities.Contains(activityId);
        }

        public bool CompleteActivity(string activityId)
        {
            if (!ValidateActivityId(activityId))
                return false;

            return _completedActivities.Add(activityId);
        }

        public bool IsActivityAvailable(string activityId)
        {
            if (!ValidateActivityId(activityId))
                return false;

            return _availableActivities.Contains(activityId);
        }

        public bool SetActivityAvailable(string activityId, bool available)
        {
            if (!ValidateActivityId(activityId))
                return false;

            if (available)
                return _availableActivities.Add(activityId);

            return _availableActivities.Remove(activityId);
        }

        private void Load(SaveData saveData)
        {
            saveData ??= new SaveData();

            LoadCurrencies(saveData.currencies);
            LoadItems(saveData.items);
            LoadHeroes(saveData.unlockedHeroes, _unlockedHeroes);
            LoadHeroes(saveData.acquiredHeroes, _acquiredHeroes);
            LoadBuildings(saveData.unlockedBuildings);
            LoadBuildingLevels(saveData.buildingLevels);
            LoadLocations(saveData.unlockedLocations);
            LoadActivities(saveData.completedActivities, _completedActivities);
            LoadActivities(saveData.availableActivities, _availableActivities);
            LoadHeroSlots(saveData.heroSlots);
        }

        private void ApplyDefaultBootstrap()
        {
            if (!ValidateActivityId(StarterActivityId))
                return;

            var rewards = RuntimeConfigs.Activities.GetRewards(StarterActivityId);
            var grantedStarterHero = false;

            foreach (var reward in rewards)
            {
                if (reward == null)
                    continue;

                if (IsReward(reward, "Hero", StarterHeroId))
                {
                    AddHero(StarterHeroId);
                    SetHeroSlot(FirstHeroSlotIndex, StarterHeroId);
                    grantedStarterHero = true;
                    continue;
                }

                if (IsReward(reward, "Equipment", StarterEquipmentId))
                    AddItem(StarterEquipmentId, RewardAmount(reward));
            }

            if (!grantedStarterHero)
                Debug.LogError($"[PlayerState] Starter bootstrap '{StarterActivityId}' has no Hero reward for '{StarterHeroId}'.");
        }

        private static bool IsReward(ActivityRewardConfigDto reward, string rewardType, string targetId)
        {
            return string.Equals(reward.rewardType, rewardType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(reward.targetId, targetId, StringComparison.Ordinal);
        }

        private static int RewardAmount(ActivityRewardConfigDto reward)
        {
            return Math.Max(1, reward.min);
        }

        private void LoadCurrencies(CurrencySaveEntry[] entries)
        {
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (entry == null || entry.amount <= 0 || !ValidateCurrencyId(entry.currencyId))
                    continue;

                _currencies[entry.currencyId] = AddClamped(GetCurrencyAmount(entry.currencyId), entry.amount);
            }
        }

        private void LoadItems(ItemSaveEntry[] entries)
        {
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (entry == null || entry.amount <= 0 || !ValidateItemId(entry.itemId))
                    continue;

                _items[entry.itemId] = AddClamped(GetItemAmount(entry.itemId), entry.amount);
            }
        }

        private void LoadHeroes(string[] heroIds, HashSet<string> target)
        {
            if (heroIds == null)
                return;

            foreach (var heroId in heroIds)
            {
                if (ValidateHeroId(heroId))
                    target.Add(heroId);
            }
        }

        private void LoadHeroSlots(HeroSlotSaveEntry[] entries)
        {
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (entry == null || entry.slotIndex < 0 || !ValidateHeroId(entry.heroId))
                    continue;

                if (!_acquiredHeroes.Contains(entry.heroId))
                {
                    Debug.LogError($"[PlayerState] Ignoring hero slot {entry.slotIndex}: hero '{entry.heroId}' is not acquired.");
                    continue;
                }

                _heroSlots[entry.slotIndex] = entry.heroId;
            }
        }

        private void LoadBuildings(string[] buildingIds)
        {
            if (buildingIds == null)
                return;

            foreach (var buildingId in buildingIds)
            {
                if (ValidateBuildingId(buildingId))
                    _unlockedBuildings.Add(buildingId);
            }
        }

        private void LoadBuildingLevels(BuildingLevelSaveEntry[] entries)
        {
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (entry == null || !ValidateBuildingId(entry.buildingId) || !ValidateBuildingLevel(entry.buildingId, entry.level))
                    continue;

                if (!_unlockedBuildings.Contains(entry.buildingId))
                {
                    Debug.LogError($"[PlayerState] Ignoring level for locked building '{entry.buildingId}'.");
                    continue;
                }

                _buildingLevels[entry.buildingId] = entry.level;
            }
        }

        private void LoadLocations(string[] locationIds)
        {
            if (locationIds == null)
                return;

            foreach (var locationId in locationIds)
            {
                if (ValidateLocationId(locationId))
                    _unlockedLocations.Add(locationId);
            }
        }

        private void LoadActivities(string[] activityIds, HashSet<string> target)
        {
            if (activityIds == null)
                return;

            foreach (var activityId in activityIds)
            {
                if (ValidateActivityId(activityId))
                    target.Add(activityId);
            }
        }

        private static bool ValidateConfigsReady(string action)
        {
            if (RuntimeConfigs.IsLoaded)
                return true;

            var reason = RuntimeConfigs.HasErrors ? $"config load failed: {RuntimeConfigs.LastError}" : "runtime configs are not loaded";
            Debug.LogError($"[PlayerState] Cannot {action}: {reason}.");
            return false;
        }

        private static bool ValidateHeroId(string heroId)
        {
            if (!ValidateConfigsReady("validate hero id"))
                return false;

            if (RuntimeConfigs.Heroes.TryGet(heroId, out _))
                return true;

            Debug.LogError($"[PlayerState] Unknown hero id '{heroId}'.");
            return false;
        }

        private static bool ValidateItemId(string itemId)
        {
            if (!ValidateConfigsReady("validate item id"))
                return false;

            if (RuntimeConfigs.Items.TryGet(itemId, out _))
                return true;

            Debug.LogError($"[PlayerState] Unknown item id '{itemId}'.");
            return false;
        }

        private static bool ValidateCurrencyId(string currencyId)
        {
            if (!ValidateConfigsReady("validate currency id"))
                return false;

            if (RuntimeConfigs.Items.TryGetCurrency(currencyId, out _))
                return true;

            Debug.LogError($"[PlayerState] Unknown currency id '{currencyId}'.");
            return false;
        }

        private static bool ValidateBuildingId(string buildingId)
        {
            if (!ValidateConfigsReady("validate building id"))
                return false;

            if (RuntimeConfigs.Buildings.TryGet(buildingId, out _))
                return true;

            Debug.LogError($"[PlayerState] Unknown building id '{buildingId}'.");
            return false;
        }

        private static bool ValidateBuildingLevel(string buildingId, int level)
        {
            if (level <= 0)
            {
                Debug.LogError($"[PlayerState] Invalid level '{level}' for building '{buildingId}'.");
                return false;
            }

            if (RuntimeConfigs.Buildings.TryGet(buildingId, out var building) && level <= building.levels)
                return true;

            Debug.LogError($"[PlayerState] Level '{level}' is not available for building '{buildingId}'.");
            return false;
        }

        private static bool ValidateLocationId(string locationId)
        {
            if (!ValidateConfigsReady("validate location id"))
                return false;

            if (RuntimeConfigs.Map.TryGetLocation(locationId, out _))
                return true;

            Debug.LogError($"[PlayerState] Unknown location id '{locationId}'.");
            return false;
        }

        private static bool ValidateActivityId(string activityId)
        {
            if (!ValidateConfigsReady("validate activity id"))
                return false;

            if (RuntimeConfigs.Activities.TryGet(activityId, out _))
                return true;

            Debug.LogError($"[PlayerState] Unknown activity id '{activityId}'.");
            return false;
        }

        private static bool ValidatePositiveAmount(long amount, string target)
        {
            if (amount > 0)
                return true;

            Debug.LogError($"[PlayerState] Cannot change {target} by non-positive amount '{amount}'.");
            return false;
        }

        private int GetItemAmount(string itemId)
        {
            return _items.TryGetValue(itemId, out var amount) ? amount : 0;
        }

        private long GetCurrencyAmount(string currencyId)
        {
            return _currencies.TryGetValue(currencyId, out var amount) ? amount : 0L;
        }

        private static int AddClamped(int current, int amount)
        {
            if (int.MaxValue - current < amount)
                return int.MaxValue;

            return current + amount;
        }

        private static long AddClamped(long current, long amount)
        {
            if (long.MaxValue - current < amount)
                return long.MaxValue;

            return current + amount;
        }

        private CurrencySaveEntry[] BuildCurrencyEntries()
        {
            var keys = SortedKeys(_currencies);
            var entries = new CurrencySaveEntry[keys.Count];
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                entries[i] = new CurrencySaveEntry { currencyId = key, amount = _currencies[key] };
            }

            return entries;
        }

        private ItemSaveEntry[] BuildItemEntries()
        {
            var keys = SortedKeys(_items);
            var entries = new ItemSaveEntry[keys.Count];
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                entries[i] = new ItemSaveEntry { itemId = key, amount = _items[key] };
            }

            return entries;
        }

        private HeroSlotSaveEntry[] BuildHeroSlotEntries()
        {
            var slotIndexes = new List<int>(_heroSlots.Keys);
            slotIndexes.Sort();

            var entries = new HeroSlotSaveEntry[slotIndexes.Count];
            for (var i = 0; i < slotIndexes.Count; i++)
            {
                var slotIndex = slotIndexes[i];
                entries[i] = new HeroSlotSaveEntry { slotIndex = slotIndex, heroId = _heroSlots[slotIndex] };
            }

            return entries;
        }

        private BuildingLevelSaveEntry[] BuildBuildingLevelEntries()
        {
            var keys = SortedKeys(_buildingLevels);
            var entries = new BuildingLevelSaveEntry[keys.Count];
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                entries[i] = new BuildingLevelSaveEntry { buildingId = key, level = _buildingLevels[key] };
            }

            return entries;
        }

        private static string[] BuildSortedArray(HashSet<string> values)
        {
            var list = new List<string>(values);
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        private static List<string> SortedKeys<T>(Dictionary<string, T> dictionary)
        {
            var keys = new List<string>(dictionary.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }
    }
}
