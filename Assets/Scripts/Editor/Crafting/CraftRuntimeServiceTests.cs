using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Crafting;
using GuildIdle.Editor.Configs;
using GuildIdle.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Editor.Crafting
{
    public sealed class CraftRuntimeServiceTests
    {
        private ConfigDatabase _database;
        private PlayerStateFactory _factory;
        private MemorySaveStorage _storage;

        [SetUp]
        public void SetUp()
        {
            _database = CreateDatabase();
            RuntimeConfigs.SetDatabaseForTests(_database);
            _factory = TestPlayerComposition.CreatePlayerStateFactory(_database);
            _storage = new MemorySaveStorage();
        }

        [Test]
        public void StartWithoutRecipeAggregatesMaterialsAcrossStorageStacksAndPersistsSnapshot()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 5);
            Seed(state, "resource_herb", 2);
            var fatigue = state.GetHeroFatigue("ren");
            var saves = _storage.SaveCalls;
            var events = new List<CraftStartedEvent>();
            var runtime = Runtime(state, events.Add);

            var result = runtime.Start(Request("craft_basic", "op-basic"));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Code, Is.EqualTo(CraftStartCode.Applied));
            Assert.That(result.Execution.status, Is.EqualTo(CraftExecutionStatus.Running));
            Assert.That(result.Execution.progressSeconds, Is.Zero);
            Assert.That(result.Execution.durationSeconds, Is.EqualTo(10));
            Assert.That(result.Execution.outputItemId, Is.EqualTo("consumable_roasted_rabbit_meat"));
            Assert.That(result.Execution.outputCount, Is.EqualTo(1));
            Assert.That(result.Execution.skillId, Is.EqualTo("skill_crafting"));
            Assert.That(result.Execution.skillExp, Is.EqualTo(2));
            Assert.That(result.Execution.fatigueCostPaid, Is.EqualTo(2));
            Assert.That(result.Execution.costsPaid, Is.True);
            Assert.That(result.Execution.paidCosts, Has.Length.EqualTo(2));
            Assert.That(FindCost(result.Execution, "resource_rabbit_meat").quantity, Is.EqualTo(3));
            Assert.That(state.GetItem("resource_rabbit_meat"), Is.EqualTo(2));
            Assert.That(state.GetItem("resource_herb"), Is.EqualTo(1));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue - 2));
            Assert.That(state.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo(result.ExecutionId));
            Assert.That(state.GetCraftExecutions(), Has.Length.EqualTo(1));
            Assert.That(_storage.SaveCalls, Is.EqualTo(saves + 1));
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(HasStartReceipt(state, "op-basic", result.ExecutionId), Is.True);
            Assert.Throws<NotSupportedException>(() => ((IList<CraftCostDescriptor>)result.Descriptor.PaidCosts).Clear());
            var mutatedSnapshot = state.GetCraftExecution(result.ExecutionId);
            mutatedSnapshot.durationSeconds++;
            Assert.That(state.UpdateCraftExecution(mutatedSnapshot), Is.False);
        }

        [Test]
        public void NonConsumableRecipeIsRequiredButRemainsInStorage()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 1);
            Seed(state, "recipe_roasting", 1);

            var result = Runtime(state).Start(Request("craft_recipe_kept", "op-recipe-kept"));

            Assert.That(result.Success, Is.True);
            Assert.That(state.GetItem("resource_rabbit_meat"), Is.Zero);
            Assert.That(state.GetItem("recipe_roasting"), Is.EqualTo(1));
            Assert.That(result.Execution.recipe.requiredItemId, Is.EqualTo("recipe_roasting"));
            Assert.That(result.Execution.recipe.requiredCount, Is.EqualTo(1));
            Assert.That(result.Execution.recipe.consume, Is.False);
            Assert.That(result.Execution.recipe.consumedCount, Is.Zero);
            Assert.That(FindCost(result.Execution, "recipe_roasting"), Is.Null);
        }

        [Test]
        public void ConsumableRecipeIsAggregatedIntoPaidBatchAndRemoved()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 1);
            Seed(state, "recipe_roasting", 2);

            var result = Runtime(state).Start(Request("craft_recipe_consumed", "op-recipe-consumed"));

            Assert.That(result.Success, Is.True);
            Assert.That(state.GetItem("recipe_roasting"), Is.Zero);
            Assert.That(FindCost(result.Execution, "recipe_roasting").quantity, Is.EqualTo(2));
            Assert.That(FindCost(result.Execution, "recipe_roasting").kind, Is.EqualTo(CraftPaidCostKind.Recipe));
            Assert.That(result.Execution.recipe.consumedCount, Is.EqualTo(2));
        }

        [Test]
        public void FailureOnLastRemovalRollsBackWholeCostBatch()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 3);
            Seed(state, "resource_herb", 1);
            var fatigue = state.GetHeroFatigue("ren");
            var fault = new FaultInjectingCraftState(new PlayerStateCraftAdapter(state)) { FailConsumeCall = 2 };

            var result = new CraftRuntimeService(_database.Crafts, fault).Start(Request("craft_basic", "op-last-removal"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(CraftStartCode.TransactionFailure));
            Assert.That(state.GetItem("resource_rabbit_meat"), Is.EqualTo(3));
            Assert.That(state.GetItem("resource_herb"), Is.EqualTo(1));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.GetCraftExecutions(), Is.Empty);
            Assert.That(HasStartReceipt(state, "op-last-removal", null), Is.False);
        }

        [TestCase("occupation")]
        [TestCase("execution")]
        public void OccupationOrExecutionCreationFailureRestoresItemsAndFatigue(string failure)
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 3);
            Seed(state, "resource_herb", 1);
            var fatigue = state.GetHeroFatigue("ren");
            var fault = new FaultInjectingCraftState(new PlayerStateCraftAdapter(state))
            {
                FailOccupation = failure == "occupation",
                FailAddExecution = failure == "execution"
            };

            var result = new CraftRuntimeService(_database.Crafts, fault).Start(Request("craft_basic", $"op-{failure}-failure"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(CraftStartCode.TransactionFailure));
            Assert.That(state.GetItem("resource_rabbit_meat"), Is.EqualTo(3));
            Assert.That(state.GetItem("resource_herb"), Is.EqualTo(1));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.GetCraftExecutions(), Is.Empty);
        }

        [Test]
        public void SaveFailureRestoresStorageFatigueOccupationExecutionReceiptAndSuppressesEvent()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 3);
            Seed(state, "resource_herb", 1);
            var fatigue = state.GetHeroFatigue("ren");
            var events = new List<CraftStartedEvent>();
            _storage.ThrowOnSet = true;
            LogAssert.Expect(LogType.Error, "[SaveService] Failed to save player state. simulated save failure");

            var result = Runtime(state, events.Add).Start(Request("craft_basic", "op-save-failure"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(CraftStartCode.SaveFailure));
            Assert.That(state.GetItem("resource_rabbit_meat"), Is.EqualTo(3));
            Assert.That(state.GetItem("resource_herb"), Is.EqualTo(1));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.GetCraftExecutions(), Is.Empty);
            Assert.That(HasStartReceipt(state, "op-save-failure", null), Is.False);
            Assert.That(events, Is.Empty);
        }

        [Test]
        public void SameOperationKeyAndPayloadReplaysWithoutSecondExecutionOrCosts()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 5);
            Seed(state, "resource_herb", 2);
            var runtime = Runtime(state);
            var request = Request("craft_basic", "op-replay");
            var first = runtime.Start(request);
            var meat = state.GetItem("resource_rabbit_meat");
            var herb = state.GetItem("resource_herb");
            var fatigue = state.GetHeroFatigue("ren");
            var saves = _storage.SaveCalls;

            var replay = runtime.Start(request);

            Assert.That(first.Success, Is.True);
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.Code, Is.EqualTo(CraftStartCode.Replayed));
            Assert.That(replay.ExecutionId, Is.EqualTo(first.ExecutionId));
            Assert.That(state.GetCraftExecutions(), Has.Length.EqualTo(1));
            Assert.That(state.GetItem("resource_rabbit_meat"), Is.EqualTo(meat));
            Assert.That(state.GetItem("resource_herb"), Is.EqualTo(herb));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
            Assert.That(_storage.SaveCalls, Is.EqualTo(saves));
        }

        [Test]
        public void ReusedOperationKeyWithDifferentPayloadIsConflictBeforeOtherChecks()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 5);
            Seed(state, "resource_herb", 5);
            var runtime = Runtime(state);
            var first = runtime.Start(Request("craft_basic", "op-conflict"));
            var items = state.GetItem("resource_herb");
            var fatigue = state.GetHeroFatigue("ren");

            var conflict = runtime.Start(Request("craft_other", "op-conflict"));

            Assert.That(first.Success, Is.True);
            Assert.That(conflict.Success, Is.False);
            Assert.That(conflict.Code, Is.EqualTo(CraftStartCode.OperationReplayConflict));
            Assert.That(state.GetCraftExecutions(), Has.Length.EqualTo(1));
            Assert.That(state.GetItem("resource_herb"), Is.EqualTo(items));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
        }

        [Test]
        public void RunningExecutionSurvivesSaveLoadWithoutRepaymentAndKeepsImmutableSnapshot()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 5);
            Seed(state, "resource_herb", 2);
            var started = Runtime(state).Start(Request("craft_basic", "op-roundtrip"));
            var meatAfterStart = state.GetItem("resource_rabbit_meat");
            var fatigueAfterStart = state.GetHeroFatigue("ren");

            var source = FindDefinition("craft_basic");
            source.craftDurationSec = 999;
            source.targetItemId = "consumable_other";
            source.outputCount = 7;
            source.skillExp = 99;

            var loaded = SaveService.Load(_factory, _storage);
            var execution = loaded.GetCraftExecution(started.ExecutionId);

            Assert.That(execution, Is.Not.Null);
            Assert.That(execution.status, Is.EqualTo(CraftExecutionStatus.Running));
            Assert.That(execution.durationSeconds, Is.EqualTo(10));
            Assert.That(execution.outputItemId, Is.EqualTo("consumable_roasted_rabbit_meat"));
            Assert.That(execution.outputCount, Is.EqualTo(1));
            Assert.That(execution.skillExp, Is.EqualTo(2));
            Assert.That(loaded.GetItem("resource_rabbit_meat"), Is.EqualTo(meatAfterStart));
            Assert.That(loaded.GetHeroFatigue("ren"), Is.EqualTo(fatigueAfterStart));
            Assert.That(loaded.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo(started.ExecutionId));

            var replay = Runtime(loaded).Start(Request("craft_basic", "op-roundtrip"));
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.ExecutionId, Is.EqualTo(started.ExecutionId));
            Assert.That(loaded.GetItem("resource_rabbit_meat"), Is.EqualTo(meatAfterStart));
            Assert.That(loaded.GetHeroFatigue("ren"), Is.EqualTo(fatigueAfterStart));
        }

        [Test]
        public void BusyLimitFatigueAndUnknownCraftChecksDoNotMutateState()
        {
            var unknownState = NewState();
            AssertBlockedWithoutMutation(unknownState, Request("missing", "op-unknown"), CraftStartCode.UnknownOrDisabledCraft);

            var busyState = NewState();
            Assert.That(busyState.SetHeroBusy("ren", "foreign-execution"), Is.True);
            AssertBlockedWithoutMutation(busyState, Request("craft_basic", "op-busy"), CraftStartCode.HeroBusy);

            var fatigueState = NewState();
            Seed(fatigueState, "resource_rabbit_meat", 3);
            Seed(fatigueState, "resource_herb", 1);
            var currentFatigue = fatigueState.GetHeroFatigue("ren");
            Assert.That(fatigueState.SpendHeroFatigue("ren", currentFatigue - 1), Is.True);
            AssertBlockedWithoutMutation(fatigueState, Request("craft_basic", "op-fatigue"), CraftStartCode.InsufficientFatigue);

            var limitState = NewState();
            Assert.That(limitState.AddHero("aska"), Is.True);
            Assert.That(limitState.SetHeroBusy("ren", "foreign-execution"), Is.True);
            Seed(limitState, "resource_rabbit_meat", 3);
            Seed(limitState, "resource_herb", 1);
            AssertBlockedWithoutMutation(limitState, Request("craft_basic", "op-limit", "aska"), CraftStartCode.ActiveHeroLimitReached);
        }

        [Test]
        public void StationAdditionalRequirementAndRecipeFailuresAreTypedAndReadOnly()
        {
            var station = NewState();
            AssertBlockedWithoutMutation(station, Request("craft_basic", "op-station", stationLevel: 0), CraftStartCode.StationUnavailable);

            var requirement = NewState();
            AssertBlockedWithoutMutation(requirement, Request("craft_requires_locked_building", "op-requirement"), CraftStartCode.AdditionalBuildingUnavailable);

            var recipe = NewState();
            Seed(recipe, "resource_rabbit_meat", 1);
            AssertBlockedWithoutMutation(recipe, Request("craft_recipe_kept", "op-missing-recipe"), CraftStartCode.MissingOrInvalidRecipe);

            var materials = NewState();
            Seed(materials, "resource_rabbit_meat", 2);
            Seed(materials, "resource_herb", 1);
            AssertBlockedWithoutMutation(materials, Request("craft_basic", "op-missing-material"), CraftStartCode.MissingMaterials);
        }

        [Test]
        public void NewOperationForMatchingActiveExecutionIsRejectedWithoutSecondMutation()
        {
            var state = NewState();
            Seed(state, "resource_rabbit_meat", 6);
            Seed(state, "resource_herb", 2);
            var runtime = Runtime(state);
            Assert.That(runtime.Start(Request("craft_basic", "op-first-active")).Success, Is.True);
            var meat = state.GetItem("resource_rabbit_meat");
            var fatigue = state.GetHeroFatigue("ren");

            var duplicate = runtime.Start(Request("craft_basic", "op-second-active"));

            Assert.That(duplicate.Success, Is.False);
            Assert.That(duplicate.Code, Is.EqualTo(CraftStartCode.ExecutionAlreadyActive));
            Assert.That(state.GetCraftExecutions(), Has.Length.EqualTo(1));
            Assert.That(state.GetItem("resource_rabbit_meat"), Is.EqualTo(meat));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(fatigue));
        }

        [Test]
        public void AdditionalCraftStartsThroughSameGenericPath()
        {
            var state = NewState();
            Seed(state, "resource_herb", 2);

            var result = Runtime(state).Start(Request("craft_other", "op-other"));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Execution.craftId, Is.EqualTo("craft_other"));
            Assert.That(result.Execution.durationSeconds, Is.EqualTo(20));
            Assert.That(result.Execution.outputItemId, Is.EqualTo("consumable_other"));
            Assert.That(state.GetItem("resource_herb"), Is.Zero);
        }

        private PlayerState NewState()
        {
            return SaveService.Load(_factory, _storage);
        }

        private CraftRuntimeService Runtime(PlayerState state, Action<CraftStartedEvent> eventSink = null)
        {
            return new CraftRuntimeService(_database.Crafts, new PlayerStateCraftAdapter(state), eventSink);
        }

        private static CraftStartRequest Request(
            string craftId,
            string operationKey,
            string heroId = "ren",
            string stationId = "building_campfire",
            int stationLevel = 1)
        {
            return new CraftStartRequest
            {
                CraftId = craftId,
                HeroId = heroId,
                StationBuildingId = stationId,
                StationBuildingLevel = stationLevel,
                OperationKey = operationKey
            };
        }

        private static void Seed(PlayerState state, string itemId, int quantity)
        {
            var result = state.Storage.Add($"seed:{itemId}:{Guid.NewGuid():N}", state.Storage.GetSnapshot().Revision, itemId, quantity);
            Assert.That(result.Success, Is.True, result.Message);
        }

        private void AssertBlockedWithoutMutation(PlayerState state, CraftStartRequest request, string code)
        {
            var before = JsonUtility.ToJson(state.ToSaveData());
            var result = Runtime(state).Start(request);
            var after = JsonUtility.ToJson(state.ToSaveData());
            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(code));
            Assert.That(after, Is.EqualTo(before));
        }

        private CraftDefinitionConfigDto FindDefinition(string craftId)
        {
            foreach (var definition in _database.Items.CraftDefinitions)
                if (string.Equals(definition.craftId, craftId, StringComparison.Ordinal)) return definition;
            return null;
        }

        private static CraftPaidCostSaveData FindCost(CraftExecutionSaveData execution, string itemId)
        {
            foreach (var cost in execution.paidCosts ?? Array.Empty<CraftPaidCostSaveData>())
                if (cost != null && string.Equals(cost.itemId, itemId, StringComparison.Ordinal)) return cost;
            return null;
        }

        private static bool HasStartReceipt(PlayerState state, string operationKey, string executionId)
        {
            foreach (var receipt in state.ToSaveData().operationReceipts)
            {
                if (receipt == null || !string.Equals(receipt.aggregateId, "craft-start", StringComparison.Ordinal) ||
                    !string.Equals(receipt.operationId, operationKey, StringComparison.Ordinal))
                    continue;
                return executionId == null || string.Equals(receipt.executionId, executionId, StringComparison.Ordinal);
            }
            return false;
        }

        private static ConfigDatabase CreateDatabase()
        {
            return new ConfigDatabase(
                new ItemsRuntimeConfigDto
                {
                    resources = new[]
                    {
                        new ResourceConfigDto { id = "resource_rabbit_meat", kind = "resource" },
                        new ResourceConfigDto { id = "resource_herb", kind = "resource" }
                    },
                    recipes = new[]
                    {
                        new RecipeConfigDto { id = "recipe_roasting", kind = "recipe", enabled = true }
                    },
                    consumables = new[]
                    {
                        new ConsumableConfigDto { id = "consumable_roasted_rabbit_meat", kind = "consumable" },
                        new ConsumableConfigDto { id = "consumable_other", kind = "consumable" }
                    },
                    craftDefinitions = new[]
                    {
                        Definition("craft_basic", "consumable_roasted_rabbit_meat", 10, 2, 2,
                            new[]
                            {
                                new MaterialCostDto { id = "resource_rabbit_meat", count = 1 },
                                new MaterialCostDto { id = "resource_rabbit_meat", count = 2 },
                                new MaterialCostDto { id = "resource_herb", count = 1 }
                            }),
                        Definition("craft_recipe_kept", "consumable_roasted_rabbit_meat", 10, 1, 2,
                            new[] { new MaterialCostDto { id = "resource_rabbit_meat", count = 1 } },
                            "recipe_roasting", 1, false),
                        Definition("craft_recipe_consumed", "consumable_roasted_rabbit_meat", 10, 1, 2,
                            new[] { new MaterialCostDto { id = "resource_rabbit_meat", count = 1 } },
                            "recipe_roasting", 2, true),
                        Definition("craft_other", "consumable_other", 20, 1, 5,
                            new[] { new MaterialCostDto { id = "resource_herb", count = 2 } }),
                        Definition("craft_requires_locked_building", "consumable_other", 5, 0, 0,
                            Array.Empty<MaterialCostDto>(), requiredBuildings: new[] { new RequiredBuildingDto { buildingId = "building_locked", level = 1 } })
                    }
                },
                new HeroesRuntimeConfigDto
                {
                    heroes = new[]
                    {
                        new HeroConfigDto { heroId = "ren", enabled = true, baseStats = new HeroBaseStatsDto { endurance = 5 } },
                        new HeroConfigDto { heroId = "aska", enabled = true, baseStats = new HeroBaseStatsDto { endurance = 5 } }
                    }
                },
                new ActivitiesRuntimeConfigDto
                {
                    skills = new[] { new SkillConfigDto { skillId = "skill_crafting" } },
                    skillsProgression = new[] { new SkillProgressionConfigDto { level = 1, totalExpRequired = 0 } }
                },
                new BuildingsRuntimeConfigDto
                {
                    buildings = new[]
                    {
                        Building("building_hall", true),
                        Building("building_campfire", true),
                        Building("building_warehouse", true),
                        Building("building_locked", false)
                    },
                    buildingLevels = new[]
                    {
                        new BuildingLevelConfigDto { buildingId = "building_hall", level = 1, activeHeroLimit = 1 },
                        new BuildingLevelConfigDto { buildingId = "building_campfire", level = 1 },
                        new BuildingLevelConfigDto { buildingId = "building_warehouse", level = 1 },
                        new BuildingLevelConfigDto { buildingId = "building_locked", level = 1 }
                    },
                    buildingCraftables = new[]
                    {
                        Craftable("craft_basic"),
                        Craftable("craft_recipe_kept"),
                        Craftable("craft_recipe_consumed"),
                        Craftable("craft_other"),
                        Craftable("craft_requires_locked_building")
                    },
                    settlementStageStarterHeroes = new[]
                    {
                        new SettlementStageStarterHeroConfigDto { stageId = "stage_arrival", heroId = "ren" }
                    }
                },
                new QuestRuntimeConfigDto
                {
                    stages = new[] { new StageConfigDto { stageId = "stage_arrival", enabled = true } }
                },
                null,
                new FormulaRuntimeConfigDto
                {
                    formulas = new[]
                    {
                        new FormulaConfigDto
                        {
                            formulaId = "hero_max_fatigue",
                            derivedStatId = "max_fatigue",
                            formulaType = "linear_stats_with_skill_level",
                            baseValue = 100,
                            primaryStat = "Endurance",
                            primaryStatMultiplier = 1,
                            secondaryStat = "Endurance",
                            rounding = "floor",
                            enabled = true
                        }
                    }
                },
                null,
                null,
                new StorageRuntimeConfigDto
                {
                    storageRules = new[]
                    {
                        new StorageRuleConfigDto { storageRuleId = "resource", itemKind = "resource", mode = "stack", maxStack = 2, occupiesSlot = true },
                        new StorageRuleConfigDto { storageRuleId = "recipe", itemKind = "recipe", mode = "stack", maxStack = 2, occupiesSlot = true },
                        new StorageRuleConfigDto { storageRuleId = "consumable", itemKind = "consumable", mode = "stack", maxStack = 20, occupiesSlot = true }
                    },
                    storageBuildings = new[] { new StorageBuildingConfigDto { buildingId = "building_warehouse", level = 1, slotCount = 30 } },
                    itemStates = new[]
                    {
                        new ItemStateConfigDto { stateId = "on_storage", isInStorage = true, occupiesCapacity = true, availabilityMode = "available", availableForCraft = true },
                        new ItemStateConfigDto { stateId = "equipped", requiresOwner = true, availabilityMode = "equipped" }
                    }
                },
                null);
        }

        private static CraftDefinitionConfigDto Definition(
            string craftId,
            string outputItemId,
            int duration,
            int fatigue,
            int skillExp,
            MaterialCostDto[] materials,
            string recipeItemId = null,
            int recipeCount = 0,
            bool consumeRecipe = false,
            RequiredBuildingDto[] requiredBuildings = null)
        {
            return new CraftDefinitionConfigDto
            {
                craftId = craftId,
                targetItemId = outputItemId,
                craftStationId = "building_campfire",
                craftDurationSec = duration,
                craftSkillId = "skill_crafting",
                requiredBuildings = requiredBuildings ?? Array.Empty<RequiredBuildingDto>(),
                materials = materials,
                requiredRecipeItemId = recipeItemId,
                requiredRecipeItemCount = recipeCount,
                consumeRecipeItem = consumeRecipe,
                outputCount = 1,
                fatigueCost = fatigue,
                skillExp = skillExp
            };
        }

        private static BuildingConfigDto Building(string id, bool visibleAtStart)
        {
            return new BuildingConfigDto { buildingId = id, levels = 1, startLevel = 1, visibleAtStart = visibleAtStart };
        }

        private static BuildingCraftableConfigDto Craftable(string craftId)
        {
            return new BuildingCraftableConfigDto
            {
                buildingId = "building_campfire",
                buildingLevel = 1,
                craftId = craftId,
                enabled = true
            };
        }

        private sealed class FaultInjectingCraftState : ICraftPlayerState
        {
            private readonly ICraftPlayerState _inner;
            private int _consumeCalls;

            public FaultInjectingCraftState(ICraftPlayerState inner) => _inner = inner;
            public int FailConsumeCall { get; set; } = -1;
            public bool FailOccupation { get; set; }
            public bool FailAddExecution { get; set; }
            public SaveData CaptureCheckpoint() => _inner.CaptureCheckpoint();
            public void RestoreCheckpoint(SaveData checkpoint) => _inner.RestoreCheckpoint(checkpoint);
            public bool TryGetOperationReceipt(string aggregateId, string operationId, out OperationReceiptSaveData receipt) => _inner.TryGetOperationReceipt(aggregateId, operationId, out receipt);
            public void RecordOperationReceipt(OperationReceiptSaveData receipt) => _inner.RecordOperationReceipt(receipt);
            public bool HasHero(string heroId) => _inner.HasHero(heroId);
            public bool HasHeroState(string heroId) => _inner.HasHeroState(heroId);
            public int GetHeroFatigue(string heroId) => _inner.GetHeroFatigue(heroId);
            public bool SpendHeroFatigue(string heroId, int amount) => _inner.SpendHeroFatigue(heroId, amount);
            public bool IsHeroBusy(string heroId) => _inner.IsHeroBusy(heroId);
            public int GetActiveHeroCount() => _inner.GetActiveHeroCount();
            public int GetActiveHeroLimit() => _inner.GetActiveHeroLimit();
            public bool TryOccupyHero(string heroId, string executionId) => !FailOccupation && _inner.TryOccupyHero(heroId, executionId);
            public bool IsBuildingUnlocked(string buildingId) => _inner.IsBuildingUnlocked(buildingId);
            public int GetBuildingLevel(string buildingId) => _inner.GetBuildingLevel(buildingId);
            public int GetAvailableForCraftCount(string itemId) => _inner.GetAvailableForCraftCount(itemId);

            public bool TryConsumeCraftCost(string itemId, int quantity, out string error)
            {
                _consumeCalls++;
                if (_consumeCalls == FailConsumeCall)
                {
                    error = "simulated removal failure";
                    return false;
                }
                return _inner.TryConsumeCraftCost(itemId, quantity, out error);
            }

            public void PublishCraftStartCommit() => _inner.PublishCraftStartCommit();

            public CraftExecutionSaveData[] GetCraftExecutions() => _inner.GetCraftExecutions();
            public CraftExecutionSaveData GetCraftExecution(string executionId) => _inner.GetCraftExecution(executionId);
            public bool AddCraftExecution(CraftExecutionSaveData execution) => !FailAddExecution && _inner.AddCraftExecution(execution);
            public bool Save() => _inner.Save();
        }

        private sealed class MemorySaveStorage : ISaveStorage
        {
            private string _json;
            public int SaveCalls { get; private set; }
            public bool ThrowOnSet { get; set; }
            public bool HasKey(string key) => _json != null;
            public string GetString(string key, string defaultValue) => _json ?? defaultValue;
            public void SetString(string key, string value)
            {
                if (ThrowOnSet)
                    throw new InvalidOperationException("simulated save failure");
                _json = value;
            }
            public void DeleteKey(string key) => _json = null;
            public void Save() => SaveCalls++;
        }
    }
}
