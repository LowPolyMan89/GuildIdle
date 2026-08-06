using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Player;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;
using CoreActivityRuntimeStatus = GuildIdle.Core.ActivityRuntimeStatus;

namespace GuildIdle.Activities
{
    public sealed class ActivityRuntimeService : ILinkedCombatStartGateway, IDisposable
    {
        public const int MaxCyclesPerTick = 100;
        public const int DefaultWorkAdvanceOperationLimit = 256;
        public const int DefaultConstructionAdvanceOperationLimit = 4096;

        private const string RuntimeKindActivity = "Activity";
        private const string RuntimeKindWork = "Work";
        private const string RuntimeKindBuild = "Build";
        private const string CyclePhaseRunning = "Running";
        private const string CyclePhaseResultStaged = "ResultStaged";
        private const string EndReasonPlanCompleted = "PlanCompleted";
        private const string EndReasonManualStop = "ManualStop";
        private const string EndReasonDangerTriggered = "DangerTriggered";
        private const string EndReasonInsufficientFatigue = "InsufficientFatigue";
        private const string CompletionPhaseBuildingEventPending = "BuildingEventPending";
        private const string CompletionPhaseCompletionReady = "CompletionReady";
        private const string WorkEffectTrigger = "OnWorkCycleComplete";
        private const string AddExtraBaseResourceEffect = "AddExtraBaseResource";
        private const string CompletedWorkBaseResourceTarget = "completed_work_base_resource";

        private readonly IActivityRuntimeStore _store;
        private readonly IActivityPlayerState _activityState;
        private readonly IActivityRandom _random;
        private readonly FormulaRuntime _formulas;
        private readonly Action<ActivityRuntimeEvent> _eventSink;
        private readonly IActivityRuntimeProgressionProcessor _progressionProcessor;
        private readonly ILinkedCombatRuntimeCoordinator _linkedCombatCoordinator;
        private readonly bool _runtimeLifecycleEnabled;
        private readonly Dictionary<string, Func<HeroSkillEffectConfigDto, ActivityStagedRewardSaveData[], bool>> _workEffectHandlers;
        private bool _disposed;

        private enum WorkCycleOutcome
        {
            Completed = 0,
            InsufficientFatigue = 1,
            DangerBoundaryReached = 2,
            ValidationFailed = 3,
            RuntimeError = 4
        }

        private sealed class DangerHandoffMutationResult
        {
            public bool Success;
            public string Code;
            public string RequestId;
            public string PendingResultId;
            public int CombatEntryCount;
            public int NonCombatEntryCount;
            public bool ActivityBagResolved;
            public bool RequestCreated;
            public bool Replayed;
        }

        private readonly struct WorkCycleOptions
        {
            public WorkCycleOptions(bool saveStagedBoundary, bool stopAtDangerBoundary, bool fatigueFailureIsStop)
            {
                SaveStagedBoundary = saveStagedBoundary;
                StopAtDangerBoundary = stopAtDangerBoundary;
                FatigueFailureIsStop = fatigueFailureIsStop;
            }

            public bool SaveStagedBoundary { get; }
            public bool StopAtDangerBoundary { get; }
            public bool FatigueFailureIsStop { get; }

            public static WorkCycleOptions Online => new WorkCycleOptions(true, false, false);
            public static WorkCycleOptions Offline => new WorkCycleOptions(false, true, true);
        }

        private readonly struct BuildCompletionOptions
        {
            public BuildCompletionOptions(bool saveInternally, bool publishEvents, bool useOuterTransaction)
            {
                SaveInternally = saveInternally;
                PublishEvents = publishEvents;
                UseOuterTransaction = useOuterTransaction;
            }

            public bool SaveInternally { get; }
            public bool PublishEvents { get; }
            public bool UseOuterTransaction { get; }

            public static BuildCompletionOptions Online => new BuildCompletionOptions(true, true, false);
            public static BuildCompletionOptions OuterTransaction => new BuildCompletionOptions(false, false, true);
        }

        private enum BuildProgressOutcome
        {
            IntervalExhausted = 0,
            Completed = 1,
            ProcessingLimitReached = 2,
            FormulaFailed = 3
        }

        public ActivityRuntimeService(
            IActivityRuntimeStore store,
            IActivityPlayerState activityState,
            IActivityRandom random = null,
            FormulaRuntime formulas = null,
            Action<ActivityRuntimeEvent> eventSink = null,
            IActivityRuntimeProgressionProcessor progressionProcessor = null,
            Func<
                ILinkedCombatStartGateway,
                IActivityRuntimeProgressionProcessor,
                ILinkedCombatRuntimeCoordinator> linkedCombatCoordinatorFactory = null)
            : this(
                store,
                activityState,
                random,
                formulas,
                eventSink,
                progressionProcessor,
                linkedCombatCoordinatorFactory,
                true)
        {
        }

        private ActivityRuntimeService(
            IActivityRuntimeStore store,
            IActivityPlayerState activityState,
            IActivityRandom random,
            FormulaRuntime formulas,
            Action<ActivityRuntimeEvent> eventSink,
            IActivityRuntimeProgressionProcessor progressionProcessor,
            Func<
                ILinkedCombatStartGateway,
                IActivityRuntimeProgressionProcessor,
                ILinkedCombatRuntimeCoordinator> linkedCombatCoordinatorFactory,
            bool enableRuntimeLifecycle)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _activityState = activityState ?? throw new ArgumentNullException(nameof(activityState));
            _random = random ?? new SystemActivityRandom();
            _formulas = formulas ?? new FormulaRuntime();
            _eventSink = eventSink;
            _progressionProcessor = progressionProcessor;
            _workEffectHandlers = new Dictionary<string, Func<HeroSkillEffectConfigDto, ActivityStagedRewardSaveData[], bool>>(StringComparer.OrdinalIgnoreCase)
            {
                [AddExtraBaseResourceEffect] = ApplyExtraBaseResource
            };
            _runtimeLifecycleEnabled = enableRuntimeLifecycle;
            if (_runtimeLifecycleEnabled)
            {
                _activityState.PendingResults.Resolved += HandlePendingResultResolved;
                ReconcilePendingBuildingEvents();
                ReconcileLinkedCombatCompletions();
                _linkedCombatCoordinator =
                    linkedCombatCoordinatorFactory?.Invoke(
                        this,
                        _progressionProcessor);
                _linkedCombatCoordinator?.Reconcile();
            }
        }

        internal static ActivityRuntimeService CreateWorkAdvanceCore(
            IActivityRuntimeStore store,
            IActivityPlayerState activityState,
            ITransactionalActivityRandom random,
            FormulaRuntime formulas)
        {
            return new ActivityRuntimeService(
                store,
                activityState,
                random ?? throw new ArgumentNullException(nameof(random)),
                formulas,
                null,
                null,
                null,
                false);
        }

        internal static ActivityRuntimeService CreateConstructionAdvanceCore(
            IActivityRuntimeStore store,
            IActivityPlayerState activityState,
            FormulaRuntime formulas,
            IActivityRuntimeProgressionProcessor progressionProcessor)
        {
            return new ActivityRuntimeService(
                store,
                activityState,
                new SystemActivityRandom(1),
                formulas,
                null,
                progressionProcessor,
                null,
                false);
        }

        internal static ActivityRuntimeService CreateDangerEncounterPreparationCore(
            IActivityRuntimeStore store,
            IActivityPlayerState activityState,
            FormulaRuntime formulas)
        {
            return new ActivityRuntimeService(
                store,
                activityState,
                new SystemActivityRandom(1),
                formulas,
                null,
                null,
                null,
                false);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            if (_runtimeLifecycleEnabled)
                _activityState.PendingResults.Resolved -= HandlePendingResultResolved;
            _linkedCombatCoordinator?.Dispose();
            _disposed = true;
        }

        public ActivityStartResult Start(string activityId, string heroId)
        {
            return Start(new ActivityStartRequest { activityId = activityId, heroId = heroId });
        }

        public ActivityStartResult Start(ActivityStartRequest request)
        {
            request ??= new ActivityStartRequest();
            if (RuntimeConfigs.Buildings.TryGetBuildAction(request.activityId, out var buildAction))
                return StartBuild(request, buildAction);

            var issues = new List<ActivityRequirementIssue>();
            if (!TryGetRuntimeInfo(request.activityId, issues, out var info))
                return FinishStart(NewStartResult(request), issues, false);

            if (IsWork(info))
                return StartWork(request, info);
            return StartStandard(request, info);
        }

        public WorkDescriptorResult GetWorkDescriptor(string activityId, string heroId, int plannedCycleCount = 0)
        {
            var issues = new List<ActivityRequirementIssue>();
            if (!TryGetRuntimeInfo(activityId, issues, out var info) || !IsWork(info))
            {
                if (info != null)
                    AddIssue(issues, activityId, "NotWorkActivity", activityId, 1, 0, false, false, $"Activity '{activityId}' is not a Work activity.");
                return new WorkDescriptorResult { success = false, issues = issues.ToArray() };
            }
            if (string.IsNullOrWhiteSpace(heroId) || !_activityState.HasHero(heroId) || !_activityState.HasHeroState(heroId))
            {
                AddIssue(issues, activityId, "HeroExecutor", heroId, 1, 0, false, false, "Work descriptor requires an acquired hero with runtime state.");
                return new WorkDescriptorResult { success = false, issues = issues.ToArray() };
            }
            if (info.activity.fatigueCost <= 0)
            {
                AddIssue(issues, activityId, "InvalidCycleFatigue", activityId, 1, info.activity.fatigueCost, true, false, "Work activity requires positive fatigue_cost per cycle.");
                return new WorkDescriptorResult { success = false, issues = issues.ToArray() };
            }

            var maxCycles = _activityState.GetHeroFatigue(heroId) / info.activity.fatigueCost;
            var selected = plannedCycleCount > 0 ? plannedCycleCount : 0;
            var ranges = new List<ActivityPlannedRewardRange>();
            foreach (var reward in RuntimeConfigs.Activities.GetRewards(activityId))
            {
                if (reward == null || !ActivityResolverUtilities.MomentMatches(reward.grantMoment, GrantMoment.OnCycle) ||
                    !ActivityTypeParser.TryParseRewardType(reward.rewardType, out var rewardType) || rewardType == RewardTypeEnum.LootTable)
                    continue;
                var minPerCycle = reward.chance >= 100f ? Math.Max(0, reward.min) : 0;
                var maxPerCycle = reward.chance > 0f ? Math.Max(reward.min, reward.max) : 0;
                ranges.Add(new ActivityPlannedRewardRange
                {
                    rewardType = reward.rewardType,
                    targetId = reward.targetId,
                    minAmount = (long)minPerCycle * selected,
                    maxAmount = (long)maxPerCycle * selected
                });
            }

            return new WorkDescriptorResult
            {
                success = true,
                descriptor = new WorkRuntimeDescriptor
                {
                    activityId = activityId,
                    heroId = heroId,
                    minCycleCount = maxCycles > 0 ? 1 : 0,
                    maxCycleCount = maxCycles,
                    cycleSeconds = info.activity.cycleSec,
                    fatiguePerCycle = info.activity.fatigueCost,
                    plannedCycleCount = selected,
                    plannedFatigue = selected * info.activity.fatigueCost,
                    plannedDurationSeconds = (long)selected * info.activity.cycleSec,
                    expectedRewards = ranges.ToArray()
                },
                issues = issues.ToArray()
            };
        }

        internal DangerEncounterPreparationResult PrepareDangerEncounterCore(
            DangerEncounterPreparationRequest request)
        {
            var issues = new List<ActivityRequirementIssue>();
            var deferredResolvedEvents = new List<PendingResultDeferredResolvedEvent>();
            var executionId = request?.ExecutionId;
            if (request == null || string.IsNullOrWhiteSpace(executionId))
            {
                AddIssue(
                    issues,
                    string.Empty,
                    DangerEncounterPreparationCode.ValidationFailed,
                    executionId,
                    1,
                    0,
                    true,
                    false,
                    "Danger encounter preparation requires executionId.");
                return FinishDangerEncounterPreparation(
                    false,
                    DangerEncounterPreparationCode.ValidationFailed,
                    executionId,
                    null,
                    issues,
                    deferredResolvedEvents);
            }

            var execution = GetExecution(executionId);
            if (execution == null)
            {
                AddIssue(
                    issues,
                    string.Empty,
                    DangerEncounterPreparationCode.ExecutionNotFound,
                    executionId,
                    1,
                    0,
                    true,
                    false,
                    $"Activity execution '{executionId}' was not found.");
                return FinishDangerEncounterPreparation(
                    false,
                    DangerEncounterPreparationCode.ExecutionNotFound,
                    executionId,
                    null,
                    issues,
                    deferredResolvedEvents);
            }

            var hasLinkedCombat = execution.linkedCombat != null;
            if (!hasLinkedCombat &&
                (execution.status != CoreActivityRuntimeStatus.Running ||
                 !string.Equals(execution.runtimeKind, RuntimeKindWork, StringComparison.Ordinal) ||
                 !string.Equals(execution.cyclePhase, CyclePhaseResultStaged, StringComparison.Ordinal) ||
                 !IsTriggeredDangerBoundary(execution)))
            {
                AddIssue(
                    issues,
                    execution.activityId,
                    DangerEncounterPreparationCode.NotDangerBoundary,
                    execution.executionId,
                    1,
                    0,
                    false,
                    false,
                    "Execution is not a saved triggered Work danger boundary.");
                return FinishDangerEncounterPreparation(
                    false,
                    DangerEncounterPreparationCode.NotDangerBoundary,
                    executionId,
                    null,
                    issues,
                    deferredResolvedEvents);
            }

            if (!TryGetRuntimeInfo(execution.activityId, issues, out var info) || !IsWork(info) ||
                (!hasLinkedCombat && !ValidateWorkAdvanceExecution(execution, info, issues)))
            {
                return FinishDangerEncounterPreparation(
                    false,
                    DangerEncounterPreparationCode.ValidationFailed,
                    executionId,
                    null,
                    issues,
                    deferredResolvedEvents);
            }

            var checkpoint = _activityState.CaptureCheckpoint();
            DangerHandoffMutationResult handoff;
            try
            {
                handoff = PrepareDangerHandoff(
                    execution,
                    info,
                    issues,
                    true,
                    deferredResolvedEvents);
            }
            catch (Exception exception)
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(
                    issues,
                    execution.activityId,
                    DangerEncounterPreparationCode.RuntimeError,
                    execution.executionId,
                    1,
                    0,
                    true,
                    false,
                    $"Danger encounter preparation failed: {exception.Message}");
                handoff = new DangerHandoffMutationResult
                {
                    Success = false,
                    Code = DangerEncounterPreparationCode.RuntimeError
                };
            }

            if (!handoff.Success)
                _activityState.RestoreCheckpoint(checkpoint);
            return FinishDangerEncounterPreparation(
                handoff.Success,
                handoff.Code,
                executionId,
                handoff,
                issues,
                deferredResolvedEvents);
        }

        internal WorkAdvanceResult AdvanceWorkCore(WorkAdvanceRequest request)
        {
            var issues = new List<ActivityRequirementIssue>();
            var rewards = new List<ActivityRewardResult>();
            var deferredResolvedEvents = new List<PendingResultDeferredResolvedEvent>();
            var executionId = request?.ExecutionId;
            var availableSeconds = request == null ? 0L : Math.Max(0L, request.AvailableSeconds);
            var remainingSeconds = availableSeconds;
            var processedCycles = 0;

            if (request == null || string.IsNullOrWhiteSpace(request.ExecutionId) ||
                request.AvailableSeconds < 0L || request.OperationLimit <= 0)
            {
                AddIssue(
                    issues,
                    string.Empty,
                    "WorkAdvanceRequest",
                    executionId,
                    0,
                    request?.AvailableSeconds ?? 0L,
                    true,
                    false,
                    "Work advance requires executionId, non-negative availableSeconds, and a positive operationLimit.");
                return FinishWorkAdvance(
                    false,
                    WorkAdvanceStopReason.InvalidRequest,
                    executionId,
                    processedCycles,
                    availableSeconds,
                    remainingSeconds,
                    null,
                    issues,
                    deferredResolvedEvents);
            }

            var execution = GetExecution(request.ExecutionId);
            if (execution == null)
            {
                AddIssue(issues, string.Empty, "ActivityExecution", request.ExecutionId, 1, 0, false, false, $"Activity execution '{request.ExecutionId}' was not found.");
                return FinishWorkAdvance(
                    false,
                    WorkAdvanceStopReason.ExecutionNotFound,
                    request.ExecutionId,
                    processedCycles,
                    availableSeconds,
                    remainingSeconds,
                    null,
                    issues,
                    deferredResolvedEvents);
            }
            if (execution.status != CoreActivityRuntimeStatus.Running)
            {
                AddIssue(issues, execution.activityId, "ActivityExecution", request.ExecutionId, 1, (long)execution.status, false, false, $"Activity execution '{request.ExecutionId}' is not running.");
                return FinishWorkAdvance(
                    false,
                    WorkAdvanceStopReason.ExecutionNotRunning,
                    request.ExecutionId,
                    processedCycles,
                    availableSeconds,
                    remainingSeconds,
                    execution,
                    issues,
                    deferredResolvedEvents);
            }
            if (!string.Equals(execution.runtimeKind, RuntimeKindWork, StringComparison.Ordinal))
            {
                AddIssue(issues, execution.activityId, "NotWorkExecution", execution.executionId, 1, 0, false, false, $"Activity execution '{execution.executionId}' is not a Work execution.");
                return FinishWorkAdvance(
                    false,
                    WorkAdvanceStopReason.NotWorkExecution,
                    request.ExecutionId,
                    processedCycles,
                    availableSeconds,
                    remainingSeconds,
                    execution,
                    issues,
                    deferredResolvedEvents);
            }
            if (!TryGetRuntimeInfo(execution.activityId, issues, out var info))
            {
                return FinishWorkAdvance(
                    false,
                    WorkAdvanceStopReason.ValidationFailed,
                    request.ExecutionId,
                    processedCycles,
                    availableSeconds,
                    remainingSeconds,
                    execution,
                    issues,
                    deferredResolvedEvents);
            }
            if (!IsWork(info))
            {
                AddIssue(issues, execution.activityId, "NotWorkExecution", execution.executionId, 1, 0, false, false, $"Activity execution '{execution.executionId}' is not a Work execution.");
                return FinishWorkAdvance(
                    false,
                    WorkAdvanceStopReason.NotWorkExecution,
                    request.ExecutionId,
                    processedCycles,
                    availableSeconds,
                    remainingSeconds,
                    execution,
                    issues,
                    deferredResolvedEvents);
            }
            if (!ValidateWorkAdvanceExecution(execution, info, issues))
            {
                return FinishWorkAdvance(
                    false,
                    WorkAdvanceStopReason.ValidationFailed,
                    request.ExecutionId,
                    processedCycles,
                    availableSeconds,
                    remainingSeconds,
                    execution,
                    issues,
                    deferredResolvedEvents);
            }

            var changed = false;
            if (string.Equals(execution.cyclePhase, CyclePhaseResultStaged, StringComparison.Ordinal))
            {
                var stagedOutcome = FinalizeStagedWorkCycle(
                    execution,
                    info,
                    issues,
                    ref changed,
                    WorkCycleOptions.Offline,
                    deferredResolvedEvents);
                if (stagedOutcome == WorkCycleOutcome.DangerBoundaryReached)
                {
                    return FinishWorkAdvance(
                        true,
                        WorkAdvanceStopReason.DangerBoundaryReached,
                        request.ExecutionId,
                        processedCycles,
                        availableSeconds,
                        remainingSeconds,
                        execution,
                        issues,
                        deferredResolvedEvents);
                }
                if (stagedOutcome == WorkCycleOutcome.InsufficientFatigue)
                {
                    return FinishWorkAdvance(
                        true,
                        WorkAdvanceStopReason.InsufficientFatigue,
                        request.ExecutionId,
                        processedCycles,
                        availableSeconds,
                        remainingSeconds,
                        execution,
                        issues,
                        deferredResolvedEvents);
                }
                if (stagedOutcome != WorkCycleOutcome.Completed)
                {
                    return FinishWorkAdvance(
                        false,
                        stagedOutcome == WorkCycleOutcome.ValidationFailed
                            ? WorkAdvanceStopReason.ValidationFailed
                            : WorkAdvanceStopReason.RuntimeError,
                        request.ExecutionId,
                        processedCycles,
                        availableSeconds,
                        remainingSeconds,
                        execution,
                        issues,
                        deferredResolvedEvents);
                }
                if (execution.status != CoreActivityRuntimeStatus.Running)
                {
                    return FinishWorkAdvance(
                        true,
                        WorkAdvanceStopReason.PlanCompleted,
                        request.ExecutionId,
                        processedCycles,
                        availableSeconds,
                        remainingSeconds,
                        execution,
                        issues,
                        deferredResolvedEvents);
                }
            }

            var fatigueOutcome = EnsureCurrentWorkCycleFatigue(
                execution,
                info,
                issues,
                ref changed,
                deferredResolvedEvents);
            if (fatigueOutcome != WorkCycleOutcome.Completed)
            {
                return FinishWorkAdvance(
                    fatigueOutcome == WorkCycleOutcome.InsufficientFatigue && !HasBlockingIssues(issues),
                    fatigueOutcome == WorkCycleOutcome.InsufficientFatigue
                        ? WorkAdvanceStopReason.InsufficientFatigue
                        : WorkAdvanceStopReason.RuntimeError,
                    request.ExecutionId,
                    processedCycles,
                    availableSeconds,
                    remainingSeconds,
                    execution,
                    issues,
                    deferredResolvedEvents);
            }

            while (execution.status == CoreActivityRuntimeStatus.Running &&
                   execution.completedCycles < execution.plannedCycles)
            {
                if (processedCycles >= request.OperationLimit)
                {
                    return FinishWorkAdvance(
                        true,
                        WorkAdvanceStopReason.ProcessingLimitReached,
                        request.ExecutionId,
                        processedCycles,
                        availableSeconds,
                        remainingSeconds,
                        execution,
                        issues,
                        deferredResolvedEvents);
                }

                var secondsForCycle = Math.Max(0L, (long)Math.Ceiling(info.durationSeconds - execution.elapsedSeconds));
                if (remainingSeconds < secondsForCycle)
                {
                    var partial = CloneExecution(execution);
                    partial.elapsedSeconds += remainingSeconds;
                    if (!UpdateExecution(partial))
                    {
                        AddIssue(issues, execution.activityId, "ActivityExecution", execution.executionId, 1, 0, true, false, "Failed to persist partial work-cycle progress.");
                        return FinishWorkAdvance(
                            false,
                            WorkAdvanceStopReason.RuntimeError,
                            request.ExecutionId,
                            processedCycles,
                            availableSeconds,
                            remainingSeconds,
                            execution,
                            issues,
                            deferredResolvedEvents);
                    }
                    CopyExecutionState(partial, execution);
                    remainingSeconds = 0L;
                    return FinishWorkAdvance(
                        true,
                        WorkAdvanceStopReason.IntervalExhausted,
                        request.ExecutionId,
                        processedCycles,
                        availableSeconds,
                        remainingSeconds,
                        execution,
                        issues,
                        deferredResolvedEvents);
                }

                var cycleExecution = CloneExecution(execution);
                cycleExecution.elapsedSeconds += secondsForCycle;
                var cycleOutcome = CompleteWorkCycle(
                    cycleExecution,
                    info,
                    issues,
                    rewards,
                    ref changed,
                    WorkCycleOptions.Offline,
                    deferredResolvedEvents);
                if (cycleOutcome == WorkCycleOutcome.ValidationFailed || cycleOutcome == WorkCycleOutcome.RuntimeError)
                {
                    return FinishWorkAdvance(
                        false,
                        cycleOutcome == WorkCycleOutcome.ValidationFailed
                            ? WorkAdvanceStopReason.ValidationFailed
                            : WorkAdvanceStopReason.RuntimeError,
                        request.ExecutionId,
                        processedCycles,
                        availableSeconds,
                        remainingSeconds,
                        execution,
                        issues,
                        deferredResolvedEvents);
                }

                remainingSeconds -= secondsForCycle;
                processedCycles++;
                CopyExecutionState(cycleExecution, execution);

                if (cycleOutcome == WorkCycleOutcome.DangerBoundaryReached)
                {
                    return FinishWorkAdvance(
                        true,
                        WorkAdvanceStopReason.DangerBoundaryReached,
                        request.ExecutionId,
                        processedCycles,
                        availableSeconds,
                        remainingSeconds,
                        execution,
                        issues,
                        deferredResolvedEvents);
                }
                if (cycleOutcome == WorkCycleOutcome.InsufficientFatigue)
                {
                    return FinishWorkAdvance(
                        true,
                        WorkAdvanceStopReason.InsufficientFatigue,
                        request.ExecutionId,
                        processedCycles,
                        availableSeconds,
                        remainingSeconds,
                        execution,
                        issues,
                        deferredResolvedEvents);
                }
                if (execution.status != CoreActivityRuntimeStatus.Running ||
                    execution.completedCycles >= execution.plannedCycles)
                {
                    return FinishWorkAdvance(
                        true,
                        WorkAdvanceStopReason.PlanCompleted,
                        request.ExecutionId,
                        processedCycles,
                        availableSeconds,
                        remainingSeconds,
                        execution,
                        issues,
                        deferredResolvedEvents);
                }
            }

            return FinishWorkAdvance(
                true,
                execution.completedCycles >= execution.plannedCycles
                    ? WorkAdvanceStopReason.PlanCompleted
                    : WorkAdvanceStopReason.IntervalExhausted,
                request.ExecutionId,
                processedCycles,
                availableSeconds,
                remainingSeconds,
                execution,
                issues,
                deferredResolvedEvents);
        }

        internal ConstructionAdvanceResult AdvanceConstructionCore(ConstructionAdvanceRequest request)
        {
            var issues = new List<ActivityRequirementIssue>();
            var deferredEvents = new List<ActivityRuntimeEvent>();
            var deferredResolvedEvents = new List<PendingResultDeferredResolvedEvent>();
            var executionId = request?.ExecutionId;
            var availableSeconds = request == null ? 0L : Math.Max(0L, request.AvailableSeconds);

            if (request == null || string.IsNullOrWhiteSpace(request.ExecutionId) ||
                request.AvailableSeconds < 0L || request.OperationLimit <= 0)
            {
                AddIssue(issues, string.Empty, "ConstructionAdvanceRequest", executionId, 0,
                    request?.AvailableSeconds ?? 0L, true, false,
                    "Construction advance requires executionId, non-negative availableSeconds, and a positive operationLimit.");
                return FinishConstructionAdvance(false, ConstructionAdvanceStopReason.InvalidRequest,
                    executionId, availableSeconds, availableSeconds, 0f, null, issues,
                    deferredEvents, deferredResolvedEvents);
            }

            var execution = GetExecution(request.ExecutionId);
            if (execution == null)
            {
                AddIssue(issues, string.Empty, "ActivityExecution", request.ExecutionId, 1, 0, false, false,
                    $"Activity execution '{request.ExecutionId}' was not found.");
                return FinishConstructionAdvance(false, ConstructionAdvanceStopReason.ExecutionNotFound,
                    request.ExecutionId, availableSeconds, availableSeconds, 0f, null, issues,
                    deferredEvents, deferredResolvedEvents);
            }
            if (execution.status != CoreActivityRuntimeStatus.Running)
            {
                AddIssue(issues, execution.activityId, "ActivityExecution", execution.executionId, 1,
                    (long)execution.status, false, false,
                    $"Activity execution '{execution.executionId}' is not running.");
                return FinishConstructionAdvance(false, ConstructionAdvanceStopReason.ExecutionNotRunning,
                    request.ExecutionId, availableSeconds, availableSeconds, 0f, execution, issues,
                    deferredEvents, deferredResolvedEvents);
            }
            if (!string.Equals(execution.runtimeKind, RuntimeKindBuild, StringComparison.Ordinal))
            {
                AddIssue(issues, execution.activityId, "NotConstructionExecution", execution.executionId, 1, 0,
                    false, false, $"Activity execution '{execution.executionId}' is not a construction execution.");
                return FinishConstructionAdvance(false, ConstructionAdvanceStopReason.NotConstructionExecution,
                    request.ExecutionId, availableSeconds, availableSeconds, 0f, execution, issues,
                    deferredEvents, deferredResolvedEvents);
            }
            if (!RuntimeConfigs.Buildings.TryGetBuildAction(execution.activityId, out var action))
            {
                AddIssue(issues, execution.activityId, "BuildAction", execution.activityId, 1, 0, true, false,
                    "Saved build action is no longer configured.");
                return FinishConstructionAdvance(false, ConstructionAdvanceStopReason.ValidationFailed,
                    request.ExecutionId, availableSeconds, availableSeconds, 0f, execution, issues,
                    deferredEvents, deferredResolvedEvents);
            }
            if (!ValidateConstructionAdvanceExecution(execution, action, issues))
            {
                return FinishConstructionAdvance(false, ConstructionAdvanceStopReason.ValidationFailed,
                    request.ExecutionId, availableSeconds, availableSeconds, 0f, execution, issues,
                    deferredEvents, deferredResolvedEvents);
            }

            var checkpoint = _activityState.CaptureCheckpoint();
            var draft = CloneExecution(execution);
            var startingPoints = draft.accumulatedBuildPoints;
            var initialWholeSeconds = (long)Math.Floor(draft.elapsedSeconds);
            var fractionalSeconds = draft.elapsedSeconds - initialWholeSeconds;
            var availableOperations = initialWholeSeconds > long.MaxValue - availableSeconds
                ? long.MaxValue
                : initialWholeSeconds + availableSeconds;
            var progressOutcome = ApplyBuildOperations(
                draft,
                action,
                availableOperations,
                request.OperationLimit,
                out var processedSeconds,
                out var formulaFailure);

            if (progressOutcome == BuildProgressOutcome.FormulaFailed)
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, execution.activityId, formulaFailure.code, action.buildFormulaId, 1, 0, true,
                    false, formulaFailure.message);
                return FinishConstructionAdvance(false, ConstructionAdvanceStopReason.RuntimeError,
                    request.ExecutionId, availableSeconds, availableSeconds, 0f, execution, issues,
                    deferredEvents, deferredResolvedEvents);
            }

            var processedExistingSeconds = Math.Min(initialWholeSeconds, processedSeconds);
            var processedAvailableSeconds = Math.Min(
                availableSeconds,
                Math.Max(0L, processedSeconds - processedExistingSeconds));
            var remainingSeconds = progressOutcome == BuildProgressOutcome.IntervalExhausted
                ? 0L
                : availableSeconds - processedAvailableSeconds;
            var unprocessedExistingSeconds = initialWholeSeconds - processedExistingSeconds;
            draft.elapsedSeconds = (float)(unprocessedExistingSeconds + fractionalSeconds);

            if (progressOutcome == BuildProgressOutcome.Completed)
            {
                var changed = false;
                CompleteBuildCore(draft, action, issues, deferredEvents, ref changed,
                    BuildCompletionOptions.OuterTransaction, deferredResolvedEvents);
                if (HasBlockingIssues(issues))
                {
                    _activityState.RestoreCheckpoint(checkpoint);
                    return FinishConstructionAdvance(false, ConstructionAdvanceStopReason.RuntimeError,
                        request.ExecutionId, availableSeconds, availableSeconds, 0f, execution, issues,
                        deferredEvents, deferredResolvedEvents);
                }
                return FinishConstructionAdvance(true, ConstructionAdvanceStopReason.ConstructionCompleted,
                    request.ExecutionId, availableSeconds, remainingSeconds,
                    Math.Max(0f, action.buildPointsRequired - startingPoints), draft, issues,
                    deferredEvents, deferredResolvedEvents);
            }

            if (!UpdateExecution(draft))
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, execution.activityId, "ActivityExecution", execution.executionId, 1, 0, true,
                    false, "Failed to persist construction progress in the outer transaction.");
                return FinishConstructionAdvance(false, ConstructionAdvanceStopReason.RuntimeError,
                    request.ExecutionId, availableSeconds, availableSeconds, 0f, execution, issues,
                    deferredEvents, deferredResolvedEvents);
            }

            return FinishConstructionAdvance(true,
                progressOutcome == BuildProgressOutcome.ProcessingLimitReached
                    ? ConstructionAdvanceStopReason.ProcessingLimitReached
                    : ConstructionAdvanceStopReason.IntervalExhausted,
                request.ExecutionId, availableSeconds, remainingSeconds,
                Math.Max(0f, draft.accumulatedBuildPoints - startingPoints), draft, issues,
                deferredEvents, deferredResolvedEvents);
        }

        public ActivityTickResult Tick(float deltaTime)
        {
            var issues = new List<ActivityRequirementIssue>();
            var rewards = new List<ActivityRewardResult>();
            var events = new List<ActivityRuntimeEvent>();
            var result = new ActivityTickResult { deltaTime = deltaTime };
            var changed = false;

            if (deltaTime < 0f)
            {
                AddIssue(issues, string.Empty, "TickDeltaTime", string.Empty, 0, 0, true, false, "ActivityRuntimeService.Tick requires non-negative deltaTime.");
                return FinishTick(result, issues, rewards, events, changed);
            }

            foreach (var execution in GetExecutions())
            {
                if (execution == null || execution.status != CoreActivityRuntimeStatus.Running)
                    continue;
                result.processedExecutions++;

                if (string.Equals(execution.runtimeKind, RuntimeKindBuild, StringComparison.Ordinal))
                {
                    ProcessBuildTick(execution, deltaTime, issues, events, result, ref changed);
                    continue;
                }

                if (!TryGetRuntimeInfo(execution.activityId, issues, out var info))
                    continue;
                if (string.Equals(execution.runtimeKind, RuntimeKindWork, StringComparison.Ordinal) || IsWork(info))
                    ProcessWorkTick(execution, info, deltaTime, issues, rewards, result, ref changed);
                else
                    ProcessStandardTick(execution, info, deltaTime, issues, rewards, result, ref changed);
            }

            return FinishTick(result, issues, rewards, events, changed);
        }

        public ActivityCompleteResult Complete(string executionId)
        {
            var issues = new List<ActivityRequirementIssue>();
            var rewards = new List<ActivityRewardResult>();
            var events = new List<ActivityRuntimeEvent>();
            var result = new ActivityCompleteResult { executionId = executionId };
            var changed = false;
            var execution = GetExecution(executionId);
            if (execution == null || execution.status != CoreActivityRuntimeStatus.Running)
            {
                AddIssue(issues, string.Empty, "ActivityExecution", executionId, 1, 0, false, false, $"Activity execution '{executionId}' is not running.");
                return FinishComplete(result, issues, rewards, events, changed);
            }

            if (string.Equals(execution.runtimeKind, RuntimeKindBuild, StringComparison.Ordinal))
            {
                if (!RuntimeConfigs.Buildings.TryGetBuildAction(execution.activityId, out var action))
                    AddIssue(issues, execution.activityId, "BuildAction", execution.activityId, 1, 0, true, false, "Saved build action is no longer configured.");
                else
                {
                    execution.accumulatedBuildPoints = action.buildPointsRequired;
                    CompleteBuild(execution, action, issues, events, ref changed);
                }
                return FinishComplete(result, issues, rewards, events, changed);
            }

            if (!TryGetRuntimeInfo(execution.activityId, issues, out var info))
                return FinishComplete(result, issues, rewards, events, changed);
            execution.elapsedSeconds = info.durationSeconds;
            if (string.Equals(execution.runtimeKind, RuntimeKindWork, StringComparison.Ordinal) || IsWork(info))
                ProcessWorkTick(execution, info, 0f, issues, rewards, new ActivityTickResult(), ref changed);
            else if (info.isRepeatable)
                ProcessLegacyRepeatableTick(execution, info, issues, rewards, new ActivityTickResult(), ref changed);
            else
                ProcessOneShotCompletion(execution, issues, rewards, ref changed);
            return FinishComplete(result, issues, rewards, events, changed);
        }

        public ActivityCompleteResult CompleteCurrent()
        {
            var executions = GetExecutions();
            return Complete(executions.Length == 0 ? null : executions[0].executionId);
        }

        public ActivityCancelResult StopWork(string executionId)
        {
            var issues = new List<ActivityRequirementIssue>();
            var result = new ActivityCancelResult { executionId = executionId };
            var execution = GetExecution(executionId);
            if (execution == null || !string.Equals(execution.runtimeKind, RuntimeKindWork, StringComparison.Ordinal))
            {
                AddIssue(issues, string.Empty, "WorkExecution", executionId, 1, 0, false, false, $"Work execution '{executionId}' does not exist.");
                return FinishCancel(result, issues, new List<ActivityRuntimeEvent>(), false, false);
            }
            if (execution.status == CoreActivityRuntimeStatus.ResultPending)
                return FinishCancel(result, issues, new List<ActivityRuntimeEvent>(), false, true);

            var checkpoint = _activityState.CaptureCheckpoint();
            execution.elapsedSeconds = 0f;
            execution.currentCycleFatiguePaid = false;
            execution.endReason = EndReasonManualStop;
            execution.stagedRewards = Array.Empty<ActivityStagedRewardSaveData>();

            if (execution.completedCycles == 0 && string.IsNullOrWhiteSpace(execution.pendingResultId))
            {
                if (!RemoveExecution(execution.executionId) || !Save())
                {
                    _activityState.RestoreCheckpoint(checkpoint);
                    AddIssue(issues, execution.activityId, "StopFailed", execution.executionId, 1, 0, true, false, "Failed to stop empty work execution.");
                    return FinishCancel(result, issues, new List<ActivityRuntimeEvent>(), false, false);
                }
                return FinishCancel(result, issues, new List<ActivityRuntimeEvent>(), true, true);
            }

            if (!UpdateExecution(execution))
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, execution.activityId, "StopFailed", execution.executionId, 1, 0, true, false, "Failed to persist stopped work execution.");
                return FinishCancel(result, issues, new List<ActivityRuntimeEvent>(), false, false);
            }
            var formation = _activityState.PendingResults.CreateOrAppend(
                $"activity:{execution.executionId}:stop",
                BuildPendingDraft(execution, Array.Empty<ActivityStagedRewardSaveData>()),
                true,
                GetPendingResultRevision(execution));
            if (!formation.Success)
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, execution.activityId, "PendingResult", execution.executionId, 1, 0, true, false, formation.Message ?? "Failed to expose stopped work result.");
                return FinishCancel(result, issues, new List<ActivityRuntimeEvent>(), false, false);
            }
            return FinishCancel(result, issues, new List<ActivityRuntimeEvent>(), true, true);
        }

        public ActivityCancelResult PauseConstruction(string executionId)
        {
            var issues = new List<ActivityRequirementIssue>();
            var result = new ActivityCancelResult { executionId = executionId };
            var execution = GetExecution(executionId);
            if (execution == null || !string.Equals(execution.runtimeKind, RuntimeKindBuild, StringComparison.Ordinal))
            {
                AddIssue(issues, string.Empty, "ConstructionExecution", executionId, 1, 0, false, false, $"Construction execution '{executionId}' does not exist.");
                return FinishCancel(result, issues, new List<ActivityRuntimeEvent>(), false, false);
            }
            if (execution.status == CoreActivityRuntimeStatus.Paused)
                return FinishCancel(result, issues, new List<ActivityRuntimeEvent>(), false, true);
            if (execution.status != CoreActivityRuntimeStatus.Running)
            {
                AddIssue(issues, execution.activityId, "ConstructionState", execution.executionId, 1, 0, false, false, "Only Running construction can be paused.");
                return FinishCancel(result, issues, new List<ActivityRuntimeEvent>(), false, false);
            }

            var checkpoint = _activityState.CaptureCheckpoint();
            execution.heroId = null;
            execution.status = CoreActivityRuntimeStatus.Paused;
            var changed = UpdateExecution(execution) && Save();
            if (!changed)
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, execution.activityId, "PauseFailed", execution.executionId, 1, 0, true, false, "Failed to pause construction.");
            }
            return FinishCancel(result, issues, new List<ActivityRuntimeEvent>(), changed, changed);
        }

        public ActivityStartResult ResumeConstruction(string executionId, string heroId)
        {
            var execution = GetExecution(executionId);
            var request = new ActivityStartRequest { activityId = execution?.activityId, heroId = heroId };
            var result = NewStartResult(request);
            result.executionId = executionId;
            var issues = new List<ActivityRequirementIssue>();
            if (execution == null || !string.Equals(execution.runtimeKind, RuntimeKindBuild, StringComparison.Ordinal))
            {
                AddIssue(issues, string.Empty, "ConstructionExecution", executionId, 1, 0, false, false, $"Construction execution '{executionId}' does not exist.");
                return FinishStart(result, issues, false);
            }
            if (execution.status != CoreActivityRuntimeStatus.Paused)
            {
                AddIssue(issues, execution.activityId, "ConstructionState", execution.executionId, 1, 0, false, false, "Only Paused construction can be resumed.");
                return FinishStart(result, issues, false);
            }
            if (!string.IsNullOrWhiteSpace(execution.completionPhase))
            {
                AddIssue(issues, execution.activityId, "ConstructionCompletionPending", execution.executionId, 1, 0, false, false, "Construction completion is pending and cannot be resumed.");
                return FinishStart(result, issues, false);
            }
            if (!RuntimeConfigs.Buildings.TryGetBuildAction(execution.activityId, out var action))
            {
                AddIssue(issues, execution.activityId, "BuildAction", execution.activityId, 1, 0, true, false, "Saved build action is no longer configured.");
                return FinishStart(result, issues, false);
            }
            ValidateHeroStart(execution.activityId, heroId, executionId, issues);
            ValidateActiveHeroLimit(heroId, issues, execution.activityId);
            if (_activityState.GetHeroFatigue(heroId) < action.fatigueCost)
                AddIssue(issues, execution.activityId, "InsufficientFatigue", heroId, action.fatigueCost, _activityState.GetHeroFatigue(heroId), false, false, "Hero cannot pay construction assignment fatigue.");
            if (HasBlockingIssues(issues))
                return FinishStart(result, issues, false);

            var checkpoint = _activityState.CaptureCheckpoint();
            if (action.fatigueCost > 0 && !_activityState.SpendHeroFatigue(heroId, action.fatigueCost))
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, execution.activityId, "InsufficientFatigue", heroId, action.fatigueCost, _activityState.GetHeroFatigue(heroId), false, false, "Failed to spend construction assignment fatigue.");
                return FinishStart(result, issues, false);
            }
            execution.heroId = heroId;
            execution.status = CoreActivityRuntimeStatus.Running;
            execution.startedAtUnixSeconds = UnixNow();
            if (!UpdateExecution(execution) || !Save())
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, execution.activityId, "ResumeFailed", execution.executionId, 1, 0, true, false, "Failed to resume construction.");
                return FinishStart(result, issues, false);
            }
            result.context = ToContext(execution);
            return FinishStart(result, issues, true);
        }

        public ActivityCancelResult Cancel(string executionId)
        {
            var execution = GetExecution(executionId);
            if (execution != null && string.Equals(execution.runtimeKind, RuntimeKindWork, StringComparison.Ordinal))
                return StopWork(executionId);
            if (execution != null && string.Equals(execution.runtimeKind, RuntimeKindBuild, StringComparison.Ordinal))
                return PauseConstruction(executionId);

            var issues = new List<ActivityRequirementIssue>();
            var result = new ActivityCancelResult { executionId = executionId };
            if (execution == null)
            {
                AddIssue(issues, string.Empty, "ActivityExecution", executionId, 1, 0, false, false, $"Activity execution '{executionId}' is not running.");
                return FinishCancel(result, issues, new List<ActivityRuntimeEvent>(), false, false);
            }
            if (!string.IsNullOrWhiteSpace(execution.pendingResultId))
            {
                execution.status = CoreActivityRuntimeStatus.ResultPending;
                var pendingChanged = UpdateExecution(execution);
                return FinishCancel(result, issues, new List<ActivityRuntimeEvent>(), pendingChanged, pendingChanged);
            }
            var changed = RemoveExecution(execution.executionId);
            return FinishCancel(result, issues, new List<ActivityRuntimeEvent>(), changed, changed);
        }

        public LinkedCombatStartRequestSaveData[] GetPendingLinkedCombatStarts()
        {
            var requests = new List<LinkedCombatStartRequestSaveData>();
            foreach (var execution in GetExecutions())
            {
                if (execution?.linkedCombat != null && !execution.linkedCombat.resolved)
                    requests.Add(execution.linkedCombat);
            }
            return requests.ToArray();
        }

        public LinkedCombatGatewayResult BindLinkedCombatExecution(string requestId, string combatExecutionId)
        {
            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(combatExecutionId))
                return GatewayFailure("InvalidBinding", "requestId and combatExecutionId are required.");
            var execution = FindLinkedExecution(requestId);
            if (execution == null)
                return GatewayFailure("RequestNotFound", $"Linked combat request '{requestId}' was not found.");
            if (!string.IsNullOrWhiteSpace(execution.linkedCombat.combatExecutionId))
            {
                if (!string.Equals(execution.linkedCombat.combatExecutionId, combatExecutionId, StringComparison.Ordinal))
                    return GatewayFailure("BindingConflict", "Linked combat request is already bound to another execution.");
                return GatewaySuccess(execution, true, null);
            }
            var checkpoint = _activityState.CaptureCheckpoint();
            execution.linkedCombat.combatExecutionId = combatExecutionId;
            if (!UpdateExecution(execution) || !Save())
            {
                _activityState.RestoreCheckpoint(checkpoint);
                return GatewayFailure("SaveFailed", "Failed to persist linked combat binding.");
            }
            return GatewaySuccess(execution, false, null);
        }

        public LinkedCombatGatewayResult ResolveLinkedCombatExecution(string requestId, string combatExecutionId)
        {
            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(combatExecutionId))
                return GatewayFailure("InvalidResolution", "requestId and combatExecutionId are required.");
            var receiptAggregateId = LinkedCombatResolutionReceiptAggregateId(requestId);
            if (_activityState.TryGetOperationReceipt(receiptAggregateId, "resolve", out var receipt))
            {
                if (!string.Equals(receipt.fingerprint, combatExecutionId, StringComparison.Ordinal))
                    return GatewayFailure("CombatExecutionMismatch", "Resolved combat execution does not match the linked binding.");
                return new LinkedCombatGatewayResult { success = receipt.success, replayed = true, code = receipt.code, snapshot = GetSnapshot() };
            }
            var execution = FindLinkedExecution(requestId);
            if (execution == null)
                return GatewayFailure("RequestNotFound", $"Linked combat request '{requestId}' was not found.");
            if (string.IsNullOrWhiteSpace(execution.linkedCombat.combatExecutionId))
                return GatewayFailure("CombatNotBound", "Linked combat request must be bound before it can be resolved.");
            if (!string.Equals(execution.linkedCombat.combatExecutionId, combatExecutionId, StringComparison.Ordinal))
                return GatewayFailure("CombatExecutionMismatch", "Resolved combat execution does not match the linked binding.");
            if (execution.linkedCombat.resolved)
            {
                if (execution.activityBagResolved)
                    return TryFinalizeLinkedCombatCompletion(execution, combatExecutionId, false, true);
                return GatewaySuccess(execution, true, null);
            }

            if (execution.activityBagResolved)
                return TryFinalizeLinkedCombatCompletion(execution, combatExecutionId, true, false);

            var checkpoint = _activityState.CaptureCheckpoint();
            execution.linkedCombat.resolved = true;
            if (!UpdateExecution(execution) || !Save())
            {
                _activityState.RestoreCheckpoint(checkpoint);
                return GatewayFailure("SaveFailed", "Failed to persist linked combat resolution.");
            }
            return GatewaySuccess(execution, false, null);
        }

        public ActivityRuntimeSnapshot GetSnapshot()
        {
            var executions = GetExecutions();
            var snapshots = new ActivityExecutionSnapshot[executions.Length];
            for (var i = 0; i < executions.Length; i++)
                snapshots[i] = ToSnapshot(executions[i]);
            return new ActivityRuntimeSnapshot { executions = snapshots };
        }

        public HeroActivityState GetHeroActivityState(string heroId) => new HeroActivityState
        {
            heroId = heroId,
            isBusy = _activityState.IsHeroBusy(heroId),
            currentActivityExecutionId = _activityState.GetHeroCurrentActivityExecutionId(heroId)
        };

        public int GetActiveHeroLimit() => ActiveHeroLimitResolver.GetCurrentLimit(_activityState);

        public static bool RollDanger(float finalRiskPercent, IActivityRandom random, out int roll)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            roll = random.RangeInclusive(1, 100);
            return roll <= finalRiskPercent;
        }

        public static bool TryGetRuntimeInfo(string activityId, out ActivityRuntimeInfo info)
        {
            return TryGetRuntimeInfo(activityId, new List<ActivityRequirementIssue>(), out info);
        }

        private ActivityStartResult StartWork(ActivityStartRequest request, ActivityRuntimeInfo info)
        {
            var result = NewStartResult(request);
            var issues = new List<ActivityRequirementIssue>();
            if (!request.plannedCycleCount.HasValue)
            {
                AddIssue(issues, request.activityId, "CycleCountRequired", request.activityId, 1, 0, false, false, "Work start requires plannedCycleCount.");
                return FinishStart(result, issues, false);
            }
            var descriptorResult = GetWorkDescriptor(request.activityId, request.heroId, request.plannedCycleCount.Value);
            issues.AddRange(descriptorResult.issues);
            if (!descriptorResult.success)
                return FinishStart(result, issues, false);
            var descriptor = descriptorResult.descriptor;
            if (descriptor.maxCycleCount == 0)
                AddIssue(issues, request.activityId, "InsufficientFatigue", request.heroId, info.activity.fatigueCost, _activityState.GetHeroFatigue(request.heroId), false, false, "Hero cannot pay one full work cycle.");
            else if (request.plannedCycleCount.Value < 1 || request.plannedCycleCount.Value > descriptor.maxCycleCount)
                AddIssue(issues, request.activityId, "CycleCountOutOfRange", request.activityId, descriptor.maxCycleCount, request.plannedCycleCount.Value, false, false, $"plannedCycleCount must be in range 1..{descriptor.maxCycleCount}.");

            ValidateStandardStart(result, info, issues, includeCost: true);
            if (HasBlockingIssues(issues))
                return FinishStart(result, issues, false);

            var checkpoint = _activityState.CaptureCheckpoint();
            result.appliedCost = ActivityResolver.ApplyCost(result.context, _activityState);
            issues.AddRange(result.appliedCost.issues);
            if (!result.appliedCost.success || HasBlockingIssues(issues))
            {
                _activityState.RestoreCheckpoint(checkpoint);
                return FinishStart(result, issues, false);
            }
            var execution = new ActivityExecutionSaveData
            {
                executionId = result.executionId,
                activityId = request.activityId,
                runtimeKind = RuntimeKindWork,
                heroId = request.heroId,
                status = CoreActivityRuntimeStatus.Running,
                elapsedSeconds = 0f,
                completedCycles = 0,
                plannedCycles = request.plannedCycleCount.Value,
                currentCycleFatiguePaid = true,
                cyclePhase = CyclePhaseRunning,
                startedAtUnixSeconds = result.context.startedAtUnixSeconds
            };
            if (!AddExecution(execution) || !Save())
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, request.activityId, "ActivityExecution", result.executionId, 1, 0, true, false, "Failed to create work execution.");
                return FinishStart(result, issues, false);
            }
            return FinishStart(result, issues, true);
        }

        private ActivityStartResult StartStandard(ActivityStartRequest request, ActivityRuntimeInfo info)
        {
            var result = NewStartResult(request);
            var issues = new List<ActivityRequirementIssue>();
            ValidateStandardStart(result, info, issues, includeCost: true);
            if (HasBlockingIssues(issues))
                return FinishStart(result, issues, false);

            var checkpoint = _activityState.CaptureCheckpoint();
            result.appliedCost = ActivityResolver.ApplyCost(result.context, _activityState);
            issues.AddRange(result.appliedCost.issues);
            if (!result.appliedCost.success || HasBlockingIssues(issues))
            {
                _activityState.RestoreCheckpoint(checkpoint);
                return FinishStart(result, issues, false);
            }
            var execution = new ActivityExecutionSaveData
            {
                executionId = result.executionId,
                activityId = request.activityId,
                runtimeKind = RuntimeKindActivity,
                heroId = request.heroId,
                status = CoreActivityRuntimeStatus.Running,
                startedAtUnixSeconds = result.context.startedAtUnixSeconds
            };
            if (!AddExecution(execution) || !Save())
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, request.activityId, "ActivityExecution", result.executionId, 1, 0, true, false, "Failed to create activity execution.");
                return FinishStart(result, issues, false);
            }
            return FinishStart(result, issues, true);
        }

        private ActivityStartResult StartBuild(ActivityStartRequest request, BuildActionConfigDto action)
        {
            var result = NewStartResult(request);
            var issues = new List<ActivityRequirementIssue>();
            var existing = FindConstruction(action);
            if (existing != null)
            {
                var code = !string.IsNullOrWhiteSpace(existing.completionPhase)
                    ? "ConstructionCompletionPending"
                    : existing.status == CoreActivityRuntimeStatus.Paused
                        ? "ConstructionResumeRequired"
                        : existing.status == CoreActivityRuntimeStatus.ResultPending
                            ? "ConstructionResultPending"
                            : "ConstructionAlreadyRunning";
                AddIssue(issues, request.activityId, code, existing.executionId, 1, 1, false, false, $"Construction '{action.targetBuildingId}:{action.targetLevel}' already has unfinished execution '{existing.executionId}'.");
                return FinishStart(result, issues, false);
            }
            ValidateHeroStart(request.activityId, request.heroId, result.executionId, issues);
            ValidateActiveHeroLimit(request.heroId, issues, request.activityId);
            ValidateBuildRequirements(action, request.heroId, result.executionId, issues);
            if (HasBlockingIssues(issues))
                return FinishStart(result, issues, false);

            var checkpoint = _activityState.CaptureCheckpoint();
            var context = new StorageActionContext(StorageContextType.ActivityExecution, result.executionId);
            foreach (var material in action.materials ?? Array.Empty<MaterialCostDto>())
            {
                var consumed = _activityState.Storage.Consume(
                    $"build:{result.executionId}:materials:{material.id}",
                    _activityState.Storage.GetSnapshot().Revision,
                    material.id,
                    material.count,
                    context);
                if (!consumed.Success)
                {
                    _activityState.RestoreCheckpoint(checkpoint);
                    AddIssue(issues, request.activityId, "BuildMaterials", material.id, material.count, _activityState.GetItem(material.id), true, false, consumed.Message ?? "Failed to consume build materials.");
                    return FinishStart(result, issues, false);
                }
            }
            if (action.fatigueCost > 0 && !_activityState.SpendHeroFatigue(request.heroId, action.fatigueCost))
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, request.activityId, "InsufficientFatigue", request.heroId, action.fatigueCost, _activityState.GetHeroFatigue(request.heroId), false, false, "Failed to spend construction assignment fatigue.");
                return FinishStart(result, issues, false);
            }
            var execution = new ActivityExecutionSaveData
            {
                executionId = result.executionId,
                activityId = action.id,
                runtimeKind = RuntimeKindBuild,
                heroId = request.heroId,
                status = CoreActivityRuntimeStatus.Running,
                materialsPaid = true,
                accumulatedBuildPoints = 0f,
                startedAtUnixSeconds = result.context.startedAtUnixSeconds
            };
            if (!AddExecution(execution) || !Save())
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, request.activityId, "ActivityExecution", result.executionId, 1, 0, true, false, "Failed to create construction execution.");
                return FinishStart(result, issues, false);
            }
            return FinishStart(result, issues, true);
        }

        private void ProcessWorkTick(
            ActivityExecutionSaveData execution,
            ActivityRuntimeInfo info,
            float deltaTime,
            List<ActivityRequirementIssue> issues,
            List<ActivityRewardResult> rewards,
            ActivityTickResult tickResult,
            ref bool changed)
        {
            if (string.Equals(execution.cyclePhase, CyclePhaseResultStaged, StringComparison.Ordinal))
            {
                if (FinalizeStagedWorkCycle(execution, info, issues, ref changed, WorkCycleOptions.Online) != WorkCycleOutcome.Completed ||
                    execution.status != CoreActivityRuntimeStatus.Running)
                    return;
            }
            execution.elapsedSeconds += deltaTime;
            changed = true;
            var cycles = 0;
            var aborted = false;
            while (execution.elapsedSeconds >= info.durationSeconds && cycles < MaxCyclesPerTick &&
                   execution.status == CoreActivityRuntimeStatus.Running && execution.completedCycles < execution.plannedCycles)
            {
                if (CompleteWorkCycle(execution, info, issues, rewards, ref changed, WorkCycleOptions.Online) != WorkCycleOutcome.Completed)
                {
                    aborted = true;
                    break;
                }
                cycles++;
                tickResult.processedCycles++;
            }
            if (aborted)
                return;
            if (execution.status == CoreActivityRuntimeStatus.Running && execution.elapsedSeconds >= info.durationSeconds && cycles >= MaxCyclesPerTick)
            {
                tickResult.cycleLimitReached = true;
                AddIssue(issues, execution.activityId, "TickCycleLimitReached", execution.executionId, MaxCyclesPerTick, cycles, false, false, $"Processed maximum {MaxCyclesPerTick} cycles for execution '{execution.executionId}'.");
            }
            if (execution.status == CoreActivityRuntimeStatus.Running)
                changed |= UpdateExecution(execution);
        }

        private WorkCycleOutcome CompleteWorkCycle(
            ActivityExecutionSaveData execution,
            ActivityRuntimeInfo info,
            List<ActivityRequirementIssue> issues,
            List<ActivityRewardResult> rewards,
            ref bool changed,
            WorkCycleOptions options,
            List<PendingResultDeferredResolvedEvent> deferredResolvedEvents = null)
        {
            if (string.Equals(execution.cyclePhase, CyclePhaseResultStaged, StringComparison.Ordinal))
                return FinalizeStagedWorkCycle(execution, info, issues, ref changed, options, deferredResolvedEvents);

            if (!ValidateWorkCyclePreparation(execution, info, issues))
                return WorkCycleOutcome.ValidationFailed;

            var checkpoint = _activityState.CaptureCheckpoint();
            var transactionalRandom = _random as ITransactionalActivityRandom;
            var randomCheckpoint = transactionalRandom?.CaptureState();
            var draft = CloneExecution(execution);
            var reward = ActivityRewardResolver.PreparePendingRewards(ToContext(draft), GrantMoment.OnCycle, _activityState, _random);
            rewards.Add(reward);
            if (!reward.success)
            {
                RestoreWorkCycleCheckpoint(checkpoint, transactionalRandom, randomCheckpoint);
                issues.AddRange(reward.issues);
                return WorkCycleOutcome.RuntimeError;
            }

            var staged = ToStagedRewards(reward, PendingResultOrigin.ActivityReward);
            draft.cyclePhase = CyclePhaseResultStaged;
            draft.stagedRewards = staged;
            draft.completedCycles++;
            draft.elapsedSeconds = Math.Max(0f, draft.elapsedSeconds - info.durationSeconds);
            draft.currentCycleFatiguePaid = false;
            if (!ApplyWorkHeroEffects(draft, info, staged, issues))
            {
                RestoreWorkCycleCheckpoint(checkpoint, transactionalRandom, randomCheckpoint);
                return WorkCycleOutcome.RuntimeError;
            }

            var danger = EvaluateDanger(draft, info, issues);
            if (danger == DangerOutcome.Failed)
            {
                RestoreWorkCycleCheckpoint(checkpoint, transactionalRandom, randomCheckpoint);
                return WorkCycleOutcome.ValidationFailed;
            }

            if (!UpdateExecution(draft) || (options.SaveStagedBoundary && !Save()))
            {
                RestoreWorkCycleCheckpoint(checkpoint, transactionalRandom, randomCheckpoint);
                AddIssue(
                    issues,
                    draft.activityId,
                    options.SaveStagedBoundary ? "SaveFailed" : "ActivityExecution",
                    draft.executionId,
                    1,
                    0,
                    true,
                    false,
                    options.SaveStagedBoundary
                        ? "Failed to persist staged work-cycle result."
                        : "Failed to stage work-cycle result.");
                return WorkCycleOutcome.RuntimeError;
            }
            changed = true;
            if (danger == DangerOutcome.Triggered && options.StopAtDangerBoundary)
            {
                CopyExecutionState(draft, execution);
                return WorkCycleOutcome.DangerBoundaryReached;
            }

            var outcome = FinalizeStagedWorkCycle(draft, info, issues, ref changed, options, deferredResolvedEvents);
            var stored = GetExecution(draft.executionId);
            CopyExecutionState(stored ?? draft, execution);
            return outcome;
        }

        private void RestoreWorkCycleCheckpoint(
            SaveData checkpoint,
            ITransactionalActivityRandom transactionalRandom,
            ActivityRandomState? randomCheckpoint)
        {
            _activityState.RestoreCheckpoint(checkpoint);
            if (transactionalRandom != null && randomCheckpoint.HasValue)
                transactionalRandom.RestoreState(randomCheckpoint.Value);
        }

        private WorkCycleOutcome FinalizeStagedWorkCycle(
            ActivityExecutionSaveData execution,
            ActivityRuntimeInfo info,
            List<ActivityRequirementIssue> issues,
            ref bool changed,
            WorkCycleOptions options,
            List<PendingResultDeferredResolvedEvent> deferredResolvedEvents = null)
        {
            var checkpoint = _activityState.CaptureCheckpoint();
            var staged = execution.stagedRewards ?? Array.Empty<ActivityStagedRewardSaveData>();
            var dangerTriggered = execution.dangerRollCompleted && execution.dangerRoll <= execution.dangerRiskPercent;
            if (dangerTriggered)
            {
                if (options.StopAtDangerBoundary)
                    return WorkCycleOutcome.DangerBoundaryReached;

                var handoff = PrepareDangerHandoff(
                    execution,
                    info,
                    issues,
                    !options.SaveStagedBoundary,
                    deferredResolvedEvents);
                if (!handoff.Success)
                    return string.Equals(
                        handoff.Code,
                        DangerEncounterPreparationCode.ValidationFailed,
                        StringComparison.Ordinal)
                        ? WorkCycleOutcome.ValidationFailed
                        : WorkCycleOutcome.RuntimeError;
                changed = handoff.RequestCreated || changed;
                return WorkCycleOutcome.Completed;
            }

            var planCompleted = execution.completedCycles >= execution.plannedCycles;
            var fatigueStopped = false;
            if (planCompleted)
                execution.endReason = EndReasonPlanCompleted;
            else
            {
                if (!_activityState.SpendHeroFatigue(execution.heroId, info.activity.fatigueCost))
                {
                    if (!options.FatigueFailureIsStop)
                    {
                        _activityState.RestoreCheckpoint(checkpoint);
                        AddIssue(issues, execution.activityId, "InsufficientFatigue", execution.heroId, info.activity.fatigueCost, _activityState.GetHeroFatigue(execution.heroId), true, false, "Failed to pay the next planned work cycle.");
                        return WorkCycleOutcome.RuntimeError;
                    }
                    fatigueStopped = true;
                    execution.endReason = EndReasonInsufficientFatigue;
                }
                else
                {
                    execution.currentCycleFatiguePaid = true;
                    execution.cyclePhase = CyclePhaseRunning;
                    execution.dangerRollCompleted = false;
                    execution.dangerRiskPercent = 0f;
                    execution.dangerRoll = 0;
                }
            }
            var makeClaimable = planCompleted || fatigueStopped;
            execution.stagedRewards = Array.Empty<ActivityStagedRewardSaveData>();
            if (!UpdateExecution(execution))
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, execution.activityId, "ActivityExecution", execution.executionId, 1, 0, true, false, "Failed to advance work cycle.");
                return WorkCycleOutcome.RuntimeError;
            }

            if (staged.Length == 0 && !makeClaimable)
            {
                if (options.SaveStagedBoundary && !Save())
                {
                    _activityState.RestoreCheckpoint(checkpoint);
                    AddIssue(issues, execution.activityId, "SaveFailed", execution.executionId, 1, 0, true, false, "Failed to save work cycle.");
                    return WorkCycleOutcome.RuntimeError;
                }
                changed = true;
                return fatigueStopped ? WorkCycleOutcome.InsufficientFatigue : WorkCycleOutcome.Completed;
            }
            var pendingOperationId = $"activity:{execution.executionId}:cycle:{execution.completedCycles}";
            var pendingDraft = BuildPendingDraft(execution, staged);
            PendingResultFormationResult formation;
            if (options.SaveStagedBoundary)
            {
                formation = _activityState.PendingResults.CreateOrAppend(
                    pendingOperationId,
                    pendingDraft,
                    makeClaimable,
                    GetPendingResultRevision(execution));
            }
            else if (_activityState.PendingResults is ITransactionalPendingResultService transactionalPendingResults)
            {
                formation = transactionalPendingResults.CreateOrAppendInTransaction(
                    pendingOperationId,
                    pendingDraft,
                    makeClaimable,
                    GetPendingResultRevision(execution));
            }
            else
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, execution.activityId, "PendingResultTransaction", execution.executionId, 1, 0, true, false, "PendingResult service does not support outer transactions.");
                return WorkCycleOutcome.RuntimeError;
            }
            if (!formation.Success)
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, execution.activityId, "PendingResult", execution.executionId, 1, 0, true, false, formation.Message ?? "Failed to append work cycle result.");
                return WorkCycleOutcome.RuntimeError;
            }
            execution.pendingResultId = formation.Result?.resultId ?? execution.pendingResultId;
            if (formation.DeferredResolvedEvent != null)
                deferredResolvedEvents?.Add(formation.DeferredResolvedEvent);
            if (makeClaimable)
                execution.status = CoreActivityRuntimeStatus.ResultPending;
            changed = true;
            return fatigueStopped ? WorkCycleOutcome.InsufficientFatigue : WorkCycleOutcome.Completed;
        }

        private DangerHandoffMutationResult PrepareDangerHandoff(
            ActivityExecutionSaveData execution,
            ActivityRuntimeInfo info,
            List<ActivityRequirementIssue> issues,
            bool useOuterTransaction,
            List<PendingResultDeferredResolvedEvent> deferredResolvedEvents)
        {
            var checkpoint = _activityState.CaptureCheckpoint();
            var encounters = RuntimeConfigs.Activities.GetDangerEncounters(execution?.activityId);
            if (execution == null || info?.activity == null || encounters.Length == 0)
            {
                AddIssue(
                    issues,
                    execution?.activityId,
                    DangerEncounterPreparationCode.ValidationFailed,
                    execution?.activityId,
                    1,
                    0,
                    true,
                    false,
                    "Staged danger result has no encounter descriptor.");
                return new DangerHandoffMutationResult
                {
                    Success = false,
                    Code = DangerEncounterPreparationCode.ValidationFailed
                };
            }

            var encounter = encounters[0];
            if (execution.linkedCombat != null)
            {
                if (!ValidatePreparedDangerHandoff(execution, info, encounter, out var integrityMessage))
                {
                    AddIssue(
                        issues,
                        execution.activityId,
                        DangerEncounterPreparationCode.DataIntegrityFailure,
                        execution.executionId,
                        1,
                        0,
                        true,
                        false,
                        integrityMessage);
                    return new DangerHandoffMutationResult
                    {
                        Success = false,
                        Code = DangerEncounterPreparationCode.DataIntegrityFailure
                    };
                }

                return new DangerHandoffMutationResult
                {
                    Success = true,
                    Code = DangerEncounterPreparationCode.AlreadyPrepared,
                    RequestId = execution.linkedCombat.requestId,
                    PendingResultId = execution.activityBagResolved ? string.Empty : execution.pendingResultId,
                    CombatEntryCount = execution.linkedCombat.loot?.Length ?? 0,
                    NonCombatEntryCount = execution.dangerNonCombatEntryCount,
                    ActivityBagResolved = execution.activityBagResolved,
                    RequestCreated = false,
                    Replayed = true
                };
            }

            if (!string.IsNullOrWhiteSpace(execution.dangerHandoffFingerprint))
            {
                AddIssue(
                    issues,
                    execution.activityId,
                    DangerEncounterPreparationCode.DataIntegrityFailure,
                    execution.executionId,
                    1,
                    0,
                    true,
                    false,
                    "Danger handoff marker exists without its linked request.");
                return new DangerHandoffMutationResult
                {
                    Success = false,
                    Code = DangerEncounterPreparationCode.DataIntegrityFailure
                };
            }

            if (execution.status != CoreActivityRuntimeStatus.Running ||
                !string.Equals(execution.runtimeKind, RuntimeKindWork, StringComparison.Ordinal) ||
                !string.Equals(execution.cyclePhase, CyclePhaseResultStaged, StringComparison.Ordinal) ||
                !IsTriggeredDangerBoundary(execution))
            {
                AddIssue(
                    issues,
                    execution.activityId,
                    DangerEncounterPreparationCode.NotDangerBoundary,
                    execution.executionId,
                    1,
                    0,
                    false,
                    false,
                    "Execution is not a saved triggered Work danger boundary.");
                return new DangerHandoffMutationResult
                {
                    Success = false,
                    Code = DangerEncounterPreparationCode.NotDangerBoundary
                };
            }

            var bagIntegrityMessage = (string)null;
            var occupationValid = HasValidDangerOccupation(execution);
            var activityBagValid = ValidateActivityBagBeforeDangerHandoff(
                execution,
                out bagIntegrityMessage);
            if (!occupationValid || !activityBagValid)
            {
                AddIssue(
                    issues,
                    execution.activityId,
                    DangerEncounterPreparationCode.DataIntegrityFailure,
                    execution.executionId,
                    1,
                    0,
                    true,
                    false,
                    bagIntegrityMessage ?? "Danger boundary no longer owns its hero occupation.");
                return new DangerHandoffMutationResult
                {
                    Success = false,
                    Code = DangerEncounterPreparationCode.DataIntegrityFailure
                };
            }

            var combatLoot = new List<ActivityStagedRewardSaveData>();
            var nonCombat = new List<ActivityStagedRewardSaveData>();
            foreach (var sourceEntry in execution.stagedRewards ?? Array.Empty<ActivityStagedRewardSaveData>())
            {
                if (sourceEntry == null || sourceEntry.quantity <= 0 ||
                    string.IsNullOrWhiteSpace(sourceEntry.rewardType) ||
                    string.IsNullOrWhiteSpace(sourceEntry.targetId))
                {
                    AddIssue(
                        issues,
                        execution.activityId,
                        DangerEncounterPreparationCode.ValidationFailed,
                        execution.executionId,
                        1,
                        0,
                        true,
                        false,
                        "Staged danger rewards contain an invalid entry.");
                    return new DangerHandoffMutationResult
                    {
                        Success = false,
                        Code = DangerEncounterPreparationCode.ValidationFailed
                    };
                }

                var entry = CloneStagedReward(sourceEntry);
                if (IsLootRewardType(entry.rewardType))
                {
                    entry.origin = PendingResultOrigin.ActivityLootInCombat;
                    combatLoot.Add(entry);
                }
                else
                {
                    nonCombat.Add(entry);
                }
            }

            var request = new LinkedCombatStartRequestSaveData
            {
                requestId = BuildLinkedCombatRequestId(execution),
                rootExecutionId = execution.executionId,
                occupationOwnerId = execution.executionId,
                heroId = execution.heroId,
                dangerEncounterId = encounter.dangerEncounterId,
                enemyGroupId = encounter.enemyGroupId,
                combatMode = encounter.combatMode,
                enemyExpTargetId = info.activity.mainSkillId,
                defeatLossRule = encounter.defeatLossRule,
                suppressFatigueCost = true,
                combatExecutionId = string.Empty,
                resolved = false,
                loot = combatLoot.ToArray()
            };
            var fingerprint = BuildDangerHandoffFingerprint(execution, request, nonCombat.Count);
            var draft = CloneExecution(execution);
            draft.endReason = EndReasonDangerTriggered;
            draft.stagedRewards = Array.Empty<ActivityStagedRewardSaveData>();
            draft.linkedCombat = request;
            draft.dangerHandoffFingerprint = fingerprint;
            draft.dangerNonCombatEntryCount = nonCombat.Count;
            draft.activityBagResolved = false;
            if (!UpdateExecution(draft))
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(
                    issues,
                    execution.activityId,
                    DangerEncounterPreparationCode.RuntimeError,
                    execution.executionId,
                    1,
                    0,
                    true,
                    false,
                    "Failed to persist danger handoff source state.");
                return new DangerHandoffMutationResult
                {
                    Success = false,
                    Code = DangerEncounterPreparationCode.RuntimeError
                };
            }

            var pendingDraft = BuildPendingDraft(draft, nonCombat.ToArray());
            pendingDraft.OperationContext = $"danger-handoff|{fingerprint}";
            var operationId = $"activity:{execution.executionId}:danger:{execution.completedCycles}";
            PendingResultFormationResult formation;
            if (useOuterTransaction)
            {
                if (!(_activityState.PendingResults is ITransactionalPendingResultService transactionalPendingResults))
                {
                    _activityState.RestoreCheckpoint(checkpoint);
                    AddIssue(
                        issues,
                        execution.activityId,
                        DangerEncounterPreparationCode.PendingResultFailed,
                        execution.executionId,
                        1,
                        0,
                        true,
                        false,
                        "PendingResult service does not support outer transactions.");
                    return new DangerHandoffMutationResult
                    {
                        Success = false,
                        Code = DangerEncounterPreparationCode.PendingResultFailed
                    };
                }

                formation = transactionalPendingResults.CreateOrAppendInTransaction(
                    operationId,
                    pendingDraft,
                    true,
                    GetPendingResultRevision(execution));
            }
            else
            {
                formation = _activityState.PendingResults.CreateOrAppend(
                    operationId,
                    pendingDraft,
                    true,
                    GetPendingResultRevision(execution));
            }

            if (formation == null || !formation.Success)
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(
                    issues,
                    execution.activityId,
                    DangerEncounterPreparationCode.PendingResultFailed,
                    execution.executionId,
                    1,
                    0,
                    true,
                    false,
                    formation?.Message ?? "Failed to form danger work result.");
                return new DangerHandoffMutationResult
                {
                    Success = false,
                    Code = DangerEncounterPreparationCode.PendingResultFailed
                };
            }

            var stored = GetExecution(execution.executionId);
            var postconditionMessage = (string)null;
            var postconditionValid = stored != null &&
                                     ValidatePreparedDangerHandoff(
                                         stored,
                                         info,
                                         encounter,
                                         out postconditionMessage);
            if (!postconditionValid)
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(
                    issues,
                    execution.activityId,
                    DangerEncounterPreparationCode.DataIntegrityFailure,
                    execution.executionId,
                    1,
                    0,
                    true,
                    false,
                    postconditionMessage ?? "Danger handoff did not reach a valid persisted state.");
                return new DangerHandoffMutationResult
                {
                    Success = false,
                    Code = DangerEncounterPreparationCode.DataIntegrityFailure
                };
            }

            if (formation.DeferredResolvedEvent != null)
                deferredResolvedEvents?.Add(formation.DeferredResolvedEvent);
            CopyExecutionState(stored, execution);
            return new DangerHandoffMutationResult
            {
                Success = true,
                Code = DangerEncounterPreparationCode.PendingEncounterCreated,
                RequestId = stored.linkedCombat.requestId,
                PendingResultId = stored.activityBagResolved ? string.Empty : stored.pendingResultId,
                CombatEntryCount = combatLoot.Count,
                NonCombatEntryCount = nonCombat.Count,
                ActivityBagResolved = stored.activityBagResolved,
                RequestCreated = true,
                Replayed = false
            };
        }

        private bool ValidatePreparedDangerHandoff(
            ActivityExecutionSaveData execution,
            ActivityRuntimeInfo info,
            DangerEncounterConfigDto encounter,
            out string message)
        {
            message = null;
            var request = execution?.linkedCombat;
            if (execution == null || request == null || info?.activity == null || encounter == null ||
                execution.status != CoreActivityRuntimeStatus.ResultPending ||
                !string.Equals(execution.runtimeKind, RuntimeKindWork, StringComparison.Ordinal) ||
                !string.Equals(execution.cyclePhase, CyclePhaseResultStaged, StringComparison.Ordinal) ||
                !string.Equals(execution.endReason, EndReasonDangerTriggered, StringComparison.Ordinal) ||
                !IsTriggeredDangerBoundary(execution) ||
                (execution.stagedRewards?.Length ?? 0) != 0 ||
                execution.dangerNonCombatEntryCount < 0 ||
                string.IsNullOrWhiteSpace(execution.dangerHandoffFingerprint))
            {
                message = "Linked combat is incompatible with the staged danger source state.";
                return false;
            }

            if (!HasValidDangerOccupation(execution) ||
                !string.Equals(request.requestId, BuildLinkedCombatRequestId(execution), StringComparison.Ordinal) ||
                !string.Equals(request.rootExecutionId, execution.executionId, StringComparison.Ordinal) ||
                !string.Equals(request.occupationOwnerId, execution.executionId, StringComparison.Ordinal) ||
                !string.Equals(request.heroId, execution.heroId, StringComparison.Ordinal) ||
                !string.Equals(request.dangerEncounterId, encounter.dangerEncounterId, StringComparison.Ordinal) ||
                !string.Equals(request.enemyGroupId, encounter.enemyGroupId, StringComparison.Ordinal) ||
                !string.Equals(request.combatMode, encounter.combatMode, StringComparison.Ordinal) ||
                !string.Equals(request.enemyExpTargetId, info.activity.mainSkillId, StringComparison.Ordinal) ||
                !string.Equals(request.defeatLossRule, encounter.defeatLossRule, StringComparison.Ordinal) ||
                !request.suppressFatigueCost)
            {
                message = "Linked combat source links, hero, encounter, or combat context do not match the danger boundary.";
                return false;
            }

            foreach (var loot in request.loot ?? Array.Empty<ActivityStagedRewardSaveData>())
            {
                if (loot == null || loot.quantity <= 0 || !IsLootRewardType(loot.rewardType) ||
                    !string.Equals(loot.origin, PendingResultOrigin.ActivityLootInCombat, StringComparison.Ordinal))
                {
                    message = "Linked combat partition loot is invalid.";
                    return false;
                }
            }

            var fingerprint = BuildDangerHandoffFingerprint(
                execution,
                request,
                execution.dangerNonCombatEntryCount);
            if (!string.Equals(execution.dangerHandoffFingerprint, fingerprint, StringComparison.Ordinal))
            {
                message = "Linked combat request or partition loot does not match its saved integrity marker.";
                return false;
            }

            var activityResults = FindActivityPendingResults(execution.executionId);
            if (execution.activityBagResolved)
            {
                if (!string.IsNullOrWhiteSpace(execution.pendingResultId) || activityResults.Count != 0)
                {
                    message = "Resolved Activity Bag still has pending result state.";
                    return false;
                }
            }
            else if (activityResults.Count != 1 || string.IsNullOrWhiteSpace(execution.pendingResultId) ||
                     !string.Equals(activityResults[0].resultId, execution.pendingResultId, StringComparison.Ordinal) ||
                     !string.Equals(activityResults[0].sourceId, execution.activityId, StringComparison.Ordinal) ||
                     !string.Equals(activityResults[0].ownerHeroId, execution.heroId, StringComparison.Ordinal))
            {
                message = "Unresolved Activity Bag is missing or does not match its danger source.";
                return false;
            }

            return true;
        }

        private bool ValidateActivityBagBeforeDangerHandoff(
            ActivityExecutionSaveData execution,
            out string message)
        {
            message = null;
            var activityResults = FindActivityPendingResults(execution.executionId);
            if (activityResults.Count > 1)
            {
                message = "Danger source has multiple Activity Bags.";
                return false;
            }
            if (activityResults.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(execution.pendingResultId))
                {
                    message = "Danger source references a missing Activity Bag.";
                    return false;
                }
                return true;
            }

            var result = activityResults[0];
            if (string.IsNullOrWhiteSpace(execution.pendingResultId) ||
                !string.Equals(execution.pendingResultId, result.resultId, StringComparison.Ordinal) ||
                !string.Equals(execution.activityId, result.sourceId, StringComparison.Ordinal) ||
                !string.Equals(execution.heroId, result.ownerHeroId, StringComparison.Ordinal))
            {
                message = "Existing Activity Bag does not match its danger source.";
                return false;
            }
            return true;
        }

        private List<PendingResultSaveData> FindActivityPendingResults(string executionId)
        {
            var results = new List<PendingResultSaveData>();
            foreach (var result in _activityState.PendingResults.GetAll() ?? Array.Empty<PendingResultSaveData>())
            {
                if (result != null &&
                    string.Equals(result.sourceType, PendingResultSourceType.Activity, StringComparison.Ordinal) &&
                    string.Equals(result.sourceExecutionId, executionId, StringComparison.Ordinal))
                    results.Add(result);
            }
            return results;
        }

        private bool HasValidDangerOccupation(ActivityExecutionSaveData execution) =>
            execution != null && !string.IsNullOrWhiteSpace(execution.heroId) &&
            _activityState.IsHeroBusy(execution.heroId) &&
            string.Equals(
                _activityState.GetHeroCurrentActivityExecutionId(execution.heroId),
                execution.executionId,
                StringComparison.Ordinal);

        private static bool IsTriggeredDangerBoundary(ActivityExecutionSaveData execution) =>
            execution != null && execution.dangerRollCompleted &&
            !float.IsNaN(execution.dangerRiskPercent) &&
            !float.IsInfinity(execution.dangerRiskPercent) &&
            execution.dangerRiskPercent >= 0f && execution.dangerRiskPercent <= 100f &&
            execution.dangerRoll >= 1 && execution.dangerRoll <= 100 &&
            execution.dangerRoll <= execution.dangerRiskPercent;

        private static string BuildLinkedCombatRequestId(ActivityExecutionSaveData execution) =>
            $"linked-combat:{execution.executionId}:{execution.completedCycles}";

        private static string BuildDangerHandoffFingerprint(
            ActivityExecutionSaveData execution,
            LinkedCombatStartRequestSaveData request,
            int nonCombatEntryCount)
        {
            var builder = new StringBuilder();
            AppendFingerprintField(builder, execution.executionId);
            AppendFingerprintField(builder, execution.activityId);
            AppendFingerprintField(builder, execution.runtimeKind);
            AppendFingerprintField(builder, execution.heroId);
            AppendFingerprintField(builder, execution.cyclePhase);
            AppendFingerprintField(builder, execution.completedCycles.ToString(CultureInfo.InvariantCulture));
            AppendFingerprintField(builder, execution.plannedCycles.ToString(CultureInfo.InvariantCulture));
            AppendFingerprintField(builder, execution.startedAtUnixSeconds.ToString(CultureInfo.InvariantCulture));
            AppendFingerprintField(builder, execution.dangerRollCompleted ? "1" : "0");
            AppendFingerprintField(builder, execution.dangerRiskPercent.ToString("R", CultureInfo.InvariantCulture));
            AppendFingerprintField(builder, execution.dangerRoll.ToString(CultureInfo.InvariantCulture));
            AppendFingerprintField(builder, nonCombatEntryCount.ToString(CultureInfo.InvariantCulture));
            AppendFingerprintField(builder, request.requestId);
            AppendFingerprintField(builder, request.rootExecutionId);
            AppendFingerprintField(builder, request.occupationOwnerId);
            AppendFingerprintField(builder, request.heroId);
            AppendFingerprintField(builder, request.dangerEncounterId);
            AppendFingerprintField(builder, request.enemyGroupId);
            AppendFingerprintField(builder, request.combatMode);
            AppendFingerprintField(builder, request.enemyExpTargetId);
            AppendFingerprintField(builder, request.defeatLossRule);
            AppendFingerprintField(builder, request.suppressFatigueCost ? "1" : "0");
            var loot = request.loot ?? Array.Empty<ActivityStagedRewardSaveData>();
            AppendFingerprintField(builder, loot.Length.ToString(CultureInfo.InvariantCulture));
            foreach (var entry in loot)
            {
                AppendFingerprintField(builder, entry?.rewardType);
                AppendFingerprintField(builder, entry?.targetId);
                AppendFingerprintField(builder, (entry?.quantity ?? 0L).ToString(CultureInfo.InvariantCulture));
                AppendFingerprintField(builder, entry?.origin);
                AppendFingerprintField(builder, (entry?.quality ?? 0).ToString(CultureInfo.InvariantCulture));
                AppendFingerprintField(builder, entry?.instanceId);
            }
            return builder.ToString();
        }

        private static void AppendFingerprintField(StringBuilder builder, string value)
        {
            value ??= string.Empty;
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
            builder.Append('|');
        }

        private void ProcessBuildTick(
            ActivityExecutionSaveData execution,
            float deltaTime,
            List<ActivityRequirementIssue> issues,
            List<ActivityRuntimeEvent> events,
            ActivityTickResult tickResult,
            ref bool changed)
        {
            if (TryResumeBuildCompletionLifecycle(execution, issues, events, ref changed))
                return;
            if (!RuntimeConfigs.Buildings.TryGetBuildAction(execution.activityId, out var action))
            {
                AddIssue(issues, execution.activityId, "BuildAction", execution.activityId, 1, 0, true, false, "Saved build action is no longer configured.");
                return;
            }
            var draft = CloneExecution(execution);
            draft.elapsedSeconds += deltaTime;
            var availableOperations = (long)Math.Floor(draft.elapsedSeconds);
            var progressOutcome = ApplyBuildOperations(
                draft,
                action,
                availableOperations,
                MaxCyclesPerTick,
                out var seconds,
                out var formulaFailure);
            if (progressOutcome == BuildProgressOutcome.FormulaFailed)
            {
                AddIssue(issues, execution.activityId, formulaFailure.code, action.buildFormulaId, 1, 0, true,
                    false, formulaFailure.message);
                return;
            }
            draft.elapsedSeconds -= seconds;
            changed = deltaTime > 0f || seconds > 0;
            if (progressOutcome == BuildProgressOutcome.ProcessingLimitReached)
            {
                tickResult.cycleLimitReached = true;
                AddIssue(issues, execution.activityId, "TickCycleLimitReached", execution.executionId, MaxCyclesPerTick, seconds, false, false, "Construction tick processing limit reached.");
            }
            if (progressOutcome == BuildProgressOutcome.Completed)
            {
                CompleteBuild(draft, action, issues, events, ref changed);
                return;
            }
            changed |= UpdateExecution(draft);
        }

        private void CompleteBuild(
            ActivityExecutionSaveData execution,
            BuildActionConfigDto action,
            List<ActivityRequirementIssue> issues,
            List<ActivityRuntimeEvent> events,
            ref bool changed)
        {
            CompleteBuildCore(execution, action, issues, events, ref changed,
                BuildCompletionOptions.Online, null);
        }

        private void CompleteBuildCore(
            ActivityExecutionSaveData execution,
            BuildActionConfigDto action,
            List<ActivityRequirementIssue> issues,
            List<ActivityRuntimeEvent> events,
            ref bool changed,
            BuildCompletionOptions options,
            List<PendingResultDeferredResolvedEvent> deferredResolvedEvents)
        {
            if (TryResumeBuildCompletionLifecycle(execution, issues, events, ref changed, options))
                return;

            var checkpoint = _activityState.CaptureCheckpoint();
            execution.accumulatedBuildPoints = action.buildPointsRequired;
            if (!execution.buildingLevelApplied)
            {
                if (!_activityState.SetBuildingLevel(action.targetBuildingId, action.targetLevel))
                {
                    _activityState.RestoreCheckpoint(checkpoint);
                    AddIssue(issues, execution.activityId, "BuildingLevel", action.targetBuildingId, action.targetLevel, _activityState.GetBuildingLevel(action.targetBuildingId), true, false, "Failed to apply completed building level.");
                    return;
                }
                execution.buildingLevelApplied = true;
            }
            if (!execution.buildingEventPublished && !execution.buildingEventPending)
            {
                execution.buildingEventPending = true;
                execution.completionPhase = CompletionPhaseBuildingEventPending;
            }
            var staged = action.skillExp > 0
                ? new[] { new ActivityStagedRewardSaveData { rewardType = RewardType.SkillExp, targetId = action.skillId, quantity = action.skillExp, origin = PendingResultOrigin.ActivityReward } }
                : Array.Empty<ActivityStagedRewardSaveData>();
            if (staged.Length == 0)
            {
                execution.heroId = null;
                execution.status = CoreActivityRuntimeStatus.Paused;
                if (!UpdateExecution(execution) || (options.SaveInternally && !Save()))
                {
                    _activityState.RestoreCheckpoint(checkpoint);
                    AddIssue(issues, execution.activityId, "BuildCompletion", execution.executionId, 1, 0, true, false, "Failed to persist construction event outbox.");
                    return;
                }
                changed = true;
                TryProcessPendingBuildingEvent(execution.executionId, issues, events, ref changed, options);
                TryFinalizeCompletionReady(execution.executionId, issues, events, ref changed, options);
                return;
            }
            execution.stagedRewards = Array.Empty<ActivityStagedRewardSaveData>();
            if (!UpdateExecution(execution))
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, execution.activityId, "ActivityExecution", execution.executionId, 1, 0, true, false, "Failed to persist construction completion.");
                return;
            }
            var pendingResults = _activityState.PendingResults;
            PendingResultFormationResult formation;
            if (options.UseOuterTransaction)
            {
                var transactional = pendingResults as ITransactionalPendingResultService;
                if (transactional == null)
                {
                    _activityState.RestoreCheckpoint(checkpoint);
                    AddIssue(issues, execution.activityId, "PendingResultTransaction", execution.executionId, 1,
                        0, true, false, "Construction advance requires transaction-aware PendingResult support.");
                    return;
                }
                formation = transactional.CreateOrAppendInTransaction(
                    $"activity:{execution.executionId}:completion",
                    BuildPendingDraft(execution, staged),
                    true,
                    GetPendingResultRevision(execution));
            }
            else
            {
                formation = pendingResults.CreateOrAppend(
                    $"activity:{execution.executionId}:completion",
                    BuildPendingDraft(execution, staged),
                    true,
                    GetPendingResultRevision(execution));
            }
            if (!formation.Success)
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, execution.activityId, "PendingResult", execution.executionId, 1, 0, true, false, formation.Message ?? "Failed to form construction result.");
                return;
            }
            if (formation.DeferredResolvedEvent != null)
                deferredResolvedEvents?.Add(formation.DeferredResolvedEvent);
            TryProcessPendingBuildingEvent(execution.executionId, issues, events, ref changed, options);
            changed = true;
        }

        private void ProcessStandardTick(
            ActivityExecutionSaveData execution,
            ActivityRuntimeInfo info,
            float deltaTime,
            List<ActivityRequirementIssue> issues,
            List<ActivityRewardResult> rewards,
            ActivityTickResult tickResult,
            ref bool changed)
        {
            execution.elapsedSeconds += deltaTime;
            changed = true;
            if (execution.elapsedSeconds < info.durationSeconds)
            {
                changed |= UpdateExecution(execution);
                return;
            }
            if (info.isRepeatable)
                ProcessLegacyRepeatableTick(execution, info, issues, rewards, tickResult, ref changed);
            else
                ProcessOneShotCompletion(execution, issues, rewards, ref changed);
        }

        private void ProcessLegacyRepeatableTick(
            ActivityExecutionSaveData execution,
            ActivityRuntimeInfo info,
            List<ActivityRequirementIssue> issues,
            List<ActivityRewardResult> rewards,
            ActivityTickResult tickResult,
            ref bool changed)
        {
            var cycles = 0;
            while (execution.elapsedSeconds >= info.durationSeconds && cycles < MaxCyclesPerTick)
            {
                var reward = ActivityRewardResolver.PreparePendingRewards(ToContext(execution), GrantMoment.OnCycle, _activityState, _random);
                rewards.Add(reward);
                if (!reward.success)
                {
                    issues.AddRange(reward.issues);
                    break;
                }
                execution.completedCycles++;
                execution.elapsedSeconds -= info.durationSeconds;
                if (!UpdateExecution(execution))
                    break;
                var staged = ToStagedRewards(reward, PendingResultOrigin.ActivityReward);
                if (staged.Length > 0)
                {
                    var formation = _activityState.PendingResults.CreateOrAppend($"activity:{execution.executionId}:cycle:{execution.completedCycles}", BuildPendingDraft(execution, staged), false, GetPendingResultRevision(execution));
                    if (!formation.Success)
                    {
                        AddIssue(issues, execution.activityId, "PendingResult", execution.executionId, 1, 0, true, false, formation.Message ?? "Failed to append activity result.");
                        break;
                    }
                    execution.pendingResultId = formation.Result?.resultId ?? execution.pendingResultId;
                }
                cycles++;
                tickResult.processedCycles++;
                changed = true;
            }
            changed |= UpdateExecution(execution);
        }

        private void ProcessOneShotCompletion(ActivityExecutionSaveData execution, List<ActivityRequirementIssue> issues, List<ActivityRewardResult> rewards, ref bool changed)
        {
            var wasCompleted = _activityState.IsActivityCompleted(execution.activityId);
            var complete = ActivityRewardResolver.PreparePendingRewards(ToContext(execution), GrantMoment.OnComplete, _activityState, _random);
            rewards.Add(complete);
            if (!complete.success)
            {
                issues.AddRange(complete.issues);
                return;
            }
            ActivityRewardResult firstComplete = null;
            if (!wasCompleted)
            {
                firstComplete = ActivityRewardResolver.PreparePendingRewards(ToContext(execution), GrantMoment.OnFirstComplete, _activityState, _random);
                rewards.Add(firstComplete);
                if (!firstComplete.success)
                {
                    issues.AddRange(firstComplete.issues);
                    return;
                }
            }
            var staged = new List<ActivityStagedRewardSaveData>();
            staged.AddRange(ToStagedRewards(complete, PendingResultOrigin.ActivityReward));
            staged.AddRange(ToStagedRewards(firstComplete, PendingResultOrigin.ActivityReward));
            var formation = _activityState.PendingResults.CreateOrAppend($"activity:{execution.executionId}:completion", BuildPendingDraft(execution, staged.ToArray()), true, 0);
            if (!formation.Success)
            {
                AddIssue(issues, execution.activityId, "PendingResult", execution.executionId, 1, 0, true, false, formation.Message ?? "Failed to form ActivityBag.");
                return;
            }
            changed = true;
        }

        private DangerOutcome EvaluateDanger(ActivityExecutionSaveData execution, ActivityRuntimeInfo info, List<ActivityRequirementIssue> issues)
        {
            var encounters = RuntimeConfigs.Activities.GetDangerEncounters(execution.activityId);
            if (encounters.Length == 0)
                return DangerOutcome.None;
            var encounter = encounters[0];
            if (!RuntimeConfigs.Formulas.TryGetFormula(encounter.riskFormulaId, out var formula))
            {
                AddIssue(issues, execution.activityId, "DangerFormula", encounter.riskFormulaId, 1, 0, true, false, "DangerEncounter formula is missing.");
                return DangerOutcome.Failed;
            }
            var context = BuildFormulaContext(execution.heroId, info.activity.mainSkillId, encounter.riskPercent, true, formula);
            var evaluated = _formulas.Evaluate(formula, context);
            if (!evaluated.success)
            {
                AddIssue(issues, execution.activityId, evaluated.code, formula.formulaId, 1, 0, true, false, evaluated.message);
                return DangerOutcome.Failed;
            }
            execution.dangerRollCompleted = true;
            execution.dangerRiskPercent = evaluated.value;
            var triggered = RollDanger(evaluated.value, _random, out var roll);
            execution.dangerRoll = roll;
            return triggered ? DangerOutcome.Triggered : DangerOutcome.Missed;
        }

        private FormulaEvaluationResult EvaluateBuildFormula(BuildActionConfigDto action, string heroId)
        {
            if (!RuntimeConfigs.Formulas.TryGetFormula(action.buildFormulaId, out var formula))
                return new FormulaEvaluationResult { success = false, code = "BuildFormulaMissing", message = $"Build formula '{action.buildFormulaId}' is missing." };
            return _formulas.Evaluate(formula, BuildFormulaContext(heroId, action.skillId, 0f, false, formula));
        }

        private BuildProgressOutcome ApplyBuildOperations(
            ActivityExecutionSaveData execution,
            BuildActionConfigDto action,
            long availableOperations,
            int operationLimit,
            out long processedOperations,
            out FormulaEvaluationResult formulaFailure)
        {
            processedOperations = 0L;
            formulaFailure = null;
            if (execution.accumulatedBuildPoints >= action.buildPointsRequired)
            {
                execution.accumulatedBuildPoints = action.buildPointsRequired;
                return BuildProgressOutcome.Completed;
            }

            while (processedOperations < availableOperations && processedOperations < operationLimit)
            {
                var formula = EvaluateBuildFormula(action, execution.heroId);
                if (!formula.success)
                {
                    formulaFailure = formula;
                    return BuildProgressOutcome.FormulaFailed;
                }
                execution.accumulatedBuildPoints = Math.Min(
                    action.buildPointsRequired,
                    execution.accumulatedBuildPoints + formula.value);
                processedOperations++;
                if (execution.accumulatedBuildPoints >= action.buildPointsRequired)
                    return BuildProgressOutcome.Completed;
            }

            return processedOperations < availableOperations
                ? BuildProgressOutcome.ProcessingLimitReached
                : BuildProgressOutcome.IntervalExhausted;
        }

        private bool ValidateConstructionAdvanceExecution(
            ActivityExecutionSaveData execution,
            BuildActionConfigDto action,
            List<ActivityRequirementIssue> issues)
        {
            var valid = true;
            if (action == null || action.buildPointsRequired <= 0f)
            {
                AddIssue(issues, execution?.activityId, "BuildPointsRequired", execution?.activityId, 1, 0,
                    true, false, "Construction advance requires positive buildPointsRequired.");
                valid = false;
            }
            if (execution == null || string.IsNullOrWhiteSpace(execution.heroId) ||
                !_activityState.HasHero(execution.heroId) ||
                !string.Equals(_activityState.GetHeroCurrentActivityExecutionId(execution.heroId),
                    execution.executionId, StringComparison.Ordinal))
            {
                AddIssue(issues, execution?.activityId, "ConstructionHero", execution?.heroId, 1, 0, true,
                    false, "Running construction advance requires its assigned hero to own the execution.");
                valid = false;
            }
            if (execution != null && !execution.materialsPaid)
            {
                AddIssue(issues, execution.activityId, "ConstructionMaterialsPayment", execution.executionId, 1,
                    0, true, false, "Running construction must have its one-time materials payment recorded.");
                valid = false;
            }
            if (execution != null &&
                (float.IsNaN(execution.elapsedSeconds) || float.IsInfinity(execution.elapsedSeconds) ||
                 execution.elapsedSeconds < 0f || execution.elapsedSeconds > long.MaxValue))
            {
                AddIssue(issues, execution.activityId, "ConstructionElapsedSeconds", execution.executionId, 0,
                    0, true, false, "Construction advance requires finite non-negative elapsedSeconds.");
                valid = false;
            }
            if (execution != null &&
                (float.IsNaN(execution.accumulatedBuildPoints) ||
                 float.IsInfinity(execution.accumulatedBuildPoints) || execution.accumulatedBuildPoints < 0f))
            {
                AddIssue(issues, execution.activityId, "ConstructionBuildPoints", execution.executionId, 0, 0,
                    true, false, "Construction advance requires finite non-negative accumulatedBuildPoints.");
                valid = false;
            }
            if (execution != null &&
                (execution.buildingLevelApplied || execution.buildingEventPending ||
                 execution.buildingEventPublished || !string.IsNullOrWhiteSpace(execution.completionPhase)))
            {
                AddIssue(issues, execution.activityId, "ConstructionCompletionState", execution.executionId, 0,
                    0, true, false, "Running construction has an inconsistent completion state.");
                valid = false;
            }
            return valid;
        }

        private bool ValidateWorkCyclePreparation(ActivityExecutionSaveData execution, ActivityRuntimeInfo info, List<ActivityRequirementIssue> issues)
        {
            var rewardDescriptorsValid = ValidateWorkCycleRewardDescriptors(execution, issues);
            var dangerValid = ValidateDangerDescriptor(execution, info, issues);
            var effectsValid = ValidateWorkEffectDescriptors(execution, info, issues);
            return rewardDescriptorsValid && dangerValid && effectsValid;
        }

        private static bool ValidateWorkAdvanceExecution(
            ActivityExecutionSaveData execution,
            ActivityRuntimeInfo info,
            List<ActivityRequirementIssue> issues)
        {
            var valid = true;
            if (info == null || info.durationSeconds <= 0)
            {
                AddIssue(issues, execution?.activityId, "ActivityDuration", execution?.activityId, 1, info?.durationSeconds ?? 0, true, false, "Work advance requires a positive cycle duration.");
                valid = false;
            }
            if (execution == null || execution.plannedCycles <= 0 ||
                execution.completedCycles < 0 || execution.completedCycles > execution.plannedCycles)
            {
                AddIssue(issues, execution?.activityId, "WorkCyclePlan", execution?.executionId, 1, execution?.plannedCycles ?? 0, true, false, "Work advance requires a valid bounded cycle plan.");
                valid = false;
            }
            if (execution != null &&
                (float.IsNaN(execution.elapsedSeconds) || float.IsInfinity(execution.elapsedSeconds) || execution.elapsedSeconds < 0f))
            {
                AddIssue(issues, execution.activityId, "WorkElapsedSeconds", execution.executionId, 0, 0, true, false, "Work advance requires finite non-negative elapsedSeconds.");
                valid = false;
            }
            if (execution != null &&
                !string.Equals(execution.cyclePhase, CyclePhaseRunning, StringComparison.Ordinal) &&
                !string.Equals(execution.cyclePhase, CyclePhaseResultStaged, StringComparison.Ordinal))
            {
                AddIssue(issues, execution.activityId, "WorkCyclePhase", execution.executionId, 1, 0, true, false, $"Work execution has unsupported cycle phase '{execution.cyclePhase}'.");
                valid = false;
            }
            if (execution != null &&
                string.Equals(execution.cyclePhase, CyclePhaseRunning, StringComparison.Ordinal) &&
                execution.completedCycles >= execution.plannedCycles)
            {
                AddIssue(issues, execution.activityId, "WorkCyclePlan", execution.executionId, execution.plannedCycles, execution.completedCycles, true, false, "Running work execution already reached its planned cycle count.");
                valid = false;
            }
            return valid;
        }

        private WorkCycleOutcome EnsureCurrentWorkCycleFatigue(
            ActivityExecutionSaveData execution,
            ActivityRuntimeInfo info,
            List<ActivityRequirementIssue> issues,
            ref bool changed,
            List<PendingResultDeferredResolvedEvent> deferredResolvedEvents)
        {
            if (execution.currentCycleFatiguePaid || execution.completedCycles >= execution.plannedCycles)
                return WorkCycleOutcome.Completed;

            var checkpoint = _activityState.CaptureCheckpoint();
            if (!_activityState.SpendHeroFatigue(execution.heroId, info.activity.fatigueCost))
            {
                var stopped = CloneExecution(execution);
                stopped.endReason = EndReasonInsufficientFatigue;
                stopped.currentCycleFatiguePaid = false;
                stopped.stagedRewards = Array.Empty<ActivityStagedRewardSaveData>();
                if (!UpdateExecution(stopped))
                {
                    _activityState.RestoreCheckpoint(checkpoint);
                    AddIssue(issues, execution.activityId, "ActivityExecution", execution.executionId, 1, 0, true, false, "Failed to finish work after fatigue was exhausted.");
                    return WorkCycleOutcome.RuntimeError;
                }
                if (!(_activityState.PendingResults is ITransactionalPendingResultService transactionalPendingResults))
                {
                    _activityState.RestoreCheckpoint(checkpoint);
                    AddIssue(issues, execution.activityId, "PendingResultTransaction", execution.executionId, 1, 0, true, false, "PendingResult service does not support outer transactions.");
                    return WorkCycleOutcome.RuntimeError;
                }

                var formation = transactionalPendingResults.CreateOrAppendInTransaction(
                    $"activity:{execution.executionId}:fatigue-stop:{execution.completedCycles}",
                    BuildPendingDraft(stopped, Array.Empty<ActivityStagedRewardSaveData>()),
                    true,
                    GetPendingResultRevision(stopped));
                if (!formation.Success)
                {
                    _activityState.RestoreCheckpoint(checkpoint);
                    AddIssue(issues, execution.activityId, "PendingResult", execution.executionId, 1, 0, true, false, formation.Message ?? "Failed to finish work after fatigue was exhausted.");
                    return WorkCycleOutcome.RuntimeError;
                }

                stopped.pendingResultId = formation.Result?.resultId ?? stopped.pendingResultId;
                stopped.status = CoreActivityRuntimeStatus.ResultPending;
                if (formation.DeferredResolvedEvent != null)
                    deferredResolvedEvents?.Add(formation.DeferredResolvedEvent);
                CopyExecutionState(stopped, execution);
                changed = true;
                return WorkCycleOutcome.InsufficientFatigue;
            }

            var draft = CloneExecution(execution);
            draft.currentCycleFatiguePaid = true;
            if (!UpdateExecution(draft))
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, execution.activityId, "ActivityExecution", execution.executionId, 1, 0, true, false, "Failed to persist work-cycle fatigue payment.");
                return WorkCycleOutcome.RuntimeError;
            }
            CopyExecutionState(draft, execution);
            changed = true;
            return WorkCycleOutcome.Completed;
        }

        private bool ValidateWorkCycleRewardDescriptors(ActivityExecutionSaveData execution, List<ActivityRequirementIssue> issues)
        {
            var definitions = new List<RewardDefinition>();
            foreach (var reward in RuntimeConfigs.Activities.GetRewards(execution.activityId))
            {
                if (reward == null || !ActivityResolverUtilities.MomentMatches(reward.grantMoment, GrantMoment.OnCycle))
                    continue;
                definitions.Add(new RewardDefinition
                {
                    sourceId = execution.activityId,
                    rewardType = reward.rewardType,
                    targetId = reward.targetId,
                    min = reward.min,
                    max = reward.max,
                    chance = reward.chance,
                    grantMoment = reward.grantMoment
                });
            }
            var validation = RewardBatchPipeline.Validate(definitions, GrantMoment.OnCycle, execution.heroId, true);
            issues.AddRange(validation.issues);
            return validation.success;
        }

        private bool ValidateDangerDescriptor(ActivityExecutionSaveData execution, ActivityRuntimeInfo info, List<ActivityRequirementIssue> issues)
        {
            var encounters = RuntimeConfigs.Activities.GetDangerEncounters(execution.activityId);
            if (encounters.Length == 0)
                return true;
            var encounter = encounters[0];
            if (!RuntimeConfigs.Formulas.TryGetFormula(encounter.riskFormulaId, out var formula))
            {
                AddIssue(issues, execution.activityId, "DangerFormula", encounter.riskFormulaId, 1, 0, true, false, "DangerEncounter formula is missing.");
                return false;
            }
            var evaluated = _formulas.Evaluate(formula, BuildFormulaContext(execution.heroId, info.activity.mainSkillId, encounter.riskPercent, true, formula));
            if (evaluated.success)
                return true;
            AddIssue(issues, execution.activityId, evaluated.code, formula.formulaId, 1, 0, true, false, evaluated.message);
            return false;
        }

        private bool ValidateWorkEffectDescriptors(ActivityExecutionSaveData execution, ActivityRuntimeInfo info, List<ActivityRequirementIssue> issues)
        {
            if (!RuntimeConfigs.Heroes.TryGet(execution.heroId, out var hero))
                return true;
            var valid = true;
            foreach (var effect in RuntimeConfigs.Heroes.GetEffectsByTrigger(WorkEffectTrigger))
            {
                if (effect == null || !HeroOwnsSkill(hero, effect.skillId) || !ConditionMatchesCategory(effect.condition, info.activity.category))
                    continue;
                if (!_workEffectHandlers.ContainsKey(effect.effect ?? string.Empty))
                {
                    AddIssue(issues, execution.activityId, "HeroEffectUnsupported", effect.effectId, 1, 0, true, false, $"Unsupported work effect handler '{effect.effect}'.");
                    valid = false;
                    continue;
                }
                if (!int.TryParse(effect.interval, out var interval) || interval <= 0)
                {
                    AddIssue(issues, execution.activityId, "HeroEffectInterval", effect.effectId, 1, 0, true, false, $"Work effect interval '{effect.interval}' must be a positive integer.");
                    valid = false;
                    continue;
                }
                if (effect.chancePercent < 0f || effect.chancePercent > 100f || float.IsNaN(effect.chancePercent) || float.IsInfinity(effect.chancePercent))
                {
                    AddIssue(issues, execution.activityId, "HeroEffectChance", effect.effectId, 1, 0, true, false, $"Work effect chance '{effect.chancePercent}' must be in range 0..100.");
                    valid = false;
                    continue;
                }
                if (string.Equals(effect.effect, AddExtraBaseResourceEffect, StringComparison.OrdinalIgnoreCase) &&
                    !ValidateCompletedWorkBaseResourceTarget(execution.activityId, effect, issues))
                    valid = false;
            }
            return valid;
        }

        private bool ValidateCompletedWorkBaseResourceTarget(string activityId, HeroSkillEffectConfigDto effect, List<ActivityRequirementIssue> issues)
        {
            if (!string.Equals(effect?.target, CompletedWorkBaseResourceTarget, StringComparison.OrdinalIgnoreCase))
            {
                AddIssue(issues, activityId, "HeroEffectTarget", effect?.effectId, 1, 0, true, false, $"Work effect target '{effect?.target}' could not be resolved.");
                return false;
            }
            ActivityRewardConfigDto baseResource = null;
            foreach (var reward in RuntimeConfigs.Activities.GetRewards(activityId))
            {
                if (reward == null || !ActivityResolverUtilities.MomentMatches(reward.grantMoment, GrantMoment.OnCycle) ||
                    !ActivityTypeParser.TryParseRewardType(reward.rewardType, out var type) || type != RewardTypeEnum.Resource)
                    continue;
                if (reward.chance < 100f)
                {
                    AddIssue(issues, activityId, "HeroEffectTarget", effect.effectId, 1, 0, true, false, "completed_work_base_resource requires a guaranteed OnCycle Resource reward.");
                    return false;
                }
                if (baseResource != null)
                {
                    AddIssue(issues, activityId, "HeroEffectTarget", effect.effectId, 1, 0, true, false, "completed_work_base_resource requires exactly one OnCycle Resource reward.");
                    return false;
                }
                baseResource = reward;
            }
            if (baseResource != null)
                return true;
            AddIssue(issues, activityId, "HeroEffectTarget", effect.effectId, 1, 0, true, false, "completed_work_base_resource requires an OnCycle Resource reward.");
            return false;
        }

        private FormulaEvaluationContext BuildFormulaContext(string heroId, string skillId, float contextBase, bool hasContextBase, FormulaConfigDto formula)
        {
            var context = new FormulaEvaluationContext
            {
                skillLevel = _activityState.GetHeroSkillLevel(heroId, skillId),
                contextBase = contextBase,
                hasContextBase = hasContextBase
            };
            context.SetStat(formula.primaryStat, _activityState.GetHeroStat(heroId, formula.primaryStat));
            context.SetStat(formula.secondaryStat, _activityState.GetHeroStat(heroId, formula.secondaryStat));
            return context;
        }

        private bool ApplyWorkHeroEffects(ActivityExecutionSaveData execution, ActivityRuntimeInfo info, ActivityStagedRewardSaveData[] staged, List<ActivityRequirementIssue> issues)
        {
            if (!RuntimeConfigs.Heroes.TryGet(execution.heroId, out var hero))
                return true;
            foreach (var effect in RuntimeConfigs.Heroes.GetEffectsByTrigger(WorkEffectTrigger))
            {
                if (effect == null || !HeroOwnsSkill(hero, effect.skillId) || !ConditionMatchesCategory(effect.condition, info.activity.category))
                    continue;
                var counter = _activityState.GetHeroEffectCounter(execution.heroId, effect.effectId) + 1;
                if (!_activityState.SetHeroEffectCounter(execution.heroId, effect.effectId, counter))
                {
                    AddIssue(issues, execution.activityId, "HeroEffectCounter", effect.effectId, 1, counter - 1, true, false, "Failed to persist hero effect counter.");
                    return false;
                }
                if (!int.TryParse(effect.interval, out var interval) || interval <= 0 || counter % interval != 0 ||
                    !ActivityResolverUtilities.ChancePassed(effect.chancePercent, _random))
                    continue;
                if (!_workEffectHandlers.TryGetValue(effect.effect ?? string.Empty, out var handler))
                {
                    AddIssue(issues, execution.activityId, "HeroEffectUnsupported", effect.effectId, 1, 0, true, false, $"Unsupported work effect handler '{effect.effect}'.");
                    return false;
                }
                if (!handler(effect, staged))
                {
                    AddIssue(issues, execution.activityId, "HeroEffectTarget", effect.effectId, 1, 0, true, false, $"Work effect target '{effect.target}' could not be resolved.");
                    return false;
                }
            }
            return true;
        }

        private static bool ApplyExtraBaseResource(HeroSkillEffectConfigDto effect, ActivityStagedRewardSaveData[] staged)
        {
            if (!string.Equals(effect?.target, CompletedWorkBaseResourceTarget, StringComparison.OrdinalIgnoreCase))
                return false;
            foreach (var entry in staged ?? Array.Empty<ActivityStagedRewardSaveData>())
            {
                if (!ActivityTypeParser.TryParseRewardType(entry?.rewardType, out var rewardType) || rewardType != RewardTypeEnum.Resource)
                    continue;
                entry.quantity += Math.Max(0L, (long)Math.Round(effect.value, MidpointRounding.AwayFromZero));
                return true;
            }
            return false;
        }

        private void ValidateStandardStart(ActivityStartResult result, ActivityRuntimeInfo info, List<ActivityRequirementIssue> issues, bool includeCost)
        {
            var context = result.context;
            if (string.IsNullOrWhiteSpace(context.heroId))
                AddIssue(issues, context.activityId, "HeroExecutor", string.Empty, 1, 0, true, false, "Activity start requires heroId.");
            result.startCheck = ActivityResolver.CanStart(context, _activityState);
            issues.AddRange(result.startCheck.issues);
            if (!info.isRepeatable && _activityState.IsActivityCompleted(context.activityId))
                AddIssue(issues, context.activityId, "ActivityCompleted", context.activityId, 1, 1, false, false, $"Activity '{context.activityId}' is non-repeatable and already completed.");
            if (includeCost && !HasBlockingIssues(issues))
            {
                result.costCheck = ActivityResolver.CanPayCost(context, _activityState);
                issues.AddRange(result.costCheck.issues);
            }
            if (!HasBlockingIssues(issues))
                ValidateHeroStart(context.activityId, context.heroId, context.executionId, issues);
            if (!HasBlockingIssues(issues))
                ValidateActiveHeroLimit(context.heroId, issues, context.activityId);
        }

        private void ValidateHeroStart(string activityId, string heroId, string executionId, List<ActivityRequirementIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(heroId) || !_activityState.HasHero(heroId) || !_activityState.HasHeroState(heroId))
            {
                AddIssue(issues, activityId, "HeroExecutor", heroId, 1, 0, false, false, "Activity start requires an acquired hero with runtime state.");
                return;
            }
            var current = _activityState.GetHeroCurrentActivityExecutionId(heroId);
            if (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, executionId, StringComparison.Ordinal))
                AddIssue(issues, activityId, "HeroBusy", heroId, 1, 1, false, false, $"Hero '{heroId}' is busy with execution '{current}'.");
        }

        private void ValidateBuildRequirements(BuildActionConfigDto action, string heroId, string executionId, List<ActivityRequirementIssue> issues)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.buildFormulaId) || action.buildPointsRequired <= 0)
            {
                AddIssue(issues, action?.id, "InvalidBuildDescriptor", action?.id, 1, 0, true, false, "Generated build action requires formula and positive build points.");
                return;
            }
            if (_activityState.GetBuildingLevel(action.targetBuildingId) != action.targetLevel - 1)
                AddIssue(issues, action.id, "BuildingLevel", action.targetBuildingId, action.targetLevel - 1, _activityState.GetBuildingLevel(action.targetBuildingId), false, false, "Building is not at the source level for this build action.");
            if (_activityState.GetHeroFatigue(heroId) < action.fatigueCost)
                AddIssue(issues, action.id, "InsufficientFatigue", heroId, action.fatigueCost, _activityState.GetHeroFatigue(heroId), false, false, "Hero cannot pay construction assignment fatigue.");
            foreach (var material in action.materials ?? Array.Empty<MaterialCostDto>())
            {
                var available = _activityState.GetAvailableForActionCount(material.id, new StorageActionContext(StorageContextType.ActivityExecution, executionId));
                if (available < material.count)
                    AddIssue(issues, action.id, "BuildMaterials", material.id, material.count, available, false, false, "Insufficient build material quantity.");
            }
            foreach (var requirement in action.requirementsActivities ?? Array.Empty<RequiredActivityDto>())
                if (requirement.count > 0 && !_activityState.IsActivityCompleted(requirement.activityId)) AddIssue(issues, action.id, "ActivityCompleted", requirement.activityId, requirement.count, 0, false, false, "Required activity is not completed.");
            foreach (var requirement in action.requirementsBuildings ?? Array.Empty<RequiredBuildingDto>())
                if (_activityState.GetBuildingLevel(requirement.buildingId) < requirement.level) AddIssue(issues, action.id, "BuildingLevel", requirement.buildingId, requirement.level, _activityState.GetBuildingLevel(requirement.buildingId), false, false, "Required building level is not met.");
            foreach (var requirement in action.requirementsSkills ?? Array.Empty<RequiredSkillDto>())
                if (_activityState.GetHeroSkillLevel(heroId, requirement.skillId) < requirement.level) AddIssue(issues, action.id, "SkillLevel", requirement.skillId, requirement.level, _activityState.GetHeroSkillLevel(heroId, requirement.skillId), false, false, "Required skill level is not met.");
            if (RuntimeConfigs.Formulas.TryGetFormula(action.buildFormulaId, out var formula))
            {
                var validation = _formulas.Evaluate(formula, BuildFormulaContext(heroId, action.skillId, 0f, false, formula));
                if (!validation.success) AddIssue(issues, action.id, validation.code, action.buildFormulaId, 1, 0, true, false, validation.message);
            }
            else
            {
                AddIssue(issues, action.id, "BuildFormulaMissing", action.buildFormulaId, 1, 0, true, false, "Build formula is missing.");
            }
        }

        private void ValidateActiveHeroLimit(string heroId, List<ActivityRequirementIssue> issues, string activityId)
        {
            if (_activityState.IsHeroBusy(heroId))
                return;
            var currentLimit = ActiveHeroLimitResolver.GetCurrentLimit(_activityState);
            var activeHeroCount = CountActiveHeroes();
            if (activeHeroCount >= currentLimit)
                AddIssue(issues, activityId, "ActiveHeroLimitReached", heroId, currentLimit, activeHeroCount, false, false, $"Active hero limit reached: {activeHeroCount}/{currentLimit}.");
        }

        private ActivityExecutionSaveData FindConstruction(BuildActionConfigDto requestedAction)
        {
            foreach (var execution in GetExecutions())
            {
                if (execution == null || !string.Equals(execution.runtimeKind, RuntimeKindBuild, StringComparison.Ordinal) ||
                    (execution.status != CoreActivityRuntimeStatus.Running && execution.status != CoreActivityRuntimeStatus.Paused && execution.status != CoreActivityRuntimeStatus.ResultPending) ||
                    !RuntimeConfigs.Buildings.TryGetBuildAction(execution.activityId, out var existingAction))
                    continue;
                if (string.Equals(existingAction.targetBuildingId, requestedAction.targetBuildingId, StringComparison.Ordinal) &&
                    existingAction.targetLevel == requestedAction.targetLevel)
                    return execution;
            }
            return null;
        }

        private void ReconcilePendingBuildingEvents()
        {
            foreach (var execution in GetExecutions())
            {
                var changed = false;
                TryResumeBuildCompletionLifecycle(execution, null, null, ref changed);
            }
        }

        private void ReconcileLinkedCombatCompletions()
        {
            foreach (var execution in GetExecutions())
            {
                if (!IsLinkedCombatReadyForCompletion(execution))
                    continue;
                TryFinalizeLinkedCombatCompletion(execution, execution.linkedCombat.combatExecutionId, false, false);
            }
        }

        private void HandlePendingResultResolved(PendingResultResolvedEvent resolved)
        {
            if (resolved == null || !string.Equals(resolved.SourceType, PendingResultSourceType.Activity, StringComparison.Ordinal))
                return;
            var execution = GetExecution(resolved.SourceExecutionId);
            if (!IsLinkedCombatReadyForCompletion(execution))
                return;
            TryFinalizeLinkedCombatCompletion(execution, execution.linkedCombat.combatExecutionId, false, false);
        }

        private bool IsLinkedCombatReadyForCompletion(ActivityExecutionSaveData execution)
        {
            return execution?.linkedCombat != null &&
                   !string.IsNullOrWhiteSpace(execution.linkedCombat.requestId) &&
                   !string.IsNullOrWhiteSpace(execution.linkedCombat.combatExecutionId) &&
                   execution.linkedCombat.resolved &&
                   execution.activityBagResolved &&
                   string.Equals(execution.linkedCombat.rootExecutionId, execution.executionId, StringComparison.Ordinal) &&
                   string.Equals(execution.linkedCombat.occupationOwnerId, execution.executionId, StringComparison.Ordinal);
        }

        private LinkedCombatGatewayResult TryFinalizeLinkedCombatCompletion(
            ActivityExecutionSaveData execution,
            string combatExecutionId,
            bool markCombatResolved,
            bool replayed)
        {
            if (execution?.linkedCombat == null)
                return GatewayFailure("RequestNotFound", "Linked combat execution was not found.");
            if (string.IsNullOrWhiteSpace(execution.linkedCombat.combatExecutionId))
                return GatewayFailure("CombatNotBound", "Linked combat request must be bound before it can be resolved.");
            if (!string.Equals(execution.linkedCombat.combatExecutionId, combatExecutionId, StringComparison.Ordinal))
                return GatewayFailure("CombatExecutionMismatch", "Resolved combat execution does not match the linked binding.");
            if (!execution.activityBagResolved)
                return GatewaySuccess(execution, replayed, null);
            if (_progressionProcessor == null)
                return GatewayFailure("ActivityCompletedProcessorMissing", "Linked combat completion requires a transaction-aware progression processor.");

            var checkpoint = _activityState.CaptureCheckpoint();
            var current = GetExecution(execution.executionId);
            if (current?.linkedCombat == null)
            {
                _activityState.RestoreCheckpoint(checkpoint);
                return GatewayFailure("RequestNotFound", "Linked combat execution was not found.");
            }
            if (markCombatResolved)
                current.linkedCombat.resolved = true;
            if (!IsLinkedCombatReadyForCompletion(current))
            {
                _activityState.RestoreCheckpoint(checkpoint);
                return GatewayFailure("LinkedCombatNotReady", "Linked combat completion requires both Activity Bag and Combat Result to be resolved.");
            }

            var completedActivityId = current.activityId;
            var request = CloneLinkedCombat(current.linkedCombat);
            var progression = _progressionProcessor.ProcessActivityCompleted(completedActivityId);
            if (!progression.success)
            {
                _activityState.RestoreCheckpoint(checkpoint);
                return GatewayFailure(progression.code ?? "ActivityCompletedProcessor", progression.message ?? "ActivityCompleted processor failed.");
            }
            current = GetExecution(execution.executionId);
            if (current == null)
            {
                _activityState.RestoreCheckpoint(checkpoint);
                return GatewayFailure("RequestNotFound", "Linked combat execution disappeared while finalizing completion.");
            }
            if (!_activityState.IsActivityCompleted(completedActivityId) && !_activityState.CompleteActivity(completedActivityId))
            {
                _activityState.RestoreCheckpoint(checkpoint);
                return GatewayFailure("ActivityCompleted", "Failed to mark linked work activity completed.");
            }
            if (!RemoveExecution(current.executionId))
            {
                _activityState.RestoreCheckpoint(checkpoint);
                return GatewayFailure("ActivityExecution", "Failed to remove linked work execution.");
            }
            _activityState.RecordOperationReceipt(new OperationReceiptSaveData
            {
                aggregateId = LinkedCombatResolutionReceiptAggregateId(request.requestId),
                operationId = "resolve",
                fingerprint = combatExecutionId,
                success = true,
                code = "Resolved",
                resolved = true
            });
            if (!Save())
            {
                _activityState.RestoreCheckpoint(checkpoint);
                return GatewayFailure("SaveFailed", "Failed to persist linked combat completion.");
            }

            NotifyEventSink(new ActivityRuntimeEvent
            {
                eventType = ActivityRuntimeEventType.ActivityCompleted,
                targetId = completedActivityId,
                value = 1,
                progressionAlreadyProcessed = true
            });
            return GatewaySuccess(new ActivityExecutionSaveData { activityId = completedActivityId, linkedCombat = request }, replayed, completedActivityId);
        }

        private bool TryResumeBuildCompletionLifecycle(
            ActivityExecutionSaveData execution,
            List<ActivityRequirementIssue> issues,
            List<ActivityRuntimeEvent> events,
            ref bool changed)
        {
            return TryResumeBuildCompletionLifecycle(
                execution, issues, events, ref changed, BuildCompletionOptions.Online);
        }

        private bool TryResumeBuildCompletionLifecycle(
            ActivityExecutionSaveData execution,
            List<ActivityRequirementIssue> issues,
            List<ActivityRuntimeEvent> events,
            ref bool changed,
            BuildCompletionOptions options)
        {
            if (execution == null || !string.Equals(execution.runtimeKind, RuntimeKindBuild, StringComparison.Ordinal))
                return false;
            var handled = false;
            if (string.Equals(execution.completionPhase, CompletionPhaseBuildingEventPending, StringComparison.Ordinal) ||
                (execution.buildingLevelApplied && execution.buildingEventPending && !execution.buildingEventPublished))
                handled |= TryProcessPendingBuildingEvent(execution.executionId, issues, events, ref changed, options);
            var current = GetExecution(execution.executionId);
            if (current != null && string.Equals(current.completionPhase, CompletionPhaseCompletionReady, StringComparison.Ordinal))
                handled |= TryFinalizeCompletionReady(current.executionId, issues, events, ref changed, options);
            return handled;
        }

        private bool TryProcessPendingBuildingEvent(
            string executionId,
            List<ActivityRequirementIssue> issues,
            List<ActivityRuntimeEvent> events,
            ref bool changed)
        {
            return TryProcessPendingBuildingEvent(
                executionId, issues, events, ref changed, BuildCompletionOptions.Online);
        }

        private bool TryProcessPendingBuildingEvent(
            string executionId,
            List<ActivityRequirementIssue> issues,
            List<ActivityRuntimeEvent> events,
            ref bool changed,
            BuildCompletionOptions options)
        {
            var current = GetExecution(executionId);
            if (current == null || !current.buildingEventPending || current.buildingEventPublished ||
                !RuntimeConfigs.Buildings.TryGetBuildAction(current.activityId, out var action))
                return false;
            if (_progressionProcessor == null)
            {
                AddIssue(issues, current.activityId, "BuildingEventProcessorMissing", current.executionId, 1, 0, true, false, "BuildingLevelChanged requires a transaction-aware progression processor.");
                return false;
            }
            var checkpoint = _activityState.CaptureCheckpoint();
            var progression = _progressionProcessor.ProcessBuildingLevelChanged(action.targetBuildingId, action.targetLevel);
            if (!progression.success)
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, current.activityId, progression.code ?? "BuildingEventProcessor", action.targetBuildingId, action.targetLevel, 0, true, false, progression.message ?? "BuildingLevelChanged processor failed.");
                return false;
            }
            current = GetExecution(executionId);
            if (current == null)
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, action.id, "ActivityExecution", executionId, 1, 0, true, false, "Construction execution disappeared while processing BuildingLevelChanged.");
                return false;
            }
            var emptyCompletion = current.status == CoreActivityRuntimeStatus.Paused && string.IsNullOrWhiteSpace(current.pendingResultId);
            current.buildingEventPending = false;
            current.buildingEventPublished = true;
            current.completionPhase = emptyCompletion ? CompletionPhaseCompletionReady : null;
            if (!UpdateExecution(current) || (options.SaveInternally && !Save()))
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, current.activityId, "BuildingEventAck", current.executionId, 1, 0, true, false, "Failed to persist BuildingLevelChanged acknowledgment.");
                return false;
            }
            changed = true;
            var runtimeEvent = new ActivityRuntimeEvent { eventType = ActivityRuntimeEventType.BuildingLevelChanged, targetId = action.targetBuildingId, value = action.targetLevel, progressionAlreadyProcessed = true };
            events?.Add(runtimeEvent);
            if (options.PublishEvents)
                NotifyEventSink(runtimeEvent);
            return true;
        }

        private bool TryFinalizeCompletionReady(
            string executionId,
            List<ActivityRequirementIssue> issues,
            List<ActivityRuntimeEvent> events,
            ref bool changed)
        {
            return TryFinalizeCompletionReady(
                executionId, issues, events, ref changed, BuildCompletionOptions.Online);
        }

        private bool TryFinalizeCompletionReady(
            string executionId,
            List<ActivityRequirementIssue> issues,
            List<ActivityRuntimeEvent> events,
            ref bool changed,
            BuildCompletionOptions options)
        {
            var current = GetExecution(executionId);
            if (current == null || !string.Equals(current.completionPhase, CompletionPhaseCompletionReady, StringComparison.Ordinal))
                return false;
            if (_progressionProcessor == null)
            {
                AddIssue(issues, current.activityId, "ActivityCompletedProcessorMissing", current.executionId, 1, 0, true, false, "ActivityCompleted requires a transaction-aware progression processor.");
                return false;
            }
            var checkpoint = _activityState.CaptureCheckpoint();
            var progression = _progressionProcessor.ProcessActivityCompleted(current.activityId);
            if (!progression.success)
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, current.activityId, progression.code ?? "ActivityCompletedProcessor", current.activityId, 1, 0, true, false, progression.message ?? "ActivityCompleted processor failed.");
                return false;
            }
            current = GetExecution(executionId);
            if (current == null)
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, string.Empty, "ActivityExecution", executionId, 1, 0, true, false, "Construction execution disappeared while finalizing completion.");
                return false;
            }
            if (!_activityState.IsActivityCompleted(current.activityId) && !_activityState.CompleteActivity(current.activityId))
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, current.activityId, "ActivityCompleted", current.activityId, 1, 0, true, false, "Failed to mark construction activity completed.");
                return false;
            }
            var completedActivityId = current.activityId;
            if (!RemoveExecution(current.executionId) || (options.SaveInternally && !Save()))
            {
                _activityState.RestoreCheckpoint(checkpoint);
                AddIssue(issues, completedActivityId, "BuildCompletion", current.executionId, 1, 0, true, false, "Failed to finalize construction completion.");
                return false;
            }
            changed = true;
            var runtimeEvent = new ActivityRuntimeEvent { eventType = ActivityRuntimeEventType.ActivityCompleted, targetId = completedActivityId, value = 1, progressionAlreadyProcessed = true };
            events?.Add(runtimeEvent);
            if (options.PublishEvents)
                NotifyEventSink(runtimeEvent);
            return true;
        }

        private int CountActiveHeroes()
        {
            var heroIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var execution in GetExecutions())
                if (execution != null && (execution.status == CoreActivityRuntimeStatus.Running || execution.status == CoreActivityRuntimeStatus.ResultPending) && !string.IsNullOrWhiteSpace(execution.heroId)) heroIds.Add(execution.heroId);
            return heroIds.Count;
        }

        private static ActivityStagedRewardSaveData[] ToStagedRewards(ActivityRewardResult rewards, string origin)
        {
            if (rewards == null)
                return Array.Empty<ActivityStagedRewardSaveData>();
            var result = new List<ActivityStagedRewardSaveData>();
            foreach (var entry in PendingResultEntryFactory.FromActivityRewards(rewards.rewards, origin))
                result.Add(new ActivityStagedRewardSaveData { rewardType = entry.RewardType, targetId = entry.TargetId, quantity = entry.Quantity, origin = entry.Origin, quality = entry.Quality, instanceId = entry.InstanceId });
            return result.ToArray();
        }

        private static PendingResultDraft BuildPendingDraft(ActivityExecutionSaveData execution, ActivityStagedRewardSaveData[] staged)
        {
            var entries = new List<PendingResultEntryDraft>();
            foreach (var entry in staged ?? Array.Empty<ActivityStagedRewardSaveData>())
            {
                if (entry == null || entry.quantity <= 0)
                    continue;
                entries.Add(new PendingResultEntryDraft
                {
                    SortOrder = entries.Count,
                    RewardType = entry.rewardType,
                    TargetId = entry.targetId,
                    Quantity = entry.quantity,
                    Origin = string.IsNullOrWhiteSpace(entry.origin) ? PendingResultOrigin.ActivityReward : entry.origin,
                    Quality = entry.quality,
                    InstanceId = entry.instanceId
                });
            }
            return new PendingResultDraft
            {
                SourceType = PendingResultSourceType.Activity,
                SourceId = execution.activityId,
                SourceExecutionId = execution.executionId,
                OwnerHeroId = execution.heroId,
                Entries = entries.ToArray()
            };
        }

        private static ActivityExecutionSaveData CloneExecution(ActivityExecutionSaveData execution)
        {
            if (execution == null)
                return null;
            return new ActivityExecutionSaveData
            {
                executionId = execution.executionId,
                activityId = execution.activityId,
                runtimeKind = execution.runtimeKind,
                heroId = execution.heroId,
                status = execution.status,
                elapsedSeconds = execution.elapsedSeconds,
                completedCycles = execution.completedCycles,
                plannedCycles = execution.plannedCycles,
                currentCycleFatiguePaid = execution.currentCycleFatiguePaid,
                cyclePhase = execution.cyclePhase,
                stagedRewards = CloneStagedRewards(execution.stagedRewards),
                endReason = execution.endReason,
                dangerRollCompleted = execution.dangerRollCompleted,
                dangerRiskPercent = execution.dangerRiskPercent,
                dangerRoll = execution.dangerRoll,
                dangerHandoffFingerprint = execution.dangerHandoffFingerprint,
                dangerNonCombatEntryCount = execution.dangerNonCombatEntryCount,
                activityBagResolved = execution.activityBagResolved,
                materialsPaid = execution.materialsPaid,
                accumulatedBuildPoints = execution.accumulatedBuildPoints,
                buildingLevelApplied = execution.buildingLevelApplied,
                buildingEventPending = execution.buildingEventPending,
                buildingEventPublished = execution.buildingEventPublished,
                completionPhase = execution.completionPhase,
                linkedCombat = CloneLinkedCombat(execution.linkedCombat),
                pendingResultId = execution.pendingResultId,
                startedAtUnixSeconds = execution.startedAtUnixSeconds
            };
        }

        private static void CopyExecutionState(ActivityExecutionSaveData source, ActivityExecutionSaveData target)
        {
            if (source == null || target == null)
                return;
            target.executionId = source.executionId;
            target.activityId = source.activityId;
            target.runtimeKind = source.runtimeKind;
            target.heroId = source.heroId;
            target.status = source.status;
            target.elapsedSeconds = source.elapsedSeconds;
            target.completedCycles = source.completedCycles;
            target.plannedCycles = source.plannedCycles;
            target.currentCycleFatiguePaid = source.currentCycleFatiguePaid;
            target.cyclePhase = source.cyclePhase;
            target.stagedRewards = CloneStagedRewards(source.stagedRewards);
            target.endReason = source.endReason;
            target.dangerRollCompleted = source.dangerRollCompleted;
            target.dangerRiskPercent = source.dangerRiskPercent;
            target.dangerRoll = source.dangerRoll;
            target.dangerHandoffFingerprint = source.dangerHandoffFingerprint;
            target.dangerNonCombatEntryCount = source.dangerNonCombatEntryCount;
            target.activityBagResolved = source.activityBagResolved;
            target.materialsPaid = source.materialsPaid;
            target.accumulatedBuildPoints = source.accumulatedBuildPoints;
            target.buildingLevelApplied = source.buildingLevelApplied;
            target.buildingEventPending = source.buildingEventPending;
            target.buildingEventPublished = source.buildingEventPublished;
            target.completionPhase = source.completionPhase;
            target.linkedCombat = CloneLinkedCombat(source.linkedCombat);
            target.pendingResultId = source.pendingResultId;
            target.startedAtUnixSeconds = source.startedAtUnixSeconds;
        }

        private static ActivityStagedRewardSaveData[] CloneStagedRewards(ActivityStagedRewardSaveData[] source)
        {
            var entries = source ?? Array.Empty<ActivityStagedRewardSaveData>();
            var result = new ActivityStagedRewardSaveData[entries.Length];
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                result[index] = entry == null ? null : new ActivityStagedRewardSaveData
                {
                    rewardType = entry.rewardType,
                    targetId = entry.targetId,
                    quantity = entry.quantity,
                    origin = entry.origin,
                    quality = entry.quality,
                    instanceId = entry.instanceId
                };
            }
            return result;
        }

        private static ActivityStagedRewardSaveData CloneStagedReward(ActivityStagedRewardSaveData source)
        {
            if (source == null)
                return null;
            return new ActivityStagedRewardSaveData
            {
                rewardType = source.rewardType,
                targetId = source.targetId,
                quantity = source.quantity,
                origin = source.origin,
                quality = source.quality,
                instanceId = source.instanceId
            };
        }

        private static LinkedCombatStartRequestSaveData CloneLinkedCombat(LinkedCombatStartRequestSaveData source)
        {
            if (source == null)
                return null;
            return new LinkedCombatStartRequestSaveData
            {
                requestId = source.requestId,
                rootExecutionId = source.rootExecutionId,
                occupationOwnerId = source.occupationOwnerId,
                heroId = source.heroId,
                dangerEncounterId = source.dangerEncounterId,
                enemyGroupId = source.enemyGroupId,
                combatMode = source.combatMode,
                enemyExpTargetId = source.enemyExpTargetId,
                defeatLossRule = source.defeatLossRule,
                suppressFatigueCost = source.suppressFatigueCost,
                combatExecutionId = source.combatExecutionId,
                resolved = source.resolved,
                loot = CloneStagedRewards(source.loot)
            };
        }

        private static bool IsLootRewardType(string rewardType)
        {
            if (!ActivityTypeParser.TryParseRewardType(rewardType, out var type))
                return false;
            return type == RewardTypeEnum.Resource || type == RewardTypeEnum.Item ||
                   type == RewardTypeEnum.Consumable || type == RewardTypeEnum.Equipment ||
                   type == RewardTypeEnum.Recipe;
        }

        private static bool HeroOwnsSkill(HeroConfigDto hero, string skillId)
        {
            foreach (var owned in hero?.uniqueSkillIds ?? Array.Empty<string>())
                if (string.Equals(owned, skillId, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool ConditionMatchesCategory(string condition, string category)
        {
            const string prefix = "activity_category=";
            return !string.IsNullOrWhiteSpace(condition) && condition.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(condition.Substring(prefix.Length).Trim(), category, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWork(ActivityRuntimeInfo info) => info != null && string.Equals(info.activityType, RuntimeKindWork, StringComparison.OrdinalIgnoreCase);

        private static bool TryGetRuntimeInfo(string activityId, List<ActivityRequirementIssue> issues, out ActivityRuntimeInfo info)
        {
            info = null;
            if (!ActivityResolverUtilities.TryGetActivity(activityId, issues, out var activity))
                return false;
            var duration = activity.durationSec > 0 ? activity.durationSec : activity.cycleSec;
            if (duration <= 0)
            {
                AddIssue(issues, activityId, "ActivityDuration", activityId, 1, duration, true, false, $"Activity '{activityId}' has no positive durationSec or cycleSec.");
                return false;
            }
            info = new ActivityRuntimeInfo { activityId = activity.id, activityType = activity.type, durationSeconds = duration, isRepeatable = activity.isRepeatable, requiresHero = true, activity = activity };
            return true;
        }

        private ActivityStartResult NewStartResult(ActivityStartRequest request)
        {
            var executionId = NewExecutionId();
            var context = new ActivityExecutionContext { activityId = request?.activityId, heroId = request?.heroId, executionId = executionId, startedAtUnixSeconds = UnixNow() };
            return new ActivityStartResult { executionId = executionId, context = context };
        }

        private ActivityExecutionContext ToContext(ActivityExecutionSaveData execution) => new ActivityExecutionContext { activityId = execution.activityId, heroId = execution.heroId, executionId = execution.executionId, startedAtUnixSeconds = execution.startedAtUnixSeconds };

        private ActivityExecutionSnapshot ToSnapshot(ActivityExecutionSaveData execution)
        {
            var duration = 0f;
            if (string.Equals(execution.runtimeKind, RuntimeKindBuild, StringComparison.Ordinal) && RuntimeConfigs.Buildings.TryGetBuildAction(execution.activityId, out var build))
                duration = build.buildPointsRequired;
            else if (TryGetRuntimeInfo(execution.activityId, out var info))
                duration = info.durationSeconds;
            var progressValue = string.Equals(execution.runtimeKind, RuntimeKindBuild, StringComparison.Ordinal) ? execution.accumulatedBuildPoints : execution.elapsedSeconds;
            return new ActivityExecutionSnapshot
            {
                executionId = execution.executionId,
                activityId = execution.activityId,
                runtimeKind = execution.runtimeKind,
                heroId = execution.heroId,
                status = execution.status,
                elapsedSeconds = execution.elapsedSeconds,
                durationSeconds = duration,
                progress = duration > 0 ? Mathf.Clamp01(progressValue / duration) : 0f,
                remainingSeconds = Math.Max(0f, duration - progressValue),
                completedCycles = execution.completedCycles,
                plannedCycles = execution.plannedCycles,
                currentCycleFatiguePaid = execution.currentCycleFatiguePaid,
                cyclePhase = execution.cyclePhase,
                completionPhase = execution.completionPhase,
                endReason = execution.endReason,
                accumulatedBuildPoints = execution.accumulatedBuildPoints,
                linkedCombat = execution.linkedCombat,
                pendingResultId = execution.pendingResultId,
                startedAtUnixSeconds = execution.startedAtUnixSeconds
            };
        }

        private long GetPendingResultRevision(ActivityExecutionSaveData execution)
        {
            if (execution == null || string.IsNullOrWhiteSpace(execution.pendingResultId))
                return 0;
            return _activityState.PendingResults.Get(execution.pendingResultId)?.revision ?? 0;
        }

        private ActivityExecutionSaveData FindLinkedExecution(string requestId)
        {
            foreach (var execution in GetExecutions())
                if (execution?.linkedCombat != null && string.Equals(execution.linkedCombat.requestId, requestId, StringComparison.Ordinal)) return execution;
            return null;
        }

        private static string LinkedCombatResolutionReceiptAggregateId(string requestId) => $"linked-combat-resolution:{requestId}";

        private LinkedCombatGatewayResult GatewaySuccess(ActivityExecutionSaveData execution, bool replayed, string completedActivityId) => new LinkedCombatGatewayResult
        {
            success = true,
            replayed = replayed,
            code = replayed ? "Existing" : "Applied",
            completedActivityId = completedActivityId,
            events = string.IsNullOrWhiteSpace(completedActivityId)
                ? Array.Empty<ActivityRuntimeEvent>()
                : new[] { new ActivityRuntimeEvent { eventType = ActivityRuntimeEventType.ActivityCompleted, targetId = completedActivityId, value = 1 } },
            request = execution?.linkedCombat,
            snapshot = GetSnapshot()
        };

        private LinkedCombatGatewayResult GatewayFailure(string code, string message) => new LinkedCombatGatewayResult { success = false, code = code, message = message, snapshot = GetSnapshot() };

        private ActivityExecutionSaveData[] GetExecutions() => _store.GetActivityExecutions();
        private ActivityExecutionSaveData GetExecution(string executionId) => _store.GetActivityExecution(executionId);
        private bool AddExecution(ActivityExecutionSaveData execution) => _store.AddActivityExecution(execution);
        private bool UpdateExecution(ActivityExecutionSaveData execution) => _store.UpdateActivityExecution(execution);
        private bool RemoveExecution(string executionId) => _store.RemoveActivityExecution(executionId);
        private bool Save() => _store.Save();

        private void NotifyEventSink(ActivityRuntimeEvent runtimeEvent)
        {
            if (_eventSink == null || runtimeEvent == null)
                return;
            try
            {
                _eventSink(runtimeEvent);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[ActivityRuntime] Event sink failed for '{runtimeEvent.eventType}:{runtimeEvent.targetId}': {exception.Message}");
            }
        }

        private ActivityStartResult FinishStart(ActivityStartResult result, List<ActivityRequirementIssue> issues, bool success)
        {
            result.success = success && !HasBlockingIssues(issues);
            result.issues = issues.ToArray();
            result.snapshot = GetSnapshot();
            return result;
        }

        private DangerEncounterPreparationResult FinishDangerEncounterPreparation(
            bool success,
            string code,
            string executionId,
            DangerHandoffMutationResult handoff,
            List<ActivityRequirementIssue> issues,
            List<PendingResultDeferredResolvedEvent> deferredResolvedEvents)
        {
            var execution = string.IsNullOrWhiteSpace(executionId) ? null : GetExecution(executionId);
            var finalSuccess = success && !HasBlockingIssues(issues);
            return new DangerEncounterPreparationResult(
                finalSuccess,
                code,
                executionId,
                handoff?.RequestId ?? execution?.linkedCombat?.requestId,
                handoff?.PendingResultId ??
                (execution?.activityBagResolved == true ? string.Empty : execution?.pendingResultId),
                handoff?.CombatEntryCount ?? execution?.linkedCombat?.loot?.Length ?? 0,
                handoff?.NonCombatEntryCount ?? execution?.dangerNonCombatEntryCount ?? 0,
                execution?.status ?? CoreActivityRuntimeStatus.None,
                execution?.cyclePhase,
                handoff?.ActivityBagResolved ?? execution?.activityBagResolved ?? false,
                finalSuccess && handoff?.RequestCreated == true,
                finalSuccess && handoff?.Replayed == true,
                Array.AsReadOnly(issues?.ToArray() ?? Array.Empty<ActivityRequirementIssue>()),
                finalSuccess
                    ? Array.AsReadOnly(deferredResolvedEvents?.ToArray() ??
                                       Array.Empty<PendingResultDeferredResolvedEvent>())
                    : Array.AsReadOnly(Array.Empty<PendingResultDeferredResolvedEvent>()));
        }

        private WorkAdvanceResult FinishWorkAdvance(
            bool success,
            WorkAdvanceStopReason stopReason,
            string executionId,
            int processedCycles,
            long availableSeconds,
            long remainingSeconds,
            ActivityExecutionSaveData execution,
            List<ActivityRequirementIssue> issues,
            List<PendingResultDeferredResolvedEvent> deferredResolvedEvents)
        {
            var storedExecution = string.IsNullOrWhiteSpace(executionId) ? null : GetExecution(executionId);
            var finalExecution = storedExecution ?? execution;
            var planCompleted = finalExecution != null && finalExecution.plannedCycles > 0 &&
                                finalExecution.completedCycles >= finalExecution.plannedCycles;
            var executionStatus = storedExecution?.status ??
                                  (planCompleted || stopReason == WorkAdvanceStopReason.InsufficientFatigue
                                      ? CoreActivityRuntimeStatus.Completed
                                      : finalExecution?.status ?? CoreActivityRuntimeStatus.None);
            var partialCycle = finalExecution != null &&
                               executionStatus == CoreActivityRuntimeStatus.Running &&
                               finalExecution.elapsedSeconds > 0f &&
                               !string.Equals(finalExecution.cyclePhase, CyclePhaseResultStaged, StringComparison.Ordinal) &&
                               !planCompleted;
            return new WorkAdvanceResult(
                success && !HasBlockingIssues(issues),
                stopReason,
                executionId,
                processedCycles,
                Math.Max(0L, availableSeconds - remainingSeconds),
                Math.Max(0L, remainingSeconds),
                executionStatus,
                partialCycle,
                planCompleted,
                Array.AsReadOnly(issues?.ToArray() ?? Array.Empty<ActivityRequirementIssue>()),
                Array.AsReadOnly(deferredResolvedEvents?.ToArray() ??
                                 Array.Empty<PendingResultDeferredResolvedEvent>()));
        }

        private ConstructionAdvanceResult FinishConstructionAdvance(
            bool success,
            ConstructionAdvanceStopReason stopReason,
            string executionId,
            long availableSeconds,
            long remainingSeconds,
            float addedBuildPoints,
            ActivityExecutionSaveData execution,
            List<ActivityRequirementIssue> issues,
            List<ActivityRuntimeEvent> deferredEvents,
            List<PendingResultDeferredResolvedEvent> deferredResolvedEvents)
        {
            var storedExecution = string.IsNullOrWhiteSpace(executionId) ? null : GetExecution(executionId);
            var completed = stopReason == ConstructionAdvanceStopReason.ConstructionCompleted;
            var finalSuccess = success && !HasBlockingIssues(issues);
            var executionStatus = storedExecution?.status ??
                                  (completed
                                      ? CoreActivityRuntimeStatus.Completed
                                      : execution?.status ?? CoreActivityRuntimeStatus.None);
            var completionPhase = storedExecution?.completionPhase ?? (completed ? null : execution?.completionPhase);
            return new ConstructionAdvanceResult(
                finalSuccess,
                stopReason,
                executionId,
                Math.Max(0L, availableSeconds),
                Math.Max(0L, availableSeconds - remainingSeconds),
                Math.Max(0L, remainingSeconds),
                Math.Max(0f, addedBuildPoints),
                executionStatus,
                completionPhase,
                completed,
                Array.AsReadOnly(issues?.ToArray() ?? Array.Empty<ActivityRequirementIssue>()),
                finalSuccess
                    ? Array.AsReadOnly(deferredEvents?.ToArray() ?? Array.Empty<ActivityRuntimeEvent>())
                    : Array.AsReadOnly(Array.Empty<ActivityRuntimeEvent>()),
                finalSuccess
                    ? Array.AsReadOnly(deferredResolvedEvents?.ToArray() ??
                                       Array.Empty<PendingResultDeferredResolvedEvent>())
                    : Array.AsReadOnly(Array.Empty<PendingResultDeferredResolvedEvent>()));
        }

        private ActivityTickResult FinishTick(ActivityTickResult result, List<ActivityRequirementIssue> issues, List<ActivityRewardResult> rewards, List<ActivityRuntimeEvent> events, bool changed)
        {
            result.success = !HasBlockingIssues(issues);
            result.issues = issues.ToArray();
            result.rewardResults = rewards.ToArray();
            result.events = events.ToArray();
            result.saved = result.success && changed && Save();
            result.snapshot = GetSnapshot();
            if (result.success && (!changed || result.saved))
                _linkedCombatCoordinator?.Reconcile();
            return result;
        }

        private ActivityCompleteResult FinishComplete(ActivityCompleteResult result, List<ActivityRequirementIssue> issues, List<ActivityRewardResult> rewards, List<ActivityRuntimeEvent> events, bool changed)
        {
            result.success = !HasBlockingIssues(issues);
            result.issues = issues.ToArray();
            result.rewardResults = rewards.ToArray();
            result.events = events.ToArray();
            result.saved = changed && Save();
            result.snapshot = GetSnapshot();
            return result;
        }

        private ActivityCancelResult FinishCancel(ActivityCancelResult result, List<ActivityRequirementIssue> issues, List<ActivityRuntimeEvent> events, bool changed, bool success)
        {
            result.success = success && !HasBlockingIssues(issues);
            result.issues = issues.ToArray();
            result.events = events.ToArray();
            result.saved = changed;
            result.snapshot = GetSnapshot();
            return result;
        }

        private static bool HasBlockingIssues(List<ActivityRequirementIssue> issues)
        {
            foreach (var issue in issues)
                if (!string.Equals(issue.issueType, "TickCycleLimitReached", StringComparison.Ordinal)) return true;
            return false;
        }

        private static void AddIssue(List<ActivityRequirementIssue> issues, string activityId, string issueType, string targetId, int requiredAmount, long currentAmount, bool isError, bool isNotImplemented, string message)
        {
            if (issues == null)
                return;
            ActivityResolverUtilities.AddIssue(issues, activityId, issueType, targetId, requiredAmount, currentAmount, isError, isNotImplemented, message);
        }

        private static string NewExecutionId() => $"activity_{Guid.NewGuid():N}";
        private static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private enum DangerOutcome
        {
            None,
            Missed,
            Triggered,
            Failed
        }
    }

    public sealed class DangerEncounterPreparationProcessor : IDisposable
    {
        private readonly ITransactionalPendingResultService _pendingResults;
        private readonly ActivityRuntimeService _core;

        public DangerEncounterPreparationProcessor(
            IActivityRuntimeStore store,
            IActivityPlayerState activityState,
            FormulaRuntime formulas = null)
        {
            if (activityState == null)
                throw new ArgumentNullException(nameof(activityState));

            _pendingResults = activityState.PendingResults as ITransactionalPendingResultService ??
                              throw new ArgumentException(
                                  "PendingResult service must support outer transactions.",
                                  nameof(activityState));
            _core = ActivityRuntimeService.CreateDangerEncounterPreparationCore(
                store,
                activityState,
                formulas);
        }

        public DangerEncounterPreparationResult Prepare(DangerEncounterPreparationRequest request) =>
            _core.PrepareDangerEncounterCore(request);

        public void PublishDeferredResolvedEvents(DangerEncounterPreparationResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (!result.Success || !result.TryMarkDeferredEventsPublished())
                return;
            foreach (var deferredEvent in result.DeferredResolvedEvents)
                _pendingResults.PublishDeferred(deferredEvent);
        }

        public void Dispose() => _core.Dispose();
    }

    public sealed class WorkAdvanceProcessor : IDisposable
    {
        private readonly ITransactionalActivityRandom _random;
        private readonly ITransactionalPendingResultService _pendingResults;
        private readonly ActivityRuntimeService _core;

        public WorkAdvanceProcessor(
            IActivityRuntimeStore store,
            IActivityPlayerState activityState,
            ITransactionalActivityRandom random,
            FormulaRuntime formulas = null)
        {
            if (activityState == null)
                throw new ArgumentNullException(nameof(activityState));

            _random = random ?? throw new ArgumentNullException(nameof(random));
            _pendingResults = activityState.PendingResults as ITransactionalPendingResultService ??
                              throw new ArgumentException(
                                  "PendingResult service must support outer transactions.",
                                  nameof(activityState));
            _core = ActivityRuntimeService.CreateWorkAdvanceCore(store, activityState, random, formulas);
        }

        public WorkAdvanceResult Advance(WorkAdvanceRequest request) => _core.AdvanceWorkCore(request);

        public ActivityRandomState CaptureRandomState() => _random.CaptureState();

        public void RestoreRandomState(ActivityRandomState state) => _random.RestoreState(state);

        public void PublishDeferredResolvedEvents(WorkAdvanceResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            foreach (var deferredEvent in result.DeferredResolvedEvents)
                _pendingResults.PublishDeferred(deferredEvent);
        }

        public void Dispose() => _core.Dispose();
    }

    public sealed class ConstructionAdvanceProcessor : IDisposable
    {
        private readonly ITransactionalPendingResultService _pendingResults;
        private readonly Action<ActivityRuntimeEvent> _eventSink;
        private readonly ActivityRuntimeService _core;

        public ConstructionAdvanceProcessor(
            IActivityRuntimeStore store,
            IActivityPlayerState activityState,
            IActivityRuntimeProgressionProcessor progressionProcessor,
            FormulaRuntime formulas = null,
            Action<ActivityRuntimeEvent> eventSink = null)
        {
            if (activityState == null)
                throw new ArgumentNullException(nameof(activityState));

            _pendingResults = activityState.PendingResults as ITransactionalPendingResultService ??
                              throw new ArgumentException(
                                  "PendingResult service must support outer transactions.",
                                  nameof(activityState));
            _eventSink = eventSink;
            _core = ActivityRuntimeService.CreateConstructionAdvanceCore(
                store,
                activityState,
                formulas,
                progressionProcessor);
        }

        public ConstructionAdvanceResult Advance(ConstructionAdvanceRequest request) =>
            _core.AdvanceConstructionCore(request);

        public void PublishDeferredEvents(ConstructionAdvanceResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (!result.Success)
                return;
            if (!result.TryMarkDeferredEventsPublished())
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
                        $"[ConstructionAdvance] Deferred event sink failed for '{runtimeEvent?.eventType}:{runtimeEvent?.targetId}': {exception.Message}");
                }
            }
        }

        public void Dispose() => _core.Dispose();
    }
}
