using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Editor.Configs;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Player.Editor
{
    public sealed class StoragePendingResultTests
    {
        private PlayerStateFactory _factory;
        private MemorySaveStorage _storage;
        private PlayerState _state;

        [SetUp]
        public void SetUp()
        {
            var database = new TestConfigDatabaseBuilder().WithFullPlayerStateTestData().Build();
            RuntimeConfigs.SetDatabaseForTests(database);
            _factory = TestPlayerComposition.CreatePlayerStateFactory(database);
            _storage = new MemorySaveStorage();
            _state = SaveService.Load(_factory, _storage);
        }

        [Test]
        public void PreviewBatchAccountsForEarlierEntriesWithoutMutatingStorage()
        {
            var filled = _state.Storage.Add("fill-nineteen-slots", _state.Storage.GetSnapshot().Revision, "resource_pine_wood", 1900);
            Assert.That(filled.Success, Is.True);
            var before = _state.ToSaveData();

            var preview = _state.Storage.PreviewBatch(
                new StorageAddRequest { ItemId = "consumable_hunting_potion", Quantity = 1 },
                new StorageAddRequest { ItemId = "item_wooden_club", Quantity = 1, Quality = 3 });

            Assert.That(preview.FitsAll, Is.False);
            Assert.That(preview.Entries[0].AcceptedQuantity, Is.EqualTo(1));
            Assert.That(preview.Entries[1].AcceptedQuantity, Is.Zero);
            Assert.That(preview.RequiredNewSlots, Is.EqualTo(1));
            Assert.That(_state.Storage.GetSnapshot().Revision, Is.EqualTo(before.storageRevision));
            Assert.That(_state.GetItem("consumable_hunting_potion"), Is.Zero);
        }

        [Test]
        public void CapacityRequiresAnExistingFunctionalCurrentLevelState()
        {
            var empty = _factory.Create(new SaveData
            {
                saveVersion = SaveData.CurrentSaveVersion,
                currentStageId = "stage_arrival"
            });

            Assert.That(empty.Storage.GetSnapshot().Capacity, Is.Zero);
            Assert.That(empty.UnlockBuilding("building_warehouse"), Is.True);
            Assert.That(empty.Storage.GetSnapshot().Capacity, Is.Zero);
            Assert.That(empty.SetBuildingLevel("building_warehouse", 0), Is.True);
            Assert.That(empty.Storage.GetSnapshot().Capacity, Is.EqualTo(20));
            Assert.That(empty.UnlockBuilding("building_tavern"), Is.True);
            Assert.That(empty.SetBuildingLevel("building_tavern", 0), Is.True);
            Assert.That(empty.Storage.GetSnapshot().Capacity, Is.EqualTo(20));
        }

        [Test]
        public void EquipmentAddCreatesSeparateQualityInstances()
        {
            var added = _state.Storage.Add("add-quality-equipment", _state.Storage.GetSnapshot().Revision, "item_wooden_club", 2, 7);

            Assert.That(added.Success, Is.True);
            var instances = _state.Storage.GetSnapshot().Instances;
            Assert.That(instances, Has.Length.EqualTo(2));
            Assert.That(instances[0].instanceId, Is.Not.EqualTo(instances[1].instanceId));
            Assert.That(instances[0].quality, Is.EqualTo(7));
            Assert.That(instances[1].quality, Is.EqualTo(7));
        }

        [Test]
        public void EquipmentResultPreservesConcreteInstanceIdThroughClaim()
        {
            var execution = new ActivityExecutionSaveData
            {
                executionId = "execution-equipment-payload",
                activityId = "combat_first_map_node",
                heroId = "ren",
                status = ActivityRuntimeStatus.Running
            };
            Assert.That(_state.AddActivityExecution(execution), Is.True);
            var formed = _state.PendingResults.CreateOrAppend(
                "form-equipment-payload",
                new PendingResultDraft
                {
                    SourceType = PendingResultSourceType.Activity,
                    SourceId = execution.activityId,
                    SourceExecutionId = execution.executionId,
                    OwnerHeroId = execution.heroId,
                    Entries = new[]
                    {
                        new PendingResultEntryDraft
                        {
                            RewardType = "Equipment",
                            TargetId = "item_wooden_club",
                            Quantity = 1,
                            Quality = 9,
                            Origin = PendingResultOrigin.ActivityReward,
                            InstanceId = "equipment-reward-instance"
                        }
                    }
                },
                true);

            Assert.That(formed.Success, Is.True);
            Assert.That(formed.Result.entries[0].instanceId, Is.EqualTo("equipment-reward-instance"));
            var claimed = _state.PendingResults.ClaimAll(
                "claim-equipment-payload",
                formed.Result.resultId,
                formed.Result.revision,
                _state.Storage.GetSnapshot().Revision);

            Assert.That(claimed.Success, Is.True);
            Assert.That(_state.GetItemInstance("equipment-reward-instance"), Is.Not.Null);
            Assert.That(_state.GetItemInstance("equipment-reward-instance").quality, Is.EqualTo(9));
        }

        [Test]
        public void ConcreteEquipmentInstanceIdMustBeUniqueAcrossPendingResults()
        {
            var first = _state.PendingResults.CreateCombatResult(
                "form-first-concrete-equipment",
                new PendingResultDraft
                {
                    SourceId = "combat-source-a",
                    SourceExecutionId = "combat-execution-a",
                    Entries = new[]
                    {
                        new PendingResultEntryDraft
                        {
                            RewardType = "Equipment",
                            TargetId = "item_wooden_club",
                            Quantity = 1,
                            Origin = PendingResultOrigin.CombatLoot,
                            InstanceId = "shared-equipment-instance"
                        }
                    }
                },
                null,
                null,
                _state.Storage.GetSnapshot().Revision);
            var second = _state.PendingResults.CreateCombatResult(
                "form-second-concrete-equipment",
                new PendingResultDraft
                {
                    SourceId = "combat-source-b",
                    SourceExecutionId = "combat-execution-b",
                    Entries = new[]
                    {
                        new PendingResultEntryDraft
                        {
                            RewardType = "Equipment",
                            TargetId = "item_wooden_club",
                            Quantity = 1,
                            Origin = PendingResultOrigin.CombatLoot,
                            InstanceId = "shared-equipment-instance"
                        }
                    }
                },
                null,
                null,
                _state.Storage.GetSnapshot().Revision);

            Assert.That(first.Success, Is.True);
            Assert.That(second.Success, Is.False);
            Assert.That(second.Code, Is.EqualTo("InstanceConflict"));
            Assert.That(_state.PendingResults.GetAll(), Has.Length.EqualTo(1));
        }

        [Test]
        public void GeneratedEquipmentPayloadDoesNotMutateDraftAndReplaysByOperationId()
        {
            var execution = new ActivityExecutionSaveData
            {
                executionId = "execution-generated-equipment-payload",
                activityId = "combat_first_map_node",
                heroId = "ren",
                status = ActivityRuntimeStatus.Running
            };
            Assert.That(_state.AddActivityExecution(execution), Is.True);
            var entry = new PendingResultEntryDraft
            {
                RewardType = "Equipment",
                TargetId = "item_wooden_club",
                Quantity = 1,
                Quality = 4,
                Origin = PendingResultOrigin.ActivityReward
            };
            var draft = new PendingResultDraft
            {
                SourceType = PendingResultSourceType.Activity,
                SourceId = execution.activityId,
                SourceExecutionId = execution.executionId,
                OwnerHeroId = execution.heroId,
                Entries = new[] { entry }
            };

            var formed = _state.PendingResults.CreateOrAppend("form-generated-equipment-payload", draft, true);
            var replay = _state.PendingResults.CreateOrAppend("form-generated-equipment-payload", draft, true);

            Assert.That(formed.Success, Is.True);
            Assert.That(formed.Result.entries[0].instanceId, Is.Not.Empty);
            Assert.That(entry.InstanceId, Is.Null);
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.Result.entries[0].instanceId, Is.EqualTo(formed.Result.entries[0].instanceId));
        }

        [Test]
        public void LoadNormalizesOwnerContextPairsAgainstConfiguredStateSemantics()
        {
            var loaded = _factory.Create(new SaveData
            {
                saveVersion = SaveData.CurrentSaveVersion,
                currentStageId = "stage_arrival",
                itemStacks = new[]
                {
                    new ItemStackSaveData
                    {
                        stackId = "reserved-valid-context",
                        itemId = "resource_pine_wood",
                        quantity = 2,
                        stateId = "reserved_for_task",
                        ownerType = StorageOwnerType.Hero,
                        ownerId = "ren",
                        contextType = StorageContextType.ActivityExecution,
                        contextId = "activity-a"
                    },
                    new ItemStackSaveData
                    {
                        stackId = "reserved-broken-context",
                        itemId = "resource_pine_wood",
                        quantity = 1,
                        stateId = "reserved_for_task",
                        contextType = StorageContextType.ActivityExecution
                    }
                }
            });

            var valid = Array.Find(loaded.GetItemStacks(), stack => stack.stackId == "reserved-valid-context");
            var repaired = Array.Find(loaded.GetItemStacks(), stack => stack.stackId == "reserved-broken-context");
            Assert.That(valid.ownerType, Is.Null);
            Assert.That(valid.contextId, Is.EqualTo("activity-a"));
            Assert.That(repaired.stateId, Is.EqualTo("on_storage"));
            Assert.That(repaired.contextType, Is.Null);
            Assert.That(repaired.contextId, Is.Null);
        }

        [Test]
        public void AddSplitsByMaxStackAndDuplicateOperationReplays()
        {
            var revision = _state.Storage.GetSnapshot().Revision;
            var first = _state.Storage.Add("add-wood", revision, "resource_pine_wood", 150);
            var replay = _state.Storage.Add("add-wood", revision, "resource_pine_wood", 150);

            Assert.That(first.Success, Is.True);
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.StackId, Is.EqualTo(first.StackId));
            Assert.That(replay.Quantity, Is.EqualTo(first.Quantity));
            Assert.That(_state.GetItemStacks(), Has.Length.EqualTo(2));
            Assert.That(_state.GetItem("resource_pine_wood"), Is.EqualTo(150));
            Assert.That(_state.Storage.GetSnapshot().OccupiedSlots, Is.EqualTo(2));
        }

        [Test]
        public void ConflictingOperationIsRejectedAndEvictedRetryIsStoppedByRevision()
        {
            var initialRevision = _state.Storage.GetSnapshot().Revision;
            var first = _state.Storage.Add("old-operation", initialRevision, "resource_pine_wood", 1);
            var conflict = _state.Storage.Add("old-operation", initialRevision, "resource_pine_wood", 2);
            Assert.That(first.Success, Is.True);
            Assert.That(conflict.Success, Is.False);
            Assert.That(conflict.Code, Is.EqualTo("OperationConflict"));

            for (var index = 0; index < 64; index++)
            {
                var applied = _state.Storage.Add($"new-operation-{index}", _state.Storage.GetSnapshot().Revision, "resource_pine_wood", 1);
                Assert.That(applied.Success, Is.True);
            }

            var staleRetry = _state.Storage.Add("old-operation", initialRevision, "resource_pine_wood", 1);
            Assert.That(_state.ToSaveData().operationReceipts, Has.Length.EqualTo(64));
            Assert.That(staleRetry.Success, Is.False);
            Assert.That(staleRetry.Code, Is.EqualTo("StaleStorageRevision"));
            Assert.That(_state.GetItem("resource_pine_wood"), Is.EqualTo(65));
        }

        [Test]
        public void PartialReservationKeepsOriginalIdAndOwnContextRemainsAvailable()
        {
            var added = _state.Storage.Add("add-wood", _state.Storage.GetSnapshot().Revision, "resource_pine_wood", 30);
            var originalId = _state.GetItemStacks()[0].stackId;
            var owner = new StorageActionContext("ActivityExecution", "activity-a");
            var other = new StorageActionContext("ActivityExecution", "activity-b");

            var reserved = _state.Storage.Reserve("reserve-wood", added.StorageRevision, originalId, 10, owner);
            var replay = _state.Storage.Reserve("reserve-wood", added.StorageRevision, originalId, 10, owner);

            Assert.That(reserved.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(replay.StackId, Is.EqualTo(reserved.StackId));
            Assert.That(reserved.StackId, Is.Not.EqualTo(originalId));
            Assert.That(Array.Exists(_state.GetItemStacks(), stack => stack.stackId == originalId && stack.quantity == 20), Is.True);
            Assert.That(_state.GetItem("resource_pine_wood"), Is.EqualTo(30));
            Assert.That(_state.GetAvailableForActionCount("resource_pine_wood", owner), Is.EqualTo(30));
            Assert.That(_state.GetAvailableForActionCount("resource_pine_wood", other), Is.EqualTo(20));
        }

        [Test]
        public void PartialReservationCannotCreateAnOverflowSlot()
        {
            var filled = _state.Storage.Add("fill-for-reservation", _state.Storage.GetSnapshot().Revision, "resource_pine_wood", 2000);
            var original = _state.GetItemStacks()[0];

            var reserved = _state.Storage.Reserve(
                "reserve-without-slot",
                filled.StorageRevision,
                original.stackId,
                1,
                new StorageActionContext(StorageContextType.ActivityExecution, "activity-full"));

            Assert.That(reserved.Success, Is.False);
            Assert.That(_state.GetItemStacks(), Has.Length.EqualTo(20));
            Assert.That(_state.GetItem("resource_pine_wood"), Is.EqualTo(2000));
            Assert.That(_state.Storage.GetSnapshot().Revision, Is.EqualTo(filled.StorageRevision));
        }

        [Test]
        public void PartialReleaseMergesIntoAvailableStackWhenStorageIsFull()
        {
            var stacks = new ItemStackSaveData[20];
            stacks[0] = new ItemStackSaveData { stackId = "available", itemId = "resource_pine_wood", quantity = 50, stateId = "on_storage" };
            stacks[1] = new ItemStackSaveData
            {
                stackId = "reserved",
                itemId = "resource_pine_wood",
                quantity = 50,
                stateId = "reserved_for_task",
                contextType = StorageContextType.ActivityExecution,
                contextId = "activity-release"
            };
            for (var index = 2; index < stacks.Length; index++)
                stacks[index] = new ItemStackSaveData { stackId = $"full-{index:D2}", itemId = "resource_pine_wood", quantity = 100, stateId = "on_storage" };
            var state = _factory.Create(new SaveData
            {
                saveVersion = SaveData.CurrentSaveVersion,
                currentStageId = "stage_arrival",
                unlockedBuildings = new[] { "building_warehouse" },
                buildingLevels = new[] { new BuildingLevelSaveEntry { buildingId = "building_warehouse", level = 0 } },
                itemStacks = stacks
            });
            Assert.That(state.Storage.GetSnapshot().FreeSlots, Is.Zero);

            var released = state.Storage.Release(
                "release-into-existing",
                state.Storage.GetSnapshot().Revision,
                "reserved",
                25,
                new StorageActionContext(StorageContextType.ActivityExecution, "activity-release"));

            Assert.That(released.Success, Is.True);
            Assert.That(Array.Find(state.GetItemStacks(), stack => stack.stackId == "available").quantity, Is.EqualTo(75));
            Assert.That(Array.Find(state.GetItemStacks(), stack => stack.stackId == "reserved").quantity, Is.EqualTo(25));
            Assert.That(state.Storage.GetSnapshot().OccupiedSlots, Is.EqualTo(20));
        }

        [Test]
        public void ClaimAvailableTakesNonItemsFirstAndLeavesItemsWhenFull()
        {
            var fill = _state.Storage.Add("fill-storage", _state.Storage.GetSnapshot().Revision, "resource_pine_wood", 2000);
            Assert.That(fill.Success, Is.True);
            Assert.That(_state.Storage.GetSnapshot().FreeSlots, Is.Zero);

            var execution = new ActivityExecutionSaveData
            {
                executionId = "execution-result",
                activityId = "combat_first_map_node",
                heroId = "ren",
                status = ActivityRuntimeStatus.Running
            };
            Assert.That(_state.AddActivityExecution(execution), Is.True);
            var formed = _state.PendingResults.CreateOrAppend(
                "form-result",
                new PendingResultDraft
                {
                    SourceType = PendingResultSourceType.Activity,
                    SourceId = "combat_first_map_node",
                    SourceExecutionId = execution.executionId,
                    OwnerHeroId = "ren",
                    Entries = new[]
                    {
                        new PendingResultEntryDraft { SortOrder = 20, RewardType = "Consumable", TargetId = "consumable_hunting_potion", Quantity = 1, Origin = PendingResultOrigin.ActivityReward },
                        new PendingResultEntryDraft { SortOrder = 10, RewardType = "Currency", TargetId = "gold_id", Quantity = 2, Origin = PendingResultOrigin.ActivityReward }
                    }
                },
                true,
                0);

            var all = _state.PendingResults.ClaimAll("claim-all-full", formed.Result.resultId, formed.Result.revision, fill.StorageRevision);
            Assert.That(all.Success, Is.False);
            Assert.That(_state.GetCurrency("gold_id"), Is.Zero);
            Assert.That(_state.PendingResults.Get(formed.Result.resultId).revision, Is.EqualTo(formed.Result.revision));

            var claimed = _state.PendingResults.ClaimAvailable("claim-available", formed.Result.resultId, formed.Result.revision, fill.StorageRevision);

            Assert.That(claimed.Success, Is.True);
            Assert.That(claimed.Resolved, Is.False);
            Assert.That(_state.GetCurrency("gold_id"), Is.EqualTo(2));
            Assert.That(_state.GetItem("consumable_hunting_potion"), Is.Zero);
            Assert.That(claimed.Result.entries, Has.Length.EqualTo(1));
            Assert.That(claimed.Result.entries[0].targetId, Is.EqualTo("consumable_hunting_potion"));
        }

        [Test]
        public void ClaimQuantityUsesOperationIdAndResultRevision()
        {
            var execution = new ActivityExecutionSaveData
            {
                executionId = "execution-quantity",
                activityId = "combat_first_map_node",
                heroId = "ren",
                status = ActivityRuntimeStatus.Running
            };
            Assert.That(_state.AddActivityExecution(execution), Is.True);
            var formed = _state.PendingResults.CreateOrAppend(
                "form-quantity",
                new PendingResultDraft
                {
                    SourceType = PendingResultSourceType.Activity,
                    SourceId = "combat_first_map_node",
                    SourceExecutionId = execution.executionId,
                    OwnerHeroId = "ren",
                    Entries = new[] { new PendingResultEntryDraft { RewardType = "Resource", TargetId = "resource_pine_wood", Quantity = 10, Origin = PendingResultOrigin.ActivityReward } }
                },
                true,
                0);
            var entry = formed.Result.entries[0];
            var storageRevision = _state.Storage.GetSnapshot().Revision;

            var first = _state.PendingResults.ClaimQuantity("claim-five", formed.Result.resultId, entry.entryId, 5, formed.Result.revision, storageRevision);
            var replay = _state.PendingResults.ClaimQuantity("claim-five", formed.Result.resultId, entry.entryId, 5, formed.Result.revision, storageRevision);
            var stale = _state.PendingResults.ClaimQuantity("claim-five-stale", formed.Result.resultId, entry.entryId, 1, formed.Result.revision, first.StorageRevision);

            Assert.That(first.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(stale.Success, Is.False);
            Assert.That(stale.Code, Is.EqualTo("StaleResultRevision"));
            Assert.That(_state.GetItem("resource_pine_wood"), Is.EqualTo(5));
            Assert.That(first.Result.entries[0].quantity, Is.EqualTo(5));
        }

        [Test]
        public void NonItemClaimDoesNotRequireCurrentStorageRevision()
        {
            var execution = new ActivityExecutionSaveData
            {
                executionId = "execution-currency",
                activityId = "combat_first_map_node",
                heroId = "ren",
                status = ActivityRuntimeStatus.Running
            };
            Assert.That(_state.AddActivityExecution(execution), Is.True);
            var formed = _state.PendingResults.CreateOrAppend(
                "form-currency",
                new PendingResultDraft
                {
                    SourceType = PendingResultSourceType.Activity,
                    SourceId = execution.activityId,
                    SourceExecutionId = execution.executionId,
                    OwnerHeroId = execution.heroId,
                    Entries = new[] { new PendingResultEntryDraft { RewardType = "Currency", TargetId = "gold_id", Quantity = 3, Origin = PendingResultOrigin.ActivityReward } }
                },
                true);
            var oldStorageRevision = _state.Storage.GetSnapshot().Revision;
            Assert.That(_state.Storage.Add("unrelated-storage-change", oldStorageRevision, "resource_pine_wood", 1).Success, Is.True);

            var claim = _state.PendingResults.ClaimQuantity(
                "claim-currency",
                formed.Result.resultId,
                formed.Result.entries[0].entryId,
                3,
                formed.Result.revision,
                oldStorageRevision);

            Assert.That(claim.Success, Is.True);
            Assert.That(claim.Resolved, Is.True);
            Assert.That(_state.GetCurrency("gold_id"), Is.EqualTo(3));
        }

        [Test]
        public void ClaimAvailableRollsBackAllNonItemsWhenOneGrantFails()
        {
            var execution = new ActivityExecutionSaveData
            {
                executionId = "execution-non-item-rollback",
                activityId = "combat_first_map_node",
                heroId = "ren",
                status = ActivityRuntimeStatus.Running
            };
            Assert.That(_state.AddActivityExecution(execution), Is.True);
            var formed = _state.PendingResults.CreateOrAppend(
                "form-non-item-rollback",
                new PendingResultDraft
                {
                    SourceType = PendingResultSourceType.Activity,
                    SourceId = execution.activityId,
                    SourceExecutionId = execution.executionId,
                    OwnerHeroId = execution.heroId,
                    Entries = new[]
                    {
                        new PendingResultEntryDraft { SortOrder = 0, RewardType = "Currency", TargetId = "gold_id", Quantity = 3, Origin = PendingResultOrigin.ActivityReward },
                        new PendingResultEntryDraft { SortOrder = 1, RewardType = "SkillExp", TargetId = "skill_gathering", Quantity = (long)int.MaxValue + 1, Origin = PendingResultOrigin.ActivityReward }
                    }
                },
                true);

            var claim = _state.PendingResults.ClaimAvailable(
                "claim-non-item-rollback",
                formed.Result.resultId,
                formed.Result.revision,
                _state.Storage.GetSnapshot().Revision);

            Assert.That(claim.Success, Is.False);
            Assert.That(claim.Code, Is.EqualTo("Rejected"));
            Assert.That(_state.GetCurrency("gold_id"), Is.Zero);
            Assert.That(_state.GetHeroSkillExp("ren", "skill_gathering"), Is.Zero);
            Assert.That(_state.PendingResults.Get(formed.Result.resultId).revision, Is.EqualTo(formed.Result.revision));
            Assert.That(_state.PendingResults.Get(formed.Result.resultId).entries, Has.Length.EqualTo(2));
        }

        [Test]
        public void CraftDefinitionRewardIsRejectedInsteadOfEnteringItemPath()
        {
            var execution = new ActivityExecutionSaveData
            {
                executionId = "execution-invalid-craft",
                activityId = "combat_first_map_node",
                heroId = "ren",
                status = ActivityRuntimeStatus.Running
            };
            Assert.That(_state.AddActivityExecution(execution), Is.True);

            var formed = _state.PendingResults.CreateOrAppend(
                "form-invalid-craft",
                new PendingResultDraft
                {
                    SourceType = PendingResultSourceType.Activity,
                    SourceId = execution.activityId,
                    SourceExecutionId = execution.executionId,
                    Entries = new[] { new PendingResultEntryDraft { RewardType = "CraftDefinition", TargetId = "resource_pine_wood", Quantity = 1, Origin = PendingResultOrigin.CraftOutput } }
                },
                true);

            Assert.That(formed.Success, Is.False);
            Assert.That(formed.Code, Is.EqualTo("UnsupportedRewardType"));
            Assert.That(_state.PendingResults.GetAll(), Is.Empty);
            Assert.That(_state.GetActivityExecution(execution.executionId).status, Is.EqualTo(ActivityRuntimeStatus.Running));
        }

        [Test]
        public void CustomSourceHandlerCanBeRegisteredWithoutChangingPlayerState()
        {
            var handler = new TestPendingResultSourceHandler();
            _state.PendingResults.RegisterSourceHandler(handler);
            var formed = _state.PendingResults.CreateOrAppend(
                "form-custom-source",
                new PendingResultDraft
                {
                    SourceType = handler.SourceType,
                    SourceId = "future-source",
                    SourceExecutionId = "future-execution",
                    Entries = new[] { new PendingResultEntryDraft { RewardType = "Currency", TargetId = "gold_id", Quantity = 1, Origin = PendingResultOrigin.CraftOutput } }
                },
                true);
            var discarded = _state.PendingResults.DiscardAll("discard-custom-source", formed.Result.resultId, formed.Result.revision);

            Assert.That(formed.Success, Is.True);
            Assert.That(handler.BindCalls, Is.EqualTo(1));
            Assert.That(discarded.Success, Is.True);
            Assert.That(handler.ResolveCalls, Is.EqualTo(1));
        }

        [Test]
        public void CustomSourceHandlerCanBeComposedBeforePendingResultsAreLoaded()
        {
            var database = new TestConfigDatabaseBuilder().WithFullPlayerStateTestData().Build();
            RuntimeConfigs.SetDatabaseForTests(database);
            var handler = new TestPendingResultSourceHandler();
            var factory = TestPlayerComposition.CreatePlayerStateFactory(
                database,
                new Func<PlayerState, IPendingResultSourceHandler>[] { _ => handler });
            var state = factory.Create(new SaveData
            {
                saveVersion = SaveData.CurrentSaveVersion,
                currentStageId = "stage_arrival",
                pendingResults = new[]
                {
                    new PendingResultSaveData
                    {
                        resultId = "result:FutureSource:future-load",
                        sourceType = handler.SourceType,
                        sourceId = "future-source",
                        sourceExecutionId = "future-load",
                        state = PendingResultState.ResultPending,
                        revision = 1,
                        entries = new[]
                        {
                            new PendingResultEntrySaveData
                            {
                                entryId = "future-entry",
                                rewardType = "Currency",
                                targetId = "gold_id",
                                quantity = 1,
                                origin = PendingResultOrigin.CraftOutput
                            }
                        }
                    }
                }
            });

            Assert.That(state.PendingResults.Get("result:FutureSource:future-load"), Is.Not.Null);
            Assert.That(handler.BindCalls, Is.EqualTo(1));
        }

        [Test]
        public void DiscardQuantityThenDiscardAllResolvesWithoutGrantingRewards()
        {
            var execution = new ActivityExecutionSaveData
            {
                executionId = "execution-discard",
                activityId = "combat_first_map_node",
                heroId = "ren",
                status = ActivityRuntimeStatus.Running
            };
            Assert.That(_state.AddActivityExecution(execution), Is.True);
            var formed = _state.PendingResults.CreateOrAppend(
                "form-discard",
                new PendingResultDraft
                {
                    SourceType = PendingResultSourceType.Activity,
                    SourceId = execution.activityId,
                    SourceExecutionId = execution.executionId,
                    OwnerHeroId = execution.heroId,
                    Entries = new[]
                    {
                        new PendingResultEntryDraft { RewardType = "Resource", TargetId = "resource_pine_wood", Quantity = 5, Origin = PendingResultOrigin.ActivityReward },
                        new PendingResultEntryDraft { RewardType = "Currency", TargetId = "gold_id", Quantity = 2, Origin = PendingResultOrigin.ActivityReward }
                    }
                },
                true);
            var itemEntry = Array.Find(formed.Result.entries, entry => entry.targetId == "resource_pine_wood");

            var partial = _state.PendingResults.DiscardQuantity("discard-two", formed.Result.resultId, itemEntry.entryId, 2, formed.Result.revision);
            var all = _state.PendingResults.DiscardAll("discard-rest", formed.Result.resultId, partial.ResultRevision);

            Assert.That(partial.Success, Is.True);
            Assert.That(partial.Result.entries[0].quantity, Is.EqualTo(3));
            Assert.That(all.Success, Is.True);
            Assert.That(all.Resolved, Is.True);
            Assert.That(_state.PendingResults.Get(formed.Result.resultId), Is.Null);
            Assert.That(_state.GetItem("resource_pine_wood"), Is.Zero);
            Assert.That(_state.GetCurrency("gold_id"), Is.Zero);
            Assert.That(_state.GetActivityExecution(execution.executionId), Is.Null);
        }

        [Test]
        public void QuestCompletesAndGrantsFlagsOnlyAfterResultResolution()
        {
            const string instanceId = "story:quest_build_hut";
            Assert.That(_state.SetQuestInstance(new QuestInstanceSaveData
            {
                instanceId = instanceId,
                questId = "quest_build_hut",
                status = QuestInstanceStatus.Active
            }), Is.True);
            var formed = _state.PendingResults.CreateOrAppend(
                "form-quest-reward",
                new PendingResultDraft
                {
                    SourceType = PendingResultSourceType.Quest,
                    SourceId = "quest_build_hut",
                    SourceExecutionId = instanceId,
                    Entries = new[] { new PendingResultEntryDraft { RewardType = "Currency", TargetId = "gold_id", Quantity = 5, Origin = PendingResultOrigin.QuestReward } }
                },
                true);

            Assert.That(formed.Success, Is.True);
            Assert.That(_state.GetQuestInstance(instanceId).status, Is.EqualTo(QuestInstanceStatus.RewardPending));
            Assert.That(_state.GetQuestInstance(instanceId).rewardsGranted, Is.False);
            PendingResultResolvedEvent resolvedEvent = null;
            _state.PendingResults.Resolved += value => resolvedEvent = value;
            var claim = _state.PendingResults.ClaimAll(
                "claim-quest-reward",
                formed.Result.resultId,
                formed.Result.revision,
                _state.Storage.GetSnapshot().Revision);

            Assert.That(claim.Success, Is.True);
            Assert.That(_state.GetQuestInstance(instanceId).status, Is.EqualTo(QuestInstanceStatus.Completed));
            Assert.That(_state.GetQuestInstance(instanceId).rewardsGranted, Is.True);
            Assert.That(_state.GetCurrency("gold_id"), Is.EqualTo(5));
            Assert.That(resolvedEvent?.SourceExecutionId, Is.EqualTo(instanceId));
        }

        [Test]
        public void EmptyQuestRewardUsesFormationPathAndResolvesImmediately()
        {
            const string instanceId = "story:quest_clear_underwood";
            Assert.That(_state.SetQuestInstance(new QuestInstanceSaveData
            {
                instanceId = instanceId,
                questId = "quest_clear_underwood",
                status = QuestInstanceStatus.Active
            }), Is.True);

            var formed = _state.PendingResults.CreateOrAppend(
                "form-empty-quest-reward",
                new PendingResultDraft
                {
                    SourceType = PendingResultSourceType.Quest,
                    SourceId = "quest_clear_underwood",
                    SourceExecutionId = instanceId
                },
                true);

            Assert.That(formed.Success, Is.True);
            Assert.That(formed.ResolvedImmediately, Is.True);
            Assert.That(formed.Result, Is.Null);
            Assert.That(_state.PendingResults.GetAll(), Is.Empty);
            Assert.That(_state.GetQuestInstance(instanceId).status, Is.EqualTo(QuestInstanceStatus.Completed));
            Assert.That(_state.GetQuestInstance(instanceId).rewardsGranted, Is.True);
        }

        [Test]
        public void SaveV8RoundtripRestoresPendingResultSourceAndReceipt()
        {
            var execution = new ActivityExecutionSaveData
            {
                executionId = "execution-roundtrip",
                activityId = "combat_first_map_node",
                heroId = "ren",
                status = ActivityRuntimeStatus.Running
            };
            Assert.That(_state.AddActivityExecution(execution), Is.True);
            var formed = _state.PendingResults.CreateOrAppend(
                "form-roundtrip",
                new PendingResultDraft
                {
                    SourceType = PendingResultSourceType.Activity,
                    SourceId = execution.activityId,
                    SourceExecutionId = execution.executionId,
                    OwnerHeroId = execution.heroId,
                    Entries = new[]
                    {
                        new PendingResultEntryDraft { RewardType = "Resource", TargetId = "resource_pine_wood", Quantity = 4, Origin = PendingResultOrigin.ActivityReward },
                        new PendingResultEntryDraft { RewardType = "Equipment", TargetId = "item_wooden_club", Quantity = 1, Quality = 6, Origin = PendingResultOrigin.ActivityReward }
                    }
                },
                true);
            Assert.That(formed.Success, Is.True);
            var equipmentInstanceId = formed.Result.entries[1].instanceId;
            Assert.That(_state.Save(), Is.True);

            var restored = SaveService.Load(_factory, _storage, out var origin);
            var replay = restored.PendingResults.CreateOrAppend("form-roundtrip", new PendingResultDraft
            {
                SourceType = PendingResultSourceType.Activity,
                SourceId = execution.activityId,
                SourceExecutionId = execution.executionId,
                OwnerHeroId = execution.heroId,
                Entries = new[]
                {
                    new PendingResultEntryDraft { RewardType = "Resource", TargetId = "resource_pine_wood", Quantity = 4, Origin = PendingResultOrigin.ActivityReward },
                    new PendingResultEntryDraft { RewardType = "Equipment", TargetId = "item_wooden_club", Quantity = 1, Quality = 6, Origin = PendingResultOrigin.ActivityReward }
                }
            }, true);

            Assert.That(origin, Is.EqualTo(SaveLoadOrigin.ExistingV8));
            Assert.That(restored.GetActivityExecution(execution.executionId).status, Is.EqualTo(ActivityRuntimeStatus.ResultPending));
            Assert.That(restored.PendingResults.Get(formed.Result.resultId).entries[0].quantity, Is.EqualTo(4));
            Assert.That(restored.PendingResults.Get(formed.Result.resultId).entries[1].instanceId, Is.EqualTo(equipmentInstanceId));
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
        }

        [Test]
        public void FormationSaveFailureRollsBackResultAndSourceState()
        {
            var execution = new ActivityExecutionSaveData
            {
                executionId = "execution-save-failure",
                activityId = "combat_first_map_node",
                heroId = "ren",
                status = ActivityRuntimeStatus.Running
            };
            Assert.That(_state.AddActivityExecution(execution), Is.True);
            _storage.ThrowOnSet = true;
            LogAssert.Expect(LogType.Error, "[SaveService] Failed to save player state. simulated save failure");

            var formed = _state.PendingResults.CreateOrAppend(
                "form-save-failure",
                new PendingResultDraft
                {
                    SourceType = PendingResultSourceType.Activity,
                    SourceId = execution.activityId,
                    SourceExecutionId = execution.executionId,
                    OwnerHeroId = execution.heroId,
                    Entries = new[] { new PendingResultEntryDraft { RewardType = "Resource", TargetId = "resource_pine_wood", Quantity = 1, Origin = PendingResultOrigin.ActivityReward } }
                },
                true);

            Assert.That(formed.Success, Is.False);
            Assert.That(formed.Code, Is.EqualTo("SaveFailed"));
            Assert.That(_state.PendingResults.GetAll(), Is.Empty);
            Assert.That(_state.GetActivityExecution(execution.executionId).status, Is.EqualTo(ActivityRuntimeStatus.Running));
            Assert.That(_state.GetActivityExecution(execution.executionId).pendingResultId, Is.Null);
        }

        [Test]
        public void CombatTransferConsumesOnceAndReturnsRemainderOnlyThroughClaim()
        {
            var added = _state.Storage.Add("add-consumables", _state.Storage.GetSnapshot().Revision, "consumable_hunting_potion", 10);
            var original = _state.GetItemStacks()[0];
            var context = new StorageActionContext("CombatExecution", "combat-a");
            var transferred = _state.Storage.TransferToAction("transfer-consumables", added.StorageRevision, original.stackId, 5, context);
            var consumed = _state.Storage.ConsumeTransferredStack("consume-two", transferred.StorageRevision, transferred.StackId, 2, context);
            var replay = _state.Storage.ConsumeTransferredStack("consume-two", transferred.StorageRevision, transferred.StackId, 2, context);

            Assert.That(consumed.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(_state.GetItem("consumable_hunting_potion"), Is.EqualTo(5));

            var formed = _state.PendingResults.CreateCombatResult(
                "combat-result",
                new PendingResultDraft { SourceId = "combat-source", SourceExecutionId = "combat-a" },
                transferred.StackId,
                context,
                consumed.StorageRevision);

            Assert.That(formed.Success, Is.True);
            Assert.That(formed.Result.entries, Has.Length.EqualTo(1));
            Assert.That(formed.Result.entries[0].origin, Is.EqualTo(PendingResultOrigin.BroughtConsumable));
            Assert.That(formed.Result.entries[0].quantity, Is.EqualTo(3));
            Assert.That(_state.GetItem("consumable_hunting_potion"), Is.EqualTo(5));

            var claim = _state.PendingResults.ClaimAll("claim-combat", formed.Result.resultId, formed.Result.revision, _state.Storage.GetSnapshot().Revision);
            Assert.That(claim.Success, Is.True);
            Assert.That(_state.GetItem("consumable_hunting_potion"), Is.EqualTo(8));
            Assert.That(_state.PendingResults.Get(formed.Result.resultId), Is.Null);
            Assert.That(_state.ToSaveData().resultSources[0].state, Is.EqualTo(PendingResultSourceState.Resolved));

            var restored = SaveService.Load(_factory, _storage);
            var secondFormation = restored.PendingResults.CreateCombatResult(
                "combat-result-second-attempt",
                new PendingResultDraft { SourceId = "combat-source", SourceExecutionId = "combat-a" },
                null,
                context,
                restored.Storage.GetSnapshot().Revision);
            Assert.That(secondFormation.Success, Is.False);
            Assert.That(secondFormation.Code, Is.EqualTo("SourceTransitionFailed"));
        }

        private sealed class MemorySaveStorage : ISaveStorage
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>();
            public bool ThrowOnSet { get; set; }
            public bool HasKey(string key) => _values.ContainsKey(key);
            public string GetString(string key, string defaultValue) => _values.TryGetValue(key, out var value) ? value : defaultValue;
            public void SetString(string key, string value)
            {
                if (ThrowOnSet)
                    throw new InvalidOperationException("simulated save failure");
                _values[key] = value;
            }
            public void DeleteKey(string key) => _values.Remove(key);
            public void Save() { }
        }

        private sealed class TestPendingResultSourceHandler : IPendingResultSourceHandler
        {
            private string _resultId;
            public string SourceType => "FutureSource";
            public int BindCalls { get; private set; }
            public int ResolveCalls { get; private set; }
            public bool AcceptsOrigin(string origin) => origin == PendingResultOrigin.CraftOutput;
            public bool TryBind(PendingResultSaveData result, bool makeClaimable, PendingResultBindMode mode)
            {
                BindCalls++;
                _resultId = result?.resultId;
                return true;
            }
            public bool CanClaim(PendingResultSaveData result) => result != null && result.resultId == _resultId;
            public bool Resolve(PendingResultSaveData result)
            {
                if (!CanClaim(result)) return false;
                ResolveCalls++;
                return true;
            }
        }
    }
}
