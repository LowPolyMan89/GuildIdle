using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Player;

namespace GuildIdle.Progression
{
    public interface IQuestRuntimeConfigProvider
    {
        QuestDefinition[] Definitions { get; }
        bool TryGetDefinition(string questId, out QuestDefinition definition);
        QuestStartConditionConfigDto[] GetStartConditions(string questId);
        QuestStepConfigDto[] GetSteps(string questId);
        QuestRewardConfigDto[] GetRewards(string questId);
    }

    public interface IStageProgressionConfigProvider
    {
        bool TryGetStage(string stageId, out StageConfigDto stage);
        StageQuestConfigDto[] GetStageQuests(string stageId);
        bool TryGetDefinition(string questId, out QuestDefinition definition);
    }

    public interface IProgressionRuntimeStore
    {
        string CurrentStageId { get; }
        bool SetCurrentStage(string stageId);
        QuestInstanceSaveData GetQuestInstance(string instanceId);
        QuestInstanceSaveData[] GetQuestInstances();
        bool SetQuestInstance(QuestInstanceSaveData instance);
        int GetItem(string itemId);
        int GetBuildingLevel(string buildingId);
        bool IsActivityCompleted(string activityId);
        bool TryCommitQuestRewardBatch(QuestInstanceSaveData instance, RewardMutation[] mutations, out RewardMutationResult[] results, out string error);
        bool Save();
    }

    public sealed class RepositoryProgressionConfigAdapter : IQuestRuntimeConfigProvider, IStageProgressionConfigProvider
    {
        private readonly QuestConfigRepository _quests;
        public RepositoryProgressionConfigAdapter(QuestConfigRepository quests) { _quests = quests ?? throw new System.ArgumentNullException(nameof(quests)); }
        public QuestDefinition[] Definitions => _quests.Definitions;
        public bool TryGetDefinition(string questId, out QuestDefinition definition) => _quests.TryGetDefinition(questId, out definition);
        public QuestStartConditionConfigDto[] GetStartConditions(string questId) => _quests.GetStartConditions(questId);
        public QuestStepConfigDto[] GetSteps(string questId) => _quests.GetSteps(questId);
        public QuestRewardConfigDto[] GetRewards(string questId) => _quests.GetRewards(questId);
        public bool TryGetStage(string stageId, out StageConfigDto stage) => _quests.TryGetStage(stageId, out stage);
        public StageQuestConfigDto[] GetStageQuests(string stageId) => _quests.GetStageQuests(stageId);
    }

    public sealed class PlayerStateProgressionAdapter : IProgressionRuntimeStore
    {
        private readonly PlayerState _state;
        public PlayerStateProgressionAdapter(PlayerState state) { _state = state ?? throw new System.ArgumentNullException(nameof(state)); }
        public string CurrentStageId => _state.CurrentStageId;
        public bool SetCurrentStage(string stageId) => _state.SetCurrentStage(stageId);
        public QuestInstanceSaveData GetQuestInstance(string instanceId) => _state.GetQuestInstance(instanceId);
        public QuestInstanceSaveData[] GetQuestInstances() => _state.GetQuestInstances();
        public bool SetQuestInstance(QuestInstanceSaveData instance) => _state.SetQuestInstance(instance);
        public int GetItem(string itemId) => _state.GetItem(itemId);
        public int GetBuildingLevel(string buildingId) => _state.GetBuildingLevel(buildingId);
        public bool IsActivityCompleted(string activityId) => _state.IsActivityCompleted(activityId);
        public bool TryCommitQuestRewardBatch(QuestInstanceSaveData instance, RewardMutation[] mutations, out RewardMutationResult[] results, out string error) => _state.TryCommitQuestRewardBatch(instance, mutations, out results, out error);
        public bool Save() => _state.Save();
    }
}
