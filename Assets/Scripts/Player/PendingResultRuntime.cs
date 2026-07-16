using System;
using System.Collections.Generic;
using GuildIdle.Activities;
using GuildIdle.Core;

namespace GuildIdle.Player
{
    public static class PendingResultOrigin
    {
        public const string ActivityReward = "activity_reward";
        public const string CombatLoot = "combat_loot";
        public const string ActivityLootInCombat = "activity_loot_in_combat";
        public const string BroughtConsumable = "brought_consumable";
        public const string CraftOutput = "craft_output";
        public const string QuestReward = "quest_reward";
    }

    public sealed class PendingResultEntryDraft
    {
        public int SortOrder { get; set; }
        public string RewardType { get; set; }
        public string TargetId { get; set; }
        public long Quantity { get; set; }
        public string Origin { get; set; }
        public int Quality { get; set; }
    }

    public sealed class PendingResultDraft
    {
        public string SourceType { get; set; }
        public string SourceId { get; set; }
        public string SourceExecutionId { get; set; }
        public string OwnerHeroId { get; set; }
        public string OperationContext { get; set; }
        public PendingResultEntryDraft[] Entries { get; set; } = Array.Empty<PendingResultEntryDraft>();
    }

    public sealed class PendingResultFormationResult
    {
        public bool Success { get; set; }
        public bool Replayed { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public PendingResultSaveData Result { get; set; }
        public bool ResolvedImmediately { get; set; }
    }

    public sealed class PendingResultMutationResult
    {
        public bool Success { get; set; }
        public bool Replayed { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public long ResultRevision { get; set; }
        public long StorageRevision { get; set; }
        public bool Resolved { get; set; }
        public PendingResultSaveData Result { get; set; }
    }

    public sealed class PendingResultResolvedEvent
    {
        public string ResultId { get; set; }
        public string SourceType { get; set; }
        public string SourceId { get; set; }
        public string SourceExecutionId { get; set; }
        public string OwnerHeroId { get; set; }
    }

    public interface IPendingResultService
    {
        event Action<PendingResultResolvedEvent> Resolved;
        PendingResultSaveData Get(string resultId);
        PendingResultSaveData[] GetAll();
        PendingResultFormationResult CreateOrAppend(string operationId, PendingResultDraft draft, bool makeClaimable, long expectedResultRevision = 0);
        PendingResultFormationResult CreateCombatResult(string operationId, PendingResultDraft calculatedResult, string broughtStackId, StorageActionContext combatContext, long expectedStorageRevision);
        PendingResultMutationResult ClaimAll(string operationId, string resultId, long expectedResultRevision, long expectedStorageRevision);
        PendingResultMutationResult ClaimAvailable(string operationId, string resultId, long expectedResultRevision, long expectedStorageRevision);
        PendingResultMutationResult ClaimQuantity(string operationId, string resultId, string entryId, long quantity, long expectedResultRevision, long expectedStorageRevision);
        PendingResultMutationResult DiscardAll(string operationId, string resultId, long expectedResultRevision);
        PendingResultMutationResult DiscardQuantity(string operationId, string resultId, string entryId, long quantity, long expectedResultRevision);
        PendingResultSaveData[] GetSaveData();
        void Load(PendingResultSaveData[] results);
    }

    public interface IPendingResultSourceLifecycle
    {
        bool TryBind(PendingResultSaveData result, bool makeClaimable);
        bool CanClaim(PendingResultSaveData result);
        bool Resolve(PendingResultSaveData result);
    }

    public sealed class PendingResultService : IPendingResultService
    {
        private readonly PlayerState _state;
        private readonly StorageService _storage;
        private readonly IPendingResultSourceLifecycle _sourceLifecycle;
        private readonly Dictionary<string, PendingResultSaveData> _results = new Dictionary<string, PendingResultSaveData>(StringComparer.Ordinal);

        internal PendingResultService(PlayerState state, StorageService storage)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _sourceLifecycle = state;
        }

        public event Action<PendingResultResolvedEvent> Resolved;

        public PendingResultSaveData Get(string resultId) => !string.IsNullOrWhiteSpace(resultId) && _results.TryGetValue(resultId, out var value) ? CloneResult(value) : null;

        public PendingResultSaveData[] GetAll() => GetSaveData();

        public PendingResultSaveData[] GetSaveData()
        {
            var keys = new List<string>(_results.Keys);
            keys.Sort(StringComparer.Ordinal);
            var result = new PendingResultSaveData[keys.Count];
            for (var index = 0; index < keys.Count; index++)
                result[index] = CloneResult(_results[keys[index]]);
            return result;
        }

        public void Load(PendingResultSaveData[] results)
        {
            _results.Clear();
            var sourceKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in results ?? Array.Empty<PendingResultSaveData>())
            {
                if (!TryNormalize(source, out var normalized) || _results.ContainsKey(normalized.resultId))
                {
                    _state.MarkNormalized();
                    continue;
                }
                var sourceKey = $"{normalized.sourceType}\n{normalized.sourceExecutionId}";
                if (!sourceKeys.Add(sourceKey))
                {
                    _state.MarkNormalized();
                    continue;
                }
                _results.Add(normalized.resultId, normalized);
                var makeClaimable = string.Equals(normalized.sourceType, PendingResultSourceType.Quest, StringComparison.Ordinal);
                if (string.Equals(normalized.sourceType, PendingResultSourceType.Activity, StringComparison.Ordinal))
                    makeClaimable = _state.GetActivityExecution(normalized.sourceExecutionId)?.status == ActivityRuntimeStatus.ResultPending;
                if (!_sourceLifecycle.TryBind(normalized, makeClaimable))
                {
                    _results.Remove(normalized.resultId);
                    sourceKeys.Remove(sourceKey);
                    _state.MarkNormalized();
                }
            }
        }

        public PendingResultFormationResult CreateOrAppend(string operationId, PendingResultDraft draft, bool makeClaimable, long expectedResultRevision = 0)
        {
            if (draft == null || string.IsNullOrWhiteSpace(draft.SourceType) || string.IsNullOrWhiteSpace(draft.SourceExecutionId) || string.IsNullOrWhiteSpace(operationId))
                return FormationFailure("InvalidFormation", "Source type, execution id and operation id are required.");

            var resultId = BuildResultId(draft.SourceType, draft.SourceExecutionId);
            var aggregateId = resultId;
            var fingerprint = FormationFingerprint(draft, makeClaimable, expectedResultRevision);
            if (_state.TryGetOperationReceipt(aggregateId, operationId, out var receipt))
            {
                if (!string.Equals(receipt.fingerprint, fingerprint, StringComparison.Ordinal))
                    return FormationFailure("OperationConflict", "operationId was already used with another payload.");
                return new PendingResultFormationResult { Success = receipt.success, Replayed = true, Code = receipt.code, Result = Get(resultId), ResolvedImmediately = receipt.resolved };
            }

            var before = _state.ToSaveData();
            if (!_results.TryGetValue(resultId, out var result))
            {
                if (expectedResultRevision != 0)
                    return FormationFailure("StaleResultRevision", "A new result must start at revision 0.");
                result = new PendingResultSaveData
                {
                    resultId = resultId,
                    sourceType = draft.SourceType,
                    sourceId = draft.SourceId,
                    sourceExecutionId = draft.SourceExecutionId,
                    ownerHeroId = draft.OwnerHeroId,
                    state = PendingResultState.ResultPending,
                    revision = 0,
                    entries = Array.Empty<PendingResultEntrySaveData>()
                };
                _results.Add(resultId, result);
            }
            else if (!SameSource(result, draft))
            {
                return FormationFailure("SourceConflict", "A result id is already bound to another source.");
            }
            else if (result.revision != expectedResultRevision)
            {
                return FormationFailure("StaleResultRevision", $"Expected result revision {expectedResultRevision}, current revision is {result.revision}.");
            }
            else if (makeClaimable && _sourceLifecycle.CanClaim(result))
            {
                return new PendingResultFormationResult { Success = true, Code = "Existing", Result = CloneResult(result) };
            }

            var entries = new List<PendingResultEntrySaveData>(result.entries ?? Array.Empty<PendingResultEntrySaveData>());
            foreach (var entry in draft.Entries ?? Array.Empty<PendingResultEntryDraft>())
            {
                if (entry == null || entry.Quantity <= 0 || string.IsNullOrWhiteSpace(entry.RewardType) || string.IsNullOrWhiteSpace(entry.TargetId) || string.IsNullOrWhiteSpace(entry.Origin))
                {
                    _state.RestoreTransactional(before);
                    return FormationFailure("InvalidEntry", "PendingResult entries require reward type, target, origin and positive quantity.");
                }
                if (!TryValidateDraftEntry(entry, out var validationCode, out var validationError))
                {
                    _state.RestoreTransactional(before);
                    return FormationFailure(validationCode, validationError);
                }
                entries.Add(new PendingResultEntrySaveData
                {
                    entryId = Guid.NewGuid().ToString("N"),
                    sortOrder = entry.SortOrder,
                    rewardType = entry.RewardType,
                    targetId = entry.TargetId,
                    quantity = entry.Quantity,
                    origin = entry.Origin,
                    quality = entry.Quality
                });
            }
            result.entries = entries.ToArray();
            result.revision++;

            if (!_sourceLifecycle.TryBind(result, makeClaimable))
            {
                _state.RestoreTransactional(before);
                return FormationFailure("SourceTransitionFailed", "Could not bind PendingResult to its source.");
            }

            var resolvedImmediately = false;
            if (makeClaimable && result.entries.Length == 0)
            {
                if (!_sourceLifecycle.Resolve(result))
                {
                    _state.RestoreTransactional(before);
                    return FormationFailure("SourceResolutionFailed", "Could not resolve empty PendingResult source.");
                }
                _results.Remove(resultId);
                resolvedImmediately = true;
            }

            _state.RecordOperationReceipt(new OperationReceiptSaveData
            {
                aggregateId = aggregateId,
                operationId = operationId,
                fingerprint = fingerprint,
                success = true,
                code = resolvedImmediately ? "Resolved" : "Formed",
                storageRevision = _state.StorageRevision,
                resultRevision = result.revision,
                resolved = resolvedImmediately
            });
            if (!_state.Save())
            {
                _state.RestoreTransactional(before);
                return FormationFailure("SaveFailed", "PendingResult formation could not be saved and was rolled back.");
            }
            return new PendingResultFormationResult { Success = true, Code = resolvedImmediately ? "Resolved" : "Formed", Result = resolvedImmediately ? null : CloneResult(result), ResolvedImmediately = resolvedImmediately };
        }

        public PendingResultFormationResult CreateCombatResult(string operationId, PendingResultDraft calculatedResult, string broughtStackId, StorageActionContext combatContext, long expectedStorageRevision)
        {
            if (calculatedResult == null || string.IsNullOrWhiteSpace(calculatedResult.SourceExecutionId) || string.IsNullOrWhiteSpace(operationId))
                return FormationFailure("InvalidFormation", "Combat result is required.");
            var combatDraft = new PendingResultDraft
            {
                SourceType = PendingResultSourceType.Combat,
                SourceId = calculatedResult.SourceId,
                SourceExecutionId = calculatedResult.SourceExecutionId,
                OwnerHeroId = calculatedResult.OwnerHeroId,
                Entries = calculatedResult.Entries ?? Array.Empty<PendingResultEntryDraft>(),
                OperationContext = $"combat|{broughtStackId}|{ContextFingerprint(combatContext)}|{expectedStorageRevision}|{DraftEntriesFingerprint(calculatedResult.Entries)}"
            };
            var resultId = BuildResultId(combatDraft.SourceType, combatDraft.SourceExecutionId);
            var replayFingerprint = FormationFingerprint(combatDraft, true, 0);
            if (_state.TryGetOperationReceipt(resultId, operationId, out var replayReceipt))
            {
                if (!string.Equals(replayReceipt.fingerprint, replayFingerprint, StringComparison.Ordinal))
                    return FormationFailure("OperationConflict", "operationId was already used with another combat result payload.");
                return new PendingResultFormationResult { Success = replayReceipt.success, Replayed = true, Code = replayReceipt.code, Result = Get(resultId), ResolvedImmediately = replayReceipt.resolved };
            }
            if (expectedStorageRevision != _state.StorageRevision)
                return FormationFailure("StaleStorageRevision", $"Expected storage revision {expectedStorageRevision}, current revision is {_state.StorageRevision}.");
            var before = _state.ToSaveData();
            var entries = new List<PendingResultEntryDraft>(combatDraft.Entries);
            var storageChanged = false;
            if (!string.IsNullOrWhiteSpace(broughtStackId))
            {
                if (combatContext == null || !_state.MutableItemStacks.TryGetValue(broughtStackId, out var brought) ||
                    !combatContext.Matches(brought.contextType, brought.contextId) ||
                    !_state.ConfigProvider.TryGetItemState(brought.stateId, out var broughtState) ||
                    !string.Equals(broughtState.availabilityMode, ItemAvailabilityMode.InAction, StringComparison.Ordinal) ||
                    !_state.ConfigProvider.TryGetItem(brought.itemId, out var broughtItem) ||
                    !string.Equals(broughtItem.Kind, "consumable", StringComparison.Ordinal))
                    return FormationFailure("InvalidBroughtStack", "Brought consumable stack does not belong to this combat context.");
                entries.Add(new PendingResultEntryDraft { SortOrder = int.MaxValue, RewardType = "Consumable", TargetId = brought.itemId, Quantity = brought.quantity, Origin = PendingResultOrigin.BroughtConsumable });
                _state.MutableItemStacks.Remove(broughtStackId);
                _storage.CommitExternalMutation();
                storageChanged = true;
            }
            combatDraft.Entries = entries.ToArray();
            var formed = CreateOrAppend(operationId, combatDraft, true, 0);
            if (!formed.Success)
                _state.RestoreTransactional(before);
            else if (storageChanged)
                _storage.NotifyExternalMutation();
            return formed;
        }

        public PendingResultMutationResult ClaimAll(string operationId, string resultId, long expectedResultRevision, long expectedStorageRevision) =>
            Mutate(operationId, resultId, $"claim_all|{expectedResultRevision}|{expectedStorageRevision}", expectedResultRevision, expectedStorageRevision, true, HasItemEntries, (result, changedEntries) => ClaimEntries(result, OrderedEntries(result), false, changedEntries));

        public PendingResultMutationResult ClaimAvailable(string operationId, string resultId, long expectedResultRevision, long expectedStorageRevision) =>
            Mutate(operationId, resultId, $"claim_available|{expectedResultRevision}|{expectedStorageRevision}", expectedResultRevision, expectedStorageRevision, true, HasItemEntries, (result, changedEntries) => ClaimEntries(result, OrderedEntries(result), true, changedEntries));

        public PendingResultMutationResult ClaimQuantity(string operationId, string resultId, string entryId, long quantity, long expectedResultRevision, long expectedStorageRevision) =>
            Mutate(operationId, resultId, $"claim_quantity|{entryId}|{quantity}|{expectedResultRevision}|{expectedStorageRevision}", expectedResultRevision, expectedStorageRevision, true,
            result => IsItemReward(FindEntry(result, entryId)?.rewardType), (result, changedEntries) =>
            {
                var entry = FindEntry(result, entryId);
                if (entry == null || quantity <= 0 || quantity > entry.quantity)
                    return "Invalid entry quantity.";
                return ClaimOne(result, entry, quantity, false, changedEntries);
            });

        public PendingResultMutationResult DiscardAll(string operationId, string resultId, long expectedResultRevision) =>
            Mutate(operationId, resultId, $"discard_all|{expectedResultRevision}", expectedResultRevision, null, false, null, (result, changedEntries) =>
            {
                foreach (var entry in result.entries ?? Array.Empty<PendingResultEntrySaveData>())
                    if (entry != null && entry.quantity > 0) { entry.quantity = 0; changedEntries.Add(entry.entryId); }
                return null;
            });

        public PendingResultMutationResult DiscardQuantity(string operationId, string resultId, string entryId, long quantity, long expectedResultRevision) =>
            Mutate(operationId, resultId, $"discard_quantity|{entryId}|{quantity}|{expectedResultRevision}", expectedResultRevision, null, false, null, (result, changedEntries) =>
            {
                var entry = FindEntry(result, entryId);
                if (entry == null || quantity <= 0 || quantity > entry.quantity)
                    return "Invalid entry quantity.";
                entry.quantity -= quantity;
                changedEntries.Add(entry.entryId);
                return null;
            });

        private PendingResultMutationResult Mutate(
            string operationId,
            string resultId,
            string fingerprint,
            long expectedResultRevision,
            long? expectedStorageRevision,
            bool isClaim,
            Func<PendingResultSaveData, bool> requiresStorageRevision,
            Func<PendingResultSaveData, HashSet<string>, string> mutation)
        {
            if (string.IsNullOrWhiteSpace(operationId) || string.IsNullOrWhiteSpace(resultId))
                return MutationFailure("OperationIdRequired", "operationId and resultId are required.");
            if (_state.TryGetOperationReceipt(resultId, operationId, out var receipt))
            {
                if (!string.Equals(receipt.fingerprint, fingerprint, StringComparison.Ordinal))
                    return MutationFailure("OperationConflict", "operationId was already used with another payload.");
                return new PendingResultMutationResult { Success = receipt.success, Replayed = true, Code = receipt.code, ResultRevision = receipt.resultRevision, StorageRevision = receipt.storageRevision, Resolved = receipt.resolved, Result = Get(resultId) };
            }
            if (!_results.TryGetValue(resultId, out var result))
                return MutationFailure("ResultNotFound", "PendingResult does not exist or has already been resolved.");
            if (result.revision != expectedResultRevision)
                return MutationFailure("StaleResultRevision", $"Expected result revision {expectedResultRevision}, current revision is {result.revision}.");
            if (expectedStorageRevision.HasValue && requiresStorageRevision != null && requiresStorageRevision(result) && expectedStorageRevision.Value != _state.StorageRevision)
                return MutationFailure("StaleStorageRevision", $"Expected storage revision {expectedStorageRevision.Value}, current revision is {_state.StorageRevision}.");
            if (!_sourceLifecycle.CanClaim(result))
                return MutationFailure("SourceNotClaimable", "Source execution has not entered its pending-result state.");

            var before = _state.ToSaveData();
            var changedEntries = new HashSet<string>(StringComparer.Ordinal);
            var error = mutation(result, changedEntries);
            if (error != null)
            {
                _state.RestoreTransactional(before);
                return MutationFailure("Rejected", error);
            }
            if (changedEntries.Count == 0)
                return MutationFailure("NothingChanged", "No entries could be processed.");

            NormalizeEntries(result);
            var storageChanged = isClaim && StorageChanged(before);
            if (storageChanged)
                _storage.CommitExternalMutation();

            result.revision++;
            var resolved = result.entries.Length == 0;
            PendingResultResolvedEvent resolvedEvent = null;
            if (resolved)
            {
                if (!_sourceLifecycle.Resolve(result))
                {
                    _state.RestoreTransactional(before);
                    return MutationFailure("SourceResolutionFailed", "Source completion failed; transaction was rolled back.");
                }
                _results.Remove(result.resultId);
                resolvedEvent = ToResolvedEvent(result);
            }

            _state.RecordOperationReceipt(new OperationReceiptSaveData
            {
                aggregateId = resultId,
                operationId = operationId,
                fingerprint = fingerprint,
                success = true,
                code = resolved ? "Resolved" : "Applied",
                storageRevision = _state.StorageRevision,
                resultRevision = result.revision,
                resolved = resolved
            });
            if (!_state.Save())
            {
                _state.RestoreTransactional(before);
                return MutationFailure("SaveFailed", "Transaction could not be saved and was rolled back.");
            }
            if (storageChanged)
                _storage.NotifyExternalMutation();
            if (resolvedEvent != null)
                Resolved?.Invoke(resolvedEvent);
            return new PendingResultMutationResult { Success = true, Code = resolved ? "Resolved" : "Applied", ResultRevision = result.revision, StorageRevision = _state.StorageRevision, Resolved = resolved, Result = resolved ? null : CloneResult(result) };
        }

        private string ClaimEntries(PendingResultSaveData result, List<PendingResultEntrySaveData> entries, bool allowPartial, HashSet<string> changedEntries)
        {
            foreach (var entry in entries)
            {
                if (entry == null || entry.quantity <= 0)
                    continue;
                var error = ClaimOne(result, entry, entry.quantity, allowPartial, changedEntries);
                if (error != null && !allowPartial)
                    return error;
            }
            return null;
        }

        private string ClaimOne(PendingResultSaveData result, PendingResultEntrySaveData entry, long quantity, bool allowPartial, HashSet<string> changedEntries)
        {
            if (IsItemReward(entry.rewardType))
            {
                if (quantity > int.MaxValue)
                    return "Item entry quantity exceeds the supported range.";
                if (!_state.ConfigProvider.TryGetItem(entry.targetId, out _))
                    return $"Unknown item reward target '{entry.targetId}'.";
                if (!_storage.TryAddResultItem(entry.targetId, (int)quantity, entry.quality, allowPartial, out var accepted, out _, out _, out var error))
                    return error;
                if (accepted <= 0)
                    return allowPartial ? null : "Storage capacity is insufficient.";
                entry.quantity -= accepted;
                changedEntries.Add(entry.entryId);
                return null;
            }

            if (!TryBuildRewardMutation(result, entry, quantity, out var mutation, out var mutationError))
                return mutationError;
            if (!_state.TryApplyRewardBatch(new[] { mutation }, out _, out var applyError))
                return applyError ?? "Reward mutation failed.";
            entry.quantity -= quantity;
            changedEntries.Add(entry.entryId);
            return null;
        }

        private bool TryBuildRewardMutation(PendingResultSaveData result, PendingResultEntrySaveData entry, long quantity, out RewardMutation mutation, out string error)
        {
            mutation = null;
            error = null;
            if (!ActivityTypeParser.TryParseRewardType(entry.rewardType, out var type))
            {
                error = $"Unsupported reward type '{entry.rewardType}'.";
                return false;
            }
            switch (type)
            {
                case RewardTypeEnum.Gold:
                case RewardTypeEnum.Currency:
                    mutation = new RewardMutation(RewardMutationKind.Currency, entry.targetId, quantity);
                    return true;
                case RewardTypeEnum.SkillExp:
                    mutation = new RewardMutation(RewardMutationKind.HeroSkillExp, entry.targetId, quantity, result.ownerHeroId);
                    return true;
                case RewardTypeEnum.Hero:
                    mutation = new RewardMutation(RewardMutationKind.Hero, entry.targetId, quantity);
                    return true;
                case RewardTypeEnum.UnlockBuilding:
                    mutation = new RewardMutation(RewardMutationKind.UnlockBuilding, entry.targetId, quantity);
                    return true;
                case RewardTypeEnum.UnlockLocation:
                    mutation = new RewardMutation(RewardMutationKind.UnlockLocation, entry.targetId, quantity);
                    return true;
                default:
                    error = $"Reward type '{entry.rewardType}' is not claimable by the current PlayerState pipeline.";
                    return false;
            }
        }

        private List<PendingResultEntrySaveData> OrderedEntries(PendingResultSaveData result)
        {
            var nonItems = new List<PendingResultEntrySaveData>();
            var stackItems = new List<PendingResultEntrySaveData>();
            var equipment = new List<PendingResultEntrySaveData>();
            foreach (var entry in result.entries ?? Array.Empty<PendingResultEntrySaveData>())
            {
                if (entry == null || entry.quantity <= 0)
                    continue;
                if (!IsItemReward(entry.rewardType))
                    nonItems.Add(entry);
                else if (_state.ConfigProvider.TryGetItem(entry.targetId, out var item) &&
                         _state.ConfigProvider.TryGetStorageRuleForItemKind(item.Kind, out var rule) &&
                         string.Equals(rule.mode, "single", StringComparison.Ordinal))
                    equipment.Add(entry);
                else
                    stackItems.Add(entry);
            }
            Comparison<PendingResultEntrySaveData> byOrder = (left, right) =>
            {
                var value = left.sortOrder.CompareTo(right.sortOrder);
                return value != 0 ? value : string.CompareOrdinal(left.entryId, right.entryId);
            };
            nonItems.Sort(byOrder);
            stackItems.Sort(byOrder);
            equipment.Sort((left, right) => string.CompareOrdinal(left.entryId, right.entryId));
            var ordered = new List<PendingResultEntrySaveData>(nonItems.Count + stackItems.Count + equipment.Count);
            ordered.AddRange(nonItems);
            ordered.AddRange(stackItems);
            ordered.AddRange(equipment);
            return ordered;
        }

        private static PendingResultEntrySaveData FindEntry(PendingResultSaveData result, string entryId)
        {
            foreach (var entry in result.entries ?? Array.Empty<PendingResultEntrySaveData>())
                if (entry != null && string.Equals(entry.entryId, entryId, StringComparison.Ordinal)) return entry;
            return null;
        }

        private static bool IsItemReward(string rewardType)
        {
            if (!ActivityTypeParser.TryParseRewardType(rewardType, out var type))
                return false;
            return type == RewardTypeEnum.Resource || type == RewardTypeEnum.Equipment ||
                   type == RewardTypeEnum.Consumable || type == RewardTypeEnum.Recipe ||
                   type == RewardTypeEnum.Item;
        }

        private static bool HasItemEntries(PendingResultSaveData result)
        {
            foreach (var entry in result?.entries ?? Array.Empty<PendingResultEntrySaveData>())
                if (entry != null && entry.quantity > 0 && IsItemReward(entry.rewardType)) return true;
            return false;
        }

        private bool TryValidateDraftEntry(PendingResultEntryDraft entry, out string code, out string error)
        {
            code = null;
            error = null;
            if (!ActivityTypeParser.TryParseRewardType(entry.RewardType, out var type))
            {
                code = "UnsupportedRewardType";
                error = $"Reward type '{entry.RewardType}' is not supported by PendingResult.";
                return false;
            }
            if (entry.Quality < 0)
            {
                code = "InvalidQuality";
                error = "Result entry quality must be non-negative.";
                return false;
            }
            if (IsItemReward(entry.RewardType))
            {
                if (!_state.ConfigProvider.TryGetItem(entry.TargetId, out var item) || item == null ||
                    !_state.ConfigProvider.TryGetStorageRuleForItemKind(item.Kind, out var rule))
                {
                    code = "UnknownItemReward";
                    error = $"Unknown item reward target '{entry.TargetId}'.";
                    return false;
                }
                var single = string.Equals(rule.mode, "single", StringComparison.Ordinal);
                if (single && entry.Quantity != 1)
                {
                    code = "InvalidEquipmentQuantity";
                    error = "Single equipment result entries must have quantity 1.";
                    return false;
                }
                if (!single && entry.Quality != 0)
                {
                    code = "InvalidQuality";
                    error = "Only single-item result entries can specify quality.";
                    return false;
                }
                return true;
            }

            if (entry.Quality != 0)
            {
                code = "InvalidQuality";
                error = "Only single-item result entries can specify quality.";
                return false;
            }
            switch (type)
            {
                case RewardTypeEnum.Gold:
                case RewardTypeEnum.Currency:
                case RewardTypeEnum.SkillExp:
                case RewardTypeEnum.Hero:
                case RewardTypeEnum.UnlockBuilding:
                case RewardTypeEnum.UnlockLocation:
                    return true;
                default:
                    code = "UnsupportedRewardType";
                    error = $"Reward type '{entry.RewardType}' is not claimable by PendingResult.";
                    return false;
            }
        }

        private static void NormalizeEntries(PendingResultSaveData result)
        {
            var entries = new List<PendingResultEntrySaveData>();
            foreach (var entry in result.entries ?? Array.Empty<PendingResultEntrySaveData>())
                if (entry != null && entry.quantity > 0) entries.Add(entry);
            result.entries = entries.ToArray();
        }

        private static bool SameSource(PendingResultSaveData result, PendingResultDraft draft) =>
            string.Equals(result.sourceType, draft.SourceType, StringComparison.Ordinal) &&
            string.Equals(result.sourceId, draft.SourceId, StringComparison.Ordinal) &&
            string.Equals(result.sourceExecutionId, draft.SourceExecutionId, StringComparison.Ordinal) &&
            string.Equals(result.ownerHeroId, draft.OwnerHeroId, StringComparison.Ordinal);

        private static string BuildResultId(string sourceType, string executionId) => $"result:{sourceType}:{executionId}";

        private static string FormationFingerprint(PendingResultDraft draft, bool makeClaimable, long expectedResultRevision)
        {
            var value = $"form|{draft.SourceType}|{draft.SourceId}|{draft.SourceExecutionId}|{draft.OwnerHeroId}|{makeClaimable}|{expectedResultRevision}";
            if (!string.IsNullOrWhiteSpace(draft.OperationContext))
                return value + "|" + draft.OperationContext;
            foreach (var entry in draft.Entries ?? Array.Empty<PendingResultEntryDraft>())
                if (entry != null) value += $"|{entry.SortOrder}:{entry.RewardType}:{entry.TargetId}:{entry.Quantity}:{entry.Origin}:{entry.Quality}";
            return value;
        }

        private static string DraftEntriesFingerprint(PendingResultEntryDraft[] entries)
        {
            var value = string.Empty;
            foreach (var entry in entries ?? Array.Empty<PendingResultEntryDraft>())
                if (entry != null) value += $"{entry.SortOrder}:{entry.RewardType}:{entry.TargetId}:{entry.Quantity}:{entry.Origin}:{entry.Quality}|";
            return value;
        }

        private static string ContextFingerprint(StorageActionContext context) => context == null ? string.Empty : $"{context.ContextType}:{context.ContextId}";

        private bool StorageChanged(SaveData before) => before.storageRevision != _state.StorageRevision || !SameStorage(before.itemStacks, _state.GetItemStacks()) || !SameInstances(before.itemInstances, _state.GetItemInstances());

        private static bool SameStorage(ItemStackSaveData[] left, ItemStackSaveData[] right)
        {
            left ??= Array.Empty<ItemStackSaveData>(); right ??= Array.Empty<ItemStackSaveData>();
            if (left.Length != right.Length) return false;
            for (var index = 0; index < left.Length; index++)
                if (left[index]?.stackId != right[index]?.stackId || left[index]?.quantity != right[index]?.quantity || left[index]?.stateId != right[index]?.stateId) return false;
            return true;
        }

        private static bool SameInstances(ItemInstanceSaveData[] left, ItemInstanceSaveData[] right)
        {
            left ??= Array.Empty<ItemInstanceSaveData>(); right ??= Array.Empty<ItemInstanceSaveData>();
            if (left.Length != right.Length) return false;
            for (var index = 0; index < left.Length; index++)
                if (left[index]?.instanceId != right[index]?.instanceId || left[index]?.stateId != right[index]?.stateId) return false;
            return true;
        }

        private bool TryNormalize(PendingResultSaveData source, out PendingResultSaveData result)
        {
            result = null;
            if (source == null || string.IsNullOrWhiteSpace(source.resultId) || string.IsNullOrWhiteSpace(source.sourceType) || string.IsNullOrWhiteSpace(source.sourceExecutionId))
                return false;
            result = CloneResult(source);
            if (!string.Equals(source.state, PendingResultState.ResultPending, StringComparison.Ordinal))
                _state.MarkNormalized();
            if (result.revision < 1)
                _state.MarkNormalized();
            result.revision = Math.Max(1, result.revision);
            var entryCount = result.entries?.Length ?? 0;
            NormalizeEntries(result);
            if (result.entries.Length != entryCount)
                _state.MarkNormalized();
            var entryIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in result.entries)
            {
                if (string.IsNullOrWhiteSpace(entry.entryId) || !entryIds.Add(entry.entryId))
                {
                    entry.entryId = Guid.NewGuid().ToString("N");
                    entryIds.Add(entry.entryId);
                    _state.MarkNormalized();
                }
                if (entry.quality < 0)
                {
                    entry.quality = 0;
                    _state.MarkNormalized();
                }
                if (_state.ConfigProvider.TryGetItem(entry.targetId, out var item) && item != null &&
                    _state.ConfigProvider.TryGetStorageRuleForItemKind(item.Kind, out var rule) &&
                    string.Equals(rule.mode, "single", StringComparison.Ordinal) && entry.quantity != 1)
                    return false;
            }
            return result.entries.Length > 0;
        }

        private static PendingResultSaveData CloneResult(PendingResultSaveData source)
        {
            if (source == null) return null;
            var sourceEntries = source.entries ?? Array.Empty<PendingResultEntrySaveData>();
            var entries = new PendingResultEntrySaveData[sourceEntries.Length];
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = sourceEntries[index];
                entries[index] = entry == null ? null : new PendingResultEntrySaveData
                {
                    entryId = entry.entryId,
                    sortOrder = entry.sortOrder,
                    rewardType = entry.rewardType,
                    targetId = entry.targetId,
                    quantity = entry.quantity,
                    origin = entry.origin,
                    quality = entry.quality
                };
            }
            return new PendingResultSaveData
            {
                resultId = source.resultId,
                sourceType = source.sourceType,
                sourceId = source.sourceId,
                sourceExecutionId = source.sourceExecutionId,
                ownerHeroId = source.ownerHeroId,
                state = PendingResultState.ResultPending,
                revision = source.revision,
                entries = entries
            };
        }

        private static PendingResultResolvedEvent ToResolvedEvent(PendingResultSaveData result) => new PendingResultResolvedEvent
        {
            ResultId = result.resultId,
            SourceType = result.sourceType,
            SourceId = result.sourceId,
            SourceExecutionId = result.sourceExecutionId,
            OwnerHeroId = result.ownerHeroId
        };

        private static PendingResultFormationResult FormationFailure(string code, string message) => new PendingResultFormationResult { Success = false, Code = code, Message = message };
        private PendingResultMutationResult MutationFailure(string code, string message) => new PendingResultMutationResult { Success = false, Code = code, Message = message, StorageRevision = _state.StorageRevision };
    }

    public static class PendingResultEntryFactory
    {
        public static PendingResultEntryDraft[] FromActivityRewards(ActivityAppliedReward[] rewards, string origin)
        {
            var entries = new List<PendingResultEntryDraft>();
            var sortOrder = 0;
            foreach (var reward in rewards ?? Array.Empty<ActivityAppliedReward>())
            {
                if (reward == null || reward.amount <= 0 || reward.isResultOnly || reward.lootRoll != null)
                    continue;
                entries.Add(new PendingResultEntryDraft
                {
                    SortOrder = sortOrder++,
                    RewardType = reward.rewardType,
                    TargetId = reward.targetId,
                    Quantity = reward.amount,
                    Origin = origin
                });
            }
            return entries.ToArray();
        }
    }
}
