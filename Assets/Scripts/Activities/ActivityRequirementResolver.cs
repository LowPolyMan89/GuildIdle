using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Activities
{
    public static class ActivityRequirementResolver
    {
        public static ActivityRequirementIssue[] GetMissingRequirements(ActivityExecutionContext context, IActivityPlayerState state)
        {
            var issues = new List<ActivityRequirementIssue>();
            if (!ActivityResolverUtilities.TryGetActivity(context?.activityId, issues, out _))
                return issues.ToArray();

            if (!ActivityResolverUtilities.ValidateExecutionContext(context, state, issues))
                return issues.ToArray();

            foreach (var requirement in RuntimeConfigs.Activities.GetRequirements(context.activityId))
                CheckRequirement(requirement, context, state, issues);

            return issues.ToArray();
        }

        private static ActivityRequirementIssue[] GetMissingRequirements(string activityId, ActivityExecutionContext context, IActivityPlayerState state)
        {
            var issues = new List<ActivityRequirementIssue>();
            if (!ActivityResolverUtilities.TryGetActivity(activityId, issues, out _))
                return issues.ToArray();

            foreach (var requirement in RuntimeConfigs.Activities.GetRequirements(activityId))
                CheckRequirement(requirement, context, state, issues);

            return issues.ToArray();
        }

        internal static void CheckRequirement(ActivityRequirementConfigDto requirement, ActivityExecutionContext context, IActivityPlayerState state, List<ActivityRequirementIssue> issues)
        {
            if (requirement == null)
                return;

            var activityId = requirement.activityId;
            var targetId = requirement.targetId;
            var required = ActivityResolverUtilities.RequirementAmount(requirement.value);
            var type = requirement.reqType ?? string.Empty;

            if (RequirementType.Matches(type, RequirementType.BuildingLevel) ||
                RequirementType.Matches(type, RequirementType.Building))
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

            if (RequirementType.Matches(type, RequirementType.LocationUnlocked))
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

            if (RequirementType.Matches(type, RequirementType.HeroAvailable))
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

            if (RequirementType.Matches(type, RequirementType.ActivityCompleted))
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

            if (RequirementType.Matches(type, RequirementType.Currency))
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

            if (RequirementType.Matches(type, RequirementType.SkillLevel))
            {
                if (!ActivityResolverUtilities.IsKnownSkill(targetId))
                {
                    Unknown(issues, activityId, type, targetId);
                    return;
                }

                if (context == null || string.IsNullOrWhiteSpace(context.heroId))
                {
                    ActivityResolverUtilities.AddIssue(issues, activityId, type, targetId, required, 0, true, false, $"[ActivityRequirementResolver] Requirement '{type}' needs an executor hero context.");
                    return;
                }

                var current = state.GetHeroSkillLevel(context.heroId, targetId);
                if (current < required)
                    Missing(issues, activityId, type, targetId, required, current);
                return;
            }

            if (RequirementType.Matches(type, RequirementType.ItemEquipped))
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
