using System;
using System.Collections.Generic;
using GuildIdle.Configs;
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
        bool Save();
    }

    public sealed class CraftRuntimeService
    {
        private const string StartReceiptAggregateId = "craft-start";

        private readonly CraftsConfigRepository _configs;
        private readonly ICraftPlayerState _state;
        private readonly Action<CraftStartedEvent> _eventSink;

        public CraftRuntimeService(
            CraftsConfigRepository configs,
            ICraftPlayerState state,
            Action<CraftStartedEvent> eventSink = null)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _eventSink = eventSink;
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

            if (_state.GetHeroFatigue(request.HeroId) < definition.FatigueCost)
                return Blocked(request, CraftStartCode.InsufficientFatigue, "Hero has insufficient fatigue.", fingerprint, definition);

            if (!TryBuildCosts(definition, out var costs, out var recipe, out var costErrorCode, out var costError))
                return Blocked(request, costErrorCode, costError, fingerprint, definition);

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
                Descriptor = CreateDescriptor(request, definition, costs, recipe, true, CraftStartCode.Available, string.Empty)
            };
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
            return $"craft:{Part(request?.CraftId)}|hero:{Part(request?.HeroId)}|station:{Part(request?.StationBuildingId)}|level:{request?.StationBuildingLevel ?? 0}";
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
            CraftRecipeDescriptor recipe = null)
        {
            return new PreflightResult
            {
                Fingerprint = fingerprint,
                Descriptor = CreateDescriptor(request, definition, costs, recipe, false, code, message)
            };
        }

        private static CraftStartDescriptor CreateDescriptor(
            CraftStartRequest request,
            CraftDefinitionDescriptor definition,
            IList<CraftCostDescriptor> costs,
            CraftRecipeDescriptor recipe,
            bool canStart,
            string code,
            string message)
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
                definition?.CraftDurationSec ?? 0,
                definition?.TargetItemId,
                definition?.OutputCount ?? 0,
                definition?.CraftSkillId,
                definition?.SkillExp ?? 0,
                definition?.FatigueCost ?? 0,
                requirements,
                costs ?? Array.Empty<CraftCostDescriptor>(),
                recipe ?? new CraftRecipeDescriptor(definition?.RequiredRecipeItemId, definition?.RequiredRecipeItemCount ?? 0, definition?.ConsumeRecipeItem ?? false),
                canStart,
                code,
                message);
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
                   string.Equals(execution.startOperationKey, request.OperationKey, StringComparison.Ordinal) &&
                   string.Equals(execution.startFingerprint, fingerprint, StringComparison.Ordinal) &&
                   IsActive(execution.status) &&
                   string.Equals(_state.GetHeroOccupationOwnerId(execution.heroId), execution.executionId, StringComparison.Ordinal);
        }

        private static CraftExecutionSaveData CloneExecution(CraftExecutionSaveData source)
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
            return new CraftExecutionSaveData
            {
                executionId = source.executionId,
                craftId = source.craftId,
                heroId = source.heroId,
                stationBuildingId = source.stationBuildingId,
                stationBuildingLevel = source.stationBuildingLevel,
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
}
