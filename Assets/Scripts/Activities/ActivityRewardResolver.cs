using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Core;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Activities
{
    public static class ActivityRewardResolver
    {
        public static ActivityRewardResult PreviewRewards(ActivityExecutionContext context, string grantMoment, IActivityPlayerState state)
        {
            return ResolveRewards(context?.activityId, context, grantMoment, state, ActivityResolverUtilities.DefaultRandom(), apply: false, markCompletion: false);
        }

        public static ActivityRewardResult ApplyRewards(ActivityExecutionContext context, string grantMoment, IActivityPlayerState state, IActivityRandom random)
        {
            return ResolveRewards(context?.activityId, context, grantMoment, state, random, apply: true, markCompletion: true);
        }

        public static ActivityRewardResult ApplyRewards(ActivityExecutionContext context, string grantMoment, IActivityPlayerState state, IActivityRandom random, bool markCompletion)
        {
            return ResolveRewards(context?.activityId, context, grantMoment, state, random, apply: true, markCompletion: markCompletion);
        }

        private static ActivityRewardResult ResolveRewards(string activityId, ActivityExecutionContext context, string grantMoment, IActivityPlayerState state, IActivityRandom random, bool apply, bool markCompletion)
        {
            random ??= ActivityResolverUtilities.DefaultRandom();
            var issues = new List<ActivityRequirementIssue>();
            var appliedRewards = new List<ActivityAppliedReward>();
            if (!ActivityResolverUtilities.TryGetActivity(activityId, issues, out var activity))
                return Finish(activityId, grantMoment, false, false, issues, appliedRewards);

            if (context != null || apply)
            {
                if (!ActivityResolverUtilities.ValidateExecutionContext(context, state, issues))
                    return Finish(activityId, grantMoment, false, false, issues, appliedRewards);
            }

            var wasCompleted = state.IsActivityCompleted(activityId);
            if (ShouldSkipForCompletion(activity, grantMoment, wasCompleted))
                return Finish(activityId, grantMoment, true, true, issues, appliedRewards);

            foreach (var reward in RuntimeConfigs.Activities.GetRewards(activityId))
            {
                if (reward == null || !ActivityResolverUtilities.MomentMatches(reward.grantMoment, grantMoment))
                    continue;

                if (!ActivityResolverUtilities.ChancePassed(reward.chance, random))
                    continue;

                ApplyOrPreviewReward(reward, context, state, random, apply, issues, appliedRewards);
            }

            if (apply && markCompletion && IsCompletionMoment(grantMoment))
                state.CompleteActivity(activityId);

            return Finish(activityId, grantMoment, true, false, issues, appliedRewards);
        }

        private static bool ShouldSkipForCompletion(ActivityConfigDto activity, string grantMoment, bool wasCompleted)
        {
            if (!wasCompleted)
                return false;

            if (ActivityResolverUtilities.MomentMatches(grantMoment, GrantMoment.OnFirstComplete))
                return true;

            return ActivityResolverUtilities.MomentMatches(grantMoment, GrantMoment.OnComplete) && !activity.isRepeatable;
        }

        private static bool IsCompletionMoment(string grantMoment)
        {
            return ActivityResolverUtilities.MomentMatches(grantMoment, GrantMoment.OnComplete) ||
                ActivityResolverUtilities.MomentMatches(grantMoment, GrantMoment.OnFirstComplete);
        }

        private static void ApplyOrPreviewReward(
            ActivityRewardConfigDto reward,
            ActivityExecutionContext context,
            IActivityPlayerState state,
            IActivityRandom random,
            bool apply,
            List<ActivityRequirementIssue> issues,
            List<ActivityAppliedReward> appliedRewards)
        {
            var type = reward.rewardType ?? string.Empty;
            var targetId = reward.targetId;
            var amount = ActivityResolverUtilities.PositiveAmount(reward.min, reward.max, random);

            if (!RewardType.TryParse(type, out var parsedType))
            {
                UnsupportedReward(issues, reward, type);
                return;
            }

            if (parsedType == RewardTypeEnum.SkillExp)
            {
                if (!ActivityResolverUtilities.IsKnownSkill(targetId))
                {
                    AddRewardIssue(issues, reward, true, false, $"Unknown skill id '{targetId}'.");
                    return;
                }

                if (context == null || string.IsNullOrWhiteSpace(context.heroId))
                {
                    AddRewardIssue(issues, reward, apply, false, "SkillExp reward needs an executor hero context.");
                    appliedRewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, ownerType = ActivityResolverUtilities.OwnerHero, amount = amount, applied = false, message = "Preview hero skill exp reward." });
                    return;
                }

                var applied = apply && state.AddHeroSkillExp(context.heroId, targetId, amount);
                appliedRewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, ownerType = ActivityResolverUtilities.OwnerHero, ownerId = context.heroId, amount = amount, applied = applied, message = apply ? "Applied hero skill exp reward." : "Preview hero skill exp reward." });
                return;
            }

            if (parsedType == RewardTypeEnum.LootTable)
            {
                if (!apply)
                {
                    appliedRewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, ownerType = ActivityResolverUtilities.OwnerProfile, applied = false, isResultOnly = true, message = "LootTable is not rolled during preview." });
                    return;
                }

                var roll = LootResolver.RollLootTable(targetId, random);
                appliedRewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, ownerType = ActivityResolverUtilities.OwnerProfile, applied = roll.success, lootRoll = roll, message = roll.success ? "Rolled loot table." : "Failed to roll loot table." });
                foreach (var issue in roll.issues)
                    ActivityResolverUtilities.AddIssue(issues, reward.activityId, type, targetId, 0, 0, true, false, issue);

                foreach (var drop in roll.drops)
                    appliedRewards.Add(LootResolver.ApplyDrop(drop, state));
                return;
            }

            if (TryResolveCurrencyReward(parsedType, targetId, out var currencyId, out var normalizedType))
            {
                if (!RuntimeConfigs.Items.TryGetCurrency(currencyId, out _))
                {
                    AddRewardIssue(issues, reward, true, false, $"Unknown currency id '{currencyId}'.");
                    return;
                }

                var applied = apply && state.AddCurrency(currencyId, amount);
                appliedRewards.Add(new ActivityAppliedReward { rewardType = normalizedType, targetId = currencyId, ownerType = ActivityResolverUtilities.OwnerProfile, amount = amount, applied = applied, isCurrency = true, message = apply ? "Applied currency reward." : "Preview currency reward." });
                return;
            }

            if (parsedType == RewardTypeEnum.Hero)
            {
                if (!RuntimeConfigs.Heroes.TryGet(targetId, out _))
                {
                    AddRewardIssue(issues, reward, true, false, $"Unknown hero id '{targetId}'.");
                    return;
                }

                var applied = apply && state.AddHero(targetId);
                appliedRewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, ownerType = ActivityResolverUtilities.OwnerProfile, amount = 1, applied = applied, message = apply ? "Applied hero reward." : "Preview hero reward." });
                return;
            }

            if (IsBuildingUnlock(parsedType))
            {
                if (!RuntimeConfigs.Buildings.TryGet(targetId, out _))
                {
                    AddRewardIssue(issues, reward, true, false, $"Unknown building id '{targetId}'.");
                    return;
                }

                var applied = apply && state.UnlockBuilding(targetId);
                appliedRewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, ownerType = ActivityResolverUtilities.OwnerProfile, amount = 1, applied = applied, message = apply ? "Applied building unlock." : "Preview building unlock." });
                return;
            }

            if (IsLocationUnlock(parsedType))
            {
                if (!RuntimeConfigs.Map.TryGetLocation(targetId, out _))
                {
                    AddRewardIssue(issues, reward, true, false, $"Unknown location id '{targetId}'.");
                    return;
                }

                var applied = apply && state.UnlockLocation(targetId);
                appliedRewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, ownerType = ActivityResolverUtilities.OwnerProfile, amount = 1, applied = applied, message = apply ? "Applied location unlock." : "Preview location unlock." });
                return;
            }

            if (IsItemRewardType(parsedType))
            {
                if (!TryValidateItemReward(parsedType, targetId))
                {
                    AddRewardIssue(issues, reward, true, false, $"Unknown {type} reward target id '{targetId}'.");
                    return;
                }

                var applied = apply && state.AddItem(targetId, amount);
                appliedRewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, ownerType = ActivityResolverUtilities.OwnerProfile, amount = amount, applied = applied, message = apply ? "Applied item reward." : "Preview item reward." });
                return;
            }

            if (parsedType == RewardTypeEnum.HeroExp || parsedType == RewardTypeEnum.Reputation)
            {
                AddRewardIssue(issues, reward, true, true, $"Reward type '{type}' is recognized but not implemented in PlayerState yet.");
                return;
            }

            UnsupportedReward(issues, reward, type);
        }

        private static bool TryResolveCurrencyReward(RewardTypeEnum type, string targetId, out string currencyId, out string normalizedType)
        {
            if (type == RewardTypeEnum.Gold)
            {
                currencyId = ActivityResolverUtilities.GoldCurrencyId;
                normalizedType = RewardType.Gold;
                return true;
            }

            if (type == RewardTypeEnum.Currency)
            {
                currencyId = targetId;
                normalizedType = RewardType.Currency;
                return true;
            }

            currencyId = null;
            normalizedType = null;
            return false;
        }

        private static bool TryValidateItemReward(RewardTypeEnum type, string targetId)
        {
            switch (type)
            {
                case RewardTypeEnum.Resource:
                    return RuntimeConfigs.Items.TryGetResource(targetId, out _);
                case RewardTypeEnum.Equipment:
                    return ActivityResolverUtilities.IsEquipment(targetId);
                case RewardTypeEnum.Consumable:
                    return RuntimeConfigs.Items.TryGetConsumable(targetId, out _);
                case RewardTypeEnum.Recipe:
                    return RuntimeConfigs.Items.TryGetRecipe(targetId, out _);
                case RewardTypeEnum.Item:
                    return RuntimeConfigs.Items.TryGet(targetId, out _);
                default:
                    return false;
            }
        }

        private static bool IsItemRewardType(RewardTypeEnum type)
        {
            return type == RewardTypeEnum.Resource ||
                type == RewardTypeEnum.Equipment ||
                type == RewardTypeEnum.Consumable ||
                type == RewardTypeEnum.Recipe ||
                type == RewardTypeEnum.Item;
        }

        private static bool IsBuildingUnlock(RewardTypeEnum type)
        {
            return type == RewardTypeEnum.UnlockBuilding ||
                type == RewardTypeEnum.BuildingUnlock ||
                type == RewardTypeEnum.Building;
        }

        private static bool IsLocationUnlock(RewardTypeEnum type)
        {
            return type == RewardTypeEnum.UnlockLocation ||
                type == RewardTypeEnum.MapAccess ||
                type == RewardTypeEnum.Location;
        }

        private static void UnsupportedReward(List<ActivityRequirementIssue> issues, ActivityRewardConfigDto reward, string type)
        {
            AddRewardIssue(issues, reward, true, false, $"Unsupported reward type '{type}'.");
            Debug.LogError($"[ActivityRewardResolver] Unsupported reward type '{type}' for activity '{reward.activityId}'.");
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
