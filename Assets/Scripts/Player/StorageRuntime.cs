using System;
using System.Collections.Generic;
using GuildIdle.Configs;

namespace GuildIdle.Player
{
    public static class ItemAvailabilityMode
    {
        public const string Available = "available";
        public const string Reserved = "reserved";
        public const string InAction = "in_action";
        public const string Equipped = "equipped";
        public const string Unavailable = "unavailable";
    }

    public static class StorageOwnerType
    {
        public const string Hero = "Hero";
    }

    public static class StorageContextType
    {
        public const string ActivityExecution = "ActivityExecution";
        public const string CombatExecution = "CombatExecution";
        public const string CraftExecution = "CraftExecution";
    }

    public sealed class StorageActionContext
    {
        public StorageActionContext(string contextType, string contextId)
        {
            ContextType = string.IsNullOrWhiteSpace(contextType) ? null : contextType;
            ContextId = string.IsNullOrWhiteSpace(contextId) ? null : contextId;
            if (ContextType == null || ContextId == null)
                throw new ArgumentException("contextType and contextId are required together.");
        }

        public string ContextType { get; }
        public string ContextId { get; }

        internal bool Matches(string contextType, string contextId) =>
            string.Equals(ContextType, contextType, StringComparison.Ordinal) &&
            string.Equals(ContextId, contextId, StringComparison.Ordinal);
    }

    public sealed class StorageSnapshot
    {
        public long Revision { get; internal set; }
        public int Capacity { get; internal set; }
        public int OccupiedSlots { get; internal set; }
        public int FreeSlots => Math.Max(0, Capacity - OccupiedSlots);
        public ItemStackSaveData[] Stacks { get; internal set; } = Array.Empty<ItemStackSaveData>();
        public ItemInstanceSaveData[] Instances { get; internal set; } = Array.Empty<ItemInstanceSaveData>();
    }

    public sealed class StorageMutationResult
    {
        public bool Success { get; internal set; }
        public bool Replayed { get; internal set; }
        public string Code { get; internal set; }
        public string Message { get; internal set; }
        public string StackId { get; internal set; }
        public string InstanceId { get; internal set; }
        public int Quantity { get; internal set; }
        public long StorageRevision { get; internal set; }
        public StorageSnapshot Snapshot { get; internal set; }
    }

    public sealed class StorageAddPreview
    {
        public int RequestedQuantity { get; internal set; }
        public int AcceptedQuantity { get; internal set; }
        public int RequiredNewSlots { get; internal set; }
        public bool FitsAll => AcceptedQuantity == RequestedQuantity;
    }

    public sealed class StorageAddRequest
    {
        public string ItemId { get; set; }
        public int Quantity { get; set; }
        public int Quality { get; set; }
    }

    public sealed class StorageBatchPreview
    {
        public StorageAddPreview[] Entries { get; internal set; } = Array.Empty<StorageAddPreview>();
        public int RequiredNewSlots { get; internal set; }
        public bool FitsAll { get; internal set; }
    }

    public interface IStorageService
    {
        event Action<StorageSnapshot> Changed;
        StorageSnapshot GetSnapshot();
        int GetOwnedInStorageCount(string itemId);
        int GetAvailableForActionCount(string itemId, StorageActionContext actionContext);
        StorageAddPreview PreviewAdd(string itemId, int quantity);
        StorageBatchPreview PreviewBatch(params StorageAddRequest[] requests);
        StorageMutationResult Add(string operationId, long expectedStorageRevision, string itemId, int quantity, int quality = 0);
        StorageMutationResult Remove(string operationId, long expectedStorageRevision, string itemId, int quantity, StorageActionContext actionContext = null);
        StorageMutationResult Consume(string operationId, long expectedStorageRevision, string itemId, int quantity, StorageActionContext actionContext = null);
        StorageMutationResult Reserve(string operationId, long expectedStorageRevision, string stackId, int quantity, StorageActionContext actionContext);
        StorageMutationResult Release(string operationId, long expectedStorageRevision, string stackId, int quantity, StorageActionContext actionContext);
        StorageMutationResult TransferToAction(string operationId, long expectedStorageRevision, string stackId, int quantity, StorageActionContext actionContext);
        StorageMutationResult ConsumeTransferredStack(string operationId, long expectedStorageRevision, string stackId, int quantity, StorageActionContext actionContext);
        StorageMutationResult ReserveInstance(string operationId, long expectedStorageRevision, string instanceId, StorageActionContext actionContext);
        StorageMutationResult ReleaseInstance(string operationId, long expectedStorageRevision, string instanceId, StorageActionContext actionContext);
        StorageMutationResult TransferInstanceToAction(string operationId, long expectedStorageRevision, string instanceId, StorageActionContext actionContext);
        StorageMutationResult Equip(string operationId, long expectedStorageRevision, string heroId, string equipmentSlot, string instanceId);
        StorageMutationResult Unequip(string operationId, long expectedStorageRevision, string heroId, string equipmentSlot);
    }

    public sealed class StorageService : IStorageService
    {
        private const string AggregateId = "storage";
        private readonly PlayerState _state;
        private readonly IPlayerBootstrapConfigProvider _configs;

        internal StorageService(PlayerState state, IPlayerBootstrapConfigProvider configs)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public event Action<StorageSnapshot> Changed;

        public StorageSnapshot GetSnapshot()
        {
            var capacity = GetCapacity();
            var occupied = GetOccupiedSlots();
            return new StorageSnapshot
            {
                Revision = _state.StorageRevision,
                Capacity = capacity,
                OccupiedSlots = occupied,
                Stacks = GetStorageStacks(),
                Instances = GetStorageInstances()
            };
        }

        public int GetOwnedInStorageCount(string itemId)
        {
            if (!_configs.TryGetItem(itemId, out _))
                return 0;

            long total = 0;
            foreach (var stack in _state.MutableItemStacks.Values)
            {
                if (stack != null && string.Equals(stack.itemId, itemId, StringComparison.Ordinal) &&
                    TryGetState(stack.stateId, out var state) && state.isInStorage)
                    total += stack.quantity;
            }
            foreach (var instance in _state.MutableItemInstances.Values)
            {
                if (instance != null && string.Equals(instance.itemId, itemId, StringComparison.Ordinal) &&
                    TryGetState(instance.stateId, out var state) && state.isInStorage)
                    total++;
            }
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        public int GetAvailableForActionCount(string itemId, StorageActionContext actionContext)
        {
            if (!_configs.TryGetItem(itemId, out _))
                return 0;

            long total = 0;
            foreach (var stack in _state.MutableItemStacks.Values)
            {
                if (stack != null && string.Equals(stack.itemId, itemId, StringComparison.Ordinal) && IsAvailable(stack.stateId, stack.contextType, stack.contextId, actionContext))
                    total += stack.quantity;
            }
            foreach (var instance in _state.MutableItemInstances.Values)
            {
                if (instance != null && string.Equals(instance.itemId, itemId, StringComparison.Ordinal) && IsAvailable(instance.stateId, instance.contextType, instance.contextId, actionContext))
                    total++;
            }
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        public StorageAddPreview PreviewAdd(string itemId, int quantity)
        {
            var preview = new StorageAddPreview { RequestedQuantity = Math.Max(0, quantity) };
            if (quantity <= 0 || !TryGetRule(itemId, out var rule) || !TryGetStateByMode(ItemAvailabilityMode.Available, out var availableState))
                return preview;

            if (string.Equals(rule.mode, "single", StringComparison.Ordinal))
            {
                var free = rule.occupiesSlot && availableState.occupiesCapacity ? Math.Max(0, GetCapacity() - GetOccupiedSlots()) : quantity;
                preview.AcceptedQuantity = Math.Min(quantity, free);
                preview.RequiredNewSlots = rule.occupiesSlot && availableState.occupiesCapacity ? preview.AcceptedQuantity : 0;
                return preview;
            }

            var accepted = 0;
            foreach (var stack in OrderedStacks(itemId))
            {
                if (!IsAvailableState(stack.stateId))
                    continue;
                accepted += Math.Max(0, rule.maxStack - stack.quantity);
                if (accepted >= quantity)
                {
                    preview.AcceptedQuantity = quantity;
                    return preview;
                }
            }

            var remaining = quantity - accepted;
            var requiredSlots = (remaining + rule.maxStack - 1) / rule.maxStack;
            var freeSlots = rule.occupiesSlot && availableState.occupiesCapacity ? Math.Max(0, GetCapacity() - GetOccupiedSlots()) : requiredSlots;
            var creatableSlots = Math.Min(requiredSlots, freeSlots);
            preview.RequiredNewSlots = creatableSlots;
            preview.AcceptedQuantity = accepted + Math.Min(remaining, creatableSlots * rule.maxStack);
            return preview;
        }

        public StorageBatchPreview PreviewBatch(params StorageAddRequest[] requests)
        {
            requests ??= Array.Empty<StorageAddRequest>();
            var before = _state.ToSaveData();
            var occupiedBefore = GetOccupiedSlots();
            var entries = new StorageAddPreview[requests.Length];
            var fitsAll = true;
            try
            {
                for (var index = 0; index < requests.Length; index++)
                {
                    var request = requests[index];
                    var requested = Math.Max(0, request?.Quantity ?? 0);
                    var entry = new StorageAddPreview { RequestedQuantity = requested };
                    entries[index] = entry;
                    if (request == null || requested == 0 ||
                        !TryAddResultItem(request.ItemId, requested, request.Quality, true, out var accepted, out _, out _, out _))
                    {
                        fitsAll = false;
                        continue;
                    }
                    entry.AcceptedQuantity = accepted;
                    entry.RequiredNewSlots = Math.Max(0, GetOccupiedSlots() - occupiedBefore);
                    occupiedBefore = GetOccupiedSlots();
                    fitsAll &= accepted == requested;
                }

                return new StorageBatchPreview
                {
                    Entries = entries,
                    RequiredNewSlots = Math.Max(0, GetOccupiedSlots() - GetOccupiedSlots(before)),
                    FitsAll = fitsAll
                };
            }
            finally
            {
                _state.RestoreTransactional(before);
            }
        }

        public StorageMutationResult Add(string operationId, long expectedStorageRevision, string itemId, int quantity, int quality = 0)
        {
            return Mutate(operationId, expectedStorageRevision, $"add|{itemId}|{quantity}|{quality}", () =>
            {
                if (!TryAddResultItem(itemId, quantity, quality, false, out var accepted, out var stackId, out var instanceId, out var error))
                    return Failure(error);
                return Success(accepted, stackId, instanceId);
            });
        }

        public StorageMutationResult Remove(string operationId, long expectedStorageRevision, string itemId, int quantity, StorageActionContext actionContext = null) =>
            Consume(operationId, expectedStorageRevision, itemId, quantity, actionContext);

        public StorageMutationResult Consume(string operationId, long expectedStorageRevision, string itemId, int quantity, StorageActionContext actionContext = null)
        {
            return Mutate(operationId, expectedStorageRevision, $"consume|{itemId}|{quantity}|{ContextFingerprint(actionContext)}", () =>
            {
                if (quantity <= 0 || GetAvailableForActionCount(itemId, actionContext) < quantity)
                    return Failure("Insufficient available quantity.");

                var remaining = quantity;
                foreach (var stack in OrderedConsumableStacks(itemId, actionContext))
                {
                    var removed = Math.Min(stack.quantity, remaining);
                    stack.quantity -= removed;
                    remaining -= removed;
                    if (stack.quantity == 0)
                        _state.MutableItemStacks.Remove(stack.stackId);
                    if (remaining == 0)
                        break;
                }
                return remaining == 0 ? Success(quantity) : Failure("Only stack items can be consumed by quantity.");
            });
        }

        public StorageMutationResult Reserve(string operationId, long expectedStorageRevision, string stackId, int quantity, StorageActionContext actionContext)
        {
            return MoveStack(operationId, expectedStorageRevision, "reserve", stackId, quantity, actionContext, ItemAvailabilityMode.Reserved);
        }

        public StorageMutationResult Release(string operationId, long expectedStorageRevision, string stackId, int quantity, StorageActionContext actionContext)
        {
            return ReleaseStack(operationId, expectedStorageRevision, stackId, quantity, actionContext);
        }

        public StorageMutationResult TransferToAction(string operationId, long expectedStorageRevision, string stackId, int quantity, StorageActionContext actionContext)
        {
            return MoveStack(operationId, expectedStorageRevision, "transfer", stackId, quantity, actionContext, ItemAvailabilityMode.InAction);
        }

        public StorageMutationResult ConsumeTransferredStack(string operationId, long expectedStorageRevision, string stackId, int quantity, StorageActionContext actionContext)
        {
            return Mutate(operationId, expectedStorageRevision, $"consume_transferred|{stackId}|{quantity}|{ContextFingerprint(actionContext)}", () =>
            {
                if (actionContext == null || quantity <= 0 || !_state.MutableItemStacks.TryGetValue(stackId, out var stack) || quantity > stack.quantity ||
                    !TryGetState(stack.stateId, out var state) || !string.Equals(state.availabilityMode, ItemAvailabilityMode.InAction, StringComparison.Ordinal) ||
                    !actionContext.Matches(stack.contextType, stack.contextId))
                    return Failure("Transferred stack is not owned by this action context or has insufficient quantity.");
                stack.quantity -= quantity;
                if (stack.quantity == 0)
                    _state.MutableItemStacks.Remove(stack.stackId);
                return Success(quantity, stack.stackId);
            });
        }

        public StorageMutationResult ReserveInstance(string operationId, long expectedStorageRevision, string instanceId, StorageActionContext actionContext) =>
            MoveInstance(operationId, expectedStorageRevision, "reserve_instance", instanceId, actionContext, ItemAvailabilityMode.Reserved);

        public StorageMutationResult ReleaseInstance(string operationId, long expectedStorageRevision, string instanceId, StorageActionContext actionContext) =>
            MoveInstance(operationId, expectedStorageRevision, "release_instance", instanceId, actionContext, ItemAvailabilityMode.Available);

        public StorageMutationResult TransferInstanceToAction(string operationId, long expectedStorageRevision, string instanceId, StorageActionContext actionContext) =>
            MoveInstance(operationId, expectedStorageRevision, "transfer_instance", instanceId, actionContext, ItemAvailabilityMode.InAction);

        public StorageMutationResult Equip(string operationId, long expectedStorageRevision, string heroId, string equipmentSlot, string instanceId)
        {
            return Mutate(operationId, expectedStorageRevision, $"equip|{heroId}|{equipmentSlot}|{instanceId}", () =>
            {
                if (!_state.HasHero(heroId) || !_state.MutableItemInstances.TryGetValue(instanceId, out var instance) ||
                    !_configs.TryGetEquipmentSlot(instance.itemId, out var configuredSlot) || !string.Equals(configuredSlot, equipmentSlot, StringComparison.Ordinal) ||
                    !IsAvailableState(instance.stateId) || !TryGetStateByMode(ItemAvailabilityMode.Equipped, out var equippedState))
                    return Failure("Equipment instance is not available for this slot.");

                var key = PlayerState.EquipmentSlotKeyForStorage(heroId, equipmentSlot);
                if (_state.MutableEquipmentSlots.ContainsKey(key))
                    return Failure("Equipment slot is already occupied.");

                instance.stateId = equippedState.stateId;
                instance.ownerType = StorageOwnerType.Hero;
                instance.ownerId = heroId;
                instance.contextType = null;
                instance.contextId = null;
                _state.MutableEquipmentSlots[key] = new EquipmentSlotSaveData { heroId = heroId, equipmentSlot = equipmentSlot, itemInstanceId = instanceId };
                return Success(1, instanceId: instanceId);
            });
        }

        public StorageMutationResult Unequip(string operationId, long expectedStorageRevision, string heroId, string equipmentSlot)
        {
            return Mutate(operationId, expectedStorageRevision, $"unequip|{heroId}|{equipmentSlot}", () =>
            {
                var key = PlayerState.EquipmentSlotKeyForStorage(heroId, equipmentSlot);
                if (!_state.MutableEquipmentSlots.TryGetValue(key, out var slot) || !_state.MutableItemInstances.TryGetValue(slot.itemInstanceId, out var instance) ||
                    !TryGetStateByMode(ItemAvailabilityMode.Available, out var availableState))
                    return Failure("No equipment is assigned to the slot.");
                if (availableState.occupiesCapacity && TryGetRule(instance.itemId, out var rule) && rule.occupiesSlot && GetOccupiedSlots() >= GetCapacity())
                    return Failure("Storage capacity is full.");

                _state.MutableEquipmentSlots.Remove(key);
                instance.stateId = availableState.stateId;
                instance.ownerType = null;
                instance.ownerId = null;
                instance.contextType = null;
                instance.contextId = null;
                return Success(1, instanceId: instance.instanceId);
            });
        }

        internal bool TryAddResultItem(string itemId, int requested, int quality, bool allowPartial, out int accepted, out string stackId, out string instanceId, out string error)
        {
            return TryAddResultItem(itemId, requested, quality, allowPartial, null, out accepted, out stackId, out instanceId, out error);
        }

        internal bool TryAddResultItem(string itemId, int requested, int quality, bool allowPartial, string preferredInstanceId, out int accepted, out string stackId, out string instanceId, out string error)
        {
            accepted = 0;
            stackId = null;
            instanceId = null;
            error = null;
            if (requested <= 0 || !TryGetRule(itemId, out var rule) || !TryGetStateByMode(ItemAvailabilityMode.Available, out var availableState))
            {
                error = "Unknown item storage rule or invalid quantity.";
                return false;
            }

            var preview = PreviewAdd(itemId, requested);
            if (!allowPartial && !preview.FitsAll)
            {
                error = "Storage capacity is insufficient.";
                return false;
            }
            accepted = allowPartial ? preview.AcceptedQuantity : requested;
            if (accepted <= 0)
                return allowPartial;

            if (string.Equals(rule.mode, "single", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(preferredInstanceId) && (accepted != 1 || _state.MutableItemInstances.ContainsKey(preferredInstanceId)))
                {
                    error = "Equipment instance id is invalid or already exists.";
                    return false;
                }
                for (var index = 0; index < accepted; index++)
                {
                    instanceId = string.IsNullOrWhiteSpace(preferredInstanceId) ? NewInstanceId() : preferredInstanceId;
                    _state.MutableItemInstances.Add(instanceId, new ItemInstanceSaveData
                    {
                        instanceId = instanceId,
                        itemId = itemId,
                        quality = quality,
                        stateId = availableState.stateId
                    });
                }
                return true;
            }

            var remaining = accepted;
            foreach (var stack in OrderedStacks(itemId))
            {
                if (!IsAvailableState(stack.stateId))
                    continue;
                var added = Math.Min(remaining, Math.Max(0, rule.maxStack - stack.quantity));
                stack.quantity += added;
                remaining -= added;
                stackId = stack.stackId;
                if (remaining == 0)
                    break;
            }
            while (remaining > 0)
            {
                var amount = Math.Min(remaining, rule.maxStack);
                stackId = NewStackId();
                _state.MutableItemStacks.Add(stackId, new ItemStackSaveData
                {
                    stackId = stackId,
                    itemId = itemId,
                    quantity = amount,
                    stateId = availableState.stateId
                });
                remaining -= amount;
            }
            return true;
        }

        internal void CommitExternalMutation()
        {
            _state.StorageRevision++;
        }

        internal void NotifyExternalMutation()
        {
            Changed?.Invoke(GetSnapshot());
        }

        private StorageMutationResult MoveStack(string operationId, long expectedStorageRevision, string action, string stackId, int quantity, StorageActionContext context, string targetMode)
        {
            return Mutate(operationId, expectedStorageRevision, $"{action}|{stackId}|{quantity}|{ContextFingerprint(context)}", () =>
            {
                if (context == null || quantity <= 0 || !_state.MutableItemStacks.TryGetValue(stackId, out var source) || quantity > source.quantity ||
                    !TryGetState(source.stateId, out var sourceState) || !TryGetStateByMode(targetMode, out var targetState))
                    return Failure("Stack transfer parameters are invalid.");

                if (string.Equals(action, "release", StringComparison.Ordinal))
                {
                    if (!string.Equals(sourceState.availabilityMode, ItemAvailabilityMode.Reserved, StringComparison.Ordinal) || !context.Matches(source.contextType, source.contextId))
                        return Failure("Only a reservation owned by this context can be released.");
                }
                else if (!IsAvailable(source.stateId, source.contextType, source.contextId, context))
                {
                    return Failure("Stack is not available to this context.");
                }

                if (quantity < source.quantity && targetState.occupiesCapacity && TryGetRule(source.itemId, out var targetRule) &&
                    targetRule.occupiesSlot && GetOccupiedSlots() >= GetCapacity())
                {
                    return Failure("Storage capacity is insufficient for the split stack.");
                }

                var moved = source;
                if (quantity < source.quantity)
                {
                    source.quantity -= quantity;
                    var newId = NewStackId();
                    moved = CloneStack(source);
                    moved.stackId = newId;
                    moved.quantity = quantity;
                    _state.MutableItemStacks.Add(newId, moved);
                }

                moved.stateId = targetState.stateId;
                moved.ownerType = null;
                moved.ownerId = null;
                if (string.Equals(targetMode, ItemAvailabilityMode.Available, StringComparison.Ordinal))
                {
                    moved.contextType = null;
                    moved.contextId = null;
                }
                else
                {
                    moved.contextType = context.ContextType;
                    moved.contextId = context.ContextId;
                }
                return Success(quantity, moved.stackId);
            });
        }

        private StorageMutationResult ReleaseStack(string operationId, long expectedStorageRevision, string stackId, int quantity, StorageActionContext context)
        {
            return Mutate(operationId, expectedStorageRevision, $"release|{stackId}|{quantity}|{ContextFingerprint(context)}", () =>
            {
                if (context == null || quantity <= 0 || !_state.MutableItemStacks.TryGetValue(stackId, out var source) || quantity > source.quantity ||
                    !TryGetState(source.stateId, out var sourceState) ||
                    !string.Equals(sourceState.availabilityMode, ItemAvailabilityMode.Reserved, StringComparison.Ordinal) ||
                    !context.Matches(source.contextType, source.contextId) ||
                    !TryGetStateByMode(ItemAvailabilityMode.Available, out var availableState) ||
                    !TryGetRule(source.itemId, out var rule))
                {
                    return Failure("Only a reservation owned by this context can be released.");
                }

                if (quantity == source.quantity)
                {
                    source.stateId = availableState.stateId;
                    source.ownerType = null;
                    source.ownerId = null;
                    source.contextType = null;
                    source.contextId = null;
                    return Success(quantity, source.stackId);
                }

                var remaining = quantity;
                string targetStackId = null;
                foreach (var target in OrderedStacks(source.itemId))
                {
                    if (!IsAvailableState(target.stateId) || string.Equals(target.stackId, source.stackId, StringComparison.Ordinal))
                        continue;
                    var moved = Math.Min(remaining, Math.Max(0, rule.maxStack - target.quantity));
                    if (moved <= 0)
                        continue;
                    target.quantity += moved;
                    remaining -= moved;
                    targetStackId = target.stackId;
                    if (remaining == 0)
                        break;
                }

                if (remaining > 0)
                {
                    if (availableState.occupiesCapacity && rule.occupiesSlot && GetOccupiedSlots() >= GetCapacity())
                        return Failure("Storage capacity is insufficient for the released split stack.");
                    targetStackId = NewStackId();
                    _state.MutableItemStacks.Add(targetStackId, new ItemStackSaveData
                    {
                        stackId = targetStackId,
                        itemId = source.itemId,
                        quantity = remaining,
                        stateId = availableState.stateId
                    });
                }

                source.quantity -= quantity;
                return Success(quantity, targetStackId);
            });
        }

        private StorageMutationResult MoveInstance(string operationId, long expectedStorageRevision, string action, string instanceId, StorageActionContext context, string targetMode)
        {
            return Mutate(operationId, expectedStorageRevision, $"{action}|{instanceId}|{ContextFingerprint(context)}", () =>
            {
                if (context == null || !_state.MutableItemInstances.TryGetValue(instanceId, out var instance) ||
                    !TryGetState(instance.stateId, out var sourceState) || !TryGetStateByMode(targetMode, out var targetState))
                    return Failure("Instance transfer parameters are invalid.");
                if (string.Equals(action, "release_instance", StringComparison.Ordinal))
                {
                    if (!string.Equals(sourceState.availabilityMode, ItemAvailabilityMode.Reserved, StringComparison.Ordinal) || !context.Matches(instance.contextType, instance.contextId))
                        return Failure("Only a reservation owned by this context can be released.");
                }
                else if (!IsAvailable(instance.stateId, instance.contextType, instance.contextId, context))
                {
                    return Failure("Instance is not available to this context.");
                }
                instance.stateId = targetState.stateId;
                instance.ownerType = null;
                instance.ownerId = null;
                if (string.Equals(targetMode, ItemAvailabilityMode.Available, StringComparison.Ordinal))
                {
                    instance.contextType = null;
                    instance.contextId = null;
                }
                else
                {
                    instance.contextType = context.ContextType;
                    instance.contextId = context.ContextId;
                }
                return Success(1, instanceId: instanceId);
            });
        }

        private StorageMutationResult Mutate(string operationId, long expectedRevision, string fingerprint, Func<StorageMutationResult> mutation)
        {
            if (string.IsNullOrWhiteSpace(operationId))
                return ImmediateFailure("OperationIdRequired", "operationId is required.");
            fingerprint = $"{fingerprint}|expected:{expectedRevision}";

            if (_state.TryGetOperationReceipt(AggregateId, operationId, out var receipt))
            {
                if (!string.Equals(receipt.fingerprint, fingerprint, StringComparison.Ordinal))
                    return ImmediateFailure("OperationConflict", "operationId was already used with another payload.");
                return new StorageMutationResult
                {
                    Success = receipt.success,
                    Replayed = true,
                    Code = receipt.code,
                    StackId = receipt.stackId,
                    InstanceId = receipt.instanceId,
                    Quantity = receipt.quantity,
                    StorageRevision = receipt.storageRevision,
                    Snapshot = GetSnapshot()
                };
            }
            if (expectedRevision != _state.StorageRevision)
                return ImmediateFailure("StaleStorageRevision", $"Expected storage revision {expectedRevision}, current revision is {_state.StorageRevision}.");

            var before = _state.ToSaveData();
            var result = mutation();
            if (!result.Success)
            {
                _state.RestoreTransactional(before);
                result.StorageRevision = _state.StorageRevision;
                result.Snapshot = GetSnapshot();
                return result;
            }

            _state.StorageRevision++;
            _state.RecordOperationReceipt(new OperationReceiptSaveData
            {
                aggregateId = AggregateId,
                operationId = operationId,
                fingerprint = fingerprint,
                success = true,
                code = "Applied",
                storageRevision = _state.StorageRevision,
                stackId = result.StackId,
                instanceId = result.InstanceId,
                quantity = result.Quantity
            });
            result.Code = "Applied";
            result.StorageRevision = _state.StorageRevision;
            result.Snapshot = GetSnapshot();
            Changed?.Invoke(result.Snapshot);
            return result;
        }

        private int GetCapacity()
        {
            var total = 0;
            foreach (var building in _configs.StorageBuildings ?? Array.Empty<StorageBuildingConfigDto>())
            {
                if (building != null && _state.TryGetBuildingLevelState(building.buildingId, out var level) && level == building.level)
                    total = AddClamped(total, building.slotCount);
            }
            return total;
        }

        private int GetOccupiedSlots()
        {
            var occupied = 0;
            foreach (var stack in _state.MutableItemStacks.Values)
                if (stack != null && TryGetState(stack.stateId, out var state) && state.occupiesCapacity && TryGetRule(stack.itemId, out var rule) && rule.occupiesSlot) occupied++;
            foreach (var instance in _state.MutableItemInstances.Values)
                if (instance != null && TryGetState(instance.stateId, out var state) && state.occupiesCapacity && TryGetRule(instance.itemId, out var rule) && rule.occupiesSlot) occupied++;
            return occupied;
        }

        private int GetOccupiedSlots(SaveData save)
        {
            var occupied = 0;
            foreach (var stack in save?.itemStacks ?? Array.Empty<ItemStackSaveData>())
                if (stack != null && TryGetState(stack.stateId, out var state) && state.occupiesCapacity && TryGetRule(stack.itemId, out var rule) && rule.occupiesSlot) occupied++;
            foreach (var instance in save?.itemInstances ?? Array.Empty<ItemInstanceSaveData>())
                if (instance != null && TryGetState(instance.stateId, out var state) && state.occupiesCapacity && TryGetRule(instance.itemId, out var rule) && rule.occupiesSlot) occupied++;
            return occupied;
        }

        private ItemStackSaveData[] GetStorageStacks()
        {
            var values = new List<ItemStackSaveData>();
            foreach (var stack in _state.GetItemStacks())
                if (stack != null && TryGetState(stack.stateId, out var state) && state.isInStorage) values.Add(stack);
            return values.ToArray();
        }

        private ItemInstanceSaveData[] GetStorageInstances()
        {
            var values = new List<ItemInstanceSaveData>();
            foreach (var instance in _state.GetItemInstances())
                if (instance != null && TryGetState(instance.stateId, out var state) && state.isInStorage) values.Add(instance);
            return values.ToArray();
        }

        private bool TryGetRule(string itemId, out StorageRuleConfigDto rule)
        {
            rule = null;
            return _configs.TryGetItem(itemId, out var item) && item != null && _configs.TryGetStorageRuleForItemKind(item.Kind, out rule);
        }

        private bool TryGetState(string stateId, out ItemStateConfigDto state) => _configs.TryGetItemState(stateId, out state);
        private bool TryGetStateByMode(string mode, out ItemStateConfigDto state) => _configs.TryGetItemStateByAvailabilityMode(mode, out state);
        private bool IsAvailableState(string stateId) => TryGetState(stateId, out var state) && string.Equals(state.availabilityMode, ItemAvailabilityMode.Available, StringComparison.Ordinal);

        private bool IsAvailable(string stateId, string contextType, string contextId, StorageActionContext context)
        {
            if (!TryGetState(stateId, out var state) || !state.isInStorage)
                return false;
            if (string.Equals(state.availabilityMode, ItemAvailabilityMode.Available, StringComparison.Ordinal))
                return true;
            return string.Equals(state.availabilityMode, ItemAvailabilityMode.Reserved, StringComparison.Ordinal) && context != null && context.Matches(contextType, contextId);
        }

        private List<ItemStackSaveData> OrderedStacks(string itemId)
        {
            var result = new List<ItemStackSaveData>();
            foreach (var stack in _state.MutableItemStacks.Values)
                if (stack != null && string.Equals(stack.itemId, itemId, StringComparison.Ordinal)) result.Add(stack);
            result.Sort((left, right) => string.CompareOrdinal(left.stackId, right.stackId));
            return result;
        }

        private List<ItemStackSaveData> OrderedConsumableStacks(string itemId, StorageActionContext context)
        {
            var result = OrderedStacks(itemId);
            result.RemoveAll(stack => !IsAvailable(stack.stateId, stack.contextType, stack.contextId, context));
            result.Sort((left, right) =>
            {
                TryGetState(left.stateId, out var leftState);
                TryGetState(right.stateId, out var rightState);
                var leftReserved = string.Equals(leftState?.availabilityMode, ItemAvailabilityMode.Reserved, StringComparison.Ordinal) ? 0 : 1;
                var rightReserved = string.Equals(rightState?.availabilityMode, ItemAvailabilityMode.Reserved, StringComparison.Ordinal) ? 0 : 1;
                var order = leftReserved.CompareTo(rightReserved);
                return order != 0 ? order : string.CompareOrdinal(left.stackId, right.stackId);
            });
            return result;
        }

        private string NewStackId()
        {
            string value;
            do value = Guid.NewGuid().ToString("N"); while (_state.MutableItemStacks.ContainsKey(value));
            return value;
        }

        private string NewInstanceId()
        {
            string value;
            do value = Guid.NewGuid().ToString("N"); while (_state.MutableItemInstances.ContainsKey(value));
            return value;
        }

        private static ItemStackSaveData CloneStack(ItemStackSaveData value) => new ItemStackSaveData
        {
            stackId = value.stackId,
            itemId = value.itemId,
            quantity = value.quantity,
            stateId = value.stateId,
            ownerType = value.ownerType,
            ownerId = value.ownerId,
            contextType = value.contextType,
            contextId = value.contextId
        };

        private static string ContextFingerprint(StorageActionContext context) => context == null ? string.Empty : $"{context.ContextType}:{context.ContextId}";
        private static int AddClamped(int left, int right) => right > int.MaxValue - left ? int.MaxValue : left + right;
        private static StorageMutationResult Success(int quantity, string stackId = null, string instanceId = null) => new StorageMutationResult { Success = true, Quantity = quantity, StackId = stackId, InstanceId = instanceId };
        private static StorageMutationResult Failure(string message) => new StorageMutationResult { Success = false, Code = "Rejected", Message = message };
        private StorageMutationResult ImmediateFailure(string code, string message) => new StorageMutationResult { Success = false, Code = code, Message = message, StorageRevision = _state.StorageRevision, Snapshot = GetSnapshot() };
    }
}
