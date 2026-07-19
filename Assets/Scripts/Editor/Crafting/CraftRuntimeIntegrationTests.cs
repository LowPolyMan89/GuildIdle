using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using GuildIdle.Activities;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Crafting;
using GuildIdle.Player;
using NUnit.Framework;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Editor.Crafting
{
    public sealed class CraftRuntimeIntegrationTests
    {
        private const int SaveSizeLimitBytes = 200 * 1024;
        private const string HeroId = "ren";
        private const string CampfireId = "building_campfire";
        private const string ProductionCraftId = "cook_roasted_rabbit_meat";

        [SetUp]
        public void SetUp()
        {
            SetPlayerState(null);
            InvalidateProductionFactory();
        }

        [TearDown]
        public void TearDown()
        {
            SetPlayerState(null);
            InvalidateProductionFactory();
        }

        [Test]
        public void GeneratedStage1CraftRunsEndToEndThroughProductionComposition()
        {
            var database = LoadGeneratedConfigs().BuildDatabase();
            var storage = new MemorySaveStorage();
            var state = CreateProductionState(database, storage);
            Assert.That(state.SetBuildingLevel(CampfireId, 1), Is.True);
            Seed(state, "resource_rabbit_meat", 1);
            SetPlayerState(state);

            var startedEvents = new List<CraftStartedEvent>();
            var resultEvents = new List<CraftResultPendingEvent>();
            var resolvedEvents = new List<PendingResultResolvedEvent>();
            Action<CraftStartedEvent> startedHandler = startedEvents.Add;
            Action<CraftResultPendingEvent> resultHandler = resultEvents.Add;
            PlayerRuntimeComposition.CraftStarted += startedHandler;
            PlayerRuntimeComposition.CraftResultPending += resultHandler;
            try
            {
                var runtime = PlayerRuntimeComposition.CreateCraftRuntimeService();
                var pendingResults = PlayerRuntimeComposition.CreatePendingResultService();
                pendingResults.Resolved += resolvedEvents.Add;
                var request = Request(ProductionCraftId, "stage1-start", CampfireId, 1);
                var descriptor = runtime.GetStartDescriptor(request);

                Assert.That(descriptor.CanStart, Is.True, descriptor.BlockMessage);
                Assert.That(descriptor.DurationSeconds, Is.EqualTo(10));
                Assert.That(descriptor.OutputItemId, Is.EqualTo("consumable_roasted_rabbit_meat"));
                Assert.That(descriptor.OutputCount, Is.EqualTo(1));
                Assert.That(descriptor.SkillId, Is.EqualTo("skill_crafting"));
                Assert.That(descriptor.SkillExp, Is.EqualTo(2));
                Assert.That(descriptor.FatigueCost, Is.EqualTo(1));
                Assert.That(descriptor.PaidCosts, Has.Count.EqualTo(1));
                Assert.That(descriptor.PaidCosts[0].ItemId, Is.EqualTo("resource_rabbit_meat"));
                Assert.That(descriptor.PaidCosts[0].Quantity, Is.EqualTo(1));

                var fatigueBefore = state.GetHeroFatigue(HeroId);
                var started = runtime.Start(request);
                var replayedStart = runtime.Start(request);

                Assert.That(started.Success, Is.True, started.Message);
                Assert.That(replayedStart.Success, Is.True);
                Assert.That(replayedStart.Replayed, Is.True);
                Assert.That(replayedStart.ExecutionId, Is.EqualTo(started.ExecutionId));
                Assert.That(state.GetItem("resource_rabbit_meat"), Is.Zero);
                Assert.That(state.GetHeroFatigue(HeroId), Is.EqualTo(fatigueBefore - 1));
                Assert.That(state.GetHeroCurrentActivityExecutionId(HeroId), Is.EqualTo(started.ExecutionId));
                Assert.That(startedEvents, Has.Count.EqualTo(1));

                var completed = runtime.Advance(started.ExecutionId, 10d, "stage1-complete", 1);
                var replayedCompletion = runtime.Advance(started.ExecutionId, 10d, "stage1-complete", 1);

                Assert.That(completed.Success, Is.True, completed.Message);
                Assert.That(completed.Completed, Is.True);
                Assert.That(replayedCompletion.Success, Is.True);
                Assert.That(replayedCompletion.Replayed, Is.True);
                Assert.That(resultEvents, Has.Count.EqualTo(1));
                Assert.That(state.GetItem("consumable_roasted_rabbit_meat"), Is.Zero);
                Assert.That(state.GetHeroSkillExp(HeroId, "skill_crafting"), Is.Zero);
                Assert.That(state.GetHeroCurrentActivityExecutionId(HeroId), Is.EqualTo(started.ExecutionId));

                var pending = pendingResults.Get(completed.PendingResultId);
                Assert.That(pending, Is.Not.Null);
                AssertEntry(pending, RewardType.Item, "consumable_roasted_rabbit_meat", 1);
                AssertEntry(pending, RewardType.SkillExp, "skill_crafting", 2);
                var storageRevision = state.Storage.GetSnapshot().Revision;
                var claimed = pendingResults.ClaimAll("stage1-claim", pending.resultId, pending.revision, storageRevision);
                var replayedClaim = pendingResults.ClaimAll("stage1-claim", pending.resultId, pending.revision, storageRevision);

                Assert.That(claimed.Success, Is.True, claimed.Message);
                Assert.That(claimed.Resolved, Is.True);
                Assert.That(replayedClaim.Success, Is.True);
                Assert.That(replayedClaim.Replayed, Is.True);
                Assert.That(state.GetItem("consumable_roasted_rabbit_meat"), Is.EqualTo(1));
                Assert.That(state.GetHeroSkillExp(HeroId, "skill_crafting"), Is.EqualTo(2));
                Assert.That(state.GetCraftExecution(started.ExecutionId), Is.Null);
                Assert.That(state.GetHeroCurrentActivityExecutionId(HeroId), Is.Null.Or.Empty);
                Assert.That(FindSource(state, pending.resultId).state, Is.EqualTo(PendingResultSourceState.Resolved));
                Assert.That(resolvedEvents, Has.Count.EqualTo(1));

                var loaded = SaveService.Load(GetProductionFactory(), storage);
                Assert.That(loaded.GetItem("consumable_roasted_rabbit_meat"), Is.EqualTo(1));
                Assert.That(loaded.GetHeroSkillExp(HeroId, "skill_crafting"), Is.EqualTo(2));
                Assert.That(loaded.GetCraftExecution(started.ExecutionId), Is.Null);
                Assert.That(loaded.GetHeroCurrentActivityExecutionId(HeroId), Is.Null.Or.Empty);
                Assert.That(FindSource(loaded, pending.resultId).state, Is.EqualTo(PendingResultSourceState.Resolved));
            }
            finally
            {
                PlayerRuntimeComposition.CraftStarted -= startedHandler;
                PlayerRuntimeComposition.CraftResultPending -= resultHandler;
            }
        }

        [Test]
        public void AdditionalStationAndLevelCraftUsesTheSameProductionLifecycle()
        {
            const string craftId = "integration_generic_underwood_craft";
            const string stationId = "building_underwood";
            var configs = LoadGeneratedConfigs();
            configs.items.craftDefinitions = Append(configs.items.craftDefinitions, new CraftDefinitionConfigDto
            {
                craftId = craftId,
                targetItemId = "consumable_roasted_rabbit_meat",
                craftStationId = stationId,
                craftDurationSec = 3,
                craftSkillId = "skill_crafting",
                requiredBuildings = new[] { new RequiredBuildingDto { buildingId = stationId, level = 1 } },
                materials = new[] { new MaterialCostDto { id = "resource_pine_wood", count = 1 } },
                outputCount = 1,
                fatigueCost = 1,
                skillExp = 1
            });
            configs.buildings.buildingCraftables = Append(configs.buildings.buildingCraftables, new BuildingCraftableConfigDto
            {
                buildingId = stationId,
                buildingLevel = 1,
                craftId = craftId,
                sortOrder = 999,
                uiCategory = "Integration",
                enabled = true
            });

            var storage = new MemorySaveStorage();
            var state = CreateProductionState(configs.BuildDatabase(), storage);
            Assert.That(state.SetBuildingLevel(stationId, 1), Is.True);
            Assert.That(state.SetBuildingLevel(CampfireId, 1), Is.True);
            Seed(state, "resource_pine_wood", 1);
            SetPlayerState(state);
            var runtime = PlayerRuntimeComposition.CreateCraftRuntimeService();

            var wrongStation = runtime.GetStartDescriptor(Request(craftId, "generic-wrong-station", CampfireId, 1));
            Assert.That(wrongStation.CanStart, Is.False);
            Assert.That(wrongStation.BlockCode, Is.EqualTo(CraftStartCode.CraftUnavailableAtStationLevel));

            var started = runtime.Start(Request(craftId, "generic-start", stationId, 1));
            Assert.That(started.Success, Is.True, started.Message);
            Assert.That(started.Execution.stationBuildingId, Is.EqualTo(stationId));
            Assert.That(started.Execution.stationBuildingLevel, Is.EqualTo(1));
            Assert.That(started.Execution.durationSeconds, Is.EqualTo(3));
            Assert.That(state.GetItem("resource_pine_wood"), Is.Zero);

            var completed = runtime.Advance(started.ExecutionId, 3d, "generic-complete", 1);
            Assert.That(completed.Success, Is.True, completed.Message);
            var pending = PlayerRuntimeComposition.CreatePendingResultService().Get(completed.PendingResultId);
            var claimed = PlayerRuntimeComposition.CreatePendingResultService().ClaimAll(
                "generic-claim",
                pending.resultId,
                pending.revision,
                state.Storage.GetSnapshot().Revision);

            Assert.That(claimed.Success, Is.True, claimed.Message);
            Assert.That(claimed.Resolved, Is.True);
            Assert.That(state.GetItem("consumable_roasted_rabbit_meat"), Is.EqualTo(1));
            Assert.That(state.GetHeroSkillExp(HeroId, "skill_crafting"), Is.EqualTo(1));
            Assert.That(state.GetCraftExecution(started.ExecutionId), Is.Null);
            Assert.That(state.GetHeroCurrentActivityExecutionId(HeroId), Is.Null.Or.Empty);
        }

        [Test]
        public void ReceiptChurnUsesDurableExecutionAndResolvedSourceWithoutExceedingSaveLimit()
        {
            var storage = new MemorySaveStorage();
            var state = CreateProductionState(LoadGeneratedConfigs().BuildDatabase(), storage);
            Assert.That(state.SetBuildingLevel(CampfireId, 1), Is.True);
            Seed(state, "resource_rabbit_meat", 2);
            SetPlayerState(state);
            var runtime = PlayerRuntimeComposition.CreateCraftRuntimeService();
            var pendingResults = PlayerRuntimeComposition.CreatePendingResultService();
            var startRequest = Request(ProductionCraftId, "retention-start-anchor", CampfireId, 1);
            var started = runtime.Start(startRequest);
            Assert.That(started.Success, Is.True, started.Message);

            var firstAdvance = runtime.Advance(started.ExecutionId, 1d, "retention-anchor-nonzero", 1);
            Assert.That(firstAdvance.Success, Is.True, firstAdvance.Message);
            for (var index = 0; index < 64; index++)
            {
                var advanced = runtime.Advance(started.ExecutionId, 0d, $"retention-anchor-zero-{index}", index + 2L);
                Assert.That(advanced.Success, Is.True, advanced.Message);
            }

            Assert.That(state.GetCraftExecution(started.ExecutionId).advanceReceipts, Has.Length.EqualTo(64));
            Assert.That(state.GetCraftExecution(started.ExecutionId).advanceReceipts[0].operationKey, Is.Not.EqualTo("retention-anchor-nonzero"));
            var loadedAfterAdvanceEviction = SaveService.Load(GetProductionFactory(), storage);
            SetPlayerState(loadedAfterAdvanceEviction);
            state = loadedAfterAdvanceEviction;
            runtime = PlayerRuntimeComposition.CreateCraftRuntimeService();
            pendingResults = PlayerRuntimeComposition.CreatePendingResultService();
            var resultEvents = new List<CraftResultPendingEvent>();
            Action<CraftResultPendingEvent> resultHandler = resultEvents.Add;
            PlayerRuntimeComposition.CraftResultPending += resultHandler;
            CraftAdvanceResult replayedEvictedAdvance;
            try
            {
                replayedEvictedAdvance = runtime.Advance(started.ExecutionId, 1d, "retention-anchor-nonzero", 1);
            }
            finally
            {
                PlayerRuntimeComposition.CraftResultPending -= resultHandler;
            }
            Assert.That(replayedEvictedAdvance.Success, Is.True, replayedEvictedAdvance.Message);
            Assert.That(replayedEvictedAdvance.Replayed, Is.True);
            Assert.That(replayedEvictedAdvance.Completed, Is.False);
            Assert.That(state.GetCraftExecution(started.ExecutionId).progressSeconds, Is.EqualTo(1f));
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
            Assert.That(state.GetItem("consumable_roasted_rabbit_meat"), Is.Zero);
            Assert.That(state.GetHeroSkillExp(HeroId, "skill_crafting"), Is.Zero);
            Assert.That(state.GetHeroCurrentActivityExecutionId(HeroId), Is.EqualTo(started.ExecutionId));
            Assert.That(resultEvents, Is.Empty);

            ChurnStorageReceipts(state, "retention-anchor-storage", 33);
            Assert.That(state.ToSaveData().operationReceipts, Has.Length.EqualTo(64));
            Assert.That(HasReceipt(state, "craft-start", startRequest.OperationKey), Is.False);
            var replayedStart = runtime.Start(startRequest);
            Assert.That(replayedStart.Success, Is.True);
            Assert.That(replayedStart.Replayed, Is.True);
            Assert.That(replayedStart.ExecutionId, Is.EqualTo(started.ExecutionId));
            Assert.That(state.GetItem("resource_rabbit_meat"), Is.EqualTo(1));

            var completed = runtime.Advance(started.ExecutionId, 9d, "retention-anchor-complete", 66);
            Assert.That(completed.Success, Is.True, completed.Message);
            var pending = pendingResults.Get(completed.PendingResultId);
            var skillEntry = FindEntry(pending, RewardType.SkillExp);
            var discarded = pendingResults.DiscardQuantity(
                "retention-discard-partial",
                pending.resultId,
                skillEntry.entryId,
                1,
                pending.revision);
            Assert.That(discarded.Success, Is.True, discarded.Message);
            Assert.That(discarded.Resolved, Is.False);

            var finalExpectedRevision = discarded.ResultRevision;
            var finalExpectedStorageRevision = state.Storage.GetSnapshot().Revision;
            const string finalOperationId = "retention-claim-final";
            var claimed = pendingResults.ClaimAll(
                finalOperationId,
                pending.resultId,
                finalExpectedRevision,
                finalExpectedStorageRevision);
            Assert.That(claimed.Success, Is.True, claimed.Message);
            Assert.That(claimed.Resolved, Is.True);
            Assert.That(state.GetItem("consumable_roasted_rabbit_meat"), Is.EqualTo(1));
            Assert.That(state.GetHeroSkillExp(HeroId, "skill_crafting"), Is.EqualTo(1));
            Assert.That(state.GetCraftExecution(started.ExecutionId), Is.Null);

            var second = runtime.Start(Request(ProductionCraftId, "retention-start-second", CampfireId, 1));
            Assert.That(second.Success, Is.True, second.Message);
            for (var index = 0; index < 65; index++)
            {
                var advanced = runtime.Advance(second.ExecutionId, 0d, $"retention-second-zero-{index}", index + 1L);
                Assert.That(advanced.Success, Is.True, advanced.Message);
            }

            Assert.That(state.GetCraftExecution(second.ExecutionId).advanceReceipts, Has.Length.EqualTo(64));
            ChurnStorageReceipts(state, "retention-second-storage", 33);
            Assert.That(HasReceipt(state, pending.resultId, finalOperationId), Is.False);
            var itemBeforeReplay = state.GetItem("consumable_roasted_rabbit_meat");
            var expBeforeReplay = state.GetHeroSkillExp(HeroId, "skill_crafting");
            var replayedFinal = pendingResults.ClaimAll(
                finalOperationId,
                pending.resultId,
                finalExpectedRevision,
                finalExpectedStorageRevision);

            Assert.That(replayedFinal.Success, Is.True, replayedFinal.Message);
            Assert.That(replayedFinal.Replayed, Is.True);
            Assert.That(replayedFinal.Resolved, Is.True);
            Assert.That(state.GetItem("consumable_roasted_rabbit_meat"), Is.EqualTo(itemBeforeReplay));
            Assert.That(state.GetHeroSkillExp(HeroId, "skill_crafting"), Is.EqualTo(expBeforeReplay));
            Assert.That(state.GetCraftExecution(started.ExecutionId), Is.Null);
            Assert.That(state.GetCraftExecution(second.ExecutionId), Is.Not.Null);
            Assert.That(state.GetHeroCurrentActivityExecutionId(HeroId), Is.EqualTo(second.ExecutionId));
            Assert.That(state.PendingResults.GetAll(), Is.Empty);
            Assert.That(FindSource(state, pending.resultId).state, Is.EqualTo(PendingResultSourceState.Resolved));

            var saveJson = JsonUtility.ToJson(state.ToSaveData());
            Assert.That(Encoding.UTF8.GetByteCount(saveJson), Is.LessThan(SaveSizeLimitBytes));
            Assert.That(state.ToSaveData().operationReceipts, Has.Length.EqualTo(64));

            var loaded = SaveService.Load(GetProductionFactory(), storage);
            Assert.That(loaded.GetItem("consumable_roasted_rabbit_meat"), Is.EqualTo(itemBeforeReplay));
            Assert.That(loaded.GetHeroSkillExp(HeroId, "skill_crafting"), Is.EqualTo(expBeforeReplay));
            Assert.That(loaded.GetCraftExecution(started.ExecutionId), Is.Null);
            Assert.That(loaded.GetCraftExecution(second.ExecutionId), Is.Not.Null);
            Assert.That(loaded.GetHeroCurrentActivityExecutionId(HeroId), Is.EqualTo(second.ExecutionId));
            Assert.That(loaded.PendingResults.GetAll(), Is.Empty);
            Assert.That(FindSource(loaded, pending.resultId).state, Is.EqualTo(PendingResultSourceState.Resolved));
            Assert.That(Encoding.UTF8.GetByteCount(JsonUtility.ToJson(loaded.ToSaveData())), Is.LessThan(SaveSizeLimitBytes));

            var replayedAfterLoad = loaded.PendingResults.ClaimAll(
                finalOperationId,
                pending.resultId,
                finalExpectedRevision,
                finalExpectedStorageRevision);
            Assert.That(replayedAfterLoad.Success, Is.True, replayedAfterLoad.Message);
            Assert.That(replayedAfterLoad.Replayed, Is.True);
            Assert.That(replayedAfterLoad.Resolved, Is.True);
            Assert.That(loaded.GetHeroCurrentActivityExecutionId(HeroId), Is.EqualTo(second.ExecutionId));

            var oversizedSave = loaded.ToSaveData();
            var oversizedExecution = oversizedSave.craftRuntime.executions[0];
            oversizedExecution.advanceReceipts = Append(
                oversizedExecution.advanceReceipts,
                new CraftAdvanceReceiptSaveData
                {
                    operationSequence = oversizedExecution.lastAdvanceSequence + 1,
                    operationKey = "retention-imported-overflow",
                    fingerprint = "execution:imported|delta:0",
                    code = CraftAdvanceCode.Applied
                });
            oversizedExecution.lastAdvanceSequence++;
            var normalized = GetProductionFactory().Create(oversizedSave);
            var normalizedReceipts = normalized.GetCraftExecution(second.ExecutionId).advanceReceipts;
            Assert.That(normalizedReceipts, Has.Length.EqualTo(64));
            Assert.That(normalizedReceipts[63].operationKey, Is.EqualTo("retention-imported-overflow"));

            SetPlayerState(loaded);
            var loadedRuntime = PlayerRuntimeComposition.CreateCraftRuntimeService();
            var completedSecond = loadedRuntime.Advance(second.ExecutionId, 10d, "retention-second-complete", 66);
            Assert.That(completedSecond.Success, Is.True, completedSecond.Message);
            var secondResult = loaded.PendingResults.Get(completedSecond.PendingResultId);
            Assert.That(loaded.PendingResults.DiscardAll(
                "retention-second-discard",
                secondResult.resultId,
                secondResult.revision).Success, Is.True);

            for (var index = 0; index < 64; index++)
            {
                Seed(loaded, "resource_rabbit_meat", 1);
                var churnedStart = loadedRuntime.Start(Request(
                    ProductionCraftId,
                    $"retention-source-start-{index}",
                    CampfireId,
                    1));
                Assert.That(churnedStart.Success, Is.True, churnedStart.Message);
                var churnedCompletion = loadedRuntime.Advance(
                    churnedStart.ExecutionId,
                    10d,
                    $"retention-source-complete-{index}",
                    1);
                Assert.That(churnedCompletion.Success, Is.True, churnedCompletion.Message);
                var churnedResult = loaded.PendingResults.Get(churnedCompletion.PendingResultId);
                var churnedDiscard = loaded.PendingResults.DiscardAll(
                    $"retention-source-discard-{index}",
                    churnedResult.resultId,
                    churnedResult.revision);
                Assert.That(churnedDiscard.Success, Is.True, churnedDiscard.Message);
            }

            var retainedSave = loaded.ToSaveData();
            Assert.That(retainedSave.resultSources, Has.Length.EqualTo(64));
            Assert.That(retainedSave.operationReceipts, Has.Length.EqualTo(64));
            Assert.That(loaded.GetCraftExecutions(), Is.Empty);
            Assert.That(loaded.PendingResults.GetAll(), Is.Empty);
            Assert.That(loaded.GetHeroCurrentActivityExecutionId(HeroId), Is.Null.Or.Empty);
            Assert.That(Encoding.UTF8.GetByteCount(JsonUtility.ToJson(retainedSave)), Is.LessThan(SaveSizeLimitBytes));
        }

        [Test]
        public void MixedCraftAndCombatResolvedSourcesRetainNewestBySequenceAndEvictedClaimCannotRepeatRewards()
        {
            var storage = new MemorySaveStorage();
            var state = CreateProductionState(LoadGeneratedConfigs().BuildDatabase(), storage);
            Assert.That(state.SetBuildingLevel(CampfireId, 1), Is.True);
            SetPlayerState(state);
            var runtime = PlayerRuntimeComposition.CreateCraftRuntimeService();
            var pendingResults = PlayerRuntimeComposition.CreatePendingResultService();
            var anchor = pendingResults.CreateOrAppend(
                "mixed-anchor-form",
                CombatDraft("mixed-anchor", "resource_pine_wood"),
                true);
            Assert.That(anchor.Success, Is.True, anchor.Message);
            const string anchorClaimOperation = "mixed-anchor-claim";
            var anchorRevision = anchor.Result.revision;
            var anchorStorageRevision = state.Storage.GetSnapshot().Revision;
            var anchorClaim = pendingResults.ClaimAll(
                anchorClaimOperation,
                anchor.Result.resultId,
                anchorRevision,
                anchorStorageRevision);
            Assert.That(anchorClaim.Success, Is.True, anchorClaim.Message);
            Assert.That(state.GetItem("resource_pine_wood"), Is.EqualTo(1));

            for (var index = 0; index < 65; index++)
            {
                if (index % 2 == 0)
                {
                    Seed(state, "resource_rabbit_meat", 1);
                    var started = runtime.Start(Request(
                        ProductionCraftId,
                        $"mixed-craft-start-{index}",
                        CampfireId,
                        1));
                    Assert.That(started.Success, Is.True, started.Message);
                    var completed = runtime.Advance(started.ExecutionId, 10d, $"mixed-craft-complete-{index}", 1);
                    Assert.That(completed.Success, Is.True, completed.Message);
                    var discarded = pendingResults.DiscardAll(
                        $"mixed-craft-discard-{index}",
                        completed.PendingResultId,
                        pendingResults.Get(completed.PendingResultId).revision);
                    Assert.That(discarded.Success, Is.True, discarded.Message);
                }
                else
                {
                    var formed = pendingResults.CreateOrAppend(
                        $"mixed-combat-form-{index}",
                        CombatDraft($"mixed-combat-{index}", "resource_pine_wood"),
                        true);
                    Assert.That(formed.Success, Is.True, formed.Message);
                    var discarded = pendingResults.DiscardAll(
                        $"mixed-combat-discard-{index}",
                        formed.Result.resultId,
                        formed.Result.revision);
                    Assert.That(discarded.Success, Is.True, discarded.Message);
                }
            }

            var retained = state.ToSaveData().resultSources;
            Assert.That(retained, Has.Length.EqualTo(64));
            var sourceTypes = new HashSet<string>(StringComparer.Ordinal);
            var sequences = new HashSet<long>();
            var minSequence = long.MaxValue;
            var maxSequence = 0L;
            foreach (var source in retained)
            {
                sourceTypes.Add(source.sourceType);
                Assert.That(source.resolutionSequence, Is.GreaterThan(0));
                Assert.That(sequences.Add(source.resolutionSequence), Is.True);
                minSequence = Math.Min(minSequence, source.resolutionSequence);
                maxSequence = Math.Max(maxSequence, source.resolutionSequence);
            }
            Assert.That(sourceTypes, Does.Contain(PendingResultSourceType.Craft));
            Assert.That(sourceTypes, Does.Contain(PendingResultSourceType.Combat));
            Assert.That(minSequence, Is.EqualTo(3));
            Assert.That(maxSequence, Is.EqualTo(66));
            Assert.That(FindSourceOrNull(state, anchor.Result.resultId), Is.Null);
            Assert.That(HasReceipt(state, anchor.Result.resultId, anchorClaimOperation), Is.False);

            var loaded = SaveService.Load(GetProductionFactory(), storage);
            var itemBeforeReplay = loaded.GetItem("resource_pine_wood");
            var replay = loaded.PendingResults.ClaimAll(
                anchorClaimOperation,
                anchor.Result.resultId,
                anchorRevision,
                anchorStorageRevision);
            Assert.That(replay.Success, Is.False);
            Assert.That(replay.Code, Is.EqualTo("ResultNotFound"));
            Assert.That(loaded.GetItem("resource_pine_wood"), Is.EqualTo(itemBeforeReplay));
            Assert.That(loaded.PendingResults.GetAll(), Is.Empty);
            Assert.That(loaded.GetCraftExecutions(), Is.Empty);
            Assert.That(loaded.GetHeroCurrentActivityExecutionId(HeroId), Is.Null.Or.Empty);
            Assert.That(Encoding.UTF8.GetByteCount(JsonUtility.ToJson(loaded.ToSaveData())), Is.LessThan(SaveSizeLimitBytes));
        }

        private static PendingResultDraft CombatDraft(string sourceExecutionId, string itemId)
        {
            return new PendingResultDraft
            {
                SourceType = PendingResultSourceType.Combat,
                SourceId = "mixed-retention-combat",
                SourceExecutionId = sourceExecutionId,
                Entries = new[]
                {
                    new PendingResultEntryDraft
                    {
                        RewardType = RewardType.Item,
                        TargetId = itemId,
                        Quantity = 1,
                        Origin = PendingResultOrigin.CombatLoot
                    }
                }
            };
        }

        private static PlayerState CreateProductionState(ConfigDatabase database, ISaveStorage storage)
        {
            RuntimeConfigs.SetDatabaseForTests(database);
            InvalidateProductionFactory();
            return SaveService.Load(GetProductionFactory(), storage);
        }

        private static PlayerStateFactory GetProductionFactory()
        {
            var method = typeof(PlayerRuntimeComposition).GetMethod(
                "GetPlayerStateFactory",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (PlayerStateFactory)method.Invoke(null, null);
        }

        private static void InvalidateProductionFactory()
        {
            var method = typeof(PlayerRuntimeComposition).GetMethod(
                "InvalidatePlayerStateFactory",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, null);
        }

        private static void SetPlayerState(PlayerState state)
        {
            var field = typeof(global::GuildIdle.Player.Player).GetField(
                "_state",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null);
            field.SetValue(null, state);
        }

        private static CraftStartRequest Request(
            string craftId,
            string operationKey,
            string stationId,
            int stationLevel)
        {
            return new CraftStartRequest
            {
                CraftId = craftId,
                HeroId = HeroId,
                StationBuildingId = stationId,
                StationBuildingLevel = stationLevel,
                OperationKey = operationKey
            };
        }

        private static void Seed(PlayerState state, string itemId, int quantity)
        {
            var result = state.Storage.Add(
                $"integration-seed:{itemId}:{Guid.NewGuid():N}",
                state.Storage.GetSnapshot().Revision,
                itemId,
                quantity);
            Assert.That(result.Success, Is.True, result.Message);
        }

        private static void AssertEntry(PendingResultSaveData result, string rewardType, string targetId, long quantity)
        {
            var entry = FindEntry(result, rewardType);
            Assert.That(entry.targetId, Is.EqualTo(targetId));
            Assert.That(entry.quantity, Is.EqualTo(quantity));
            Assert.That(entry.origin, Is.EqualTo(PendingResultOrigin.CraftOutput));
        }

        private static PendingResultEntrySaveData FindEntry(PendingResultSaveData result, string rewardType)
        {
            foreach (var entry in result?.entries ?? Array.Empty<PendingResultEntrySaveData>())
                if (entry != null && string.Equals(entry.rewardType, rewardType, StringComparison.Ordinal))
                    return entry;
            Assert.Fail($"Missing PendingResult entry '{rewardType}'.");
            return null;
        }

        private static PendingResultSourceReferenceSaveData FindSource(PlayerState state, string resultId)
        {
            var source = FindSourceOrNull(state, resultId);
            if (source != null)
                return source;
            Assert.Fail($"Missing PendingResult source reference '{resultId}'.");
            return null;
        }

        private static PendingResultSourceReferenceSaveData FindSourceOrNull(PlayerState state, string resultId)
        {
            foreach (var source in state.ToSaveData().resultSources)
                if (source != null && string.Equals(source.resultId, resultId, StringComparison.Ordinal))
                    return source;
            return null;
        }

        private static bool HasReceipt(PlayerState state, string aggregateId, string operationId)
        {
            foreach (var receipt in state.ToSaveData().operationReceipts)
            {
                if (receipt != null && string.Equals(receipt.aggregateId, aggregateId, StringComparison.Ordinal) &&
                    string.Equals(receipt.operationId, operationId, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static void ChurnStorageReceipts(PlayerState state, string operationPrefix, int pairs)
        {
            for (var index = 0; index < pairs; index++)
            {
                var added = state.Storage.Add(
                    $"{operationPrefix}:add:{index}",
                    state.Storage.GetSnapshot().Revision,
                    "resource_pine_wood",
                    1);
                Assert.That(added.Success, Is.True, added.Message);
                var removed = state.Storage.Remove(
                    $"{operationPrefix}:remove:{index}",
                    state.Storage.GetSnapshot().Revision,
                    "resource_pine_wood",
                    1);
                Assert.That(removed.Success, Is.True, removed.Message);
            }
        }

        private static T[] Append<T>(T[] source, T item)
        {
            source ??= Array.Empty<T>();
            var result = new T[source.Length + 1];
            Array.Copy(source, result, source.Length);
            result[source.Length] = item;
            return result;
        }

        private static GeneratedConfigs LoadGeneratedConfigs()
        {
            var root = Path.Combine(Application.dataPath, "StreamingAssets", "Configs");
            return new GeneratedConfigs
            {
                items = ReadConfig<ItemsRuntimeConfigDto>(root, "items_configs.runtime.json"),
                heroes = ReadConfig<HeroesRuntimeConfigDto>(root, "heroes_configs.runtime.json"),
                activities = ReadConfig<ActivitiesRuntimeConfigDto>(root, "activity_configs.runtime.json"),
                buildings = ReadConfig<BuildingsRuntimeConfigDto>(root, "buildings_configs.runtime.json"),
                quests = ReadConfig<QuestRuntimeConfigDto>(root, "quest_configs.runtime.json"),
                enemies = ReadConfig<EnemiesRuntimeConfigDto>(root, "enemies_configs.runtime.json"),
                formulas = ReadConfig<FormulaRuntimeConfigDto>(root, "formula_configs.runtime.json"),
                loot = ReadConfig<LootRuntimeConfigDto>(root, "loot_configs.runtime.json"),
                map = ReadConfig<MapRuntimeConfigDto>(root, "map_configs.runtime.json"),
                storage = ReadConfig<StorageRuntimeConfigDto>(root, "storage_configs.runtime.json"),
                localisation = ReadConfig<LocalisationRuntimeConfigDto>(root, "localisation_configs.runtime.json")
            };
        }

        private static T ReadConfig<T>(string root, string fileName)
            where T : class
        {
            var path = Path.Combine(root, fileName);
            Assert.That(File.Exists(path), Is.True, $"Missing generated runtime config '{path}'.");
            var config = JsonUtility.FromJson<T>(File.ReadAllText(path, Encoding.UTF8));
            Assert.That(config, Is.Not.Null, $"Could not parse generated runtime config '{path}'.");
            return config;
        }

        private sealed class GeneratedConfigs
        {
            public ItemsRuntimeConfigDto items;
            public HeroesRuntimeConfigDto heroes;
            public ActivitiesRuntimeConfigDto activities;
            public BuildingsRuntimeConfigDto buildings;
            public QuestRuntimeConfigDto quests;
            public EnemiesRuntimeConfigDto enemies;
            public FormulaRuntimeConfigDto formulas;
            public LootRuntimeConfigDto loot;
            public MapRuntimeConfigDto map;
            public StorageRuntimeConfigDto storage;
            public LocalisationRuntimeConfigDto localisation;

            public ConfigDatabase BuildDatabase()
            {
                return new ConfigDatabase(
                    items,
                    heroes,
                    activities,
                    buildings,
                    quests,
                    enemies,
                    formulas,
                    loot,
                    map,
                    storage,
                    localisation);
            }
        }

        private sealed class MemorySaveStorage : ISaveStorage
        {
            private string _json;
            public bool HasKey(string key) => _json != null;
            public string GetString(string key, string defaultValue) => _json ?? defaultValue;
            public void SetString(string key, string value) => _json = value;
            public void DeleteKey(string key) => _json = null;
            public void Save() { }
        }
    }
}
