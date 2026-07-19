using System;
using System.Collections.Generic;
using GuildIdle.Activities;
using GuildIdle.Core;
using GuildIdle.Crafting;
using RuntimeConfigs = GuildIdle.Configs.Configs;

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
        public string InstanceId { get; set; }
    }

    public sealed class PendingResultDraft
    {
        public string SourceType { get; set; }
        public string SourceId { get; set; }
        public string SourceExecutionId { get; set; }
        public string OwnerHeroId { get; set; }
        public long SourceSequence { get; set; }
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
        public bool ResolvedImmediately { get; set; }
        public bool SourceCompleted { get; set; }
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
        void RegisterSourceHandler(IPendingResultSourceHandler handler);
        PendingResultSaveData[] GetSaveData();
        void Load(PendingResultSaveData[] results);
    }

    public interface IPendingResultSourceHandler
    {
        string SourceType { get; }
        bool AcceptsOrigin(string origin);
        bool TryBind(PendingResultSaveData result, bool makeClaimable, PendingResultBindMode mode);
        bool CanClaim(PendingResultSaveData result);
        bool Resolve(PendingResultSaveData result);
    }

    internal interface IPendingResultSourceReconciler
    {
        void Reconcile();
    }

    public enum PendingResultBindMode
    {
        Create,
        Append,
        Restore
    }

    public sealed class PendingResultSourceRegistry
    {
        private readonly Dictionary<string, IPendingResultSourceHandler> _handlers = new Dictionary<string, IPendingResultSourceHandler>(StringComparer.Ordinal);

        public void Register(IPendingResultSourceHandler handler)
        {
            if (handler == null || string.IsNullOrWhiteSpace(handler.SourceType))
                throw new ArgumentException("PendingResult source handler and source type are required.", nameof(handler));
            _handlers[handler.SourceType] = handler;
        }

        public bool TryBind(PendingResultSaveData result, bool makeClaimable, PendingResultBindMode mode) =>
            TryGet(result, out var handler) && handler.TryBind(result, makeClaimable, mode);

        public bool CanClaim(PendingResultSaveData result) =>
            TryGet(result, out var handler) && handler.CanClaim(result);

        public bool Resolve(PendingResultSaveData result) =>
            TryGet(result, out var handler) && handler.Resolve(result);

        public bool HasHandler(string sourceType) => !string.IsNullOrWhiteSpace(sourceType) && _handlers.ContainsKey(sourceType);

        public bool AcceptsOrigin(string sourceType, string origin) =>
            !string.IsNullOrWhiteSpace(sourceType) && _handlers.TryGetValue(sourceType, out var handler) && handler.AcceptsOrigin(origin);

        public void Reconcile()
        {
            foreach (var handler in _handlers.Values)
                if (handler is IPendingResultSourceReconciler reconciler) reconciler.Reconcile();
        }

        private bool TryGet(PendingResultSaveData result, out IPendingResultSourceHandler handler)
        {
            handler = null;
            return result != null && !string.IsNullOrWhiteSpace(result.sourceType) && _handlers.TryGetValue(result.sourceType, out handler);
        }
    }

    internal sealed class ActivityPendingResultSourceHandler : IPendingResultSourceHandler
    {
        private readonly PlayerState _state;

        public ActivityPendingResultSourceHandler(PlayerState state) => _state = state ?? throw new ArgumentNullException(nameof(state));
        public string SourceType => PendingResultSourceType.Activity;
        public bool AcceptsOrigin(string origin) => string.Equals(origin, PendingResultOrigin.ActivityReward, StringComparison.Ordinal);

        public bool TryBind(PendingResultSaveData result, bool makeClaimable, PendingResultBindMode mode)
        {
            var execution = result == null ? null : _state.GetActivityExecution(result.sourceExecutionId);
            if (execution == null || _state.IsPendingResultSourceQuarantined(result.sourceType, result.sourceExecutionId) ||
                execution.status == ActivityRuntimeStatus.Completed || execution.status == ActivityRuntimeStatus.Cancelled ||
                !string.Equals(execution.activityId, result.sourceId, StringComparison.Ordinal) ||
                (!string.IsNullOrWhiteSpace(execution.pendingResultId) && !string.Equals(execution.pendingResultId, result.resultId, StringComparison.Ordinal)))
                return false;
            if (mode == PendingResultBindMode.Create &&
                (execution.status != ActivityRuntimeStatus.Running || !string.IsNullOrWhiteSpace(execution.pendingResultId)))
                return false;
            execution.pendingResultId = result.resultId;
            if (makeClaimable)
                execution.status = ActivityRuntimeStatus.ResultPending;
            return _state.UpdateActivityExecution(execution);
        }

        public bool CanClaim(PendingResultSaveData result)
        {
            var execution = result == null ? null : _state.GetActivityExecution(result.sourceExecutionId);
            if (execution != null && string.Equals(execution.runtimeKind, "Build", StringComparison.Ordinal) &&
                execution.buildingLevelApplied && execution.buildingEventPending && !execution.buildingEventPublished)
                return false;
            return execution != null && !_state.IsPendingResultSourceQuarantined(result.sourceType, result.sourceExecutionId) &&
                   execution.status == ActivityRuntimeStatus.ResultPending &&
                   string.Equals(execution.activityId, result.sourceId, StringComparison.Ordinal) &&
                   string.Equals(execution.pendingResultId, result.resultId, StringComparison.Ordinal);
        }

        public bool Resolve(PendingResultSaveData result)
        {
            if (!CanClaim(result))
                return false;
            var execution = _state.GetActivityExecution(result.sourceExecutionId);
            if (execution.linkedCombat != null &&
                !string.IsNullOrWhiteSpace(execution.linkedCombat.requestId) &&
                string.Equals(execution.linkedCombat.rootExecutionId, execution.executionId, StringComparison.Ordinal) &&
                string.Equals(execution.linkedCombat.occupationOwnerId, execution.executionId, StringComparison.Ordinal))
            {
                execution.activityBagResolved = true;
                return _state.UpdateActivityExecution(execution);
            }
            if (string.Equals(execution.runtimeKind, "Build", StringComparison.Ordinal))
                _state.CompleteActivity(execution.activityId);
            if (_state.ConfigProvider.TryGetActivity(execution.activityId, out var activity) && activity != null && !activity.isRepeatable)
                _state.CompleteActivity(execution.activityId);
            return _state.RemoveActivityExecution(execution.executionId);
        }
    }

    internal sealed class QuestPendingResultSourceHandler : IPendingResultSourceHandler
    {
        private readonly PlayerState _state;

        public QuestPendingResultSourceHandler(PlayerState state) => _state = state ?? throw new ArgumentNullException(nameof(state));
        public string SourceType => PendingResultSourceType.Quest;
        public bool AcceptsOrigin(string origin) => string.Equals(origin, PendingResultOrigin.QuestReward, StringComparison.Ordinal);

        public bool TryBind(PendingResultSaveData result, bool makeClaimable, PendingResultBindMode mode)
        {
            var quest = result == null ? null : _state.GetQuestInstance(result.sourceExecutionId);
            if (quest == null || _state.IsPendingResultSourceQuarantined(result.sourceType, result.sourceExecutionId) ||
                quest.rewardsGranted || quest.status == QuestInstanceStatus.Completed || quest.status == QuestInstanceStatus.Expired ||
                !string.Equals(quest.questId, result.sourceId, StringComparison.Ordinal) ||
                (!string.IsNullOrWhiteSpace(quest.pendingResultId) && !string.Equals(quest.pendingResultId, result.resultId, StringComparison.Ordinal)))
                return false;
            if (mode == PendingResultBindMode.Create &&
                (quest.status != QuestInstanceStatus.Active || !string.IsNullOrWhiteSpace(quest.pendingResultId)))
                return false;
            quest.pendingResultId = result.resultId;
            quest.status = QuestInstanceStatus.RewardPending;
            return _state.SetQuestInstance(quest);
        }

        public bool CanClaim(PendingResultSaveData result)
        {
            var quest = result == null ? null : _state.GetQuestInstance(result.sourceExecutionId);
            return quest != null && !_state.IsPendingResultSourceQuarantined(result.sourceType, result.sourceExecutionId) &&
                   quest.status == QuestInstanceStatus.RewardPending &&
                   string.Equals(quest.questId, result.sourceId, StringComparison.Ordinal) &&
                   string.Equals(quest.pendingResultId, result.resultId, StringComparison.Ordinal);
        }

        public bool Resolve(PendingResultSaveData result)
        {
            if (!CanClaim(result))
                return false;
            var quest = _state.GetQuestInstance(result.sourceExecutionId);
            quest.status = QuestInstanceStatus.Completed;
            quest.rewardsGranted = true;
            quest.pendingResultId = null;
            return _state.SetQuestInstance(quest);
        }
    }

    internal sealed class PersistentPendingResultSourceHandler : IPendingResultSourceHandler
    {
        private readonly PlayerState _state;

        private readonly HashSet<string> _origins;

        public PersistentPendingResultSourceHandler(string sourceType, PlayerState state, params string[] origins)
        {
            SourceType = string.IsNullOrWhiteSpace(sourceType) ? throw new ArgumentException("Source type is required.", nameof(sourceType)) : sourceType;
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _origins = new HashSet<string>(origins ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        public string SourceType { get; }
        public bool AcceptsOrigin(string origin) => !string.IsNullOrWhiteSpace(origin) && _origins.Contains(origin);
        public bool TryBind(PendingResultSaveData result, bool makeClaimable, PendingResultBindMode mode) =>
            _state.TryBindPersistentResultSource(result, mode != PendingResultBindMode.Create);
        public bool CanClaim(PendingResultSaveData result) => _state.CanClaimPersistentResultSource(result);
        public bool Resolve(PendingResultSaveData result) => _state.ResolvePersistentResultSource(result);
    }

    internal sealed class CraftPendingResultSourceHandler : IPendingResultSourceHandler, IPendingResultSourceReconciler
    {
        private readonly PlayerState _state;

        public CraftPendingResultSourceHandler(PlayerState state) => _state = state ?? throw new ArgumentNullException(nameof(state));
        public string SourceType => PendingResultSourceType.Craft;
        public bool AcceptsOrigin(string origin) => string.Equals(origin, PendingResultOrigin.CraftOutput, StringComparison.Ordinal);

        public bool TryBind(PendingResultSaveData result, bool makeClaimable, PendingResultBindMode mode)
        {
            if (mode == PendingResultBindMode.Append)
                return false;
            var execution = result == null ? null : _state.GetCraftExecution(result.sourceExecutionId);
            if (!CraftPendingResultValidator.Validate(execution, result) ||
                _state.IsPendingResultSourceQuarantined(result.sourceType, result.sourceExecutionId) ||
                (!string.IsNullOrWhiteSpace(execution.pendingResultId) &&
                 !string.Equals(execution.pendingResultId, result.resultId, StringComparison.Ordinal)))
                return false;

            if (mode == PendingResultBindMode.Create)
            {
                if (!makeClaimable || execution.status != CraftExecutionStatus.Running || execution.completionRecorded ||
                    !string.IsNullOrWhiteSpace(execution.pendingResultId) || execution.progressSeconds < execution.durationSeconds)
                    return false;
                execution.pendingResultId = result.resultId;
                execution.completionRecorded = true;
                execution.status = CraftExecutionStatus.ResultPending;
            }
            else if (execution.status != CraftExecutionStatus.ResultPending || !execution.completionRecorded ||
                     !string.Equals(execution.pendingResultId, result.resultId, StringComparison.Ordinal))
            {
                return false;
            }

            if (!_state.TryBindPersistentResultSource(result, mode != PendingResultBindMode.Create))
                return false;
            return _state.UpdateCraftExecution(execution);
        }

        public bool CanClaim(PendingResultSaveData result)
        {
            var execution = result == null ? null : _state.GetCraftExecution(result.sourceExecutionId);
            return CraftPendingResultValidator.Validate(execution, result) &&
                   !_state.IsPendingResultSourceQuarantined(result.sourceType, result.sourceExecutionId) &&
                   execution.status == CraftExecutionStatus.ResultPending && execution.completionRecorded &&
                   string.Equals(execution.pendingResultId, result.resultId, StringComparison.Ordinal) &&
                   string.Equals(_state.GetHeroCurrentActivityExecutionId(execution.heroId), execution.executionId, StringComparison.Ordinal) &&
                   _state.CanClaimPersistentResultSource(result);
        }

        public bool Resolve(PendingResultSaveData result)
        {
            var execution = result == null ? null : _state.GetCraftExecution(result.sourceExecutionId);
            if (!CraftPendingResultValidator.ValidateForFinalization(execution, result) ||
                _state.IsPendingResultSourceQuarantined(result.sourceType, result.sourceExecutionId) ||
                execution.status != CraftExecutionStatus.ResultPending || !execution.completionRecorded ||
                !string.Equals(execution.pendingResultId, result.resultId, StringComparison.Ordinal) ||
                !string.Equals(_state.GetHeroCurrentActivityExecutionId(execution.heroId), execution.executionId, StringComparison.Ordinal) ||
                !_state.ResolvePersistentResultSource(result) ||
                !_state.RemoveCraftExecution(execution.executionId))
                return false;

            return string.Equals(_state.GetHeroCurrentActivityExecutionId(execution.heroId), execution.executionId, StringComparison.Ordinal) &&
                   _state.ClearHeroBusy(execution.heroId, execution.executionId);
        }

        public void Reconcile() => _state.ReconcileCraftExecutions();
    }

    public sealed class PendingResultService : IPendingResultService
    {
        private readonly PlayerState _state;
        private readonly StorageService _storage;
        private readonly PendingResultSourceRegistry _sourceLifecycle;
        private readonly Dictionary<string, PendingResultSaveData> _results = new Dictionary<string, PendingResultSaveData>(StringComparer.Ordinal);

        internal PendingResultService(PlayerState state, StorageService storage)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _sourceLifecycle = new PendingResultSourceRegistry();
            _sourceLifecycle.Register(new ActivityPendingResultSourceHandler(state));
            _sourceLifecycle.Register(new QuestPendingResultSourceHandler(state));
            _sourceLifecycle.Register(new PersistentPendingResultSourceHandler(
                PendingResultSourceType.Combat,
                state,
                PendingResultOrigin.CombatLoot,
                PendingResultOrigin.ActivityLootInCombat,
                PendingResultOrigin.BroughtConsumable));
            _sourceLifecycle.Register(new CraftPendingResultSourceHandler(state));
        }

        public event Action<PendingResultResolvedEvent> Resolved;

        public void RegisterSourceHandler(IPendingResultSourceHandler handler) => _sourceLifecycle.Register(handler);

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
                    _state.QuarantinePendingResultSource(source);
                    UnityEngine.Debug.LogError($"[PendingResult] Corrupt result '{source?.resultId ?? "<missing>"}' was quarantined; its source remains blocked to prevent reward reroll.");
                    continue;
                }
                var sourceKey = $"{normalized.sourceType}\n{normalized.sourceExecutionId}";
                if (!sourceKeys.Add(sourceKey))
                {
                    _state.MarkNormalized();
                    _state.QuarantinePendingResultSource(normalized);
                    UnityEngine.Debug.LogError($"[PendingResult] Duplicate source result '{normalized.resultId}' was quarantined; its source remains blocked.");
                    continue;
                }
                _results.Add(normalized.resultId, normalized);
                var makeClaimable = string.Equals(normalized.sourceType, PendingResultSourceType.Quest, StringComparison.Ordinal);
                if (string.Equals(normalized.sourceType, PendingResultSourceType.Activity, StringComparison.Ordinal))
                    makeClaimable = _state.GetActivityExecution(normalized.sourceExecutionId)?.status == ActivityRuntimeStatus.ResultPending;
                if (!_sourceLifecycle.TryBind(normalized, makeClaimable, PendingResultBindMode.Restore))
                {
                    _state.MarkNormalized();
                    _state.QuarantinePendingResultSource(normalized);
                    UnityEngine.Debug.LogError($"[PendingResult] Result '{normalized.resultId}' could not bind to source and remains blocked for manual recovery.");
                }
            }
            _sourceLifecycle.Reconcile();
        }

        public PendingResultFormationResult CreateOrAppend(string operationId, PendingResultDraft draft, bool makeClaimable, long expectedResultRevision = 0)
        {
            if (draft == null || string.IsNullOrWhiteSpace(draft.SourceType) || string.IsNullOrWhiteSpace(draft.SourceId) ||
                string.IsNullOrWhiteSpace(draft.SourceExecutionId) || string.IsNullOrWhiteSpace(operationId) || !_sourceLifecycle.HasHandler(draft.SourceType))
                return FormationFailure("InvalidFormation", "Registered source type, source id, execution id and operation id are required.");
            var isCombat = string.Equals(draft.SourceType, PendingResultSourceType.Combat, StringComparison.Ordinal);
            if (isCombat && draft.SourceSequence <= 0)
                return FormationFailure("CombatSequenceRequired", "Combat result formation requires a positive source sequence.");

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
            var aggregateExisted = _results.TryGetValue(resultId, out var result);
            if (isCombat)
            {
                if (draft.SourceSequence <= _state.LastCombatResultSequence)
                {
                    var existing = Get(resultId);
                    return new PendingResultFormationResult
                    {
                        Success = true,
                        Replayed = true,
                        Code = existing == null ? "Resolved" : "Existing",
                        Result = existing,
                        ResolvedImmediately = existing == null
                    };
                }
                if (aggregateExisted)
                    return FormationFailure("SourceConflict", "A new Combat source sequence cannot append to an existing result.");
                if (draft.SourceSequence != _state.LastCombatResultSequence + 1)
                    return FormationFailure("CombatSequenceGap", "Combat result source sequence must be the next monotonic value.");
                if (!_state.TryAcceptCombatResultSequence(draft.SourceSequence))
                    return FormationFailure("CombatSequenceGap", "Combat result source sequence could not be accepted.");
            }
            if (!aggregateExisted)
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
            var equipmentInstanceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var existingEntry in entries)
                if (existingEntry != null && !string.IsNullOrWhiteSpace(existingEntry.instanceId)) equipmentInstanceIds.Add(existingEntry.instanceId);
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
                if (!_sourceLifecycle.AcceptsOrigin(draft.SourceType, entry.Origin))
                {
                    _state.RestoreTransactional(before);
                    return FormationFailure("InvalidOrigin", $"Origin '{entry.Origin}' is not valid for source type '{draft.SourceType}'.");
                }
                if (IsSkillExp(entry.RewardType) && (string.IsNullOrWhiteSpace(draft.OwnerHeroId) || !_state.HasHero(draft.OwnerHeroId)))
                {
                    _state.RestoreTransactional(before);
                    return FormationFailure("InvalidOwner", "SkillExp result entry requires an acquired owner hero.");
                }
                var instanceId = entry.InstanceId;
                if (string.IsNullOrWhiteSpace(instanceId) && IsSingleItemEntry(entry))
                    instanceId = Guid.NewGuid().ToString("N");
                if (!string.IsNullOrWhiteSpace(instanceId) &&
                    (!equipmentInstanceIds.Add(instanceId) || _state.MutableItemInstances.ContainsKey(instanceId) ||
                     IsInstanceIdInAnotherPendingResult(instanceId, resultId)))
                {
                    _state.RestoreTransactional(before);
                    return FormationFailure("InstanceConflict", $"Equipment instance id '{instanceId}' is already in use.");
                }
                entries.Add(new PendingResultEntrySaveData
                {
                    entryId = Guid.NewGuid().ToString("N"),
                    sortOrder = entry.SortOrder,
                    rewardType = entry.RewardType,
                    targetId = entry.TargetId,
                    quantity = entry.Quantity,
                    origin = entry.Origin,
                    quality = entry.Quality,
                    instanceId = instanceId
                });
            }
            result.entries = entries.ToArray();
            result.revision++;

            if (!_sourceLifecycle.TryBind(result, makeClaimable, aggregateExisted ? PendingResultBindMode.Append : PendingResultBindMode.Create))
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
            if (resolvedImmediately)
                Resolved?.Invoke(ToResolvedEvent(result, true));
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
                SourceSequence = calculatedResult.SourceSequence,
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
            if (combatDraft.SourceSequence <= 0)
                return FormationFailure("CombatSequenceRequired", "Combat result formation requires a positive source sequence.");
            if (combatDraft.SourceSequence <= _state.LastCombatResultSequence)
                return CreateOrAppend(operationId, combatDraft, true, 0);
            if (combatDraft.SourceSequence != _state.LastCombatResultSequence + 1)
                return FormationFailure("CombatSequenceGap", "Combat result source sequence must be the next monotonic value.");
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
            {
                if (_state.IsPendingResultSourceResolved(resultId))
                {
                    return new PendingResultMutationResult
                    {
                        Success = true,
                        Replayed = true,
                        Code = "Resolved",
                        ResultRevision = expectedResultRevision == long.MaxValue ? long.MaxValue : expectedResultRevision + 1,
                        StorageRevision = _state.StorageRevision,
                        Resolved = true
                    };
                }
                return MutationFailure("ResultNotFound", "PendingResult does not exist or has already been resolved.");
            }
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
            var nonItemEntries = new List<PendingResultEntrySaveData>();
            var nonItemMutations = new List<RewardMutation>();
            foreach (var entry in entries)
            {
                if (entry == null || entry.quantity <= 0 || IsItemReward(entry.rewardType))
                    continue;
                if (!TryBuildRewardMutation(result, entry, entry.quantity, out var mutation, out var mutationError))
                    return mutationError;
                nonItemEntries.Add(entry);
                nonItemMutations.Add(mutation);
            }

            if (nonItemMutations.Count > 0)
            {
                if (!_state.TryApplyRewardBatch(nonItemMutations.ToArray(), out _, out var applyError))
                    return applyError ?? "Reward mutation batch failed.";
                foreach (var entry in nonItemEntries)
                {
                    entry.quantity = 0;
                    changedEntries.Add(entry.entryId);
                }
            }

            foreach (var entry in entries)
            {
                if (entry == null || entry.quantity <= 0 || !IsItemReward(entry.rewardType))
                    continue;
                var error = ClaimOne(result, entry, entry.quantity, allowPartial, changedEntries);
                if (error != null)
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
                if (!_storage.TryAddResultItem(entry.targetId, (int)quantity, entry.quality, allowPartial, entry.instanceId, out var accepted, out _, out _, out var error))
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
                if (!TryValidateItemTarget(type, entry.TargetId) ||
                    !_state.ConfigProvider.TryGetItem(entry.TargetId, out var item) || item == null ||
                    !_state.ConfigProvider.TryGetStorageRuleForItemKind(item.Kind, out var rule))
                {
                    code = "UnknownItemReward";
                    error = $"Item reward target '{entry.TargetId}' is invalid for type '{entry.RewardType}'.";
                    return false;
                }
                var single = string.Equals(rule.mode, "single", StringComparison.Ordinal);
                if (single && entry.Quantity != 1)
                {
                    code = "InvalidEquipmentQuantity";
                    error = "Single equipment result entries must have quantity 1.";
                    return false;
                }
                if (!single && !string.IsNullOrWhiteSpace(entry.InstanceId))
                {
                    code = "InvalidInstancePayload";
                    error = "Only single-item result entries can specify instanceId.";
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

            if (entry.Quality != 0 || !string.IsNullOrWhiteSpace(entry.InstanceId))
            {
                code = "InvalidInstancePayload";
                error = "Only single-item result entries can specify quality or instanceId.";
                return false;
            }
            switch (type)
            {
                case RewardTypeEnum.Gold:
                    if (!RuntimeConfigs.Items.TryGetCurrency(entry.TargetId, out _))
                        return InvalidTarget(entry, out code, out error);
                    return true;
                case RewardTypeEnum.Currency:
                    if (!RuntimeConfigs.Items.TryGetCurrency(entry.TargetId, out _))
                        return InvalidTarget(entry, out code, out error);
                    return true;
                case RewardTypeEnum.SkillExp:
                    if (string.IsNullOrWhiteSpace(entry.TargetId) || !IsKnownSkill(entry.TargetId))
                        return InvalidTarget(entry, out code, out error);
                    return true;
                case RewardTypeEnum.Hero:
                    if (!RuntimeConfigs.Heroes.TryGet(entry.TargetId, out _))
                        return InvalidTarget(entry, out code, out error);
                    return true;
                case RewardTypeEnum.UnlockBuilding:
                    if (!RuntimeConfigs.Buildings.TryGet(entry.TargetId, out _))
                        return InvalidTarget(entry, out code, out error);
                    return true;
                case RewardTypeEnum.UnlockLocation:
                    if (!RuntimeConfigs.Map.TryGetLocation(entry.TargetId, out _))
                        return InvalidTarget(entry, out code, out error);
                    return true;
                default:
                    code = "UnsupportedRewardType";
                    error = $"Reward type '{entry.RewardType}' is not claimable by PendingResult.";
                    return false;
            }
        }

        private static bool InvalidTarget(PendingResultEntryDraft entry, out string code, out string error)
        {
            code = "InvalidRewardTarget";
            error = $"Reward target '{entry.TargetId}' is invalid for type '{entry.RewardType}'.";
            return false;
        }

        private static bool TryValidateItemTarget(RewardTypeEnum type, string targetId)
        {
            switch (type)
            {
                case RewardTypeEnum.Resource:
                    return RuntimeConfigs.Items.TryGetResource(targetId, out _);
                case RewardTypeEnum.Equipment:
                    return RuntimeConfigs.Items.TryGetEquipmentWeapon(targetId, out _) || RuntimeConfigs.Items.TryGetEquipmentArmor(targetId, out _);
                case RewardTypeEnum.Consumable:
                    return RuntimeConfigs.Items.TryGetConsumable(targetId, out _);
                case RewardTypeEnum.Recipe:
                    return RuntimeConfigs.Items.TryGetRecipe(targetId, out _);
                case RewardTypeEnum.Item:
                    return RuntimeConfigs.Items.TryGet(targetId, out _);
                default:
                    return false;
            }
        }

        private static bool IsKnownSkill(string skillId)
        {
            foreach (var skill in RuntimeConfigs.Activities.Skills)
                if (skill != null && string.Equals(skill.skillId, skillId, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool IsSkillExp(string rewardType) =>
            ActivityTypeParser.TryParseRewardType(rewardType, out var type) && type == RewardTypeEnum.SkillExp;

        private bool IsSingleItemEntry(PendingResultEntryDraft entry)
        {
            return entry != null && IsItemReward(entry.RewardType) &&
                   _state.ConfigProvider.TryGetItem(entry.TargetId, out var item) && item != null &&
                   _state.ConfigProvider.TryGetStorageRuleForItemKind(item.Kind, out var rule) &&
                   string.Equals(rule.mode, "single", StringComparison.Ordinal);
        }

        private bool IsInstanceIdInAnotherPendingResult(string instanceId, string excludedResultId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                return false;
            foreach (var pending in _results.Values)
            {
                if (pending == null || string.Equals(pending.resultId, excludedResultId, StringComparison.Ordinal))
                    continue;
                foreach (var entry in pending.entries ?? Array.Empty<PendingResultEntrySaveData>())
                    if (entry != null && string.Equals(entry.instanceId, instanceId, StringComparison.Ordinal))
                        return true;
            }
            return false;
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
            if (draft.SourceSequence > 0)
                value += $"|sequence:{draft.SourceSequence}";
            if (!string.IsNullOrWhiteSpace(draft.OperationContext))
                return value + "|" + draft.OperationContext;
            foreach (var entry in draft.Entries ?? Array.Empty<PendingResultEntryDraft>())
                if (entry != null) value += $"|{entry.SortOrder}:{entry.RewardType}:{entry.TargetId}:{entry.Quantity}:{entry.Origin}:{entry.Quality}:{entry.InstanceId}";
            return value;
        }

        private static string DraftEntriesFingerprint(PendingResultEntryDraft[] entries)
        {
            var value = string.Empty;
            foreach (var entry in entries ?? Array.Empty<PendingResultEntryDraft>())
                if (entry != null) value += $"{entry.SortOrder}:{entry.RewardType}:{entry.TargetId}:{entry.Quantity}:{entry.Origin}:{entry.Quality}:{entry.InstanceId}|";
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
            if (source == null || string.IsNullOrWhiteSpace(source.resultId) || string.IsNullOrWhiteSpace(source.sourceType) ||
                string.IsNullOrWhiteSpace(source.sourceId) || string.IsNullOrWhiteSpace(source.sourceExecutionId) ||
                !_sourceLifecycle.HasHandler(source.sourceType) ||
                !string.Equals(source.resultId, BuildResultId(source.sourceType, source.sourceExecutionId), StringComparison.Ordinal) ||
                !string.Equals(source.state, PendingResultState.ResultPending, StringComparison.Ordinal) ||
                source.revision < 1 || source.entries == null || source.entries.Length == 0)
                return false;
            result = CloneResult(source);
            var entryIds = new HashSet<string>(StringComparer.Ordinal);
            var instanceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in result.entries)
            {
                if (entry == null || entry.quantity <= 0 || string.IsNullOrWhiteSpace(entry.entryId) || !entryIds.Add(entry.entryId) ||
                    string.IsNullOrWhiteSpace(entry.rewardType) || string.IsNullOrWhiteSpace(entry.targetId) ||
                    !_sourceLifecycle.AcceptsOrigin(result.sourceType, entry.origin))
                    return false;
                var draft = new PendingResultEntryDraft
                {
                    RewardType = entry.rewardType,
                    TargetId = entry.targetId,
                    Quantity = entry.quantity,
                    Origin = entry.origin,
                    Quality = entry.quality,
                    InstanceId = entry.instanceId
                };
                if (!TryValidateDraftEntry(draft, out _, out _))
                    return false;
                if (IsSkillExp(entry.rewardType) && (string.IsNullOrWhiteSpace(result.ownerHeroId) || !_state.HasHero(result.ownerHeroId)))
                    return false;
                var instanceId = draft.InstanceId;
                if (string.IsNullOrWhiteSpace(instanceId) && IsSingleItemEntry(draft))
                    instanceId = Guid.NewGuid().ToString("N");
                if (!string.Equals(entry.instanceId, instanceId, StringComparison.Ordinal))
                {
                    entry.instanceId = instanceId;
                    _state.MarkNormalized();
                }
                if (!string.IsNullOrWhiteSpace(entry.instanceId) &&
                    (!instanceIds.Add(entry.instanceId) || _state.MutableItemInstances.ContainsKey(entry.instanceId) ||
                     IsInstanceIdInAnotherPendingResult(entry.instanceId, result.resultId)))
                    return false;
            }
            return true;
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
                    quality = entry.quality,
                    instanceId = entry.instanceId
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

        private PendingResultResolvedEvent ToResolvedEvent(PendingResultSaveData result, bool resolvedImmediately = false) => new PendingResultResolvedEvent
        {
            ResultId = result.resultId,
            SourceType = result.sourceType,
            SourceId = result.sourceId,
            SourceExecutionId = result.sourceExecutionId,
            OwnerHeroId = result.ownerHeroId,
            ResolvedImmediately = resolvedImmediately,
            SourceCompleted = !string.Equals(result.sourceType, PendingResultSourceType.Activity, StringComparison.Ordinal) ||
                              _state.GetActivityExecution(result.sourceExecutionId) == null
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
