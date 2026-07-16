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
        public string heroId;
        public ActivityRuntimeStatus status;
        public float elapsedSeconds;
        public int completedCycles;
        public int plannedCycles;
        public bool materialsPaid;
        public float accumulatedBuildPoints;
        public string pendingResultId;
        public long startedAtUnixSeconds;
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
