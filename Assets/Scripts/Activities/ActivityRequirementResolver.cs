using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Core;
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

            if (!RequirementType.TryParse(type, out var parsedType))
            {
                Unsupported(issues, activityId, type, targetId, required);
                return;
            }

            switch (parsedType)
            {
                case RequirementTypeEnum.BuildingLevel:
                case RequirementTypeEnum.Building:
                    if (!RuntimeConfigs.Buildings.TryGet(targetId, out _))
                    {
                        Unknown(issues, activityId, type, targetId);
                        return;
                    }

                    var buildingLevel = state.GetBuildingLevel(targetId);
                    if (buildingLevel < required)
                        Missing(issues, activityId, type, targetId, required, buildingLevel);
                    return;

                case RequirementTypeEnum.LocationUnlocked:
                    if (!RuntimeConfigs.Map.TryGetLocation(targetId, out _))
                    {
                        Unknown(issues, activityId, type, targetId);
                        return;
                    }

                    if (!state.IsLocationUnlocked(targetId))
                        Missing(issues, activityId, type, targetId, required, 0);
                    return;

                case RequirementTypeEnum.HeroAvailable:
                    if (!RuntimeConfigs.Heroes.TryGet(targetId, out _))
                    {
                        Unknown(issues, activityId, type, targetId);
                        return;
                    }

                    if (!state.HasHero(targetId))
                        Missing(issues, activityId, type, targetId, required, 0);
                    return;

                case RequirementTypeEnum.ActivityCompleted:
                    if (!RuntimeConfigs.Activities.TryGet(targetId, out _))
                    {
                        Unknown(issues, activityId, type, targetId);
                        return;
                    }

                    if (!state.IsActivityCompleted(targetId))
                        Missing(issues, activityId, type, targetId, required, 0);
                    return;

                case RequirementTypeEnum.Resource:
                    if (!RuntimeConfigs.Items.TryGetResource(targetId, out _))
                    {
                        Unknown(issues, activityId, type, targetId);
                        return;
                    }

                    var resourceCount = state.GetItem(targetId);
                    if (resourceCount < required)
                        Missing(issues, activityId, type, targetId, required, resourceCount);
                    return;

                case RequirementTypeEnum.Item:
                case RequirementTypeEnum.ItemCount:
                    if (!RuntimeConfigs.Items.TryGet(targetId, out _))
                    {
                        Unknown(issues, activityId, type, targetId);
                        return;
                    }

                    var itemCount = state.GetItem(targetId);
                    if (itemCount < required)
                        Missing(issues, activityId, type, targetId, required, itemCount);
                    return;

                case RequirementTypeEnum.Currency:
                    if (!RuntimeConfigs.Items.TryGetCurrency(targetId, out _))
                    {
                        Unknown(issues, activityId, type, targetId);
                        return;
                    }

                    var currency = state.GetCurrency(targetId);
                    if (currency < required)
                        Missing(issues, activityId, type, targetId, required, currency);
                    return;

                case RequirementTypeEnum.SkillLevel:
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

                    var skillLevel = state.GetHeroSkillLevel(context.heroId, targetId);
                    if (skillLevel < required)
                        Missing(issues, activityId, type, targetId, required, skillLevel);
                    return;

                case RequirementTypeEnum.HeroLevel:
                case RequirementTypeEnum.HeroClass:
                case RequirementTypeEnum.ItemEquipped:
                case RequirementTypeEnum.QuestCompleted:
                    ActivityResolverUtilities.AddIssue(issues, activityId, type, targetId, required, 0, true, true, $"[ActivityRequirementResolver] Requirement '{type}' is recognized but not implemented in PlayerState yet.");
                    return;

                default:
                    Unsupported(issues, activityId, type, targetId, required);
                    return;
            }
        }

        private static void Unsupported(List<ActivityRequirementIssue> issues, string activityId, string type, string targetId, int required)
        {
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
