using System;
using GuildIdle.Activities;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Player;
using UnityEngine;

namespace GuildIdle.Combat
{
    public enum CombatStartKind
    {
        Direct = 1,
        Linked = 2
    }

    public enum CombatStartCode
    {
        Applied = 0,
        Replayed = 1,
        InvalidCommand = 2,
        OperationIdRequired = 3,
        OperationConflict = 4,
        InvalidSourceContract = 5,
        SourceAlreadyStarted = 6,
        ActivityUnavailable = 7,
        InvalidActivityDescriptor = 8,
        HeroNotFound = 9,
        HeroBusy = 10,
        ActiveHeroLimitReached = 11,
        InsufficientFatigue = 12,
        InvalidLoadout = 13,
        StackNotFound = 14,
        StackUnavailable = 15,
        UnsupportedConsumable = 16,
        QuantityExceedsStack = 17,
        QuantityExceedsMaxStack = 18,
        StaleStorageRevision = 19,
        EnemyQueueInvalid = 20,
        HeroSnapshotInvalid = 21,
        TransactionFailure = 22,
        SaveFailure = 23,
        ActivityRequirementsNotMet = 24,
        ActivityCompleted = 25,
        ActivityAlreadyRunning = 26,
        CorruptedReplayState = 27
    }

    public sealed class CombatStartCommand
    {
        public string OperationId { get; set; }
        public CombatStartKind Kind { get; set; }
        public string SourceActivityId { get; set; }
        public string SourceExecutionId { get; set; }
        public string SourceRequestId { get; set; }
        public string OccupationOwnerId { get; set; }
        public string HeroId { get; set; }
        public string EnemyGroupId { get; set; }
        public string CombatMode { get; set; }
        public string StackId { get; set; }
        public int RequestedQuantity { get; set; }
        public long ExpectedStorageRevision { get; set; }
    }

    public sealed class CombatStartResult
    {
        public bool Success { get; internal set; }
        public bool Replayed { get; internal set; }
        public CombatStartCode Code { get; internal set; }
        public string Message { get; internal set; }
        public string ExecutionId { get; internal set; }
        public string SessionId { get; internal set; }
        public CombatRuntimeAggregate Aggregate { get; internal set; }
    }

    public sealed class CombatStartedEvent
    {
        public string ExecutionId { get; internal set; }
        public string SessionId { get; internal set; }
        public string SourceActivityId { get; internal set; }
        public string SourceExecutionId { get; internal set; }
        public string SourceRequestId { get; internal set; }
        public string OccupationOwnerId { get; internal set; }
        public string HeroId { get; internal set; }
        public CombatLoadoutKind LoadoutKind { get; internal set; }
    }

    public sealed class CombatStartActivityDescriptor
    {
        public CombatStartActivityDescriptor(
            string activityId,
            string enemyGroupId,
            string combatMode,
            int fatigueCost,
            bool isRepeatable = false)
        {
            ActivityId = activityId;
            EnemyGroupId = enemyGroupId;
            CombatMode = combatMode;
            FatigueCost = fatigueCost;
            IsRepeatable = isRepeatable;
        }

        public string ActivityId { get; }
        public string EnemyGroupId { get; }
        public string CombatMode { get; }
        public int FatigueCost { get; }
        public bool IsRepeatable { get; }
    }

    public interface ICombatStartActivityDescriptorProvider
    {
        bool TryGet(string activityId, out CombatStartActivityDescriptor descriptor, out string error);
    }

    public sealed class ConfigCombatStartActivityDescriptorProvider :
        ICombatStartActivityDescriptorProvider
    {
        private readonly ActivitiesConfigRepository _configs;

        public ConfigCombatStartActivityDescriptorProvider(ActivitiesConfigRepository configs)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public bool TryGet(
            string activityId,
            out CombatStartActivityDescriptor descriptor,
            out string error)
        {
            descriptor = null;
            error = null;
            if (!_configs.TryGet(activityId, out var activity) ||
                activity == null ||
                !_configs.TryGetCombatDetails(activityId, out var details) ||
                details == null)
            {
                error = $"Combat activity '{activityId ?? "<null>"}' was not found.";
                return false;
            }

            if (!string.Equals(activity.type, "CombatTask", StringComparison.OrdinalIgnoreCase) ||
                activity.fatigueCost < 0 ||
                string.IsNullOrWhiteSpace(details.enemyGroupId) ||
                string.IsNullOrWhiteSpace(details.combatMode))
            {
                error = $"Combat activity '{activityId}' has an invalid start descriptor.";
                return false;
            }

            descriptor = new CombatStartActivityDescriptor(
                activity.id,
                details.enemyGroupId,
                details.combatMode,
                activity.fatigueCost,
                activity.isRepeatable);
            return true;
        }
    }

    public interface ICombatStartIdentityProvider
    {
        string CreateExecutionId();
        string CreateSessionId();
        ulong CreateRngSeed();
        long GetUtcNowUnixSeconds();
    }

    public sealed class CombatStartIdentityProvider : ICombatStartIdentityProvider
    {
        public string CreateExecutionId() => Guid.NewGuid().ToString("N");
        public string CreateSessionId() => Guid.NewGuid().ToString("N");

        public ulong CreateRngSeed()
        {
            return BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
        }

        public long GetUtcNowUnixSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public interface ICombatStartPlayerState
    {
        SaveData CaptureCheckpoint();
        void RestoreCheckpoint(SaveData checkpoint);
        bool TryGetOperationReceipt(
            string aggregateId,
            string operationId,
            out OperationReceiptSaveData receipt);
        void RecordOperationReceipt(OperationReceiptSaveData receipt);
        bool HasHero(string heroId);
        bool HasHeroState(string heroId);
        int GetHeroFatigue(string heroId);
        bool SpendHeroFatigue(string heroId, int amount);
        bool IsHeroBusy(string heroId);
        string GetHeroOccupationOwnerId(string heroId);
        int GetActiveHeroCount();
        int GetActiveHeroLimit();
        bool IsActivityAvailable(string activityId);
        ActivityCheckResult CanStartActivity(ActivityExecutionContext context);
        bool IsActivityCompleted(string activityId);
        bool HasUnfinishedActivityExecution(string activityId);
        ActivityExecutionSaveData GetActivityExecution(string executionId);
        bool BindLinkedCombatExecution(
            string sourceExecutionId,
            string sourceRequestId,
            string combatExecutionId);
        long GetStorageRevision();
        bool TryGetCombatSourceStack(
            string stackId,
            StorageActionContext actionContext,
            out ItemStackSaveData stack,
            out string code,
            out string error);
        bool TryExtractCombatSourceStack(
            string stackId,
            int quantity,
            StorageActionContext actionContext,
            out string itemId,
            out string error);
        bool TryCreateHeroCombatant(
            string heroId,
            string sessionId,
            out CombatantStateSaveData hero,
            out string error);
        CombatRuntimeAggregate[] GetCombatAggregates();
        CombatRuntimeAggregate GetCombatAggregate(string executionId);
        bool AddCombatAggregate(CombatRuntimeAggregate aggregate);
        void PublishCombatStartCommit();
        bool Save();
    }

    public sealed class CombatStartService
    {
        private const string ReceiptAggregateId = "combat-start";

        private readonly ICombatStartPlayerState _state;
        private readonly ICombatStartActivityDescriptorProvider _activities;
        private readonly ICombatConsumableDescriptorProvider _consumables;
        private readonly ICombatEnemyQueueProvider _enemyQueue;
        private readonly ICombatRngFactory _rngFactory;
        private readonly ICombatStartIdentityProvider _identity;
        private readonly Action<CombatStartedEvent> _eventSink;

        public CombatStartService(
            ICombatStartPlayerState state,
            ICombatStartActivityDescriptorProvider activities,
            ICombatConsumableDescriptorProvider consumables,
            ICombatEnemyQueueProvider enemyQueue,
            ICombatRngFactory rngFactory = null,
            ICombatStartIdentityProvider identity = null,
            Action<CombatStartedEvent> eventSink = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _activities = activities ?? throw new ArgumentNullException(nameof(activities));
            _consumables = consumables ?? throw new ArgumentNullException(nameof(consumables));
            _enemyQueue = enemyQueue ?? throw new ArgumentNullException(nameof(enemyQueue));
            _rngFactory = rngFactory ?? new CombatRngFactory();
            _identity = identity ?? new CombatStartIdentityProvider();
            _eventSink = eventSink;
        }

        public CombatStartResult Start(CombatStartCommand command)
        {
            Preflight preflight;
            CombatStartResult failure;
            try
            {
                if (!TryPreflight(command, out preflight, out failure))
                    return failure;
            }
            catch (Exception exception)
            {
                return Failed(
                    CombatStartCode.TransactionFailure,
                    $"Combat start preflight failed: {exception.Message}");
            }

            if (preflight.Replay != null)
                return preflight.Replay;

            var checkpoint = _state.CaptureCheckpoint();
            var committed = false;
            try
            {
                if (preflight.LoadoutKind == CombatLoadoutKind.Consumable)
                {
                    if (!_state.TryExtractCombatSourceStack(
                            command.StackId,
                            command.RequestedQuantity,
                            preflight.StorageContext,
                            out var extractedItemId,
                            out var extractionError) ||
                        !string.Equals(
                            extractedItemId,
                            preflight.Stack.itemId,
                            StringComparison.Ordinal))
                    {
                        _state.RestoreCheckpoint(checkpoint);
                        return Failed(
                            CombatStartCode.TransactionFailure,
                            extractionError ?? "Combat source stack changed after preflight.");
                    }
                }

                if (command.Kind == CombatStartKind.Direct &&
                    preflight.Activity.FatigueCost > 0 &&
                    !_state.SpendHeroFatigue(command.HeroId, preflight.Activity.FatigueCost))
                {
                    _state.RestoreCheckpoint(checkpoint);
                    return Failed(
                        CombatStartCode.TransactionFailure,
                        "Failed to spend direct combat fatigue after a successful preflight.");
                }

                if (!_state.AddCombatAggregate(preflight.Aggregate))
                {
                    _state.RestoreCheckpoint(checkpoint);
                    return Failed(
                        CombatStartCode.TransactionFailure,
                        "Failed to create the CombatExecution/CombatSession aggregate.");
                }

                if (command.Kind == CombatStartKind.Linked &&
                    !_state.BindLinkedCombatExecution(
                        command.SourceExecutionId,
                        command.SourceRequestId,
                        preflight.Aggregate.execution.executionId))
                {
                    _state.RestoreCheckpoint(checkpoint);
                    return Failed(
                        CombatStartCode.TransactionFailure,
                        "Failed to bind the linked combat source inside the start transaction.");
                }

                var payload = new CombatStartReceiptPayload
                {
                    executionId = preflight.Aggregate.execution.executionId,
                    sessionId = preflight.Aggregate.session.sessionId
                };
                _state.RecordOperationReceipt(new OperationReceiptSaveData
                {
                    aggregateId = ReceiptAggregateId,
                    operationId = command.OperationId,
                    fingerprint = preflight.Fingerprint,
                    success = true,
                    code = CombatStartCode.Applied.ToString(),
                    storageRevision = _state.GetStorageRevision(),
                    executionId = payload.executionId,
                    resultPayload = JsonUtility.ToJson(payload)
                });

                if (!_state.Save())
                {
                    _state.RestoreCheckpoint(checkpoint);
                    return Failed(
                        CombatStartCode.SaveFailure,
                        "Failed to save the combat start transaction.");
                }

                committed = true;
                var stored = _state.GetCombatAggregate(payload.executionId) ??
                             preflight.Aggregate;
                _state.PublishCombatStartCommit();
                _eventSink?.Invoke(ToStartedEvent(stored));
                return Succeeded(stored, false);
            }
            catch (Exception exception)
            {
                if (committed)
                {
                    Debug.LogException(exception);
                    var stored = _state.GetCombatAggregate(
                        preflight.Aggregate.execution.executionId) ??
                                 preflight.Aggregate;
                    return Succeeded(stored, false);
                }

                _state.RestoreCheckpoint(checkpoint);
                return Failed(
                    CombatStartCode.TransactionFailure,
                    $"Combat start transaction failed: {exception.Message}");
            }
        }

        private bool TryPreflight(
            CombatStartCommand command,
            out Preflight preflight,
            out CombatStartResult failure)
        {
            preflight = null;
            failure = null;
            if (command == null)
                return FailPreflight(CombatStartCode.InvalidCommand, "Combat start command is required.", out failure);

            var fingerprint = BuildFingerprint(command);
            if (string.IsNullOrWhiteSpace(command.OperationId))
                return FailPreflight(CombatStartCode.OperationIdRequired, "operation_id is required.", out failure);

            var hasReceipt = _state.TryGetOperationReceipt(
                ReceiptAggregateId,
                command.OperationId,
                out var receipt);
            if (hasReceipt &&
                !string.Equals(receipt.fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return FailPreflight(
                    CombatStartCode.OperationConflict,
                    "operation_id was already used with another combat start payload.",
                    out failure);
            }

            var matchingOperationAggregate = FindOperationAggregate(
                command.OperationId,
                out var matchingOperationCount);
            if (matchingOperationCount > 1)
            {
                return FailPreflight(
                    CombatStartCode.TransactionFailure,
                    "Multiple combat aggregates reference the same start operation.",
                    out failure);
            }

            if (matchingOperationAggregate != null)
            {
                if (!string.Equals(
                        matchingOperationAggregate.execution.startFingerprint,
                        fingerprint,
                        StringComparison.Ordinal))
                {
                    return FailPreflight(
                        CombatStartCode.OperationConflict,
                        "operation_id was already used with another combat start payload.",
                        out failure);
                }

                var receiptPayload = hasReceipt
                    ? ParseReceiptPayload(receipt)
                    : null;
                if (!AggregateMatchesCommand(matchingOperationAggregate, command) ||
                    hasReceipt &&
                    (!receipt.success ||
                     receiptPayload == null ||
                     !string.Equals(
                         receipt.executionId,
                         matchingOperationAggregate.execution.executionId,
                         StringComparison.Ordinal) ||
                     !string.Equals(
                         receiptPayload.executionId,
                         matchingOperationAggregate.execution.executionId,
                         StringComparison.Ordinal) ||
                     !string.Equals(
                         receiptPayload.sessionId,
                         matchingOperationAggregate.session.sessionId,
                         StringComparison.Ordinal)))
                {
                    return FailPreflight(
                        CombatStartCode.CorruptedReplayState,
                        "Combat start idempotency data does not reference a valid aggregate.",
                        out failure);
                }

                preflight = new Preflight
                {
                    Fingerprint = fingerprint,
                    Replay = Succeeded(matchingOperationAggregate, true)
                };
                return true;
            }

            if (hasReceipt)
            {
                return FailPreflight(
                    CombatStartCode.CorruptedReplayState,
                    "Combat start receipt has no corresponding combat aggregate.",
                    out failure);
            }

            if (!TryValidateCommand(command, out var loadoutKind, out failure))
                return false;

            CombatStartActivityDescriptor activity = null;
            if (command.Kind == CombatStartKind.Direct &&
                (!_activities.TryGet(
                     command.SourceActivityId,
                     out activity,
                     out var activityError) ||
                 activity == null ||
                 !string.Equals(
                     activity.EnemyGroupId,
                     command.EnemyGroupId,
                     StringComparison.Ordinal) ||
                 !string.Equals(
                     activity.CombatMode,
                     command.CombatMode,
                     StringComparison.Ordinal)))
            {
                return FailPreflight(
                    CombatStartCode.InvalidActivityDescriptor,
                    activityError ?? "Combat activity/group descriptor does not match the command.",
                    out failure);
            }
            if (command.Kind == CombatStartKind.Linked)
            {
                activity = new CombatStartActivityDescriptor(
                    command.SourceActivityId,
                    command.EnemyGroupId,
                    command.CombatMode,
                    0);
            }

            var sourceMatch = FindSourceAggregate(command.SourceRequestId);
            if (sourceMatch != null)
            {
                return FailPreflight(
                    CombatStartCode.SourceAlreadyStarted,
                    $"Combat source request '{command.SourceRequestId}' is already started.",
                    out failure);
            }

            if (!_state.HasHero(command.HeroId) ||
                !_state.HasHeroState(command.HeroId))
            {
                return FailPreflight(
                    CombatStartCode.HeroNotFound,
                    $"Hero '{command.HeroId}' is not acquired or has no runtime state.",
                    out failure);
            }

            if (!TryCreateIdentifiers(
                    command.OperationId,
                    out var executionId,
                    out var sessionId,
                    out failure))
            {
                return false;
            }

            StorageActionContext storageContext;
            if (command.Kind == CombatStartKind.Direct)
            {
                storageContext = null;
                if (!_state.IsActivityAvailable(command.SourceActivityId))
                {
                    return FailPreflight(
                        CombatStartCode.ActivityUnavailable,
                        $"Combat activity '{command.SourceActivityId}' is unavailable.",
                        out failure);
                }
                if (_state.IsHeroBusy(command.HeroId))
                {
                    return FailPreflight(
                        CombatStartCode.HeroBusy,
                        $"Hero '{command.HeroId}' is already occupied.",
                        out failure);
                }
                if (_state.GetActiveHeroCount() >= _state.GetActiveHeroLimit())
                {
                    return FailPreflight(
                        CombatStartCode.ActiveHeroLimitReached,
                        "Active hero limit has been reached.",
                        out failure);
                }
                if (_state.GetHeroFatigue(command.HeroId) < activity.FatigueCost)
                {
                    return FailPreflight(
                        CombatStartCode.InsufficientFatigue,
                        "Hero has insufficient fatigue for direct combat start.",
                        out failure);
                }

                var eligibility = _state.CanStartActivity(
                    new ActivityExecutionContext
                    {
                        activityId = command.SourceActivityId,
                        heroId = command.HeroId,
                        executionId = executionId
                    });
                if (eligibility == null || !eligibility.canStart)
                {
                    return FailPreflight(
                        CombatStartCode.ActivityRequirementsNotMet,
                        FirstEligibilityMessage(eligibility),
                        out failure);
                }
                if (!activity.IsRepeatable &&
                    _state.IsActivityCompleted(command.SourceActivityId))
                {
                    return FailPreflight(
                        CombatStartCode.ActivityCompleted,
                        $"Activity '{command.SourceActivityId}' is non-repeatable and already completed.",
                        out failure);
                }
                if (!activity.IsRepeatable &&
                    HasUnfinishedActivity(command.SourceActivityId))
                {
                    return FailPreflight(
                        CombatStartCode.ActivityAlreadyRunning,
                        $"Activity '{command.SourceActivityId}' is non-repeatable and already has an unfinished execution.",
                        out failure);
                }
            }
            else
            {
                storageContext = new StorageActionContext(
                    StorageContextType.ActivityExecution,
                    command.OccupationOwnerId);
                if (!TryValidateLinkedSource(command, out failure))
                    return false;
            }

            if (command.ExpectedStorageRevision != _state.GetStorageRevision())
            {
                return FailPreflight(
                    CombatStartCode.StaleStorageRevision,
                    $"Expected storage revision {command.ExpectedStorageRevision}, current revision is {_state.GetStorageRevision()}.",
                    out failure);
            }

            ItemStackSaveData stack = null;
            CombatConsumableDescriptor consumable = null;
            if (loadoutKind == CombatLoadoutKind.Consumable)
            {
                if (!_state.TryGetCombatSourceStack(
                        command.StackId,
                        storageContext,
                        out stack,
                        out var stackCode,
                        out var stackError))
                {
                    var code = string.Equals(
                        stackCode,
                        "StackNotFound",
                        StringComparison.Ordinal)
                        ? CombatStartCode.StackNotFound
                        : CombatStartCode.StackUnavailable;
                    return FailPreflight(code, stackError, out failure);
                }
                if (command.RequestedQuantity > stack.quantity)
                {
                    return FailPreflight(
                        CombatStartCode.QuantityExceedsStack,
                        "requested_quantity exceeds the selected Storage stack.",
                        out failure);
                }
                if (!_consumables.TryGet(stack.itemId, out consumable) ||
                    consumable == null ||
                    consumable.UsePlace != CombatConsumableUsePlace.Combat)
                {
                    return FailPreflight(
                        CombatStartCode.UnsupportedConsumable,
                        $"Item '{stack.itemId}' is not an enabled combat consumable.",
                        out failure);
                }
                if (command.RequestedQuantity > consumable.MaxStack)
                {
                    return FailPreflight(
                        CombatStartCode.QuantityExceedsMaxStack,
                        "requested_quantity exceeds the combat consumable MaxStack.",
                        out failure);
                }
            }

            if (!TryBuildAggregate(
                    command,
                    fingerprint,
                    activity,
                    loadoutKind,
                    stack,
                    consumable,
                    executionId,
                    sessionId,
                    out var aggregate,
                    out failure))
            {
                return false;
            }

            preflight = new Preflight
            {
                Fingerprint = fingerprint,
                Activity = activity,
                LoadoutKind = loadoutKind,
                StorageContext = storageContext,
                Stack = stack,
                Aggregate = aggregate
            };
            return true;
        }

        private bool TryValidateCommand(
            CombatStartCommand command,
            out CombatLoadoutKind loadoutKind,
            out CombatStartResult failure)
        {
            loadoutKind = CombatLoadoutKind.None;
            failure = null;
            if ((command.Kind != CombatStartKind.Direct &&
                 command.Kind != CombatStartKind.Linked) ||
                string.IsNullOrWhiteSpace(command.SourceActivityId) ||
                string.IsNullOrWhiteSpace(command.SourceRequestId) ||
                string.IsNullOrWhiteSpace(command.HeroId) ||
                string.IsNullOrWhiteSpace(command.EnemyGroupId) ||
                string.IsNullOrWhiteSpace(command.CombatMode) ||
                command.ExpectedStorageRevision < 0)
            {
                return FailPreflight(
                    CombatStartCode.InvalidCommand,
                    "Combat start identity, source descriptor and non-negative storage revision are required.",
                    out failure);
            }

            if (command.Kind == CombatStartKind.Direct)
            {
                if (!string.IsNullOrWhiteSpace(command.SourceExecutionId) ||
                    !string.IsNullOrWhiteSpace(command.OccupationOwnerId))
                {
                    return FailPreflight(
                        CombatStartCode.InvalidSourceContract,
                        "Direct combat source_execution_id and occupation_owner_id are generated by the transaction.",
                        out failure);
                }
            }
            else if (string.IsNullOrWhiteSpace(command.SourceExecutionId) ||
                     string.IsNullOrWhiteSpace(command.OccupationOwnerId))
            {
                return FailPreflight(
                    CombatStartCode.InvalidSourceContract,
                    "Linked combat requires source_execution_id and occupation_owner_id.",
                    out failure);
            }

            if (string.IsNullOrWhiteSpace(command.StackId) &&
                command.RequestedQuantity == 0)
            {
                loadoutKind = CombatLoadoutKind.Empty;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(command.StackId) &&
                command.RequestedQuantity >= 1)
            {
                loadoutKind = CombatLoadoutKind.Consumable;
                return true;
            }

            return FailPreflight(
                CombatStartCode.InvalidLoadout,
                "Empty loadout requires null stack_id and quantity 0; consumable loadout requires stack_id and quantity >= 1.",
                out failure);
        }

        private bool TryValidateLinkedSource(
            CombatStartCommand command,
            out CombatStartResult failure)
        {
            failure = null;
            var source = _state.GetActivityExecution(command.SourceExecutionId);
            var linked = source?.linkedCombat;
            if (source == null ||
                linked == null ||
                linked.resolved ||
                !string.Equals(
                    source.activityId,
                    command.SourceActivityId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    source.executionId,
                    linked.rootExecutionId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    linked.requestId,
                    command.SourceRequestId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    linked.occupationOwnerId,
                    command.OccupationOwnerId,
                    StringComparison.Ordinal) ||
                !string.Equals(linked.heroId, command.HeroId, StringComparison.Ordinal) ||
                !string.Equals(
                    linked.enemyGroupId,
                    command.EnemyGroupId,
                    StringComparison.Ordinal) ||
                !string.Equals(linked.combatMode, command.CombatMode, StringComparison.Ordinal) ||
                !linked.suppressFatigueCost ||
                !string.Equals(
                    _state.GetHeroOccupationOwnerId(command.HeroId),
                    command.OccupationOwnerId,
                    StringComparison.Ordinal))
            {
                return FailPreflight(
                    CombatStartCode.InvalidSourceContract,
                    "Linked source execution, request, hero and occupation owner do not form one valid handoff.",
                    out failure);
            }

            if (!string.IsNullOrWhiteSpace(linked.combatExecutionId))
            {
                return FailPreflight(
                    CombatStartCode.SourceAlreadyStarted,
                    "Linked combat source is already bound to a combat execution.",
                    out failure);
            }

            return true;
        }

        private bool TryBuildAggregate(
            CombatStartCommand command,
            string fingerprint,
            CombatStartActivityDescriptor activity,
            CombatLoadoutKind loadoutKind,
            ItemStackSaveData stack,
            CombatConsumableDescriptor consumable,
            string executionId,
            string sessionId,
            out CombatRuntimeAggregate aggregate,
            out CombatStartResult failure)
        {
            aggregate = null;
            failure = null;
            if (!_state.TryCreateHeroCombatant(
                    command.HeroId,
                    sessionId,
                    out var hero,
                    out var heroError) ||
                hero == null)
            {
                return FailPreflight(
                    CombatStartCode.HeroSnapshotInvalid,
                    heroError ?? "Failed to create the hero combat snapshot.",
                    out failure);
            }

            var session = new CombatSessionSaveData
            {
                sessionId = sessionId,
                executionId = executionId,
                enemyGroupId = activity.EnemyGroupId,
                combatMode = activity.CombatMode,
                hero = hero,
                combatTimeSeconds = 0d,
                scheduler = new CombatSchedulerStateSaveData(),
                rng = CombatRngStateFactory.CreateSplitMix64(
                    _identity.CreateRngSeed()),
                loadoutKind = loadoutKind,
                broughtConsumable = loadoutKind == CombatLoadoutKind.Consumable
                    ? new CombatConsumableStateSaveData
                    {
                        sourceStackId = stack.stackId,
                        itemId = stack.itemId,
                        initialQuantity = command.RequestedQuantity,
                        remainingQuantity = command.RequestedQuantity,
                        nextCheckAtSeconds = consumable.CheckIntervalSeconds,
                        nextAllowedUseAtSeconds = 0d
                    }
                    : null
            };

            var queueBuilder = new CombatEnemyQueueBuilder(_enemyQueue, _rngFactory);
            if (!queueBuilder.TryBuild(session, out var builtSession, out var queueError))
            {
                return FailPreflight(
                    CombatStartCode.EnemyQueueInvalid,
                    queueError?.Message ?? "Failed to build the combat enemy queue.",
                    out failure);
            }

            var execution = new CombatExecutionSaveData
            {
                executionId = executionId,
                sessionId = sessionId,
                sourceActivityId = command.SourceActivityId,
                sourceExecutionId = command.Kind == CombatStartKind.Direct
                    ? executionId
                    : command.SourceExecutionId,
                sourceRequestId = command.SourceRequestId,
                occupationOwnerId = command.Kind == CombatStartKind.Direct
                    ? executionId
                    : command.OccupationOwnerId,
                heroId = command.HeroId,
                startOperationId = command.OperationId,
                startFingerprint = fingerprint,
                status = CombatExecutionStatus.Running,
                startedAtUnixSeconds = _identity.GetUtcNowUnixSeconds()
            };

            if (!CombatRuntimeSaveDataUtility.TryNormalize(
                    execution,
                    builtSession,
                    out aggregate,
                    out _,
                    out var validationError))
            {
                return FailPreflight(
                    CombatStartCode.TransactionFailure,
                    validationError ?? "Created combat aggregate is invalid.",
                    out failure);
            }

            return true;
        }

        private bool TryCreateIdentifiers(
            string operationId,
            out string executionId,
            out string sessionId,
            out CombatStartResult failure)
        {
            executionId = _identity.CreateExecutionId();
            sessionId = _identity.CreateSessionId();
            failure = null;
            if (!string.IsNullOrWhiteSpace(executionId) &&
                !string.IsNullOrWhiteSpace(sessionId) &&
                !string.Equals(executionId, sessionId, StringComparison.Ordinal) &&
                !string.Equals(executionId, operationId, StringComparison.Ordinal) &&
                !string.Equals(sessionId, operationId, StringComparison.Ordinal))
            {
                return true;
            }

            return FailPreflight(
                CombatStartCode.TransactionFailure,
                "Combat execution, session and operation identifiers must be distinct and non-empty.",
                out failure);
        }

        private bool HasUnfinishedActivity(string activityId)
        {
            if (_state.HasUnfinishedActivityExecution(activityId))
                return true;
            foreach (var aggregate in _state.GetCombatAggregates() ??
                                      Array.Empty<CombatRuntimeAggregate>())
            {
                if (aggregate?.execution != null &&
                    CombatRuntimeSaveDataUtility.IsUnfinished(aggregate.execution) &&
                    string.Equals(
                        aggregate.execution.sourceActivityId,
                        activityId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FirstEligibilityMessage(ActivityCheckResult result)
        {
            foreach (var issue in result?.issues ??
                                  Array.Empty<ActivityRequirementIssue>())
            {
                if (!string.IsNullOrWhiteSpace(issue?.message))
                    return issue.message;
            }

            return "Combat activity requirements are not satisfied.";
        }

        private CombatRuntimeAggregate FindOperationAggregate(
            string operationId,
            out int count)
        {
            count = 0;
            CombatRuntimeAggregate match = null;
            foreach (var aggregate in _state.GetCombatAggregates() ??
                                      Array.Empty<CombatRuntimeAggregate>())
            {
                if (aggregate?.execution == null ||
                    !string.Equals(
                        aggregate.execution.startOperationId,
                        operationId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                count++;
                match = aggregate;
            }

            return match;
        }

        private CombatRuntimeAggregate FindSourceAggregate(string sourceRequestId)
        {
            foreach (var aggregate in _state.GetCombatAggregates() ??
                                      Array.Empty<CombatRuntimeAggregate>())
            {
                if (aggregate?.execution != null &&
                    CombatRuntimeSaveDataUtility.IsUnfinished(aggregate.execution) &&
                    string.Equals(
                        aggregate.execution.sourceRequestId,
                        sourceRequestId,
                        StringComparison.Ordinal))
                {
                    return aggregate;
                }
            }

            return null;
        }

        private static bool AggregateMatchesCommand(
            CombatRuntimeAggregate aggregate,
            CombatStartCommand command)
        {
            var execution = aggregate?.execution;
            var session = aggregate?.session;
            if (execution == null ||
                session == null ||
                !string.Equals(
                    execution.sourceActivityId,
                    command.SourceActivityId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    execution.sourceRequestId,
                    command.SourceRequestId,
                    StringComparison.Ordinal) ||
                !string.Equals(execution.heroId, command.HeroId, StringComparison.Ordinal) ||
                !string.Equals(
                    session.enemyGroupId,
                    command.EnemyGroupId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    session.combatMode,
                    command.CombatMode,
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (command.Kind == CombatStartKind.Direct)
            {
                if (!string.Equals(
                        execution.sourceExecutionId,
                        execution.executionId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        execution.occupationOwnerId,
                        execution.executionId,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }
            else if (!string.Equals(
                         execution.sourceExecutionId,
                         command.SourceExecutionId,
                         StringComparison.Ordinal) ||
                     !string.Equals(
                         execution.occupationOwnerId,
                         command.OccupationOwnerId,
                         StringComparison.Ordinal))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(command.StackId) &&
                command.RequestedQuantity == 0)
            {
                return session.loadoutKind == CombatLoadoutKind.Empty &&
                       session.broughtConsumable == null;
            }

            return session.loadoutKind == CombatLoadoutKind.Consumable &&
                   session.broughtConsumable != null &&
                   string.Equals(
                       session.broughtConsumable.sourceStackId,
                       command.StackId,
                       StringComparison.Ordinal) &&
                   session.broughtConsumable.initialQuantity ==
                   command.RequestedQuantity;
        }

        private static string BuildFingerprint(CombatStartCommand command)
        {
            return
                $"kind:{(int)(command?.Kind ?? 0)}" +
                $"|activity:{Part(command?.SourceActivityId)}" +
                $"|source:{Part(command?.SourceExecutionId)}" +
                $"|request:{Part(command?.SourceRequestId)}" +
                $"|owner:{Part(command?.OccupationOwnerId)}" +
                $"|hero:{Part(command?.HeroId)}" +
                $"|group:{Part(command?.EnemyGroupId)}" +
                $"|mode:{Part(command?.CombatMode)}" +
                $"|stack:{Part(command?.StackId)}" +
                $"|quantity:{command?.RequestedQuantity ?? 0}" +
                $"|storage:{command?.ExpectedStorageRevision ?? 0}";
        }

        private static string Part(string value)
        {
            value ??= string.Empty;
            return $"{value.Length}:{value}";
        }

        private static CombatStartReceiptPayload ParseReceiptPayload(
            OperationReceiptSaveData receipt)
        {
            try
            {
                var payload = string.IsNullOrWhiteSpace(receipt?.resultPayload)
                    ? null
                    : JsonUtility.FromJson<CombatStartReceiptPayload>(
                        receipt.resultPayload);
                return payload != null &&
                       !string.IsNullOrWhiteSpace(payload.executionId) &&
                       !string.IsNullOrWhiteSpace(payload.sessionId)
                    ? payload
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool FailPreflight(
            CombatStartCode code,
            string message,
            out CombatStartResult failure)
        {
            failure = Failed(code, message);
            return false;
        }

        private static CombatStartResult Failed(
            CombatStartCode code,
            string message)
        {
            return new CombatStartResult
            {
                Success = false,
                Code = code,
                Message = message ?? string.Empty
            };
        }

        private static CombatStartResult Succeeded(
            CombatRuntimeAggregate aggregate,
            bool replayed)
        {
            return new CombatStartResult
            {
                Success = true,
                Replayed = replayed,
                Code = replayed
                    ? CombatStartCode.Replayed
                    : CombatStartCode.Applied,
                ExecutionId = aggregate.execution.executionId,
                SessionId = aggregate.session.sessionId,
                Aggregate = CombatRuntimeSaveDataUtility.CloneAggregate(aggregate)
            };
        }

        private static CombatStartedEvent ToStartedEvent(
            CombatRuntimeAggregate aggregate)
        {
            return new CombatStartedEvent
            {
                ExecutionId = aggregate.execution.executionId,
                SessionId = aggregate.session.sessionId,
                SourceActivityId = aggregate.execution.sourceActivityId,
                SourceExecutionId = aggregate.execution.sourceExecutionId,
                SourceRequestId = aggregate.execution.sourceRequestId,
                OccupationOwnerId = aggregate.execution.occupationOwnerId,
                HeroId = aggregate.execution.heroId,
                LoadoutKind = aggregate.session.loadoutKind
            };
        }

        private sealed class Preflight
        {
            public string Fingerprint;
            public CombatStartActivityDescriptor Activity;
            public CombatLoadoutKind LoadoutKind;
            public StorageActionContext StorageContext;
            public ItemStackSaveData Stack;
            public CombatRuntimeAggregate Aggregate;
            public CombatStartResult Replay;
        }

        [Serializable]
        private sealed class CombatStartReceiptPayload
        {
            public string executionId;
            public string sessionId;
        }
    }
}
