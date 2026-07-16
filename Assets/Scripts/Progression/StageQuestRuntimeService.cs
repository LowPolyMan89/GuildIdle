using System;
using System.Collections.Generic;
using GuildIdle.Activities;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Player;

namespace GuildIdle.Progression
{
    public sealed class QuestRuntimeService
    {
        private readonly IQuestRuntimeConfigProvider _configs;
        private readonly IProgressionRuntimeStore _store;
        private readonly IActivityRandom _random;

        public QuestRuntimeService(IQuestRuntimeConfigProvider configs, IProgressionRuntimeStore store, IActivityRandom random = null)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _random = random ?? new SystemActivityRandom();
        }

        public QuestRuntimeResult Initialize()
        {
            var result = new QuestRuntimeResult();
            ReconcileInstances(result);
            ActivateDefinitions(null, stateOnly: true, result);
            ApplyStateBackedSteps(result);
            CompleteReadyInstances(result);
            return result;
        }

        public QuestRuntimeResult Handle(ProgressionEvent progressionEvent)
        {
            if (progressionEvent == null) throw new ArgumentNullException(nameof(progressionEvent));
            var result = new QuestRuntimeResult();
            ActivateDefinitions(progressionEvent, stateOnly: false, result);
            ApplyEventToActiveInstances(progressionEvent, result);
            CompleteReadyInstances(result);
            return result;
        }

        public QuestRuntimeSnapshot GetSnapshot()
        {
            var active = new List<QuestInstanceSnapshot>();
            var completed = new List<QuestInstanceSnapshot>();
            foreach (var instance in OrderedInstances())
            {
                if (!_configs.TryGetDefinition(instance.questId, out var definition) || !IsValidInstanceForDefinition(instance, definition)) continue;
                var snapshot = BuildSnapshot(instance, definition);
                if (instance.status == QuestInstanceStatus.Active || instance.status == QuestInstanceStatus.RewardPending) active.Add(snapshot);
                else if (instance.status == QuestInstanceStatus.Completed) completed.Add(snapshot);
            }
            return new QuestRuntimeSnapshot { ActiveInstances = SnapshotLists.ReadOnly(active), CompletedInstances = SnapshotLists.ReadOnly(completed) };
        }

        private void ReconcileInstances(QuestRuntimeResult result)
        {
            foreach (var instance in _store.GetQuestInstances() ?? Array.Empty<QuestInstanceSaveData>())
            {
                if (!_configs.TryGetDefinition(instance.questId, out var definition))
                {
                    result.IssueValues.Add(new ProgressionIssue("UnknownQuestDefinition", "Saved quest instance has no current definition and was preserved.", instance.questId, instance.instanceId));
                    continue;
                }
                if (!IsValidInstanceForDefinition(instance, definition))
                {
                    result.IssueValues.Add(new ProgressionIssue("InvalidQuestInstance", "Saved quest instance id/cycle does not match its quest definition kind and was preserved without progression.", instance.questId, instance.instanceId));
                    continue;
                }
                var configuredSteps = _configs.GetSteps(instance.questId) ?? Array.Empty<QuestStepConfigDto>();
                var configuredStepIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var step in configuredSteps)
                    if (step != null && !string.IsNullOrWhiteSpace(step.stepId)) configuredStepIds.Add(step.stepId);
                foreach (var savedStep in instance.steps ?? Array.Empty<QuestStepSaveData>())
                    if (savedStep != null && !string.IsNullOrWhiteSpace(savedStep.stepId) && !configuredStepIds.Contains(savedStep.stepId))
                        result.IssueValues.Add(new ProgressionIssue("UnknownQuestStep", "Saved quest step has no current definition and was preserved.", instance.questId, instance.instanceId, savedStep.stepId));
                var reconciled = QuestStateBuilder.Reconcile(instance, configuredSteps, out var changed);
                if (changed && _store.SetQuestInstance(reconciled)) result.ChangedValue = true;
            }
        }

        private void ActivateDefinitions(ProgressionEvent progressionEvent, bool stateOnly, QuestRuntimeResult result)
        {
            foreach (var definition in OrderedDefinitions())
            {
                if (definition == null || !definition.Enabled || definition.Kind != QuestDefinitionKind.Story) continue;
                var instanceId = QuestInstanceIds.Story(definition.QuestId);
                if (_store.GetQuestInstance(instanceId) != null) continue;
                var conditions = _configs.GetStartConditions(definition.QuestId) ?? Array.Empty<QuestStartConditionConfigDto>();
                if (!ConditionsMatch(definition, conditions, progressionEvent, stateOnly, result)) continue;
                var instance = QuestStateBuilder.Create(instanceId, definition.QuestId, null, _configs.GetSteps(definition.QuestId));
                if (!_store.SetQuestInstance(instance))
                {
                    result.IssueValues.Add(new ProgressionIssue("QuestActivationFailed", "Failed to persist activated quest instance in PlayerState.", definition.QuestId, instanceId));
                    continue;
                }
                result.ChangedValue = true;
                result.ActivatedValues.Add(instanceId);
                ApplyStateBackedSteps(instance, result);
            }
        }

        private bool ConditionsMatch(QuestDefinition definition, QuestStartConditionConfigDto[] conditions, ProgressionEvent progressionEvent, bool stateOnly, QuestRuntimeResult result)
        {
            if (conditions.Length == 0) return false;
            var groups = new Dictionary<string, List<QuestStartConditionConfigDto>>(StringComparer.Ordinal);
            foreach (var condition in conditions)
            {
                if (!IsValidCondition(condition, out var error))
                {
                    result.IssueValues.Add(new ProgressionIssue("InvalidQuestStartCondition", error, definition.QuestId));
                    return false;
                }
                if (!groups.TryGetValue(condition.conditionGroup, out var group)) groups[condition.conditionGroup] = group = new List<QuestStartConditionConfigDto>();
                group.Add(condition);
            }
            foreach (var group in groups.Values)
            {
                var matches = true;
                foreach (var condition in group)
                {
                    if (!ConditionMatches(condition, progressionEvent, stateOnly)) { matches = false; break; }
                }
                if (matches) return true;
            }
            return false;
        }

        private bool ConditionMatches(QuestStartConditionConfigDto condition, ProgressionEvent progressionEvent, bool stateOnly)
        {
            switch (condition.conditionType)
            {
                case "NewGame":
                    return !stateOnly && progressionEvent?.Kind == ProgressionEventKind.NewGame && Compare(1, condition.value, condition.compareOperator);
                case "ActivityFailed":
                    return !stateOnly && progressionEvent is ActivityFailed failed && failed.TargetId == condition.targetId && Compare(failed.CurrentValue, condition.value, condition.compareOperator);
                case "StageEntered":
                    if (string.Equals(_store.CurrentStageId, condition.targetId, StringComparison.Ordinal) && Compare(1, condition.value, condition.compareOperator)) return true;
                    return !stateOnly && progressionEvent is StageEntered entered && entered.TargetId == condition.targetId && Compare(entered.CurrentValue, condition.value, condition.compareOperator);
                case "BuildingLevel":
                    return Compare(_store.GetBuildingLevel(condition.targetId), condition.value, condition.compareOperator);
                case "QuestCompleted":
                    if (!stateOnly && progressionEvent is QuestCompleted completed && completed.QuestId == condition.targetId && Compare(1, condition.value, condition.compareOperator)) return true;
                    return Compare(HasCompletedDefinition(condition.targetId) ? 1 : 0, condition.value, condition.compareOperator);
                default:
                    return false;
            }
        }

        private static bool IsValidCondition(QuestStartConditionConfigDto condition, out string error)
        {
            error = null;
            if (condition == null || string.IsNullOrWhiteSpace(condition.conditionGroup)) { error = "condition_group is required."; return false; }
            if (!IsOperatorSupported(condition.compareOperator)) { error = $"Unsupported compare operator '{condition.compareOperator}'."; return false; }
            if (condition.value < 0) { error = "Condition value must be non-negative."; return false; }
            switch (condition.conditionType)
            {
                case "NewGame": if (!string.IsNullOrWhiteSpace(condition.targetId)) { error = "NewGame requires empty target_id."; return false; } return true;
                case "ActivityFailed": case "StageEntered": case "BuildingLevel": case "QuestCompleted":
                    if (string.IsNullOrWhiteSpace(condition.targetId)) { error = $"{condition.conditionType} requires target_id."; return false; } return true;
                default: error = $"Unsupported condition_type '{condition.conditionType}'."; return false;
            }
        }

        private void ApplyStateBackedSteps(QuestRuntimeResult result)
        {
            foreach (var instance in _store.GetQuestInstances() ?? Array.Empty<QuestInstanceSaveData>())
                if (instance.status == QuestInstanceStatus.Active && _configs.TryGetDefinition(instance.questId, out var definition) && IsValidInstanceForDefinition(instance, definition)) ApplyStateBackedSteps(instance, result);
        }

        private void ApplyStateBackedSteps(QuestInstanceSaveData instance, QuestRuntimeResult result)
        {
            var changed = false;
            var states = IndexSteps(instance);
            foreach (var step in _configs.GetSteps(instance.questId) ?? Array.Empty<QuestStepConfigDto>())
            {
                if (step == null || !states.TryGetValue(step.stepId, out var state) || state.completed) continue;
                int? value = null;
                switch (step.objectiveType)
                {
                    case "ResourceCount": case "ItemCount": value = _store.GetItem(step.targetId); break;
                    case "BuildingLevel": value = _store.GetBuildingLevel(step.targetId); break;
                    case "ActivityCompleted": value = _store.IsActivityCompleted(step.targetId) ? 1 : 0; break;
                    case "QuestCompleted": value = HasCompletedDefinition(step.targetId) ? 1 : 0; break;
                }
                if (value.HasValue) changed |= UpdateStep(state, value.Value, step);
            }
            if (changed && _store.SetQuestInstance(instance)) { result.ChangedValue = true; }
        }

        private void ApplyEventToActiveInstances(ProgressionEvent progressionEvent, QuestRuntimeResult result)
        {
            foreach (var instance in _store.GetQuestInstances() ?? Array.Empty<QuestInstanceSaveData>())
            {
                if (instance.status != QuestInstanceStatus.Active || !_configs.TryGetDefinition(instance.questId, out var definition) || !IsValidInstanceForDefinition(instance, definition)) continue;
                var changed = false;
                var states = IndexSteps(instance);
                foreach (var step in _configs.GetSteps(instance.questId) ?? Array.Empty<QuestStepConfigDto>())
                {
                    if (step == null || !states.TryGetValue(step.stepId, out var state) || state.completed) continue;
                    if (TryGetEventValue(step, progressionEvent, out var value)) changed |= UpdateStep(state, value, step);
                }
                if (changed && _store.SetQuestInstance(instance)) result.ChangedValue = true;
            }
        }

        private static bool TryGetEventValue(QuestStepConfigDto step, ProgressionEvent progressionEvent, out int value)
        {
            value = 0;
            if (progressionEvent is QuestCompleted completed && step.objectiveType == "QuestCompleted" && completed.QuestId == step.targetId) { value = 1; return true; }
            if (!(progressionEvent is TargetValueProgressionEvent target) || target.TargetId != step.targetId) return false;
            if (step.objectiveType == "ResourceCount" && progressionEvent.Kind == ProgressionEventKind.ResourceQuantityChanged ||
                step.objectiveType == "ItemCount" && progressionEvent.Kind == ProgressionEventKind.ItemQuantityChanged ||
                step.objectiveType == "BuildingLevel" && progressionEvent.Kind == ProgressionEventKind.BuildingLevelChanged ||
                step.objectiveType == "ActivityCompleted" && progressionEvent.Kind == ProgressionEventKind.ActivityCompleted)
            { value = target.CurrentValue; return true; }
            return false;
        }

        private void CompleteReadyInstances(QuestRuntimeResult result)
        {
            foreach (var instance in _store.GetQuestInstances() ?? Array.Empty<QuestInstanceSaveData>())
            {
                if (instance.status != QuestInstanceStatus.Active || !_configs.TryGetDefinition(instance.questId, out var definition) || !IsValidInstanceForDefinition(instance, definition)) continue;
                var states = IndexSteps(instance); var blocks = false;
                foreach (var step in _configs.GetSteps(instance.questId) ?? Array.Empty<QuestStepConfigDto>())
                {
                    if (!IsValidStep(step, out var error))
                    {
                        result.IssueValues.Add(new ProgressionIssue("UnsupportedQuestStep", error, instance.questId, instance.instanceId, step?.stepId));
                        if (step != null && step.required) blocks = true;
                        continue;
                    }
                    if (step.required && (!states.TryGetValue(step.stepId, out var state) || !state.completed)) blocks = true;
                }
                if (blocks) continue;
                var definitions = new List<RewardDefinition>(); var valid = true;
                foreach (var reward in _configs.GetRewards(instance.questId) ?? Array.Empty<QuestRewardConfigDto>())
                {
                    if (reward == null || reward.grantMoment != GrantMoment.OnComplete)
                    {
                        result.IssueValues.Add(new ProgressionIssue("InvalidQuestReward", "Quest reward must use OnComplete grant moment.", instance.questId, instance.instanceId)); valid = false; continue;
                    }
                    definitions.Add(new RewardDefinition { sourceId = instance.instanceId, rewardType = reward.rewardType, targetId = reward.targetId, min = reward.min, max = reward.max, chance = reward.chance, grantMoment = reward.grantMoment });
                }
                if (!valid) continue;
                var prepared = RewardBatchPipeline.Prepare(definitions, GrantMoment.OnComplete, null, _random, true);
                if (!prepared.success)
                {
                    foreach (var issue in prepared.issues) result.IssueValues.Add(new ProgressionIssue("InvalidQuestReward", issue.message, instance.questId, instance.instanceId));
                    continue;
                }
                var formation = _store.PendingResults.CreateOrAppend(
                    $"quest:{instance.instanceId}:reward",
                    new PendingResultDraft
                    {
                        SourceType = PendingResultSourceType.Quest,
                        SourceId = instance.questId,
                        SourceExecutionId = instance.instanceId,
                        Entries = PendingResultEntryFactory.FromActivityRewards(prepared.rewards, PendingResultOrigin.QuestReward)
                    },
                    true);
                if (!formation.Success)
                {
                    result.IssueValues.Add(new ProgressionIssue("QuestRewardFormationFailed", formation.Message ?? "Quest PendingResult formation failed.", instance.questId, instance.instanceId));
                    continue;
                }
                result.ChangedValue = true;
                foreach (var reward in prepared.rewards)
                    if (reward != null && reward.amount > 0 && !reward.isResultOnly && reward.lootRoll == null)
                        result.RewardValues.Add(new QuestRewardGrant { InstanceId = instance.instanceId, QuestId = instance.questId, RewardType = reward.rewardType, TargetId = reward.targetId, Amount = reward.amount, Applied = false });
                if (formation.ResolvedImmediately)
                {
                    result.CompletedValues.Add(instance.instanceId);
                    result.CompletionEventValues.Add(new QuestCompleted(instance.instanceId, instance.questId));
                }
            }
        }

        private QuestInstanceSnapshot BuildSnapshot(QuestInstanceSaveData instance, QuestDefinition definition)
        {
            var steps = new List<QuestStepSnapshot>(); var states = IndexSteps(instance);
            foreach (var config in _configs.GetSteps(instance.questId) ?? Array.Empty<QuestStepConfigDto>())
            {
                states.TryGetValue(config.stepId, out var state);
                steps.Add(new QuestStepSnapshot { StepId = config.stepId, StepOrder = config.stepOrder, ObjectiveType = config.objectiveType, TargetId = config.targetId, CompareOperator = config.compareOperator, TargetValue = config.targetValue, CurrentValue = state?.currentValue ?? 0, DescriptionId = config.descriptionId, Required = config.required, Completed = state?.completed ?? false });
            }
            return new QuestInstanceSnapshot { InstanceId = instance.instanceId, QuestId = instance.questId, CycleId = instance.cycleId, Status = instance.status, DefinitionKind = definition.Kind, NameId = definition.NameId, DescriptionId = definition.DescriptionId, IconId = definition.IconId, JournalCategory = definition.JournalCategory, SortOrder = definition.SortOrder, IsTutorial = definition.IsTutorial, RewardsGranted = instance.rewardsGranted, Steps = SnapshotLists.ReadOnly(steps) };
        }

        private bool HasCompletedDefinition(string questId)
        {
            if (!_configs.TryGetDefinition(questId, out var definition)) return false;
            foreach (var instance in _store.GetQuestInstances() ?? Array.Empty<QuestInstanceSaveData>())
                if (instance.questId == questId && instance.status == QuestInstanceStatus.Completed && IsValidInstanceForDefinition(instance, definition)) return true;
            return false;
        }

        private static bool IsValidInstanceForDefinition(QuestInstanceSaveData instance, QuestDefinition definition)
        {
            if (instance == null || definition == null || !string.Equals(instance.questId, definition.QuestId, StringComparison.Ordinal)) return false;
            if (definition.Kind == QuestDefinitionKind.Story)
                return string.IsNullOrWhiteSpace(instance.cycleId) && string.Equals(instance.instanceId, QuestInstanceIds.Story(definition.QuestId), StringComparison.Ordinal);
            return !string.IsNullOrWhiteSpace(instance.cycleId) && string.Equals(instance.instanceId, QuestInstanceIds.Daily(instance.cycleId, definition.QuestId), StringComparison.Ordinal);
        }

        private QuestDefinition[] OrderedDefinitions()
        {
            var result = (QuestDefinition[])(_configs.Definitions ?? Array.Empty<QuestDefinition>()).Clone();
            Array.Sort(result, (left, right) => { var order = (left?.SortOrder ?? int.MaxValue).CompareTo(right?.SortOrder ?? int.MaxValue); return order != 0 ? order : string.CompareOrdinal(left?.QuestId, right?.QuestId); });
            return result;
        }

        private QuestInstanceSaveData[] OrderedInstances()
        {
            var result = _store.GetQuestInstances() ?? Array.Empty<QuestInstanceSaveData>();
            Array.Sort(result, (left, right) => string.CompareOrdinal(left?.instanceId, right?.instanceId)); return result;
        }

        private static Dictionary<string, QuestStepSaveData> IndexSteps(QuestInstanceSaveData instance)
        {
            var result = new Dictionary<string, QuestStepSaveData>(StringComparer.Ordinal);
            foreach (var step in instance.steps ?? Array.Empty<QuestStepSaveData>()) if (step != null && !string.IsNullOrWhiteSpace(step.stepId) && !result.ContainsKey(step.stepId)) result.Add(step.stepId, step);
            return result;
        }

        private static bool UpdateStep(QuestStepSaveData state, int value, QuestStepConfigDto config)
        {
            var normalized = Math.Max(0, value); var completed = Compare(normalized, config.targetValue, config.compareOperator);
            if (state.currentValue == normalized && state.completed == completed) return false;
            state.currentValue = normalized; state.completed |= completed; return true;
        }

        private static bool IsValidStep(QuestStepConfigDto step, out string error)
        {
            error = null;
            if (step == null || string.IsNullOrWhiteSpace(step.stepId) || string.IsNullOrWhiteSpace(step.targetId)) { error = "Quest step requires step_id and target_id."; return false; }
            if (!IsOperatorSupported(step.compareOperator)) { error = $"Unsupported compare operator '{step.compareOperator}'."; return false; }
            if (step.targetValue < 0) { error = "Quest step target_value must be non-negative."; return false; }
            switch (step.objectiveType)
            {
                case "ResourceCount": case "ItemCount": case "BuildingLevel": case "ActivityCompleted": case "QuestCompleted": return true;
                default: error = $"Unsupported objective_type '{step.objectiveType}'."; return false;
            }
        }

        private static bool IsOperatorSupported(string value) => value == "GreaterOrEqual" || value == "Equal";
        private static bool Compare(int current, int target, string operation) => operation == "Equal" ? current == target : operation == "GreaterOrEqual" && current >= target;
    }

    public sealed class StageProgressionService
    {
        private readonly IStageProgressionConfigProvider _configs;
        private readonly IProgressionRuntimeStore _store;
        public StageProgressionService(IStageProgressionConfigProvider configs, IProgressionRuntimeStore store) { _configs = configs ?? throw new ArgumentNullException(nameof(configs)); _store = store ?? throw new ArgumentNullException(nameof(store)); }
        public StageTransitionResult TryTransition() => TryTransition(new List<ProgressionIssue>());

        internal StageTransitionResult TryTransition(List<ProgressionIssue> issues)
        {
            if (!_configs.TryGetStage(_store.CurrentStageId, out var stage) || stage == null || !stage.enabled)
            { issues.Add(new ProgressionIssue("InvalidCurrentStage", $"Current stage '{_store.CurrentStageId}' is missing or disabled.")); return StageTransitionResult.None; }
            if (stage.completionRule != "AllRequired" || string.IsNullOrWhiteSpace(stage.nextStageId)) return StageTransitionResult.None;
            var hasRequiredQuest = false;
            foreach (var relation in _configs.GetStageQuests(stage.stageId) ?? Array.Empty<StageQuestConfigDto>())
            {
                if (relation == null || !relation.enabled || !relation.required) continue;
                hasRequiredQuest = true;
                var instance = _store.GetQuestInstance(QuestInstanceIds.Story(relation.questId));
                if (instance == null || instance.questId != relation.questId || instance.status != QuestInstanceStatus.Completed || !instance.rewardsGranted) return StageTransitionResult.None;
            }
            if (!hasRequiredQuest) return StageTransitionResult.None;
            if (!_configs.TryGetStage(stage.nextStageId, out var next) || next == null || !next.enabled || !_store.SetCurrentStage(next.stageId))
            { issues.Add(new ProgressionIssue("StageTransitionFailed", $"Could not enter stage '{stage.nextStageId}'.")); return StageTransitionResult.None; }
            return new StageTransitionResult { Occurred = true, FromStageId = stage.stageId, ToStageId = next.stageId };
        }

        public StageProgressionSnapshot GetSnapshot()
        {
            if (!_configs.TryGetStage(_store.CurrentStageId, out var stage) || stage == null) return new StageProgressionSnapshot { StageId = _store.CurrentStageId };
            var visible = new List<StageQuestInstanceSnapshot>(); var progress = 0;
            foreach (var relation in _configs.GetStageQuests(stage.stageId) ?? Array.Empty<StageQuestConfigDto>())
            {
                if (relation == null || !relation.enabled) continue;
                var instance = _store.GetQuestInstance(QuestInstanceIds.Story(relation.questId));
                if (instance != null && instance.questId != relation.questId) instance = null;
                if (relation.required && instance?.status == QuestInstanceStatus.Completed) progress += relation.weightPercent;
                if (relation.showInStageUi && instance != null) visible.Add(new StageQuestInstanceSnapshot { InstanceId = instance.instanceId, QuestId = instance.questId, Status = instance.status, WeightPercent = relation.weightPercent, Required = relation.required, SortOrder = relation.sortOrder });
            }
            visible.Sort((left, right) => left.SortOrder.CompareTo(right.SortOrder));
            return new StageProgressionSnapshot { StageId = stage.stageId, NameId = stage.nameId, DescriptionId = stage.descriptionId, StagePrefabId = stage.stagePrefabId, CompletionRule = stage.completionRule, NextStageId = stage.nextStageId, RequiredProgressPercent = Math.Min(100, progress), VisibleInstances = SnapshotLists.ReadOnly(visible) };
        }
    }

    public sealed class ProgressionRuntimeService
    {
        private readonly QuestRuntimeService _quests;
        private readonly StageProgressionService _stages;
        private readonly IProgressionRuntimeStore _store;
        private readonly INonBuildTransitionProvider _nonBuildTransitions;
        private bool _savePending;
        public ProgressionRuntimeService(QuestRuntimeService quests, StageProgressionService stages, IProgressionRuntimeStore store, INonBuildTransitionProvider nonBuildTransitions = null)
        {
            _quests = quests ?? throw new ArgumentNullException(nameof(quests));
            _stages = stages ?? throw new ArgumentNullException(nameof(stages));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _nonBuildTransitions = nonBuildTransitions;
            if (_store.PendingResults != null)
                _store.PendingResults.Resolved += HandlePendingResultResolved;
        }
        public event Action<ProgressionRuntimeUpdate> Updated;
        public ProgressionRuntimeUpdate Initialize() => CompleteTransaction(_quests.Initialize());
        public ProgressionRuntimeUpdate Handle(ProgressionEvent progressionEvent) => CompleteTransaction(_quests.Handle(progressionEvent ?? throw new ArgumentNullException(nameof(progressionEvent))));
        public ProgressionRuntimeUpdate HandleActivityCompleted(string activityId) => CompleteTransaction(BuildActivityCompletion(activityId));
        public QuestRuntimeSnapshot GetQuestSnapshot() => _quests.GetSnapshot();
        public StageProgressionSnapshot GetStageSnapshot() => _stages.GetSnapshot();

        private void HandlePendingResultResolved(PendingResultResolvedEvent resolved)
        {
            if (resolved == null)
                return;
            if (string.Equals(resolved.SourceType, PendingResultSourceType.Activity, StringComparison.Ordinal))
            {
                if (!resolved.SourceCompleted)
                    return;
                HandleActivityCompleted(resolved.SourceId);
                return;
            }
            if (!string.Equals(resolved.SourceType, PendingResultSourceType.Quest, StringComparison.Ordinal))
                return;
            if (resolved.ResolvedImmediately)
                return; // Empty quest rewards are already folded into the active QuestRuntime transaction.
            var completed = new QuestCompleted(resolved.SourceExecutionId, resolved.SourceId);
            var aggregate = new QuestRuntimeResult { ChangedValue = true };
            aggregate.CompletedValues.Add(resolved.SourceExecutionId);
            aggregate.CompletionEventValues.Add(completed);
            CompleteTransaction(aggregate);
        }

        private QuestRuntimeResult BuildActivityCompletion(string activityId)
        {
            var aggregate = _quests.Handle(new ActivityCompleted(activityId));
            foreach (var level in _nonBuildTransitions?.GetLevelsBySourceActivity(activityId) ?? Array.Empty<BuildingLevelConfigDto>())
            {
                if (level == null || !string.IsNullOrWhiteSpace(level.buildFormulaId) ||
                    level.level <= _store.GetBuildingLevel(level.buildingId) ||
                    !_store.SetBuildingLevel(level.buildingId, level.level))
                    continue;
                aggregate.ChangedValue = true;
                Merge(aggregate, _quests.Handle(new BuildingLevelChanged(level.buildingId, level.level)));
            }
            return aggregate;
        }

        private ProgressionRuntimeUpdate CompleteTransaction(QuestRuntimeResult aggregate)
        {
            DrainQuestCompleted(aggregate);
            var transition = _stages.TryTransition(aggregate.IssueValues);
            if (transition.Occurred)
            {
                aggregate.ChangedValue = true;
                Merge(aggregate, _quests.Handle(new StageEntered(transition.ToStageId)));
                DrainQuestCompleted(aggregate);
            }
            var mustSave = aggregate.ChangedValue || _savePending;
            var saved = !mustSave || _store.Save();
            _savePending = mustSave && !saved;
            if (!saved) aggregate.IssueValues.Add(new ProgressionIssue("ProgressionSaveFailed", "Failed to save the coordinated progression transaction."));
            var update = new ProgressionRuntimeUpdate
            {
                QuestSnapshot = _quests.GetSnapshot(), StageSnapshot = _stages.GetSnapshot(), Issues = SnapshotLists.ReadOnly(aggregate.IssueValues),
                ActivatedInstanceIds = SnapshotLists.ReadOnly(aggregate.ActivatedValues), CompletedInstanceIds = SnapshotLists.ReadOnly(aggregate.CompletedValues),
                PublishedQuestCompletedEvents = SnapshotLists.ReadOnly(aggregate.CompletionEventValues), Rewards = SnapshotLists.ReadOnly(aggregate.RewardValues),
                Transition = transition, Changed = aggregate.ChangedValue, Saved = saved
            };
            Updated?.Invoke(update);
            return update;
        }

        private void DrainQuestCompleted(QuestRuntimeResult aggregate)
        {
            for (var index = 0; index < aggregate.CompletionEventValues.Count; index++) Merge(aggregate, _quests.Handle(aggregate.CompletionEventValues[index]));
        }

        private static void Merge(QuestRuntimeResult target, QuestRuntimeResult source)
        {
            if (source == null) return;
            target.ChangedValue |= source.ChangedValue; target.IssueValues.AddRange(source.IssueValues); target.ActivatedValues.AddRange(source.ActivatedValues);
            target.CompletedValues.AddRange(source.CompletedValues); target.CompletionEventValues.AddRange(source.CompletionEventValues); target.RewardValues.AddRange(source.RewardValues);
        }
    }
}
