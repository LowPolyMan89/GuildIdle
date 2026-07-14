using GuildIdle.Configs;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Player
{
    public static class PlayerStateFactory
    {
        public static PlayerState CreateDefault()
        {
            var state = new PlayerState(new SaveData());
            ApplyDefaultBootstrap(state);
            return state;
        }

        private static void ApplyDefaultBootstrap(PlayerState state)
        {
            ApplyDefaultBuildingsBootstrap(state);

            const string starterActivityId = "starter_hero_available";
            const string starterHeroId = "ren";
            const string starterEquipmentId = "item_wooden_club";

            if (!ValidateActivityId(starterActivityId))
                return;

            var rewards = RuntimeConfigs.Activities.GetRewards(starterActivityId);
            var grantedStarterHero = false;

            foreach (var reward in rewards)
            {
                if (reward == null)
                    continue;

                if (IsReward(reward, "Hero", starterHeroId))
                {
                    state.AddHero(starterHeroId);
                    grantedStarterHero = true;
                    continue;
                }

                if (IsReward(reward, "Equipment", starterEquipmentId))
                    state.AddItem(starterEquipmentId, Mathf.Max(1, reward.min));
            }

            if (!grantedStarterHero)
                Debug.LogError($"[PlayerStateFactory] Starter bootstrap '{starterActivityId}' has no Hero reward for '{starterHeroId}'.");
        }

        private static void ApplyDefaultBuildingsBootstrap(PlayerState state)
        {
            if (!RuntimeConfigs.IsLoaded)
            {
                Debug.LogError("[PlayerStateFactory] Cannot bootstrap buildings before runtime configs are loaded.");
                return;
            }

            foreach (var building in RuntimeConfigs.Buildings.Buildings)
            {
                if (building == null || !building.visibleAtStart)
                    continue;

                state.UnlockBuilding(building.buildingId);
                state.SetBuildingLevel(building.buildingId, building.startLevel);
            }
        }

        private static bool ValidateActivityId(string activityId)
        {
            if (!RuntimeConfigs.IsLoaded)
            {
                Debug.LogError("[PlayerStateFactory] Cannot validate activity before runtime configs are loaded.");
                return false;
            }

            if (!RuntimeConfigs.Activities.TryGet(activityId, out _))
            {
                Debug.LogError($"[PlayerStateFactory] Starter activity '{activityId}' not found in configs.");
                return false;
            }

            return true;
        }

        private static bool IsReward(ActivityRewardConfigDto reward, string rewardType, string targetId)
        {
            return string.Equals(reward.rewardType, rewardType, System.StringComparison.OrdinalIgnoreCase) &&
                string.Equals(reward.targetId, targetId, System.StringComparison.Ordinal);
        }
    }
}