using System;

namespace GuildIdle.Core
{
    public enum ActivityRuntimeStatus
    {
        None = 0,
        Running = 1,
        Completed = 2,
        Cancelled = 3,
        Paused = 4,
        ResultPending = 5
    }

    [Serializable]
    public sealed class ActivityRuntimeSaveData
    {
        public ActivityExecutionSaveData[] executions = Array.Empty<ActivityExecutionSaveData>();
    }

    [Serializable]
    public sealed class ActivityExecutionSaveData
    {
        public string executionId;
        public string activityId;
        public string runtimeKind;
        public string heroId;
        public ActivityRuntimeStatus status;
        public float elapsedSeconds;
        public int completedCycles;
        public int plannedCycles;
        public bool currentCycleFatiguePaid;
        public string cyclePhase;
        public ActivityStagedRewardSaveData[] stagedRewards = Array.Empty<ActivityStagedRewardSaveData>();
        public string endReason;
        public bool dangerRollCompleted;
        public float dangerRiskPercent;
        public int dangerRoll;
        public bool activityBagResolved;
        public bool materialsPaid;
        public float accumulatedBuildPoints;
        public bool buildingLevelApplied;
        public bool buildingEventPublished;
        public LinkedCombatStartRequestSaveData linkedCombat;
        public string pendingResultId;
        public long startedAtUnixSeconds;
    }

    [Serializable]
    public sealed class ActivityStagedRewardSaveData
    {
        public string rewardType;
        public string targetId;
        public long quantity;
        public string origin;
        public int quality;
        public string instanceId;
    }

    [Serializable]
    public sealed class LinkedCombatStartRequestSaveData
    {
        public string requestId;
        public string rootExecutionId;
        public string occupationOwnerId;
        public string heroId;
        public string dangerEncounterId;
        public string enemyGroupId;
        public string combatMode;
        public string defeatLossRule;
        public bool suppressFatigueCost;
        public string combatExecutionId;
        public bool resolved;
        public ActivityStagedRewardSaveData[] loot = Array.Empty<ActivityStagedRewardSaveData>();
    }

    public interface IActivityRuntimeStore
    {
        ActivityExecutionSaveData[] GetActivityExecutions();
        ActivityExecutionSaveData GetActivityExecution(string executionId);
        bool AddActivityExecution(ActivityExecutionSaveData execution);
        bool UpdateActivityExecution(ActivityExecutionSaveData execution);
        bool RemoveActivityExecution(string executionId);
        bool Save();
    }
}
