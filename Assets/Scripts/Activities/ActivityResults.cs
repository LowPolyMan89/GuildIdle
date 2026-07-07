using System;
using GuildIdle.Configs;

namespace GuildIdle.Activities
{
    public sealed class ActivityExecutionContext
    {
        public string activityId;
        public string heroId;
        public int heroSlotIndex;
        public string executionId;
    }

    public sealed class ActivityCheckResult
    {
        public string activityId;
        public ActivityExecutionContext context;
        public ActivityConfigDto activity;
        public bool canStart;
        public ActivityRequirementIssue[] issues = Array.Empty<ActivityRequirementIssue>();
    }

    public sealed class ActivityRequirementIssue
    {
        public string activityId;
        public string issueType;
        public string targetId;
        public int requiredAmount;
        public long currentAmount;
        public bool isError;
        public bool isNotImplemented;
        public string message;
    }

    public sealed class ActivityCostResult
    {
        public string activityId;
        public bool success;
        public ActivityAppliedCost[] costs = Array.Empty<ActivityAppliedCost>();
        public ActivityRequirementIssue[] issues = Array.Empty<ActivityRequirementIssue>();
    }

    public sealed class ActivityAppliedCost
    {
        public string costType;
        public string targetId;
        public string ownerType;
        public string ownerId;
        public int amount;
        public bool applied;
        public string message;
    }

    public sealed class ActivityRewardResult
    {
        public string activityId;
        public string grantMoment;
        public bool success;
        public bool skippedDuplicate;
        public ActivityAppliedReward[] rewards = Array.Empty<ActivityAppliedReward>();
        public ActivityRequirementIssue[] issues = Array.Empty<ActivityRequirementIssue>();
    }

    public sealed class ActivityAppliedReward
    {
        public string rewardType;
        public string targetId;
        public string ownerType;
        public string ownerId;
        public int amount;
        public bool applied;
        public bool isCurrency;
        public bool isResultOnly;
        public string message;
        public LootRollResult lootRoll;
    }

    public sealed class LootRollResult
    {
        public string lootTableId;
        public string lootGroupId;
        public bool success;
        public LootDropResult[] drops = Array.Empty<LootDropResult>();
        public string[] issues = Array.Empty<string>();
    }

    public sealed class LootDropResult
    {
        public string dropType;
        public string targetId;
        public int amount;
        public bool isCurrency;
        public bool granted;
        public string message;
    }
}
