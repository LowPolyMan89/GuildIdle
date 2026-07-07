using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Activities
{
    public static class ActivityRequirementResolver
    {
        public static ActivityRequirementIssue[] GetMissingRequirements(string activityId)
        {
            return GetMissingRequirements(activityId, ActivityResolverUtilities.DefaultState());
        }

        public static ActivityRequirementIssue[] GetMissingRequirements(string activityId, IActivityPlayerState state)
        {
            var issues = new List<ActivityRequirementIssue>();
            if (!ActivityResolverUtilities.TryGetActivity(activityId, issues, out _))
                return issues.ToArray();

            foreach (var requirement in RuntimeConfigs.Activities.GetRequirements(activityId))
                CheckRequirement(requirement, state, issues);

            return issues.ToArray();
        }

        internal static void CheckRequirement(ActivityRequirementConfigDto requirement, IActivityPlayerState state, List<ActivityRequirementIssue> issues)
        {
            if (requirement == null)
                return;

            var activityId = requirement.activityId;
            var targetId = requirement.targetId;
            var required = ActivityResolverUtilities.RequirementAmount(requirement.value);
            var type = requirement.reqType ?? string.Empty;

            if (string.Equals(type, "BuildingLevel", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Building", StringComparison.OrdinalIgnoreCase))
            {
                if (!RuntimeConfigs.Buildings.TryGet(targetId, out _))
                {
                    Unknown(issues, activityId, type, targetId);
                    return;
                }

                var current = state.GetBuildingLevel(targetId);
                if (current < required)
                    Missing(issues, activityId, type, targetId, required, current);
                return;
            }

            if (string.Equals(type, "LocationUnlocked", StringComparison.OrdinalIgnoreCase))
            {
                if (!RuntimeConfigs.Map.TryGetLocation(targetId, out _))
                {
                    Unknown(issues, activityId, type, targetId);
                    return;
                }

                if (!state.IsLocationUnlocked(targetId))
                    Missing(issues, activityId, type, targetId, required, 0);
                return;
            }

            if (string.Equals(type, "HeroAvailable", StringComparison.OrdinalIgnoreCase))
            {
                if (!RuntimeConfigs.Heroes.TryGet(targetId, out _))
                {
                    Unknown(issues, activityId, type, targetId);
                    return;
                }

                if (!state.HasHero(targetId))
                    Missing(issues, activityId, type, targetId, required, 0);
                return;
            }

            if (string.Equals(type, "ActivityCompleted", StringComparison.OrdinalIgnoreCase))
            {
                if (!RuntimeConfigs.Activities.TryGet(targetId, out _))
                {
                    Unknown(issues, activityId, type, targetId);
                    return;
                }

                if (!state.IsActivityCompleted(targetId))
                    Missing(issues, activityId, type, targetId, required, 0);
                return;
            }

            if (ActivityResolverUtilities.IsAnyItemType(type))
            {
                if (!RuntimeConfigs.Items.TryGet(targetId, out _))
                {
                    Unknown(issues, activityId, type, targetId);
                    return;
                }

                var current = state.GetItem(targetId);
                if (current < required)
                    Missing(issues, activityId, type, targetId, required, current);
                return;
            }

            if (string.Equals(type, "Currency", StringComparison.OrdinalIgnoreCase))
            {
                if (!RuntimeConfigs.Items.TryGetCurrency(targetId, out _))
                {
                    Unknown(issues, activityId, type, targetId);
                    return;
                }

                var current = state.GetCurrency(targetId);
                if (current < required)
                    Missing(issues, activityId, type, targetId, required, current);
                return;
            }

            if (string.Equals(type, "SkillLevel", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "ItemEquipped", StringComparison.OrdinalIgnoreCase))
            {
                ActivityResolverUtilities.AddIssue(issues, activityId, type, targetId, required, 0, false, true, $"[ActivityRequirementResolver] Requirement '{type}' is not implemented in PlayerState yet.");
                return;
            }

            ActivityResolverUtilities.AddIssue(issues, activityId, type, targetId, required, 0, true, false, $"[ActivityRequirementResolver] Unsupported requirement type '{type}'.");
            Debug.LogError($"[ActivityRequirementResolver] Unsupported requirement type '{type}' for activity '{activityId}'.");
        }

        private static void Missing(List<ActivityRequirementIssue> issues, string activityId, string type, string targetId, int required, long current)
        {
            ActivityResolverUtilities.AddIssue(issues, activityId, type, targetId, required, current, false, false, $"Missing {type} '{targetId}': {current}/{required}.");
        }

        private static void Unknown(List<ActivityRequirementIssue> issues, string activityId, string type, string targetId)
        {
            ActivityResolverUtilities.AddIssue(issues, activityId, type, targetId, 0, 0, true, false, $"Unknown {type} target id '{targetId}'.");
            Debug.LogError($"[ActivityRequirementResolver] Unknown {type} target id '{targetId}' for activity '{activityId}'.");
        }
    }
}
