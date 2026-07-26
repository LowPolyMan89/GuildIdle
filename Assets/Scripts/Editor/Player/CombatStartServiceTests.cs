using System;
using System.Collections.Generic;
using System.Text;
using GuildIdle.Combat;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Editor.Configs;
using GuildIdle.Player;
using NUnit.Framework;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Editor.Player
{
    public sealed class CombatStartServiceTests
    {
        private const int ReceiptRetentionLimit = 64;

        [Test]
        public void DirectEmptyStartCreatesAggregateAndPaysFatigueOnce()
        {
            var fixture = new Fixture();
            var beforeFatigue = fixture.State.Fatigue;

            var result = fixture.Service.Start(fixture.Direct());

            Assert.That(result.Success, Is.True);
            Assert.That(result.Replayed, Is.False);
            Assert.That(result.ExecutionId, Is.EqualTo("combat-1"));
            Assert.That(result.SessionId, Is.EqualTo("session-1"));
            Assert.That(result.Aggregate.session.loadoutKind, Is.EqualTo(CombatLoadoutKind.Empty));
            Assert.That(result.Aggregate.session.broughtConsumable, Is.Null);
            Assert.That(result.Aggregate.execution.sourceExecutionId, Is.EqualTo("combat-1"));
            Assert.That(result.Aggregate.execution.occupationOwnerId, Is.EqualTo("combat-1"));
            Assert.That(fixture.State.BusyOwner, Is.EqualTo("combat-1"));
            Assert.That(fixture.State.Fatigue, Is.EqualTo(beforeFatigue - 5));
            Assert.That(fixture.State.SaveCalls, Is.EqualTo(1));
            Assert.That(fixture.StartedEvents, Is.EqualTo(1));
        }

        [Test]
        public void ConsumableStartExtractsOnlySelectedStackAndKeepsSourceIdOnPartial()
        {
            var fixture = new Fixture();
            fixture.State.AddStack("chosen", "consumable-food", 5);
            fixture.State.AddStack("other", "consumable-food", 7);
            var command = fixture.Direct("operation-consumable", "request-consumable");
            command.StackId = "chosen";
            command.RequestedQuantity = 3;

            var result = fixture.Service.Start(command);

            Assert.That(result.Success, Is.True);
            Assert.That(fixture.State.StackQuantity("chosen"), Is.EqualTo(2));
            Assert.That(fixture.State.StackQuantity("other"), Is.EqualTo(7));
            Assert.That(fixture.State.StorageRevision, Is.EqualTo(1));
            Assert.That(fixture.State.StorageEvents, Is.EqualTo(1));
            Assert.That(result.Aggregate.session.loadoutKind, Is.EqualTo(CombatLoadoutKind.Consumable));
            Assert.That(result.Aggregate.session.broughtConsumable.sourceStackId, Is.EqualTo("chosen"));
            Assert.That(result.Aggregate.session.broughtConsumable.initialQuantity, Is.EqualTo(3));
            Assert.That(result.Aggregate.session.broughtConsumable.remainingQuantity, Is.EqualTo(3));
        }

        [Test]
        public void FullConsumableExtractionRemovesStorageStack()
        {
            var fixture = new Fixture();
            fixture.State.AddStack("chosen", "consumable-food", 3);
            var command = fixture.Direct();
            command.StackId = "chosen";
            command.RequestedQuantity = 3;

            var result = fixture.Service.Start(command);

            Assert.That(result.Success, Is.True);
            Assert.That(fixture.State.HasStack("chosen"), Is.False);
            Assert.That(result.Aggregate.session.broughtConsumable.sourceStackId, Is.EqualTo("chosen"));
        }

        [TestCase(null, 1)]
        [TestCase("chosen", 0)]
        [TestCase("chosen", -1)]
        public void InvalidLoadoutCombinationIsRejected(string stackId, int quantity)
        {
            var fixture = new Fixture();
            fixture.State.AddStack("chosen", "consumable-food", 3);
            var command = fixture.Direct();
            command.StackId = stackId;
            command.RequestedQuantity = quantity;

            var result = fixture.Service.Start(command);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(CombatStartCode.InvalidLoadout));
            fixture.AssertUnchanged();
        }

        [Test]
        public void ConsumableValidationRejectsQuantityAndDescriptorFailuresWithoutClamp()
        {
            var fixture = new Fixture();
            fixture.State.AddStack("chosen", "consumable-food", 5);
            var exceedsStack = fixture.Direct("op-stack", "request-stack");
            exceedsStack.StackId = "chosen";
            exceedsStack.RequestedQuantity = 6;
            Assert.That(
                fixture.Service.Start(exceedsStack).Code,
                Is.EqualTo(CombatStartCode.QuantityExceedsStack));

            var exceedsMax = fixture.Direct("op-max", "request-max");
            exceedsMax.StackId = "chosen";
            exceedsMax.RequestedQuantity = 4;
            Assert.That(
                fixture.Service.Start(exceedsMax).Code,
                Is.EqualTo(CombatStartCode.QuantityExceedsMaxStack));

            fixture.State.AddStack("unsupported", "resource-stone", 2);
            var unsupported = fixture.Direct("op-unsupported", "request-unsupported");
            unsupported.StackId = "unsupported";
            unsupported.RequestedQuantity = 1;
            Assert.That(
                fixture.Service.Start(unsupported).Code,
                Is.EqualTo(CombatStartCode.UnsupportedConsumable));
            Assert.That(fixture.State.StackQuantity("chosen"), Is.EqualTo(5));
            Assert.That(fixture.State.StackQuantity("unsupported"), Is.EqualTo(2));
        }

        [Test]
        public void StaleRevisionIsRejectedBeforeMutation()
        {
            var fixture = new Fixture();
            var command = fixture.Direct();
            command.ExpectedStorageRevision = 1;

            var result = fixture.Service.Start(command);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(CombatStartCode.StaleStorageRevision));
            fixture.AssertUnchanged();
        }

        [TestCase("busy", 1, 10, CombatStartCode.HeroBusy)]
        [TestCase(null, 0, 10, CombatStartCode.ActiveHeroLimitReached)]
        [TestCase(null, 1, 4, CombatStartCode.InsufficientFatigue)]
        public void DirectHeroGuardsRejectWithoutStateChange(
            string busyOwner,
            int activeLimit,
            int fatigue,
            CombatStartCode expected)
        {
            var fixture = new Fixture();
            fixture.State.BusyOwner = busyOwner;
            fixture.State.ActiveLimit = activeLimit;
            fixture.State.Fatigue = fatigue;

            var result = fixture.Service.Start(fixture.Direct());

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(expected));
            Assert.That(fixture.State.Fatigue, Is.EqualTo(fatigue));
            Assert.That(fixture.State.Aggregates.Count, Is.EqualTo(0));
        }

        [Test]
        public void ReplayReturnsSameIdsWithoutRepeatingCostsQueueOrSeed()
        {
            var fixture = new Fixture();
            fixture.State.AddStack("chosen", "consumable-food", 3);
            var command = fixture.Direct();
            command.StackId = "chosen";
            command.RequestedQuantity = 2;

            var first = fixture.Service.Start(command);
            var replay = fixture.Service.Start(command);

            Assert.That(first.Success, Is.True);
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.ExecutionId, Is.EqualTo(first.ExecutionId));
            Assert.That(replay.SessionId, Is.EqualTo(first.SessionId));
            Assert.That(fixture.State.StackQuantity("chosen"), Is.EqualTo(1));
            Assert.That(fixture.State.Fatigue, Is.EqualTo(5));
            Assert.That(fixture.Identity.SeedCalls, Is.EqualTo(1));
            Assert.That(fixture.Queue.BuildCalls, Is.EqualTo(1));
            Assert.That(fixture.State.SaveCalls, Is.EqualTo(1));
            Assert.That(fixture.StartedEvents, Is.EqualTo(1));
        }

        [Test]
        public void OperationConflictDoesNotMutateState()
        {
            var fixture = new Fixture();
            var first = fixture.Direct();
            Assert.That(fixture.Service.Start(first).Success, Is.True);
            var conflicting = fixture.Direct();
            conflicting.EnemyGroupId = "another-group";

            var result = fixture.Service.Start(conflicting);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(CombatStartCode.OperationConflict));
            Assert.That(fixture.State.Aggregates.Count, Is.EqualTo(1));
            Assert.That(fixture.State.Fatigue, Is.EqualTo(5));
        }

        [Test]
        public void DifferentOperationCannotStartTheSameSourceRequestTwice()
        {
            var fixture = new Fixture();
            Assert.That(fixture.Service.Start(fixture.Direct()).Success, Is.True);
            var duplicateSource = fixture.Direct(
                "another-operation",
                "request-1");
            duplicateSource.ExpectedStorageRevision =
                fixture.State.StorageRevision;

            var result = fixture.Service.Start(duplicateSource);

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.Code,
                Is.EqualTo(CombatStartCode.SourceAlreadyStarted));
            Assert.That(fixture.State.Aggregates.Count, Is.EqualTo(1));
            Assert.That(fixture.State.Fatigue, Is.EqualTo(5));
        }

        [Test]
        public void ReplaySurvivesReceiptEvictionWhileAggregateIsActive()
        {
            var fixture = new Fixture();
            var command = fixture.Direct();
            var first = fixture.Service.Start(command);
            for (var index = 0; index < ReceiptRetentionLimit; index++)
            {
                fixture.State.RecordOperationReceipt(new OperationReceiptSaveData
                {
                    aggregateId = "other",
                    operationId = $"other-{index}",
                    fingerprint = "x"
                });
            }

            var replay = fixture.Service.Start(command);

            Assert.That(first.Success, Is.True);
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.ExecutionId, Is.EqualTo(first.ExecutionId));
            Assert.That(fixture.State.Fatigue, Is.EqualTo(5));
        }

        [Test]
        public void ReplayAfterReloadUsesSavedAggregateAndReceipt()
        {
            var fixture = new Fixture();
            var command = fixture.Direct();
            var first = fixture.Service.Start(command);
            var loadedState = fixture.State.CloneForLoad();
            var loadedIdentity = new IdentityProvider();
            var loaded = fixture.CreateService(loadedState, loadedIdentity);

            var replay = loaded.Start(command);

            Assert.That(first.Success, Is.True);
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.ExecutionId, Is.EqualTo(first.ExecutionId));
            Assert.That(loadedState.Fatigue, Is.EqualTo(5));
            Assert.That(loadedIdentity.SeedCalls, Is.EqualTo(0));
        }

        [TestCase("extract")]
        [TestCase("aggregate")]
        [TestCase("save")]
        public void FailureAfterPreflightRollsBackAllStateAndPublishesNothing(
            string failurePoint)
        {
            var fixture = new Fixture();
            fixture.State.AddStack("chosen", "consumable-food", 3);
            fixture.State.FailExtraction = failurePoint == "extract";
            fixture.State.FailAddAggregate = failurePoint == "aggregate";
            fixture.State.FailSave = failurePoint == "save";
            var command = fixture.Direct();
            command.StackId = "chosen";
            command.RequestedQuantity = 2;

            var result = fixture.Service.Start(command);

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.Code,
                Is.EqualTo(
                    failurePoint == "save"
                        ? CombatStartCode.SaveFailure
                        : CombatStartCode.TransactionFailure));
            Assert.That(fixture.State.StackQuantity("chosen"), Is.EqualTo(3));
            Assert.That(fixture.State.StorageRevision, Is.EqualTo(0));
            Assert.That(fixture.State.Fatigue, Is.EqualTo(10));
            Assert.That(fixture.State.BusyOwner, Is.Null);
            Assert.That(fixture.State.Aggregates.Count, Is.EqualTo(0));
            Assert.That(fixture.State.Receipts.Count, Is.EqualTo(0));
            Assert.That(fixture.State.StorageEvents, Is.EqualTo(0));
            Assert.That(fixture.StartedEvents, Is.EqualTo(0));
        }

        [Test]
        public void LinkedStartKeepsExistingOccupationAndDoesNotSpendFatigueOrLimit()
        {
            var fixture = new Fixture();
            fixture.State.ConfigureLinkedSource();
            var beforeFatigue = fixture.State.Fatigue;
            var beforeActive = fixture.State.GetActiveHeroCount();

            var result = fixture.Service.Start(fixture.Linked());

            Assert.That(result.Success, Is.True);
            Assert.That(result.Aggregate.execution.sourceExecutionId, Is.EqualTo("activity-root"));
            Assert.That(result.Aggregate.execution.occupationOwnerId, Is.EqualTo("activity-root"));
            Assert.That(fixture.State.BusyOwner, Is.EqualTo("activity-root"));
            Assert.That(fixture.State.Fatigue, Is.EqualTo(beforeFatigue));
            Assert.That(fixture.State.GetActiveHeroCount(), Is.EqualTo(beforeActive));
            Assert.That(
                fixture.State.Activity.linkedCombat.combatExecutionId,
                Is.EqualTo(result.ExecutionId));
        }

        [Test]
        public void LinkedSourceMismatchAndBindingFailureAreAtomic()
        {
            var mismatchFixture = new Fixture();
            mismatchFixture.State.ConfigureLinkedSource();
            var mismatch = mismatchFixture.Linked();
            mismatch.OccupationOwnerId = "other-owner";
            var mismatchResult = mismatchFixture.Service.Start(mismatch);
            Assert.That(mismatchResult.Code, Is.EqualTo(CombatStartCode.InvalidSourceContract));
            Assert.That(mismatchFixture.State.Aggregates.Count, Is.EqualTo(0));

            var bindFixture = new Fixture();
            bindFixture.State.ConfigureLinkedSource();
            bindFixture.State.FailBind = true;
            var bindResult = bindFixture.Service.Start(bindFixture.Linked());
            Assert.That(bindResult.Code, Is.EqualTo(CombatStartCode.TransactionFailure));
            Assert.That(bindFixture.State.Aggregates.Count, Is.EqualTo(0));
            Assert.That(
                bindFixture.State.Activity.linkedCombat.combatExecutionId,
                Is.Null.Or.Empty);
            Assert.That(bindFixture.State.BusyOwner, Is.EqualTo("activity-root"));
        }

        [Test]
        public void ConsumableMarkerWithoutStateIsRejectedBySaveNormalizer()
        {
            var fixture = new Fixture();
            var result = fixture.Service.Start(fixture.Direct());
            result.Aggregate.session.loadoutKind = CombatLoadoutKind.Consumable;
            result.Aggregate.session.broughtConsumable = null;

            var added = new FakeState().AddCombatAggregate(result.Aggregate);

            Assert.That(added, Is.False);
        }

        [Test]
        public void PlayerStateAdapterPersistsExactTransferAndReplaysAfterLoad()
        {
            var database = CreateIntegrationDatabase();
            RuntimeConfigs.SetDatabaseForTests(database);
            var factory =
                TestPlayerComposition.CreatePlayerStateFactory(database);
            var saveStorage = new MemorySaveStorage();
            var state = SaveService.Load(factory, saveStorage);
            state.SetActivityAvailable("combat-activity", true);
            Assert.That(
                state.IsActivityAvailable("combat-activity"),
                Is.True);
            var added = state.Storage.Add(
                "add-consumable",
                state.Storage.GetSnapshot().Revision,
                "consumable-food",
                5);
            Assert.That(added.Success, Is.True);
            var sourceStackId = added.StackId;
            var command = new CombatStartCommand
            {
                OperationId = "production-operation",
                Kind = CombatStartKind.Direct,
                SourceActivityId = "combat-activity",
                SourceRequestId = "production-request",
                HeroId = "ren",
                EnemyGroupId = "enemy-group",
                CombatMode = CombatEnemyQueueBuilder.Queue1V1Mode,
                StackId = sourceStackId,
                RequestedQuantity = 3,
                ExpectedStorageRevision = added.StorageRevision
            };
            var identity = new IdentityProvider();
            var service = new CombatStartService(
                new PlayerStateCombatStartAdapter(
                    state,
                    database.Formulas,
                    database.Items),
                new ConfigCombatStartActivityDescriptorProvider(
                    database.Activities),
                database.CombatConsumables,
                new ConfigCombatEnemyQueueProvider(database.Enemies),
                identity: identity);
            var beforeFatigue = state.GetHeroFatigue("ren");

            var result = service.Start(command);

            Assert.That(result.Success, Is.True);
            Assert.That(
                state.Storage.GetSnapshot().Stacks[0].stackId,
                Is.EqualTo(sourceStackId));
            Assert.That(
                state.Storage.GetSnapshot().Stacks[0].quantity,
                Is.EqualTo(2));
            Assert.That(
                state.GetHeroFatigue("ren"),
                Is.EqualTo(beforeFatigue - 5));
            Assert.That(
                state.GetHeroCurrentActivityExecutionId("ren"),
                Is.EqualTo(result.ExecutionId));
            Assert.That(result.Aggregate.session.hero.maxHp, Is.EqualTo(92));
            Assert.That(result.Aggregate.session.hero.currentHp, Is.EqualTo(92));

            var loaded = SaveService.Load(factory, saveStorage, out var origin);
            var replayService = new CombatStartService(
                new PlayerStateCombatStartAdapter(
                    loaded,
                    database.Formulas,
                    database.Items),
                new ConfigCombatStartActivityDescriptorProvider(
                    database.Activities),
                database.CombatConsumables,
                new ConfigCombatEnemyQueueProvider(database.Enemies),
                identity: new IdentityProvider());
            var replay = replayService.Start(command);

            Assert.That(origin, Is.EqualTo(SaveLoadOrigin.ExistingV9));
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.ExecutionId, Is.EqualTo(result.ExecutionId));
            Assert.That(
                loaded.Storage.GetSnapshot().Stacks[0].quantity,
                Is.EqualTo(2));
            Assert.That(
                loaded.GetHeroFatigue("ren"),
                Is.EqualTo(beforeFatigue - 5));

            for (var index = 0; index < 70; index++)
            {
                var revision = loaded.Storage.GetSnapshot().Revision;
                var mutation = index % 2 == 0
                    ? loaded.Storage.Add(
                        $"receipt-churn-add-{index}",
                        revision,
                        "consumable-food",
                        1)
                    : loaded.Storage.Consume(
                        $"receipt-churn-consume-{index}",
                        revision,
                        "consumable-food",
                        1);
                Assert.That(mutation.Success, Is.True);
            }
            Assert.That(SaveService.Save(loaded, saveStorage), Is.True);
            var serializedSize = Encoding.UTF8.GetByteCount(
                JsonUtility.ToJson(loaded.ToSaveData()));
            var replayAfterReceiptEviction = replayService.Start(command);

            Assert.That(
                loaded.ToSaveData().operationReceipts,
                Has.Length.EqualTo(ReceiptRetentionLimit));
            Assert.That(serializedSize, Is.LessThan(200 * 1024));
            Assert.That(replayAfterReceiptEviction.Success, Is.True);
            Assert.That(replayAfterReceiptEviction.Replayed, Is.True);
            Assert.That(
                replayAfterReceiptEviction.ExecutionId,
                Is.EqualTo(result.ExecutionId));
            Assert.That(
                loaded.Storage.GetSnapshot().Stacks[0].quantity,
                Is.EqualTo(2));
        }

        private static ConfigDatabase CreateIntegrationDatabase()
        {
            return new ConfigDatabase(
                new ItemsRuntimeConfigDto
                {
                    consumables = new[]
                    {
                        new ConsumableConfigDto
                        {
                            id = "consumable-food",
                            kind = "consumable",
                            usePlace = "combat",
                            useCondition = "hp_percent<=40",
                            effects = new[] { "RestoreHealthFlat:25" },
                            cooldownSeconds = 5d,
                            checkIntervalSeconds = 1d
                        }
                    }
                },
                new HeroesRuntimeConfigDto
                {
                    heroes = new[]
                    {
                        new HeroConfigDto
                        {
                            heroId = "ren",
                            enabled = true,
                            baseStats = new HeroBaseStatsDto { endurance = 5 }
                        }
                    }
                },
                new ActivitiesRuntimeConfigDto
                {
                    activities = new[]
                    {
                        new ActivityConfigDto
                        {
                            id = "combat-activity",
                            type = "CombatTask",
                            fatigueCost = 5
                        }
                    },
                    combatDetails = new[]
                    {
                        new CombatDetailConfigDto
                        {
                            activityId = "combat-activity",
                            enemyGroupId = "enemy-group",
                            combatMode =
                                CombatEnemyQueueBuilder.Queue1V1Mode
                        }
                    }
                },
                new BuildingsRuntimeConfigDto
                {
                    buildings = new[]
                    {
                        new BuildingConfigDto
                        {
                            buildingId = "building_hall",
                            levels = 0,
                            startLevel = 0,
                            visibleAtStart = true
                        },
                        new BuildingConfigDto
                        {
                            buildingId = "building_warehouse",
                            levels = 0,
                            startLevel = 0,
                            visibleAtStart = true
                        }
                    },
                    buildingLevels = new[]
                    {
                        new BuildingLevelConfigDto
                        {
                            buildingId = "building_hall",
                            level = 0,
                            activeHeroLimit = 1
                        },
                        new BuildingLevelConfigDto
                        {
                            buildingId = "building_warehouse",
                            level = 0
                        }
                    },
                    settlementStageStarterHeroes = new[]
                    {
                        new SettlementStageStarterHeroConfigDto
                        {
                            stageId = "stage_arrival",
                            heroId = "ren",
                            sortOrder = 10
                        }
                    }
                },
                new QuestRuntimeConfigDto
                {
                    stages = new[]
                    {
                        new StageConfigDto
                        {
                            stageId = "stage_arrival",
                            enabled = true
                        }
                    }
                },
                new EnemiesRuntimeConfigDto
                {
                    enemies = new[]
                    {
                        new EnemyConfigDto
                        {
                            enemyId = "wolf",
                            hp = 30,
                            damageMin = 1,
                            damageMax = 2,
                            attacksPerSecond = 1f,
                            damageType = "physical",
                            critDamageMultiplier = 1.5f
                        }
                    },
                    enemyLevels = new[]
                    {
                        new EnemyLevelConfigDto
                        {
                            level = 1,
                            hpMultiplier = 1f,
                            damageMultiplier = 1f,
                            combatExpMultiplier = 1f,
                            lootQuantityMultiplier = 1f,
                            attackSpeedMultiplier = 1f
                        }
                    },
                    enemyGroups = new[]
                    {
                        new EnemyGroupConfigDto
                        {
                            enemyGroupId = "enemy-group",
                            enemyRef = "wolf:1",
                            sortOrder = 10,
                            weight = 1,
                            minCount = 1,
                            maxCount = 1
                        }
                    }
                },
                new FormulaRuntimeConfigDto
                {
                    formulas = new[]
                    {
                        new FormulaConfigDto
                        {
                            formulaId = "hero_max_hp",
                            derivedStatId = "max_hp",
                            formulaType = "linear_stat_with_level",
                            baseValue = 50,
                            primaryStat = "Endurance",
                            primaryStatMultiplier = 8,
                            levelMultiplier = 2,
                            minValue = 1,
                            rounding = "floor",
                            enabled = true
                        },
                        new FormulaConfigDto
                        {
                            formulaId = "hero_max_fatigue",
                            derivedStatId = "max_fatigue",
                            formulaType = "linear_stat_with_level",
                            baseValue = 100,
                            primaryStat = "Endurance",
                            primaryStatMultiplier = 4,
                            levelMultiplier = 1,
                            minValue = 1,
                            rounding = "round",
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
                        new StorageRuleConfigDto
                        {
                            storageRuleId = "storage-consumable",
                            itemKind = "consumable",
                            mode = "stack",
                            maxStack = 20,
                            occupiesSlot = true
                        }
                    },
                    storageBuildings = new[]
                    {
                        new StorageBuildingConfigDto
                        {
                            buildingId = "building_warehouse",
                            level = 0,
                            slotCount = 20
                        }
                    },
                    itemStates = new[]
                    {
                        new ItemStateConfigDto
                        {
                            stateId = "on-storage",
                            isInStorage = true,
                            occupiesCapacity = true,
                            availabilityMode =
                                ItemAvailabilityMode.Available
                        },
                        new ItemStateConfigDto
                        {
                            stateId = "equipped",
                            requiresOwner = true,
                            availabilityMode =
                                ItemAvailabilityMode.Equipped
                        }
                    }
                },
                null);
        }

        private sealed class MemorySaveStorage : ISaveStorage
        {
            private readonly Dictionary<string, string> _values =
                new Dictionary<string, string>(StringComparer.Ordinal);

            public bool HasKey(string key) => _values.ContainsKey(key);
            public string GetString(string key, string defaultValue) =>
                _values.TryGetValue(key, out var value) ? value : defaultValue;
            public void SetString(string key, string value) =>
                _values[key] = value;
            public void DeleteKey(string key) => _values.Remove(key);
            public void Save()
            {
            }
        }

        private sealed class Fixture
        {
            public readonly FakeState State = new FakeState();
            public readonly IdentityProvider Identity = new IdentityProvider();
            public readonly QueueProvider Queue = new QueueProvider();
            public int StartedEvents;
            public CombatStartService Service { get; }

            public Fixture()
            {
                Service = CreateService(State, Identity);
            }

            public CombatStartService CreateService(
                FakeState state,
                IdentityProvider identity)
            {
                return new CombatStartService(
                    state,
                    new ActivityProvider(),
                    new ConsumableProvider(),
                    Queue,
                    identity: identity,
                    eventSink: _ => StartedEvents++);
            }

            public CombatStartCommand Direct(
                string operationId = "operation-1",
                string requestId = "request-1")
            {
                return new CombatStartCommand
                {
                    OperationId = operationId,
                    Kind = CombatStartKind.Direct,
                    SourceActivityId = "combat-activity",
                    SourceRequestId = requestId,
                    HeroId = "ren",
                    EnemyGroupId = "enemy-group",
                    CombatMode = CombatEnemyQueueBuilder.Queue1V1Mode,
                    RequestedQuantity = 0,
                    ExpectedStorageRevision = State.StorageRevision
                };
            }

            public CombatStartCommand Linked()
            {
                return new CombatStartCommand
                {
                    OperationId = "linked-operation",
                    Kind = CombatStartKind.Linked,
                    SourceActivityId = "work-activity",
                    SourceExecutionId = "activity-root",
                    SourceRequestId = "linked-request",
                    OccupationOwnerId = "activity-root",
                    HeroId = "ren",
                    EnemyGroupId = "enemy-group",
                    CombatMode = CombatEnemyQueueBuilder.Queue1V1Mode,
                    RequestedQuantity = 0,
                    ExpectedStorageRevision = State.StorageRevision
                };
            }

            public void AssertUnchanged()
            {
                Assert.That(State.Fatigue, Is.EqualTo(10));
                Assert.That(State.BusyOwner, Is.Null);
                Assert.That(State.Aggregates.Count, Is.EqualTo(0));
                Assert.That(State.SaveCalls, Is.EqualTo(0));
                Assert.That(StartedEvents, Is.EqualTo(0));
            }
        }

        private sealed class ActivityProvider :
            ICombatStartActivityDescriptorProvider
        {
            public bool TryGet(
                string activityId,
                out CombatStartActivityDescriptor descriptor,
                out string error)
            {
                descriptor = null;
                error = null;
                if (!string.Equals(
                        activityId,
                        "combat-activity",
                        StringComparison.Ordinal))
                {
                    error = "Missing activity.";
                    return false;
                }

                descriptor = new CombatStartActivityDescriptor(
                    activityId,
                    "enemy-group",
                    CombatEnemyQueueBuilder.Queue1V1Mode,
                    5);
                return true;
            }
        }

        private sealed class ConsumableProvider :
            ICombatConsumableDescriptorProvider
        {
            public bool TryGet(
                string itemId,
                out CombatConsumableDescriptor descriptor)
            {
                descriptor = null;
                if (!string.Equals(
                        itemId,
                        "consumable-food",
                        StringComparison.Ordinal))
                {
                    return false;
                }

                descriptor = new CombatConsumableDescriptor(
                    itemId,
                    CombatConsumableUsePlace.Combat,
                    new CombatConsumableConditionDescriptor(
                        CombatConsumableConditionKind.HpPercent,
                        CombatConsumableComparisonOperator.LessOrEqual,
                        40d),
                    new CombatEffectDescriptor(CombatEffectKind.Heal, value: 25d),
                    5d,
                    1d,
                    3);
                return true;
            }
        }

        private sealed class IdentityProvider : ICombatStartIdentityProvider
        {
            private int _execution;
            private int _session;
            public int SeedCalls { get; private set; }

            public string CreateExecutionId() => $"combat-{++_execution}";
            public string CreateSessionId() => $"session-{++_session}";

            public ulong CreateRngSeed()
            {
                SeedCalls++;
                return (ulong)SeedCalls;
            }

            public long GetUtcNowUnixSeconds() => 100;
        }

        private sealed class QueueProvider : ICombatEnemyQueueProvider
        {
            public int BuildCalls { get; private set; }

            public bool TryBuildQueue(
                string sessionId,
                string enemyGroupId,
                ICombatRng rng,
                out CombatEnemyQueueEntrySaveData[] queue,
                out string error)
            {
                BuildCalls++;
                rng.NextUInt64();
                queue = new[]
                {
                    new CombatEnemyQueueEntrySaveData
                    {
                        combatantId = $"{sessionId}:enemy:0",
                        enemyId = "wolf",
                        level = 1,
                        queueIndex = 0
                    }
                };
                error = null;
                return true;
            }

            public bool TryCreateEnemyState(
                CombatEnemyQueueEntrySaveData queueEntry,
                out CombatantStateSaveData enemy,
                out string error)
            {
                enemy = new CombatantStateSaveData
                {
                    combatantId = queueEntry.combatantId,
                    definitionId = queueEntry.enemyId,
                    currentHp = 30,
                    maxHp = 30
                };
                error = null;
                return true;
            }
        }

        private sealed class FakeState : ICombatStartPlayerState
        {
            private readonly Dictionary<string, ItemStackSaveData> _stacks =
                new Dictionary<string, ItemStackSaveData>(StringComparer.Ordinal);
            private Snapshot _checkpoint;

            public int Fatigue = 10;
            public string BusyOwner;
            public int ActiveLimit = 1;
            public bool ActivityAvailable = true;
            public long StorageRevision;
            public int SaveCalls;
            public int StorageEvents;
            public bool FailExtraction;
            public bool FailAddAggregate;
            public bool FailBind;
            public bool FailSave;
            public ActivityExecutionSaveData Activity;
            public List<CombatRuntimeAggregate> Aggregates { get; } =
                new List<CombatRuntimeAggregate>();
            public List<OperationReceiptSaveData> Receipts { get; } =
                new List<OperationReceiptSaveData>();

            public void AddStack(string stackId, string itemId, int quantity)
            {
                _stacks.Add(
                    stackId,
                    new ItemStackSaveData
                    {
                        stackId = stackId,
                        itemId = itemId,
                        quantity = quantity,
                        stateId = "available"
                    });
            }

            public bool HasStack(string stackId) => _stacks.ContainsKey(stackId);
            public int StackQuantity(string stackId) =>
                _stacks.TryGetValue(stackId, out var stack) ? stack.quantity : 0;

            public void ConfigureLinkedSource()
            {
                BusyOwner = "activity-root";
                Activity = new ActivityExecutionSaveData
                {
                    executionId = "activity-root",
                    activityId = "work-activity",
                    runtimeKind = "Work",
                    heroId = "ren",
                    status = ActivityRuntimeStatus.Running,
                    linkedCombat = new LinkedCombatStartRequestSaveData
                    {
                        requestId = "linked-request",
                        rootExecutionId = "activity-root",
                        occupationOwnerId = "activity-root",
                        heroId = "ren",
                        enemyGroupId = "enemy-group",
                        combatMode = CombatEnemyQueueBuilder.Queue1V1Mode,
                        suppressFatigueCost = true
                    }
                };
            }

            public SaveData CaptureCheckpoint()
            {
                _checkpoint = Capture();
                return new SaveData();
            }

            public void RestoreCheckpoint(SaveData checkpoint)
            {
                Restore(_checkpoint);
            }

            public bool TryGetOperationReceipt(
                string aggregateId,
                string operationId,
                out OperationReceiptSaveData receipt)
            {
                for (var index = Receipts.Count - 1; index >= 0; index--)
                {
                    var value = Receipts[index];
                    if (string.Equals(
                            value.aggregateId,
                            aggregateId,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            value.operationId,
                            operationId,
                            StringComparison.Ordinal))
                    {
                        receipt = Clone(value);
                        return true;
                    }
                }

                receipt = null;
                return false;
            }

            public void RecordOperationReceipt(OperationReceiptSaveData receipt)
            {
                Receipts.Add(Clone(receipt));
                while (Receipts.Count > ReceiptRetentionLimit)
                    Receipts.RemoveAt(0);
            }

            public bool HasHero(string heroId) => heroId == "ren";
            public bool HasHeroState(string heroId) => heroId == "ren";
            public int GetHeroFatigue(string heroId) => Fatigue;

            public bool SpendHeroFatigue(string heroId, int amount)
            {
                if (Fatigue < amount)
                    return false;
                Fatigue -= amount;
                return true;
            }

            public bool IsHeroBusy(string heroId) =>
                !string.IsNullOrWhiteSpace(BusyOwner);
            public string GetHeroOccupationOwnerId(string heroId) => BusyOwner;
            public int GetActiveHeroCount() =>
                string.IsNullOrWhiteSpace(BusyOwner) ? 0 : 1;
            public int GetActiveHeroLimit() => ActiveLimit;
            public bool IsActivityAvailable(string activityId) =>
                ActivityAvailable;
            public ActivityExecutionSaveData GetActivityExecution(
                string executionId) =>
                Activity != null &&
                string.Equals(
                    Activity.executionId,
                    executionId,
                    StringComparison.Ordinal)
                    ? Clone(Activity)
                    : null;

            public bool BindLinkedCombatExecution(
                string sourceExecutionId,
                string sourceRequestId,
                string combatExecutionId)
            {
                if (FailBind ||
                    Activity?.linkedCombat == null ||
                    !string.Equals(
                        Activity.executionId,
                        sourceExecutionId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        Activity.linkedCombat.requestId,
                        sourceRequestId,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                Activity.linkedCombat.combatExecutionId = combatExecutionId;
                return true;
            }

            public long GetStorageRevision() => StorageRevision;

            public bool TryGetCombatSourceStack(
                string stackId,
                StorageActionContext actionContext,
                out ItemStackSaveData stack,
                out string code,
                out string error)
            {
                stack = null;
                code = null;
                error = null;
                if (!_stacks.TryGetValue(stackId, out var value))
                {
                    code = "StackNotFound";
                    error = "Missing stack.";
                    return false;
                }

                if (!string.Equals(value.stateId, "available", StringComparison.Ordinal))
                {
                    code = "StackUnavailable";
                    error = "Unavailable stack.";
                    return false;
                }

                stack = Clone(value);
                return true;
            }

            public bool TryExtractCombatSourceStack(
                string stackId,
                int quantity,
                StorageActionContext actionContext,
                out string itemId,
                out string error)
            {
                itemId = null;
                error = null;
                if (FailExtraction ||
                    !_stacks.TryGetValue(stackId, out var stack) ||
                    quantity <= 0 ||
                    quantity > stack.quantity)
                {
                    error = "Extraction failed.";
                    return false;
                }

                itemId = stack.itemId;
                stack.quantity -= quantity;
                if (stack.quantity == 0)
                    _stacks.Remove(stackId);
                StorageRevision++;
                return true;
            }

            public bool TryCreateHeroCombatant(
                string heroId,
                string sessionId,
                out CombatantStateSaveData hero,
                out string error)
            {
                hero = new CombatantStateSaveData
                {
                    combatantId = $"{sessionId}:hero",
                    definitionId = heroId,
                    currentHp = 50,
                    maxHp = 50
                };
                error = null;
                return true;
            }

            public CombatRuntimeAggregate[] GetCombatAggregates()
            {
                var result = new CombatRuntimeAggregate[Aggregates.Count];
                for (var index = 0; index < result.Length; index++)
                    result[index] = CloneAggregate(Aggregates[index]);
                return result;
            }

            public CombatRuntimeAggregate GetCombatAggregate(string executionId)
            {
                foreach (var aggregate in Aggregates)
                {
                    if (string.Equals(
                            aggregate.execution.executionId,
                            executionId,
                            StringComparison.Ordinal))
                    {
                        return CloneAggregate(aggregate);
                    }
                }

                return null;
            }

            public bool AddCombatAggregate(CombatRuntimeAggregate aggregate)
            {
                if (FailAddAggregate ||
                    aggregate?.execution == null ||
                    aggregate.session == null ||
                    aggregate.session.loadoutKind == CombatLoadoutKind.Consumable &&
                    aggregate.session.broughtConsumable == null)
                {
                    return false;
                }

                if (string.Equals(
                        aggregate.execution.occupationOwnerId,
                        aggregate.execution.executionId,
                        StringComparison.Ordinal))
                {
                    if (!string.IsNullOrWhiteSpace(BusyOwner))
                        return false;
                    BusyOwner = aggregate.execution.executionId;
                }
                else if (!string.Equals(
                             BusyOwner,
                             aggregate.execution.occupationOwnerId,
                             StringComparison.Ordinal))
                {
                    return false;
                }

                Aggregates.Add(CloneAggregate(aggregate));
                return true;
            }

            public void PublishCombatStartCommit()
            {
                if (StorageRevision > 0)
                    StorageEvents++;
            }

            public bool Save()
            {
                SaveCalls++;
                return !FailSave;
            }

            public FakeState CloneForLoad()
            {
                var clone = new FakeState
                {
                    Fatigue = Fatigue,
                    BusyOwner = BusyOwner,
                    ActiveLimit = ActiveLimit,
                    ActivityAvailable = ActivityAvailable,
                    StorageRevision = StorageRevision,
                    Activity = Clone(Activity)
                };
                foreach (var pair in _stacks)
                    clone._stacks.Add(pair.Key, Clone(pair.Value));
                foreach (var aggregate in Aggregates)
                    clone.Aggregates.Add(CloneAggregate(aggregate));
                foreach (var receipt in Receipts)
                    clone.Receipts.Add(Clone(receipt));
                return clone;
            }

            private Snapshot Capture()
            {
                return new Snapshot
                {
                    Fatigue = Fatigue,
                    BusyOwner = BusyOwner,
                    StorageRevision = StorageRevision,
                    Activity = Clone(Activity),
                    Stacks = CloneStacks(_stacks),
                    Aggregates = CloneAggregates(Aggregates),
                    Receipts = CloneList(Receipts)
                };
            }

            private void Restore(Snapshot snapshot)
            {
                Fatigue = snapshot.Fatigue;
                BusyOwner = snapshot.BusyOwner;
                StorageRevision = snapshot.StorageRevision;
                Activity = Clone(snapshot.Activity);
                _stacks.Clear();
                foreach (var pair in snapshot.Stacks)
                    _stacks.Add(pair.Key, Clone(pair.Value));
                Aggregates.Clear();
                Aggregates.AddRange(CloneAggregates(snapshot.Aggregates));
                Receipts.Clear();
                Receipts.AddRange(CloneList(snapshot.Receipts));
            }

            private sealed class Snapshot
            {
                public int Fatigue;
                public string BusyOwner;
                public long StorageRevision;
                public ActivityExecutionSaveData Activity;
                public Dictionary<string, ItemStackSaveData> Stacks;
                public List<CombatRuntimeAggregate> Aggregates;
                public List<OperationReceiptSaveData> Receipts;
            }

            private static Dictionary<string, ItemStackSaveData> CloneStacks(
                Dictionary<string, ItemStackSaveData> source)
            {
                var result =
                    new Dictionary<string, ItemStackSaveData>(
                        StringComparer.Ordinal);
                foreach (var pair in source)
                    result.Add(pair.Key, Clone(pair.Value));
                return result;
            }

            private static List<T> CloneList<T>(List<T> source)
            {
                var result = new List<T>(source.Count);
                foreach (var value in source)
                    result.Add(Clone(value));
                return result;
            }

            private static List<CombatRuntimeAggregate> CloneAggregates(
                List<CombatRuntimeAggregate> source)
            {
                var result =
                    new List<CombatRuntimeAggregate>(source.Count);
                foreach (var value in source)
                    result.Add(CloneAggregate(value));
                return result;
            }

            private static CombatRuntimeAggregate CloneAggregate(
                CombatRuntimeAggregate source)
            {
                var result = Clone(source);
                if (result?.session != null &&
                    result.session.loadoutKind == CombatLoadoutKind.Empty)
                {
                    result.session.broughtConsumable = null;
                }

                return result;
            }

            private static T Clone<T>(T source)
            {
                if (ReferenceEquals(source, null))
                    return default;
                return JsonUtility.FromJson<T>(JsonUtility.ToJson(source));
            }
        }
    }
}
