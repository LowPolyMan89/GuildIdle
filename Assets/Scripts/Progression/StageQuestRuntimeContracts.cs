using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Player;

namespace GuildIdle.Progression
{
    public static class QuestConditionType
    {
        public const string NewGame = "NewGame";
        public const string ActivityFailed = "ActivityFailed";
        public const string StageEntered = "StageEntered";
        public const string BuildingLevel = "BuildingLevel";
    }

    public static class QuestObjectiveType
    {
        public const string ResourceCount = "ResourceCount";
        public const string ItemCount = "ItemCount";
        public const string BuildingLevel = "BuildingLevel";
        public const string ActivityCompleted = "ActivityCompleted";
    }

    public static class QuestStartConditionMatcher
    {
        public static bool IsSupported(string conditionType) =>
            Matches(conditionType, QuestConditionType.NewGame) ||
            Matches(conditionType, QuestConditionType.ActivityFailed) ||
            Matches(conditionType, QuestConditionType.StageEntered) ||
            Matches(conditionType, QuestConditionType.BuildingLevel);

        public static bool MatchesEvent(QuestStartConditionConfigDto condition, ProgressionEvent progressionEvent)
        {
            if (condition == null || progressionEvent == null || !IsSupported(condition.conditionType))
                return false;
            if (Matches(condition.conditionType, QuestConditionType.NewGame))
                return progressionEvent.Kind == ProgressionEventKind.NewGame && condition.value <= 1;
            if (!(progressionEvent is TargetValueProgressionEvent targetEvent))
                return false;
            if (Matches(condition.conditionType, QuestConditionType.ActivityFailed))
                return progressionEvent.Kind == ProgressionEventKind.ActivityFailed && TargetMatches(condition, targetEvent);
            if (Matches(condition.conditionType, QuestConditionType.StageEntered))
                return progressionEvent.Kind == ProgressionEventKind.StageEntered && TargetMatches(condition, targetEvent);
            return progressionEvent.Kind == ProgressionEventKind.BuildingLevelChanged && TargetMatches(condition, targetEvent);
        }

        private static bool TargetMatches(QuestStartConditionConfigDto condition, TargetValueProgressionEvent progressionEvent) =>
            string.Equals(condition.targetId, progressionEvent.TargetId, System.StringComparison.Ordinal) &&
            progressionEvent.CurrentValue >= condition.value;

        private static bool Matches(string value, string expected) =>
            string.Equals(value, expected, System.StringComparison.OrdinalIgnoreCase);
    }

    public interface IStageQuestConfigProvider
    {
        QuestConfigDto[] Quests { get; }
        bool TryGetQuest(string questId, out QuestConfigDto quest);
        QuestStartConditionConfigDto[] GetQuestStartConditions(string questId);
        QuestStepConfigDto[] GetQuestSteps(string questId);
        QuestRewardConfigDto[] GetQuestRewards(string questId);
        bool TryGetSettlementStage(string stageId, out SettlementStageConfigDto stage);
        SettlementStageObjectiveConfigDto[] GetSettlementStageObjectives(string stageId);
    }

    public interface IStageQuestRuntimeStore
    {
        string CurrentStageId { get; }
        bool SetCurrentStage(string stageId);
        QuestSaveData GetQuestState(string questId);
        QuestSaveData[] GetQuestStates();
        bool SetQuestState(QuestSaveData quest);
        int GetItem(string itemId);
        int GetBuildingLevel(string buildingId);
        bool IsActivityCompleted(string activityId);
        bool TryCommitQuestRewardBatch(
            QuestSaveData quest,
            RewardMutation[] mutations,
            out RewardMutationResult[] results,
            out string error);
        bool Save();
    }

    public sealed class RepositoryStageQuestConfigAdapter : IStageQuestConfigProvider
    {
        private readonly ActivitiesConfigRepository _activities;
        private readonly BuildingsConfigRepository _buildings;

        public RepositoryStageQuestConfigAdapter(
            ActivitiesConfigRepository activities,
            BuildingsConfigRepository buildings)
        {
            _activities = activities ?? throw new System.ArgumentNullException(nameof(activities));
            _buildings = buildings ?? throw new System.ArgumentNullException(nameof(buildings));
        }

        public QuestConfigDto[] Quests => _activities.Quests;
        public bool TryGetQuest(string questId, out QuestConfigDto quest) => _activities.TryGetQuest(questId, out quest);
        public QuestStartConditionConfigDto[] GetQuestStartConditions(string questId) => _activities.GetQuestStartConditions(questId);
        public QuestStepConfigDto[] GetQuestSteps(string questId) => _activities.GetQuestSteps(questId);
        public QuestRewardConfigDto[] GetQuestRewards(string questId) => _activities.GetQuestRewards(questId);
        public bool TryGetSettlementStage(string stageId, out SettlementStageConfigDto stage) => _buildings.TryGetSettlementStage(stageId, out stage);
        public SettlementStageObjectiveConfigDto[] GetSettlementStageObjectives(string stageId) => _buildings.GetSettlementStageObjectives(stageId);
    }

    public sealed class PlayerStateStageQuestAdapter : IStageQuestRuntimeStore
    {
        private readonly PlayerState _state;

        public PlayerStateStageQuestAdapter(PlayerState state)
        {
            _state = state ?? throw new System.ArgumentNullException(nameof(state));
        }

        public string CurrentStageId => _state.CurrentStageId;
        public bool SetCurrentStage(string stageId) => _state.SetCurrentStage(stageId);
        public QuestSaveData GetQuestState(string questId) => _state.GetQuestState(questId);
        public QuestSaveData[] GetQuestStates() => _state.GetQuestStates();
        public bool SetQuestState(QuestSaveData quest) => _state.SetQuestState(quest);
        public int GetItem(string itemId) => _state.GetItem(itemId);
        public int GetBuildingLevel(string buildingId) => _state.GetBuildingLevel(buildingId);
        public bool IsActivityCompleted(string activityId) => _state.IsActivityCompleted(activityId);
        public bool TryCommitQuestRewardBatch(QuestSaveData quest, RewardMutation[] mutations, out RewardMutationResult[] results, out string error) =>
            _state.TryCommitQuestRewardBatch(quest, mutations, out results, out error);
        public bool Save() => _state.Save();
    }
}
