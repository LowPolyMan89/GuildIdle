using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Player;
using NUnit.Framework;

namespace GuildIdle.Progression.Editor
{
    public sealed class ProgressionRuntimeServiceTests
    {
        [Test]
        public void LeafServicesNeverSave()
        {
            var store = new TestStore("stage_1");
            var configs = Configs(
                stories: new[] { Story("quest_a") },
                conditions: new[] { Condition("quest_a", "NewGame") },
                steps: new[] { Step("quest_a", "collect", "ResourceCount", "wood", 1) },
                stages: TwoStages(),
                stageQuests: new[] { StageQuest("stage_1", "quest_a") });
            var questRuntime = new QuestRuntimeService(configs, store);
            var stageRuntime = new StageProgressionService(configs, store);

            questRuntime.Handle(new NewGame());
            questRuntime.Handle(new ResourceQuantityChanged("wood", 1));
            stageRuntime.TryTransition();

            Assert.That(store.SaveCalls, Is.Zero);
        }

        [Test]
        public void RuntimeEventRefreshesStateBackedResourceStepWithoutReinitialization()
        {
            var store = new TestStore("stage_1");
            var configs = Configs(
                stories: new[] { Story("quest_a") },
                conditions: new[] { Condition("quest_a", "NewGame") },
                steps: new[] { Step("quest_a", "collect", "ResourceCount", "wood", 2) },
                stages: new[] { Stage("stage_1", null) });
            var runtime = Runtime(configs, store);
            runtime.Handle(new NewGame());

            store.SetItem("wood", 1);
            var update = runtime.Handle(new ActivityCompleted("unrelated_activity"));

            var step = update.QuestSnapshot.ActiveInstances[0].Steps[0];
            Assert.That(step.CurrentValue, Is.EqualTo(1));
            Assert.That(step.Completed, Is.False);
        }

        [Test]
        public void CoordinatorSavesOnceAfterCompletionQueueAndSingleTransition()
        {
            var store = new TestStore("stage_1");
            var configs = Configs(
                stories: new[] { Story("quest_a"), Story("quest_b", 20) },
                conditions: new[]
                {
                    Condition("quest_a", "NewGame"),
                    Condition("quest_b", "QuestCompleted", "quest_a")
                },
                steps: new[] { Step("quest_a", "collect", "ResourceCount", "wood", 1) },
                stages: new[]
                {
                    Stage("stage_1", "stage_2"), Stage("stage_2", "stage_3"), Stage("stage_3", null)
                },
                stageQuests: new[]
                {
                    StageQuest("stage_1", "quest_a"), StageQuest("stage_2", "quest_b")
                });
            var runtime = Runtime(configs, store);
            runtime.Handle(new NewGame());
            store.SaveCalls = 0;

            var update = runtime.Handle(new ResourceQuantityChanged("wood", 1));

            Assert.That(store.SaveCalls, Is.EqualTo(1));
            Assert.That(update.PublishedQuestCompletedEvents, Has.Count.EqualTo(2));
            Assert.That(update.Transition.Occurred, Is.True);
            Assert.That(update.Transition.ToStageId, Is.EqualTo("stage_2"));
            Assert.That(store.CurrentStageId, Is.EqualTo("stage_2"), "A second transition is forbidden in the same transaction.");
        }

        [Test]
        public void InvalidConditionBlocksOnlyItsDefinition()
        {
            var store = new TestStore("stage_1");
            var configs = Configs(
                stories: new[] { Story("bad"), Story("good", 20) },
                conditions: new[]
                {
                    Condition("bad", "Unsupported"), Condition("good", "NewGame")
                },
                stages: new[] { Stage("stage_1", null) });
            var runtime = Runtime(configs, store);
            var update = runtime.Handle(new NewGame());

            Assert.That(store.GetQuestInstance("story:bad"), Is.Null);
            Assert.That(store.GetQuestInstance("story:good"), Is.Not.Null);
            Assert.That(update.Issues, Has.Some.Property("Code").EqualTo("InvalidQuestStartCondition"));
        }

        [Test]
        public void UnsupportedOptionalStepDoesNotBlockButRequiredStepDoes()
        {
            var store = new TestStore("stage_1");
            var configs = Configs(
                stories: new[] { Story("optional"), Story("required", 20) },
                conditions: new[] { Condition("optional", "NewGame"), Condition("required", "NewGame") },
                steps: new[]
                {
                    Step("optional", "unknown_optional", "Unsupported", "x", 1, false),
                    Step("required", "unknown_required", "Unsupported", "x", 1, true)
                },
                stages: new[] { Stage("stage_1", null) });

            var runtime = Runtime(configs, store);
            var update = runtime.Handle(new NewGame());

            Assert.That(store.GetQuestInstance("story:optional").status, Is.EqualTo(QuestInstanceStatus.Completed));
            Assert.That(store.GetQuestInstance("story:required").status, Is.EqualTo(QuestInstanceStatus.Active));
            Assert.That(update.Issues, Has.Count.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void ExistingActiveDailyInstanceUsesSameProgressionAndRewards()
        {
            var store = new TestStore("stage_1");
            store.SetQuestInstance(QuestStateBuilder.Create("daily:cycle_1:daily_a", "daily_a", "cycle_1", new[]
            {
                Step("daily_a", "activity", "ActivityCompleted", "activity_a", 1)
            }));
            var configs = Configs(
                dailies: new[] { Daily("daily_a") },
                steps: new[] { Step("daily_a", "activity", "ActivityCompleted", "activity_a", 1) },
                stages: new[] { Stage("stage_1", null) });

            Runtime(configs, store).Handle(new ActivityCompleted("activity_a"));

            var instance = store.GetQuestInstance("daily:cycle_1:daily_a");
            Assert.That(instance.status, Is.EqualTo(QuestInstanceStatus.Completed));
            Assert.That(instance.rewardsGranted, Is.True);
        }

        [Test]
        public void SaveFailureIsReportedAfterSnapshotsAreProduced()
        {
            var store = new TestStore("stage_1") { SaveSucceeds = false };
            var configs = Configs(
                stories: new[] { Story("quest_a") },
                conditions: new[] { Condition("quest_a", "NewGame") },
                stages: new[] { Stage("stage_1", null) });

            var runtime = Runtime(configs, store);
            var update = runtime.Handle(new NewGame());

            Assert.That(update.Saved, Is.False);
            Assert.That(update.QuestSnapshot.CompletedInstances, Has.Count.EqualTo(1));
            Assert.That(update.Issues, Has.Some.Property("Code").EqualTo("ProgressionSaveFailed"));
            Assert.That(store.SaveCalls, Is.EqualTo(1));

            store.SaveSucceeds = true;
            var retry = runtime.Handle(new ActivityFailed("unrelated"));
            Assert.That(retry.Saved, Is.True);
            Assert.That(store.SaveCalls, Is.EqualTo(2));
        }

        [Test]
        public void ResolvedActivityResultPublishesActivityCompletedIntoQuestRuntime()
        {
            var store = new TestStore("stage_1");
            var configs = Configs(
                stories: new[] { Story("quest_activity") },
                conditions: new[] { Condition("quest_activity", "NewGame") },
                steps: new[] { Step("quest_activity", "complete_activity", "ActivityCompleted", "activity_a", 1) },
                stages: new[] { Stage("stage_1", null) });
            var runtime = Runtime(configs, store);
            runtime.Handle(new NewGame());
            ProgressionRuntimeUpdate update = null;
            runtime.Updated += value => update = value;

            ((TestPendingResults)store.PendingResults).RaiseResolved(new PendingResultResolvedEvent
            {
                SourceType = PendingResultSourceType.Activity,
                SourceId = "activity_a",
                SourceExecutionId = "activity-execution-a",
                ResultId = "result:Activity:activity-execution-a",
                SourceCompleted = true
            });

            Assert.That(store.GetQuestInstance("story:quest_activity").status, Is.EqualTo(QuestInstanceStatus.Completed));
            Assert.That(store.GetQuestInstance("story:quest_activity").rewardsGranted, Is.True);
            Assert.That(update, Is.Not.Null);
            Assert.That(update.CompletedInstanceIds, Has.Member("story:quest_activity"));
        }

        [Test]
        public void ActivityCompletionAppliesConfiguredNonBuildLevelTransition()
        {
            var store = new TestStore("stage_1");
            var configs = Configs(stages: new[] { Stage("stage_1", null) });
            var runtime = new ProgressionRuntimeService(
                new QuestRuntimeService(configs, store),
                new StageProgressionService(configs, store),
                store,
                new TestTransitionProvider(new BuildingLevelConfigDto
                {
                    buildingId = "building_underwood",
                    level = 1,
                    sourceActivityId = "combat_clear",
                    buildFormulaId = string.Empty
                }));

            ((TestPendingResults)store.PendingResults).RaiseResolved(new PendingResultResolvedEvent
            {
                SourceType = PendingResultSourceType.Activity,
                SourceId = "combat_clear",
                SourceCompleted = true
            });

            Assert.That(store.GetBuildingLevel("building_underwood"), Is.EqualTo(1));
        }

        private static ProgressionRuntimeService Runtime(RepositoryProgressionConfigAdapter configs, TestStore store)
        {
            return new ProgressionRuntimeService(
                new QuestRuntimeService(configs, store),
                new StageProgressionService(configs, store),
                store);
        }

        private static RepositoryProgressionConfigAdapter Configs(
            StoryQuestConfigDto[] stories = null,
            DailyQuestConfigDto[] dailies = null,
            QuestStartConditionConfigDto[] conditions = null,
            QuestStepConfigDto[] steps = null,
            StageConfigDto[] stages = null,
            StageQuestConfigDto[] stageQuests = null)
        {
            return new RepositoryProgressionConfigAdapter(new QuestConfigRepository(new QuestRuntimeConfigDto
            {
                storyQuests = stories ?? Array.Empty<StoryQuestConfigDto>(),
                dailyQuests = dailies ?? Array.Empty<DailyQuestConfigDto>(),
                questStartConditions = conditions ?? Array.Empty<QuestStartConditionConfigDto>(),
                questSteps = steps ?? Array.Empty<QuestStepConfigDto>(),
                stages = stages ?? Array.Empty<StageConfigDto>(),
                stageQuests = stageQuests ?? Array.Empty<StageQuestConfigDto>()
            }));
        }

        private static StoryQuestConfigDto Story(string id, int order = 10) =>
            new StoryQuestConfigDto { questId = id, sortOrder = order, enabled = true };

        private static DailyQuestConfigDto Daily(string id) =>
            new DailyQuestConfigDto { questId = id, dailyPoolId = "pool", selectionWeight = 1, enabled = true };

        private static QuestStartConditionConfigDto Condition(string questId, string type, string targetId = null) =>
            new QuestStartConditionConfigDto
            {
                questId = questId, conditionGroup = "default", conditionType = type, targetId = targetId,
                compareOperator = "GreaterOrEqual", value = 1
            };

        private static QuestStepConfigDto Step(string questId, string stepId, string type, string targetId, int target, bool required = true) =>
            new QuestStepConfigDto
            {
                questId = questId, stepId = stepId, objectiveType = type, targetId = targetId,
                compareOperator = "GreaterOrEqual", targetValue = target, required = required
            };

        private static StageConfigDto Stage(string id, string next) =>
            new StageConfigDto { stageId = id, completionRule = "AllRequired", nextStageId = next, enabled = true };

        private static StageConfigDto[] TwoStages() => new[] { Stage("stage_1", "stage_2"), Stage("stage_2", null) };

        private static StageQuestConfigDto StageQuest(string stageId, string questId) =>
            new StageQuestConfigDto { stageId = stageId, questId = questId, weightPercent = 100, required = true, showInStageUi = true, enabled = true };

        private sealed class TestStore : IProgressionRuntimeStore
        {
            private readonly Dictionary<string, QuestInstanceSaveData> _instances = new Dictionary<string, QuestInstanceSaveData>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _items = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _buildings = new Dictionary<string, int>(StringComparer.Ordinal);
            public TestStore(string stageId) { CurrentStageId = stageId; PendingResults = new TestPendingResults(this); }
            public string CurrentStageId { get; private set; }
            public IPendingResultService PendingResults { get; }
            public int SaveCalls { get; set; }
            public bool SaveSucceeds { get; set; } = true;
            public bool SetCurrentStage(string stageId) { if (CurrentStageId == stageId) return false; CurrentStageId = stageId; return true; }
            public QuestInstanceSaveData GetQuestInstance(string instanceId) => _instances.TryGetValue(instanceId, out var value) ? value : null;
            public QuestInstanceSaveData[] GetQuestInstances() { var values = new QuestInstanceSaveData[_instances.Count]; _instances.Values.CopyTo(values, 0); return values; }
            public bool SetQuestInstance(QuestInstanceSaveData instance) { if (instance == null || string.IsNullOrWhiteSpace(instance.instanceId)) return false; _instances[instance.instanceId] = instance; return true; }
            public int GetItem(string itemId) => _items.TryGetValue(itemId, out var value) ? value : 0;
            public void SetItem(string itemId, int value) => _items[itemId] = value;
            public int GetBuildingLevel(string buildingId) => _buildings.TryGetValue(buildingId, out var value) ? value : 0;
            public bool SetBuildingLevel(string buildingId, int level) { _buildings[buildingId] = level; return true; }
            public bool IsActivityCompleted(string activityId) => false;
            public bool Save() { SaveCalls++; return SaveSucceeds; }
        }

        private sealed class TestPendingResults : IPendingResultService
        {
            private readonly TestStore _store;
            private readonly Dictionary<string, PendingResultSaveData> _results = new Dictionary<string, PendingResultSaveData>(StringComparer.Ordinal);
            public TestPendingResults(TestStore store) { _store = store; }
            public event Action<PendingResultResolvedEvent> Resolved;
            public void RegisterSourceHandler(IPendingResultSourceHandler handler) { }
            public void RaiseResolved(PendingResultResolvedEvent value) => Resolved?.Invoke(value);
            public PendingResultSaveData Get(string resultId) => _results.TryGetValue(resultId, out var value) ? value : null;
            public PendingResultSaveData[] GetAll() { var values = new PendingResultSaveData[_results.Count]; _results.Values.CopyTo(values, 0); return values; }
            public PendingResultSaveData[] GetSaveData() => GetAll();
            public void Load(PendingResultSaveData[] results) { _results.Clear(); foreach (var result in results ?? Array.Empty<PendingResultSaveData>()) if (result != null) _results[result.resultId] = result; }
            public PendingResultFormationResult CreateOrAppend(string operationId, PendingResultDraft draft, bool makeClaimable, long expectedResultRevision = 0)
            {
                var resultId = $"result:{draft.SourceType}:{draft.SourceExecutionId}";
                var entries = new List<PendingResultEntrySaveData>();
                foreach (var entry in draft.Entries ?? Array.Empty<PendingResultEntryDraft>())
                    if (entry != null && entry.Quantity > 0) entries.Add(new PendingResultEntrySaveData { entryId = Guid.NewGuid().ToString("N"), sortOrder = entry.SortOrder, rewardType = entry.RewardType, targetId = entry.TargetId, quantity = entry.Quantity, origin = entry.Origin });
                if (entries.Count == 0 && makeClaimable)
                {
                    var quest = _store.GetQuestInstance(draft.SourceExecutionId);
                    quest.status = QuestInstanceStatus.Completed; quest.rewardsGranted = true; quest.pendingResultId = null; _store.SetQuestInstance(quest);
                    return new PendingResultFormationResult { Success = true, ResolvedImmediately = true };
                }
                var result = new PendingResultSaveData { resultId = resultId, sourceType = draft.SourceType, sourceId = draft.SourceId, sourceExecutionId = draft.SourceExecutionId, revision = 1, entries = entries.ToArray() };
                _results[resultId] = result;
                var pending = _store.GetQuestInstance(draft.SourceExecutionId);
                if (pending != null) { pending.status = QuestInstanceStatus.RewardPending; pending.pendingResultId = resultId; _store.SetQuestInstance(pending); }
                return new PendingResultFormationResult { Success = true, Result = result };
            }
            public PendingResultFormationResult CreateCombatResult(string operationId, PendingResultDraft calculatedResult, string broughtStackId, StorageActionContext combatContext, long expectedStorageRevision) => CreateOrAppend(operationId, calculatedResult, true);
            public PendingResultMutationResult ClaimAll(string operationId, string resultId, long expectedResultRevision, long expectedStorageRevision) => Unsupported();
            public PendingResultMutationResult ClaimAvailable(string operationId, string resultId, long expectedResultRevision, long expectedStorageRevision) => Unsupported();
            public PendingResultMutationResult ClaimQuantity(string operationId, string resultId, string entryId, long quantity, long expectedResultRevision, long expectedStorageRevision) => Unsupported();
            public PendingResultMutationResult DiscardAll(string operationId, string resultId, long expectedResultRevision) => Unsupported();
            public PendingResultMutationResult DiscardQuantity(string operationId, string resultId, string entryId, long quantity, long expectedResultRevision) => Unsupported();
            private static PendingResultMutationResult Unsupported() => new PendingResultMutationResult { Success = false, Code = "Unsupported" };
        }

        private sealed class TestTransitionProvider : INonBuildTransitionProvider
        {
            private readonly BuildingLevelConfigDto[] _levels;
            public TestTransitionProvider(params BuildingLevelConfigDto[] levels) { _levels = levels; }
            public BuildingLevelConfigDto[] GetLevelsBySourceActivity(string activityId) => _levels;
        }
    }
}
