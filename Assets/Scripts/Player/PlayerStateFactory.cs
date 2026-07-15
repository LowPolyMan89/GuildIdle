using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Progression;

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

        public PlayerState Create(SaveData saveData)
        {
            saveData ??= new SaveData();
            ValidateSaveVersion(saveData.saveVersion);
            ValidateLoadedStage(saveData.currentStageId);
            return new PlayerState(saveData, _heroStats, _configs);
        }

        public PlayerState CreateDefault()
        {
            ValidateStageBootstrap(_bootstrap.InitialStageId);
            var state = new PlayerState(
                new SaveData { currentStageId = _bootstrap.InitialStageId },
                _heroStats,
                _configs);
            ApplyStarterHeroes(state, _bootstrap.InitialStageId);
            ApplyDefaultBuildings(state);
            ApplyNewGameQuests(state);
            ApplyStarterEquipment(state, _bootstrap.InitialStageId);
            return state;
        }

        private void ApplyStarterHeroes(PlayerState state, string stageId)
        {
            foreach (var starterHero in _configs.GetSettlementStageStarterHeroes(stageId))
            {
                var heroId = starterHero.heroId;
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
                if (quest == null || !quest.enabled || !StartsOnNewGame(quest.questId))
                    continue;

                var configuredSteps = _configs.GetQuestSteps(quest.questId);
                var existing = state.GetQuestState(quest.questId);
                if (existing == null)
                {
                    state.SetQuestState(QuestStateBuilder.Create(quest.questId, configuredSteps));
                    continue;
                }

                state.SetQuestState(QuestStateBuilder.Reconcile(existing, configuredSteps, out _));
            }
        }

        private void ApplyStarterEquipment(PlayerState state, string stageId)
        {
            foreach (var loadout in _configs.GetSettlementStageStarterEquipment(stageId))
            {
                var instanceId = state.AddItemInstance(loadout.itemId, PlayerState.OnStorageItemStateId);
                state.EquipItemInstance(loadout.heroId, loadout.equipmentSlot, instanceId);
            }
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

        private void ValidateStageBootstrap(string stageId)
        {
            ValidateLoadedStage(stageId);

            var starterHeroes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var starterHero in _configs.GetSettlementStageStarterHeroes(stageId))
            {
                var heroId = starterHero?.heroId;
                if (string.IsNullOrWhiteSpace(heroId) || !starterHeroes.Add(heroId) ||
                    !_configs.TryGetHero(heroId, out var hero) || hero == null || !hero.enabled)
                {
                    throw new InvalidOperationException($"Invalid starter hero '{heroId}'.");
                }
            }

            var occupiedSlots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var loadout in _configs.GetSettlementStageStarterEquipment(stageId))
            {
                var slotKey = loadout == null ? null : $"{loadout.heroId}\n{loadout.equipmentSlot}";
                if (loadout == null || !starterHeroes.Contains(loadout.heroId) || !occupiedSlots.Add(slotKey) ||
                    !_configs.TryGetEquipmentSlot(loadout.itemId, out var configuredSlot) ||
                    !string.Equals(configuredSlot, loadout.equipmentSlot, StringComparison.Ordinal))
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
            if (version != SaveData.CurrentSaveVersion)
                throw new SaveCompatibilityException($"Save version '{version}' does not match supported version '{SaveData.CurrentSaveVersion}'.");
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
