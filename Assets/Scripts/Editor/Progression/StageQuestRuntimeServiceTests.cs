using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Editor.Configs;
using GuildIdle.Player;
using NUnit.Framework;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Progression.Editor
{
    public sealed class StageQuestRuntimeServiceTests
    {
        private ConfigDatabase _database;

        [SetUp]
        public void SetUp()
        {
            _database = CreateDatabase();
            RuntimeConfigs.SetDatabaseForTests(_database);
        }

        [Test]
        public void NewGame_ActivatesRequiredBranchesAndTransitionsInEitherOrder()
        {
            var state = NewState();
            var runtime = NewRuntime(state);

            var started = runtime.Handle(new NewGame());
            Assert.That(started.Snapshot.ActiveQuests.Count, Is.EqualTo(2));
            Assert.That(FindQuest(started.Snapshot.ActiveQuests, "quest_build").Required, Is.True);
            Assert.That(FindQuest(started.Snapshot.ActiveQuests, "quest_clear").Required, Is.True);
            Assert.That(runtime.Handle(new NewGame()).ActivatedQuestIds, Is.Empty);

            var clear = runtime.Handle(new ActivityCompletedEvent("activity_combat"));
            Assert.That(clear.Snapshot.CurrentStage.RequiredProgressPercent, Is.EqualTo(50));
            Assert.That(clear.Transition.Occurred, Is.False);

            runtime.Handle(new ResourceQuantityChangedEvent("resource_wood", 2));
            var build = runtime.Handle(new BuildingLevelChangedEvent("building_hall", 1));

            Assert.That(build.Transition.Occurred, Is.True);
            Assert.That(build.Transition.FromStageId, Is.EqualTo("stage_alpha"));
            Assert.That(build.Transition.ToStageId, Is.EqualTo("stage_beta"));
            Assert.That(build.Snapshot.CurrentStage.StageId, Is.EqualTo("stage_beta"));
            Assert.That(build.Snapshot.CurrentStage.RequiredProgressPercent, Is.Zero);
            Assert.That(runtime.Handle(new ActivityCompletedEvent("activity_combat")).Transition.Occurred, Is.False);

            var reverseState = NewState();
            var reverse = NewRuntime(reverseState);
            reverse.Handle(new NewGame());
            reverse.Handle(new ResourceQuantityChanged("resource_wood", 2));
            var firstBranch = reverse.Handle(new BuildingLevelChanged("building_hall", 1));
            Assert.That(firstBranch.Snapshot.CurrentStage.RequiredProgressPercent, Is.EqualTo(50));
            Assert.That(reverse.Handle(new ActivityCompleted("activity_combat")).Transition.Occurred, Is.True);
        }

        [Test]
        public void ActivityFailed_ActivatesOptionalTutorialOnceAndUsesCurrentState()
        {
            var state = NewState();
            var runtime = NewRuntime(state);
            runtime.Handle(new ActivityCompleted("activity_combat"));
            Assert.That(state.GetQuestState("quest_tutorial"), Is.Null, "Victory must not emulate ActivityFailed.");
            runtime.Handle(new NewGameProgressionEvent());
            Assert.That(state.AddItem("consumable_meat", 1), Is.True);

            var failed = runtime.Handle(new ActivityFailedEvent("activity_combat"));
            var tutorial = FindQuest(failed.Snapshot.CompletedQuests, "quest_tutorial");

            Assert.That(tutorial, Is.Not.Null);
            Assert.That(tutorial.Optional, Is.True);
            Assert.That(tutorial.IsTutorial, Is.True);
            Assert.That(failed.Snapshot.CurrentStage.RequiredProgressPercent, Is.Zero);
            Assert.That(runtime.Handle(new ActivityFailedEvent("activity_combat")).ActivatedQuestIds, Is.Empty);
        }

        [Test]
        public void StateBackedSteps_ReconcileCurrentValuesAndLatchAfterCompletion()
        {
            var state = NewState();
            var runtime = NewRuntime(state);
            runtime.Handle(new ResourceQuantityChanged("resource_wood", 2));
            runtime.Handle(new NewGame());

            var beforeActivation = Array.Find(state.GetQuestState("quest_build").steps, step => step.stepId == "collect");
            Assert.That(beforeActivation.currentValue, Is.Zero, "Historical quantity events must not be replayed at activation.");

            runtime.Handle(new ResourceQuantityChanged("resource_wood", 2));
            runtime.Handle(new ResourceQuantityChanged("resource_wood", 0));
            var latched = state.GetQuestState("quest_build");
            Assert.That(Array.Find(latched.steps, step => step.stepId == "collect").completed, Is.True);
            Assert.That(Array.Find(latched.steps, step => step.stepId == "collect").currentValue, Is.EqualTo(2));

            var saved = state.ToSaveData();
            var collect = Array.Find(Array.Find(saved.quests, quest => quest.questId == "quest_build").steps, step => step.stepId == "collect");
            collect.currentValue = 0;
            collect.completed = false;
            var restored = TestPlayerComposition.CreatePlayerStateFactory(_database).Create(saved);
            Assert.That(restored.AddItem("resource_wood", 2), Is.True);

            NewRuntime(restored);
            var reconciled = Array.Find(restored.GetQuestState("quest_build").steps, step => step.stepId == "collect");
            Assert.That(reconciled.currentValue, Is.EqualTo(2));
            Assert.That(reconciled.completed, Is.True);
        }

        [Test]
        public void Initialization_ActivatesSatisfiedBuildingLevelConditionOnly()
        {
            var state = NewState();
            Assert.That(state.UnlockBuilding("building_hall"), Is.True);
            Assert.That(state.SetBuildingLevel("building_hall", 1), Is.True);
            Assert.That(state.AddItem("resource_stone", 1), Is.True);

            var store = new TestStore(state);
            var runtime = NewRuntime(store);
            var levelQuest = FindQuest(runtime.GetSnapshot().CompletedQuests, "quest_level");

            Assert.That(levelQuest, Is.Not.Null);
            Assert.That(store.SaveCount, Is.EqualTo(1));
            Assert.That(state.GetQuestState("quest_build"), Is.Null, "Initialization must not emulate NewGame.");
            Assert.That(state.GetQuestState("quest_tutorial"), Is.Null, "Initialization must not restore ActivityFailed.");
        }

        [Test]
        public void Initialization_DoesNotRestoreHistoricalStageEntered()
        {
            var state = TestPlayerComposition.CreatePlayerStateFactory(_database).Create(
                new SaveData { currentStageId = "stage_beta" });

            var runtime = NewRuntime(state);

            Assert.That(state.GetQuestState("quest_entered"), Is.Null);
            Assert.That(runtime.GetSnapshot().CurrentStage.RequiredProgressPercent, Is.Zero);
        }

        [Test]
        public void UnknownOptionalObjective_DoesNotBlockValidRequiredQuests()
        {
            _database = CreateDatabase(includeUnknownQuest: true);
            RuntimeConfigs.SetDatabaseForTests(_database);
            var state = NewState();
            var runtime = NewRuntime(state);

            var started = runtime.Handle(new NewGameProgressionEvent());
            Assert.That(HasIssue(started, "UnsupportedObjective", "quest_unknown"), Is.True);

            runtime.Handle(new ActivityCompletedEvent("activity_combat"));
            runtime.Handle(new ResourceQuantityChangedEvent("resource_wood", 2));
            var completed = runtime.Handle(new BuildingLevelChangedEvent("building_hall", 1));

            Assert.That(completed.Transition.Occurred, Is.True);
            Assert.That(state.GetQuestState("quest_unknown").completed, Is.False);
            Assert.That(state.GetQuestState("quest_unknown").rewardsGranted, Is.False);
        }

        [Test]
        public void RewardFailure_IsAtomicAndRetriesAfterLoadBeforeTransition()
        {
            _database = CreateDatabase(addRequiredQuestReward: true);
            RuntimeConfigs.SetDatabaseForTests(_database);
            var state = NewState();
            var failingStore = new TestStore(state) { FailQuestId = "quest_build" };
            var runtime = NewRuntime(failingStore);
            runtime.Handle(new NewGameProgressionEvent());
            runtime.Handle(new ActivityCompletedEvent("activity_combat"));
            runtime.Handle(new ResourceQuantityChangedEvent("resource_wood", 2));

            var failed = runtime.Handle(new BuildingLevelChangedEvent("building_hall", 1));

            Assert.That(failed.Snapshot.CurrentStage.RequiredProgressPercent, Is.EqualTo(100));
            Assert.That(failed.Transition.Occurred, Is.False);
            Assert.That(state.GetQuestState("quest_build").completed, Is.True);
            Assert.That(state.GetQuestState("quest_build").rewardsGranted, Is.False);
            Assert.That(state.GetItem("resource_wood"), Is.Zero);
            Assert.That(HasIssue(failed, "QuestRewardCommitFailed", "quest_build"), Is.True);

            var restored = TestPlayerComposition.CreatePlayerStateFactory(_database).Create(state.ToSaveData());
            var restoredRuntime = NewRuntime(restored);

            Assert.That(restored.CurrentStageId, Is.EqualTo("stage_beta"));
            Assert.That(restored.GetItem("resource_wood"), Is.EqualTo(2));
            Assert.That(restored.GetQuestState("quest_build").rewardsGranted, Is.True);
            NewRuntime(restored);
            Assert.That(restored.GetItem("resource_wood"), Is.EqualTo(2));
            Assert.That(restoredRuntime.GetSnapshot().CurrentStage.StageId, Is.EqualTo("stage_beta"));

            var eventRetryState = NewState();
            var eventRetryStore = new TestStore(eventRetryState) { FailQuestId = "quest_build" };
            var eventRetryRuntime = NewRuntime(eventRetryStore);
            eventRetryRuntime.Handle(new NewGame());
            eventRetryRuntime.Handle(new ActivityCompleted("activity_combat"));
            eventRetryRuntime.Handle(new ResourceQuantityChanged("resource_wood", 2));
            eventRetryRuntime.Handle(new BuildingLevelChanged("building_hall", 1));

            var retried = eventRetryRuntime.Handle(new ItemQuantityChanged("consumable_meat", 0));
            Assert.That(retried.Transition.Occurred, Is.True);
            Assert.That(eventRetryState.GetItem("resource_wood"), Is.EqualTo(2));
            eventRetryRuntime.Handle(new ItemQuantityChanged("consumable_meat", 0));
            Assert.That(eventRetryState.GetItem("resource_wood"), Is.EqualTo(2));
        }

        [Test]
        public void Transition_IsUpdateOnlyAndQuestStateSurvivesSaveLoad()
        {
            Assert.That(typeof(StageQuestSnapshot).GetProperty("Transition"), Is.Null);
            var state = NewState();
            var runtime = NewRuntime(state);
            runtime.Handle(new NewGameProgressionEvent());
            runtime.Handle(new ActivityCompletedEvent("activity_combat"));

            var restored = TestPlayerComposition.CreatePlayerStateFactory(_database).Create(state.ToSaveData());
            var snapshot = NewRuntime(restored).GetSnapshot();

            Assert.That(snapshot.CurrentStage.StageId, Is.EqualTo("stage_alpha"));
            Assert.That(FindQuest(snapshot.CompletedQuests, "quest_clear"), Is.Not.Null);
            Assert.That(FindQuest(snapshot.ActiveQuests, "quest_build"), Is.Not.Null);
        }

        [Test]
        public void Transition_DoesNotRepeatThroughSnapshotNextUpdateOrLoad()
        {
            var state = NewState();
            var runtime = NewRuntime(state);
            runtime.Handle(new NewGame());
            runtime.Handle(new ActivityCompleted("activity_combat"));
            runtime.Handle(new ResourceQuantityChanged("resource_wood", 2));
            var transitioned = runtime.Handle(new BuildingLevelChanged("building_hall", 1));

            Assert.That(transitioned.Transition.Occurred, Is.True);
            Assert.That(runtime.GetSnapshot().CurrentStage.StageId, Is.EqualTo("stage_beta"));
            Assert.That(runtime.Handle(new ItemQuantityChanged("consumable_meat", 0)).Transition.Occurred, Is.False);

            var restored = TestPlayerComposition.CreatePlayerStateFactory(_database).Create(state.ToSaveData());
            var restoredRuntime = NewRuntime(restored);
            Assert.That(restoredRuntime.GetSnapshot().CurrentStage.StageId, Is.EqualTo("stage_beta"));
            Assert.That(restoredRuntime.Handle(new ItemQuantityChanged("consumable_meat", 0)).Transition.Occurred, Is.False);
        }

        [Test]
        public void PlayerStateRewardBatch_RollsBackEveryMutationOnFailure()
        {
            var state = NewState();
            var mutations = new[]
            {
                new RewardMutation(RewardMutationKind.Item, "resource_wood", 2),
                new RewardMutation((RewardMutationKind)999, "invalid", 1)
            };

            var success = state.TryApplyRewardBatch(mutations, out var results, out _);

            Assert.That(success, Is.False);
            Assert.That(results, Is.Empty);
            Assert.That(state.GetItem("resource_wood"), Is.Zero);

            Assert.That(state.SetQuestState(new QuestSaveData { questId = "quest_build", completed = true }), Is.True);
            var quest = state.GetQuestState("quest_build");
            Assert.That(state.TryCommitQuestRewardBatch(quest, mutations, out _, out _), Is.False);
            Assert.That(state.GetItem("resource_wood"), Is.Zero);
            Assert.That(state.GetQuestState("quest_build").rewardsGranted, Is.False);
        }

        [Test]
        public void Snapshot_IsOrderedImmutableAndUpdatedFiresOnlyForChangedHandle()
        {
            var state = NewState();
            var runtime = NewRuntime(state);
            var updateCount = 0;
            runtime.Updated += _ => updateCount++;

            var unchanged = runtime.Handle(new ItemQuantityChanged("consumable_meat", 0));
            var changed = runtime.Handle(new NewGame());

            Assert.That(unchanged.Changed, Is.False);
            Assert.That(updateCount, Is.EqualTo(1));
            Assert.That(changed.Snapshot.ActiveQuests[0].QuestId, Is.EqualTo("quest_build"));
            Assert.That(changed.Snapshot.ActiveQuests[0].Steps[0].StepId, Is.EqualTo("collect"));
            Assert.That(changed.Snapshot.CurrentStage.Objectives[0].QuestId, Is.EqualTo("quest_build"));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<QuestSnapshot>)changed.Snapshot.ActiveQuests).Add(changed.Snapshot.ActiveQuests[0]));
        }

        private PlayerState NewState()
        {
            return TestPlayerComposition.CreatePlayerStateFactory(_database).Create(
                new SaveData { currentStageId = "stage_alpha" });
        }

        private StageQuestRuntimeService NewRuntime(PlayerState state)
        {
            return NewRuntime(new TestStore(state));
        }

        private StageQuestRuntimeService NewRuntime(TestStore store)
        {
            return new StageQuestRuntimeService(
                new RepositoryStageQuestConfigAdapter(_database.Activities, _database.Buildings),
                store,
                new FixedRandom());
        }

        private static QuestSnapshot FindQuest(IReadOnlyList<QuestSnapshot> quests, string questId)
        {
            foreach (var quest in quests)
            {
                if (quest.QuestId == questId)
                    return quest;
            }
            return null;
        }

        private static bool HasIssue(StageQuestUpdate update, string code, string questId)
        {
            foreach (var issue in update.Issues)
            {
                if (issue.Code == code && issue.QuestId == questId)
                    return true;
            }
            return false;
        }

        private static ConfigDatabase CreateDatabase(bool includeUnknownQuest = false, bool addRequiredQuestReward = false)
        {
            var quests = new List<QuestConfigDto>
            {
                Quest("quest_build", 10),
                Quest("quest_clear", 20),
                Quest("quest_tutorial", 30),
                Quest("quest_level", 40),
                Quest("quest_entered", 50)
            };
            var conditions = new List<QuestStartConditionConfigDto>
            {
                Condition("quest_build", "NewGame", null, 1),
                Condition("quest_clear", "NewGame", null, 1),
                Condition("quest_tutorial", "ActivityFailed", "activity_combat", 1),
                Condition("quest_level", "BuildingLevel", "building_hall", 1),
                Condition("quest_entered", "StageEntered", "stage_beta", 1)
            };
            var steps = new List<QuestStepConfigDto>
            {
                Step("quest_build", "collect", 10, "ResourceCount", "resource_wood", 2),
                Step("quest_build", "build", 20, "BuildingLevel", "building_hall", 1),
                Step("quest_clear", "clear", 10, "ActivityCompleted", "activity_combat", 1),
                Step("quest_tutorial", "prepare", 10, "ItemCount", "consumable_meat", 1),
                Step("quest_level", "stock", 10, "ResourceCount", "resource_stone", 1),
                Step("quest_entered", "entered", 10, "BuildingLevel", "building_hall", 1)
            };
            if (includeUnknownQuest)
            {
                quests.Add(Quest("quest_unknown", 50));
                conditions.Add(Condition("quest_unknown", "NewGame", null, 1));
                steps.Add(Step("quest_unknown", "unknown", 10, "UnknownObjective", "unknown", 1));
            }

            var rewards = addRequiredQuestReward
                ? new[]
                {
                    new QuestRewardConfigDto
                    {
                        questId = "quest_build",
                        rewardType = "Resource",
                        targetId = "resource_wood",
                        min = 2,
                        max = 2,
                        grantMoment = "OnComplete"
                    }
                }
                : Array.Empty<QuestRewardConfigDto>();

            return new ConfigDatabase(
                new ItemsRuntimeConfigDto
                {
                    resources = new[]
                    {
                        new ResourceConfigDto { id = "resource_wood", kind = "resource" },
                        new ResourceConfigDto { id = "resource_stone", kind = "resource" }
                    },
                    consumables = new[] { new ConsumableConfigDto { id = "consumable_meat", kind = "consumable" } }
                },
                new HeroesRuntimeConfigDto(),
                new ActivitiesRuntimeConfigDto
                {
                    activities = new[] { new ActivityConfigDto { id = "activity_combat", type = "Combat" } },
                    quests = quests.ToArray(),
                    questStartConditions = conditions.ToArray(),
                    questSteps = steps.ToArray(),
                    questRewards = rewards
                },
                new BuildingsRuntimeConfigDto
                {
                    buildings = new[] { new BuildingConfigDto { buildingId = "building_hall", levels = 1 } },
                    buildingLevels = new[]
                    {
                        new BuildingLevelConfigDto { buildingId = "building_hall", level = 0 },
                        new BuildingLevelConfigDto { buildingId = "building_hall", level = 1 }
                    },
                    settlementStages = new[]
                    {
                        new SettlementStageConfigDto { stageId = "stage_alpha", completionRule = "AllRequired", nextStageId = "stage_beta", enabled = true },
                        new SettlementStageConfigDto { stageId = "stage_beta", completionRule = "AllRequired", enabled = true }
                    },
                    settlementStageObjectives = new[]
                    {
                        new SettlementStageObjectiveConfigDto { stageId = "stage_alpha", questId = "quest_build", weightPercent = 50, required = true, sortOrder = 10 },
                        new SettlementStageObjectiveConfigDto { stageId = "stage_alpha", questId = "quest_clear", weightPercent = 50, required = true, sortOrder = 20 },
                        new SettlementStageObjectiveConfigDto { stageId = "stage_alpha", questId = "quest_tutorial", weightPercent = 40, required = false, sortOrder = 30 }
                    }
                },
                null, null, null, null, null, null);
        }

        private static QuestConfigDto Quest(string id, int order) =>
            new QuestConfigDto { questId = id, sortOrder = order, isTutorial = true, enabled = true };

        private static QuestStartConditionConfigDto Condition(string questId, string type, string target, int value) =>
            new QuestStartConditionConfigDto { questId = questId, conditionType = type, targetId = target, value = value };

        private static QuestStepConfigDto Step(string questId, string stepId, int order, string type, string target, int value) =>
            new QuestStepConfigDto { questId = questId, stepId = stepId, stepOrder = order, objectiveType = type, targetId = target, targetValue = value, required = true };

        private sealed class TestStore : IStageQuestRuntimeStore
        {
            private readonly PlayerState _state;

            public TestStore(PlayerState state)
            {
                _state = state;
            }

            public string FailQuestId { get; set; }
            public int SaveCount { get; private set; }
            public string CurrentStageId => _state.CurrentStageId;
            public bool SetCurrentStage(string stageId) => _state.SetCurrentStage(stageId);
            public QuestSaveData GetQuestState(string questId) => _state.GetQuestState(questId);
            public QuestSaveData[] GetQuestStates() => _state.GetQuestStates();
            public bool SetQuestState(QuestSaveData quest) => _state.SetQuestState(quest);
            public int GetItem(string itemId) => _state.GetItem(itemId);
            public int GetBuildingLevel(string buildingId) => _state.GetBuildingLevel(buildingId);
            public bool IsActivityCompleted(string activityId) => _state.IsActivityCompleted(activityId);
            public bool Save()
            {
                SaveCount++;
                return true;
            }

            public bool TryCommitQuestRewardBatch(QuestSaveData quest, RewardMutation[] mutations, out RewardMutationResult[] results, out string error)
            {
                if (quest?.questId == FailQuestId)
                {
                    FailQuestId = null;
                    results = Array.Empty<RewardMutationResult>();
                    error = "Injected atomic reward failure.";
                    return false;
                }
                return _state.TryCommitQuestRewardBatch(quest, mutations, out results, out error);
            }
        }

        private sealed class FixedRandom : GuildIdle.Activities.IActivityRandom
        {
            public int RangeInclusive(int min, int max) => min;
            public float Percent() => 0f;
        }
    }
}
