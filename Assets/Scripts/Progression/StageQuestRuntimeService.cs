using System;
using System.Collections.Generic;
using GuildIdle.Activities;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Player;

namespace GuildIdle.Progression
{
    public sealed class StageQuestRuntimeService
    {
        private const string AllRequiredCompletionRule = "AllRequired";

        private readonly IStageQuestConfigProvider _configs;
        private readonly IStageQuestRuntimeStore _store;
        private readonly IActivityRandom _random;

        public StageQuestRuntimeService(
            IStageQuestConfigProvider configs,
            IStageQuestRuntimeStore store,
            IActivityRandom random = null)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _random = random ?? new SystemActivityRandom();
            Initialize();
        }

        public event Action<StageQuestUpdate> Updated;

        public StageQuestUpdate Handle(ProgressionEvent progressionEvent)
        {
            if (progressionEvent == null)
                throw new ArgumentNullException(nameof(progressionEvent));

            var issues = new List<StageQuestIssue>();
            var activated = new List<string>();
            var completed = new List<string>();
            var rewards = new List<StageQuestRewardGrant>();
            var changed = false;

            var validEvent = ValidateStageEnteredEvent(progressionEvent, issues);
            if (validEvent)
            {
                changed |= ActivateMatchingQuests(progressionEvent, issues, activated);
                changed |= ApplyEventToActiveQuests(progressionEvent, issues, completed);
            }
            changed |= ApplyPendingRewards(issues, rewards);
            var transition = TryTransition(issues, activated, completed, rewards, ref changed);
            var saved = changed && _store.Save();
            var update = new StageQuestUpdate(
                GetSnapshot(), issues, activated, completed, rewards, transition, changed, saved);
            if (changed)
                Updated?.Invoke(update);
            return update;
        }

        public StageQuestSnapshot GetSnapshot()
        {
            if (!_configs.TryGetSettlementStage(_store.CurrentStageId, out var stage) || stage == null || !stage.enabled)
                throw new InvalidOperationException($"Unknown or disabled current stage '{_store.CurrentStageId}'.");

            var objectives = OrderedObjectives(stage.stageId);
            var objectiveSnapshots = new List<StageObjectiveSnapshot>();
            var progress = 0;
            foreach (var objective in objectives)
            {
                if (objective == null)
                    continue;
                var state = _store.GetQuestState(objective.questId);
                var isCompleted = state?.completed == true && AreQuestObjectivesSupported(objective.questId);
                if (objective.required && isCompleted)
                    progress += Math.Max(0, objective.weightPercent);
                objectiveSnapshots.Add(new StageObjectiveSnapshot(
                    objective.questId,
                    objective.weightPercent,
                    objective.required,
                    objective.sortOrder,
                    state != null,
                    isCompleted));
            }

            progress = Math.Max(0, Math.Min(100, progress));
            var active = new List<QuestSnapshot>();
            var completed = new List<QuestSnapshot>();
            foreach (var state in OrderedQuestStates())
            {
                if (!_configs.TryGetQuest(state.questId, out var quest) || quest == null)
                    continue;
                var snapshot = BuildQuestSnapshot(quest, state, objectives);
                if (state.completed)
                    completed.Add(snapshot);
                else
                    active.Add(snapshot);
            }

            return new StageQuestSnapshot(
                new CurrentStageSnapshot(
                    stage.stageId,
                    stage.nameId,
                    stage.descriptionId,
                    stage.completionRule,
                    stage.nextStageId,
                    progress,
                    objectiveSnapshots),
                active,
                completed);
        }

        private void Initialize()
        {
            if (!_configs.TryGetSettlementStage(_store.CurrentStageId, out var stage) || stage == null || !stage.enabled)
                throw new InvalidOperationException($"Unknown or disabled current stage '{_store.CurrentStageId}'.");

            var issues = new List<StageQuestIssue>();
            var activated = new List<string>();
            var completed = new List<string>();
            var rewards = new List<StageQuestRewardGrant>();
            var changed = ReconcileExistingQuestStates(issues);
            changed |= ActivateStateBasedQuests(issues, activated);
            changed |= CompleteReadyQuests(issues, completed);
            changed |= ApplyPendingRewards(issues, rewards);
            TryTransition(issues, activated, completed, rewards, ref changed);
            if (changed)
                _store.Save();
        }

        private bool ReconcileExistingQuestStates(List<StageQuestIssue> issues)
        {
            var changed = false;
            foreach (var state in _store.GetQuestStates())
            {
                if (!_configs.TryGetQuest(state.questId, out var quest) || quest == null || !quest.enabled)
                    continue;
                var reconciled = QuestStateBuilder.Reconcile(state, _configs.GetQuestSteps(state.questId), out var questChanged);
                if (!reconciled.completed)
                    questChanged |= ApplyStateBackedObjectives(state.questId, reconciled, issues);
                if (!questChanged)
                    continue;
                if (_store.SetQuestState(reconciled))
                    changed = true;
                else
                    issues.Add(new StageQuestIssue("QuestReconcileFailed", state.questId, null, "Failed to reconcile saved quest state."));
            }
            return changed;
        }

        private bool ActivateStateBasedQuests(List<StageQuestIssue> issues, List<string> activated)
        {
            var changed = false;
            foreach (var quest in OrderedQuests())
            {
                if (quest == null || !quest.enabled || _store.GetQuestState(quest.questId) != null)
                    continue;
                var conditions = _configs.GetQuestStartConditions(quest.questId);
                if (!AllConditionsSupported(quest.questId, conditions, issues))
                    continue;
                var matches = false;
                foreach (var condition in conditions ?? Array.Empty<QuestStartConditionConfigDto>())
                {
                    if (condition != null && Matches(condition.conditionType, QuestConditionType.BuildingLevel) &&
                        _store.GetBuildingLevel(condition.targetId) >= condition.value)
                    {
                        matches = true;
                        break;
                    }
                }
                if (matches)
                    changed |= ActivateQuest(quest, issues, activated);
            }
            return changed;
        }

        private bool ActivateMatchingQuests(
            ProgressionEvent progressionEvent,
            List<StageQuestIssue> issues,
            List<string> activated)
        {
            var changed = false;
            foreach (var quest in OrderedQuests())
            {
                if (quest == null || !quest.enabled || _store.GetQuestState(quest.questId) != null)
                    continue;
                var conditions = _configs.GetQuestStartConditions(quest.questId);
                if (!AllConditionsSupported(quest.questId, conditions, issues))
                    continue;
                var matches = false;
                foreach (var condition in conditions ?? Array.Empty<QuestStartConditionConfigDto>())
                {
                    if (ConditionMatchesEvent(condition, progressionEvent))
                    {
                        matches = true;
                        break;
                    }
                }
                if (matches)
                    changed |= ActivateQuest(quest, issues, activated);
            }
            return changed;
        }

        private bool ActivateQuest(QuestConfigDto quest, List<StageQuestIssue> issues, List<string> activated)
        {
            var state = QuestStateBuilder.Create(quest.questId, _configs.GetQuestSteps(quest.questId));
            ApplyStateBackedObjectives(quest.questId, state, issues);
            if (!_store.SetQuestState(state))
            {
                issues.Add(new StageQuestIssue("QuestActivationFailed", quest.questId, null, "Failed to activate quest."));
                return false;
            }
            activated.Add(quest.questId);
            return true;
        }

        private bool ApplyStateBackedObjectives(string questId, QuestSaveData state, List<StageQuestIssue> issues)
        {
            var changed = false;
            var stepsById = IndexSteps(state);
            foreach (var config in _configs.GetQuestSteps(questId) ?? Array.Empty<QuestStepConfigDto>())
            {
                if (config == null || !stepsById.TryGetValue(config.stepId, out var step) || step.completed)
                    continue;
                if (!IsObjectiveSupported(config.objectiveType))
                {
                    AddUnsupportedObjective(questId, config, issues);
                    continue;
                }

                if (Matches(config.objectiveType, QuestObjectiveType.ResourceCount) ||
                    Matches(config.objectiveType, QuestObjectiveType.ItemCount))
                {
                    var beforeValue = step.currentValue;
                    var beforeCompleted = step.completed;
                    UpdateStep(step, _store.GetItem(config.targetId), config.targetValue);
                    changed |= beforeValue != step.currentValue || beforeCompleted != step.completed;
                }
                else if (Matches(config.objectiveType, QuestObjectiveType.BuildingLevel))
                {
                    var beforeValue = step.currentValue;
                    var beforeCompleted = step.completed;
                    UpdateStep(step, _store.GetBuildingLevel(config.targetId), config.targetValue);
                    changed |= beforeValue != step.currentValue || beforeCompleted != step.completed;
                }
            }
            return changed;
        }

        private bool ApplyEventToActiveQuests(
            ProgressionEvent progressionEvent,
            List<StageQuestIssue> issues,
            List<string> completedQuestIds)
        {
            var changed = false;
            foreach (var state in _store.GetQuestStates())
            {
                if (state == null || state.completed)
                    continue;
                var stateChanged = false;
                var invalidObjective = false;
                var stepsById = IndexSteps(state);
                foreach (var config in _configs.GetQuestSteps(state.questId) ?? Array.Empty<QuestStepConfigDto>())
                {
                    if (config == null || !stepsById.TryGetValue(config.stepId, out var step))
                        continue;
                    if (!IsObjectiveSupported(config.objectiveType))
                    {
                        AddUnsupportedObjective(state.questId, config, issues);
                        invalidObjective = true;
                        continue;
                    }
                    if (step.completed || !ObjectiveMatchesEvent(config, progressionEvent, out var currentValue))
                        continue;
                    var beforeValue = step.currentValue;
                    var beforeCompleted = step.completed;
                    UpdateStep(step, currentValue, config.targetValue);
                    stateChanged |= beforeValue != step.currentValue || beforeCompleted != step.completed;
                }

                if (!invalidObjective)
                    stateChanged |= TryMarkQuestCompleted(state.questId, state, issues, completedQuestIds);
                if (!stateChanged)
                    continue;
                if (_store.SetQuestState(state))
                    changed = true;
                else
                    issues.Add(new StageQuestIssue("QuestUpdateFailed", state.questId, null, "Failed to persist quest state."));
            }
            return changed;
        }

        private bool CompleteReadyQuests(List<StageQuestIssue> issues, List<string> completedQuestIds)
        {
            var changed = false;
            foreach (var state in _store.GetQuestStates())
            {
                if (state == null || state.completed || !TryMarkQuestCompleted(state.questId, state, issues, completedQuestIds))
                    continue;
                if (_store.SetQuestState(state))
                    changed = true;
            }
            return changed;
        }

        private bool TryMarkQuestCompleted(
            string questId,
            QuestSaveData state,
            List<StageQuestIssue> issues,
            List<string> completedQuestIds)
        {
            if (state.completed)
                return false;
            var requiredCount = 0;
            var stepsById = IndexSteps(state);
            foreach (var config in _configs.GetQuestSteps(questId) ?? Array.Empty<QuestStepConfigDto>())
            {
                if (config == null)
                    continue;
                if (!IsObjectiveSupported(config.objectiveType))
                {
                    AddUnsupportedObjective(questId, config, issues);
                    return false;
                }
                if (!config.required)
                    continue;
                requiredCount++;
                if (!stepsById.TryGetValue(config.stepId, out var step) || !step.completed)
                    return false;
            }
            if (requiredCount == 0)
                return false;
            state.completed = true;
            completedQuestIds?.Add(questId);
            return true;
        }

        private bool ApplyPendingRewards(List<StageQuestIssue> issues, List<StageQuestRewardGrant> grants)
        {
            var changed = false;
            foreach (var state in _store.GetQuestStates())
            {
                if (state == null || !state.completed || state.rewardsGranted)
                    continue;
                if (!AddObjectiveIssuesAndCheckSupported(state.questId, issues))
                    continue;
                var definitions = new List<RewardDefinition>();
                var invalidGrantMoment = false;
                foreach (var reward in _configs.GetQuestRewards(state.questId) ?? Array.Empty<QuestRewardConfigDto>())
                {
                    if (reward == null)
                        continue;
                    if (!ActivityResolverUtilities.MomentMatches(reward.grantMoment, GrantMoment.OnComplete))
                    {
                        issues.Add(new StageQuestIssue("QuestRewardInvalid", state.questId, null, $"Unsupported quest reward grant moment '{reward.grantMoment}'."));
                        invalidGrantMoment = true;
                        continue;
                    }
                    definitions.Add(new RewardDefinition
                    {
                        sourceId = state.questId,
                        rewardType = reward.rewardType,
                        targetId = reward.targetId,
                        min = reward.min,
                        max = reward.max,
                        chance = 100f,
                        grantMoment = reward.grantMoment
                    });
                }
                if (invalidGrantMoment)
                    continue;

                var prepared = RewardBatchPipeline.Prepare(definitions, GrantMoment.OnComplete, null, _random, true);
                if (!prepared.success)
                {
                    foreach (var issue in prepared.issues)
                        issues.Add(new StageQuestIssue("QuestRewardInvalid", state.questId, null, issue.message));
                    continue;
                }
                if (!_store.TryCommitQuestRewardBatch(state, prepared.mutations, out var results, out var error))
                {
                    issues.Add(new StageQuestIssue("QuestRewardCommitFailed", state.questId, null, error ?? "Failed to commit quest rewards."));
                    continue;
                }

                prepared.ApplyResults(results);
                foreach (var reward in prepared.rewards)
                    grants.Add(new StageQuestRewardGrant(state.questId, reward.rewardType, reward.targetId, reward.amount, reward.applied));
                changed = true;
            }
            return changed;
        }

        private TransitionResult TryTransition(
            List<StageQuestIssue> issues,
            List<string> activated,
            List<string> completed,
            List<StageQuestRewardGrant> rewards,
            ref bool changed)
        {
            if (!_configs.TryGetSettlementStage(_store.CurrentStageId, out var stage) || stage == null || !stage.enabled)
                return TransitionResult.None;
            var required = RequiredObjectives(stage.stageId);
            if (required.Count == 0)
                return TransitionResult.None;
            if (!Matches(stage.completionRule, AllRequiredCompletionRule))
            {
                issues.Add(new StageQuestIssue("UnsupportedCompletionRule", null, null, $"Unsupported completion rule '{stage.completionRule}'."));
                return TransitionResult.None;
            }
            foreach (var objective in required)
            {
                var quest = _store.GetQuestState(objective.questId);
                if (quest == null || !quest.completed || !quest.rewardsGranted ||
                    !AddObjectiveIssuesAndCheckSupported(objective.questId, issues))
                    return TransitionResult.None;
            }
            if (string.IsNullOrWhiteSpace(stage.nextStageId))
                return TransitionResult.None;

            var from = stage.stageId;
            if (!_store.SetCurrentStage(stage.nextStageId))
            {
                issues.Add(new StageQuestIssue("StageTransitionFailed", null, null, $"Failed to enter stage '{stage.nextStageId}'."));
                return TransitionResult.None;
            }

            changed = true;
            var entered = new StageEntered(stage.nextStageId);
            changed |= ActivateMatchingQuests(entered, issues, activated);
            changed |= ApplyEventToActiveQuests(entered, issues, completed);
            changed |= ApplyPendingRewards(issues, rewards);
            return new TransitionResult(true, from, stage.nextStageId);
        }

        private QuestSnapshot BuildQuestSnapshot(
            QuestConfigDto quest,
            QuestSaveData state,
            SettlementStageObjectiveConfigDto[] currentObjectives)
        {
            var required = false;
            foreach (var objective in currentObjectives)
            {
                if (objective != null && objective.required && string.Equals(objective.questId, quest.questId, StringComparison.Ordinal))
                {
                    required = true;
                    break;
                }
            }
            var saved = IndexSteps(state);
            var steps = new List<QuestStepSnapshot>();
            foreach (var config in OrderedSteps(quest.questId))
            {
                saved.TryGetValue(config.stepId, out var step);
                steps.Add(new QuestStepSnapshot(
                    config.stepId,
                    config.stepOrder,
                    config.objectiveType,
                    config.targetId,
                    config.targetValue,
                    step?.currentValue ?? 0,
                    config.descriptionId,
                    config.required,
                    step?.completed == true));
            }
            return new QuestSnapshot(quest.questId, quest.nameId, quest.descriptionId, quest.sortOrder, quest.isTutorial, required, state.completed, steps);
        }

        private QuestSaveData[] OrderedQuestStates()
        {
            var states = new List<QuestSaveData>(_store.GetQuestStates());
            states.Sort((left, right) =>
            {
                _configs.TryGetQuest(left.questId, out var leftConfig);
                _configs.TryGetQuest(right.questId, out var rightConfig);
                var order = (leftConfig?.sortOrder ?? 0).CompareTo(rightConfig?.sortOrder ?? 0);
                return order != 0 ? order : string.CompareOrdinal(left.questId, right.questId);
            });
            return states.ToArray();
        }

        private QuestConfigDto[] OrderedQuests()
        {
            var quests = new List<QuestConfigDto>(_configs.Quests ?? Array.Empty<QuestConfigDto>());
            quests.Sort((left, right) =>
            {
                var order = (left?.sortOrder ?? 0).CompareTo(right?.sortOrder ?? 0);
                return order != 0 ? order : string.CompareOrdinal(left?.questId, right?.questId);
            });
            return quests.ToArray();
        }

        private QuestStepConfigDto[] OrderedSteps(string questId)
        {
            var steps = new List<QuestStepConfigDto>(
                _configs.GetQuestSteps(questId) ?? Array.Empty<QuestStepConfigDto>());
            steps.Sort((left, right) =>
            {
                var order = (left?.stepOrder ?? 0).CompareTo(right?.stepOrder ?? 0);
                return order != 0 ? order : string.CompareOrdinal(left?.stepId, right?.stepId);
            });
            return steps.ToArray();
        }

        private SettlementStageObjectiveConfigDto[] OrderedObjectives(string stageId)
        {
            var objectives = new List<SettlementStageObjectiveConfigDto>(
                _configs.GetSettlementStageObjectives(stageId) ?? Array.Empty<SettlementStageObjectiveConfigDto>());
            objectives.Sort((left, right) =>
            {
                var order = (left?.sortOrder ?? 0).CompareTo(right?.sortOrder ?? 0);
                return order != 0 ? order : string.CompareOrdinal(left?.questId, right?.questId);
            });
            return objectives.ToArray();
        }

        private List<SettlementStageObjectiveConfigDto> RequiredObjectives(string stageId)
        {
            var required = new List<SettlementStageObjectiveConfigDto>();
            foreach (var objective in OrderedObjectives(stageId))
            {
                if (objective?.required == true)
                    required.Add(objective);
            }
            return required;
        }

        private static Dictionary<string, QuestStepSaveData> IndexSteps(QuestSaveData state)
        {
            var result = new Dictionary<string, QuestStepSaveData>(StringComparer.Ordinal);
            foreach (var step in state?.steps ?? Array.Empty<QuestStepSaveData>())
            {
                if (step != null && !string.IsNullOrWhiteSpace(step.stepId) && !result.ContainsKey(step.stepId))
                    result.Add(step.stepId, step);
            }
            return result;
        }

        private bool AllConditionsSupported(
            string questId,
            QuestStartConditionConfigDto[] conditions,
            List<StageQuestIssue> issues)
        {
            var supported = true;
            foreach (var condition in conditions ?? Array.Empty<QuestStartConditionConfigDto>())
            {
                if (condition != null && IsConditionSupported(condition.conditionType))
                    continue;
                supported = false;
                issues.Add(new StageQuestIssue("UnsupportedCondition", questId, null, $"Unsupported quest condition '{condition?.conditionType}'."));
            }
            return supported;
        }

        private static bool IsConditionSupported(string value) =>
            Matches(value, QuestConditionType.NewGame) || Matches(value, QuestConditionType.ActivityFailed) ||
            Matches(value, QuestConditionType.StageEntered) || Matches(value, QuestConditionType.BuildingLevel);

        private static bool IsObjectiveSupported(string value) =>
            Matches(value, QuestObjectiveType.ResourceCount) || Matches(value, QuestObjectiveType.ItemCount) ||
            Matches(value, QuestObjectiveType.BuildingLevel) || Matches(value, QuestObjectiveType.ActivityCompleted);

        private bool AreQuestObjectivesSupported(string questId)
        {
            foreach (var objective in _configs.GetQuestSteps(questId) ?? Array.Empty<QuestStepConfigDto>())
            {
                if (objective == null || !IsObjectiveSupported(objective.objectiveType))
                    return false;
            }
            return true;
        }

        private bool AddObjectiveIssuesAndCheckSupported(string questId, List<StageQuestIssue> issues)
        {
            var supported = true;
            foreach (var objective in _configs.GetQuestSteps(questId) ?? Array.Empty<QuestStepConfigDto>())
            {
                if (objective != null && IsObjectiveSupported(objective.objectiveType))
                    continue;
                supported = false;
                if (objective != null)
                    AddUnsupportedObjective(questId, objective, issues);
            }
            return supported;
        }

        private static bool ConditionMatchesEvent(QuestStartConditionConfigDto condition, ProgressionEvent progressionEvent)
        {
            if (condition == null)
                return false;
            if (Matches(condition.conditionType, QuestConditionType.NewGame))
                return progressionEvent.Kind == ProgressionEventKind.NewGame && condition.value <= 1;
            if (!(progressionEvent is TargetValueProgressionEvent targetEvent))
                return false;
            if (Matches(condition.conditionType, QuestConditionType.ActivityFailed))
                return progressionEvent.Kind == ProgressionEventKind.ActivityFailed && TargetMatches(condition, targetEvent);
            if (Matches(condition.conditionType, QuestConditionType.StageEntered))
                return progressionEvent.Kind == ProgressionEventKind.StageEntered && TargetMatches(condition, targetEvent);
            if (Matches(condition.conditionType, QuestConditionType.BuildingLevel))
                return progressionEvent.Kind == ProgressionEventKind.BuildingLevelChanged && TargetMatches(condition, targetEvent);
            return false;
        }

        private static bool TargetMatches(QuestStartConditionConfigDto condition, TargetValueProgressionEvent targetEvent) =>
            string.Equals(condition.targetId, targetEvent.TargetId, StringComparison.Ordinal) && targetEvent.CurrentValue >= condition.value;

        private static bool ObjectiveMatchesEvent(
            QuestStepConfigDto config,
            ProgressionEvent progressionEvent,
            out int currentValue)
        {
            currentValue = 0;
            if (!(progressionEvent is TargetValueProgressionEvent targetEvent) ||
                !string.Equals(config.targetId, targetEvent.TargetId, StringComparison.Ordinal))
                return false;
            if (Matches(config.objectiveType, QuestObjectiveType.ResourceCount) && progressionEvent.Kind != ProgressionEventKind.ResourceQuantityChanged)
                return false;
            if (Matches(config.objectiveType, QuestObjectiveType.ItemCount) && progressionEvent.Kind != ProgressionEventKind.ItemQuantityChanged)
                return false;
            if (Matches(config.objectiveType, QuestObjectiveType.BuildingLevel) && progressionEvent.Kind != ProgressionEventKind.BuildingLevelChanged)
                return false;
            if (Matches(config.objectiveType, QuestObjectiveType.ActivityCompleted) && progressionEvent.Kind != ProgressionEventKind.ActivityCompleted)
                return false;
            currentValue = targetEvent.CurrentValue;
            return true;
        }

        private static void UpdateStep(QuestStepSaveData step, int currentValue, int targetValue)
        {
            if (step.completed)
                return;
            step.currentValue = Math.Max(0, currentValue);
            if (step.currentValue >= targetValue)
                step.completed = true;
        }

        private static void AddUnsupportedObjective(string questId, QuestStepConfigDto config, List<StageQuestIssue> issues)
        {
            foreach (var issue in issues)
            {
                if (issue.Code == "UnsupportedObjective" &&
                    string.Equals(issue.QuestId, questId, StringComparison.Ordinal) &&
                    string.Equals(issue.StepId, config.stepId, StringComparison.Ordinal))
                {
                    return;
                }
            }
            issues.Add(new StageQuestIssue("UnsupportedObjective", questId, config.stepId, $"Unsupported quest objective '{config.objectiveType}'."));
        }

        private bool ValidateStageEnteredEvent(ProgressionEvent progressionEvent, List<StageQuestIssue> issues)
        {
            if (progressionEvent.Kind == ProgressionEventKind.StageEntered &&
                progressionEvent is TargetValueProgressionEvent entered &&
                !string.Equals(entered.TargetId, _store.CurrentStageId, StringComparison.Ordinal))
            {
                issues.Add(new StageQuestIssue("StageEnteredMismatch", null, null, $"StageEntered '{entered.TargetId}' does not match current stage '{_store.CurrentStageId}'."));
                return false;
            }
            return true;
        }

        private static bool Matches(string value, string expected) =>
            string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }
}
