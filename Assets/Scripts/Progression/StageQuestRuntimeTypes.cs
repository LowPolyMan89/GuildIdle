using System;
using System.Collections.Generic;

namespace GuildIdle.Progression
{
    public sealed class StageQuestIssue
    {
        public StageQuestIssue(string code, string questId, string stepId, string message)
        {
            Code = code ?? string.Empty;
            QuestId = questId;
            StepId = stepId;
            Message = message ?? string.Empty;
        }

        public string Code { get; }
        public string QuestId { get; }
        public string StepId { get; }
        public string Message { get; }
    }

    public sealed class StageQuestRewardGrant
    {
        public StageQuestRewardGrant(string questId, string rewardType, string targetId, long amount, bool applied)
        {
            QuestId = questId;
            RewardType = rewardType;
            TargetId = targetId;
            Amount = amount;
            Applied = applied;
        }

        public string QuestId { get; }
        public string RewardType { get; }
        public string TargetId { get; }
        public long Amount { get; }
        public bool Applied { get; }
    }

    public sealed class TransitionResult
    {
        public static readonly TransitionResult None = new TransitionResult(false, null, null);

        public TransitionResult(bool occurred, string fromStageId, string toStageId)
        {
            Occurred = occurred;
            FromStageId = fromStageId;
            ToStageId = toStageId;
        }

        public bool Occurred { get; }
        public string FromStageId { get; }
        public string ToStageId { get; }
    }

    public sealed class QuestStepSnapshot
    {
        public QuestStepSnapshot(
            string stepId,
            int stepOrder,
            string objectiveType,
            string targetId,
            int targetValue,
            int currentValue,
            string descriptionId,
            bool required,
            bool completed)
        {
            StepId = stepId;
            StepOrder = stepOrder;
            ObjectiveType = objectiveType;
            TargetId = targetId;
            TargetValue = targetValue;
            CurrentValue = currentValue;
            DescriptionId = descriptionId;
            Required = required;
            Completed = completed;
        }

        public string StepId { get; }
        public int StepOrder { get; }
        public string ObjectiveType { get; }
        public string TargetId { get; }
        public int TargetValue { get; }
        public int CurrentValue { get; }
        public string DescriptionId { get; }
        public bool Required { get; }
        public bool Completed { get; }
    }

    public sealed class QuestSnapshot
    {
        public QuestSnapshot(
            string questId,
            string nameId,
            string descriptionId,
            int sortOrder,
            bool isTutorial,
            bool required,
            bool completed,
            IReadOnlyList<QuestStepSnapshot> steps)
        {
            QuestId = questId;
            NameId = nameId;
            DescriptionId = descriptionId;
            SortOrder = sortOrder;
            IsTutorial = isTutorial;
            Required = required;
            Completed = completed;
            Steps = ReadOnly.Copy(steps);
        }

        public string QuestId { get; }
        public string NameId { get; }
        public string DescriptionId { get; }
        public int SortOrder { get; }
        public bool IsTutorial { get; }
        public bool Required { get; }
        public bool Optional => !Required;
        public bool Completed { get; }
        public IReadOnlyList<QuestStepSnapshot> Steps { get; }
    }

    public sealed class StageObjectiveSnapshot
    {
        public StageObjectiveSnapshot(string questId, int weightPercent, bool required, int sortOrder, bool active, bool completed)
        {
            QuestId = questId;
            WeightPercent = weightPercent;
            Required = required;
            SortOrder = sortOrder;
            Active = active;
            Completed = completed;
        }

        public string QuestId { get; }
        public int WeightPercent { get; }
        public bool Required { get; }
        public int SortOrder { get; }
        public bool Active { get; }
        public bool Completed { get; }
    }

    public sealed class CurrentStageSnapshot
    {
        public CurrentStageSnapshot(
            string stageId,
            string nameId,
            string descriptionId,
            string completionRule,
            string nextStageId,
            int requiredProgressPercent,
            IReadOnlyList<StageObjectiveSnapshot> objectives)
        {
            StageId = stageId;
            NameId = nameId;
            DescriptionId = descriptionId;
            CompletionRule = completionRule;
            NextStageId = nextStageId;
            RequiredProgressPercent = requiredProgressPercent;
            Objectives = ReadOnly.Copy(objectives);
        }

        public string StageId { get; }
        public string NameId { get; }
        public string DescriptionId { get; }
        public string CompletionRule { get; }
        public string NextStageId { get; }
        public int RequiredProgressPercent { get; }
        public IReadOnlyList<StageObjectiveSnapshot> Objectives { get; }
    }

    public sealed class StageQuestSnapshot
    {
        public StageQuestSnapshot(
            CurrentStageSnapshot currentStage,
            IReadOnlyList<QuestSnapshot> activeQuests,
            IReadOnlyList<QuestSnapshot> completedQuests)
        {
            CurrentStage = currentStage;
            ActiveQuests = ReadOnly.Copy(activeQuests);
            CompletedQuests = ReadOnly.Copy(completedQuests);
        }

        public CurrentStageSnapshot CurrentStage { get; }
        public IReadOnlyList<QuestSnapshot> ActiveQuests { get; }
        public IReadOnlyList<QuestSnapshot> CompletedQuests { get; }
    }

    public sealed class StageQuestUpdate
    {
        public StageQuestUpdate(
            StageQuestSnapshot snapshot,
            IReadOnlyList<StageQuestIssue> issues,
            IReadOnlyList<string> activatedQuestIds,
            IReadOnlyList<string> completedQuestIds,
            IReadOnlyList<StageQuestRewardGrant> rewards,
            TransitionResult transition,
            bool changed,
            bool saved)
        {
            Snapshot = snapshot;
            Issues = ReadOnly.Copy(issues);
            ActivatedQuestIds = ReadOnly.Copy(activatedQuestIds);
            CompletedQuestIds = ReadOnly.Copy(completedQuestIds);
            Rewards = ReadOnly.Copy(rewards);
            Transition = transition ?? TransitionResult.None;
            Changed = changed;
            Saved = saved;
        }

        public StageQuestSnapshot Snapshot { get; }
        public IReadOnlyList<StageQuestIssue> Issues { get; }
        public IReadOnlyList<string> ActivatedQuestIds { get; }
        public IReadOnlyList<string> CompletedQuestIds { get; }
        public IReadOnlyList<StageQuestRewardGrant> Rewards { get; }
        public TransitionResult Transition { get; }
        public bool Changed { get; }
        public bool Saved { get; }
    }

    internal static class ReadOnly
    {
        public static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0)
                return Array.AsReadOnly(Array.Empty<T>());

            var copy = new T[source.Count];
            for (var i = 0; i < source.Count; i++)
                copy[i] = source[i];
            return Array.AsReadOnly(copy);
        }
    }
}
