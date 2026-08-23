using System;
using System.Collections.Generic;
using GuildIdle.Configs;

namespace GuildIdle.Progression
{
    public sealed class ProgressionIssue
    {
        public ProgressionIssue(string code, string message, string questId = null, string instanceId = null, string stepId = null)
        { Code = code ?? string.Empty; Message = message ?? string.Empty; QuestId = questId; InstanceId = instanceId; StepId = stepId; }
        public string Code { get; }
        public string Message { get; }
        public string QuestId { get; }
        public string InstanceId { get; }
        public string StepId { get; }
    }

    public sealed class QuestStepSnapshot
    {
        public string StepId { get; internal set; }
        public int StepOrder { get; internal set; }
        public string ObjectiveType { get; internal set; }
        public string TargetId { get; internal set; }
        public string CompareOperator { get; internal set; }
        public int TargetValue { get; internal set; }
        public int CurrentValue { get; internal set; }
        public string DescriptionId { get; internal set; }
        public bool Required { get; internal set; }
        public bool Completed { get; internal set; }
    }

    public sealed class QuestInstanceSnapshot
    {
        public string InstanceId { get; internal set; }
        public string QuestId { get; internal set; }
        public string CycleId { get; internal set; }
        public string Status { get; internal set; }
        public QuestDefinitionKind DefinitionKind { get; internal set; }
        public string NameId { get; internal set; }
        public string ShortDescriptionId { get; internal set; }
        public string DescriptionId { get; internal set; }
        public string IconId { get; internal set; }
        public string JournalCategory { get; internal set; }
        public int SortOrder { get; internal set; }
        public bool IsTutorial { get; internal set; }
        public bool RewardsGranted { get; internal set; }
        public IReadOnlyList<QuestStepSnapshot> Steps { get; internal set; } = Array.AsReadOnly(Array.Empty<QuestStepSnapshot>());
    }

    public sealed class QuestRuntimeSnapshot
    {
        public IReadOnlyList<QuestInstanceSnapshot> ActiveInstances { get; internal set; } = Array.AsReadOnly(Array.Empty<QuestInstanceSnapshot>());
        public IReadOnlyList<QuestInstanceSnapshot> CompletedInstances { get; internal set; } = Array.AsReadOnly(Array.Empty<QuestInstanceSnapshot>());
    }

    public sealed class StageQuestInstanceSnapshot
    {
        public string InstanceId { get; internal set; }
        public string QuestId { get; internal set; }
        public string Status { get; internal set; }
        public int WeightPercent { get; internal set; }
        public bool Required { get; internal set; }
        public int SortOrder { get; internal set; }
    }

    public sealed class StageProgressionSnapshot
    {
        public string StageId { get; internal set; }
        public string NameId { get; internal set; }
        public string DescriptionId { get; internal set; }
        public string CompletionRule { get; internal set; }
        public string NextStageId { get; internal set; }
        public int RequiredProgressPercent { get; internal set; }
        public IReadOnlyList<StageQuestInstanceSnapshot> VisibleInstances { get; internal set; } = Array.AsReadOnly(Array.Empty<StageQuestInstanceSnapshot>());
    }

    public sealed class QuestRewardGrant
    {
        public string InstanceId { get; internal set; }
        public string QuestId { get; internal set; }
        public string RewardType { get; internal set; }
        public string TargetId { get; internal set; }
        public long Amount { get; internal set; }
        public bool Applied { get; internal set; }
    }

    public sealed class StageTransitionResult
    {
        public static readonly StageTransitionResult None = new StageTransitionResult();
        public bool Occurred { get; internal set; }
        public string FromStageId { get; internal set; }
        public string ToStageId { get; internal set; }
    }

    public sealed class ProgressionRuntimeUpdate
    {
        public QuestRuntimeSnapshot QuestSnapshot { get; internal set; }
        public StageProgressionSnapshot StageSnapshot { get; internal set; }
        public IReadOnlyList<ProgressionIssue> Issues { get; internal set; } = Array.AsReadOnly(Array.Empty<ProgressionIssue>());
        public IReadOnlyList<string> ActivatedInstanceIds { get; internal set; } = Array.AsReadOnly(Array.Empty<string>());
        public IReadOnlyList<string> CompletedInstanceIds { get; internal set; } = Array.AsReadOnly(Array.Empty<string>());
        public IReadOnlyList<QuestCompleted> PublishedQuestCompletedEvents { get; internal set; } = Array.AsReadOnly(Array.Empty<QuestCompleted>());
        public IReadOnlyList<QuestRewardGrant> Rewards { get; internal set; } = Array.AsReadOnly(Array.Empty<QuestRewardGrant>());
        public StageTransitionResult Transition { get; internal set; } = StageTransitionResult.None;
        public bool Changed { get; internal set; }
        public bool Saved { get; internal set; }
    }

    public sealed class QuestRuntimeResult
    {
        internal bool ChangedValue;
        internal readonly List<ProgressionIssue> IssueValues = new List<ProgressionIssue>();
        internal readonly List<string> ActivatedValues = new List<string>();
        internal readonly List<string> CompletedValues = new List<string>();
        internal readonly List<QuestCompleted> CompletionEventValues = new List<QuestCompleted>();
        internal readonly List<QuestRewardGrant> RewardValues = new List<QuestRewardGrant>();

        public bool Changed => ChangedValue;
        public IReadOnlyList<ProgressionIssue> Issues => SnapshotLists.ReadOnly(IssueValues);
        public IReadOnlyList<string> ActivatedInstanceIds => SnapshotLists.ReadOnly(ActivatedValues);
        public IReadOnlyList<string> CompletedInstanceIds => SnapshotLists.ReadOnly(CompletedValues);
        public IReadOnlyList<QuestCompleted> QuestCompletedEvents => SnapshotLists.ReadOnly(CompletionEventValues);
        public IReadOnlyList<QuestRewardGrant> Rewards => SnapshotLists.ReadOnly(RewardValues);
    }

    internal static class SnapshotLists
    {
        public static IReadOnlyList<T> ReadOnly<T>(List<T> values) => Array.AsReadOnly(values?.ToArray() ?? Array.Empty<T>());
    }
}
