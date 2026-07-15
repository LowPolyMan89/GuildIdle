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
            return ResolveRewards(context?.activityId, context, grantMoment, state, ActivityResolverUtilities.DefaultRandom(), false, false);
        }

        public static ActivityRewardResult ApplyRewards(ActivityExecutionContext context, string grantMoment, IActivityPlayerState state, IActivityRandom random)
        {
            return ResolveRewards(context?.activityId, context, grantMoment, state, random, true, true);
        }

        public static ActivityRewardResult ApplyRewards(ActivityExecutionContext context, string grantMoment, IActivityPlayerState state, IActivityRandom random, bool markCompletion)
        {
            return ResolveRewards(context?.activityId, context, grantMoment, state, random, true, markCompletion);
        }

        private static ActivityRewardResult ResolveRewards(
            string activityId,
            ActivityExecutionContext context,
            string grantMoment,
            IActivityPlayerState state,
            IActivityRandom random,
            bool apply,
            bool markCompletion)
        {
            var issues = new List<ActivityRequirementIssue>();
            if (!ActivityResolverUtilities.TryGetActivity(activityId, issues, out var activity))
                return Finish(activityId, grantMoment, false, false, issues, Array.Empty<ActivityAppliedReward>());
            if ((context != null || apply) && !ActivityResolverUtilities.ValidateExecutionContext(context, state, issues))
                return Finish(activityId, grantMoment, false, false, issues, Array.Empty<ActivityAppliedReward>());

            var wasCompleted = state.IsActivityCompleted(activityId);
            if (ShouldSkipForCompletion(activity, grantMoment, wasCompleted))
                return Finish(activityId, grantMoment, true, true, issues, Array.Empty<ActivityAppliedReward>());

            var definitions = new List<RewardDefinition>();
            foreach (var reward in RuntimeConfigs.Activities.GetRewards(activityId))
            {
                if (reward == null)
                    continue;
                definitions.Add(new RewardDefinition
                {
                    sourceId = activityId,
                    rewardType = reward.rewardType,
                    targetId = reward.targetId,
                    min = reward.min,
                    max = reward.max,
                    chance = reward.chance,
                    grantMoment = reward.grantMoment
                });
            }

            var prepared = RewardBatchPipeline.Prepare(definitions, grantMoment, context?.heroId, random, apply);
            issues.AddRange(prepared.issues);
            foreach (var issue in prepared.issues)
            {
                if (!issue.isError)
                    continue;
                if (ActivityTypeParser.TryParseRewardType(issue.issueType, out var issueType) &&
                    issueType == RewardTypeEnum.LootTable)
                {
                    continue;
                }
                Debug.LogError($"[ActivityRewardResolver] {issue.message}");
                if (issue.message.StartsWith("Unsupported reward type", StringComparison.Ordinal))
                    Debug.LogError($"[ActivityRewardResolver] {issue.message.TrimEnd('.')} for activity '{activityId}'.");
            }
            if (!prepared.success)
                return Finish(activityId, grantMoment, false, false, issues, prepared.rewards);

            if (apply)
            {
                if (!state.TryApplyRewardBatch(prepared.mutations, out var mutationResults, out var error))
                {
                    ActivityResolverUtilities.AddIssue(issues, activityId, "RewardBatch", activityId, 0, 0, true, false, error ?? "Failed to apply reward batch.");
                    return Finish(activityId, grantMoment, false, false, issues, prepared.rewards);
                }
                prepared.ApplyResults(mutationResults);
            }

            if (apply && markCompletion && IsCompletionMoment(grantMoment))
                state.CompleteActivity(activityId);
            return Finish(activityId, grantMoment, true, false, issues, prepared.rewards);
        }

        private static bool ShouldSkipForCompletion(ActivityConfigDto activity, string grantMoment, bool wasCompleted)
        {
            if (!wasCompleted)
                return false;
            if (ActivityResolverUtilities.MomentMatches(grantMoment, GrantMoment.OnFirstComplete))
                return true;
            return ActivityResolverUtilities.MomentMatches(grantMoment, GrantMoment.OnComplete) && !activity.isRepeatable;
        }

        private static bool IsCompletionMoment(string grantMoment) =>
            ActivityResolverUtilities.MomentMatches(grantMoment, GrantMoment.OnComplete) ||
            ActivityResolverUtilities.MomentMatches(grantMoment, GrantMoment.OnFirstComplete);

        private static ActivityRewardResult Finish(
            string activityId,
            string grantMoment,
            bool success,
            bool skippedDuplicate,
            List<ActivityRequirementIssue> issues,
            ActivityAppliedReward[] rewards)
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
                rewards = rewards ?? Array.Empty<ActivityAppliedReward>()
            };
        }
    }
}
