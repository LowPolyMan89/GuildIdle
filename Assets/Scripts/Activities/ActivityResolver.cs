using System;
using System.Collections.Generic;

namespace GuildIdle.Activities
{
    public static class ActivityResolver
    {
        public static ActivityCheckResult CanStart(string activityId)
        {
            return CanStart(activityId, ActivityResolverUtilities.DefaultState());
        }

        public static ActivityCheckResult CanStart(string activityId, IActivityPlayerState state)
        {
            var issues = new List<ActivityRequirementIssue>();
            ActivityResolverUtilities.TryGetActivity(activityId, issues, out var activity);
            if (activity != null)
                issues.AddRange(ActivityRequirementResolver.GetMissingRequirements(activityId, state));

            var canStart = issues.Count == 0;

            return new ActivityCheckResult
            {
                activityId = activityId,
                activity = activity,
                canStart = canStart,
                issues = issues.ToArray()
            };
        }

        public static ActivityCheckResult CanStart(ActivityExecutionContext context)
        {
            return CanStart(context, ActivityResolverUtilities.DefaultState());
        }

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

        public static ActivityRequirementIssue[] GetMissingRequirements(string activityId)
        {
            return ActivityRequirementResolver.GetMissingRequirements(activityId);
        }

        public static ActivityRequirementIssue[] GetMissingRequirements(string activityId, IActivityPlayerState state)
        {
            return ActivityRequirementResolver.GetMissingRequirements(activityId, state);
        }

        public static ActivityRequirementIssue[] GetMissingRequirements(ActivityExecutionContext context)
        {
            return ActivityRequirementResolver.GetMissingRequirements(context);
        }

        public static ActivityRequirementIssue[] GetMissingRequirements(ActivityExecutionContext context, IActivityPlayerState state)
        {
            return ActivityRequirementResolver.GetMissingRequirements(context, state);
        }

        public static ActivityCostResult CanPayCost(string activityId)
        {
            return ActivityCostResolver.CanPayCost(activityId);
        }

        public static ActivityCostResult CanPayCost(string activityId, IActivityPlayerState state)
        {
            return ActivityCostResolver.CanPayCost(activityId, state);
        }

        public static ActivityCostResult CanPayCost(ActivityExecutionContext context)
        {
            return ActivityCostResolver.CanPayCost(context);
        }

        public static ActivityCostResult CanPayCost(ActivityExecutionContext context, IActivityPlayerState state)
        {
            return ActivityCostResolver.CanPayCost(context, state);
        }

        [Obsolete("Use ApplyCost(ActivityExecutionContext) so hero-bound costs have an executor context.")]
        public static ActivityCostResult ApplyCost(string activityId)
        {
            return ActivityCostResolver.ApplyCost(activityId);
        }

        [Obsolete("Use ApplyCost(ActivityExecutionContext, IActivityPlayerState) so hero-bound costs have an executor context.")]
        public static ActivityCostResult ApplyCost(string activityId, IActivityPlayerState state)
        {
            return ActivityCostResolver.ApplyCost(activityId, state);
        }

        public static ActivityCostResult ApplyCost(ActivityExecutionContext context)
        {
            return ActivityCostResolver.ApplyCost(context);
        }

        public static ActivityCostResult ApplyCost(ActivityExecutionContext context, IActivityPlayerState state)
        {
            return ActivityCostResolver.ApplyCost(context, state);
        }

        public static ActivityRewardResult PreviewRewards(string activityId, string grantMoment)
        {
            return ActivityRewardResolver.PreviewRewards(activityId, grantMoment);
        }

        public static ActivityRewardResult PreviewRewards(string activityId, string grantMoment, IActivityPlayerState state)
        {
            return ActivityRewardResolver.PreviewRewards(activityId, grantMoment, state);
        }

        public static ActivityRewardResult PreviewRewards(ActivityExecutionContext context, string grantMoment)
        {
            return ActivityRewardResolver.PreviewRewards(context, grantMoment);
        }

        public static ActivityRewardResult PreviewRewards(ActivityExecutionContext context, string grantMoment, IActivityPlayerState state)
        {
            return ActivityRewardResolver.PreviewRewards(context, grantMoment, state);
        }

        [Obsolete("Use ApplyRewards(ActivityExecutionContext, string) so hero-bound rewards have an executor context.")]
        public static ActivityRewardResult ApplyRewards(string activityId, string grantMoment)
        {
            return ActivityRewardResolver.ApplyRewards(activityId, grantMoment);
        }

        [Obsolete("Use ApplyRewards(ActivityExecutionContext, string, IActivityPlayerState) so hero-bound rewards have an executor context.")]
        public static ActivityRewardResult ApplyRewards(string activityId, string grantMoment, IActivityPlayerState state)
        {
            return ActivityRewardResolver.ApplyRewards(activityId, grantMoment, state);
        }

        public static ActivityRewardResult ApplyRewards(ActivityExecutionContext context, string grantMoment)
        {
            return ActivityRewardResolver.ApplyRewards(context, grantMoment);
        }

        public static ActivityRewardResult ApplyRewards(ActivityExecutionContext context, string grantMoment, IActivityPlayerState state)
        {
            return ActivityRewardResolver.ApplyRewards(context, grantMoment, state);
        }
    }
}
