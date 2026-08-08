using System;
using System.Collections.Generic;
using GuildIdle.Combat;
using GuildIdle.Configs;
using GuildIdle.Crafting;
using GuildIdle.Core;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Player
{
    public sealed class PlayerState : IActivityRuntimeStore, ICombatRuntimeStore, IRewardBatchStore
    {
        internal const int OperationReceiptRetentionLimit = 64;
        internal const int ResolvedResultSourceRetentionLimit = 64;

        public const string EquippedItemStateId = "equipped";
        public const string OnStorageItemStateId = "on_storage";

        private readonly HeroStatsService _heroStats;
        private readonly IPlayerBootstrapConfigProvider _configs;
        private readonly Dictionary<string, long> _currencies = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, ItemStackSaveData> _itemStacks = new Dictionary<string, ItemStackSaveData>(StringComparer.Ordinal);
        private readonly Dictionary<string, ItemInstanceSaveData> _itemInstances = new Dictionary<string, ItemInstanceSaveData>(StringComparer.Ordinal);
        private readonly Dictionary<string, EquipmentSlotSaveData> _equipmentSlots = new Dictionary<string, EquipmentSlotSaveData>(StringComparer.Ordinal);
        private readonly HashSet<string> _unlockedHeroes = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _acquiredHeroes = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, HeroRuntimeState> _heroes = new Dictionary<string, HeroRuntimeState>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _fatigueRemainderSeconds = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _unlockedBuildings = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _buildingLevels = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _unlockedLocations = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _completedActivities = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _availableActivities = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, ActivityExecutionSaveData> _activityExecutions = new Dictionary<string, ActivityExecutionSaveData>(StringComparer.Ordinal);
        private readonly Dictionary<string, CraftExecutionSaveData> _craftExecutions = new Dictionary<string, CraftExecutionSaveData>(StringComparer.Ordinal);
        private readonly Dictionary<string, CombatRuntimeAggregate> _combatAggregates = new Dictionary<string, CombatRuntimeAggregate>(StringComparer.Ordinal);
        private readonly Dictionary<string, QuestInstanceSaveData> _questInstances = new Dictionary<string, QuestInstanceSaveData>(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingResultSourceReferenceSaveData> _resultSources = new Dictionary<string, PendingResultSourceReferenceSaveData>(StringComparer.Ordinal);
        private readonly List<OperationReceiptSaveData> _operationReceipts = new List<OperationReceiptSaveData>();
        private long _lastResultSourceResolutionSequence;
        private long _lastCombatResultSequence;
        private Func<bool> _saveHandler;
        private string _currentStageId;
        private bool _timeBaselineInitialized;
        private long _lastProcessedUtcSeconds;

        public PlayerState(
            SaveData saveData,
            HeroStatsService heroStats,
            IPlayerBootstrapConfigProvider configs,
            IEnumerable<Func<PlayerState, IPendingResultSourceHandler>> pendingResultSourceHandlerFactories = null,
            ITimeProvider timeProvider = null)
        {
            _heroStats = heroStats ?? throw new ArgumentNullException(nameof(heroStats));
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            TimeProgress = new TimeProgressService(this, timeProvider ?? SystemUtcTimeProvider.Instance);
            var storage = new StorageService(this, _configs);
            Storage = storage;
            PendingResults = new PendingResultService(this, storage);
            foreach (var factory in pendingResultSourceHandlerFactories ?? Array.Empty<Func<PlayerState, IPendingResultSourceHandler>>())
            {
                var handler = factory?.Invoke(this);
                if (handler != null)
                    PendingResults.RegisterSourceHandler(handler);
            }
            Load(saveData);
        }

        internal bool WasNormalized { get; private set; }
        public string CurrentStageId => _currentStageId;
        public IStorageService Storage { get; }
        public IPendingResultService PendingResults { get; }
        public TimeProgressService TimeProgress { get; }
        internal long StorageRevision { get; set; }
        internal Dictionary<string, ItemStackSaveData> MutableItemStacks => _itemStacks;
        internal Dictionary<string, ItemInstanceSaveData> MutableItemInstances => _itemInstances;
        internal Dictionary<string, EquipmentSlotSaveData> MutableEquipmentSlots => _equipmentSlots;
        internal IPlayerBootstrapConfigProvider ConfigProvider => _configs;

        public SaveData ToSaveData()
        {
            return new SaveData
            {
                saveVersion = SaveData.CurrentSaveVersion,
                currentStageId = _currentStageId,
                currencies = BuildCurrencyEntries(),
                storageRevision = StorageRevision,
                itemStacks = GetItemStacks(),
                itemInstances = GetItemInstances(),
                equipmentSlots = GetEquipmentSlots(),
                unlockedHeroes = BuildSortedArray(_unlockedHeroes),
                acquiredHeroes = BuildSortedArray(_acquiredHeroes),
                heroes = BuildHeroEntries(),
                timeProgress = BuildTimeProgressSaveData(),
                questInstances = GetQuestInstances(),
                unlockedBuildings = BuildSortedArray(_unlockedBuildings),
                buildingLevels = BuildBuildingLevelEntries(),
                unlockedLocations = BuildSortedArray(_unlockedLocations),
                completedActivities = BuildSortedArray(_completedActivities),
                availableActivities = BuildSortedArray(_availableActivities),
                activityRuntime = BuildActivityRuntimeSaveData(),
                craftRuntime = BuildCraftRuntimeSaveData(),
                combatRuntime = BuildCombatRuntimeSaveData(),
                pendingResults = PendingResults.GetSaveData(),
                resultSources = BuildResultSourceReferences(),
                lastCombatResultSequence = _lastCombatResultSequence,
                operationReceipts = BuildOperationReceipts()
            };
        }

        public bool SetCurrentStage(string stageId)
        {
            if (!_configs.TryGetStage(stageId, out var stage) || stage == null || !stage.enabled)
            {
                Debug.LogError($"[PlayerState] Unknown or disabled stage id '{stageId}'.");
                return false;
            }

            _currentStageId = stageId;
            return true;
        }

        public QuestInstanceSaveData GetQuestInstance(string instanceId)
        {
            return !string.IsNullOrWhiteSpace(instanceId) && _questInstances.TryGetValue(instanceId, out var quest)
                ? CloneQuestInstance(quest)
                : null;
        }

        public QuestInstanceSaveData[] GetQuestInstances()
        {
            var keys = SortedKeys(_questInstances);
            var entries = new QuestInstanceSaveData[keys.Count];
            for (var i = 0; i < keys.Count; i++)
                entries[i] = CloneQuestInstance(_questInstances[keys[i]]);

            return entries;
        }

        public bool SetQuestInstance(QuestInstanceSaveData quest)
        {
            if (!TryNormalizeQuestInstance(quest, out var normalized, out _))
                return false;

            _questInstances[normalized.instanceId] = normalized;
            return true;
        }

        public bool TryApplyRewardBatch(
            RewardMutation[] mutations,
            out RewardMutationResult[] results,
            out string error)
        {
            mutations ??= Array.Empty<RewardMutation>();
            var before = ToSaveData();
            var wasNormalized = WasNormalized;
            var applied = new RewardMutationResult[mutations.Length];

            for (var i = 0; i < mutations.Length; i++)
            {
                var mutation = mutations[i];
                if (!TryApplyRewardMutation(mutation, out var changed, out error))
                {
                    Restore(before, wasNormalized);
                    results = Array.Empty<RewardMutationResult>();
                    return false;
                }

                applied[i] = new RewardMutationResult(mutation, changed);
            }

            results = applied;
            error = null;
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
            if (!_fatigueRemainderSeconds.ContainsKey(heroId))
                _fatigueRemainderSeconds[heroId] = 0;
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

        public int CalculateHeroStat(string heroId, string statId)
        {
            var hero = GetHeroState(heroId);
            return hero == null ? 0 : _heroStats.CalculateHeroStat(heroId, statId, hero.level);
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

        public int GetActiveHeroCount()
        {
            var count = 0;
            foreach (var hero in _heroes.Values)
                if (!string.IsNullOrWhiteSpace(hero.CurrentActivityExecutionId)) count++;
            return count;
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
            return ValidateItemId(itemId) ? Storage.GetOwnedInStorageCount(itemId) : 0;
        }

        public bool HasItem(string itemId, int amount)
        {
            if (!ValidateItemId(itemId))
                return false;

            return amount <= 0 || Storage.GetAvailableForActionCount(itemId, null) >= amount;
        }

        public bool AddItem(string itemId, int amount)
        {
            if (!ValidateItemId(itemId) || !ValidatePositiveAmount(amount, "item"))
                return false;

            return Storage.Add($"legacy:add:{Guid.NewGuid():N}", Storage.GetSnapshot().Revision, itemId, amount).Success;
        }

        public bool SpendItem(string itemId, int amount)
        {
            if (!ValidateItemId(itemId) || !ValidatePositiveAmount(amount, "item"))
                return false;

            return Storage.Consume($"legacy:consume:{Guid.NewGuid():N}", Storage.GetSnapshot().Revision, itemId, amount).Success;
        }

        public int GetAvailableForActionCount(string itemId, StorageActionContext actionContext) => Storage.GetAvailableForActionCount(itemId, actionContext);

        public ItemStackSaveData[] GetItemStacks()
        {
            var keys = SortedKeys(_itemStacks);
            var entries = new ItemStackSaveData[keys.Count];
            for (var index = 0; index < keys.Count; index++)
                entries[index] = CloneItemStack(_itemStacks[keys[index]]);
            return entries;
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
            if (!_configs.TryGetItem(itemId, out var item) || item == null || !_configs.TryGetStorageRuleForItemKind(item.Kind, out var rule) ||
                !string.Equals(rule.mode, "single", StringComparison.Ordinal) || !_configs.TryGetItemState(stateId, out var itemState) ||
                !string.Equals(itemState.availabilityMode, ItemAvailabilityMode.Available, StringComparison.Ordinal))
            {
                Debug.LogError($"[PlayerState] Cannot create item instance '{itemId}' with state '{stateId}'.");
                return null;
            }

            var instanceId = NewUniqueInstanceId();
            _itemInstances.Add(instanceId, new ItemInstanceSaveData
            {
                instanceId = instanceId,
                itemId = itemId,
                quality = 0,
                stateId = stateId
            });
            return instanceId;
        }

        public bool EquipItemInstance(string heroId, string equipmentSlot, string instanceId)
        {
            return Storage.Equip($"legacy:equip:{Guid.NewGuid():N}", Storage.GetSnapshot().Revision, heroId, equipmentSlot, instanceId).Success;
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

            return _unlockedBuildings.Add(buildingId);
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

        public bool TryGetBuildingLevelState(string buildingId, out int level)
        {
            level = 0;
            return ValidateBuildingId(buildingId) && _buildingLevels.TryGetValue(buildingId, out level);
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
            if (!ValidateActivityExecution(execution, requireRunning: false))
                return false;

            if (_activityExecutions.ContainsKey(execution.executionId) || _craftExecutions.ContainsKey(execution.executionId) ||
                _combatAggregates.ContainsKey(execution.executionId))
            {
                Debug.LogError($"[PlayerState] Execution owner id '{execution.executionId}' already exists.");
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
            if (!ValidateActivityExecution(execution, requireRunning: false))
                return false;

            if (!_activityExecutions.TryGetValue(execution.executionId, out var previous))
            {
                Debug.LogError($"[PlayerState] Cannot update missing activity execution '{execution.executionId}'.");
                return false;
            }

            var previousOccupies = OccupiesHero(previous);
            var nextOccupies = OccupiesHero(execution);
            if (nextOccupies && _heroes.TryGetValue(execution.heroId, out var nextHero) &&
                !string.IsNullOrWhiteSpace(nextHero.CurrentActivityExecutionId) &&
                !string.Equals(nextHero.CurrentActivityExecutionId, execution.executionId, StringComparison.Ordinal))
            {
                Debug.LogError($"[PlayerState] Hero '{execution.heroId}' is already busy with execution '{nextHero.CurrentActivityExecutionId}'.");
                return false;
            }

            _activityExecutions[execution.executionId] = CloneExecution(execution);
            if (previousOccupies && (!nextOccupies || !string.Equals(previous.heroId, execution.heroId, StringComparison.Ordinal)) &&
                _heroes.TryGetValue(previous.heroId, out var previousHero) &&
                string.Equals(previousHero.CurrentActivityExecutionId, execution.executionId, StringComparison.Ordinal))
            {
                previousHero.CurrentActivityExecutionId = null;
            }
            if (nextOccupies && _heroes.TryGetValue(execution.heroId, out nextHero))
                nextHero.CurrentActivityExecutionId = execution.executionId;
            return true;
        }

        public bool RemoveActivityExecution(string executionId)
        {
            if (string.IsNullOrWhiteSpace(executionId))
            {
                Debug.LogError("[PlayerState] Cannot remove activity execution with empty id.");
                return false;
            }

            if (!_activityExecutions.ContainsKey(executionId))
                return false;

            _activityExecutions.Remove(executionId);
            foreach (var hero in _heroes.Values)
                if (string.Equals(hero.CurrentActivityExecutionId, executionId, StringComparison.Ordinal))
                    hero.CurrentActivityExecutionId = null;

            return true;
        }

        public CraftExecutionSaveData[] GetCraftExecutions()
        {
            var keys = SortedKeys(_craftExecutions);
            var entries = new CraftExecutionSaveData[keys.Count];
            for (var index = 0; index < keys.Count; index++)
                entries[index] = CloneCraftExecution(_craftExecutions[keys[index]]);
            return entries;
        }

        public CraftExecutionSaveData GetCraftExecution(string executionId)
        {
            return !string.IsNullOrWhiteSpace(executionId) && _craftExecutions.TryGetValue(executionId, out var execution)
                ? CloneCraftExecution(execution)
                : null;
        }

        public bool AddCraftExecution(CraftExecutionSaveData execution)
        {
            if (!ValidateCraftExecution(execution) || _craftExecutions.ContainsKey(execution.executionId) ||
                _activityExecutions.ContainsKey(execution.executionId) || _combatAggregates.ContainsKey(execution.executionId))
            {
                Debug.LogError($"[PlayerState] Craft execution '{execution?.executionId}' is invalid or already exists.");
                return false;
            }
            if (!_heroes.TryGetValue(execution.heroId, out var hero) ||
                !string.Equals(hero.CurrentActivityExecutionId, execution.executionId, StringComparison.Ordinal))
            {
                Debug.LogError($"[PlayerState] Hero '{execution.heroId}' must be occupied by craft execution '{execution.executionId}' before it is added.");
                return false;
            }

            _craftExecutions.Add(execution.executionId, CloneCraftExecution(execution));
            return true;
        }

        public bool UpdateCraftExecution(CraftExecutionSaveData execution)
        {
            if (!ValidateCraftExecution(execution) || !_craftExecutions.TryGetValue(execution.executionId, out var previous) ||
                !HasSameCraftSnapshot(previous, execution))
                return false;
            if (!_heroes.TryGetValue(execution.heroId, out var hero) ||
                !string.Equals(hero.CurrentActivityExecutionId, execution.executionId, StringComparison.Ordinal))
            {
                Debug.LogError($"[PlayerState] Craft execution '{execution.executionId}' does not own hero '{execution.heroId}'.");
                return false;
            }
            _craftExecutions[execution.executionId] = CloneCraftExecution(execution);
            return true;
        }

        internal bool RemoveCraftExecution(string executionId)
        {
            if (string.IsNullOrWhiteSpace(executionId) || !_craftExecutions.ContainsKey(executionId))
                return false;
            return _craftExecutions.Remove(executionId);
        }

        public CombatRuntimeAggregate[] GetCombatAggregates()
        {
            var keys = SortedKeys(_combatAggregates);
            var result = new CombatRuntimeAggregate[keys.Count];
            for (var index = 0; index < result.Length; index++)
                result[index] = CombatRuntimeSaveDataUtility.CloneAggregate(_combatAggregates[keys[index]]);
            return result;
        }

        public CombatRuntimeAggregate GetCombatAggregate(string executionId)
        {
            return !string.IsNullOrWhiteSpace(executionId) && _combatAggregates.TryGetValue(executionId, out var aggregate)
                ? CombatRuntimeSaveDataUtility.CloneAggregate(aggregate)
                : null;
        }

        public bool AddCombatAggregate(CombatRuntimeAggregate aggregate)
        {
            if (!TryNormalizeCombatAggregate(aggregate, out var normalized))
                return false;
            var execution = normalized.execution;
            if (execution.status != CombatExecutionStatus.Running ||
                _combatAggregates.Count >= CombatRuntimeSaveDataUtility.PersistentCollectionLimit ||
                _combatAggregates.ContainsKey(execution.executionId) || _activityExecutions.ContainsKey(execution.executionId) ||
                _craftExecutions.ContainsKey(execution.executionId) ||
                HasCombatSession(normalized.session.sessionId) ||
                HasOtherUnfinishedCombat(execution.heroId, null))
            {
                Debug.LogError($"[PlayerState] Combat execution '{execution.executionId}' or session '{normalized.session.sessionId}' is duplicated, or its hero already has unfinished combat.");
                return false;
            }
            if (!CanApplyCombatOwnership(normalized))
                return false;

            _combatAggregates.Add(execution.executionId, normalized);
            ApplyCombatOwnership(normalized);
            return true;
        }

        private bool HasCombatSession(string sessionId)
        {
            foreach (var aggregate in _combatAggregates.Values)
            {
                if (string.Equals(
                        aggregate?.session?.sessionId,
                        sessionId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public bool UpdateCombatAggregate(CombatRuntimeAggregate aggregate)
        {
            if (!TryNormalizeCombatAggregate(aggregate, out var normalized))
                return false;
            var executionId = normalized.execution.executionId;
            if (!_combatAggregates.TryGetValue(executionId, out var previous) ||
                !CombatRuntimeSaveDataUtility.HasSameIdentity(previous, normalized) ||
                !IsValidCombatLifecycleTransition(previous.execution, normalized.execution) ||
                HasOtherUnfinishedCombat(normalized.execution.heroId, executionId) ||
                !CanApplyCombatOwnership(normalized))
            {
                Debug.LogError($"[PlayerState] Combat execution '{executionId}' update violates aggregate identity, lifecycle or hero ownership.");
                return false;
            }

            _combatAggregates[executionId] = normalized;
            if (CombatRuntimeSaveDataUtility.IsUnfinished(normalized.execution))
                ApplyCombatOwnership(normalized);
            else
                ReleaseDirectCombatOwnership(previous);
            return true;
        }

        public bool RemoveCombatAggregate(string executionId)
        {
            if (string.IsNullOrWhiteSpace(executionId) || !_combatAggregates.TryGetValue(executionId, out var aggregate) ||
                CombatRuntimeSaveDataUtility.IsUnfinished(aggregate.execution))
                return false;
            _combatAggregates.Remove(executionId);
            ReleaseDirectCombatOwnership(aggregate);
            return true;
        }

        private void Load(SaveData saveData)
        {
            saveData ??= new SaveData();

            _currentStageId = string.IsNullOrWhiteSpace(saveData.currentStageId) ? null : saveData.currentStageId;
            if (saveData.lastCombatResultSequence < 0)
                WasNormalized = true;
            _lastCombatResultSequence = Math.Max(0, saveData.lastCombatResultSequence);
            if (saveData.storageRevision < 0)
                WasNormalized = true;
            StorageRevision = Math.Max(0, saveData.storageRevision);
            LoadCurrencies(saveData.currencies);
            LoadItemStacks(saveData.itemStacks);
            LoadItemInstances(saveData.itemInstances);
            LoadHeroes(saveData.unlockedHeroes, _unlockedHeroes);
            LoadHeroes(saveData.acquiredHeroes, _acquiredHeroes);
            LoadHeroStates(saveData.heroes);
            LoadTimeProgress(saveData.timeProgress);
            LoadQuestInstances(saveData.questInstances);
            LoadBuildings(saveData.unlockedBuildings);
            LoadBuildingLevels(saveData.buildingLevels);
            LoadLocations(saveData.unlockedLocations);
            LoadActivities(saveData.completedActivities, _completedActivities);
            LoadActivities(saveData.availableActivities, _availableActivities);
            EnsureHeroStatesForAcquiredHeroes();
            EnsureFatigueRemaindersForHeroes();
            LoadEquipmentSlots(saveData.equipmentSlots);
            NormalizeOrphanEquippedInstances();
            LoadActivityRuntime(saveData.activityRuntime);
            LoadCraftRuntime(saveData.craftRuntime);
            LoadCombatRuntime(saveData.combatRuntime);
            LoadResultSourceReferences(saveData.resultSources);
            LoadOperationReceipts(saveData.operationReceipts);
            PendingResults.Load(saveData.pendingResults);
            if (TrimResolvedResultSources())
                WasNormalized = true;
        }

        private void Restore(SaveData saveData, bool wasNormalized)
        {
            _currencies.Clear();
            _itemStacks.Clear();
            _itemInstances.Clear();
            _equipmentSlots.Clear();
            _unlockedHeroes.Clear();
            _acquiredHeroes.Clear();
            _heroes.Clear();
            _fatigueRemainderSeconds.Clear();
            _unlockedBuildings.Clear();
            _buildingLevels.Clear();
            _unlockedLocations.Clear();
            _completedActivities.Clear();
            _availableActivities.Clear();
            _activityExecutions.Clear();
            _craftExecutions.Clear();
            _combatAggregates.Clear();
            _questInstances.Clear();
            _resultSources.Clear();
            _operationReceipts.Clear();
            _timeBaselineInitialized = false;
            _lastProcessedUtcSeconds = 0L;
            PendingResults.Load(Array.Empty<PendingResultSaveData>());
            WasNormalized = false;
            Load(saveData);
            WasNormalized = wasNormalized;
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

        private void LoadItemStacks(ItemStackSaveData[] entries)
        {
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (entry == null || entry.quantity <= 0 || !ValidateItemId(entry.itemId) ||
                    !_configs.TryGetItem(entry.itemId, out var item) || item == null || !_configs.TryGetStorageRuleForItemKind(item.Kind, out var rule) || !string.Equals(rule.mode, "stack", StringComparison.Ordinal))
                    continue;
                var stackId = string.IsNullOrWhiteSpace(entry.stackId) || _itemStacks.ContainsKey(entry.stackId) ? NewUniqueStackId() : entry.stackId;
                if (!string.Equals(stackId, entry.stackId, StringComparison.Ordinal))
                    WasNormalized = true;
                var stateId = _configs.IsKnownItemState(entry.stateId) ? entry.stateId : GetAvailableStateId();
                if (!string.Equals(stateId, entry.stateId, StringComparison.Ordinal))
                    WasNormalized = true;
                var ownershipNormalized = NormalizeOwnership(entry.ownerType, entry.ownerId, entry.contextType, entry.contextId, out var ownerType, out var ownerId, out var contextType, out var contextId);
                WasNormalized |= ownershipNormalized;
                var bindingsChanged = false;
                if (_configs.TryGetItemState(stateId, out var state) &&
                    !TryNormalizeStateBindings(state, ref ownerType, ref ownerId, ref contextType, ref contextId, out bindingsChanged))
                {
                    stateId = GetAvailableStateId();
                    ownerType = ownerId = contextType = contextId = null;
                    WasNormalized = true;
                }
                else
                {
                    WasNormalized |= bindingsChanged;
                }
                var remaining = entry.quantity;
                var first = true;
                while (remaining > 0)
                {
                    var partId = first ? stackId : NewUniqueStackId();
                    var partQuantity = Math.Min(remaining, rule.maxStack);
                    _itemStacks.Add(partId, new ItemStackSaveData
                    {
                        stackId = partId,
                        itemId = entry.itemId,
                        quantity = partQuantity,
                        stateId = stateId,
                        ownerType = ownerType,
                        ownerId = ownerId,
                        contextType = contextType,
                        contextId = contextId
                    });
                    remaining -= partQuantity;
                    first = false;
                }
                if (entry.quantity > rule.maxStack)
                    WasNormalized = true;
            }
        }

        private void LoadItemInstances(ItemInstanceSaveData[] entries)
        {
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (entry == null || !_configs.TryGetItem(entry.itemId, out var item) || item == null ||
                    !_configs.TryGetStorageRuleForItemKind(item.Kind, out var rule) || !string.Equals(rule.mode, "single", StringComparison.Ordinal))
                    continue;

                var instanceId = entry.instanceId;
                if (string.IsNullOrWhiteSpace(instanceId) || _itemInstances.ContainsKey(instanceId))
                {
                    instanceId = NewUniqueInstanceId();
                    WasNormalized = true;
                }

                var stateId = entry.stateId;
                if (!_configs.IsKnownItemState(stateId))
                {
                    stateId = GetAvailableStateId();
                    WasNormalized = true;
                }

                var ownershipNormalized = NormalizeOwnership(entry.ownerType, entry.ownerId, entry.contextType, entry.contextId, out var ownerType, out var ownerId, out var contextType, out var contextId);
                WasNormalized |= ownershipNormalized;
                var bindingsChanged = false;
                if (_configs.TryGetItemState(stateId, out var state) &&
                    !TryNormalizeStateBindings(state, ref ownerType, ref ownerId, ref contextType, ref contextId, out bindingsChanged))
                {
                    stateId = GetAvailableStateId();
                    ownerType = ownerId = contextType = contextId = null;
                    WasNormalized = true;
                }
                else
                {
                    WasNormalized |= bindingsChanged;
                }

                _itemInstances.Add(instanceId, new ItemInstanceSaveData
                {
                    instanceId = instanceId,
                    itemId = entry.itemId,
                    quality = Math.Max(0, entry.quality),
                    stateId = stateId,
                    ownerType = ownerType,
                    ownerId = ownerId,
                    contextType = contextType,
                    contextId = contextId
                });
                if (entry.quality < 0)
                    WasNormalized = true;
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

        private void LoadTimeProgress(TimeProgressSaveData source)
        {
            if (source == null)
            {
                WasNormalized = true;
                return;
            }

            if (source.baselineInitialized && source.lastProcessedUtcSeconds >= 0L)
            {
                _timeBaselineInitialized = true;
                _lastProcessedUtcSeconds = source.lastProcessedUtcSeconds;
            }
            else
            {
                if (source.baselineInitialized || source.lastProcessedUtcSeconds != 0L)
                    WasNormalized = true;
                _timeBaselineInitialized = false;
                _lastProcessedUtcSeconds = 0L;
            }

            foreach (var entry in source.fatigueRemainders ?? Array.Empty<HeroFatigueRemainderSaveData>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.heroId) ||
                    !_heroes.ContainsKey(entry.heroId) || _fatigueRemainderSeconds.ContainsKey(entry.heroId))
                {
                    WasNormalized = true;
                    continue;
                }

                var remainder = Math.Max(
                    0,
                    Math.Min(TimeProgressService.FatigueRecoveryIntervalSeconds - 1, entry.fatigueRemainderSeconds));
                if (remainder != entry.fatigueRemainderSeconds)
                    WasNormalized = true;
                if (_heroes[entry.heroId].Fatigue >= _heroes[entry.heroId].MaxFatigue && remainder != 0)
                {
                    remainder = 0;
                    WasNormalized = true;
                }

                _fatigueRemainderSeconds[entry.heroId] = remainder;
            }

            if (!_timeBaselineInitialized && _fatigueRemainderSeconds.Count > 0)
            {
                _fatigueRemainderSeconds.Clear();
                WasNormalized = true;
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

        private void LoadQuestInstances(QuestInstanceSaveData[] entries)
        {
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (!TryNormalizeQuestInstance(entry, out var quest, out var normalized) || _questInstances.ContainsKey(quest.instanceId))
                {
                    WasNormalized = true;
                    continue;
                }

                _questInstances.Add(quest.instanceId, quest);
                WasNormalized |= normalized;
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
                _configs.TryGetItemState(instance.stateId, out var instanceState);
                if (string.Equals(instanceState?.availabilityMode, ItemAvailabilityMode.Available, StringComparison.Ordinal) &&
                    _configs.TryGetItemStateByAvailabilityMode(ItemAvailabilityMode.Equipped, out var equippedState))
                {
                    instance.stateId = equippedState.stateId;
                    instance.ownerType = StorageOwnerType.Hero;
                    instance.ownerId = slot.heroId;
                    WasNormalized = true;
                }
                else if (!string.Equals(instanceState?.availabilityMode, ItemAvailabilityMode.Equipped, StringComparison.Ordinal))
                {
                    WasNormalized = true;
                    continue;
                }
                else if (!string.Equals(instance.ownerType, StorageOwnerType.Hero, StringComparison.Ordinal) || !string.Equals(instance.ownerId, slot.heroId, StringComparison.Ordinal))
                {
                    instance.ownerType = StorageOwnerType.Hero;
                    instance.ownerId = slot.heroId;
                    instance.contextType = null;
                    instance.contextId = null;
                    WasNormalized = true;
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
                if (_configs.TryGetItemState(instance.stateId, out var state) && string.Equals(state.availabilityMode, ItemAvailabilityMode.Equipped, StringComparison.Ordinal) &&
                    !assigned.Contains(instance.instanceId))
                {
                    if (_configs.TryGetItemStateByAvailabilityMode(ItemAvailabilityMode.Available, out var availableState))
                        instance.stateId = availableState.stateId;
                    instance.ownerType = null;
                    instance.ownerId = null;
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
                if (!ValidateActivityExecution(execution, requireRunning: false))
                    continue;
                if (execution.status == ActivityRuntimeStatus.Completed || execution.status == ActivityRuntimeStatus.Cancelled)
                {
                    WasNormalized = true;
                    continue;
                }

                var keepsHeroBusy = execution.status == ActivityRuntimeStatus.Running || execution.status == ActivityRuntimeStatus.ResultPending;
                HeroRuntimeState hero = null;
                if (keepsHeroBusy && !_heroes.TryGetValue(execution.heroId, out hero))
                    continue;
                if (keepsHeroBusy && !string.IsNullOrWhiteSpace(hero.CurrentActivityExecutionId))
                {
                    Debug.LogError($"[PlayerState] Ignoring activity execution '{execution.executionId}': hero '{execution.heroId}' is already busy.");
                    continue;
                }

                var stored = CloneExecution(execution);
                _activityExecutions[stored.executionId] = stored;
                if (keepsHeroBusy)
                    hero.CurrentActivityExecutionId = stored.executionId;
            }
        }

        private void LoadCraftRuntime(CraftRuntimeSaveData runtime)
        {
            _craftExecutions.Clear();
            if (runtime?.executions == null)
                return;

            foreach (var execution in runtime.executions)
            {
                var stored = CloneCraftExecution(execution);
                if (NormalizeLegacyCraftAdvanceSequences(stored))
                    WasNormalized = true;
                if (!ValidateCraftExecution(stored) || _craftExecutions.ContainsKey(stored.executionId) ||
                    _activityExecutions.ContainsKey(stored.executionId))
                {
                    WasNormalized = true;
                    continue;
                }
                if (!_heroes.TryGetValue(stored.heroId, out var hero) ||
                    !string.IsNullOrWhiteSpace(hero.CurrentActivityExecutionId))
                {
                    Debug.LogError($"[PlayerState] Ignoring craft execution '{stored.executionId}': hero '{stored.heroId}' is already busy or missing.");
                    WasNormalized = true;
                    continue;
                }

                if (stored.advanceReceipts.Length > OperationReceiptRetentionLimit)
                {
                    var retained = new CraftAdvanceReceiptSaveData[OperationReceiptRetentionLimit];
                    Array.Copy(
                        stored.advanceReceipts,
                        stored.advanceReceipts.Length - OperationReceiptRetentionLimit,
                        retained,
                        0,
                        retained.Length);
                    stored.advanceReceipts = retained;
                    WasNormalized = true;
                }
                _craftExecutions.Add(stored.executionId, stored);
                hero.CurrentActivityExecutionId = stored.executionId;
            }
        }

        private void LoadCombatRuntime(CombatRuntimeSaveData runtime)
        {
            _combatAggregates.Clear();
            if (runtime == null)
            {
                WasNormalized = true;
                return;
            }

            var executions = runtime.executions ?? Array.Empty<CombatExecutionSaveData>();
            var sessions = runtime.sessions ?? Array.Empty<CombatSessionSaveData>();
            if (runtime.executions == null || runtime.sessions == null)
                WasNormalized = true;
            if (executions.Length > CombatRuntimeSaveDataUtility.PersistentCollectionLimit ||
                sessions.Length > CombatRuntimeSaveDataUtility.PersistentCollectionLimit)
            {
                Debug.LogError("[PlayerState] Combat runtime exceeds the persistent aggregate limit and was rejected.");
                WasNormalized = true;
                return;
            }

            var duplicateExecutionIds = FindDuplicateExecutionIds(executions);
            var duplicateSessionIds = FindDuplicateSessionIds(sessions);
            var duplicateSessionExecutionIds = FindDuplicateSessionExecutionIds(sessions);
            var orderedExecutions = new List<CombatExecutionSaveData>(executions);
            orderedExecutions.Sort((left, right) => string.Compare(left?.executionId, right?.executionId, StringComparison.Ordinal));
            var staged = new List<CombatRuntimeAggregate>();
            var acceptedSessionIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var execution in orderedExecutions)
            {
                if (execution == null || string.IsNullOrWhiteSpace(execution.executionId) ||
                    duplicateExecutionIds.Contains(execution.executionId))
                {
                    WasNormalized = true;
                    continue;
                }

                CombatSessionSaveData matchingSession = null;
                foreach (var session in sessions)
                {
                    if (session != null && string.Equals(session.sessionId, execution.sessionId, StringComparison.Ordinal) &&
                        string.Equals(session.executionId, execution.executionId, StringComparison.Ordinal))
                    {
                        matchingSession = session;
                        break;
                    }
                }
                string error = null;
                if (matchingSession == null || duplicateSessionIds.Contains(matchingSession.sessionId) ||
                    duplicateSessionExecutionIds.Contains(matchingSession.executionId) ||
                    !CombatRuntimeSaveDataUtility.TryNormalize(execution, matchingSession, out var aggregate, out var changed, out error) ||
                    _activityExecutions.ContainsKey(execution.executionId) || _craftExecutions.ContainsKey(execution.executionId))
                {
                    Debug.LogError($"[PlayerState] Combat aggregate '{execution.executionId}' was rejected during load. {error}");
                    WasNormalized = true;
                    continue;
                }

                WasNormalized |= changed;
                staged.Add(aggregate);
                acceptedSessionIds.Add(aggregate.session.sessionId);
            }

            foreach (var session in sessions)
            {
                if (session == null || string.IsNullOrWhiteSpace(session.sessionId) || !acceptedSessionIds.Contains(session.sessionId))
                    WasNormalized = true;
            }

            var unfinishedHeroCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var aggregate in staged)
            {
                if (!CombatRuntimeSaveDataUtility.IsUnfinished(aggregate.execution))
                    continue;
                unfinishedHeroCounts.TryGetValue(aggregate.execution.heroId, out var count);
                unfinishedHeroCounts[aggregate.execution.heroId] = count + 1;
            }

            foreach (var aggregate in staged)
            {
                var execution = aggregate.execution;
                if ((CombatRuntimeSaveDataUtility.IsUnfinished(execution) && unfinishedHeroCounts[execution.heroId] > 1) ||
                    !CanApplyCombatOwnership(aggregate))
                {
                    Debug.LogError($"[PlayerState] Combat aggregate '{execution.executionId}' has conflicting hero ownership and was rejected.");
                    WasNormalized = true;
                    continue;
                }
                _combatAggregates.Add(execution.executionId, aggregate);
                ApplyCombatOwnership(aggregate);
            }
        }

        private bool TryNormalizeCombatAggregate(CombatRuntimeAggregate source, out CombatRuntimeAggregate normalized)
        {
            normalized = null;
            if (!CombatRuntimeSaveDataUtility.TryNormalize(source?.execution, source?.session, out normalized, out _, out var error) ||
                !_acquiredHeroes.Contains(normalized?.execution?.heroId))
            {
                Debug.LogError($"[PlayerState] Invalid combat aggregate. {error}");
                normalized = null;
                return false;
            }
            return true;
        }

        private bool CanApplyCombatOwnership(CombatRuntimeAggregate aggregate)
        {
            var execution = aggregate?.execution;
            if (execution == null || !_acquiredHeroes.Contains(execution.heroId) || !_heroes.TryGetValue(execution.heroId, out var hero))
                return false;
            if (!CombatRuntimeSaveDataUtility.IsUnfinished(execution))
                return true;
            if (string.IsNullOrWhiteSpace(hero.CurrentActivityExecutionId))
                return string.Equals(execution.occupationOwnerId, execution.executionId, StringComparison.Ordinal);
            return string.Equals(hero.CurrentActivityExecutionId, execution.occupationOwnerId, StringComparison.Ordinal);
        }

        private void ApplyCombatOwnership(CombatRuntimeAggregate aggregate)
        {
            var execution = aggregate?.execution;
            if (execution == null || !CombatRuntimeSaveDataUtility.IsUnfinished(execution) ||
                !string.Equals(execution.occupationOwnerId, execution.executionId, StringComparison.Ordinal) ||
                !_heroes.TryGetValue(execution.heroId, out var hero) || !string.IsNullOrWhiteSpace(hero.CurrentActivityExecutionId))
                return;
            hero.CurrentActivityExecutionId = execution.executionId;
        }

        private void ReleaseDirectCombatOwnership(CombatRuntimeAggregate aggregate)
        {
            var execution = aggregate?.execution;
            if (execution == null || !string.Equals(execution.occupationOwnerId, execution.executionId, StringComparison.Ordinal) ||
                !_heroes.TryGetValue(execution.heroId, out var hero) ||
                !string.Equals(hero.CurrentActivityExecutionId, execution.executionId, StringComparison.Ordinal))
                return;
            hero.CurrentActivityExecutionId = null;
        }

        private bool HasOtherUnfinishedCombat(string heroId, string exceptExecutionId)
        {
            foreach (var pair in _combatAggregates)
            {
                if (!string.Equals(pair.Key, exceptExecutionId, StringComparison.Ordinal) &&
                    string.Equals(pair.Value.execution.heroId, heroId, StringComparison.Ordinal) &&
                    CombatRuntimeSaveDataUtility.IsUnfinished(pair.Value.execution))
                    return true;
            }
            return false;
        }

        private static bool IsValidCombatLifecycleTransition(CombatExecutionSaveData previous, CombatExecutionSaveData next)
        {
            if (previous == null || next == null || next.status < previous.status ||
                (previous.outcomeFinalized && !next.outcomeFinalized) ||
                (previous.resultCreated && !next.resultCreated) ||
                (previous.pendingResultResolved && !next.pendingResultResolved) ||
                (previous.completionPublished && !next.completionPublished) ||
                (previous.failurePublished && !next.failurePublished))
                return false;
            return !previous.outcomeFinalized || string.Equals(previous.outcome, next.outcome, StringComparison.Ordinal);
        }

        private static HashSet<string> FindDuplicateExecutionIds(CombatExecutionSaveData[] executions)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var execution in executions)
                if (execution != null && !string.IsNullOrWhiteSpace(execution.executionId) && !seen.Add(execution.executionId))
                    duplicates.Add(execution.executionId);
            return duplicates;
        }

        private static HashSet<string> FindDuplicateSessionIds(CombatSessionSaveData[] sessions)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var session in sessions)
                if (session != null && !string.IsNullOrWhiteSpace(session.sessionId) && !seen.Add(session.sessionId))
                    duplicates.Add(session.sessionId);
            return duplicates;
        }

        private static HashSet<string> FindDuplicateSessionExecutionIds(CombatSessionSaveData[] sessions)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var session in sessions)
                if (session != null && !string.IsNullOrWhiteSpace(session.executionId) && !seen.Add(session.executionId))
                    duplicates.Add(session.executionId);
            return duplicates;
        }

        private void LoadOperationReceipts(OperationReceiptSaveData[] receipts)
        {
            _operationReceipts.Clear();
            if (receipts == null)
                return;
            if (receipts.Length > OperationReceiptRetentionLimit)
                WasNormalized = true;
            var start = Math.Max(0, receipts.Length - OperationReceiptRetentionLimit);
            for (var index = start; index < receipts.Length; index++)
            {
                var receipt = receipts[index];
                if (receipt == null || string.IsNullOrWhiteSpace(receipt.aggregateId) || string.IsNullOrWhiteSpace(receipt.operationId) || string.IsNullOrWhiteSpace(receipt.fingerprint))
                {
                    WasNormalized = true;
                    continue;
                }
                _operationReceipts.Add(CloneOperationReceipt(receipt));
            }
        }

        private void LoadResultSourceReferences(PendingResultSourceReferenceSaveData[] sources)
        {
            _resultSources.Clear();
            _lastResultSourceResolutionSequence = 0;
            var resolutionSequences = new HashSet<long>();
            var requiresSequenceNormalization = false;
            foreach (var source in sources ?? Array.Empty<PendingResultSourceReferenceSaveData>())
            {
                if (source == null || string.IsNullOrWhiteSpace(source.sourceType) || string.IsNullOrWhiteSpace(source.sourceExecutionId) ||
                    string.IsNullOrWhiteSpace(source.resultId) ||
                    (source.state != PendingResultSourceState.Pending && source.state != PendingResultSourceState.Resolved && source.state != PendingResultSourceState.Blocked))
                {
                    WasNormalized = true;
                    continue;
                }
                var key = ResultSourceKey(source.sourceType, source.sourceExecutionId);
                if (_resultSources.ContainsKey(key))
                {
                    WasNormalized = true;
                    continue;
                }
                var stored = CloneResultSourceReference(source);
                if (string.Equals(stored.state, PendingResultSourceState.Resolved, StringComparison.Ordinal))
                {
                    if (stored.resolutionSequence <= 0 || !resolutionSequences.Add(stored.resolutionSequence))
                        requiresSequenceNormalization = true;
                    else
                        _lastResultSourceResolutionSequence = Math.Max(_lastResultSourceResolutionSequence, stored.resolutionSequence);
                }
                else if (stored.resolutionSequence != 0)
                {
                    stored.resolutionSequence = 0;
                    WasNormalized = true;
                }
                _resultSources.Add(key, stored);
            }
            if (requiresSequenceNormalization)
                NormalizeResultSourceResolutionSequences();
        }

        internal bool TryGetOperationReceipt(string aggregateId, string operationId, out OperationReceiptSaveData receipt)
        {
            for (var index = _operationReceipts.Count - 1; index >= 0; index--)
            {
                var candidate = _operationReceipts[index];
                if (string.Equals(candidate.aggregateId, aggregateId, StringComparison.Ordinal) && string.Equals(candidate.operationId, operationId, StringComparison.Ordinal))
                {
                    receipt = CloneOperationReceipt(candidate);
                    return true;
                }
            }
            receipt = null;
            return false;
        }

        internal void RecordOperationReceipt(OperationReceiptSaveData receipt)
        {
            if (receipt == null)
                return;
            _operationReceipts.Add(CloneOperationReceipt(receipt));
            while (_operationReceipts.Count > OperationReceiptRetentionLimit)
                _operationReceipts.RemoveAt(0);
        }

        internal void RestoreTransactional(SaveData saveData) => Restore(saveData, WasNormalized);
        internal void MarkNormalized() => WasNormalized = true;
        internal bool IsTimeBaselineInitialized => _timeBaselineInitialized;
        internal long LastProcessedUtcSeconds => _lastProcessedUtcSeconds;

        internal string[] GetOrderedHeroIds()
        {
            var keys = SortedKeys(_heroes);
            return keys.ToArray();
        }

        internal int GetHeroFatigueRemainderSeconds(string heroId)
        {
            return _fatigueRemainderSeconds.TryGetValue(heroId, out var remainder) ? remainder : 0;
        }

        internal void SetHeroFatigueRemainderSeconds(string heroId, int remainderSeconds)
        {
            if (!_heroes.ContainsKey(heroId))
                return;

            _fatigueRemainderSeconds[heroId] = Math.Max(
                0,
                Math.Min(TimeProgressService.FatigueRecoveryIntervalSeconds - 1, remainderSeconds));
        }

        internal void InitializeTimeBaseline(long utcSeconds)
        {
            _timeBaselineInitialized = true;
            _lastProcessedUtcSeconds = Math.Max(0L, utcSeconds);
            foreach (var heroId in GetOrderedHeroIds())
                _fatigueRemainderSeconds[heroId] = 0;
        }

        internal void SetLastProcessedUtcSeconds(long utcSeconds)
        {
            _lastProcessedUtcSeconds = Math.Max(_lastProcessedUtcSeconds, utcSeconds);
        }

        internal void QuarantinePendingResultSource(PendingResultSaveData result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.sourceType) || string.IsNullOrWhiteSpace(result.sourceExecutionId))
                return;
            _resultSources[ResultSourceKey(result.sourceType, result.sourceExecutionId)] = new PendingResultSourceReferenceSaveData
            {
                sourceType = result.sourceType,
                sourceId = result.sourceId,
                sourceExecutionId = result.sourceExecutionId,
                resultId = string.IsNullOrWhiteSpace(result.resultId) ? $"result:{result.sourceType}:{result.sourceExecutionId}" : result.resultId,
                state = PendingResultSourceState.Blocked
            };
        }

        internal bool IsPendingResultSourceQuarantined(string sourceType, string sourceExecutionId) =>
            _resultSources.TryGetValue(ResultSourceKey(sourceType, sourceExecutionId), out var source) &&
            string.Equals(source.state, PendingResultSourceState.Blocked, StringComparison.Ordinal);

        internal bool TryBindPersistentResultSource(PendingResultSaveData result, bool allowExisting)
        {
            if (result == null)
                return false;
            var sourceKey = ResultSourceKey(result.sourceType, result.sourceExecutionId);
            if (_resultSources.TryGetValue(sourceKey, out var source))
            {
                return allowExisting && string.Equals(source.state, PendingResultSourceState.Pending, StringComparison.Ordinal) &&
                       string.Equals(source.resultId, result.resultId, StringComparison.Ordinal) &&
                       string.Equals(source.sourceId, result.sourceId, StringComparison.Ordinal);
            }
            _resultSources[sourceKey] = new PendingResultSourceReferenceSaveData
            {
                sourceType = result.sourceType,
                sourceId = result.sourceId,
                sourceExecutionId = result.sourceExecutionId,
                resultId = result.resultId,
                state = PendingResultSourceState.Pending
            };
            return true;
        }

        internal bool CanClaimPersistentResultSource(PendingResultSaveData result)
        {
            if (result == null)
                return false;
            return _resultSources.TryGetValue(ResultSourceKey(result.sourceType, result.sourceExecutionId), out var source) &&
                   string.Equals(source.state, PendingResultSourceState.Pending, StringComparison.Ordinal) &&
                   string.Equals(source.resultId, result.resultId, StringComparison.Ordinal);
        }

        internal bool ResolvePersistentResultSource(PendingResultSaveData result)
        {
            if (!CanClaimPersistentResultSource(result))
                return false;
            if (!_resultSources.TryGetValue(ResultSourceKey(result.sourceType, result.sourceExecutionId), out var source))
                return false;
            source.state = PendingResultSourceState.Resolved;
            source.resultId = result.resultId;
            source.resolutionSequence = NextResultSourceResolutionSequence();
            TrimResolvedResultSources(ResultSourceKey(result.sourceType, result.sourceExecutionId));
            return true;
        }

        internal bool IsPendingResultSourceResolved(string resultId)
        {
            if (string.IsNullOrWhiteSpace(resultId))
                return false;
            foreach (var source in _resultSources.Values)
            {
                if (source != null && string.Equals(source.resultId, resultId, StringComparison.Ordinal) &&
                    string.Equals(source.state, PendingResultSourceState.Resolved, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        internal long LastCombatResultSequence => _lastCombatResultSequence;

        internal bool TryAcceptCombatResultSequence(long sourceSequence)
        {
            if (sourceSequence <= 0 || sourceSequence != _lastCombatResultSequence + 1)
                return false;
            _lastCombatResultSequence = sourceSequence;
            return true;
        }

        private bool TrimResolvedResultSources(string protectedSourceKey = null)
        {
            var resolvedKeys = new List<KeyValuePair<string, PendingResultSourceReferenceSaveData>>();
            foreach (var pair in _resultSources)
            {
                if (pair.Value != null && string.Equals(pair.Value.state, PendingResultSourceState.Resolved, StringComparison.Ordinal))
                    resolvedKeys.Add(pair);
            }
            if (resolvedKeys.Count <= ResolvedResultSourceRetentionLimit)
                return false;

            resolvedKeys.Sort((left, right) =>
            {
                var sequenceOrder = left.Value.resolutionSequence.CompareTo(right.Value.resolutionSequence);
                return sequenceOrder != 0 ? sequenceOrder : StringComparer.Ordinal.Compare(left.Key, right.Key);
            });
            var removeCount = resolvedKeys.Count - ResolvedResultSourceRetentionLimit;
            var removed = false;
            foreach (var pair in resolvedKeys)
            {
                if (removeCount == 0)
                    break;
                if (string.Equals(pair.Key, protectedSourceKey, StringComparison.Ordinal))
                    continue;
                if (_resultSources.Remove(pair.Key))
                {
                    removeCount--;
                    removed = true;
                }
            }
            return removed;
        }

        private long NextResultSourceResolutionSequence()
        {
            if (_lastResultSourceResolutionSequence == long.MaxValue)
                NormalizeResultSourceResolutionSequences();
            return ++_lastResultSourceResolutionSequence;
        }

        private void NormalizeResultSourceResolutionSequences()
        {
            var resolved = new List<KeyValuePair<string, PendingResultSourceReferenceSaveData>>();
            foreach (var pair in _resultSources)
                if (pair.Value != null && string.Equals(pair.Value.state, PendingResultSourceState.Resolved, StringComparison.Ordinal))
                    resolved.Add(pair);
            resolved.Sort((left, right) =>
            {
                var leftSequence = left.Value.resolutionSequence > 0 ? left.Value.resolutionSequence : long.MaxValue;
                var rightSequence = right.Value.resolutionSequence > 0 ? right.Value.resolutionSequence : long.MaxValue;
                var sequenceOrder = leftSequence.CompareTo(rightSequence);
                return sequenceOrder != 0 ? sequenceOrder : StringComparer.Ordinal.Compare(left.Key, right.Key);
            });
            _lastResultSourceResolutionSequence = 0;
            foreach (var pair in resolved)
                pair.Value.resolutionSequence = ++_lastResultSourceResolutionSequence;
            WasNormalized = true;
        }

        internal void ReconcileCraftExecutions()
        {
            foreach (var executionId in new List<string>(_craftExecutions.Keys))
            {
                var execution = _craftExecutions[executionId];
                if (execution.status != CraftExecutionStatus.ResultPending || !execution.completionRecorded ||
                    string.IsNullOrWhiteSpace(execution.pendingResultId) || PendingResults.Get(execution.pendingResultId) != null ||
                    !_resultSources.TryGetValue(ResultSourceKey(PendingResultSourceType.Craft, execution.executionId), out var source) ||
                    !string.Equals(source.sourceId, execution.craftId, StringComparison.Ordinal) ||
                    !string.Equals(source.resultId, execution.pendingResultId, StringComparison.Ordinal))
                    continue;

                if (string.Equals(source.state, PendingResultSourceState.Pending, StringComparison.Ordinal))
                {
                    source.state = PendingResultSourceState.Blocked;
                    WasNormalized = true;
                    Debug.LogError($"[PlayerState] Craft execution '{execution.executionId}' has a Pending source but no linked PendingResult and remains blocked for manual recovery.");
                    continue;
                }
                if (!string.Equals(source.state, PendingResultSourceState.Resolved, StringComparison.Ordinal))
                    continue;

                if (!RemoveCraftExecution(execution.executionId))
                    continue;
                if (string.Equals(GetHeroCurrentActivityExecutionId(execution.heroId), execution.executionId, StringComparison.Ordinal))
                    ClearHeroBusy(execution.heroId, execution.executionId);
                WasNormalized = true;
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

            if (RuntimeConfigs.Activities.TryGet(activityId, out _) || RuntimeConfigs.Buildings.TryGetBuildAction(activityId, out _))
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

        private void EnsureFatigueRemaindersForHeroes()
        {
            foreach (var heroId in GetOrderedHeroIds())
            {
                if (_fatigueRemainderSeconds.ContainsKey(heroId))
                    continue;

                _fatigueRemainderSeconds[heroId] = 0;
                WasNormalized = true;
            }
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

        private HeroSaveData[] BuildHeroEntries()
        {
            var keys = SortedKeys(_heroes);
            var entries = new HeroSaveData[keys.Count];
            for (var i = 0; i < keys.Count; i++)
                entries[i] = _heroes[keys[i]].ToSaveData();

            return entries;
        }

        private TimeProgressSaveData BuildTimeProgressSaveData()
        {
            var heroIds = GetOrderedHeroIds();
            var remainders = new HeroFatigueRemainderSaveData[heroIds.Length];
            for (var index = 0; index < heroIds.Length; index++)
            {
                var heroId = heroIds[index];
                remainders[index] = new HeroFatigueRemainderSaveData
                {
                    heroId = heroId,
                    fatigueRemainderSeconds = GetHeroFatigueRemainderSeconds(heroId)
                };
            }

            return new TimeProgressSaveData
            {
                baselineInitialized = _timeBaselineInitialized,
                lastProcessedUtcSeconds = _lastProcessedUtcSeconds,
                fatigueRemainders = remainders
            };
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

        private CraftRuntimeSaveData BuildCraftRuntimeSaveData()
        {
            return new CraftRuntimeSaveData
            {
                executions = GetCraftExecutions()
            };
        }

        private CombatRuntimeSaveData BuildCombatRuntimeSaveData()
        {
            var aggregates = GetCombatAggregates();
            var executions = new CombatExecutionSaveData[aggregates.Length];
            var sessions = new CombatSessionSaveData[aggregates.Length];
            for (var index = 0; index < aggregates.Length; index++)
            {
                executions[index] = aggregates[index].execution;
                sessions[index] = aggregates[index].session;
            }
            return new CombatRuntimeSaveData { executions = executions, sessions = sessions };
        }

        private string GetAvailableStateId()
        {
            return _configs.TryGetItemStateByAvailabilityMode(ItemAvailabilityMode.Available, out var state) && state != null
                ? state.stateId
                : null;
        }

        private OperationReceiptSaveData[] BuildOperationReceipts()
        {
            var result = new OperationReceiptSaveData[_operationReceipts.Count];
            for (var index = 0; index < result.Length; index++)
                result[index] = CloneOperationReceipt(_operationReceipts[index]);
            return result;
        }

        private PendingResultSourceReferenceSaveData[] BuildResultSourceReferences()
        {
            var keys = SortedKeys(_resultSources);
            var result = new PendingResultSourceReferenceSaveData[keys.Count];
            for (var index = 0; index < keys.Count; index++)
                result[index] = CloneResultSourceReference(_resultSources[keys[index]]);
            return result;
        }

        public bool Save()
        {
            return _saveHandler != null ? _saveHandler() : SaveService.Save(this);
        }

        internal void BindSaveStorage(ISaveStorage storage)
        {
            _saveHandler = () => SaveService.Save(this, storage);
        }

        private bool TryApplyRewardMutation(RewardMutation mutation, out bool changed, out string error)
        {
            changed = false;
            error = null;
            if (mutation == null || string.IsNullOrWhiteSpace(mutation.TargetId) || mutation.Amount <= 0)
            {
                error = "Reward mutation has an invalid target or amount.";
                return false;
            }

            switch (mutation.Kind)
            {
                case RewardMutationKind.Item:
                    if (mutation.Amount > int.MaxValue || !AddItem(mutation.TargetId, (int)mutation.Amount))
                    {
                        error = $"Failed to add item reward '{mutation.TargetId}'.";
                        return false;
                    }

                    changed = true;
                    return true;

                case RewardMutationKind.Currency:
                    if (!AddCurrency(mutation.TargetId, mutation.Amount))
                    {
                        error = $"Failed to add currency reward '{mutation.TargetId}'.";
                        return false;
                    }

                    changed = true;
                    return true;

                case RewardMutationKind.Hero:
                    if (HasHero(mutation.TargetId))
                        return true;
                    if (!AddHero(mutation.TargetId))
                    {
                        error = $"Failed to add hero reward '{mutation.TargetId}'.";
                        return false;
                    }

                    changed = true;
                    return true;

                case RewardMutationKind.HeroSkillExp:
                    if (mutation.Amount > int.MaxValue ||
                        string.IsNullOrWhiteSpace(mutation.OwnerId) ||
                        !AddHeroSkillExp(mutation.OwnerId, mutation.TargetId, (int)mutation.Amount))
                    {
                        error = $"Failed to add skill EXP reward '{mutation.TargetId}' to hero '{mutation.OwnerId}'.";
                        return false;
                    }

                    changed = true;
                    return true;

                case RewardMutationKind.UnlockBuilding:
                    if (IsBuildingUnlocked(mutation.TargetId))
                        return true;
                    if (!UnlockBuilding(mutation.TargetId))
                    {
                        error = $"Failed to unlock building reward '{mutation.TargetId}'.";
                        return false;
                    }

                    changed = true;
                    return true;

                case RewardMutationKind.UnlockLocation:
                    if (IsLocationUnlocked(mutation.TargetId))
                        return true;
                    if (!UnlockLocation(mutation.TargetId))
                    {
                        error = $"Failed to unlock location reward '{mutation.TargetId}'.";
                        return false;
                    }

                    changed = true;
                    return true;

                default:
                    error = $"Unsupported reward mutation kind '{mutation.Kind}'.";
                    return false;
            }
        }

        private static string[] BuildSortedArray(HashSet<string> values)
        {
            var list = new List<string>(values);
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        private static bool NormalizeLegacyCraftAdvanceSequences(CraftExecutionSaveData execution)
        {
            if (execution == null || execution.lastAdvanceSequence != 0 || execution.completionAdvanceSequence != 0)
                return false;

            var receipts = execution.advanceReceipts ?? Array.Empty<CraftAdvanceReceiptSaveData>();
            if (receipts.Length == 0)
                return false;
            foreach (var receipt in receipts)
                if (receipt == null || receipt.operationSequence != 0) return false;

            for (var index = 0; index < receipts.Length; index++)
                receipts[index].operationSequence = index + 1L;
            execution.lastAdvanceSequence = receipts.Length;
            if (execution.status == CraftExecutionStatus.ResultPending && execution.completionRecorded)
                execution.completionAdvanceSequence = execution.lastAdvanceSequence;
            return true;
        }

        private bool ValidateCraftExecution(CraftExecutionSaveData execution)
        {
            if (execution == null || string.IsNullOrWhiteSpace(execution.executionId) ||
                string.IsNullOrWhiteSpace(execution.craftId) || string.IsNullOrWhiteSpace(execution.heroId) ||
                !_acquiredHeroes.Contains(execution.heroId) || string.IsNullOrWhiteSpace(execution.stationBuildingId) ||
                execution.stationBuildingLevel < 0 || execution.durationSeconds <= 0 ||
                float.IsNaN(execution.progressSeconds) || float.IsInfinity(execution.progressSeconds) || execution.progressSeconds < 0f ||
                string.IsNullOrWhiteSpace(execution.outputItemId) || execution.outputCount <= 0 ||
                string.IsNullOrWhiteSpace(execution.skillId) || execution.skillExp < 0 || execution.fatigueCostPaid < 0 ||
                !execution.costsPaid || string.IsNullOrWhiteSpace(execution.startOperationKey) ||
                string.IsNullOrWhiteSpace(execution.startFingerprint))
            {
                Debug.LogError($"[PlayerState] Craft execution '{execution?.executionId}' has an invalid immutable snapshot.");
                return false;
            }
            if (execution.status != CraftExecutionStatus.Running && execution.status != CraftExecutionStatus.ResultPending)
            {
                Debug.LogError($"[PlayerState] Craft execution '{execution.executionId}' has unsupported status '{execution.status}'.");
                return false;
            }
            if (execution.status == CraftExecutionStatus.Running &&
                (!string.IsNullOrWhiteSpace(execution.pendingResultId) || execution.completionRecorded))
            {
                Debug.LogError($"[PlayerState] Running craft execution '{execution.executionId}' has completion state.");
                return false;
            }
            if (execution.status == CraftExecutionStatus.ResultPending &&
                (string.IsNullOrWhiteSpace(execution.pendingResultId) || !execution.completionRecorded ||
                 execution.progressSeconds < execution.durationSeconds))
            {
                Debug.LogError($"[PlayerState] ResultPending craft execution '{execution.executionId}' has invalid completion state.");
                return false;
            }
            var completionPrepared = execution.status == CraftExecutionStatus.Running && execution.completionAdvanceSequence > 0;
            if (execution.lastAdvanceSequence < 0 || execution.completionAdvanceSequence < 0 ||
                (completionPrepared &&
                  (execution.completionAdvanceSequence != execution.lastAdvanceSequence || execution.progressSeconds < execution.durationSeconds)) ||
                (execution.status == CraftExecutionStatus.ResultPending &&
                  execution.completionAdvanceSequence > 0 &&
                  execution.completionAdvanceSequence != execution.lastAdvanceSequence))
            {
                Debug.LogError($"[PlayerState] Craft execution '{execution.executionId}' has invalid advance sequence state.");
                return false;
            }
            var advanceOperationKeys = new HashSet<string>(StringComparer.Ordinal);
            var advanceOperationSequences = new HashSet<long>();
            var previousAdvanceSequence = 0L;
            foreach (var receipt in execution.advanceReceipts ?? Array.Empty<CraftAdvanceReceiptSaveData>())
            {
                var completed = string.Equals(receipt?.code, CraftAdvanceCode.ResultPending, StringComparison.Ordinal);
                if (receipt == null || receipt.operationSequence <= 0 || receipt.operationSequence > execution.lastAdvanceSequence ||
                    receipt.operationSequence <= previousAdvanceSequence || !advanceOperationSequences.Add(receipt.operationSequence) ||
                    string.IsNullOrWhiteSpace(receipt.operationKey) ||
                    string.IsNullOrWhiteSpace(receipt.fingerprint) || !advanceOperationKeys.Add(receipt.operationKey) ||
                    double.IsNaN(receipt.deltaSeconds) || double.IsInfinity(receipt.deltaSeconds) || receipt.deltaSeconds < 0d ||
                    float.IsNaN(receipt.progressSeconds) || float.IsInfinity(receipt.progressSeconds) || receipt.progressSeconds < 0f ||
                    receipt.progressSeconds > execution.durationSeconds ||
                    (!completed && !string.Equals(receipt.code, CraftAdvanceCode.Applied, StringComparison.Ordinal)) ||
                    (completed != !string.IsNullOrWhiteSpace(receipt.pendingResultId)) ||
                    (completed && (receipt.progressSeconds < execution.durationSeconds ||
                                   !string.Equals(receipt.pendingResultId, $"result:{PendingResultSourceType.Craft}:{execution.executionId}", StringComparison.Ordinal))) ||
                    (!completed && receipt.progressSeconds >= execution.durationSeconds) ||
                    (completed != (receipt.operationSequence == execution.completionAdvanceSequence)))
                    return false;
                previousAdvanceSequence = receipt.operationSequence;
            }
            var costIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cost in execution.paidCosts ?? Array.Empty<CraftPaidCostSaveData>())
            {
                if (cost == null || string.IsNullOrWhiteSpace(cost.itemId) || cost.quantity <= 0 || !costIds.Add(cost.itemId) ||
                    (!string.Equals(cost.kind, CraftPaidCostKind.Material, StringComparison.Ordinal) &&
                     !string.Equals(cost.kind, CraftPaidCostKind.Recipe, StringComparison.Ordinal) &&
                     !string.Equals(cost.kind, CraftPaidCostKind.MaterialAndRecipe, StringComparison.Ordinal)))
                    return false;
            }
            foreach (var requirement in execution.requiredBuildings ?? Array.Empty<CraftRequiredBuildingSnapshotSaveData>())
                if (requirement == null || string.IsNullOrWhiteSpace(requirement.buildingId) || requirement.level < 0) return false;
            var recipe = execution.recipe ?? new CraftRecipeAuditSaveData();
            var hasRecipe = !string.IsNullOrWhiteSpace(recipe.requiredItemId);
            if (hasRecipe != (recipe.requiredCount > 0) || (!hasRecipe && recipe.consume) ||
                recipe.consumedCount != (recipe.consume ? recipe.requiredCount : 0))
                return false;
            if (recipe.consume)
            {
                var recipeCostFound = false;
                foreach (var cost in execution.paidCosts ?? Array.Empty<CraftPaidCostSaveData>())
                {
                    if (cost != null && string.Equals(cost.itemId, recipe.requiredItemId, StringComparison.Ordinal) &&
                        cost.quantity >= recipe.requiredCount &&
                        (string.Equals(cost.kind, CraftPaidCostKind.Recipe, StringComparison.Ordinal) ||
                         string.Equals(cost.kind, CraftPaidCostKind.MaterialAndRecipe, StringComparison.Ordinal)))
                    {
                        recipeCostFound = true;
                        break;
                    }
                }
                if (!recipeCostFound)
                    return false;
            }
            return true;
        }

        private static CraftExecutionSaveData CloneCraftExecution(CraftExecutionSaveData source)
        {
            if (source == null)
                return null;
            var requirements = new CraftRequiredBuildingSnapshotSaveData[source.requiredBuildings?.Length ?? 0];
            for (var index = 0; index < requirements.Length; index++)
            {
                var value = source.requiredBuildings[index];
                requirements[index] = value == null ? null : new CraftRequiredBuildingSnapshotSaveData { buildingId = value.buildingId, level = value.level };
            }
            var costs = new CraftPaidCostSaveData[source.paidCosts?.Length ?? 0];
            for (var index = 0; index < costs.Length; index++)
            {
                var value = source.paidCosts[index];
                costs[index] = value == null ? null : new CraftPaidCostSaveData { itemId = value.itemId, quantity = value.quantity, kind = value.kind };
            }
            var recipe = source.recipe ?? new CraftRecipeAuditSaveData();
            var advanceReceipts = new CraftAdvanceReceiptSaveData[source.advanceReceipts?.Length ?? 0];
            for (var index = 0; index < advanceReceipts.Length; index++)
            {
                var value = source.advanceReceipts[index];
                advanceReceipts[index] = value == null ? null : new CraftAdvanceReceiptSaveData
                {
                    operationSequence = value.operationSequence,
                    operationKey = value.operationKey,
                    fingerprint = value.fingerprint,
                    deltaSeconds = value.deltaSeconds,
                    progressSeconds = value.progressSeconds,
                    code = value.code,
                    pendingResultId = value.pendingResultId
                };
            }
            return new CraftExecutionSaveData
            {
                executionId = source.executionId,
                craftId = source.craftId,
                heroId = source.heroId,
                stationBuildingId = source.stationBuildingId,
                stationBuildingLevel = source.stationBuildingLevel,
                status = source.status,
                progressSeconds = Math.Max(0f, source.progressSeconds),
                durationSeconds = source.durationSeconds,
                outputItemId = source.outputItemId,
                outputCount = source.outputCount,
                skillId = source.skillId,
                skillExp = source.skillExp,
                fatigueCostPaid = source.fatigueCostPaid,
                requiredBuildings = requirements,
                paidCosts = costs,
                recipe = new CraftRecipeAuditSaveData
                {
                    requiredItemId = recipe.requiredItemId,
                    requiredCount = recipe.requiredCount,
                    consume = recipe.consume,
                    consumedCount = recipe.consumedCount
                },
                costsPaid = source.costsPaid,
                startOperationKey = source.startOperationKey,
                startFingerprint = source.startFingerprint,
                pendingResultId = source.pendingResultId,
                completionRecorded = source.completionRecorded,
                lastAdvanceSequence = source.lastAdvanceSequence,
                completionAdvanceSequence = source.completionAdvanceSequence,
                advanceReceipts = advanceReceipts,
                startedAtUnixSeconds = source.startedAtUnixSeconds
            };
        }

        private static bool HasSameCraftSnapshot(CraftExecutionSaveData left, CraftExecutionSaveData right)
        {
            if (left == null || right == null ||
                !string.Equals(left.craftId, right.craftId, StringComparison.Ordinal) ||
                !string.Equals(left.heroId, right.heroId, StringComparison.Ordinal) ||
                !string.Equals(left.stationBuildingId, right.stationBuildingId, StringComparison.Ordinal) ||
                left.stationBuildingLevel != right.stationBuildingLevel || left.durationSeconds != right.durationSeconds ||
                !string.Equals(left.outputItemId, right.outputItemId, StringComparison.Ordinal) || left.outputCount != right.outputCount ||
                !string.Equals(left.skillId, right.skillId, StringComparison.Ordinal) || left.skillExp != right.skillExp ||
                left.fatigueCostPaid != right.fatigueCostPaid || left.costsPaid != right.costsPaid ||
                !string.Equals(left.startOperationKey, right.startOperationKey, StringComparison.Ordinal) ||
                !string.Equals(left.startFingerprint, right.startFingerprint, StringComparison.Ordinal) ||
                left.startedAtUnixSeconds != right.startedAtUnixSeconds ||
                !SameCraftRequirements(left.requiredBuildings, right.requiredBuildings) ||
                !SameCraftCosts(left.paidCosts, right.paidCosts))
                return false;

            var leftRecipe = left.recipe ?? new CraftRecipeAuditSaveData();
            var rightRecipe = right.recipe ?? new CraftRecipeAuditSaveData();
            return string.Equals(leftRecipe.requiredItemId, rightRecipe.requiredItemId, StringComparison.Ordinal) &&
                   leftRecipe.requiredCount == rightRecipe.requiredCount && leftRecipe.consume == rightRecipe.consume &&
                   leftRecipe.consumedCount == rightRecipe.consumedCount;
        }

        private static bool SameCraftRequirements(
            CraftRequiredBuildingSnapshotSaveData[] left,
            CraftRequiredBuildingSnapshotSaveData[] right)
        {
            left ??= Array.Empty<CraftRequiredBuildingSnapshotSaveData>();
            right ??= Array.Empty<CraftRequiredBuildingSnapshotSaveData>();
            if (left.Length != right.Length)
                return false;
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] == null || right[index] == null ||
                    !string.Equals(left[index].buildingId, right[index].buildingId, StringComparison.Ordinal) ||
                    left[index].level != right[index].level)
                    return false;
            }
            return true;
        }

        private static bool SameCraftCosts(CraftPaidCostSaveData[] left, CraftPaidCostSaveData[] right)
        {
            left ??= Array.Empty<CraftPaidCostSaveData>();
            right ??= Array.Empty<CraftPaidCostSaveData>();
            if (left.Length != right.Length)
                return false;
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] == null || right[index] == null ||
                    !string.Equals(left[index].itemId, right[index].itemId, StringComparison.Ordinal) ||
                    left[index].quantity != right[index].quantity ||
                    !string.Equals(left[index].kind, right[index].kind, StringComparison.Ordinal))
                    return false;
            }
            return true;
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

            if (!ValidateActivityId(execution.activityId))
                return false;

            var requiresHero = execution.status == ActivityRuntimeStatus.Running || execution.status == ActivityRuntimeStatus.ResultPending;
            if (requiresHero && (!ValidateHeroId(execution.heroId) || !_acquiredHeroes.Contains(execution.heroId)))
            {
                Debug.LogError($"[PlayerState] Activity execution '{execution.executionId}' references non-acquired hero '{execution.heroId}'.");
                return false;
            }
            if (execution.status == ActivityRuntimeStatus.Paused && !string.IsNullOrWhiteSpace(execution.heroId))
            {
                Debug.LogError($"[PlayerState] Paused execution '{execution.executionId}' must not retain an assigned hero.");
                return false;
            }

            if (requireRunning && execution.status != ActivityRuntimeStatus.Running)
            {
                Debug.LogError($"[PlayerState] Activity execution '{execution.executionId}' has unsupported status '{execution.status}'.");
                return false;
            }

            if (execution.status != ActivityRuntimeStatus.Running && execution.status != ActivityRuntimeStatus.Paused &&
                execution.status != ActivityRuntimeStatus.ResultPending && execution.status != ActivityRuntimeStatus.Completed &&
                execution.status != ActivityRuntimeStatus.Cancelled)
            {
                Debug.LogError($"[PlayerState] Activity execution '{execution.executionId}' has unknown status '{execution.status}'.");
                return false;
            }
            if (execution.status == ActivityRuntimeStatus.ResultPending &&
                string.IsNullOrWhiteSpace(execution.pendingResultId) &&
                !(execution.activityBagResolved && execution.linkedCombat != null &&
                  !string.IsNullOrWhiteSpace(execution.linkedCombat.requestId) &&
                  string.Equals(execution.linkedCombat.rootExecutionId, execution.executionId, StringComparison.Ordinal) &&
                  string.Equals(execution.linkedCombat.occupationOwnerId, execution.executionId, StringComparison.Ordinal)))
            {
                Debug.LogError($"[PlayerState] ResultPending execution '{execution.executionId}' has no pending result id.");
                return false;
            }
            if (execution.linkedCombat != null)
            {
                if (string.IsNullOrWhiteSpace(
                        execution.linkedCombat.enemyExpTargetId) &&
                    _configs.TryGetActivity(
                        execution.activityId,
                        out var activity) &&
                    activity != null &&
                    !string.IsNullOrWhiteSpace(activity.mainSkillId))
                {
                    execution.linkedCombat.enemyExpTargetId =
                        activity.mainSkillId;
                    WasNormalized = true;
                }
                if (!string.IsNullOrWhiteSpace(
                        execution.linkedCombat.enemyExpTargetId) &&
                    !ValidateSkillId(
                        execution.linkedCombat.enemyExpTargetId))
                {
                    Debug.LogError(
                        $"[PlayerState] Linked combat handoff on '{execution.executionId}' has an invalid Enemy EXP target.");
                    return false;
                }
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
                runtimeKind = execution.runtimeKind,
                heroId = execution.heroId,
                status = execution.status,
                elapsedSeconds = Math.Max(0f, execution.elapsedSeconds),
                completedCycles = Math.Max(0, execution.completedCycles),
                plannedCycles = Math.Max(0, execution.plannedCycles),
                currentCycleFatiguePaid = execution.currentCycleFatiguePaid,
                cyclePhase = execution.cyclePhase,
                stagedRewards = CloneStagedRewards(execution.stagedRewards),
                endReason = execution.endReason,
                dangerRollCompleted = execution.dangerRollCompleted,
                dangerRiskPercent = execution.dangerRiskPercent,
                dangerRoll = execution.dangerRoll,
                dangerHandoffFingerprint = execution.dangerHandoffFingerprint,
                dangerNonCombatEntryCount = Math.Max(0, execution.dangerNonCombatEntryCount),
                activityBagResolved = execution.activityBagResolved,
                materialsPaid = execution.materialsPaid,
                accumulatedBuildPoints = Math.Max(0f, execution.accumulatedBuildPoints),
                buildingLevelApplied = execution.buildingLevelApplied,
                buildingEventPending = execution.buildingEventPending,
                buildingEventPublished = execution.buildingEventPublished,
                completionPhase = execution.completionPhase,
                linkedCombat = CloneLinkedCombat(execution.linkedCombat),
                pendingResultId = execution.pendingResultId,
                startedAtUnixSeconds = execution.startedAtUnixSeconds
            };
        }

        private static ActivityStagedRewardSaveData[] CloneStagedRewards(ActivityStagedRewardSaveData[] source)
        {
            source ??= Array.Empty<ActivityStagedRewardSaveData>();
            var result = new ActivityStagedRewardSaveData[source.Length];
            for (var index = 0; index < source.Length; index++)
            {
                var entry = source[index];
                result[index] = entry == null ? null : new ActivityStagedRewardSaveData
                {
                    rewardType = entry.rewardType,
                    targetId = entry.targetId,
                    quantity = Math.Max(0L, entry.quantity),
                    origin = entry.origin,
                    quality = entry.quality,
                    instanceId = entry.instanceId
                };
            }
            return result;
        }

        private static LinkedCombatStartRequestSaveData CloneLinkedCombat(LinkedCombatStartRequestSaveData source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.requestId) ||
                string.IsNullOrWhiteSpace(source.rootExecutionId) || string.IsNullOrWhiteSpace(source.occupationOwnerId))
                return null;

            return new LinkedCombatStartRequestSaveData
            {
                requestId = source.requestId,
                rootExecutionId = source.rootExecutionId,
                occupationOwnerId = source.occupationOwnerId,
                heroId = source.heroId,
                dangerEncounterId = source.dangerEncounterId,
                enemyGroupId = source.enemyGroupId,
                combatMode = source.combatMode,
                enemyExpTargetId = source.enemyExpTargetId,
                defeatLossRule = source.defeatLossRule,
                suppressFatigueCost = source.suppressFatigueCost,
                combatExecutionId = source.combatExecutionId,
                resolved = source.resolved,
                loot = CloneStagedRewards(source.loot)
            };
        }

        private static bool OccupiesHero(ActivityExecutionSaveData execution)
        {
            return execution != null && !string.IsNullOrWhiteSpace(execution.heroId) &&
                   (execution.status == ActivityRuntimeStatus.Running || execution.status == ActivityRuntimeStatus.ResultPending);
        }

        private bool TryNormalizeQuestInstance(QuestInstanceSaveData source, out QuestInstanceSaveData quest, out bool changed)
        {
            quest = null;
            changed = false;
            if (source == null || string.IsNullOrWhiteSpace(source.questId) || !IsStructurallyValidQuestInstanceId(source.instanceId) || !QuestInstanceStatus.IsValid(source.status))
                return false;

            var configuredSteps = _configs.GetQuestSteps(source.questId) ?? Array.Empty<QuestStepConfigDto>();
            var configuredStepOrders = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var configuredStep in configuredSteps)
            {
                if (configuredStep != null && !string.IsNullOrWhiteSpace(configuredStep.stepId))
                    configuredStepOrders[configuredStep.stepId] = configuredStep.stepOrder;
            }

            var steps = new List<QuestStepSaveData>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (source.steps != null)
            {
                foreach (var step in source.steps)
                {
                    if (step == null || string.IsNullOrWhiteSpace(step.stepId) || !seen.Add(step.stepId))
                    {
                        changed = true;
                        continue;
                    }

                    if (step.currentValue < 0)
                        changed = true;
                    steps.Add(new QuestStepSaveData
                    {
                        stepId = step.stepId,
                        currentValue = Math.Max(0, step.currentValue),
                        completed = step.completed
                    });
                }
            }

            steps.Sort((left, right) =>
            {
                var leftOrder = configuredStepOrders.TryGetValue(left.stepId, out var lo) ? lo : int.MaxValue;
                var rightOrder = configuredStepOrders.TryGetValue(right.stepId, out var ro) ? ro : int.MaxValue;
                var order = leftOrder.CompareTo(rightOrder);
                return order != 0 ? order : string.CompareOrdinal(left.stepId, right.stepId);
            });
            var sourceSteps = source.steps ?? Array.Empty<QuestStepSaveData>();
            if (source.steps == null || steps.Count != sourceSteps.Length)
            {
                changed = true;
            }
            else
            {
                for (var index = 0; index < steps.Count; index++)
                {
                    if (!string.Equals(steps[index].stepId, sourceSteps[index]?.stepId, StringComparison.Ordinal))
                    {
                        changed = true;
                        break;
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(source.cycleId) && source.cycleId != null)
                changed = true;
            var rewardsGranted = source.rewardsGranted;
            if (string.Equals(source.status, QuestInstanceStatus.Completed, StringComparison.Ordinal) && !rewardsGranted)
            {
                rewardsGranted = true;
                changed = true;
            }
            if (!string.Equals(source.status, QuestInstanceStatus.Completed, StringComparison.Ordinal) && rewardsGranted)
            {
                rewardsGranted = false;
                changed = true;
            }
            quest = new QuestInstanceSaveData
            {
                instanceId = source.instanceId,
                questId = source.questId,
                cycleId = string.IsNullOrWhiteSpace(source.cycleId) ? null : source.cycleId,
                status = source.status,
                rewardsGranted = rewardsGranted,
                pendingResultId = source.pendingResultId,
                steps = steps.ToArray()
            };
            return true;
        }

        private static QuestInstanceSaveData CloneQuestInstance(QuestInstanceSaveData source)
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

            return new QuestInstanceSaveData
            {
                instanceId = source.instanceId,
                questId = source.questId,
                cycleId = source.cycleId,
                status = source.status,
                rewardsGranted = source.rewardsGranted,
                pendingResultId = source.pendingResultId,
                steps = steps
            };
        }

        private static bool IsStructurallyValidQuestInstanceId(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                return false;
            var parts = instanceId.Split(':');
            if (parts.Length < 2)
                return false;
            foreach (var part in parts)
                if (string.IsNullOrWhiteSpace(part) || !string.Equals(part, part.Trim(), StringComparison.Ordinal))
                    return false;
            return true;
        }

        private static ItemInstanceSaveData CloneItemInstance(ItemInstanceSaveData source)
        {
            if (source == null)
                return null;

            return new ItemInstanceSaveData
            {
                instanceId = source.instanceId,
                itemId = source.itemId,
                quality = source.quality,
                stateId = source.stateId,
                ownerType = source.ownerType,
                ownerId = source.ownerId,
                contextType = source.contextType,
                contextId = source.contextId
            };
        }

        private static ItemStackSaveData CloneItemStack(ItemStackSaveData source)
        {
            if (source == null)
                return null;
            return new ItemStackSaveData
            {
                stackId = source.stackId,
                itemId = source.itemId,
                quantity = source.quantity,
                stateId = source.stateId,
                ownerType = source.ownerType,
                ownerId = source.ownerId,
                contextType = source.contextType,
                contextId = source.contextId
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

        internal static string EquipmentSlotKeyForStorage(string heroId, string equipmentSlot) => EquipmentSlotKey(heroId, equipmentSlot);

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

        private string NewUniqueStackId()
        {
            string stackId;
            do stackId = Guid.NewGuid().ToString("N"); while (_itemStacks.ContainsKey(stackId));
            return stackId;
        }

        private static bool NormalizeOwnership(
            string sourceOwnerType,
            string sourceOwnerId,
            string sourceContextType,
            string sourceContextId,
            out string ownerType,
            out string ownerId,
            out string contextType,
            out string contextId)
        {
            var ownerValid = !string.IsNullOrWhiteSpace(sourceOwnerType) && !string.IsNullOrWhiteSpace(sourceOwnerId);
            var contextValid = !string.IsNullOrWhiteSpace(sourceContextType) && !string.IsNullOrWhiteSpace(sourceContextId);
            ownerType = ownerValid ? sourceOwnerType : null;
            ownerId = ownerValid ? sourceOwnerId : null;
            contextType = contextValid ? sourceContextType : null;
            contextId = contextValid ? sourceContextId : null;
            return ownerValid != (!string.IsNullOrWhiteSpace(sourceOwnerType) || !string.IsNullOrWhiteSpace(sourceOwnerId)) ||
                   contextValid != (!string.IsNullOrWhiteSpace(sourceContextType) || !string.IsNullOrWhiteSpace(sourceContextId));
        }

        private static bool TryNormalizeStateBindings(
            ItemStateConfigDto state,
            ref string ownerType,
            ref string ownerId,
            ref string contextType,
            ref string contextId,
            out bool changed)
        {
            changed = false;
            if (state == null)
                return false;

            var needsContext = string.Equals(state.availabilityMode, ItemAvailabilityMode.Reserved, StringComparison.Ordinal) ||
                               string.Equals(state.availabilityMode, ItemAvailabilityMode.InAction, StringComparison.Ordinal);
            var needsOwner = state.requiresOwner || string.Equals(state.availabilityMode, ItemAvailabilityMode.Equipped, StringComparison.Ordinal);
            if (needsContext && contextType == null || needsOwner && ownerType == null)
                return false;

            if (needsContext)
            {
                if (ownerType != null)
                {
                    ownerType = null;
                    ownerId = null;
                    changed = true;
                }
                return true;
            }

            if (needsOwner)
            {
                if (contextType != null)
                {
                    contextType = null;
                    contextId = null;
                    changed = true;
                }
                return true;
            }

            if (ownerType != null)
            {
                ownerType = null;
                ownerId = null;
                changed = true;
            }
            if (contextType != null)
            {
                contextType = null;
                contextId = null;
                changed = true;
            }
            return true;
        }

        private static OperationReceiptSaveData CloneOperationReceipt(OperationReceiptSaveData source) => new OperationReceiptSaveData
        {
            aggregateId = source.aggregateId,
            operationId = source.operationId,
            fingerprint = source.fingerprint,
            success = source.success,
            code = source.code,
            storageRevision = source.storageRevision,
            resultRevision = source.resultRevision,
            stackId = source.stackId,
            instanceId = source.instanceId,
            executionId = source.executionId,
            resultPayload = source.resultPayload,
            quantity = source.quantity,
            resolved = source.resolved
        };

        private static PendingResultSourceReferenceSaveData CloneResultSourceReference(PendingResultSourceReferenceSaveData source) => new PendingResultSourceReferenceSaveData
        {
            sourceType = source.sourceType,
            sourceId = source.sourceId,
            sourceExecutionId = source.sourceExecutionId,
            resultId = source.resultId,
            state = source.state,
            resolutionSequence = source.resolutionSequence
        };

        private static string ResultSourceKey(string sourceType, string sourceExecutionId) => $"{sourceType}\n{sourceExecutionId}";

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
