using System;
using System.Collections.Generic;

namespace GuildIdle.Activities
{
    public static class ActivityResolver
    {
        public static ActivityCheckResult CanStart(ActivityExecutionContext context, IActivityPlayerState state)
        {
            var issues = new List<ActivityRequirementIssue>();
            ActivityResolverUtilities.TryGetActivity(context?.activityId, issues, out var activity);
            if (activity != null && ActivityResolverUtilities.ValidateExecutionContext(context, state, issues))
                issues.AddRange(ActivityRequirementResolver.GetMissingRequirements(context, state));

            var canStart = issues.Count == 0;

            return new ActivityCheckResult
            {
                activityId = context?.activityId,
                context = context,
                activity = activity,
                canStart = canStart,
                issues = issues.ToArray()
            };
        }

        public static ActivityRequirementIssue[] GetMissingRequirements(ActivityExecutionContext context, IActivityPlayerState state)
        {
            return ActivityRequirementResolver.GetMissingRequirements(context, state);
        }

        public static ActivityCostResult CanPayCost(ActivityExecutionContext context, IActivityPlayerState state)
        {
            return ActivityCostResolver.CanPayCost(context, state);
        }

        public static ActivityCostResult ApplyCost(ActivityExecutionContext context, IActivityPlayerState state)
        {
            return ActivityCostResolver.ApplyCost(context, state);
        }

        public static ActivityRewardResult PreviewRewards(ActivityExecutionContext context, string grantMoment, IActivityPlayerState state)
        {
            return ActivityRewardResolver.PreviewRewards(context, grantMoment, state);
        }

        public static ActivityRewardResult ApplyRewards(ActivityExecutionContext context, string grantMoment, IActivityPlayerState state, IActivityRandom random, bool markCompletion)
        {
            return ActivityRewardResolver.ApplyRewards(context, grantMoment, state, random, markCompletion);
        }
    }
}
