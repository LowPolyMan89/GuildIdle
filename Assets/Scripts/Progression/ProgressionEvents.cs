using System;

namespace GuildIdle.Progression
{
    public enum ProgressionEventKind
    {
        NewGame,
        ResourceQuantityChanged,
        ItemQuantityChanged,
        BuildingLevelChanged,
        ActivityCompleted,
        ActivityFailed,
        StageEntered
    }

    public abstract class ProgressionEvent
    {
        protected ProgressionEvent(ProgressionEventKind kind)
        {
            Kind = kind;
        }

        public ProgressionEventKind Kind { get; }
    }

    public class NewGame : ProgressionEvent
    {
        public NewGame() : base(ProgressionEventKind.NewGame) { }
    }

    public sealed class NewGameProgressionEvent : NewGame { }

    public abstract class TargetValueProgressionEvent : ProgressionEvent
    {
        protected TargetValueProgressionEvent(ProgressionEventKind kind, string targetId, int currentValue)
            : base(kind)
        {
            TargetId = string.IsNullOrWhiteSpace(targetId)
                ? throw new ArgumentException("Progression event target id is required.", nameof(targetId))
                : targetId;
            CurrentValue = currentValue >= 0
                ? currentValue
                : throw new ArgumentOutOfRangeException(nameof(currentValue));
        }

        public string TargetId { get; }
        public int CurrentValue { get; }
    }

    public class ResourceQuantityChanged : TargetValueProgressionEvent
    {
        public ResourceQuantityChanged(string resourceId, int currentQuantity)
            : base(ProgressionEventKind.ResourceQuantityChanged, resourceId, currentQuantity) { }
    }

    public sealed class ResourceQuantityChangedEvent : ResourceQuantityChanged
    {
        public ResourceQuantityChangedEvent(string resourceId, int currentQuantity)
            : base(resourceId, currentQuantity) { }
    }

    public class ItemQuantityChanged : TargetValueProgressionEvent
    {
        public ItemQuantityChanged(string itemId, int currentQuantity)
            : base(ProgressionEventKind.ItemQuantityChanged, itemId, currentQuantity) { }
    }

    public sealed class ItemQuantityChangedEvent : ItemQuantityChanged
    {
        public ItemQuantityChangedEvent(string itemId, int currentQuantity)
            : base(itemId, currentQuantity) { }
    }

    public class BuildingLevelChanged : TargetValueProgressionEvent
    {
        public BuildingLevelChanged(string buildingId, int currentLevel)
            : base(ProgressionEventKind.BuildingLevelChanged, buildingId, currentLevel) { }
    }

    public sealed class BuildingLevelChangedEvent : BuildingLevelChanged
    {
        public BuildingLevelChangedEvent(string buildingId, int currentLevel)
            : base(buildingId, currentLevel) { }
    }

    public class ActivityCompleted : TargetValueProgressionEvent
    {
        public ActivityCompleted(string activityId, int completionCount = 1)
            : base(ProgressionEventKind.ActivityCompleted, activityId, completionCount) { }
    }

    public sealed class ActivityCompletedEvent : ActivityCompleted
    {
        public ActivityCompletedEvent(string activityId, int completionCount = 1)
            : base(activityId, completionCount) { }
    }

    public class ActivityFailed : TargetValueProgressionEvent
    {
        public ActivityFailed(string activityId, int failureCount = 1)
            : base(ProgressionEventKind.ActivityFailed, activityId, failureCount) { }
    }

    public sealed class ActivityFailedEvent : ActivityFailed
    {
        public ActivityFailedEvent(string activityId, int failureCount = 1)
            : base(activityId, failureCount) { }
    }

    public class StageEntered : TargetValueProgressionEvent
    {
        public StageEntered(string stageId)
            : base(ProgressionEventKind.StageEntered, stageId, 1) { }
    }

    public sealed class StageEnteredEvent : StageEntered
    {
        public StageEnteredEvent(string stageId) : base(stageId) { }
    }
}
