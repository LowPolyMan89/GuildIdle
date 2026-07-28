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
        private readonly IPlayerBootstrapConfigProvider _configs;
        private readonly HeroStatsService _heroStats;
        private readonly PlayerBootstrapDefinition _bootstrap;
        private readonly Func<PlayerState, IPendingResultSourceHandler>[] _pendingResultSourceHandlerFactories;
        private readonly ITimeProvider _timeProvider;

        public PlayerStateFactory(
            IPlayerBootstrapConfigProvider configs,
            HeroStatsService heroStats,
            PlayerBootstrapDefinition bootstrap,
            Func<PlayerState, IPendingResultSourceHandler>[] pendingResultSourceHandlerFactories = null,
            ITimeProvider timeProvider = null)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _heroStats = heroStats ?? throw new ArgumentNullException(nameof(heroStats));
            _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
            _pendingResultSourceHandlerFactories = pendingResultSourceHandlerFactories == null
                ? Array.Empty<Func<PlayerState, IPendingResultSourceHandler>>()
                : (Func<PlayerState, IPendingResultSourceHandler>[])pendingResultSourceHandlerFactories.Clone();
            _timeProvider = timeProvider ?? SystemUtcTimeProvider.Instance;
        }

        public PlayerState Create(SaveData saveData)
        {
            saveData ??= new SaveData();
            ValidateSaveVersion(saveData.saveVersion);
            ValidateLoadedStage(saveData.currentStageId);
            return new PlayerState(saveData, _heroStats, _configs, _pendingResultSourceHandlerFactories, _timeProvider);
        }

        public PlayerState CreateDefault()
        {
            ValidateStageBootstrap(_bootstrap.InitialStageId);
            var state = new PlayerState(
                new SaveData { currentStageId = _bootstrap.InitialStageId },
                _heroStats,
                _configs,
                _pendingResultSourceHandlerFactories,
                _timeProvider);
            ApplyStarterHeroes(state, _bootstrap.InitialStageId);
            ApplyDefaultBuildings(state);
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

        private void ApplyStarterEquipment(PlayerState state, string stageId)
        {
            if (!_configs.TryGetItemStateByAvailabilityMode(ItemAvailabilityMode.Available, out var availableState))
                throw new InvalidOperationException("Storage config has no available item state.");
            foreach (var loadout in _configs.GetSettlementStageStarterEquipment(stageId))
            {
                var instanceId = state.AddItemInstance(loadout.itemId, availableState.stateId);
                state.EquipItemInstance(loadout.heroId, loadout.equipmentSlot, instanceId);
            }
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

            if (!_configs.TryGetItemStateByAvailabilityMode(ItemAvailabilityMode.Equipped, out _) ||
                !_configs.TryGetItemStateByAvailabilityMode(ItemAvailabilityMode.Available, out _))
            {
                throw new InvalidOperationException("Required available and equipped item states are missing.");
            }
        }

        private void ValidateLoadedStage(string stageId)
        {
            if (string.IsNullOrWhiteSpace(stageId) ||
                !_configs.TryGetStage(stageId, out var stage) || stage == null || !stage.enabled)
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
