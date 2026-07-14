using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Core;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Activities
{
    internal static class ActivityResolverUtilities
    {
        public const string GoldCurrencyId = "gold_id";
        public const string OwnerHero = "Hero";
        public const string OwnerProfile = "Profile";

        public static IActivityRandom DefaultRandom() => new SystemActivityRandom();

        public static bool TryGetActivity(string activityId, List<ActivityRequirementIssue> issues, out ActivityConfigDto activity)
        {
            activity = null;
            if (!RuntimeConfigs.IsLoaded)
            {
                AddIssue(issues, activityId, "Configs", string.Empty, 0, 0, true, false, "[ActivityResolver] Runtime configs are not loaded.");
                return false;
            }

            if (RuntimeConfigs.Activities.TryGet(activityId, out activity))
                return true;

            AddIssue(issues, activityId, "Activity", activityId, 0, 0, true, false, $"[ActivityResolver] Unknown activity id '{activityId}'.");
            Debug.LogError($"[ActivityResolver] Unknown activity id '{activityId}'.");
            return false;
        }

        public static bool ValidateExecutionContext(ActivityExecutionContext context, IActivityPlayerState state, List<ActivityRequirementIssue> issues)
        {
            if (context == null)
            {
                AddIssue(issues, string.Empty, "ActivityExecutionContext", string.Empty, 1, 0, true, false, "[ActivityResolver] ActivityExecutionContext is required.");
                return false;
            }

            var valid = true;
            var activityId = context.activityId ?? string.Empty;

            if (state == null)
            {
                AddIssue(issues, activityId, "PlayerState", string.Empty, 1, 0, true, false, "[ActivityResolver] Player state is required.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(context.heroId))
            {
                AddIssue(issues, activityId, "HeroExecutor", string.Empty, 1, 0, true, false, "[ActivityResolver] Context heroId is required.");
                valid = false;
            }
            else
            {
                var hasHero = state.HasHero(context.heroId);
                var hasHeroState = false;
                if (!hasHero)
                {
                    AddIssue(issues, activityId, "HeroAvailable", context.heroId, 1, 0, false, false, $"Hero '{context.heroId}' is not acquired.");
                    valid = false;
                }
                else
                {
                    hasHeroState = state.HasHeroState(context.heroId);
                    if (!hasHeroState)
                    {
                        AddIssue(issues, activityId, "HeroState", context.heroId, 1, 0, true, false, $"Hero '{context.heroId}' has no runtime state.");
                        valid = false;
                    }
                }

                if (!hasHeroState)
                    return valid;

                if (string.IsNullOrWhiteSpace(context.executionId))
                {
                    AddIssue(issues, activityId, "ActivityExecution", string.Empty, 1, 0, true, false, "[ActivityResolver] Context executionId is required.");
                    valid = false;
                }
                else
                {
                    var currentExecutionId = state.GetHeroCurrentActivityExecutionId(context.heroId);
                    if (!string.IsNullOrWhiteSpace(currentExecutionId) &&
                        !string.Equals(currentExecutionId, context.executionId, StringComparison.Ordinal))
                    {
                        AddIssue(issues, activityId, "HeroBusy", context.heroId, 1, 1, false, false, $"Hero '{context.heroId}' is busy with execution '{currentExecutionId}'.");
                        valid = false;
                    }
                }
            }

            return valid;
        }

        public static void AddIssue(
            List<ActivityRequirementIssue> issues,
            string activityId,
            string issueType,
            string targetId,
            int requiredAmount,
            long currentAmount,
            bool isError,
            bool isNotImplemented,
            string message)
        {
            issues.Add(new ActivityRequirementIssue
            {
                activityId = activityId,
                issueType = issueType,
                targetId = targetId,
                requiredAmount = requiredAmount,
                currentAmount = currentAmount,
                isError = isError,
                isNotImplemented = isNotImplemented,
                message = message
            });
        }

        public static int PositiveAmount(int min, int max, IActivityRandom random)
        {
            var low = Math.Max(1, min);
            var high = Math.Max(low, max);
            return random.RangeInclusive(low, high);
        }

        public static int RequirementAmount(int value)
        {
            return Math.Max(1, value);
        }

        public static bool ChancePassed(float chance, IActivityRandom random)
        {
            return chance >= 100f || (chance > 0f && random.Percent() <= chance);
        }

        public static bool MomentMatches(string actual, string expected)
        {
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAnyItemType(string type)
        {
            return ActivityTypeParser.TryParseRequirementType(type, out var parsedType) &&
                (parsedType == RequirementTypeEnum.Item ||
                 parsedType == RequirementTypeEnum.ItemCount ||
                 parsedType == RequirementTypeEnum.Resource);
        }

        public static bool IsEquipment(string id)
        {
            return RuntimeConfigs.Items.TryGetEquipmentWeapon(id, out _) ||
                RuntimeConfigs.Items.TryGetEquipmentArmor(id, out _);
        }

        public static bool IsKnownSkill(string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId))
                return false;

            foreach (var skill in RuntimeConfigs.Activities.Skills)
            {
                if (skill != null && string.Equals(skill.skillId, skillId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
