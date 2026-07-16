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
        QuestCompleted,
        StageEntered
    }

    public abstract class ProgressionEvent
    {
        protected ProgressionEvent(ProgressionEventKind kind) { Kind = kind; }
        public ProgressionEventKind Kind { get; }
    }

    public sealed class NewGame : ProgressionEvent
    {
        public NewGame() : base(ProgressionEventKind.NewGame) { }
    }

    public abstract class TargetValueProgressionEvent : ProgressionEvent
    {
        protected TargetValueProgressionEvent(ProgressionEventKind kind, string targetId, int currentValue) : base(kind)
        {
            TargetId = string.IsNullOrWhiteSpace(targetId) ? throw new ArgumentException("Progression event target id is required.", nameof(targetId)) : targetId;
            CurrentValue = currentValue >= 0 ? currentValue : throw new ArgumentOutOfRangeException(nameof(currentValue));
        }
        public string TargetId { get; }
        public int CurrentValue { get; }
    }

    public sealed class ResourceQuantityChanged : TargetValueProgressionEvent
    {
        public ResourceQuantityChanged(string resourceId, int value) : base(ProgressionEventKind.ResourceQuantityChanged, resourceId, value) { }
    }

    public sealed class ItemQuantityChanged : TargetValueProgressionEvent
    {
        public ItemQuantityChanged(string itemId, int value) : base(ProgressionEventKind.ItemQuantityChanged, itemId, value) { }
    }

    public sealed class BuildingLevelChanged : TargetValueProgressionEvent
    {
        public BuildingLevelChanged(string buildingId, int value) : base(ProgressionEventKind.BuildingLevelChanged, buildingId, value) { }
    }

    public sealed class ActivityCompleted : TargetValueProgressionEvent
    {
        public ActivityCompleted(string activityId, int count = 1) : base(ProgressionEventKind.ActivityCompleted, activityId, count) { }
    }

    public sealed class ActivityFailed : TargetValueProgressionEvent
    {
        public ActivityFailed(string activityId, int count = 1) : base(ProgressionEventKind.ActivityFailed, activityId, count) { }
    }

    public sealed class StageEntered : TargetValueProgressionEvent
    {
        public StageEntered(string stageId) : base(ProgressionEventKind.StageEntered, stageId, 1) { }
    }

    public sealed class QuestCompleted : ProgressionEvent
    {
        public QuestCompleted(string instanceId, string questId) : base(ProgressionEventKind.QuestCompleted)
        {
            InstanceId = string.IsNullOrWhiteSpace(instanceId) ? throw new ArgumentException("Quest instance id is required.", nameof(instanceId)) : instanceId;
            QuestId = string.IsNullOrWhiteSpace(questId) ? throw new ArgumentException("Quest definition id is required.", nameof(questId)) : questId;
        }
        public string InstanceId { get; }
        public string QuestId { get; }
    }
}
