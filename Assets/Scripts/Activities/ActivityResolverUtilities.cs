using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Activities
{
    internal static class ActivityResolverUtilities
    {
        public const string GoldCurrencyId = "gold_id";

        public static IActivityPlayerState DefaultState() => new PlayerActivityAdapter();

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
            return string.Equals(type, "Item", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "ItemCount", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Resource", StringComparison.OrdinalIgnoreCase);
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
