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

        public static ActivityRequirementIssue[] GetMissingRequirements(string activityId)
        {
            return ActivityRequirementResolver.GetMissingRequirements(activityId);
        }

        public static ActivityRequirementIssue[] GetMissingRequirements(string activityId, IActivityPlayerState state)
        {
            return ActivityRequirementResolver.GetMissingRequirements(activityId, state);
        }

        public static ActivityCostResult CanPayCost(string activityId)
        {
            return ActivityCostResolver.CanPayCost(activityId);
        }

        public static ActivityCostResult CanPayCost(string activityId, IActivityPlayerState state)
        {
            return ActivityCostResolver.CanPayCost(activityId, state);
        }

        public static ActivityCostResult ApplyCost(string activityId)
        {
            return ActivityCostResolver.ApplyCost(activityId);
        }

        public static ActivityCostResult ApplyCost(string activityId, IActivityPlayerState state)
        {
            return ActivityCostResolver.ApplyCost(activityId, state);
        }

        public static ActivityRewardResult PreviewRewards(string activityId, string grantMoment)
        {
            return ActivityRewardResolver.PreviewRewards(activityId, grantMoment);
        }

        public static ActivityRewardResult PreviewRewards(string activityId, string grantMoment, IActivityPlayerState state)
        {
            return ActivityRewardResolver.PreviewRewards(activityId, grantMoment, state);
        }

        public static ActivityRewardResult ApplyRewards(string activityId, string grantMoment)
        {
            return ActivityRewardResolver.ApplyRewards(activityId, grantMoment);
        }

        public static ActivityRewardResult ApplyRewards(string activityId, string grantMoment, IActivityPlayerState state)
        {
            return ActivityRewardResolver.ApplyRewards(activityId, grantMoment, state);
        }
    }
}
