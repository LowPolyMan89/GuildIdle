using System;
using GuildIdle.Core;
using UnityEngine;

namespace GuildIdle.Player
{
    public sealed class PlayerStateFactory
    {
        private readonly IPlayerBootstrapConfigProvider _configs;
        private readonly HeroStatsService _heroStats;
        private readonly string _starterActivityId;

        public PlayerStateFactory(
            IPlayerBootstrapConfigProvider configs,
            HeroStatsService heroStats,
            string starterActivityId)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _heroStats = heroStats ?? throw new ArgumentNullException(nameof(heroStats));
            _starterActivityId = string.IsNullOrWhiteSpace(starterActivityId)
                ? throw new ArgumentException("Starter activity id is required.", nameof(starterActivityId))
                : starterActivityId;
        }

        public PlayerState Create(SaveData saveData, HeroSlotSaveEntry[] legacyHeroSlots = null)
        {
            return new PlayerState(saveData, legacyHeroSlots, _heroStats);
        }

        public PlayerState CreateDefault()
        {
            var state = Create(new SaveData());
            ApplyDefaultBuildingsBootstrap(state);
            ApplyStarterActivityBootstrap(state);
            return state;
        }

        private void ApplyStarterActivityBootstrap(PlayerState state)
        {
            if (!_configs.TryGetActivity(_starterActivityId, out _))
            {
                Debug.LogError($"[PlayerStateFactory] Starter activity '{_starterActivityId}' not found in configs.");
                return;
            }

            var grantedStarterHero = false;
            foreach (var reward in _configs.GetRewards(_starterActivityId))
            {
                if (reward == null)
                    continue;

                if (!ActivityTypeParser.TryParseRewardType(reward.rewardType, out var rewardType))
                {
                    Debug.LogError(
                        $"[PlayerStateFactory] Unsupported reward type '{reward.rewardType}' " +
                        $"for starter activity '{_starterActivityId}'.");
                    continue;
                }

                if (rewardType == RewardTypeEnum.Hero)
                {
                    state.AddHero(reward.targetId);
                    grantedStarterHero = true;
                    continue;
                }

                if (rewardType == RewardTypeEnum.Equipment)
                    state.AddItem(reward.targetId, Mathf.Max(1, reward.min));
            }

            if (!grantedStarterHero)
                Debug.LogError($"[PlayerStateFactory] Starter bootstrap '{_starterActivityId}' has no Hero reward.");
        }

        private void ApplyDefaultBuildingsBootstrap(PlayerState state)
        {
            foreach (var building in _configs.Buildings)
            {
                if (building == null || !building.visibleAtStart)
                    continue;

                state.UnlockBuilding(building.buildingId);
                state.SetBuildingLevel(building.buildingId, building.startLevel);
            }
        }
    }
}
