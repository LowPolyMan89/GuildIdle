using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Core;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Player
{
    public sealed class PlayerState : IActivityRuntimeStore
    {
        public const string EquippedItemStateId = "equipped";
        public const string OnStorageItemStateId = "on_storage";

        private readonly HeroStatsService _heroStats;
        private readonly IPlayerBootstrapConfigProvider _configs;
        private readonly Dictionary<string, long> _currencies = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _items = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, ItemInstanceSaveData> _itemInstances = new Dictionary<string, ItemInstanceSaveData>(StringComparer.Ordinal);
        private readonly Dictionary<string, EquipmentSlotSaveData> _equipmentSlots = new Dictionary<string, EquipmentSlotSaveData>(StringComparer.Ordinal);
        private readonly HashSet<string> _unlockedHeroes = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _acquiredHeroes = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, HeroRuntimeState> _heroes = new Dictionary<string, HeroRuntimeState>(StringComparer.Ordinal);
        private readonly HashSet<string> _unlockedBuildings = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _buildingLevels = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _unlockedLocations = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _completedActivities = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _availableActivities = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, ActivityExecutionSaveData> _activityExecutions = new Dictionary<string, ActivityExecutionSaveData>(StringComparer.Ordinal);
        private readonly Dictionary<string, QuestSaveData> _quests = new Dictionary<string, QuestSaveData>(StringComparer.Ordinal);
        private string _currentStageId;

        public PlayerState(
            SaveData saveData,
            HeroStatsService heroStats,
            IPlayerBootstrapConfigProvider configs)
        {
            _heroStats = heroStats ?? throw new ArgumentNullException(nameof(heroStats));
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            Load(saveData, null);
        }

        public PlayerState(
            SaveData saveData,
            HeroSlotSaveEntry[] legacyHeroSlots,
            HeroStatsService heroStats,
            IPlayerBootstrapConfigProvider configs)
        {
            _heroStats = heroStats ?? throw new ArgumentNullException(nameof(heroStats));
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            Load(saveData, legacyHeroSlots);
        }

        internal bool WasNormalized { get; private set; }
        public string CurrentStageId => _currentStageId;

        internal void MarkNormalized()
        {
            WasNormalized = true;
        }

        public SaveData ToSaveData()
        {
            return new SaveData
            {
                saveVersion = SaveData.CurrentSaveVersion,
                currentStageId = _currentStageId,
                currencies = BuildCurrencyEntries(),
                items = BuildItemEntries(),
                itemInstances = GetItemInstances(),
                equipmentSlots = GetEquipmentSlots(),
                unlockedHeroes = BuildSortedArray(_unlockedHeroes),
                acquiredHeroes = BuildSortedArray(_acquiredHeroes),
                heroes = BuildHeroEntries(),
                quests = GetQuestStates(),
                unlockedBuildings = BuildSortedArray(_unlockedBuildings),
                buildingLevels = BuildBuildingLevelEntries(),
                unlockedLocations = BuildSortedArray(_unlockedLocations),
                completedActivities = BuildSortedArray(_completedActivities),
                availableActivities = BuildSortedArray(_availableActivities),
                activityRuntime = BuildActivityRuntimeSaveData()
            };
        }

        public bool SetCurrentStage(string stageId)
        {
            if (!_configs.TryGetSettlementStage(stageId, out var stage) || stage == null || !stage.enabled)
            {
                Debug.LogError($"[PlayerState] Unknown or disabled stage id '{stageId}'.");
                return false;
            }

            _currentStageId = stageId;
            return true;
        }

        public QuestSaveData GetQuestState(string questId)
        {
            return !string.IsNullOrWhiteSpace(questId) && _quests.TryGetValue(questId, out var quest)
                ? CloneQuest(quest)
                : null;
        }

        public QuestSaveData[] GetQuestStates()
        {
            var keys = SortedKeys(_quests);
            var entries = new QuestSaveData[keys.Count];
            for (var i = 0; i < keys.Count; i++)
                entries[i] = CloneQuest(_quests[keys[i]]);

            return entries;
        }

        public bool SetQuestState(QuestSaveData quest)
        {
            if (!TryNormalizeQuest(quest, out var normalized))
                return false;

            _quests[normalized.questId] = normalized;
            return true;
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
            var added = _acquiredHeroes.Add(heroId);
            EnsureHeroState(heroId);
            return added;
        }

        public bool HasHeroState(string heroId)
        {
            if (!ValidateHeroId(heroId))
                return false;

            return _heroes.ContainsKey(heroId);
        }

        public HeroSaveData GetHeroState(string heroId)
        {
            if (!ValidateHeroId(heroId) || !_heroes.TryGetValue(heroId, out var hero))
                return null;

            return hero.ToSaveData();
        }

        public int GetHeroFatigue(string heroId)
        {
            return TryGetHeroState(heroId, out var hero) ? hero.Fatigue : 0;
        }

        public int GetHeroMaxFatigue(string heroId)
        {
            return TryGetHeroState(heroId, out var hero) ? hero.MaxFatigue : 0;
        }

        public bool SpendHeroFatigue(string heroId, int amount)
        {
            if (!ValidatePositiveAmount(amount, "hero fatigue") || !TryGetHeroState(heroId, out var hero))
                return false;

            if (hero.Fatigue < amount)
                return false;

            hero.Fatigue -= amount;
            return true;
        }

        public bool RestoreHeroFatigue(string heroId, int amount)
        {
            if (!ValidatePositiveAmount(amount, "hero fatigue") || !TryGetHeroState(heroId, out var hero))
                return false;

            hero.Fatigue = Math.Min(hero.MaxFatigue, hero.Fatigue + amount);
            return true;
        }

        public int GetHeroSkillLevel(string heroId, string skillId)
        {
            if (!ValidateSkillId(skillId) || !TryGetHeroState(heroId, out var hero))
                return 0;

            return hero.Skills.TryGetValue(skillId, out var skill) ? skill.Level : 1;
        }

        public long GetHeroSkillExp(string heroId, string skillId)
        {
            if (!ValidateSkillId(skillId) || !TryGetHeroState(heroId, out var hero))
                return 0L;

            return hero.Skills.TryGetValue(skillId, out var skill) ? skill.Exp : 0L;
        }

        public bool AddHeroSkillExp(string heroId, string skillId, int amount)
        {
            if (!ValidateSkillId(skillId) || !ValidatePositiveAmount(amount, "hero skill exp") || !TryGetHeroState(heroId, out var hero))
                return false;

            if (!hero.Skills.TryGetValue(skillId, out var skill))
            {
                skill = new HeroSkillRuntimeState(skillId);
                hero.Skills.Add(skillId, skill);
            }

            skill.Exp = AddClamped(skill.Exp, amount);
            skill.Level = _heroStats.ResolveSkillLevel(skill.Exp);
            return true;
        }

        public bool EnsureHeroSkill(string heroId, string skillId)
        {
            if (!ValidateSkillId(skillId) || !TryGetHeroState(heroId, out var hero))
                return false;

            if (!hero.Skills.ContainsKey(skillId))
                hero.Skills.Add(skillId, new HeroSkillRuntimeState(skillId));

            return true;
        }

        public long GetHeroEffectCounter(string heroId, string effectId)
        {
            if (string.IsNullOrWhiteSpace(effectId) || !TryGetHeroState(heroId, out var hero))
                return 0L;

            return hero.EffectCounters.TryGetValue(effectId, out var value) ? value : 0L;
        }

        public bool SetHeroEffectCounter(string heroId, string effectId, long value)
        {
            if (string.IsNullOrWhiteSpace(effectId) || value < 0 || !TryGetHeroState(heroId, out var hero))
            {
                Debug.LogError($"[PlayerState] Invalid hero effect counter '{heroId}:{effectId}' value '{value}'.");
                return false;
            }

            hero.EffectCounters[effectId] = value;
            return true;
        }

        public bool IsHeroBusy(string heroId)
        {
            return TryGetHeroState(heroId, out var hero) && !string.IsNullOrWhiteSpace(hero.CurrentActivityExecutionId);
        }

        public string GetHeroCurrentActivityExecutionId(string heroId)
        {
            return TryGetHeroState(heroId, out var hero) ? hero.CurrentActivityExecutionId : null;
        }

        public bool SetHeroBusy(string heroId, string executionId)
        {
            if (!TryGetHeroState(heroId, out var hero))
                return false;

            if (string.IsNullOrWhiteSpace(executionId))
            {
                Debug.LogError("[PlayerState] Cannot set hero busy with empty execution id.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(hero.CurrentActivityExecutionId))
            {
                hero.CurrentActivityExecutionId = executionId;
                return true;
            }

            if (string.Equals(hero.CurrentActivityExecutionId, executionId, StringComparison.Ordinal))
                return true;

            Debug.LogError($"[PlayerState] Hero '{heroId}' is already busy with execution '{hero.CurrentActivityExecutionId}'.");
            return false;
        }

        public bool ClearHeroBusy(string heroId, string executionId)
        {
            if (!TryGetHeroState(heroId, out var hero))
                return false;

            if (string.IsNullOrWhiteSpace(hero.CurrentActivityExecutionId))
                return true;

            if (!string.Equals(hero.CurrentActivityExecutionId, executionId, StringComparison.Ordinal))
            {
                Debug.LogError($"[PlayerState] Cannot clear busy state for hero '{heroId}' with execution '{executionId}': current execution is '{hero.CurrentActivityExecutionId}'.");
                return false;
            }

            hero.CurrentActivityExecutionId = null;
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

        public ItemInstanceSaveData GetItemInstance(string instanceId)
        {
            return !string.IsNullOrWhiteSpace(instanceId) && _itemInstances.TryGetValue(instanceId, out var instance)
                ? CloneItemInstance(instance)
                : null;
        }

        public ItemInstanceSaveData[] GetItemInstances()
        {
            var keys = SortedKeys(_itemInstances);
            var entries = new ItemInstanceSaveData[keys.Count];
            for (var i = 0; i < keys.Count; i++)
                entries[i] = CloneItemInstance(_itemInstances[keys[i]]);

            return entries;
        }

        public EquipmentSlotSaveData[] GetEquipmentSlots()
        {
            var keys = SortedKeys(_equipmentSlots);
            var entries = new EquipmentSlotSaveData[keys.Count];
            for (var i = 0; i < keys.Count; i++)
                entries[i] = CloneEquipmentSlot(_equipmentSlots[keys[i]]);

            return entries;
        }

        public ItemInstanceSaveData GetEquippedItem(string heroId, string equipmentSlot)
        {
            var key = EquipmentSlotKey(heroId, equipmentSlot);
            if (!_equipmentSlots.TryGetValue(key, out var slot) ||
                !_itemInstances.TryGetValue(slot.itemInstanceId, out var instance))
            {
                return null;
            }

            return CloneItemInstance(instance);
        }

        public string AddItemInstance(string itemId, string stateId)
        {
            if (!_configs.TryGetItem(itemId, out _) || !_configs.IsKnownItemState(stateId))
            {
                Debug.LogError($"[PlayerState] Cannot create item instance '{itemId}' with state '{stateId}'.");
                return null;
            }

            var instanceId = NewUniqueInstanceId();
            _itemInstances.Add(instanceId, new ItemInstanceSaveData
            {
                instanceId = instanceId,
                itemId = itemId,
                stateId = stateId
            });
            return instanceId;
        }

        public bool EquipItemInstance(string heroId, string equipmentSlot, string instanceId)
        {
            if (!_acquiredHeroes.Contains(heroId) || !_heroes.ContainsKey(heroId))
                return false;

            if (!_itemInstances.TryGetValue(instanceId, out var instance) ||
                !_configs.TryGetEquipmentSlot(instance.itemId, out var configuredSlot) ||
                !string.Equals(configuredSlot, equipmentSlot, StringComparison.Ordinal))
            {
                return false;
            }

            var slotKey = EquipmentSlotKey(heroId, equipmentSlot);
            if (_equipmentSlots.ContainsKey(slotKey) || IsInstanceEquipped(instanceId))
                return false;

            if (!string.Equals(instance.stateId, OnStorageItemStateId, StringComparison.Ordinal) &&
                !string.Equals(instance.stateId, EquippedItemStateId, StringComparison.Ordinal))
            {
                return false;
            }

            instance.stateId = EquippedItemStateId;
            _equipmentSlots.Add(slotKey, new EquipmentSlotSaveData
            {
                heroId = heroId,
                equipmentSlot = equipmentSlot,
                itemInstanceId = instanceId
            });
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

        public bool CanClickBuilding(string buildingId)
        {
            if (!ValidateBuildingId(buildingId))
                return false;

            if (!_unlockedBuildings.Contains(buildingId))
                return false;

            var requirement = GetBuildingClickableRequirement(buildingId);
            if (string.IsNullOrWhiteSpace(requirement))
                return true;

            if (!TryParseBuildingLevelRequirement(requirement, out var requiredBuildingId, out var requiredLevel))
            {
                Debug.LogError($"[PlayerState] Invalid clickable_requirement '{requirement}' for building '{buildingId}'.");
                return false;
            }

            return GetBuildingLevel(requiredBuildingId) >= requiredLevel;
        }

        public bool UnlockBuilding(string buildingId)
        {
            if (!ValidateBuildingId(buildingId))
                return false;

            var added = _unlockedBuildings.Add(buildingId);
            if (!_buildingLevels.ContainsKey(buildingId))
                _buildingLevels[buildingId] = GetConfiguredStartLevel(buildingId);

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

        public ActivityExecutionSaveData[] GetActivityExecutions()
        {
            var keys = SortedKeys(_activityExecutions);
            var entries = new ActivityExecutionSaveData[keys.Count];
            for (var i = 0; i < keys.Count; i++)
                entries[i] = CloneExecution(_activityExecutions[keys[i]]);

            return entries;
        }

        public ActivityExecutionSaveData GetActivityExecution(string executionId)
        {
            if (string.IsNullOrWhiteSpace(executionId))
                return null;

            return _activityExecutions.TryGetValue(executionId, out var execution) ? CloneExecution(execution) : null;
        }

        public bool AddActivityExecution(ActivityExecutionSaveData execution)
        {
            if (!ValidateActivityExecution(execution, requireRunning: true))
                return false;

            if (_activityExecutions.ContainsKey(execution.executionId))
            {
                Debug.LogError($"[PlayerState] Activity execution '{execution.executionId}' already exists.");
                return false;
            }

            if (!TryGetHeroState(execution.heroId, out var hero))
                return false;

            if (!string.IsNullOrWhiteSpace(hero.CurrentActivityExecutionId) &&
                !string.Equals(hero.CurrentActivityExecutionId, execution.executionId, StringComparison.Ordinal))
            {
                Debug.LogError($"[PlayerState] Hero '{execution.heroId}' is already busy with execution '{hero.CurrentActivityExecutionId}'.");
                return false;
            }

            var stored = CloneExecution(execution);
            stored.status = ActivityRuntimeStatus.Running;
            _activityExecutions.Add(stored.executionId, stored);
            hero.CurrentActivityExecutionId = stored.executionId;
            return true;
        }

        public bool UpdateActivityExecution(ActivityExecutionSaveData execution)
        {
            if (!ValidateActivityExecution(execution, requireRunning: true))
                return false;

            if (!_activityExecutions.ContainsKey(execution.executionId))
            {
                Debug.LogError($"[PlayerState] Cannot update missing activity execution '{execution.executionId}'.");
                return false;
            }

            _activityExecutions[execution.executionId] = CloneExecution(execution);
            return true;
        }

        public bool RemoveActivityExecution(string executionId)
        {
            if (string.IsNullOrWhiteSpace(executionId))
            {
                Debug.LogError("[PlayerState] Cannot remove activity execution with empty id.");
                return false;
            }

            if (!_activityExecutions.TryGetValue(executionId, out var execution))
                return false;

            _activityExecutions.Remove(executionId);
            if (_heroes.TryGetValue(execution.heroId, out var hero) &&
                string.Equals(hero.CurrentActivityExecutionId, executionId, StringComparison.Ordinal))
            {
                hero.CurrentActivityExecutionId = null;
            }

            return true;
        }

        private void Load(SaveData saveData, HeroSlotSaveEntry[] legacyHeroSlots)
        {
            saveData ??= new SaveData();

            _currentStageId = string.IsNullOrWhiteSpace(saveData.currentStageId) ? null : saveData.currentStageId;
            LoadCurrencies(saveData.currencies);
            LoadItems(saveData.items);
            LoadItemInstances(saveData.itemInstances);
            LoadHeroes(saveData.unlockedHeroes, _unlockedHeroes);
            LoadHeroes(saveData.acquiredHeroes, _acquiredHeroes);
            LoadLegacyHeroSlots(legacyHeroSlots);
            LoadHeroStates(saveData.heroes);
            LoadQuests(saveData.quests);
            LoadBuildings(saveData.unlockedBuildings);
            LoadBuildingLevels(saveData.buildingLevels);
            LoadLocations(saveData.unlockedLocations);
            LoadActivities(saveData.completedActivities, _completedActivities);
            LoadActivities(saveData.availableActivities, _availableActivities);
            EnsureHeroStatesForAcquiredHeroes();
            LoadEquipmentSlots(saveData.equipmentSlots);
            NormalizeOrphanEquippedInstances();
            LoadActivityRuntime(saveData.activityRuntime);
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

        private void LoadItemInstances(ItemInstanceSaveData[] entries)
        {
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (entry == null || !_configs.TryGetItem(entry.itemId, out _))
                    continue;

                var instanceId = entry.instanceId;
                if (string.IsNullOrWhiteSpace(instanceId) || _itemInstances.ContainsKey(instanceId))
                {
                    instanceId = NewUniqueInstanceId();
                    WasNormalized = true;
                }

                _itemInstances.Add(instanceId, new ItemInstanceSaveData
                {
                    instanceId = instanceId,
                    itemId = entry.itemId,
                    stateId = entry.stateId
                });
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

        private void LoadLegacyHeroSlots(HeroSlotSaveEntry[] entries)
        {
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (entry == null || entry.slotIndex < 0 || !ValidateHeroId(entry.heroId))
                    continue;

                _unlockedHeroes.Add(entry.heroId);
                _acquiredHeroes.Add(entry.heroId);
                WasNormalized = true;
            }
        }

        private void LoadHeroStates(HeroSaveData[] entries)
        {
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (entry == null || !ValidateHeroId(entry.heroId))
                    continue;

                if (!_acquiredHeroes.Contains(entry.heroId))
                {
                    Debug.LogError($"[PlayerState] Ignoring hero state for non-acquired hero '{entry.heroId}'.");
                    continue;
                }

                var hero = CreateHeroState(entry.heroId, Math.Max(1, entry.level));
                hero.Exp = Math.Max(0L, entry.exp);
                hero.MaxFatigue = entry.maxFatigue > 0 ? entry.maxFatigue : _heroStats.CalculateMaxFatigue(hero.HeroId, hero.Level);
                hero.Fatigue = Math.Max(0, Math.Min(hero.MaxFatigue, entry.fatigue));
                hero.CurrentActivityExecutionId = string.IsNullOrWhiteSpace(entry.currentActivityExecutionId) ? null : entry.currentActivityExecutionId;
                LoadHeroSkills(hero, entry.skills);
                LoadHeroEffectCounters(hero, entry.effectCounters);
                _heroes[hero.HeroId] = hero;
            }
        }

        private void LoadHeroSkills(HeroRuntimeState hero, HeroSkillSaveData[] entries)
        {
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (entry == null || !ValidateSkillId(entry.skillId))
                    continue;

                var exp = Math.Max(0L, entry.exp);
                hero.Skills[entry.skillId] = new HeroSkillRuntimeState(entry.skillId)
                {
                    Exp = exp,
                    Level = Math.Max(1, entry.level > 0 ? entry.level : _heroStats.ResolveSkillLevel(exp))
                };
            }
        }

        private static void LoadHeroEffectCounters(HeroRuntimeState hero, HeroEffectCounterSaveData[] entries)
        {
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.effectId))
                    continue;

                hero.EffectCounters[entry.effectId] = Math.Max(0L, entry.value);
            }
        }

        private void LoadQuests(QuestSaveData[] entries)
        {
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (!TryNormalizeQuest(entry, out var quest) || _quests.ContainsKey(quest.questId))
                    continue;

                _quests.Add(quest.questId, quest);
            }
        }

        private void LoadEquipmentSlots(EquipmentSlotSaveData[] entries)
        {
            if (entries == null)
                return;

            var assignedInstances = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (!TryNormalizeEquipmentSlot(entry, assignedInstances, out var slot))
                {
                    WasNormalized = true;
                    continue;
                }

                var key = EquipmentSlotKey(slot.heroId, slot.equipmentSlot);
                if (_equipmentSlots.ContainsKey(key))
                {
                    WasNormalized = true;
                    continue;
                }

                var instance = _itemInstances[slot.itemInstanceId];
                if (string.Equals(instance.stateId, OnStorageItemStateId, StringComparison.Ordinal))
                {
                    instance.stateId = EquippedItemStateId;
                    WasNormalized = true;
                }
                else if (!string.Equals(instance.stateId, EquippedItemStateId, StringComparison.Ordinal))
                {
                    WasNormalized = true;
                    continue;
                }

                _equipmentSlots.Add(key, slot);
                assignedInstances.Add(slot.itemInstanceId);
            }
        }

        private bool TryNormalizeEquipmentSlot(
            EquipmentSlotSaveData entry,
            HashSet<string> assignedInstances,
            out EquipmentSlotSaveData slot)
        {
            slot = null;
            if (entry == null || string.IsNullOrWhiteSpace(entry.heroId) ||
                string.IsNullOrWhiteSpace(entry.equipmentSlot) || string.IsNullOrWhiteSpace(entry.itemInstanceId) ||
                !_acquiredHeroes.Contains(entry.heroId) || !_heroes.ContainsKey(entry.heroId) ||
                !_itemInstances.TryGetValue(entry.itemInstanceId, out var instance) ||
                assignedInstances.Contains(entry.itemInstanceId) ||
                !_configs.TryGetEquipmentSlot(instance.itemId, out var configuredSlot) ||
                !string.Equals(configuredSlot, entry.equipmentSlot, StringComparison.Ordinal))
            {
                return false;
            }

            slot = CloneEquipmentSlot(entry);
            return true;
        }

        private void NormalizeOrphanEquippedInstances()
        {
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in _equipmentSlots.Values)
                assigned.Add(slot.itemInstanceId);

            foreach (var instance in _itemInstances.Values)
            {
                if (string.Equals(instance.stateId, EquippedItemStateId, StringComparison.Ordinal) &&
                    !assigned.Contains(instance.instanceId))
                {
                    instance.stateId = OnStorageItemStateId;
                    WasNormalized = true;
                }
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

        private void LoadActivityRuntime(ActivityRuntimeSaveData runtime)
        {
            _activityExecutions.Clear();
            foreach (var hero in _heroes.Values)
                hero.CurrentActivityExecutionId = null;

            if (runtime?.executions == null)
                return;

            foreach (var execution in runtime.executions)
            {
                if (!ValidateActivityExecution(execution, requireRunning: true))
                    continue;

                if (!_heroes.TryGetValue(execution.heroId, out var hero))
                    continue;

                if (!string.IsNullOrWhiteSpace(hero.CurrentActivityExecutionId))
                {
                    Debug.LogError($"[PlayerState] Ignoring activity execution '{execution.executionId}': hero '{execution.heroId}' is already busy.");
                    continue;
                }

                var stored = CloneExecution(execution);
                _activityExecutions[stored.executionId] = stored;
                hero.CurrentActivityExecutionId = stored.executionId;
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
            if (level < 0)
            {
                Debug.LogError($"[PlayerState] Invalid level '{level}' for building '{buildingId}'.");
                return false;
            }

            if (RuntimeConfigs.Buildings.TryGet(buildingId, out var building) && level <= building.levels)
                return true;

            Debug.LogError($"[PlayerState] Level '{level}' is not available for building '{buildingId}'.");
            return false;
        }

        private static int GetConfiguredStartLevel(string buildingId)
        {
            return RuntimeConfigs.Buildings.TryGet(buildingId, out var building) ? Math.Max(0, building.startLevel) : 0;
        }

        private static string GetBuildingClickableRequirement(string buildingId)
        {
            return RuntimeConfigs.Buildings.TryGet(buildingId, out var building) ? building.clickableRequirement : string.Empty;
        }

        private static bool TryParseBuildingLevelRequirement(string raw, out string buildingId, out int level)
        {
            buildingId = string.Empty;
            level = 0;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var parts = raw.Split(':');
            if (parts.Length != 2)
                return false;

            buildingId = parts[0].Trim();
            return !string.IsNullOrWhiteSpace(buildingId) &&
                   int.TryParse(parts[1].Trim(), out level) &&
                   level >= 0;
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

        private static bool ValidateSkillId(string skillId)
        {
            if (!ValidateConfigsReady("validate skill id"))
                return false;

            if (!string.IsNullOrWhiteSpace(skillId))
            {
                foreach (var skill in RuntimeConfigs.Activities.Skills)
                {
                    if (skill != null && string.Equals(skill.skillId, skillId, StringComparison.Ordinal))
                        return true;
                }
            }

            Debug.LogError($"[PlayerState] Unknown skill id '{skillId}'.");
            return false;
        }

        private static bool ValidatePositiveAmount(long amount, string target)
        {
            if (amount > 0)
                return true;

            Debug.LogError($"[PlayerState] Cannot change {target} by non-positive amount '{amount}'.");
            return false;
        }

        private bool TryGetHeroState(string heroId, out HeroRuntimeState hero)
        {
            hero = null;
            if (!ValidateHeroId(heroId))
                return false;

            if (_heroes.TryGetValue(heroId, out hero))
                return true;

            Debug.LogError($"[PlayerState] Missing hero state for hero '{heroId}'.");
            return false;
        }

        private void EnsureHeroStatesForAcquiredHeroes()
        {
            foreach (var heroId in _acquiredHeroes)
                EnsureHeroState(heroId);
        }

        private HeroRuntimeState EnsureHeroState(string heroId)
        {
            if (_heroes.TryGetValue(heroId, out var hero))
                return hero;

            hero = CreateHeroState(heroId, 1);
            _heroes.Add(heroId, hero);
            return hero;
        }

        private HeroRuntimeState CreateHeroState(string heroId, int level)
        {
            var resolvedLevel = Math.Max(1, level);
            var maxFatigue = _heroStats.CalculateMaxFatigue(heroId, resolvedLevel);
            return new HeroRuntimeState(heroId)
            {
                Level = resolvedLevel,
                Exp = 0L,
                MaxFatigue = maxFatigue,
                Fatigue = maxFatigue
            };
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

        private HeroSaveData[] BuildHeroEntries()
        {
            var keys = SortedKeys(_heroes);
            var entries = new HeroSaveData[keys.Count];
            for (var i = 0; i < keys.Count; i++)
                entries[i] = _heroes[keys[i]].ToSaveData();

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

        private ActivityRuntimeSaveData BuildActivityRuntimeSaveData()
        {
            return new ActivityRuntimeSaveData
            {
                executions = GetActivityExecutions()
            };
        }

        public bool Save()
        {
            return SaveService.Save(this);
        }

        private static string[] BuildSortedArray(HashSet<string> values)
        {
            var list = new List<string>(values);
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        private bool ValidateActivityExecution(ActivityExecutionSaveData execution, bool requireRunning)
        {
            if (execution == null)
            {
                Debug.LogError("[PlayerState] Activity execution is null.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(execution.executionId))
            {
                Debug.LogError("[PlayerState] Activity execution id is empty.");
                return false;
            }

            if (!ValidateActivityId(execution.activityId) || !ValidateHeroId(execution.heroId))
                return false;

            if (!_acquiredHeroes.Contains(execution.heroId))
            {
                Debug.LogError($"[PlayerState] Activity execution '{execution.executionId}' references non-acquired hero '{execution.heroId}'.");
                return false;
            }

            if (requireRunning && execution.status != ActivityRuntimeStatus.Running)
            {
                Debug.LogError($"[PlayerState] Activity execution '{execution.executionId}' has unsupported status '{execution.status}'.");
                return false;
            }

            return true;
        }

        private static ActivityExecutionSaveData CloneExecution(ActivityExecutionSaveData execution)
        {
            if (execution == null)
                return null;

            return new ActivityExecutionSaveData
            {
                executionId = execution.executionId,
                activityId = execution.activityId,
                heroId = execution.heroId,
                status = execution.status,
                elapsedSeconds = Math.Max(0f, execution.elapsedSeconds),
                completedCycles = Math.Max(0, execution.completedCycles),
                startedAtUnixSeconds = execution.startedAtUnixSeconds
            };
        }

        private bool TryNormalizeQuest(QuestSaveData source, out QuestSaveData quest)
        {
            quest = null;
            if (source == null || !_configs.TryGetQuest(source.questId, out _))
                return false;

            var configuredSteps = _configs.GetQuestSteps(source.questId);
            var configuredStepIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var configuredStep in configuredSteps)
            {
                if (configuredStep != null && !string.IsNullOrWhiteSpace(configuredStep.stepId))
                    configuredStepIds.Add(configuredStep.stepId);
            }

            var steps = new List<QuestStepSaveData>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (source.steps != null)
            {
                foreach (var step in source.steps)
                {
                    if (step == null || string.IsNullOrWhiteSpace(step.stepId) ||
                        !configuredStepIds.Contains(step.stepId) || !seen.Add(step.stepId))
                    {
                        continue;
                    }

                    steps.Add(new QuestStepSaveData
                    {
                        stepId = step.stepId,
                        currentValue = Math.Max(0, step.currentValue),
                        completed = step.completed
                    });
                }
            }

            steps.Sort((left, right) => string.CompareOrdinal(left.stepId, right.stepId));
            quest = new QuestSaveData
            {
                questId = source.questId,
                completed = source.completed,
                rewardsGranted = source.rewardsGranted,
                steps = steps.ToArray()
            };
            return true;
        }

        private static QuestSaveData CloneQuest(QuestSaveData source)
        {
            if (source == null)
                return null;

            var sourceSteps = source.steps ?? Array.Empty<QuestStepSaveData>();
            var steps = new QuestStepSaveData[sourceSteps.Length];
            for (var i = 0; i < sourceSteps.Length; i++)
            {
                var step = sourceSteps[i];
                steps[i] = step == null
                    ? null
                    : new QuestStepSaveData
                    {
                        stepId = step.stepId,
                        currentValue = step.currentValue,
                        completed = step.completed
                    };
            }

            return new QuestSaveData
            {
                questId = source.questId,
                completed = source.completed,
                rewardsGranted = source.rewardsGranted,
                steps = steps
            };
        }

        private static ItemInstanceSaveData CloneItemInstance(ItemInstanceSaveData source)
        {
            if (source == null)
                return null;

            return new ItemInstanceSaveData
            {
                instanceId = source.instanceId,
                itemId = source.itemId,
                stateId = source.stateId
            };
        }

        private static EquipmentSlotSaveData CloneEquipmentSlot(EquipmentSlotSaveData source)
        {
            if (source == null)
                return null;

            return new EquipmentSlotSaveData
            {
                heroId = source.heroId,
                equipmentSlot = source.equipmentSlot,
                itemInstanceId = source.itemInstanceId
            };
        }

        private static string EquipmentSlotKey(string heroId, string equipmentSlot)
        {
            return $"{heroId}\n{equipmentSlot}";
        }

        private bool IsInstanceEquipped(string instanceId)
        {
            foreach (var slot in _equipmentSlots.Values)
            {
                if (string.Equals(slot.itemInstanceId, instanceId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private string NewUniqueInstanceId()
        {
            string instanceId;
            do
            {
                instanceId = Guid.NewGuid().ToString("N");
            }
            while (_itemInstances.ContainsKey(instanceId));

            return instanceId;
        }

        private static List<string> SortedKeys<T>(Dictionary<string, T> dictionary)
        {
            var keys = new List<string>(dictionary.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        private sealed class HeroRuntimeState
        {
            public HeroRuntimeState(string heroId)
            {
                HeroId = heroId;
            }

            public string HeroId { get; }
            public int Level { get; set; }
            public long Exp { get; set; }
            public int Fatigue { get; set; }
            public int MaxFatigue { get; set; }
            public string CurrentActivityExecutionId { get; set; }
            public Dictionary<string, HeroSkillRuntimeState> Skills { get; } = new Dictionary<string, HeroSkillRuntimeState>(StringComparer.Ordinal);
            public Dictionary<string, long> EffectCounters { get; } = new Dictionary<string, long>(StringComparer.Ordinal);

            public HeroSaveData ToSaveData()
            {
                return new HeroSaveData
                {
                    heroId = HeroId,
                    level = Level,
                    exp = Exp,
                    fatigue = Fatigue,
                    maxFatigue = MaxFatigue,
                    currentActivityExecutionId = CurrentActivityExecutionId,
                    skills = BuildSkillEntries(),
                    effectCounters = BuildEffectCounterEntries()
                };
            }

            private HeroSkillSaveData[] BuildSkillEntries()
            {
                var keys = SortedKeys(Skills);
                var entries = new HeroSkillSaveData[keys.Count];
                for (var i = 0; i < keys.Count; i++)
                    entries[i] = Skills[keys[i]].ToSaveData();

                return entries;
            }

            private HeroEffectCounterSaveData[] BuildEffectCounterEntries()
            {
                var keys = SortedKeys(EffectCounters);
                var entries = new HeroEffectCounterSaveData[keys.Count];
                for (var i = 0; i < keys.Count; i++)
                {
                    var key = keys[i];
                    entries[i] = new HeroEffectCounterSaveData { effectId = key, value = EffectCounters[key] };
                }

                return entries;
            }
        }

        private sealed class HeroSkillRuntimeState
        {
            public HeroSkillRuntimeState(string skillId)
            {
                SkillId = skillId;
                Level = 1;
            }

            public string SkillId { get; }
            public int Level { get; set; }
            public long Exp { get; set; }

            public HeroSkillSaveData ToSaveData()
            {
                return new HeroSkillSaveData
                {
                    skillId = SkillId,
                    level = Level,
                    exp = Exp
                };
            }
        }
    }
}
