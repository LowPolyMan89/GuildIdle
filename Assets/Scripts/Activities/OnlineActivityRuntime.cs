using System;
using System.Collections.Generic;
using GuildIdle.Combat;
using GuildIdle.Configs;
using GuildIdle.Crafting;
using GuildIdle.Player;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Activities
{
    public sealed class ActivityPendingRewardEntrySnapshot
    {
        public string rewardType;
        public string targetId;
        public long quantity;
        public int quality;
    }

    public sealed class ActivityPendingRewardSnapshot
    {
        public string resultId;
        public string sourceId;
        public ActivityPendingRewardEntrySnapshot[] entries =
            Array.Empty<ActivityPendingRewardEntrySnapshot>();
    }

    public sealed class OnlineCombatActorSnapshot
    {
        public string definitionId;
        public int currentHp;
        public int maxHp;
        public int damageMin;
        public int damageMax;
        public double attacksPerSecond;
        public double critChancePercent;
        public double dodgeChancePercent;
        public double physicalResistancePercent;
        public double magicResistancePercent;
    }

    public sealed class OnlineCombatSnapshot
    {
        public string executionId;
        public string activityId;
        public string heroId;
        public string enemyGroupId;
        public string outcome;
        public string pendingResultId;
        public CombatExecutionStatus status;
        public double combatTimeSeconds;
        public int enemyIndex;
        public int enemyCount;
        public string consumableItemId;
        public int consumableInitialQuantity;
        public int consumableRemainingQuantity;
        public OnlineCombatActorSnapshot hero;
        public OnlineCombatActorSnapshot enemy;
    }

    public sealed class OnlineCombatStartResult
    {
        public bool success;
        public string code;
        public string message;
        public OnlineCombatSnapshot snapshot;
    }

    public sealed class OnlineCombatAdvanceResult
    {
        public bool success;
        public string code;
        public string message;
        public CombatEvent[] events = Array.Empty<CombatEvent>();
        public OnlineCombatSnapshot snapshot;
    }

    public sealed class OnlineCraftSnapshot
    {
        public string executionId;
        public string craftId;
        public string heroId;
        public string stationBuildingId;
        public int plannedCycles;
        public CraftExecutionStatus status;
        public float progressSeconds;
        public int durationSeconds;
        public string outputItemId;
        public int outputCount;
        public string pendingResultId;
    }

    public sealed class OnlineCraftStartResult
    {
        public bool success;
        public string code;
        public string message;
        public OnlineCraftSnapshot snapshot;
    }

    public static class OnlineActivityRuntime
    {
        private static OnlineActivityRuntimeHost _host;

        public static event Action<ActivityRuntimeSnapshot> Updated;
        public static event Action<string> Failed;

        public static bool IsReady => EnsureHost() != null && _host.EnsureBound();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureHost();
        }

        public static ActivityRuntimeSnapshot GetSnapshot()
        {
            return IsReady ? _host.GetSnapshot() : new ActivityRuntimeSnapshot();
        }

        public static WorkDescriptorResult GetWorkDescriptor(
            string activityId,
            string heroId,
            int plannedCycleCount = 0)
        {
            return IsReady
                ? _host.GetWorkDescriptor(activityId, heroId, plannedCycleCount)
                : new WorkDescriptorResult
                {
                    success = false,
                    issues = new[] { NotReadyIssue(activityId) }
                };
        }

        public static ActivityStartResult Start(ActivityStartRequest request)
        {
            if (!IsReady)
            {
                return new ActivityStartResult
                {
                    success = false,
                    issues = new[] { NotReadyIssue(request?.activityId) },
                    snapshot = new ActivityRuntimeSnapshot()
                };
            }

            return _host.StartActivity(request);
        }

        public static ActivityCancelResult Cancel(string executionId)
        {
            if (!IsReady)
            {
                return new ActivityCancelResult
                {
                    success = false,
                    executionId = executionId,
                    issues = new[] { NotReadyIssue(string.Empty) },
                    snapshot = new ActivityRuntimeSnapshot()
                };
            }

            return _host.CancelActivity(executionId);
        }

        public static PendingResultMutationResult Claim(string resultId)
        {
            if (!IsReady)
            {
                return new PendingResultMutationResult
                {
                    Success = false,
                    Code = "OnlineRuntimeNotReady",
                    Message = "Online activity runtime is waiting for Configs and Player."
                };
            }
            return _host.ClaimResult(resultId);
        }

        public static ActivityPendingRewardSnapshot GetPendingReward(string resultId)
        {
            return IsReady ? _host.GetPendingRewardSnapshot(resultId) : null;
        }

        public static OnlineCombatStartResult StartCombat(
            string activityId,
            string heroId,
            string stackId = null,
            int quantity = 0)
        {
            return IsReady
                ? _host.StartCombat(activityId, heroId, stackId, quantity)
                : new OnlineCombatStartResult
                {
                    success = false,
                    code = "OnlineRuntimeNotReady",
                    message = "Online activity runtime is waiting for Configs and Player."
                };
        }

        public static OnlineCombatAdvanceResult AdvanceCombat(string executionId, double deltaSeconds)
        {
            return IsReady
                ? _host.AdvanceCombat(executionId, deltaSeconds)
                : new OnlineCombatAdvanceResult
                {
                    success = false,
                    code = "OnlineRuntimeNotReady",
                    message = "Online activity runtime is waiting for Configs and Player."
                };
        }

        public static OnlineCombatSnapshot GetCombatSnapshot(string executionId)
        {
            return IsReady ? _host.GetCombatSnapshot(executionId) : null;
        }

        public static OnlineCombatSnapshot[] GetCombatSnapshots()
        {
            return IsReady ? _host.GetCombatSnapshots() : Array.Empty<OnlineCombatSnapshot>();
        }

        public static CraftStartDescriptor GetCraftDescriptor(
            string craftId,
            string heroId,
            string stationBuildingId,
            int stationBuildingLevel,
            int plannedCycles = 1)
        {
            return IsReady
                ? _host.GetCraftDescriptor(craftId, heroId, stationBuildingId, stationBuildingLevel, plannedCycles)
                : null;
        }

        public static OnlineCraftStartResult StartCraft(
            string craftId,
            string heroId,
            string stationBuildingId,
            int stationBuildingLevel,
            int plannedCycles = 1)
        {
            return IsReady
                ? _host.StartCraft(craftId, heroId, stationBuildingId, stationBuildingLevel, plannedCycles)
                : new OnlineCraftStartResult
                {
                    success = false,
                    code = "OnlineRuntimeNotReady",
                    message = "Online activity runtime is waiting for Configs and Player."
                };
        }

        public static OnlineCraftSnapshot GetCraftSnapshot(string executionId)
        {
            return IsReady ? _host.GetCraftSnapshot(executionId) : null;
        }

        public static OnlineCraftSnapshot[] GetCraftSnapshots()
        {
            return IsReady ? _host.GetCraftSnapshots() : Array.Empty<OnlineCraftSnapshot>();
        }

        internal static void RegisterHost(OnlineActivityRuntimeHost host)
        {
            _host = host;
        }

        internal static void UnregisterHost(OnlineActivityRuntimeHost host)
        {
            if (ReferenceEquals(_host, host))
                _host = null;
        }

        internal static void Publish(ActivityRuntimeSnapshot snapshot)
        {
            Updated?.Invoke(snapshot ?? new ActivityRuntimeSnapshot());
        }

        internal static void PublishFailure(string message)
        {
            Failed?.Invoke(message ?? string.Empty);
        }

        private static OnlineActivityRuntimeHost EnsureHost()
        {
            if (!Application.isPlaying)
                return null;
            if (_host != null)
                return _host;

            var gameObject = new GameObject("GuildIdle Online Activity Runtime");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            _host = gameObject.AddComponent<OnlineActivityRuntimeHost>();
            return _host;
        }

        private static ActivityRequirementIssue NotReadyIssue(string activityId)
        {
            return new ActivityRequirementIssue
            {
                activityId = activityId ?? string.Empty,
                issueType = "OnlineRuntimeNotReady",
                isError = true,
                message = "Online activity runtime is waiting for Configs and Player."
            };
        }
    }

    internal sealed class OnlineActivityRuntimeHost : MonoBehaviour
    {
        private const float PollIntervalSeconds = 0.2f;

        private PlayerState _boundState;
        private ActivityRuntimeService _activities;
        private CombatStartService _combatStart;
        private CombatRuntimeService _combatRuntime;
        private OnlineCombatDescriptorProvider _combatDescriptors;
        private CraftRuntimeService _crafts;
        private float _pollAccumulator;
        private string _lastFailure;

        private void Awake()
        {
            OnlineActivityRuntime.RegisterHost(this);
        }

        private void OnEnable()
        {
            OnlineActivityRuntime.RegisterHost(this);
        }

        private void Update()
        {
            if (!EnsureBound())
                return;

            _pollAccumulator += Time.unscaledDeltaTime;
            if (_pollAccumulator < PollIntervalSeconds)
                return;

            _pollAccumulator = 0f;
            AdvanceElapsedTime();
        }

        private void OnDestroy()
        {
            Release();
            OnlineActivityRuntime.UnregisterHost(this);
        }

        internal bool EnsureBound()
        {
            if (!GuildIdle.Player.Player.IsLoaded || GuildIdle.Player.Player.State == null)
            {
                Release();
                return false;
            }

            if (_activities != null && ReferenceEquals(_boundState, GuildIdle.Player.Player.State))
                return true;

            Release();
            _boundState = GuildIdle.Player.Player.State;
            _activities = PlayerRuntimeComposition.CreateRuntimeService(_boundState);
            _combatDescriptors = new OnlineCombatDescriptorProvider(_boundState);
            _combatStart = PlayerRuntimeComposition.CreateCombatStartService(_boundState);
            _combatRuntime = PlayerRuntimeComposition.CreateCombatRuntimeService(
                _boundState,
                _combatDescriptors);
            _crafts = PlayerRuntimeComposition.CreateCraftRuntimeService(_boundState);
            _pollAccumulator = 0f;
            _lastFailure = null;
            OnlineActivityRuntime.Publish(_activities.GetSnapshot());
            return true;
        }

        internal ActivityRuntimeSnapshot GetSnapshot()
        {
            return _activities?.GetSnapshot() ?? new ActivityRuntimeSnapshot();
        }

        internal WorkDescriptorResult GetWorkDescriptor(
            string activityId,
            string heroId,
            int plannedCycleCount)
        {
            return _activities.GetWorkDescriptor(activityId, heroId, plannedCycleCount);
        }

        internal ActivityStartResult StartActivity(ActivityStartRequest request)
        {
            if (!AdvanceElapsedTime())
            {
                return new ActivityStartResult
                {
                    success = false,
                    issues = new[]
                    {
                        new ActivityRequirementIssue
                        {
                            activityId = request?.activityId ?? string.Empty,
                            issueType = "ElapsedTimeAdvanceFailed",
                            isError = true,
                            message = _lastFailure ?? "Elapsed-time coordinator failed before activity start."
                        }
                    },
                    snapshot = GetSnapshot()
                };
            }

            var result = _activities.Start(request);
            OnlineActivityRuntime.Publish(result?.snapshot ?? GetSnapshot());
            return result;
        }

        internal ActivityCancelResult CancelActivity(string executionId)
        {
            if (!AdvanceElapsedTime())
            {
                return new ActivityCancelResult
                {
                    success = false,
                    executionId = executionId,
                    issues = new[]
                    {
                        new ActivityRequirementIssue
                        {
                            issueType = "ElapsedTimeAdvanceFailed",
                            targetId = executionId,
                            isError = true,
                            message = _lastFailure ?? "Elapsed-time coordinator failed before activity cancellation."
                        }
                    },
                    snapshot = GetSnapshot()
                };
            }

            var result = _activities.Cancel(executionId);
            OnlineActivityRuntime.Publish(result?.snapshot ?? GetSnapshot());
            return result;
        }

        internal PendingResultMutationResult ClaimResult(string resultId)
        {
            var pending = _boundState.PendingResults.Get(resultId);
            if (pending == null)
            {
                return new PendingResultMutationResult
                {
                    Success = false,
                    Code = "ResultNotFound",
                    Message = $"Pending result '{resultId}' does not exist."
                };
            }

            var result = _boundState.PendingResults.ClaimAvailable(
                $"online-activity:claim:{Guid.NewGuid():N}",
                pending.resultId,
                pending.revision,
                _boundState.Storage.GetSnapshot().Revision);
            OnlineActivityRuntime.Publish(GetSnapshot());
            return result;
        }

        internal ActivityPendingRewardSnapshot GetPendingRewardSnapshot(string resultId)
        {
            var pending = _boundState.PendingResults.Get(resultId);
            if (pending == null)
                return null;

            var source = pending.entries ?? Array.Empty<PendingResultEntrySaveData>();
            var entries = new ActivityPendingRewardEntrySnapshot[source.Length];
            for (var index = 0; index < source.Length; index++)
            {
                var entry = source[index];
                entries[index] = new ActivityPendingRewardEntrySnapshot
                {
                    rewardType = entry?.rewardType ?? string.Empty,
                    targetId = entry?.targetId ?? string.Empty,
                    quantity = entry?.quantity ?? 0L,
                    quality = entry?.quality ?? 0
                };
            }

            return new ActivityPendingRewardSnapshot
            {
                resultId = pending.resultId,
                sourceId = pending.sourceId,
                entries = entries
            };
        }

        internal OnlineCombatStartResult StartCombat(
            string activityId,
            string heroId,
            string stackId,
            int quantity)
        {
            if (!RuntimeConfigs.Activities.TryGetCombatDetails(activityId, out var details) || details == null)
            {
                return new OnlineCombatStartResult
                {
                    success = false,
                    code = CombatStartCode.InvalidActivityDescriptor.ToString(),
                    message = $"Combat activity '{activityId}' has no combat details."
                };
            }

            var madeAvailable = false;
            if (!_boundState.IsActivityAvailable(activityId))
            {
                if (!IsVisibleBuildingActivity(activityId))
                {
                    return new OnlineCombatStartResult
                    {
                        success = false,
                        code = CombatStartCode.ActivityUnavailable.ToString(),
                        message = $"Combat activity '{activityId}' is not exposed by an available building."
                    };
                }

                madeAvailable = _boundState.SetActivityAvailable(activityId, true);
            }

            var requestId = $"runtime-ui:combat:source:{Guid.NewGuid():N}";
            var result = _combatStart.Start(new CombatStartCommand
            {
                OperationId = $"runtime-ui:combat:start:{Guid.NewGuid():N}",
                Kind = CombatStartKind.Direct,
                SourceActivityId = activityId,
                SourceRequestId = requestId,
                HeroId = heroId,
                EnemyGroupId = details.enemyGroupId,
                CombatMode = details.combatMode,
                StackId = stackId,
                RequestedQuantity = quantity,
                ExpectedStorageRevision = _boundState.Storage.GetSnapshot().Revision
            });
            if (!result.Success && madeAvailable)
                _boundState.SetActivityAvailable(activityId, false);

            OnlineActivityRuntime.Publish(GetSnapshot());
            return new OnlineCombatStartResult
            {
                success = result.Success,
                code = result.Code.ToString(),
                message = result.Message,
                snapshot = result.Success ? GetCombatSnapshot(result.ExecutionId) : null
            };
        }

        internal OnlineCombatAdvanceResult AdvanceCombat(string executionId, double deltaSeconds)
        {
            var aggregate = _boundState.GetCombatAggregate(executionId);
            if (aggregate?.execution == null || aggregate.session == null)
            {
                return new OnlineCombatAdvanceResult
                {
                    success = false,
                    code = CombatAdvanceErrorCode.CombatNotFound.ToString(),
                    message = "Combat execution was not found."
                };
            }

            if (aggregate.execution.status != CombatExecutionStatus.Running ||
                aggregate.session.simulationStopped)
            {
                return new OnlineCombatAdvanceResult
                {
                    success = true,
                    snapshot = GetCombatSnapshot(executionId)
                };
            }

            var targetTime = aggregate.session.combatTimeSeconds + Math.Max(0d, deltaSeconds);
            var result = _combatRuntime.AdvanceTo(executionId, targetTime);
            OnlineActivityRuntime.Publish(GetSnapshot());
            return new OnlineCombatAdvanceResult
            {
                success = result.Success,
                code = result.Error?.Code.ToString(),
                message = result.Error?.Message,
                events = result.Events,
                snapshot = GetCombatSnapshot(executionId)
            };
        }

        internal OnlineCombatSnapshot GetCombatSnapshot(string executionId)
        {
            return BuildCombatSnapshot(_boundState.GetCombatAggregate(executionId));
        }

        internal OnlineCombatSnapshot[] GetCombatSnapshots()
        {
            var aggregates = _boundState.GetCombatAggregates();
            var result = new List<OnlineCombatSnapshot>(aggregates.Length);
            foreach (var aggregate in aggregates)
            {
                var snapshot = BuildCombatSnapshot(aggregate);
                if (snapshot != null && snapshot.status != CombatExecutionStatus.Completed)
                    result.Add(snapshot);
            }
            return result.ToArray();
        }

        internal CraftStartDescriptor GetCraftDescriptor(
            string craftId,
            string heroId,
            string stationBuildingId,
            int stationBuildingLevel,
            int plannedCycles)
        {
            return _crafts.GetStartDescriptor(new CraftStartRequest
            {
                CraftId = craftId,
                HeroId = heroId,
                StationBuildingId = stationBuildingId,
                StationBuildingLevel = stationBuildingLevel,
                PlannedCycles = plannedCycles,
                OperationKey = "runtime-ui:craft:preview"
            });
        }

        internal OnlineCraftStartResult StartCraft(
            string craftId,
            string heroId,
            string stationBuildingId,
            int stationBuildingLevel,
            int plannedCycles)
        {
            if (!AdvanceElapsedTime())
            {
                return new OnlineCraftStartResult
                {
                    success = false,
                    code = "ElapsedTimeAdvanceFailed",
                    message = _lastFailure ?? "Elapsed-time coordinator failed before craft start."
                };
            }

            var result = _crafts.Start(new CraftStartRequest
            {
                CraftId = craftId,
                HeroId = heroId,
                StationBuildingId = stationBuildingId,
                StationBuildingLevel = stationBuildingLevel,
                PlannedCycles = plannedCycles,
                OperationKey = $"runtime-ui:craft:start:{Guid.NewGuid():N}"
            });
            OnlineActivityRuntime.Publish(GetSnapshot());
            return new OnlineCraftStartResult
            {
                success = result.Success,
                code = result.Code,
                message = result.Message,
                snapshot = result.Success ? GetCraftSnapshot(result.ExecutionId) : null
            };
        }

        internal OnlineCraftSnapshot GetCraftSnapshot(string executionId)
        {
            return BuildCraftSnapshot(_boundState.GetCraftExecution(executionId));
        }

        internal OnlineCraftSnapshot[] GetCraftSnapshots()
        {
            var source = _boundState.GetCraftExecutions();
            var result = new OnlineCraftSnapshot[source.Length];
            for (var index = 0; index < source.Length; index++)
                result[index] = BuildCraftSnapshot(source[index]);
            return result;
        }

        private static OnlineCraftSnapshot BuildCraftSnapshot(CraftExecutionSaveData execution)
        {
            if (execution == null)
                return null;
            return new OnlineCraftSnapshot
            {
                executionId = execution.executionId,
                craftId = execution.craftId,
                heroId = execution.heroId,
                stationBuildingId = execution.stationBuildingId,
                plannedCycles = execution.plannedCycles,
                status = execution.status,
                progressSeconds = execution.progressSeconds,
                durationSeconds = execution.durationSeconds,
                outputItemId = execution.outputItemId,
                outputCount = execution.outputCount,
                pendingResultId = execution.pendingResultId
            };
        }

        private OnlineCombatSnapshot BuildCombatSnapshot(CombatRuntimeAggregate aggregate)
        {
            if (aggregate?.execution == null || aggregate.session == null)
                return null;

            var session = aggregate.session;
            return new OnlineCombatSnapshot
            {
                executionId = aggregate.execution.executionId,
                activityId = aggregate.execution.sourceActivityId,
                heroId = aggregate.execution.heroId,
                enemyGroupId = session.enemyGroupId,
                outcome = aggregate.execution.outcome,
                pendingResultId = aggregate.execution.pendingResultId,
                status = aggregate.execution.status,
                combatTimeSeconds = session.combatTimeSeconds,
                enemyIndex = Math.Min(session.queuePosition + 1, session.enemyQueue?.Length ?? 0),
                enemyCount = session.enemyQueue?.Length ?? 0,
                consumableItemId = session.broughtConsumable?.itemId,
                consumableInitialQuantity = session.broughtConsumable?.initialQuantity ?? 0,
                consumableRemainingQuantity = session.broughtConsumable?.remainingQuantity ?? 0,
                hero = BuildActorSnapshot(CombatActorSide.Hero, session.hero),
                enemy = BuildActorSnapshot(CombatActorSide.Enemy, session.currentEnemy)
            };
        }

        private OnlineCombatActorSnapshot BuildActorSnapshot(
            CombatActorSide side,
            CombatantStateSaveData state)
        {
            if (state == null)
                return null;
            _combatDescriptors.TryGetDescriptor(side, state.definitionId, out var descriptor, out _);
            return new OnlineCombatActorSnapshot
            {
                definitionId = state.definitionId,
                currentHp = state.currentHp,
                maxHp = state.maxHp,
                damageMin = descriptor?.DamageMin ?? 0,
                damageMax = descriptor?.DamageMax ?? 0,
                attacksPerSecond = descriptor == null
                    ? 0d
                    : descriptor.Cadence.Kind == CombatAttackCadenceKind.AttacksPerSecond
                        ? descriptor.Cadence.Value
                        : 1d / descriptor.Cadence.Value,
                critChancePercent = descriptor?.CritChancePercent ?? 0d,
                dodgeChancePercent = descriptor?.DodgeChancePercent ?? 0d,
                physicalResistancePercent = descriptor?.PhysicalResistancePercent ?? 0d,
                magicResistancePercent = descriptor?.MagicResistancePercent ?? 0d
            };
        }

        private bool IsVisibleBuildingActivity(string activityId)
        {
            foreach (var mapping in RuntimeConfigs.Buildings.BuildingActivities)
            {
                if (mapping == null ||
                    !string.Equals(mapping.activityId, activityId, StringComparison.Ordinal) ||
                    _boundState.GetBuildingLevel(mapping.buildingId) != mapping.buildingLevel ||
                    !_boundState.IsBuildingUnlocked(mapping.buildingId) ||
                    (!string.IsNullOrWhiteSpace(mapping.showIfActivityCompleted) &&
                     !_boundState.IsActivityCompleted(mapping.showIfActivityCompleted)) ||
                    (!string.IsNullOrWhiteSpace(mapping.hideIfActivityCompleted) &&
                     _boundState.IsActivityCompleted(mapping.hideIfActivityCompleted)))
                {
                    continue;
                }

                return true;
            }
            return false;
        }

        private bool AdvanceElapsedTime()
        {
            if (_boundState == null || _activities == null)
                return false;

            var plan = _boundState.TimeProgress.PrepareAdvance();
            if (plan.Code == TimeAdvanceResultCode.NoElapsedTime ||
                plan.Code == TimeAdvanceResultCode.ClockRollback)
            {
                return true;
            }

            var checkpoint = _boundState.ToSaveData();
            var eligibility = _boundState.TimeProgress.CaptureEligibilitySnapshot();
            var applied = _boundState.TimeProgress.Apply(plan, eligibility);
            if (!applied.Success)
            {
                var message =
                    $"Online time baseline could not advance: {applied.Code}.";
                return FailAndRestore(checkpoint, message);
            }

            if (plan.Code == TimeAdvanceResultCode.Applied && plan.DeltaSeconds > 0L)
            {
                var tick = _activities.Tick(plan.DeltaSeconds);
                if (!tick.success)
                    return FailAndRestore(checkpoint, FirstIssue(tick.issues, "Activity runtime tick failed."));
                if (!AdvanceCrafts(plan.DeltaSeconds, out var craftError))
                    return FailAndRestore(checkpoint, craftError);
                if (!tick.saved && !_boundState.Save())
                    return FailAndRestore(checkpoint, "Online elapsed-time state could not be saved.");
                OnlineActivityRuntime.Publish(tick.snapshot ?? GetSnapshot());
            }
            else if (!_boundState.Save())
            {
                return FailAndRestore(checkpoint, "Online time baseline could not be saved.");
            }

            _lastFailure = null;
            return true;
        }

        private bool AdvanceCrafts(long deltaSeconds, out string error)
        {
            error = null;
            foreach (var execution in _boundState.GetCraftExecutions())
            {
                if (execution == null || execution.status != CraftExecutionStatus.Running)
                    continue;
                var sequence = execution.lastAdvanceSequence + 1L;
                var result = _crafts.Advance(
                    execution.executionId,
                    deltaSeconds,
                    $"online-craft:{execution.executionId}:{sequence}",
                    sequence);
                if (result.Success)
                    continue;
                error = $"Craft '{execution.craftId}' advance failed: {result.Code}: {result.Message}";
                return false;
            }
            return true;
        }

        private bool FailAndRestore(SaveData checkpoint, string message)
        {
            _boundState.RestoreTransactional(checkpoint);
            if (!string.Equals(_lastFailure, message, StringComparison.Ordinal))
            {
                _lastFailure = message;
                Debug.LogError($"[OnlineActivityRuntime] {message}");
                OnlineActivityRuntime.PublishFailure(message);
            }
            return false;
        }

        private static string FirstIssue(ActivityRequirementIssue[] issues, string fallback)
        {
            if (issues == null || issues.Length == 0 || issues[0] == null)
                return fallback;
            return $"{issues[0].issueType}: {issues[0].message}";
        }

        private void Release()
        {
            _activities?.Dispose();
            _activities = null;
            _combatStart = null;
            _combatRuntime = null;
            _combatDescriptors = null;
            _crafts = null;
            _boundState = null;
            _pollAccumulator = 0f;
        }
    }

    internal sealed class OnlineCombatDescriptorProvider : ICombatDescriptorProvider
    {
        private readonly PlayerState _state;

        public OnlineCombatDescriptorProvider(PlayerState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public bool TryGetDescriptor(
            CombatActorSide side,
            string definitionId,
            out CombatActorDescriptor descriptor,
            out string error)
        {
            return side == CombatActorSide.Hero
                ? TryGetHeroDescriptor(definitionId, out descriptor, out error)
                : TryGetEnemyDescriptor(definitionId, out descriptor, out error);
        }

        private bool TryGetHeroDescriptor(
            string heroId,
            out CombatActorDescriptor descriptor,
            out string error)
        {
            descriptor = null;
            error = null;
            EquipmentWeaponConfigDto weapon = null;
            foreach (var slot in _state.GetEquipmentSlots())
            {
                if (slot == null || !string.Equals(slot.heroId, heroId, StringComparison.Ordinal))
                    continue;
                var item = _state.GetEquippedItem(heroId, slot.equipmentSlot);
                if (item != null && RuntimeConfigs.Items.TryGetEquipmentWeapon(item.itemId, out weapon))
                    break;
            }

            if (weapon == null)
            {
                error = $"Hero '{heroId}' has no equipped weapon combat descriptor.";
                return false;
            }

            var damagePrefix = string.Equals(weapon.damageType, "Magic", StringComparison.OrdinalIgnoreCase)
                ? "hero_magic_damage_"
                : string.Equals(weapon.attackRange, "Ranged", StringComparison.OrdinalIgnoreCase)
                    ? "hero_ranged_damage_"
                    : "hero_melee_damage_";
            if (!TryEvaluate(damagePrefix + "min", heroId, weapon, out var damageMin, out error) ||
                !TryEvaluate(damagePrefix + "max", heroId, weapon, out var damageMax, out error) ||
                !TryEvaluate("hero_attack_interval", heroId, weapon, out var attackInterval, out error) ||
                !TryEvaluate("hero_crit_chance", heroId, weapon, out var critChance, out error) ||
                !TryEvaluate("hero_crit_multiplier", heroId, weapon, out var critMultiplier, out error) ||
                !TryEvaluate("hero_physical_resistance", heroId, weapon, out var physicalResistance, out error) ||
                !TryEvaluate("hero_magic_resistance", heroId, weapon, out var magicResistance, out error) ||
                !TryEvaluate("hero_dodge_chance", heroId, weapon, out var dodgeChance, out error))
            {
                return false;
            }

            foreach (var slot in _state.GetEquipmentSlots())
            {
                if (slot == null || !string.Equals(slot.heroId, heroId, StringComparison.Ordinal))
                    continue;
                var item = _state.GetEquippedItem(heroId, slot.equipmentSlot);
                if (item != null && RuntimeConfigs.Items.TryGetEquipmentArmor(item.itemId, out var armor))
                {
                    physicalResistance += Math.Max(0, armor.physicalResistBonus);
                    magicResistance += Math.Max(0, armor.magicResistBonus);
                }
            }

            descriptor = new CombatActorDescriptor(
                CombatActorSide.Hero,
                CombatAttackCadence.HeroInterval(attackInterval),
                Math.Max(0, (int)damageMin),
                Math.Max(0, (int)damageMax),
                weapon.damageType,
                ClampPercent(critChance),
                Math.Max(1d, critMultiplier),
                ClampPercent(dodgeChance),
                ClampPercent(physicalResistance),
                ClampPercent(magicResistance));
            return true;
        }

        private static bool TryGetEnemyDescriptor(
            string enemyId,
            out CombatActorDescriptor descriptor,
            out string error)
        {
            descriptor = null;
            error = null;
            if (!RuntimeConfigs.Enemies.TryGet(enemyId, out var enemy) || enemy == null)
            {
                error = $"Enemy '{enemyId}' combat descriptor was not found.";
                return false;
            }

            descriptor = new CombatActorDescriptor(
                CombatActorSide.Enemy,
                CombatAttackCadence.EnemyRate(enemy.attacksPerSecond),
                enemy.damageMin,
                enemy.damageMax,
                enemy.damageType,
                enemy.critChancePercent,
                enemy.critDamageMultiplier,
                enemy.dodgeChancePercent,
                enemy.physicalResistPercent,
                enemy.magicResistPercent,
                null);
            return true;
        }

        private bool TryEvaluate(
            string formulaId,
            string heroId,
            EquipmentWeaponConfigDto weapon,
            out double value,
            out string error)
        {
            value = 0d;
            error = null;
            if (!RuntimeConfigs.Formulas.TryGetFormula(formulaId, out var formula) ||
                formula == null || !formula.enabled)
            {
                error = $"Combat formula '{formulaId}' is missing or disabled.";
                return false;
            }

            var primary = _state.CalculateHeroStat(heroId, formula.primaryStat);
            var secondary = string.IsNullOrWhiteSpace(formula.secondaryStat)
                ? 0
                : _state.CalculateHeroStat(heroId, formula.secondaryStat);
            var level = Math.Max(1, _state.GetHeroState(heroId)?.level ?? 1);
            var weaponValue = WeaponValue(formula.weaponValueMode, weapon);
            if (string.Equals(formula.formulaType, "inverse_interval_stat", StringComparison.OrdinalIgnoreCase))
                value = weaponValue + formula.baseValue -
                        primary * formula.primaryStatMultiplier -
                        secondary * formula.secondaryStatMultiplier +
                        level * formula.levelMultiplier;
            else
                value = weaponValue + formula.baseValue +
                        primary * formula.primaryStatMultiplier +
                        secondary * formula.secondaryStatMultiplier +
                        level * formula.levelMultiplier;

            value = Math.Max(formula.minValue, value);
            if (formula.maxValue > 0f)
                value = Math.Min(formula.maxValue, value);
            if (formula.capValue > 0f)
                value = Math.Min(formula.capValue, value);
            value = Round(value, formula.rounding);
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                error = $"Combat formula '{formulaId}' produced an invalid value.";
                return false;
            }
            return true;
        }

        private static double WeaponValue(string mode, EquipmentWeaponConfigDto weapon)
        {
            if (string.Equals(mode, "weapon_damage_min", StringComparison.OrdinalIgnoreCase))
                return weapon.weaponDamageMin;
            if (string.Equals(mode, "weapon_damage_max", StringComparison.OrdinalIgnoreCase))
                return weapon.weaponDamageMax;
            if (string.Equals(mode, "weapon_attack_interval", StringComparison.OrdinalIgnoreCase))
                return weapon.weaponAttackInterval;
            return 0d;
        }

        private static double Round(double value, string rounding)
        {
            if (string.Equals(rounding, "floor", StringComparison.OrdinalIgnoreCase))
                return Math.Floor(value);
            if (string.Equals(rounding, "ceil", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rounding, "ceiling", StringComparison.OrdinalIgnoreCase))
                return Math.Ceiling(value);
            if (string.Equals(rounding, "round_2", StringComparison.OrdinalIgnoreCase))
                return Math.Round(value, 2, MidpointRounding.AwayFromZero);
            return Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private static double ClampPercent(double value) => Math.Max(0d, Math.Min(100d, value));
    }
}
