using System;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Activities
{
    public static class ActiveHeroLimitResolver
    {
        public const string HallBuildingId = "building_hall";

        public static int GetCurrentLimit(IActivityPlayerState state)
        {
            if (state == null)
            {
                Debug.LogError("[ActiveHeroLimitResolver] Player state is required.");
                return 0;
            }

            if (!RuntimeConfigs.IsLoaded)
            {
                Debug.LogError("[ActiveHeroLimitResolver] Runtime configs are not loaded.");
                return 0;
            }

            var hallLevel = Math.Max(0, state.GetBuildingLevel(HallBuildingId));
            if (!RuntimeConfigs.Buildings.TryGetBuildingLevel(HallBuildingId, hallLevel, out var level))
            {
                Debug.LogError($"[ActiveHeroLimitResolver] Missing {HallBuildingId}:{hallLevel} building level config.");
                return 0;
            }

            return Math.Max(0, level.activeHeroLimit);
        }
    }
}
