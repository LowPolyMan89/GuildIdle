using System;
using System.Collections.Generic;
using GuildIdle.Configs;

namespace GuildIdle.Player
{
    public sealed class SaveCompatibilityException : Exception
    {
        public SaveCompatibilityException(string message) : base(message)
        {
        }
    }

    public sealed class PlayerStateFactory
    {
        private const string NewGameConditionType = "NewGame";

        private readonly IPlayerBootstrapConfigProvider _configs;
        private readonly HeroStatsService _heroStats;
        private readonly PlayerBootstrapDefinition _bootstrap;

        public PlayerStateFactory(
            IPlayerBootstrapConfigProvider configs,
            HeroStatsService heroStats,
            PlayerBootstrapDefinition bootstrap)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _heroStats = heroStats ?? throw new ArgumentNullException(nameof(heroStats));
            _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        }

        public PlayerState Create(SaveData saveData, HeroSlotSaveEntry[] legacyHeroSlots = null)
        {
            saveData ??= new SaveData();
            ValidateSaveVersion(saveData.saveVersion);

            if (saveData.saveVersion >= SaveData.CurrentSaveVersion)
                ValidateLoadedStage(saveData.currentStageId);
            else if (!string.IsNullOrWhiteSpace(saveData.currentStageId))
                ValidateLoadedStage(saveData.currentStageId);

            var state = new PlayerState(saveData, legacyHeroSlots, _heroStats, _configs);
            if (saveData.saveVersion < SaveData.CurrentSaveVersion)
            {
                ValidateBootstrapDefinition();
                ApplyLegacyBootstrap(state);
                state.MarkNormalized();
            }

            return state;
        }

        public PlayerState CreateDefault()
        {
            ValidateBootstrapDefinition();
            var state = new PlayerState(
                new SaveData { currentStageId = _bootstrap.InitialStageId },
                _heroStats,
                _configs);
            ApplyStarterHeroes(state);
            ApplyDefaultBuildings(state);
            ApplyNewGameQuests(state);
            ApplyStarterEquipment(state);
            return state;
        }

        private void ApplyLegacyBootstrap(PlayerState state)
        {
            if (string.IsNullOrWhiteSpace(state.CurrentStageId))
                state.SetCurrentStage(_bootstrap.InitialStageId);

            ApplyStarterHeroes(state);
            ApplyDefaultBuildings(state);
            ApplyNewGameQuests(state);
            ApplyStarterEquipment(state);
        }

        private void ApplyStarterHeroes(PlayerState state)
        {
            foreach (var heroId in _bootstrap.StarterHeroIds)
            {
                state.AddHero(heroId);
                foreach (var skill in _configs.Skills)
                {
                    if (skill != null && !string.IsNullOrWhiteSpace(skill.skillId))
                        state.EnsureHeroSkill(heroId, skill.skillId);
                }

                if (!_configs.TryGetHero(heroId, out var hero) || hero?.uniqueSkillIds == null)
                    continue;

                var uniqueSkills = new HashSet<string>(hero.uniqueSkillIds, StringComparer.Ordinal);
                foreach (var effect in _configs.HeroSkillEffects)
                {
                    if (effect == null || string.IsNullOrWhiteSpace(effect.effectId) ||
                        !uniqueSkills.Contains(effect.skillId) || !HasPersistentInterval(effect.interval))
                    {
                        continue;
                    }

                    if (state.GetHeroState(heroId)?.effectCounters == null ||
                        !HasEffectCounter(state.GetHeroState(heroId).effectCounters, effect.effectId))
                    {
                        state.SetHeroEffectCounter(heroId, effect.effectId, 0L);
                    }
                }
            }
        }

        private void ApplyDefaultBuildings(PlayerState state)
        {
            foreach (var building in _configs.Buildings)
            {
                if (building == null || !building.visibleAtStart || state.IsBuildingUnlocked(building.buildingId))
                    continue;

                state.UnlockBuilding(building.buildingId);
                state.SetBuildingLevel(building.buildingId, building.startLevel);
            }
        }

        private void ApplyNewGameQuests(PlayerState state)
        {
            foreach (var quest in _configs.Quests)
            {
                if (quest == null || !StartsOnNewGame(quest.questId))
                    continue;

                var configuredSteps = _configs.GetQuestSteps(quest.questId);
                var existing = state.GetQuestState(quest.questId);
                if (existing == null)
                {
                    var steps = new QuestStepSaveData[configuredSteps.Length];
                    for (var i = 0; i < configuredSteps.Length; i++)
                    {
                        steps[i] = NewQuestStep(configuredSteps[i]);
                    }

                    state.SetQuestState(new QuestSaveData
                    {
                        questId = quest.questId,
                        completed = false,
                        rewardsGranted = false,
                        steps = steps
                    });
                    continue;
                }

                var mergedSteps = new List<QuestStepSaveData>(existing.steps ?? Array.Empty<QuestStepSaveData>());
                var knownSteps = new HashSet<string>(StringComparer.Ordinal);
                foreach (var step in mergedSteps)
                {
                    if (step != null)
                        knownSteps.Add(step.stepId);
                }

                foreach (var configuredStep in configuredSteps)
                {
                    if (configuredStep != null && knownSteps.Add(configuredStep.stepId))
                        mergedSteps.Add(NewQuestStep(configuredStep));
                }

                existing.steps = mergedSteps.ToArray();
                state.SetQuestState(existing);
            }
        }

        private static QuestStepSaveData NewQuestStep(QuestStepConfigDto configuredStep)
        {
            return new QuestStepSaveData
            {
                stepId = configuredStep?.stepId,
                currentValue = 0,
                completed = false
            };
        }

        private void ApplyStarterEquipment(PlayerState state)
        {
            foreach (var loadout in _bootstrap.StarterEquipment)
            {
                if (state.GetEquippedItem(loadout.HeroId, loadout.EquipmentSlot) == null)
                {
                    var instanceId = FindFreeInstance(state, loadout.ItemId);
                    if (instanceId == null && state.GetItem(loadout.ItemId) > 0)
                    {
                        state.SpendItem(loadout.ItemId, 1);
                        instanceId = state.AddItemInstance(loadout.ItemId, PlayerState.OnStorageItemStateId);
                    }

                    instanceId ??= state.AddItemInstance(loadout.ItemId, PlayerState.OnStorageItemStateId);
                    state.EquipItemInstance(loadout.HeroId, loadout.EquipmentSlot, instanceId);
                }

                while (state.GetItem(loadout.ItemId) > 0)
                {
                    state.SpendItem(loadout.ItemId, 1);
                    state.AddItemInstance(loadout.ItemId, PlayerState.OnStorageItemStateId);
                }
            }
        }

        private static string FindFreeInstance(PlayerState state, string itemId)
        {
            var equipped = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in state.GetEquipmentSlots())
                equipped.Add(slot.itemInstanceId);

            foreach (var instance in state.GetItemInstances())
            {
                if (string.Equals(instance.itemId, itemId, StringComparison.Ordinal) &&
                    string.Equals(instance.stateId, PlayerState.OnStorageItemStateId, StringComparison.Ordinal) &&
                    !equipped.Contains(instance.instanceId))
                {
                    return instance.instanceId;
                }
            }

            return null;
        }

        private bool StartsOnNewGame(string questId)
        {
            foreach (var condition in _configs.GetQuestStartConditions(questId))
            {
                if (condition != null && string.Equals(
                        condition.conditionType,
                        NewGameConditionType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void ValidateBootstrapDefinition()
        {
            ValidateLoadedStage(_bootstrap.InitialStageId);

            var starterHeroes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var heroId in _bootstrap.StarterHeroIds)
            {
                if (string.IsNullOrWhiteSpace(heroId) || !starterHeroes.Add(heroId) ||
                    !_configs.TryGetHero(heroId, out var hero) || hero == null || !hero.enabled)
                {
                    throw new InvalidOperationException($"Invalid starter hero '{heroId}'.");
                }
            }

            foreach (var loadout in _bootstrap.StarterEquipment)
            {
                if (loadout == null || !starterHeroes.Contains(loadout.HeroId) ||
                    !_configs.TryGetEquipmentSlot(loadout.ItemId, out var configuredSlot) ||
                    !string.Equals(configuredSlot, loadout.EquipmentSlot, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Invalid starter equipment definition.");
                }
            }

            if (!_configs.IsKnownItemState(PlayerState.EquippedItemStateId) ||
                !_configs.IsKnownItemState(PlayerState.OnStorageItemStateId))
            {
                throw new InvalidOperationException("Required item states 'equipped' and 'on_storage' are missing.");
            }
        }

        private void ValidateLoadedStage(string stageId)
        {
            if (string.IsNullOrWhiteSpace(stageId) ||
                !_configs.TryGetSettlementStage(stageId, out var stage) || stage == null || !stage.enabled)
            {
                throw new SaveCompatibilityException($"Save references an unknown or disabled stage '{stageId}'.");
            }
        }

        private static void ValidateSaveVersion(int version)
        {
            if (version > SaveData.CurrentSaveVersion)
                throw new SaveCompatibilityException($"Save version '{version}' is newer than supported version '{SaveData.CurrentSaveVersion}'.");
        }

        private static bool HasPersistentInterval(string interval)
        {
            return int.TryParse(interval, out var value) && value > 0;
        }

        private static bool HasEffectCounter(HeroEffectCounterSaveData[] counters, string effectId)
        {
            foreach (var counter in counters)
            {
                if (counter != null && string.Equals(counter.effectId, effectId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
