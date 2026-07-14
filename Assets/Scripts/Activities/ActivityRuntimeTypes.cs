using System;
using GuildIdle.Configs;
using GuildIdle.Core;

namespace GuildIdle.Activities
{
    // Re-exported from GuildIdle.Core to break the Player → Activities dependency cycle.
    // Runtime code should use GuildIdle.Core.ActivityRuntimeStatus directly.
    public enum ActivityRuntimeStatus
    {
        None = GuildIdle.Core.ActivityRuntimeStatus.None,
        Running = GuildIdle.Core.ActivityRuntimeStatus.Running,
        Completed = GuildIdle.Core.ActivityRuntimeStatus.Completed,
        Cancelled = GuildIdle.Core.ActivityRuntimeStatus.Cancelled
    }

    public sealed class ActivityRuntimeInfo
    {
        public string activityId;
        public string activityType;
        public int durationSeconds;
        public bool isRepeatable;
        public bool requiresHero = true;
        public ActivityConfigDto activity;
    }

    public sealed class ActivityExecutionSnapshot
    {
        public string executionId;
        public string activityId;
        public string heroId;
        public ActivityRuntimeStatus status;
        public float elapsedSeconds;
        public float durationSeconds;
        public float progress;
        public float remainingSeconds;
        public int completedCycles;
        public long startedAtUnixSeconds;
    }

    public sealed class HeroActivityState
    {
        public string heroId;
        public bool isBusy;
        public string currentActivityExecutionId;
    }

    public sealed class ActivityRuntimeSnapshot
    {
        public ActivityExecutionSnapshot[] executions = Array.Empty<ActivityExecutionSnapshot>();
    }

    public sealed class ActivityStartResult
    {
        public bool success;
        public string executionId;
        public ActivityExecutionContext context;
        public ActivityCheckResult startCheck;
        public ActivityCostResult costCheck;
        public ActivityCostResult appliedCost;
        public ActivityRequirementIssue[] issues = Array.Empty<ActivityRequirementIssue>();
        public ActivityRuntimeSnapshot snapshot;
    }

    public sealed class ActivityTickResult
    {
        public bool success;
        public float deltaTime;
        public int processedExecutions;
        public int processedCycles;
        public bool saved;
        public bool cycleLimitReached;
        public ActivityRequirementIssue[] issues = Array.Empty<ActivityRequirementIssue>();
        public ActivityRewardResult[] rewardResults = Array.Empty<ActivityRewardResult>();
        public ActivityRuntimeSnapshot snapshot;
    }

    public sealed class ActivityCompleteResult
    {
        public bool success;
        public string executionId;
        public bool saved;
        public ActivityRequirementIssue[] issues = Array.Empty<ActivityRequirementIssue>();
        public ActivityRewardResult[] rewardResults = Array.Empty<ActivityRewardResult>();
        public ActivityRuntimeSnapshot snapshot;
    }

    public sealed class ActivityCancelResult
    {
        public bool success;
        public string executionId;
        public bool saved;
        public ActivityRequirementIssue[] issues = Array.Empty<ActivityRequirementIssue>();
        public ActivityRuntimeSnapshot snapshot;
    }

}
