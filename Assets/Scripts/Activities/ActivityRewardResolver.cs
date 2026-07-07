using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Activities
{
    public static class ActivityRewardResolver
    {
        public static ActivityRewardResult PreviewRewards(string activityId, string grantMoment)
        {
            return PreviewRewards(activityId, grantMoment, ActivityResolverUtilities.DefaultState());
        }

        public static ActivityRewardResult PreviewRewards(string activityId, string grantMoment, IActivityPlayerState state)
        {
            return ResolveRewards(activityId, grantMoment, state, ActivityResolverUtilities.DefaultRandom(), apply: false);
        }

        public static ActivityRewardResult ApplyRewards(string activityId, string grantMoment)
        {
            return ApplyRewards(activityId, grantMoment, ActivityResolverUtilities.DefaultState());
        }

        public static ActivityRewardResult ApplyRewards(string activityId, string grantMoment, IActivityPlayerState state)
        {
            return ResolveRewards(activityId, grantMoment, state, ActivityResolverUtilities.DefaultRandom(), apply: true);
        }

        public static ActivityRewardResult ApplyRewards(string activityId, string grantMoment, IActivityPlayerState state, IActivityRandom random)
        {
            return ResolveRewards(activityId, grantMoment, state, random, apply: true);
        }

        private static ActivityRewardResult ResolveRewards(string activityId, string grantMoment, IActivityPlayerState state, IActivityRandom random, bool apply)
        {
            random ??= ActivityResolverUtilities.DefaultRandom();
            var issues = new List<ActivityRequirementIssue>();
            var appliedRewards = new List<ActivityAppliedReward>();
            if (!ActivityResolverUtilities.TryGetActivity(activityId, issues, out var activity))
                return Finish(activityId, grantMoment, false, false, issues, appliedRewards);

            var wasCompleted = state.IsActivityCompleted(activityId);
            if (ShouldSkipForCompletion(activity, grantMoment, wasCompleted))
                return Finish(activityId, grantMoment, true, true, issues, appliedRewards);

            foreach (var reward in RuntimeConfigs.Activities.GetRewards(activityId))
            {
                if (reward == null || !ActivityResolverUtilities.MomentMatches(reward.grantMoment, grantMoment))
                    continue;

                if (!ActivityResolverUtilities.ChancePassed(reward.chance, random))
                    continue;

                ApplyOrPreviewReward(reward, state, random, apply, issues, appliedRewards);
            }

            if (apply && IsCompletionMoment(grantMoment))
                state.CompleteActivity(activityId);

            return Finish(activityId, grantMoment, true, false, issues, appliedRewards);
        }

        private static bool ShouldSkipForCompletion(ActivityConfigDto activity, string grantMoment, bool wasCompleted)
        {
            if (!wasCompleted)
                return false;

            if (ActivityResolverUtilities.MomentMatches(grantMoment, "OnFirstComplete"))
                return true;

            return ActivityResolverUtilities.MomentMatches(grantMoment, "OnComplete") && !activity.isRepeatable;
        }

        private static bool IsCompletionMoment(string grantMoment)
        {
            return ActivityResolverUtilities.MomentMatches(grantMoment, "OnComplete") ||
                ActivityResolverUtilities.MomentMatches(grantMoment, "OnFirstComplete");
        }

        private static void ApplyOrPreviewReward(
            ActivityRewardConfigDto reward,
            IActivityPlayerState state,
            IActivityRandom random,
            bool apply,
            List<ActivityRequirementIssue> issues,
            List<ActivityAppliedReward> appliedRewards)
        {
            var type = reward.rewardType ?? string.Empty;
            var targetId = reward.targetId;
            var amount = ActivityResolverUtilities.PositiveAmount(reward.min, reward.max, random);

            if (string.Equals(type, "SkillExp", StringComparison.OrdinalIgnoreCase))
            {
                if (!ActivityResolverUtilities.IsKnownSkill(targetId))
                    AddRewardIssue(issues, reward, true, false, $"Unknown skill id '{targetId}'.");

                appliedRewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, amount = amount, applied = false, isResultOnly = true, message = "SkillExp is result-only until PlayerState stores skills." });
                return;
            }

            if (string.Equals(type, "LootTable", StringComparison.OrdinalIgnoreCase))
            {
                if (!apply)
                {
                    appliedRewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, applied = false, isResultOnly = true, message = "LootTable is not rolled during preview." });
                    return;
                }

                var roll = LootResolver.RollLootTable(targetId, random);
                appliedRewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, applied = roll.success, lootRoll = roll, message = roll.success ? "Rolled loot table." : "Failed to roll loot table." });
                foreach (var issue in roll.issues)
                    ActivityResolverUtilities.AddIssue(issues, reward.activityId, type, targetId, 0, 0, true, false, issue);

                foreach (var drop in roll.drops)
                    appliedRewards.Add(LootResolver.ApplyDrop(drop, state));
                return;
            }

            if (TryResolveCurrencyReward(type, targetId, out var currencyId, out var normalizedType))
            {
                if (!RuntimeConfigs.Items.TryGetCurrency(currencyId, out _))
                {
                    AddRewardIssue(issues, reward, true, false, $"Unknown currency id '{currencyId}'.");
                    return;
                }

                var applied = apply && state.AddCurrency(currencyId, amount);
                appliedRewards.Add(new ActivityAppliedReward { rewardType = normalizedType, targetId = currencyId, amount = amount, applied = applied, isCurrency = true, message = apply ? "Applied currency reward." : "Preview currency reward." });
                return;
            }

            if (string.Equals(type, "Hero", StringComparison.OrdinalIgnoreCase))
            {
                if (!RuntimeConfigs.Heroes.TryGet(targetId, out _))
                {
                    AddRewardIssue(issues, reward, true, false, $"Unknown hero id '{targetId}'.");
                    return;
                }

                var applied = apply && state.AddHero(targetId);
                appliedRewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, amount = 1, applied = applied, message = apply ? "Applied hero reward." : "Preview hero reward." });
                return;
            }

            if (string.Equals(type, "BuildingUnlock", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "UnlockBuilding", StringComparison.OrdinalIgnoreCase))
            {
                if (!RuntimeConfigs.Buildings.TryGet(targetId, out _))
                {
                    AddRewardIssue(issues, reward, true, false, $"Unknown building id '{targetId}'.");
                    return;
                }

                var applied = apply && state.UnlockBuilding(targetId);
                appliedRewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, amount = 1, applied = applied, message = apply ? "Applied building unlock." : "Preview building unlock." });
                return;
            }

            if (string.Equals(type, "MapAccess", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "UnlockLocation", StringComparison.OrdinalIgnoreCase))
            {
                if (!RuntimeConfigs.Map.TryGetLocation(targetId, out _))
                {
                    AddRewardIssue(issues, reward, true, false, $"Unknown location id '{targetId}'.");
                    return;
                }

                var applied = apply && state.UnlockLocation(targetId);
                appliedRewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, amount = 1, applied = applied, message = apply ? "Applied location unlock." : "Preview location unlock." });
                return;
            }

            if (IsItemRewardType(type))
            {
                if (!TryValidateItemReward(type, targetId))
                {
                    AddRewardIssue(issues, reward, true, false, $"Unknown {type} reward target id '{targetId}'.");
                    return;
                }

                var applied = apply && state.AddItem(targetId, amount);
                appliedRewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, amount = amount, applied = applied, message = apply ? "Applied item reward." : "Preview item reward." });
                return;
            }

            AddRewardIssue(issues, reward, true, false, $"Unsupported reward type '{type}'.");
            Debug.LogError($"[ActivityRewardResolver] Unsupported reward type '{type}' for activity '{reward.activityId}'.");
        }

        private static bool TryResolveCurrencyReward(string type, string targetId, out string currencyId, out string normalizedType)
        {
            if (string.Equals(type, "Gold", StringComparison.OrdinalIgnoreCase))
            {
                currencyId = ActivityResolverUtilities.GoldCurrencyId;
                normalizedType = "Gold";
                return true;
            }

            if (string.Equals(type, "Currency", StringComparison.OrdinalIgnoreCase))
            {
                currencyId = targetId;
                normalizedType = "Currency";
                return true;
            }

            currencyId = null;
            normalizedType = null;
            return false;
        }

        private static bool TryValidateItemReward(string type, string targetId)
        {
            if (string.Equals(type, "Resource", StringComparison.OrdinalIgnoreCase))
                return RuntimeConfigs.Items.TryGetResource(targetId, out _);

            if (string.Equals(type, "Equipment", StringComparison.OrdinalIgnoreCase))
                return ActivityResolverUtilities.IsEquipment(targetId);

            if (string.Equals(type, "Consumable", StringComparison.OrdinalIgnoreCase))
                return RuntimeConfigs.Items.TryGetConsumable(targetId, out _);

            if (string.Equals(type, "Recipe", StringComparison.OrdinalIgnoreCase))
                return RuntimeConfigs.Items.TryGetRecipe(targetId, out _);

            if (string.Equals(type, "Item", StringComparison.OrdinalIgnoreCase))
                return RuntimeConfigs.Items.TryGet(targetId, out _);

            return false;
        }

        private static bool IsItemRewardType(string type)
        {
            return string.Equals(type, "Resource", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Equipment", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Consumable", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Recipe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Item", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddRewardIssue(List<ActivityRequirementIssue> issues, ActivityRewardConfigDto reward, bool isError, bool isNotImplemented, string message)
        {
            ActivityResolverUtilities.AddIssue(issues, reward.activityId, reward.rewardType, reward.targetId, 0, 0, isError, isNotImplemented, message);
            if (isError)
                Debug.LogError($"[ActivityRewardResolver] {message}");
        }

        private static ActivityRewardResult Finish(
            string activityId,
            string grantMoment,
            bool success,
            bool skippedDuplicate,
            List<ActivityRequirementIssue> issues,
            List<ActivityAppliedReward> rewards)
        {
            foreach (var issue in issues)
            {
                if (issue.isError)
                {
                    success = false;
                    break;
                }
            }

            return new ActivityRewardResult
            {
                activityId = activityId,
                grantMoment = grantMoment,
                success = success,
                skippedDuplicate = skippedDuplicate,
                issues = issues.ToArray(),
                rewards = rewards.ToArray()
            };
        }
    }
}
