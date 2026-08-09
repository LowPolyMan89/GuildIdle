using System;
using System.Collections.Generic;
using System.Globalization;
using GuildIdle.Activities;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Player;
using UnityEngine;

namespace GuildIdle.Crafting
{
    public interface ICraftPlayerState
    {
        SaveData CaptureCheckpoint();
        void RestoreCheckpoint(SaveData checkpoint);
        bool TryGetOperationReceipt(string aggregateId, string operationId, out OperationReceiptSaveData receipt);
        void RecordOperationReceipt(OperationReceiptSaveData receipt);
        bool HasHero(string heroId);
        bool HasHeroState(string heroId);
        int GetHeroFatigue(string heroId);
        bool SpendHeroFatigue(string heroId, int amount);
        bool IsHeroBusy(string heroId);
        string GetHeroOccupationOwnerId(string heroId);
        int GetActiveHeroCount();
        int GetActiveHeroLimit();
        bool TryOccupyHero(string heroId, string executionId);
        bool IsBuildingUnlocked(string buildingId);
        int GetBuildingLevel(string buildingId);
        int GetAvailableForCraftCount(string itemId);
        bool TryConsumeCraftCost(string itemId, int quantity, out string error);
        void PublishCraftStartCommit();
        CraftExecutionSaveData[] GetCraftExecutions();
        CraftExecutionSaveData GetCraftExecution(string executionId);
        bool AddCraftExecution(CraftExecutionSaveData execution);
        bool UpdateCraftExecution(CraftExecutionSaveData execution);
        PendingResultSaveData GetPendingResult(string resultId);
        ITransactionalPendingResultService TransactionalPendingResults { get; }
        bool Save();
    }

    public sealed class CraftRuntimeService
    {
        private const string StartReceiptAggregateId = "craft-start";
        private readonly CraftsConfigRepository _configs;
        private readonly ICraftPlayerState _state;
        private readonly ITransactionalPendingResultService _transactionalPendingResults;
        private readonly CraftAdvanceCore _advanceCore;
        private readonly Action<CraftStartedEvent> _eventSink;
        private readonly Action<CraftResultPendingEvent> _resultPendingEventSink;

        public CraftRuntimeService(
            CraftsConfigRepository configs,
            ICraftPlayerState state,
            Action<CraftStartedEvent> eventSink = null,
            Action<CraftResultPendingEvent> resultPendingEventSink = null)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _transactionalPendingResults = state.TransactionalPendingResults ??
                                           throw new ArgumentException(
                                               "PendingResult service must support outer transactions.",
                                               nameof(state));
            _advanceCore = new CraftAdvanceCore(state, _transactionalPendingResults);
            _eventSink = eventSink;
            _resultPendingEventSink = resultPendingEventSink;
        }

        public CraftStartDescriptor GetStartDescriptor(CraftStartRequest request)
        {
            try
            {
                return Preflight(request, false).Descriptor;
            }
            catch (Exception exception)
            {
                return Blocked(request, CraftStartCode.TransactionFailure, $"Craft preflight failed: {exception.Message}", BuildFingerprint(request)).Descriptor;
            }
        }

        public CraftStartResult Start(CraftStartRequest request)
        {
            PreflightResult preflight;
            try
            {
                preflight = Preflight(request, true);
            }
            catch (Exception exception)
            {
                return Failure(CraftStartCode.TransactionFailure, $"Craft preflight failed: {exception.Message}", null);
            }
            if (preflight.ReplayedExecution != null)
            {
                return new CraftStartResult
                {
                    Success = true,
                    Replayed = true,
                    Code = CraftStartCode.Replayed,
                    ExecutionId = preflight.ReplayedExecution.executionId,
                    Descriptor = preflight.Descriptor,
                    Execution = CloneExecution(preflight.ReplayedExecution)
                };
            }
            if (preflight.Descriptor == null || !preflight.Descriptor.CanStart)
                return Failure(preflight.Descriptor);

            var checkpoint = _state.CaptureCheckpoint();
            var executionId = Guid.NewGuid().ToString("N");
            CraftExecutionSaveData execution = null;
            var committed = false;
            try
            {
                for (var index = 0; index < preflight.Descriptor.PaidCosts.Count; index++)
                {
                    var cost = preflight.Descriptor.PaidCosts[index];
                    if (!_state.TryConsumeCraftCost(
                            cost.ItemId,
                            cost.Quantity,
                            out var error))
                    {
                        _state.RestoreCheckpoint(checkpoint);
                        return Failure(CraftStartCode.TransactionFailure, error ?? $"Failed to consume craft cost '{cost.ItemId}'.", preflight.Descriptor);
                    }
                }

                if (preflight.Descriptor.FatigueCost > 0 &&
                    !_state.SpendHeroFatigue(request.HeroId, preflight.Descriptor.FatigueCost))
                {
                    _state.RestoreCheckpoint(checkpoint);
                    return Failure(CraftStartCode.TransactionFailure, "Failed to spend hero fatigue after a successful preflight.", preflight.Descriptor);
                }

                if (!_state.TryOccupyHero(request.HeroId, executionId))
                {
                    _state.RestoreCheckpoint(checkpoint);
                    return Failure(CraftStartCode.TransactionFailure, "Failed to acquire the hero occupation owner.", preflight.Descriptor);
                }

                execution = CreateExecution(executionId, request.OperationKey, preflight.Fingerprint, preflight.Descriptor);
                if (!_state.AddCraftExecution(execution))
                {
                    _state.RestoreCheckpoint(checkpoint);
                    return Failure(CraftStartCode.TransactionFailure, "Failed to create CraftExecution.", preflight.Descriptor);
                }

                _state.RecordOperationReceipt(new OperationReceiptSaveData
                {
                    aggregateId = StartReceiptAggregateId,
                    operationId = request.OperationKey,
                    fingerprint = preflight.Fingerprint,
                    success = true,
                    code = CraftStartCode.Applied,
                    executionId = executionId,
                    resultPayload = JsonUtility.ToJson(execution)
                });

                if (!_state.Save())
                {
                    _state.RestoreCheckpoint(checkpoint);
                    return Failure(CraftStartCode.SaveFailure, "Failed to save the Running CraftExecution transaction.", preflight.Descriptor);
                }
                committed = true;

                var stored = _state.GetCraftExecution(executionId) ?? execution;
                PublishStorageCommit();
                PublishStarted(stored);
                return new CraftStartResult
                {
                    Success = true,
                    Code = CraftStartCode.Applied,
                    ExecutionId = executionId,
                    Descriptor = preflight.Descriptor,
                    Execution = CloneExecution(stored)
                };
            }
            catch (Exception exception)
            {
                if (committed)
                {
                    Debug.LogException(exception);
                    return new CraftStartResult
                    {
                        Success = true,
                        Code = CraftStartCode.Applied,
                        ExecutionId = executionId,
                        Descriptor = preflight.Descriptor,
                        Execution = CloneExecution(execution)
                    };
                }
                _state.RestoreCheckpoint(checkpoint);
                return Failure(CraftStartCode.TransactionFailure, $"Craft start transaction failed: {exception.Message}", preflight.Descriptor);
            }
        }

        public CraftAdvanceResult Advance(string executionId, double deltaSeconds, string operationKey)
        {
            return AdvanceFailure(
                CraftAdvanceCode.OperationSequenceRequired,
                "Craft advance requires an explicit positive operationSequence.",
                executionId);
        }

        public CraftAdvanceResult Advance(string executionId, double deltaSeconds, string operationKey, long operationSequence)
        {
            if (string.IsNullOrWhiteSpace(operationKey))
                return AdvanceFailure(CraftAdvanceCode.OperationKeyRequired, "Craft advance requires operationKey.", executionId);
            if (operationSequence <= 0)
                return AdvanceFailure(CraftAdvanceCode.OperationSequenceRequired, "Craft advance operationSequence must be positive.", executionId);
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0d)
                return AdvanceFailure(CraftAdvanceCode.InvalidDelta, "Craft advance delta must be finite and non-negative.", executionId);
            if (deltaSeconds == 0d)
                deltaSeconds = 0d;

            var execution = _state.GetCraftExecution(executionId);
            if (execution == null)
                return AdvanceFailure(CraftAdvanceCode.ExecutionNotFound, $"CraftExecution '{executionId}' was not found.", executionId);
            if (!TryValidateAdvanceExecution(_state, execution, out var validationError))
                return AdvanceFailure(CraftAdvanceCode.InvalidExecution, validationError, executionId, execution);

            var fingerprint = BuildAdvanceFingerprint(executionId, deltaSeconds, operationSequence);
            var savedReceipt = FindAdvanceReceipt(execution, operationKey);
            if (savedReceipt != null)
            {
                if (savedReceipt.operationSequence != operationSequence ||
                    !MatchesAdvanceFingerprint(savedReceipt, executionId, deltaSeconds, fingerprint))
                    return AdvanceFailure(CraftAdvanceCode.OperationReplayConflict, "operationKey was already used with another craft advance payload.", executionId, execution);
                if (execution.status == CraftExecutionStatus.ResultPending && !HasValidPendingResult(_state, execution))
                    return AdvanceFailure(CraftAdvanceCode.DataIntegrityFailure, "ResultPending CraftExecution has no valid linked Craft Result.", executionId, execution);
                return AdvanceReplay(execution, savedReceipt);
            }

            savedReceipt = FindAdvanceReceipt(execution, operationSequence);
            if (savedReceipt != null)
            {
                if (!string.Equals(savedReceipt.operationKey, operationKey, StringComparison.Ordinal) ||
                    !MatchesAdvanceFingerprint(savedReceipt, executionId, deltaSeconds, fingerprint))
                    return AdvanceFailure(CraftAdvanceCode.OperationReplayConflict, "operationSequence was already used with another craft advance payload.", executionId, execution);
                if (execution.status == CraftExecutionStatus.ResultPending && !HasValidPendingResult(_state, execution))
                    return AdvanceFailure(CraftAdvanceCode.DataIntegrityFailure, "ResultPending CraftExecution has no valid linked Craft Result.", executionId, execution);
                return AdvanceReplay(execution, savedReceipt);
            }

            if (operationSequence <= execution.lastAdvanceSequence)
            {
                if (execution.status == CraftExecutionStatus.ResultPending && !HasValidPendingResult(_state, execution))
                    return AdvanceFailure(CraftAdvanceCode.DataIntegrityFailure, "ResultPending CraftExecution has no valid linked Craft Result.", executionId, execution);
                return AdvanceEvictedReplay(execution, operationSequence);
            }
            if (operationSequence != execution.lastAdvanceSequence + 1)
                return AdvanceFailure(CraftAdvanceCode.OperationSequenceGap, "Craft advance operationSequence must be the next monotonic value.", executionId, execution);

            if (execution.status == CraftExecutionStatus.ResultPending)
            {
                if (!HasValidPendingResult(_state, execution))
                    return AdvanceFailure(CraftAdvanceCode.DataIntegrityFailure, "ResultPending CraftExecution has no valid linked Craft Result.", executionId, execution);
                return AdvanceSuccess(execution, CraftAdvanceCode.ResultPending, false);
            }

            var checkpoint = _state.CaptureCheckpoint();
            var committed = false;
            try
            {
                var coreResult = _advanceCore.Advance(
                    new CraftAdvanceRequest(executionId, deltaSeconds, operationKey));
                if (!coreResult.Success)
                {
                    _state.RestoreCheckpoint(checkpoint);
                    return AdvanceFailure(
                        MapCoreFailureCode(coreResult.StopReason),
                        FirstCoreIssue(coreResult),
                        executionId,
                        execution);
                }

                var next = CloneExecution(_state.GetCraftExecution(executionId));
                if (next == null)
                {
                    _state.RestoreCheckpoint(checkpoint);
                    return AdvanceFailure(
                        CraftAdvanceCode.TransactionFailure,
                        "Craft advance core did not leave an execution to record the online operation.",
                        executionId,
                        execution);
                }
                var completes = coreResult.Completed;
                next.lastAdvanceSequence = operationSequence;
                if (completes)
                    next.completionAdvanceSequence = operationSequence;
                AddAdvanceReceipt(next, operationSequence, operationKey, fingerprint, deltaSeconds, completes ? CraftAdvanceCode.ResultPending : CraftAdvanceCode.Applied,
                    completes ? next.pendingResultId : null);

                if (!_state.UpdateCraftExecution(next))
                {
                    _state.RestoreCheckpoint(checkpoint);
                    return AdvanceFailure(CraftAdvanceCode.TransactionFailure, "Failed to record the online craft advance operation.", executionId, execution);
                }

                if (!_state.Save())
                {
                    _state.RestoreCheckpoint(checkpoint);
                    return AdvanceFailure(CraftAdvanceCode.SaveFailure, "Failed to save CraftExecution progress.", executionId, execution);
                }
                committed = true;
                var stored = _state.GetCraftExecution(executionId);
                if (stored == null)
                {
                    Debug.LogError($"[CraftRuntime] Committed CraftExecution '{executionId}' is unavailable.");
                    return AdvanceFailure(CraftAdvanceCode.DataIntegrityFailure, "Committed CraftExecution is unavailable.", executionId);
                }
                PublishDeferred(coreResult);
                return AdvanceSuccess(
                    stored,
                    completes ? CraftAdvanceCode.ResultPending : CraftAdvanceCode.Applied,
                    false);
            }
            catch (Exception exception)
            {
                if (committed)
                {
                    Debug.LogException(exception);
                    var stored = _state.GetCraftExecution(executionId);
                    return stored == null
                        ? AdvanceFailure(CraftAdvanceCode.DataIntegrityFailure, "Committed CraftExecution is unavailable.", executionId)
                        : AdvanceSuccess(stored, stored.status == CraftExecutionStatus.ResultPending ? CraftAdvanceCode.ResultPending : CraftAdvanceCode.Applied, false);
                }
                _state.RestoreCheckpoint(checkpoint);
                return AdvanceFailure(CraftAdvanceCode.TransactionFailure, $"Craft advance transaction failed: {exception.Message}", executionId, execution);
            }
        }

        private PreflightResult Preflight(CraftStartRequest request, bool requireOperationKey)
        {
            request ??= new CraftStartRequest();
            var fingerprint = BuildFingerprint(request);
            if (requireOperationKey && string.IsNullOrWhiteSpace(request.OperationKey))
                return Blocked(request, CraftStartCode.OperationKeyRequired, "Craft start requires operationKey.", fingerprint);

            if (requireOperationKey)
            {
                var hasReceipt = _state.TryGetOperationReceipt(StartReceiptAggregateId, request.OperationKey, out var receipt);
                if (hasReceipt && !string.Equals(receipt.fingerprint, fingerprint, StringComparison.Ordinal))
                    return Blocked(request, CraftStartCode.OperationReplayConflict, "operationKey was already used with another craft start payload.", fingerprint);

                CraftExecutionSaveData replayed = null;
                var matchingExecutionCount = 0;
                foreach (var execution in _state.GetCraftExecutions() ?? Array.Empty<CraftExecutionSaveData>())
                {
                    if (execution == null || !string.Equals(execution.startOperationKey, request.OperationKey, StringComparison.Ordinal))
                        continue;
                    matchingExecutionCount++;
                    replayed = execution;
                }

                if (matchingExecutionCount > 1)
                    return Blocked(request, CraftStartCode.TransactionFailure, "Multiple CraftExecutions reference the same craft start operationKey.", fingerprint);

                if (replayed != null)
                {
                    if (!string.Equals(replayed.startFingerprint, fingerprint, StringComparison.Ordinal))
                        return Blocked(request, CraftStartCode.OperationReplayConflict, "operationKey was already used with another craft start payload.", fingerprint);
                    if (!IsValidReplayExecution(replayed, request, fingerprint) ||
                        hasReceipt && (!receipt.success || !string.Equals(receipt.executionId, replayed.executionId, StringComparison.Ordinal)))
                    {
                        return Blocked(request, CraftStartCode.TransactionFailure, "Craft start idempotency data does not reference a valid live execution.", fingerprint);
                    }

                    return new PreflightResult
                    {
                        Descriptor = DescriptorFromExecution(replayed),
                        Fingerprint = fingerprint,
                        ReplayedExecution = replayed
                    };
                }

                if (hasReceipt)
                    return Blocked(request, CraftStartCode.TransactionFailure, "Craft start receipt has no matching live execution.", fingerprint);
            }

            if (!_configs.TryGetDefinition(request.CraftId, out var definition))
                return Blocked(request, CraftStartCode.UnknownOrDisabledCraft, $"Craft '{request.CraftId}' is unknown or disabled.", fingerprint);

            if (!TryValidateDefinition(definition, out var definitionError))
                return Blocked(request, CraftStartCode.InvalidCraftDescriptor, definitionError, fingerprint, definition);
            if (request.PlannedCycles <= 0)
                return Blocked(request, CraftStartCode.InvalidCraftDescriptor, "Planned craft cycles must be positive.", fingerprint, definition);

            if (!_state.IsBuildingUnlocked(request.StationBuildingId) ||
                _state.GetBuildingLevel(request.StationBuildingId) != request.StationBuildingLevel)
            {
                return Blocked(request, CraftStartCode.StationUnavailable, "Requested craft station is locked or is not at the requested level.", fingerprint, definition);
            }

            if (!string.Equals(definition.CraftStationId, request.StationBuildingId, StringComparison.Ordinal) ||
                !IsAvailableAtStation(definition.CraftId, request.StationBuildingId, request.StationBuildingLevel))
            {
                return Blocked(request, CraftStartCode.CraftUnavailableAtStationLevel, "Craft is not enabled through Craftables for this station and level.", fingerprint, definition);
            }

            foreach (var requirement in definition.RequiredBuildings)
            {
                if (!_state.IsBuildingUnlocked(requirement.BuildingId) || _state.GetBuildingLevel(requirement.BuildingId) < requirement.Level)
                {
                    return Blocked(request, CraftStartCode.AdditionalBuildingUnavailable,
                        $"Required building '{requirement.BuildingId}' level {requirement.Level} is unavailable.", fingerprint, definition);
                }
            }

            if (!_state.HasHero(request.HeroId) || !_state.HasHeroState(request.HeroId))
                return Blocked(request, CraftStartCode.HeroNotFound, "Craft start requires an acquired hero with runtime state.", fingerprint, definition);

            foreach (var execution in _state.GetCraftExecutions() ?? Array.Empty<CraftExecutionSaveData>())
            {
                if (execution != null && IsActive(execution.status) &&
                    string.Equals(execution.heroId, request.HeroId, StringComparison.Ordinal) &&
                    string.Equals(execution.craftId, request.CraftId, StringComparison.Ordinal) &&
                    string.Equals(execution.stationBuildingId, request.StationBuildingId, StringComparison.Ordinal))
                {
                    return Blocked(request, CraftStartCode.ExecutionAlreadyActive, "A matching CraftExecution is already Running or ResultPending.", fingerprint, definition);
                }
            }

            if (_state.IsHeroBusy(request.HeroId))
                return Blocked(request, CraftStartCode.HeroBusy, $"Hero '{request.HeroId}' is already occupied.", fingerprint, definition);

            if (_state.GetActiveHeroCount() >= _state.GetActiveHeroLimit())
                return Blocked(request, CraftStartCode.ActiveHeroLimitReached, "Active hero limit has been reached.", fingerprint, definition);

            if (!TryBuildCosts(definition, out var baseCosts, out var baseRecipe, out var costErrorCode, out var costError))
                return Blocked(request, costErrorCode, costError, fingerprint, definition);
            var maxCycles = CalculateMaxCycles(definition, baseCosts, baseRecipe);
            if (request.PlannedCycles > maxCycles)
            {
                return Blocked(
                    request,
                    CraftStartCode.MissingMaterials,
                    $"Requested {request.PlannedCycles} craft cycles, but available ingredients allow {maxCycles}.",
                    fingerprint,
                    definition,
                    baseCosts,
                    baseRecipe,
                    maxCycles);
            }
            if (!TryScaleBatch(baseCosts, baseRecipe, request.PlannedCycles, out var costs, out var recipe))
                return Blocked(request, CraftStartCode.InvalidCraftDescriptor, "Craft batch totals overflow Int32.", fingerprint, definition, baseCosts, baseRecipe, maxCycles);

            var totalFatigue = checked(definition.FatigueCost * request.PlannedCycles);
            if (_state.GetHeroFatigue(request.HeroId) < totalFatigue)
                return Blocked(request, CraftStartCode.InsufficientFatigue, "Hero has insufficient fatigue for all planned cycles.", fingerprint, definition, costs, recipe, maxCycles);

            foreach (var cost in costs)
            {
                var required = cost.Quantity;
                if (!recipe.Consume && string.Equals(recipe.RequiredItemId, cost.ItemId, StringComparison.Ordinal))
                {
                    try
                    {
                        required = checked(required + recipe.RequiredCount);
                    }
                    catch (OverflowException)
                    {
                        return Blocked(request, CraftStartCode.InvalidCraftDescriptor, "Combined material and retained recipe requirement overflows Int32.", fingerprint, definition, costs, recipe);
                    }
                }
                if (_state.GetAvailableForCraftCount(cost.ItemId) < required)
                {
                    var code = string.Equals(cost.Kind, CraftPaidCostKind.Recipe, StringComparison.Ordinal)
                        ? CraftStartCode.MissingOrInvalidRecipe
                        : CraftStartCode.MissingMaterials;
                    return Blocked(request, code, $"Storage has insufficient available '{cost.ItemId}'.", fingerprint, definition, costs, recipe);
                }
            }

            if (!string.IsNullOrWhiteSpace(recipe.RequiredItemId) && !recipe.Consume &&
                !ContainsCost(costs, recipe.RequiredItemId) &&
                _state.GetAvailableForCraftCount(recipe.RequiredItemId) < recipe.RequiredCount)
            {
                return Blocked(request, CraftStartCode.MissingOrInvalidRecipe, $"Required recipe item '{recipe.RequiredItemId}' is unavailable.", fingerprint, definition, costs, recipe);
            }

            return new PreflightResult
            {
                Fingerprint = fingerprint,
                Descriptor = CreateDescriptor(request, definition, costs, recipe, true, CraftStartCode.Available, string.Empty, maxCycles)
            };
        }

        private int CalculateMaxCycles(
            CraftDefinitionDescriptor definition,
            IList<CraftCostDescriptor> costs,
            CraftRecipeDescriptor recipe)
        {
            var maximum = int.MaxValue;
            foreach (var cost in costs ?? Array.Empty<CraftCostDescriptor>())
            {
                if (cost == null || cost.Quantity <= 0)
                    continue;
                var available = _state.GetAvailableForCraftCount(cost.ItemId);
                if (!string.IsNullOrWhiteSpace(recipe?.RequiredItemId) &&
                    !recipe.Consume &&
                    string.Equals(recipe.RequiredItemId, cost.ItemId, StringComparison.Ordinal))
                {
                    available = Math.Max(0, available - recipe.RequiredCount);
                }
                maximum = Math.Min(maximum, available / cost.Quantity);
            }
            if (!string.IsNullOrWhiteSpace(recipe?.RequiredItemId) && !recipe.Consume)
            {
                if (_state.GetAvailableForCraftCount(recipe.RequiredItemId) < recipe.RequiredCount)
                    return 0;
            }
            maximum = LimitByProduct(maximum, definition.CraftDurationSec);
            maximum = LimitByProduct(maximum, definition.OutputCount);
            maximum = LimitByProduct(maximum, definition.SkillExp);
            maximum = LimitByProduct(maximum, definition.FatigueCost);
            return maximum == int.MaxValue ? 1 : Math.Max(0, maximum);
        }

        private static int LimitByProduct(int maximum, int perCycle)
        {
            return perCycle <= 0 ? maximum : Math.Min(maximum, int.MaxValue / perCycle);
        }

        private static bool TryScaleBatch(
            IList<CraftCostDescriptor> baseCosts,
            CraftRecipeDescriptor baseRecipe,
            int cycles,
            out List<CraftCostDescriptor> costs,
            out CraftRecipeDescriptor recipe)
        {
            costs = new List<CraftCostDescriptor>();
            recipe = null;
            try
            {
                foreach (var cost in baseCosts ?? Array.Empty<CraftCostDescriptor>())
                    costs.Add(new CraftCostDescriptor(cost.ItemId, checked(cost.Quantity * cycles), cost.Kind));
                var requiredCount = baseRecipe?.RequiredCount ?? 0;
                if (baseRecipe?.Consume == true)
                    requiredCount = checked(requiredCount * cycles);
                recipe = new CraftRecipeDescriptor(
                    baseRecipe?.RequiredItemId,
                    requiredCount,
                    baseRecipe?.Consume ?? false);
                return true;
            }
            catch (OverflowException)
            {
                costs.Clear();
                recipe = baseRecipe;
                return false;
            }
        }

        private bool IsAvailableAtStation(string craftId, string buildingId, int buildingLevel)
        {
            foreach (var available in _configs.GetAvailableCrafts(buildingId, buildingLevel))
                if (string.Equals(available.CraftId, craftId, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool TryValidateDefinition(CraftDefinitionDescriptor definition, out string error)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.CraftId) ||
                string.IsNullOrWhiteSpace(definition.TargetItemId) || string.IsNullOrWhiteSpace(definition.CraftStationId) ||
                definition.CraftDurationSec <= 0 || definition.OutputCount <= 0 || definition.FatigueCost < 0 ||
                string.IsNullOrWhiteSpace(definition.CraftSkillId) || definition.SkillExp < 0)
            {
                error = "Craft definition has an invalid immutable runtime payload.";
                return false;
            }
            error = null;
            return true;
        }

        private static bool TryBuildCosts(
            CraftDefinitionDescriptor definition,
            out List<CraftCostDescriptor> costs,
            out CraftRecipeDescriptor recipe,
            out string errorCode,
            out string error)
        {
            costs = new List<CraftCostDescriptor>();
            recipe = new CraftRecipeDescriptor(definition.RequiredRecipeItemId, definition.RequiredRecipeItemCount, definition.ConsumeRecipeItem);
            errorCode = null;
            error = null;
            var totals = new SortedDictionary<string, CostAccumulator>(StringComparer.Ordinal);
            try
            {
                foreach (var material in definition.Materials)
                {
                    if (material == null || string.IsNullOrWhiteSpace(material.ItemId) || material.Count <= 0)
                    {
                        errorCode = CraftStartCode.InvalidCraftDescriptor;
                        error = "Craft material descriptor is invalid.";
                        return false;
                    }
                    AddCost(totals, material.ItemId, material.Count, CraftPaidCostKind.Material);
                }

                var hasRecipe = !string.IsNullOrWhiteSpace(definition.RequiredRecipeItemId);
                if (hasRecipe != (definition.RequiredRecipeItemCount > 0) || (!hasRecipe && definition.ConsumeRecipeItem))
                {
                    errorCode = CraftStartCode.MissingOrInvalidRecipe;
                    error = "Craft recipe requirement is invalid.";
                    return false;
                }
                if (hasRecipe && definition.ConsumeRecipeItem)
                    AddCost(totals, definition.RequiredRecipeItemId, definition.RequiredRecipeItemCount, CraftPaidCostKind.Recipe);

                foreach (var pair in totals)
                    costs.Add(new CraftCostDescriptor(pair.Key, pair.Value.Quantity, pair.Value.Kind));
                return true;
            }
            catch (OverflowException)
            {
                errorCode = CraftStartCode.InvalidCraftDescriptor;
                error = "Aggregated craft costs overflow Int32.";
                return false;
            }
        }

        private static void AddCost(IDictionary<string, CostAccumulator> totals, string itemId, int quantity, string kind)
        {
            if (!totals.TryGetValue(itemId, out var value))
                value = new CostAccumulator { Kind = kind };
            value.Quantity = checked(value.Quantity + quantity);
            if (!string.Equals(value.Kind, kind, StringComparison.Ordinal))
                value.Kind = CraftPaidCostKind.MaterialAndRecipe;
            totals[itemId] = value;
        }

        private static bool ContainsCost(IEnumerable<CraftCostDescriptor> costs, string itemId)
        {
            foreach (var cost in costs)
                if (string.Equals(cost.ItemId, itemId, StringComparison.Ordinal)) return true;
            return false;
        }

        private static CraftExecutionSaveData CreateExecution(
            string executionId,
            string operationKey,
            string fingerprint,
            CraftStartDescriptor descriptor)
        {
            var requirements = new CraftRequiredBuildingSnapshotSaveData[descriptor.RequiredBuildings.Count];
            for (var index = 0; index < requirements.Length; index++)
            {
                var requirement = descriptor.RequiredBuildings[index];
                requirements[index] = new CraftRequiredBuildingSnapshotSaveData { buildingId = requirement.BuildingId, level = requirement.Level };
            }
            var costs = new CraftPaidCostSaveData[descriptor.PaidCosts.Count];
            for (var index = 0; index < costs.Length; index++)
            {
                var cost = descriptor.PaidCosts[index];
                costs[index] = new CraftPaidCostSaveData { itemId = cost.ItemId, quantity = cost.Quantity, kind = cost.Kind };
            }
            return new CraftExecutionSaveData
            {
                executionId = executionId,
                craftId = descriptor.CraftId,
                heroId = descriptor.HeroId,
                stationBuildingId = descriptor.StationBuildingId,
                stationBuildingLevel = descriptor.StationBuildingLevel,
                plannedCycles = descriptor.PlannedCycles,
                status = CraftExecutionStatus.Running,
                progressSeconds = 0f,
                durationSeconds = descriptor.DurationSeconds,
                outputItemId = descriptor.OutputItemId,
                outputCount = descriptor.OutputCount,
                skillId = descriptor.SkillId,
                skillExp = descriptor.SkillExp,
                fatigueCostPaid = descriptor.FatigueCost,
                requiredBuildings = requirements,
                paidCosts = costs,
                recipe = new CraftRecipeAuditSaveData
                {
                    requiredItemId = descriptor.Recipe.RequiredItemId,
                    requiredCount = descriptor.Recipe.RequiredCount,
                    consume = descriptor.Recipe.Consume,
                    consumedCount = descriptor.Recipe.Consume ? descriptor.Recipe.RequiredCount : 0
                },
                costsPaid = true,
                startOperationKey = operationKey,
                startFingerprint = fingerprint,
                startedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
        }

        private static string BuildFingerprint(CraftStartRequest request)
        {
            var fingerprint = $"craft:{Part(request?.CraftId)}|hero:{Part(request?.HeroId)}|station:{Part(request?.StationBuildingId)}|level:{request?.StationBuildingLevel ?? 0}";
            return request?.PlannedCycles == 1
                ? fingerprint
                : $"{fingerprint}|cycles:{request?.PlannedCycles ?? 0}";
        }

        private static string Part(string value)
        {
            value ??= string.Empty;
            return $"{value.Length}:{value}";
        }

        private PreflightResult Blocked(
            CraftStartRequest request,
            string code,
            string message,
            string fingerprint,
            CraftDefinitionDescriptor definition = null,
            IList<CraftCostDescriptor> costs = null,
            CraftRecipeDescriptor recipe = null,
            int maxCycles = 0)
        {
            return new PreflightResult
            {
                Fingerprint = fingerprint,
                Descriptor = CreateDescriptor(request, definition, costs, recipe, false, code, message, maxCycles)
            };
        }

        private static CraftStartDescriptor CreateDescriptor(
            CraftStartRequest request,
            CraftDefinitionDescriptor definition,
            IList<CraftCostDescriptor> costs,
            CraftRecipeDescriptor recipe,
            bool canStart,
            string code,
            string message,
            int maxCycles)
        {
            request ??= new CraftStartRequest();
            var requirements = new List<CraftBuildingRequirementDescriptor>();
            if (definition != null)
                foreach (var requirement in definition.RequiredBuildings)
                    requirements.Add(new CraftBuildingRequirementDescriptor(requirement.BuildingId, requirement.Level));
            return new CraftStartDescriptor(
                definition?.CraftId ?? request.CraftId,
                request.HeroId,
                request.StationBuildingId,
                request.StationBuildingLevel,
                request.PlannedCycles,
                maxCycles,
                MultiplyOrZero(definition?.CraftDurationSec ?? 0, request.PlannedCycles),
                definition?.TargetItemId,
                MultiplyOrZero(definition?.OutputCount ?? 0, request.PlannedCycles),
                definition?.CraftSkillId,
                MultiplyOrZero(definition?.SkillExp ?? 0, request.PlannedCycles),
                MultiplyOrZero(definition?.FatigueCost ?? 0, request.PlannedCycles),
                requirements,
                costs ?? Array.Empty<CraftCostDescriptor>(),
                recipe ?? new CraftRecipeDescriptor(definition?.RequiredRecipeItemId, definition?.RequiredRecipeItemCount ?? 0, definition?.ConsumeRecipeItem ?? false),
                canStart,
                code,
                message);
        }

        private static int MultiplyOrZero(int value, int cycles)
        {
            try { return checked(value * cycles); }
            catch (OverflowException) { return 0; }
        }

        private static CraftStartDescriptor DescriptorFromExecution(CraftExecutionSaveData execution)
        {
            var requirements = new List<CraftBuildingRequirementDescriptor>();
            foreach (var requirement in execution.requiredBuildings ?? Array.Empty<CraftRequiredBuildingSnapshotSaveData>())
                if (requirement != null) requirements.Add(new CraftBuildingRequirementDescriptor(requirement.buildingId, requirement.level));
            var costs = new List<CraftCostDescriptor>();
            foreach (var cost in execution.paidCosts ?? Array.Empty<CraftPaidCostSaveData>())
                if (cost != null) costs.Add(new CraftCostDescriptor(cost.itemId, cost.quantity, cost.kind));
            var recipe = execution.recipe ?? new CraftRecipeAuditSaveData();
            return new CraftStartDescriptor(
                execution.craftId,
                execution.heroId,
                execution.stationBuildingId,
                execution.stationBuildingLevel,
                execution.plannedCycles,
                execution.plannedCycles,
                execution.durationSeconds,
                execution.outputItemId,
                execution.outputCount,
                execution.skillId,
                execution.skillExp,
                execution.fatigueCostPaid,
                requirements,
                costs,
                new CraftRecipeDescriptor(recipe.requiredItemId, recipe.requiredCount, recipe.consume),
                true,
                CraftStartCode.Replayed,
                string.Empty);
        }

        internal static PreparedRewardBatch PrepareCompletionRewards(CraftExecutionSaveData execution)
        {
            var definitions = new List<RewardDefinition>
            {
                new RewardDefinition
                {
                    sourceId = execution.craftId,
                    rewardType = RewardType.Item,
                    targetId = execution.outputItemId,
                    min = execution.outputCount,
                    max = execution.outputCount,
                    chance = 100f,
                    grantMoment = GrantMoment.OnComplete
                }
            };
            if (execution.skillExp > 0)
            {
                definitions.Add(new RewardDefinition
                {
                    sourceId = execution.craftId,
                    rewardType = RewardType.SkillExp,
                    targetId = execution.skillId,
                    min = execution.skillExp,
                    max = execution.skillExp,
                    chance = 100f,
                    grantMoment = GrantMoment.OnComplete
                });
            }
            return RewardBatchPipeline.Prepare(definitions, GrantMoment.OnComplete, execution.heroId, null, false);
        }

        internal static string FirstRewardIssue(PreparedRewardBatch prepared)
        {
            foreach (var issue in prepared?.issues ?? Array.Empty<ActivityRequirementIssue>())
                if (issue != null && issue.isError) return issue.message ?? "Craft reward validation failed.";
            return "Craft reward validation failed.";
        }

        internal static bool TryValidateAdvanceExecution(
            ICraftPlayerState state,
            CraftExecutionSaveData execution,
            out string error)
        {
            error = null;
            if (execution == null || string.IsNullOrWhiteSpace(execution.executionId) ||
                string.IsNullOrWhiteSpace(execution.craftId) || string.IsNullOrWhiteSpace(execution.heroId) ||
                string.IsNullOrWhiteSpace(execution.stationBuildingId) || execution.stationBuildingLevel < 0 ||
                execution.plannedCycles <= 0 ||
                execution.durationSeconds <= 0 || float.IsNaN(execution.progressSeconds) ||
                float.IsInfinity(execution.progressSeconds) || execution.progressSeconds < 0f ||
                string.IsNullOrWhiteSpace(execution.outputItemId) || execution.outputCount <= 0 ||
                string.IsNullOrWhiteSpace(execution.skillId) || execution.skillExp < 0 || !execution.costsPaid)
            {
                error = "CraftExecution immutable snapshot is invalid.";
                return false;
            }
            if (state == null || !state.HasHero(execution.heroId) || !state.HasHeroState(execution.heroId) ||
                !string.Equals(state.GetHeroOccupationOwnerId(execution.heroId), execution.executionId, StringComparison.Ordinal))
            {
                error = "CraftExecution does not own its acquired hero.";
                return false;
            }
            if (execution.status == CraftExecutionStatus.Running)
            {
                if (!string.IsNullOrWhiteSpace(execution.pendingResultId) || execution.completionRecorded ||
                    execution.progressSeconds >= execution.durationSeconds)
                {
                    error = "Running CraftExecution has inconsistent completion state.";
                    return false;
                }
            }
            else if (execution.status == CraftExecutionStatus.ResultPending)
            {
                if (string.IsNullOrWhiteSpace(execution.pendingResultId) || !execution.completionRecorded ||
                    execution.progressSeconds < execution.durationSeconds)
                {
                    error = "ResultPending CraftExecution has inconsistent completion state.";
                    return false;
                }
            }
            else
            {
                error = $"CraftExecution status '{execution.status}' cannot be advanced.";
                return false;
            }

            var operationKeys = new HashSet<string>(StringComparer.Ordinal);
            var operationSequences = new HashSet<long>();
            var previousSequence = 0L;
            if (execution.lastAdvanceSequence < 0 || execution.completionAdvanceSequence < 0 ||
                (execution.status == CraftExecutionStatus.Running && execution.completionAdvanceSequence != 0) ||
                (execution.status == CraftExecutionStatus.ResultPending &&
                 execution.completionAdvanceSequence > 0 &&
                 execution.completionAdvanceSequence != execution.lastAdvanceSequence))
            {
                error = "CraftExecution advance sequence state is invalid.";
                return false;
            }
            foreach (var receipt in execution.advanceReceipts ?? Array.Empty<CraftAdvanceReceiptSaveData>())
            {
                var completed = string.Equals(receipt?.code, CraftAdvanceCode.ResultPending, StringComparison.Ordinal);
                if (receipt == null || receipt.operationSequence <= 0 || receipt.operationSequence > execution.lastAdvanceSequence ||
                    receipt.operationSequence <= previousSequence || !operationSequences.Add(receipt.operationSequence) ||
                    string.IsNullOrWhiteSpace(receipt.operationKey) ||
                    string.IsNullOrWhiteSpace(receipt.fingerprint) || !operationKeys.Add(receipt.operationKey) ||
                    double.IsNaN(receipt.deltaSeconds) || double.IsInfinity(receipt.deltaSeconds) || receipt.deltaSeconds < 0d ||
                    float.IsNaN(receipt.progressSeconds) || float.IsInfinity(receipt.progressSeconds) || receipt.progressSeconds < 0f ||
                    receipt.progressSeconds > execution.durationSeconds ||
                    (!completed && !string.Equals(receipt.code, CraftAdvanceCode.Applied, StringComparison.Ordinal)) ||
                    (completed != !string.IsNullOrWhiteSpace(receipt.pendingResultId)) ||
                    (completed && (receipt.progressSeconds < execution.durationSeconds ||
                                   !string.Equals(receipt.pendingResultId, BuildCraftResultId(execution.executionId), StringComparison.Ordinal))) ||
                    (!completed && receipt.progressSeconds >= execution.durationSeconds))
                {
                    error = "CraftExecution advance receipts are invalid.";
                    return false;
                }
                if (completed != (receipt.operationSequence == execution.completionAdvanceSequence))
                {
                    error = "CraftExecution completion sequence does not match its receipt.";
                    return false;
                }
                previousSequence = receipt.operationSequence;
            }
            return true;
        }

        internal static bool HasValidPendingResult(ICraftPlayerState state, CraftExecutionSaveData execution)
        {
            return CraftPendingResultValidator.Validate(execution, state?.GetPendingResult(execution?.pendingResultId));
        }

        private static CraftAdvanceReceiptSaveData FindAdvanceReceipt(CraftExecutionSaveData execution, string operationKey)
        {
            foreach (var receipt in execution?.advanceReceipts ?? Array.Empty<CraftAdvanceReceiptSaveData>())
                if (receipt != null && string.Equals(receipt.operationKey, operationKey, StringComparison.Ordinal)) return receipt;
            return null;
        }

        private static CraftAdvanceReceiptSaveData FindAdvanceReceipt(CraftExecutionSaveData execution, long operationSequence)
        {
            foreach (var receipt in execution?.advanceReceipts ?? Array.Empty<CraftAdvanceReceiptSaveData>())
                if (receipt != null && receipt.operationSequence == operationSequence) return receipt;
            return null;
        }

        private static void AddAdvanceReceipt(
            CraftExecutionSaveData execution,
            long operationSequence,
            string operationKey,
            string fingerprint,
            double deltaSeconds,
            string code,
            string pendingResultId)
        {
            var receipts = new List<CraftAdvanceReceiptSaveData>(execution.advanceReceipts ?? Array.Empty<CraftAdvanceReceiptSaveData>())
            {
                new CraftAdvanceReceiptSaveData
                {
                    operationSequence = operationSequence,
                    operationKey = operationKey,
                    fingerprint = fingerprint,
                    deltaSeconds = deltaSeconds,
                    progressSeconds = execution.progressSeconds,
                    code = code,
                    pendingResultId = pendingResultId
                }
            };
            while (receipts.Count > PlayerState.OperationReceiptRetentionLimit)
                receipts.RemoveAt(0);
            execution.advanceReceipts = receipts.ToArray();
        }

        private static string BuildAdvanceFingerprint(string executionId, double deltaSeconds, long operationSequence) =>
            $"execution:{Part(executionId)}|delta:{deltaSeconds.ToString("R", CultureInfo.InvariantCulture)}|sequence:{operationSequence.ToString(CultureInfo.InvariantCulture)}";

        private static bool MatchesAdvanceFingerprint(
            CraftAdvanceReceiptSaveData receipt,
            string executionId,
            double deltaSeconds,
            string currentFingerprint)
        {
            if (string.Equals(receipt?.fingerprint, currentFingerprint, StringComparison.Ordinal))
                return true;
            var legacyFingerprint = $"execution:{Part(executionId)}|delta:{deltaSeconds.ToString("R", CultureInfo.InvariantCulture)}";
            return string.Equals(receipt?.fingerprint, legacyFingerprint, StringComparison.Ordinal);
        }

        internal static string BuildCraftResultId(string executionId) =>
            $"result:{PendingResultSourceType.Craft}:{executionId}";

        private static CraftAdvanceResult AdvanceSuccess(CraftExecutionSaveData execution, string code, bool replayed)
        {
            return new CraftAdvanceResult
            {
                Success = true,
                Replayed = replayed,
                Completed = execution?.status == CraftExecutionStatus.ResultPending,
                Code = code,
                ExecutionId = execution?.executionId,
                OperationSequence = execution?.lastAdvanceSequence ?? 0,
                ProgressSeconds = execution?.progressSeconds ?? 0f,
                PendingResultId = execution?.pendingResultId,
                Execution = CloneExecution(execution)
            };
        }

        private static CraftAdvanceResult AdvanceReplay(CraftExecutionSaveData current, CraftAdvanceReceiptSaveData receipt)
        {
            var execution = CloneExecution(current);
            var completed = string.Equals(receipt.code, CraftAdvanceCode.ResultPending, StringComparison.Ordinal);
            execution.progressSeconds = receipt.progressSeconds;
            execution.status = completed ? CraftExecutionStatus.ResultPending : CraftExecutionStatus.Running;
            execution.completionRecorded = completed;
            execution.pendingResultId = receipt.pendingResultId;
            execution.lastAdvanceSequence = receipt.operationSequence;
            execution.completionAdvanceSequence = completed ? receipt.operationSequence : 0;

            var receiptCount = 0;
            foreach (var saved in execution.advanceReceipts ?? Array.Empty<CraftAdvanceReceiptSaveData>())
            {
                receiptCount++;
                if (saved != null && string.Equals(saved.operationKey, receipt.operationKey, StringComparison.Ordinal))
                    break;
            }
            if (receiptCount < execution.advanceReceipts.Length)
            {
                var historicalReceipts = new CraftAdvanceReceiptSaveData[receiptCount];
                Array.Copy(execution.advanceReceipts, historicalReceipts, receiptCount);
                execution.advanceReceipts = historicalReceipts;
            }

            return new CraftAdvanceResult
            {
                Success = true,
                Replayed = true,
                Completed = completed,
                Code = receipt.code,
                ExecutionId = current.executionId,
                OperationSequence = receipt.operationSequence,
                ProgressSeconds = receipt.progressSeconds,
                PendingResultId = receipt.pendingResultId,
                Execution = execution
            };
        }

        private static CraftAdvanceResult AdvanceEvictedReplay(CraftExecutionSaveData execution, long operationSequence)
        {
            var completed = execution.status == CraftExecutionStatus.ResultPending &&
                            execution.completionAdvanceSequence == operationSequence;
            return new CraftAdvanceResult
            {
                Success = true,
                Replayed = true,
                Completed = completed,
                Code = completed ? CraftAdvanceCode.ResultPending : CraftAdvanceCode.Applied,
                ExecutionId = execution.executionId,
                OperationSequence = operationSequence,
                ProgressSeconds = execution.progressSeconds,
                PendingResultId = completed ? execution.pendingResultId : null,
                Execution = CloneExecution(execution)
            };
        }

        private static CraftAdvanceResult AdvanceFailure(
            string code,
            string message,
            string executionId,
            CraftExecutionSaveData execution = null)
        {
            return new CraftAdvanceResult
            {
                Success = false,
                Completed = execution?.status == CraftExecutionStatus.ResultPending,
                Code = code,
                Message = message ?? string.Empty,
                ExecutionId = executionId,
                ProgressSeconds = execution?.progressSeconds ?? 0f,
                PendingResultId = execution?.pendingResultId,
                Execution = CloneExecution(execution)
            };
        }

        private static CraftStartResult Failure(CraftStartDescriptor descriptor)
        {
            return Failure(descriptor?.BlockCode ?? CraftStartCode.InvalidCraftDescriptor, descriptor?.BlockMessage, descriptor);
        }

        private static CraftStartResult Failure(string code, string message, CraftStartDescriptor descriptor)
        {
            return new CraftStartResult
            {
                Success = false,
                Code = code,
                Message = message ?? string.Empty,
                Descriptor = descriptor
            };
        }

        private void PublishStarted(CraftExecutionSaveData execution)
        {
            if (_eventSink == null || execution == null)
                return;
            try
            {
                _eventSink(new CraftStartedEvent
                {
                    ExecutionId = execution.executionId,
                    CraftId = execution.craftId,
                    HeroId = execution.heroId,
                    StationBuildingId = execution.stationBuildingId,
                    StationBuildingLevel = execution.stationBuildingLevel
                });
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void PublishDeferred(CraftAdvanceCoreResult result)
        {
            if (result == null || !result.Success || !result.TryMarkDeferredEventsPublished())
                return;

            foreach (var deferredEvent in result.DeferredResolvedEvents)
                _transactionalPendingResults.PublishDeferred(deferredEvent);
            foreach (var runtimeEvent in result.DeferredEvents)
            {
                try
                {
                    _resultPendingEventSink?.Invoke(runtimeEvent);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private static string MapCoreFailureCode(CraftAdvanceStopReason stopReason)
        {
            switch (stopReason)
            {
                case CraftAdvanceStopReason.ExecutionNotFound:
                    return CraftAdvanceCode.ExecutionNotFound;
                case CraftAdvanceStopReason.InvalidExecution:
                case CraftAdvanceStopReason.ExecutionNotRunning:
                case CraftAdvanceStopReason.InvalidRequest:
                    return CraftAdvanceCode.InvalidExecution;
                case CraftAdvanceStopReason.RewardValidationFailed:
                    return CraftAdvanceCode.RewardValidationFailure;
                case CraftAdvanceStopReason.PendingResultFailed:
                    return CraftAdvanceCode.PendingResultFailure;
                default:
                    return CraftAdvanceCode.TransactionFailure;
            }
        }

        private static string FirstCoreIssue(CraftAdvanceCoreResult result)
        {
            foreach (var issue in result?.Issues ?? Array.Empty<CraftAdvanceIssue>())
                if (issue != null && !string.IsNullOrWhiteSpace(issue.Message)) return issue.Message;
            return "Craft advance core failed.";
        }

        private void PublishStorageCommit()
        {
            try
            {
                _state.PublishCraftStartCommit();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static bool IsActive(CraftExecutionStatus status) =>
            status == CraftExecutionStatus.Running || status == CraftExecutionStatus.ResultPending;

        private bool IsValidReplayExecution(CraftExecutionSaveData execution, CraftStartRequest request, string fingerprint)
        {
            return execution != null &&
                   !string.IsNullOrWhiteSpace(execution.executionId) &&
                   string.Equals(execution.craftId, request.CraftId, StringComparison.Ordinal) &&
                   string.Equals(execution.heroId, request.HeroId, StringComparison.Ordinal) &&
                   string.Equals(execution.stationBuildingId, request.StationBuildingId, StringComparison.Ordinal) &&
                   execution.stationBuildingLevel == request.StationBuildingLevel &&
                   execution.plannedCycles == request.PlannedCycles &&
                   string.Equals(execution.startOperationKey, request.OperationKey, StringComparison.Ordinal) &&
                   string.Equals(execution.startFingerprint, fingerprint, StringComparison.Ordinal) &&
                   IsActive(execution.status) &&
                   string.Equals(_state.GetHeroOccupationOwnerId(execution.heroId), execution.executionId, StringComparison.Ordinal);
        }

        internal static CraftExecutionSaveData CloneExecution(CraftExecutionSaveData source)
        {
            if (source == null)
                return null;
            var requirements = new CraftRequiredBuildingSnapshotSaveData[source.requiredBuildings?.Length ?? 0];
            for (var index = 0; index < requirements.Length; index++)
            {
                var value = source.requiredBuildings[index];
                requirements[index] = value == null ? null : new CraftRequiredBuildingSnapshotSaveData { buildingId = value.buildingId, level = value.level };
            }
            var costs = new CraftPaidCostSaveData[source.paidCosts?.Length ?? 0];
            for (var index = 0; index < costs.Length; index++)
            {
                var value = source.paidCosts[index];
                costs[index] = value == null ? null : new CraftPaidCostSaveData { itemId = value.itemId, quantity = value.quantity, kind = value.kind };
            }
            var recipe = source.recipe ?? new CraftRecipeAuditSaveData();
            var advanceReceipts = new CraftAdvanceReceiptSaveData[source.advanceReceipts?.Length ?? 0];
            for (var index = 0; index < advanceReceipts.Length; index++)
            {
                var value = source.advanceReceipts[index];
                advanceReceipts[index] = value == null ? null : new CraftAdvanceReceiptSaveData
                {
                    operationKey = value.operationKey,
                    operationSequence = value.operationSequence,
                    fingerprint = value.fingerprint,
                    deltaSeconds = value.deltaSeconds,
                    progressSeconds = value.progressSeconds,
                    code = value.code,
                    pendingResultId = value.pendingResultId
                };
            }
            return new CraftExecutionSaveData
            {
                executionId = source.executionId,
                craftId = source.craftId,
                heroId = source.heroId,
                stationBuildingId = source.stationBuildingId,
                stationBuildingLevel = source.stationBuildingLevel,
                plannedCycles = source.plannedCycles,
                status = source.status,
                progressSeconds = source.progressSeconds,
                durationSeconds = source.durationSeconds,
                outputItemId = source.outputItemId,
                outputCount = source.outputCount,
                skillId = source.skillId,
                skillExp = source.skillExp,
                fatigueCostPaid = source.fatigueCostPaid,
                requiredBuildings = requirements,
                paidCosts = costs,
                recipe = new CraftRecipeAuditSaveData
                {
                    requiredItemId = recipe.requiredItemId,
                    requiredCount = recipe.requiredCount,
                    consume = recipe.consume,
                    consumedCount = recipe.consumedCount
                },
                costsPaid = source.costsPaid,
                startOperationKey = source.startOperationKey,
                startFingerprint = source.startFingerprint,
                pendingResultId = source.pendingResultId,
                completionRecorded = source.completionRecorded,
                lastAdvanceSequence = source.lastAdvanceSequence,
                completionAdvanceSequence = source.completionAdvanceSequence,
                advanceReceipts = advanceReceipts,
                startedAtUnixSeconds = source.startedAtUnixSeconds
            };
        }

        private sealed class PreflightResult
        {
            public CraftStartDescriptor Descriptor;
            public string Fingerprint;
            public CraftExecutionSaveData ReplayedExecution;
        }

        private struct CostAccumulator
        {
            public int Quantity;
            public string Kind;
        }
    }

    internal sealed class CraftAdvanceCore
    {
        private readonly ICraftPlayerState _state;
        private readonly ITransactionalPendingResultService _pendingResults;

        internal CraftAdvanceCore(
            ICraftPlayerState state,
            ITransactionalPendingResultService pendingResults)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _pendingResults = pendingResults ?? throw new ArgumentNullException(nameof(pendingResults));
        }

        internal CraftAdvanceCoreResult Advance(CraftAdvanceRequest request)
        {
            var executionId = request?.ExecutionId;
            var availableSeconds = request?.AvailableSeconds ?? 0d;
            if (request == null || string.IsNullOrWhiteSpace(executionId) ||
                double.IsNaN(availableSeconds) || double.IsInfinity(availableSeconds) ||
                availableSeconds < 0d)
            {
                return Failure(
                    CraftAdvanceStopReason.InvalidRequest,
                    executionId,
                    0d,
                    "InvalidRequest",
                    "Craft advance requires executionId and finite non-negative availableSeconds.");
            }
            if (availableSeconds == 0d)
                availableSeconds = 0d;

            var execution = _state.GetCraftExecution(executionId);
            if (execution == null)
            {
                return Failure(
                    CraftAdvanceStopReason.ExecutionNotFound,
                    executionId,
                    availableSeconds,
                    "ExecutionNotFound",
                    $"CraftExecution '{executionId}' was not found.");
            }

            var progressBefore = (double)execution.progressSeconds;
            if (execution.status != CraftExecutionStatus.Running &&
                execution.status != CraftExecutionStatus.ResultPending)
            {
                return Failure(
                    CraftAdvanceStopReason.ExecutionNotRunning,
                    executionId,
                    availableSeconds,
                    progressBefore,
                    execution.status,
                    "ExecutionNotRunning",
                    $"CraftExecution '{executionId}' is not running.");
            }
            if (!CraftRuntimeService.TryValidateAdvanceExecution(_state, execution, out var validationError))
            {
                return Failure(
                    CraftAdvanceStopReason.InvalidExecution,
                    executionId,
                    availableSeconds,
                    progressBefore,
                    execution.status,
                    "InvalidExecution",
                    validationError);
            }

            if (execution.status == CraftExecutionStatus.ResultPending)
            {
                if (!CraftRuntimeService.HasValidPendingResult(_state, execution))
                {
                    return Failure(
                        CraftAdvanceStopReason.InvalidExecution,
                        executionId,
                        availableSeconds,
                        progressBefore,
                        execution.status,
                        "InvalidExecution",
                        "ResultPending CraftExecution has no valid linked Craft Result.");
                }

                return Success(
                    CraftAdvanceStopReason.AlreadyResultPending,
                    execution,
                    availableSeconds,
                    0d,
                    availableSeconds,
                    progressBefore,
                    Array.Empty<CraftResultPendingEvent>(),
                    Array.Empty<PendingResultDeferredResolvedEvent>());
            }

            var expectedResultId = CraftRuntimeService.BuildCraftResultId(executionId);
            var completionOperationId = BuildCompletionOperationId(executionId);
            if (_state.GetPendingResult(expectedResultId) != null ||
                _state.TryGetOperationReceipt(expectedResultId, completionOperationId, out _))
            {
                return Failure(
                    CraftAdvanceStopReason.InvalidExecution,
                    executionId,
                    availableSeconds,
                    progressBefore,
                    execution.status,
                    "InvalidExecution",
                    "Running CraftExecution already has Craft Result formation state.");
            }

            var remainingToCompletion = Math.Max(0d, execution.durationSeconds - progressBefore);
            if (availableSeconds < remainingToCompletion)
            {
                try
                {
                    var next = CraftRuntimeService.CloneExecution(execution);
                    next.progressSeconds = (float)(progressBefore + availableSeconds);
                    if (!_state.UpdateCraftExecution(next))
                    {
                        return Failure(
                            CraftAdvanceStopReason.RuntimeError,
                            executionId,
                            availableSeconds,
                            progressBefore,
                            execution.status,
                            "ExecutionUpdateFailed",
                            "Failed to update CraftExecution progress.");
                    }

                    return Success(
                        CraftAdvanceStopReason.AppliedPartial,
                        _state.GetCraftExecution(executionId) ?? next,
                        availableSeconds,
                        availableSeconds,
                        0d,
                        progressBefore,
                        Array.Empty<CraftResultPendingEvent>(),
                        Array.Empty<PendingResultDeferredResolvedEvent>());
                }
                catch (Exception exception)
                {
                    return Failure(
                        CraftAdvanceStopReason.RuntimeError,
                        executionId,
                        availableSeconds,
                        progressBefore,
                        execution.status,
                        "RuntimeError",
                        $"Craft partial advance failed: {exception.Message}");
                }
            }

            PreparedRewardBatch preparedRewards;
            try
            {
                preparedRewards = CraftRuntimeService.PrepareCompletionRewards(execution);
            }
            catch (Exception exception)
            {
                return Failure(
                    CraftAdvanceStopReason.RuntimeError,
                    executionId,
                    availableSeconds,
                    progressBefore,
                    execution.status,
                    "RuntimeError",
                    $"Craft completion preflight failed: {exception.Message}");
            }
            if (preparedRewards == null || !preparedRewards.success)
            {
                return Failure(
                    CraftAdvanceStopReason.RewardValidationFailed,
                    executionId,
                    availableSeconds,
                    progressBefore,
                    execution.status,
                    "RewardValidationFailed",
                    CraftRuntimeService.FirstRewardIssue(preparedRewards));
            }

            try
            {
                var next = CraftRuntimeService.CloneExecution(execution);
                next.progressSeconds = next.durationSeconds;
                if (!_state.UpdateCraftExecution(next))
                {
                    return Failure(
                        CraftAdvanceStopReason.RuntimeError,
                        executionId,
                        availableSeconds,
                        progressBefore,
                        execution.status,
                        "ExecutionUpdateFailed",
                        "Failed to update CraftExecution completion progress.");
                }

                var formation = _pendingResults.CreateOrAppendInTransaction(
                    completionOperationId,
                    new PendingResultDraft
                    {
                        SourceType = PendingResultSourceType.Craft,
                        SourceId = next.craftId,
                        SourceExecutionId = next.executionId,
                        OwnerHeroId = next.heroId,
                        OperationContext = "craft-completion",
                        Entries = PendingResultEntryFactory.FromActivityRewards(
                            preparedRewards.rewards,
                            PendingResultOrigin.CraftOutput)
                    },
                    true);
                if (formation == null || !formation.Success)
                {
                    return Failure(
                        CraftAdvanceStopReason.PendingResultFailed,
                        executionId,
                        availableSeconds,
                        progressBefore,
                        execution.status,
                        formation?.Code ?? "PendingResultFailed",
                        formation?.Message ?? "Craft Result creation failed.");
                }

                var stored = _state.GetCraftExecution(executionId);
                if (stored == null || stored.status != CraftExecutionStatus.ResultPending ||
                    !string.Equals(stored.pendingResultId, formation.Result?.resultId, StringComparison.Ordinal) ||
                    !CraftRuntimeService.HasValidPendingResult(_state, stored))
                {
                    return Failure(
                        CraftAdvanceStopReason.PendingResultFailed,
                        executionId,
                        availableSeconds,
                        progressBefore,
                        execution.status,
                        "PendingResultFailed",
                        "Craft Result was not linked to its CraftExecution.");
                }

                var deferredResolvedEvents = formation.DeferredResolvedEvent == null
                    ? Array.Empty<PendingResultDeferredResolvedEvent>()
                    : new[] { formation.DeferredResolvedEvent };
                return Success(
                    CraftAdvanceStopReason.CraftCompleted,
                    stored,
                    availableSeconds,
                    remainingToCompletion,
                    Math.Max(0d, availableSeconds - remainingToCompletion),
                    progressBefore,
                    new[]
                    {
                        new CraftResultPendingEvent
                        {
                            ExecutionId = stored.executionId,
                            CraftId = stored.craftId,
                            HeroId = stored.heroId,
                            PendingResultId = stored.pendingResultId
                        }
                    },
                    deferredResolvedEvents);
            }
            catch (Exception exception)
            {
                return Failure(
                    CraftAdvanceStopReason.RuntimeError,
                    executionId,
                    availableSeconds,
                    progressBefore,
                    execution.status,
                    "RuntimeError",
                    $"Craft completion failed: {exception.Message}");
            }
        }

        internal static string BuildCompletionOperationId(string executionId) =>
            $"craft:{executionId}:completion";

        private static CraftAdvanceCoreResult Success(
            CraftAdvanceStopReason stopReason,
            CraftExecutionSaveData execution,
            double availableSeconds,
            double consumedSeconds,
            double remainingSeconds,
            double progressBefore,
            IReadOnlyList<CraftResultPendingEvent> deferredEvents,
            IReadOnlyList<PendingResultDeferredResolvedEvent> deferredResolvedEvents)
        {
            return new CraftAdvanceCoreResult(
                true,
                stopReason,
                execution?.executionId,
                availableSeconds,
                consumedSeconds,
                remainingSeconds,
                progressBefore,
                execution == null ? progressBefore : execution.progressSeconds,
                execution?.status ?? CraftExecutionStatus.None,
                execution?.status == CraftExecutionStatus.ResultPending,
                execution?.pendingResultId,
                Array.AsReadOnly(Array.Empty<CraftAdvanceIssue>()),
                Array.AsReadOnly(new List<CraftResultPendingEvent>(deferredEvents ?? Array.Empty<CraftResultPendingEvent>()).ToArray()),
                Array.AsReadOnly(new List<PendingResultDeferredResolvedEvent>(deferredResolvedEvents ?? Array.Empty<PendingResultDeferredResolvedEvent>()).ToArray()));
        }

        private static CraftAdvanceCoreResult Failure(
            CraftAdvanceStopReason stopReason,
            string executionId,
            double availableSeconds,
            string issueCode,
            string message)
        {
            return Failure(
                stopReason,
                executionId,
                availableSeconds,
                0d,
                CraftExecutionStatus.None,
                issueCode,
                message);
        }

        private static CraftAdvanceCoreResult Failure(
            CraftAdvanceStopReason stopReason,
            string executionId,
            double availableSeconds,
            double progress,
            CraftExecutionStatus executionStatus,
            string issueCode,
            string message)
        {
            return new CraftAdvanceCoreResult(
                false,
                stopReason,
                executionId,
                availableSeconds,
                0d,
                availableSeconds,
                progress,
                progress,
                executionStatus,
                false,
                string.Empty,
                Array.AsReadOnly(new[] { new CraftAdvanceIssue(issueCode, message) }),
                Array.AsReadOnly(Array.Empty<CraftResultPendingEvent>()),
                Array.AsReadOnly(Array.Empty<PendingResultDeferredResolvedEvent>()));
        }
    }

    public sealed class CraftAdvanceProcessor
    {
        private readonly ITransactionalPendingResultService _pendingResults;
        private readonly Action<CraftResultPendingEvent> _eventSink;
        private readonly CraftAdvanceCore _core;

        public CraftAdvanceProcessor(
            ICraftPlayerState state,
            Action<CraftResultPendingEvent> eventSink = null)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            _pendingResults = state.TransactionalPendingResults ??
                              throw new ArgumentException(
                                  "PendingResult service must support outer transactions.",
                                  nameof(state));
            _eventSink = eventSink;
            _core = new CraftAdvanceCore(state, _pendingResults);
        }

        public CraftAdvanceCoreResult Advance(CraftAdvanceRequest request) => _core.Advance(request);

        public void PublishDeferredEvents(CraftAdvanceCoreResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (!result.Success || !result.TryMarkDeferredEventsPublished())
                return;

            foreach (var deferredEvent in result.DeferredResolvedEvents)
                _pendingResults.PublishDeferred(deferredEvent);
            if (_eventSink == null)
                return;
            foreach (var runtimeEvent in result.DeferredEvents)
            {
                try
                {
                    _eventSink(runtimeEvent);
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[CraftAdvance] Deferred event sink failed for '{runtimeEvent?.ExecutionId}': {exception.Message}");
                }
            }
        }
    }

    internal static class CraftPendingResultValidator
    {
        public static bool Validate(CraftExecutionSaveData execution, PendingResultSaveData result)
        {
            return Validate(execution, result, false);
        }

        public static bool ValidateForFinalization(CraftExecutionSaveData execution, PendingResultSaveData result)
        {
            return Validate(execution, result, true) && result.entries.Length == 0;
        }

        private static bool Validate(CraftExecutionSaveData execution, PendingResultSaveData result, bool allowEmptyEntries)
        {
            if (execution == null || result == null ||
                !string.Equals(result.resultId, $"result:{PendingResultSourceType.Craft}:{execution.executionId}", StringComparison.Ordinal) ||
                !string.Equals(result.sourceType, PendingResultSourceType.Craft, StringComparison.Ordinal) ||
                !string.Equals(result.sourceId, execution.craftId, StringComparison.Ordinal) ||
                !string.Equals(result.sourceExecutionId, execution.executionId, StringComparison.Ordinal) ||
                !string.Equals(result.ownerHeroId, execution.heroId, StringComparison.Ordinal) ||
                !string.Equals(result.state, PendingResultState.ResultPending, StringComparison.Ordinal) ||
                result.entries == null || (!allowEmptyEntries && result.entries.Length == 0))
                return false;

            var itemFound = false;
            var skillExpFound = false;
            foreach (var entry in result.entries)
            {
                if (entry == null || entry.quantity <= 0 ||
                    !string.Equals(entry.origin, PendingResultOrigin.CraftOutput, StringComparison.Ordinal))
                    return false;

                if (string.Equals(entry.rewardType, RewardType.Item, StringComparison.Ordinal))
                {
                    if (itemFound || !string.Equals(entry.targetId, execution.outputItemId, StringComparison.Ordinal) ||
                        entry.quantity > execution.outputCount)
                        return false;
                    itemFound = true;
                    continue;
                }

                if (string.Equals(entry.rewardType, RewardType.SkillExp, StringComparison.Ordinal))
                {
                    if (skillExpFound || !string.Equals(entry.targetId, execution.skillId, StringComparison.Ordinal) ||
                        entry.quantity > execution.skillExp)
                        return false;
                    skillExpFound = true;
                    continue;
                }

                return false;
            }
            return allowEmptyEntries || itemFound || skillExpFound;
        }
    }
}
