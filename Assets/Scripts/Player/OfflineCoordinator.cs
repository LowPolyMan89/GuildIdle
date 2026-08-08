using System;
using System.Collections.Generic;
using GuildIdle.Activities;
using GuildIdle.Combat;
using GuildIdle.Core;
using GuildIdle.Crafting;

namespace GuildIdle.Player
{
    public enum OfflineCoordinatorCode
    {
        BaselineInitialized,
        Applied,
        AppliedWithPostCommitErrors,
        NoElapsedTime,
        ClockRollback,
        ValidationFailed,
        DataIntegrityFailure,
        ProcessingLimitReached,
        ProcessorFailed,
        SaveFailed,
        RuntimeError
    }

    public enum OfflineCoordinatorStage
    {
        None = 0,
        Validation = 1,
        Work = 2,
        Danger = 3,
        Construction = 4,
        Craft = 5,
        Fatigue = 6,
        Save = 7,
        PostCommit = 8
    }

    public sealed class OfflineCoordinatorIssue
    {
        internal OfflineCoordinatorIssue(
            string code,
            OfflineCoordinatorStage stage,
            string executionId,
            string message)
        {
            Code = code ?? string.Empty;
            Stage = stage;
            ExecutionId = executionId ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string Code { get; }
        public OfflineCoordinatorStage Stage { get; }
        public string ExecutionId { get; }
        public string Message { get; }
    }

    public sealed class OfflineProcessorSummary
    {
        internal OfflineProcessorSummary(int attempted, int succeeded, int completed, int partial, int noOp)
        {
            Attempted = attempted;
            Succeeded = succeeded;
            Completed = completed;
            Partial = partial;
            NoOp = noOp;
        }

        public int Attempted { get; }
        public int Succeeded { get; }
        public int Completed { get; }
        public int Partial { get; }
        public int NoOp { get; }
    }

    public sealed class OfflineFatigueSummary
    {
        private readonly HeroFatigueRecoveryResult[] _recoveries;

        internal OfflineFatigueSummary(
            int eligibleHeroCount,
            int restoredFatigue,
            HeroFatigueRecoveryResult[] recoveries)
        {
            EligibleHeroCount = eligibleHeroCount;
            RestoredFatigue = restoredFatigue;
            _recoveries = recoveries ?? Array.Empty<HeroFatigueRecoveryResult>();
        }

        public int EligibleHeroCount { get; }
        public int RestoredFatigue { get; }
        public IReadOnlyList<HeroFatigueRecoveryResult> Recoveries => Array.AsReadOnly(_recoveries);
    }

    public sealed class OfflineCoordinatorReport
    {
        private readonly string[] _processedExecutionIds;
        private readonly OfflineCoordinatorIssue[] _issues;

        internal OfflineCoordinatorReport(
            bool success,
            OfflineCoordinatorCode code,
            bool stateCommitted,
            bool saved,
            long fromUtcSeconds,
            long toUtcSeconds,
            long deltaSeconds,
            OfflineProcessorSummary work,
            OfflineProcessorSummary danger,
            OfflineProcessorSummary construction,
            OfflineProcessorSummary craft,
            OfflineFatigueSummary fatigue,
            string[] processedExecutionIds,
            int deferredEventCount,
            int attemptedEventCount,
            int publishedEventCount,
            int failedEventCount,
            OfflineCoordinatorStage failedStage,
            string failedExecutionId,
            OfflineCoordinatorIssue[] issues)
        {
            Success = success;
            Code = code;
            StateCommitted = stateCommitted;
            Saved = saved;
            FromUtcSeconds = fromUtcSeconds;
            ToUtcSeconds = toUtcSeconds;
            DeltaSeconds = deltaSeconds;
            Work = work ?? EmptySummary;
            Danger = danger ?? EmptySummary;
            Construction = construction ?? EmptySummary;
            Craft = craft ?? EmptySummary;
            Fatigue = fatigue ?? EmptyFatigue;
            _processedExecutionIds = processedExecutionIds ?? Array.Empty<string>();
            DeferredEventCount = deferredEventCount;
            AttemptedEventCount = attemptedEventCount;
            PublishedEventCount = publishedEventCount;
            FailedEventCount = failedEventCount;
            FailedStage = failedStage;
            FailedExecutionId = failedExecutionId ?? string.Empty;
            _issues = issues ?? Array.Empty<OfflineCoordinatorIssue>();
        }

        private static OfflineProcessorSummary EmptySummary => new OfflineProcessorSummary(0, 0, 0, 0, 0);
        private static OfflineFatigueSummary EmptyFatigue =>
            new OfflineFatigueSummary(0, 0, Array.Empty<HeroFatigueRecoveryResult>());

        public bool Success { get; }
        public OfflineCoordinatorCode Code { get; }
        public bool StateCommitted { get; }
        public bool Saved { get; }
        public long FromUtcSeconds { get; }
        public long ToUtcSeconds { get; }
        public long DeltaSeconds { get; }
        public OfflineProcessorSummary Work { get; }
        public OfflineProcessorSummary Danger { get; }
        public OfflineProcessorSummary Construction { get; }
        public OfflineProcessorSummary Craft { get; }
        public OfflineFatigueSummary Fatigue { get; }
        public IReadOnlyList<string> ProcessedExecutionIds => Array.AsReadOnly(_processedExecutionIds);
        public int DeferredEventCount { get; }
        public int AttemptedEventCount { get; }
        public int PublishedEventCount { get; }
        public int FailedEventCount { get; }
        public OfflineCoordinatorStage FailedStage { get; }
        public string FailedExecutionId { get; }
        public IReadOnlyList<OfflineCoordinatorIssue> Issues => Array.AsReadOnly(_issues);
    }

    public sealed class OfflineCoordinator : IDisposable
    {
        private const string RuntimeKindWork = "Work";
        private const string RuntimeKindBuild = "Build";
        private const string CyclePhaseResultStaged = "ResultStaged";

        private readonly PlayerState _state;
        private readonly WorkAdvanceProcessor _work;
        private readonly DangerEncounterPreparationProcessor _danger;
        private readonly ConstructionAdvanceProcessor _construction;
        private readonly CraftAdvanceProcessor _craft;
        private readonly ITransactionalPendingResultService _pendingResults;
        private readonly Action<ActivityRuntimeEvent> _activityEventSink;
        private readonly Action<CraftResultPendingEvent> _craftEventSink;
        private readonly Action<OfflineCoordinatorReport> _diagnosticSink;
        private readonly int _workOperationLimit;
        private readonly int _constructionOperationLimit;
        private bool _disposed;

        public OfflineCoordinator(
            PlayerState state,
            WorkAdvanceProcessor work,
            DangerEncounterPreparationProcessor danger,
            ConstructionAdvanceProcessor construction,
            CraftAdvanceProcessor craft,
            Action<ActivityRuntimeEvent> activityEventSink = null,
            Action<CraftResultPendingEvent> craftEventSink = null,
            Action<OfflineCoordinatorReport> diagnosticSink = null,
            int workOperationLimit = ActivityRuntimeService.DefaultWorkAdvanceOperationLimit,
            int constructionOperationLimit = ActivityRuntimeService.DefaultConstructionAdvanceOperationLimit)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _work = work ?? throw new ArgumentNullException(nameof(work));
            _danger = danger ?? throw new ArgumentNullException(nameof(danger));
            _construction = construction ?? throw new ArgumentNullException(nameof(construction));
            _craft = craft ?? throw new ArgumentNullException(nameof(craft));
            _pendingResults = state.PendingResults as ITransactionalPendingResultService ??
                              throw new ArgumentException(
                                  "PendingResult service must support outer transactions.",
                                  nameof(state));
            _activityEventSink = activityEventSink;
            _craftEventSink = craftEventSink;
            _diagnosticSink = diagnosticSink;
            if (workOperationLimit <= 0)
                throw new ArgumentOutOfRangeException(nameof(workOperationLimit));
            if (constructionOperationLimit <= 0)
                throw new ArgumentOutOfRangeException(nameof(constructionOperationLimit));
            _workOperationLimit = workOperationLimit;
            _constructionOperationLimit = constructionOperationLimit;
        }

        public OfflineCoordinatorReport Run()
        {
            ThrowIfDisposed();

            TimeAdvancePlan timePlan;
            try
            {
                // PrepareAdvance is the only wall-clock read in the entire pass.
                timePlan = _state.TimeProgress.PrepareAdvance();
            }
            catch (Exception exception)
            {
                return PublishDiagnostic(FailureWithoutPlan(
                    OfflineCoordinatorCode.RuntimeError,
                    OfflineCoordinatorStage.None,
                    string.Empty,
                    "TimePlanFailed",
                    $"Failed to prepare the offline time plan: {exception.Message}"));
            }

            if (timePlan.Code == TimeAdvanceResultCode.NoElapsedTime)
                return PublishDiagnostic(NoOp(timePlan, OfflineCoordinatorCode.NoElapsedTime));
            if (timePlan.Code == TimeAdvanceResultCode.ClockRollback)
                return PublishDiagnostic(NoOp(timePlan, OfflineCoordinatorCode.ClockRollback));

            var context = new PassContext(timePlan);
            var checkpoint = _state.ToSaveData();
            var randomCheckpoint = _work.CaptureRandomState();

            try
            {
                if (timePlan.Code == TimeAdvanceResultCode.BaselineInitialized)
                    return PublishDiagnostic(InitializeBaseline(context, checkpoint, randomCheckpoint));
                if (timePlan.Code != TimeAdvanceResultCode.Applied || timePlan.DeltaSeconds <= 0L)
                {
                    return PublishDiagnostic(Rollback(
                        context,
                        checkpoint,
                        randomCheckpoint,
                        OfflineCoordinatorCode.RuntimeError,
                        OfflineCoordinatorStage.None,
                        string.Empty,
                        "InvalidTimePlan",
                        $"Unsupported time plan '{timePlan.Code}'."));
                }

                var eligibility = _state.TimeProgress.CaptureEligibilitySnapshot();
                context.EligibleHeroCount = eligibility.EligibleHeroIds.Count;
                var activitySnapshot = _state.GetActivityExecutions();
                var craftSnapshot = _state.GetCraftExecutions();
                var combatSnapshot = _state.GetCombatAggregates();
                SortActivityExecutions(activitySnapshot);
                SortCraftExecutions(craftSnapshot);
                SortCombatAggregates(combatSnapshot);

                if (!ValidateSnapshot(activitySnapshot, craftSnapshot, combatSnapshot, context.Issues))
                {
                    return PublishDiagnostic(FailWithoutMutation(
                        context,
                        OfflineCoordinatorCode.DataIntegrityFailure,
                        OfflineCoordinatorStage.Validation,
                        FirstIssueExecutionId(context.Issues)));
                }

                var failed = ProcessWork(activitySnapshot, timePlan.DeltaSeconds, context) ??
                             ProcessDanger(activitySnapshot, context) ??
                             ProcessConstruction(activitySnapshot, timePlan.DeltaSeconds, context) ??
                             ProcessCraft(craftSnapshot, timePlan.DeltaSeconds, context);
                if (failed != null)
                {
                    return PublishDiagnostic(Rollback(
                        context,
                        checkpoint,
                        randomCheckpoint,
                        failed.Code,
                        failed.Stage,
                        failed.ExecutionId,
                        null,
                        null));
                }

                var timeResult = _state.TimeProgress.Apply(timePlan, eligibility);
                if (!timeResult.Success || timeResult.Code != TimeAdvanceResultCode.Applied)
                {
                    return PublishDiagnostic(Rollback(
                        context,
                        checkpoint,
                        randomCheckpoint,
                        OfflineCoordinatorCode.RuntimeError,
                        OfflineCoordinatorStage.Fatigue,
                        string.Empty,
                        "TimeApplyFailed",
                        $"Failed to apply time plan: {timeResult.Code}."));
                }

                context.SetFatigue(timeResult);
                if (!_state.Save())
                {
                    return PublishDiagnostic(Rollback(
                        context,
                        checkpoint,
                        randomCheckpoint,
                        OfflineCoordinatorCode.SaveFailed,
                        OfflineCoordinatorStage.Save,
                        string.Empty,
                        "SaveFailed",
                        "Failed to persist the offline pass."));
                }

                context.Saved = true;
                context.StateCommitted = true;
                PublishDeferredEvents(context);
                context.Code = context.FailedEventCount == 0
                    ? OfflineCoordinatorCode.Applied
                    : OfflineCoordinatorCode.AppliedWithPostCommitErrors;
                context.Success = true;
                return PublishDiagnostic(context.ToReport());
            }
            catch (Exception exception)
            {
                return PublishDiagnostic(Rollback(
                    context,
                    checkpoint,
                    randomCheckpoint,
                    OfflineCoordinatorCode.RuntimeError,
                    OfflineCoordinatorStage.None,
                    string.Empty,
                    "RuntimeError",
                    $"Offline pass failed: {exception.Message}"));
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _work.Dispose();
            _danger.Dispose();
            _construction.Dispose();
            _disposed = true;
        }

        private OfflineCoordinatorReport InitializeBaseline(
            PassContext context,
            SaveData checkpoint,
            ActivityRandomState randomCheckpoint)
        {
            var eligibility = _state.TimeProgress.CaptureEligibilitySnapshot();
            context.EligibleHeroCount = eligibility.EligibleHeroIds.Count;
            var applied = _state.TimeProgress.Apply(context.TimePlan, eligibility);
            if (!applied.Success || applied.Code != TimeAdvanceResultCode.BaselineInitialized)
            {
                return Rollback(
                    context,
                    checkpoint,
                    randomCheckpoint,
                    OfflineCoordinatorCode.RuntimeError,
                    OfflineCoordinatorStage.Fatigue,
                    string.Empty,
                    "BaselineApplyFailed",
                    $"Failed to initialize the time baseline: {applied.Code}.");
            }

            if (!_state.Save())
            {
                return Rollback(
                    context,
                    checkpoint,
                    randomCheckpoint,
                    OfflineCoordinatorCode.SaveFailed,
                    OfflineCoordinatorStage.Save,
                    string.Empty,
                    "SaveFailed",
                    "Failed to persist the initialized time baseline.");
            }

            context.Success = true;
            context.Code = OfflineCoordinatorCode.BaselineInitialized;
            context.Saved = true;
            context.StateCommitted = true;
            return context.ToReport();
        }

        private ProcessorFailure ProcessWork(
            ActivityExecutionSaveData[] snapshot,
            long deltaSeconds,
            PassContext context)
        {
            foreach (var execution in snapshot)
            {
                if (execution == null || execution.status != ActivityRuntimeStatus.Running ||
                    !string.Equals(execution.runtimeKind, RuntimeKindWork, StringComparison.Ordinal))
                    continue;

                context.Work.Attempted++;
                context.AddProcessedExecution(execution.executionId);
                var result = _work.Advance(new WorkAdvanceRequest(
                    execution.executionId,
                    deltaSeconds,
                    _workOperationLimit));
                AddActivityIssues(result?.Issues, OfflineCoordinatorStage.Work, execution.executionId, context.Issues);
                if (result == null)
                    return ProcessorFailure.Runtime(OfflineCoordinatorStage.Work, execution.executionId);
                if (result.ProcessingLimitReached)
                {
                    context.Issues.Add(new OfflineCoordinatorIssue(
                        "ProcessingLimitReached",
                        OfflineCoordinatorStage.Work,
                        execution.executionId,
                        $"Work processor left {result.RemainingSeconds} seconds unprocessed."));
                    return new ProcessorFailure(
                        OfflineCoordinatorCode.ProcessingLimitReached,
                        OfflineCoordinatorStage.Work,
                        execution.executionId);
                }
                if (!result.Success)
                    return ProcessorFailure.FromActivityResult(result.StopReason, OfflineCoordinatorStage.Work, execution.executionId);

                context.Work.Succeeded++;
                if (result.PlanCompleted || result.ExecutionStatus == ActivityRuntimeStatus.ResultPending)
                    context.Work.Completed++;
                else if (result.HasPartialCycle || result.StopReason == WorkAdvanceStopReason.IntervalExhausted)
                    context.Work.Partial++;
                else
                    context.Work.NoOp++;
                AddPendingEvents(result.DeferredResolvedEvents, OfflineCoordinatorStage.Work, execution.executionId, context);
            }

            return null;
        }

        private ProcessorFailure ProcessDanger(ActivityExecutionSaveData[] snapshot, PassContext context)
        {
            foreach (var original in snapshot)
            {
                if (original == null)
                    continue;
                var execution = _state.GetActivityExecution(original.executionId);
                if (!IsDangerCandidate(execution))
                    continue;

                context.Danger.Attempted++;
                context.AddProcessedExecution(execution.executionId);
                var result = _danger.Prepare(new DangerEncounterPreparationRequest(execution.executionId));
                AddActivityIssues(result?.Issues, OfflineCoordinatorStage.Danger, execution.executionId, context.Issues);
                if (result == null)
                    return ProcessorFailure.Runtime(OfflineCoordinatorStage.Danger, execution.executionId);
                if (!result.Success)
                {
                    var code = string.Equals(result.Code, DangerEncounterPreparationCode.DataIntegrityFailure, StringComparison.Ordinal)
                        ? OfflineCoordinatorCode.DataIntegrityFailure
                        : string.Equals(result.Code, DangerEncounterPreparationCode.ValidationFailed, StringComparison.Ordinal)
                            ? OfflineCoordinatorCode.ValidationFailed
                            : OfflineCoordinatorCode.ProcessorFailed;
                    return new ProcessorFailure(code, OfflineCoordinatorStage.Danger, execution.executionId);
                }

                context.Danger.Succeeded++;
                if (string.Equals(result.Code, DangerEncounterPreparationCode.AlreadyPrepared, StringComparison.Ordinal) ||
                    result.Replayed)
                    context.Danger.NoOp++;
                else
                    context.Danger.Completed++;
                AddPendingEvents(result.DeferredResolvedEvents, OfflineCoordinatorStage.Danger, execution.executionId, context);
            }

            return null;
        }

        private ProcessorFailure ProcessConstruction(
            ActivityExecutionSaveData[] snapshot,
            long deltaSeconds,
            PassContext context)
        {
            foreach (var execution in snapshot)
            {
                if (execution == null || execution.status != ActivityRuntimeStatus.Running ||
                    !string.Equals(execution.runtimeKind, RuntimeKindBuild, StringComparison.Ordinal))
                    continue;

                context.Construction.Attempted++;
                context.AddProcessedExecution(execution.executionId);
                var result = _construction.Advance(
                    new ConstructionAdvanceRequest(
                        execution.executionId,
                        deltaSeconds,
                        _constructionOperationLimit));
                AddActivityIssues(result?.Issues, OfflineCoordinatorStage.Construction, execution.executionId, context.Issues);
                if (result == null)
                    return ProcessorFailure.Runtime(OfflineCoordinatorStage.Construction, execution.executionId);
                if (result.ProcessingLimitReached)
                {
                    context.Issues.Add(new OfflineCoordinatorIssue(
                        "ProcessingLimitReached",
                        OfflineCoordinatorStage.Construction,
                        execution.executionId,
                        $"Construction processor left {result.RemainingSeconds} seconds unprocessed."));
                    return new ProcessorFailure(
                        OfflineCoordinatorCode.ProcessingLimitReached,
                        OfflineCoordinatorStage.Construction,
                        execution.executionId);
                }
                if (!result.Success)
                    return ProcessorFailure.FromActivityResult(result.StopReason, OfflineCoordinatorStage.Construction, execution.executionId);

                context.Construction.Succeeded++;
                if (result.Completed)
                    context.Construction.Completed++;
                else if (result.AddedBuildPoints > 0f || result.StopReason == ConstructionAdvanceStopReason.IntervalExhausted)
                    context.Construction.Partial++;
                else
                    context.Construction.NoOp++;
                var localOrder = AddPendingEvents(
                    result.DeferredResolvedEvents,
                    OfflineCoordinatorStage.Construction,
                    execution.executionId,
                    context);
                foreach (var runtimeEvent in result.DeferredEvents)
                {
                    var captured = runtimeEvent;
                    context.DeferredEvents.Add(new DeferredEvent(
                        OfflineCoordinatorStage.Construction,
                        execution.executionId,
                        localOrder++,
                        () => _activityEventSink?.Invoke(captured)));
                }
            }

            return null;
        }

        private ProcessorFailure ProcessCraft(
            CraftExecutionSaveData[] snapshot,
            long deltaSeconds,
            PassContext context)
        {
            foreach (var execution in snapshot)
            {
                if (execution == null || execution.status != CraftExecutionStatus.Running)
                    continue;

                context.Craft.Attempted++;
                context.AddProcessedExecution(execution.executionId);
                var result = _craft.Advance(new CraftAdvanceRequest(
                    execution.executionId,
                    deltaSeconds,
                    $"offline:{context.TimePlan.PreviousUtcSeconds}:{context.TimePlan.NowUtcSeconds}"));
                AddCraftIssues(result?.Issues, execution.executionId, context.Issues);
                if (result == null)
                    return ProcessorFailure.Runtime(OfflineCoordinatorStage.Craft, execution.executionId);
                if (!result.Success)
                {
                    var code = HasDataIntegrityIssue(result.Issues)
                        ? OfflineCoordinatorCode.DataIntegrityFailure
                        : result.StopReason == CraftAdvanceStopReason.InvalidExecution
                            ? OfflineCoordinatorCode.ValidationFailed
                            : OfflineCoordinatorCode.ProcessorFailed;
                    return new ProcessorFailure(code, OfflineCoordinatorStage.Craft, execution.executionId);
                }

                context.Craft.Succeeded++;
                if (result.Completed || result.ExecutionStatus == CraftExecutionStatus.ResultPending)
                    context.Craft.Completed++;
                else if (result.StopReason == CraftAdvanceStopReason.AppliedPartial)
                    context.Craft.Partial++;
                else
                    context.Craft.NoOp++;
                var localOrder = AddPendingEvents(
                    result.DeferredResolvedEvents,
                    OfflineCoordinatorStage.Craft,
                    execution.executionId,
                    context);
                foreach (var runtimeEvent in result.DeferredEvents)
                {
                    var captured = runtimeEvent;
                    context.DeferredEvents.Add(new DeferredEvent(
                        OfflineCoordinatorStage.Craft,
                        execution.executionId,
                        localOrder++,
                        () => _craftEventSink?.Invoke(captured)));
                }
            }

            return null;
        }

        private void PublishDeferredEvents(PassContext context)
        {
            context.DeferredEvents.Sort(DeferredEvent.Compare);
            foreach (var deferredEvent in context.DeferredEvents)
            {
                context.AttemptedEventCount++;
                try
                {
                    deferredEvent.Publish();
                    context.PublishedEventCount++;
                }
                catch (Exception exception)
                {
                    context.FailedEventCount++;
                    context.Issues.Add(new OfflineCoordinatorIssue(
                        "PostCommitPublicationFailed",
                        OfflineCoordinatorStage.PostCommit,
                        deferredEvent.ExecutionId,
                        $"Deferred event publication failed: {exception.Message}"));
                }
            }
        }

        private int AddPendingEvents(
            IReadOnlyList<PendingResultDeferredResolvedEvent> events,
            OfflineCoordinatorStage stage,
            string executionId,
            PassContext context)
        {
            var localOrder = 0;
            foreach (var deferredEvent in events ?? Array.Empty<PendingResultDeferredResolvedEvent>())
            {
                var captured = deferredEvent;
                context.DeferredEvents.Add(new DeferredEvent(
                    stage,
                    executionId,
                    localOrder++,
                    () => _pendingResults.PublishDeferred(captured)));
            }

            return localOrder;
        }

        private OfflineCoordinatorReport Rollback(
            PassContext context,
            SaveData checkpoint,
            ActivityRandomState randomCheckpoint,
            OfflineCoordinatorCode code,
            OfflineCoordinatorStage failedStage,
            string failedExecutionId,
            string issueCode,
            string message)
        {
            _state.RestoreTransactional(checkpoint);
            _work.RestoreRandomState(randomCheckpoint);
            context.DeferredEvents.Clear();
            context.Success = false;
            context.Code = code;
            context.StateCommitted = false;
            context.Saved = false;
            context.FailedStage = failedStage;
            context.FailedExecutionId = failedExecutionId;
            if (!string.IsNullOrWhiteSpace(issueCode))
            {
                context.Issues.Add(new OfflineCoordinatorIssue(
                    issueCode,
                    failedStage,
                    failedExecutionId,
                    message));
            }
            return context.ToReport();
        }

        private static OfflineCoordinatorReport FailWithoutMutation(
            PassContext context,
            OfflineCoordinatorCode code,
            OfflineCoordinatorStage failedStage,
            string failedExecutionId)
        {
            context.DeferredEvents.Clear();
            context.Success = false;
            context.Code = code;
            context.StateCommitted = false;
            context.Saved = false;
            context.FailedStage = failedStage;
            context.FailedExecutionId = failedExecutionId ?? string.Empty;
            return context.ToReport();
        }

        private bool ValidateSnapshot(
            ActivityExecutionSaveData[] activities,
            CraftExecutionSaveData[] crafts,
            CombatRuntimeAggregate[] combats,
            List<OfflineCoordinatorIssue> issues)
        {
            var executionIds = new HashSet<string>(StringComparer.Ordinal);
            var claimsByHero = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var activitiesById = new Dictionary<string, ActivityExecutionSaveData>(StringComparer.Ordinal);

            foreach (var execution in activities)
            {
                if (execution == null || string.IsNullOrWhiteSpace(execution.executionId) ||
                    !executionIds.Add(execution.executionId))
                {
                    AddIntegrityIssue(issues, execution?.executionId, "Activity execution id is missing or duplicated.");
                    continue;
                }
                activitiesById[execution.executionId] = execution;
                if (execution.status == ActivityRuntimeStatus.Running ||
                    execution.status == ActivityRuntimeStatus.ResultPending)
                    AddClaim(claimsByHero, execution.heroId, execution.executionId, issues);
                ValidatePendingResult(
                    execution.status == ActivityRuntimeStatus.ResultPending,
                    execution.pendingResultId,
                    PendingResultSourceType.Activity,
                    execution.executionId,
                    issues);
            }

            foreach (var execution in crafts)
            {
                if (execution == null || string.IsNullOrWhiteSpace(execution.executionId) ||
                    !executionIds.Add(execution.executionId))
                {
                    AddIntegrityIssue(issues, execution?.executionId, "Craft execution id is missing or duplicated.");
                    continue;
                }
                if (execution.status == CraftExecutionStatus.Running ||
                    execution.status == CraftExecutionStatus.ResultPending)
                    AddClaim(claimsByHero, execution.heroId, execution.executionId, issues);
                ValidatePendingResult(
                    execution.status == CraftExecutionStatus.ResultPending,
                    execution.pendingResultId,
                    PendingResultSourceType.Craft,
                    execution.executionId,
                    issues);
            }

            foreach (var aggregate in combats)
            {
                var execution = aggregate?.execution;
                if (execution == null || string.IsNullOrWhiteSpace(execution.executionId) ||
                    !executionIds.Add(execution.executionId))
                {
                    AddIntegrityIssue(issues, execution?.executionId, "Combat execution id is missing or duplicated.");
                    continue;
                }
                if (execution.status == CombatExecutionStatus.Running ||
                    execution.status == CombatExecutionStatus.ResultPending)
                {
                    if (string.Equals(execution.occupationOwnerId, execution.executionId, StringComparison.Ordinal))
                    {
                        AddClaim(claimsByHero, execution.heroId, execution.executionId, issues);
                    }
                    else if (!activitiesById.TryGetValue(execution.occupationOwnerId, out var owner) ||
                             owner.linkedCombat == null ||
                             !string.Equals(owner.heroId, execution.heroId, StringComparison.Ordinal) ||
                             !string.Equals(owner.linkedCombat.requestId, execution.sourceRequestId, StringComparison.Ordinal) ||
                             !string.Equals(owner.linkedCombat.combatExecutionId, execution.executionId, StringComparison.Ordinal))
                    {
                        AddIntegrityIssue(
                            issues,
                            execution.executionId,
                            "Linked combat execution has an incompatible occupation owner or source request.");
                    }
                }
                ValidatePendingResult(
                    execution.status == CombatExecutionStatus.ResultPending,
                    execution.pendingResultId,
                    PendingResultSourceType.Combat,
                    execution.executionId,
                    issues);
            }

            foreach (var heroId in _state.GetOrderedHeroIds())
            {
                var occupation = _state.GetHeroCurrentActivityExecutionId(heroId);
                claimsByHero.TryGetValue(heroId, out var claims);
                if (string.IsNullOrWhiteSpace(occupation))
                {
                    if (claims != null && claims.Count > 0)
                        AddIntegrityIssue(issues, claims[0], $"Hero '{heroId}' is not occupied by its active execution.");
                    continue;
                }

                if (claims == null || claims.Count == 0 || !claims.Contains(occupation))
                    AddIntegrityIssue(issues, occupation, $"Hero '{heroId}' references missing occupation '{occupation}'.");
                else if (claims.Count != 1)
                    AddIntegrityIssue(issues, claims[0], $"Hero '{heroId}' has {claims.Count} conflicting occupation claims.");
            }

            return issues.Count == 0;

            void ValidatePendingResult(
                bool required,
                string resultId,
                string sourceType,
                string executionId,
                List<OfflineCoordinatorIssue> targetIssues)
            {
                if (!required || string.IsNullOrWhiteSpace(resultId))
                    return;
                var result = _state.PendingResults.Get(resultId);
                if (result == null ||
                    !string.Equals(result.sourceType, sourceType, StringComparison.Ordinal) ||
                    !string.Equals(result.sourceExecutionId, executionId, StringComparison.Ordinal))
                {
                    AddIntegrityIssue(
                        targetIssues,
                        executionId,
                        $"Execution references incompatible pending result '{resultId}'.");
                }
            }

        }

        private static void AddClaim(
            Dictionary<string, List<string>> claimsByHero,
            string heroId,
            string executionId,
            List<OfflineCoordinatorIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(heroId))
            {
                AddIntegrityIssue(issues, executionId, "Active execution has no hero.");
                return;
            }
            if (!claimsByHero.TryGetValue(heroId, out var claims))
            {
                claims = new List<string>();
                claimsByHero.Add(heroId, claims);
            }
            claims.Add(executionId);
        }

        private static void AddIntegrityIssue(
            List<OfflineCoordinatorIssue> issues,
            string executionId,
            string message)
        {
            issues.Add(new OfflineCoordinatorIssue(
                "DataIntegrityFailure",
                OfflineCoordinatorStage.Validation,
                executionId,
                message));
        }

        private static bool IsDangerCandidate(ActivityExecutionSaveData execution)
        {
            if (execution?.linkedCombat != null)
                return true;
            return execution != null &&
                   execution.status == ActivityRuntimeStatus.Running &&
                   string.Equals(execution.runtimeKind, RuntimeKindWork, StringComparison.Ordinal) &&
                   string.Equals(execution.cyclePhase, CyclePhaseResultStaged, StringComparison.Ordinal) &&
                   execution.dangerRollCompleted &&
                   execution.dangerRoll <= execution.dangerRiskPercent;
        }

        private static void AddActivityIssues(
            IReadOnlyList<ActivityRequirementIssue> source,
            OfflineCoordinatorStage stage,
            string executionId,
            List<OfflineCoordinatorIssue> target)
        {
            foreach (var issue in source ?? Array.Empty<ActivityRequirementIssue>())
            {
                target.Add(new OfflineCoordinatorIssue(
                    issue?.issueType,
                    stage,
                    executionId,
                    issue?.message));
            }
        }

        private static void AddCraftIssues(
            IReadOnlyList<CraftAdvanceIssue> source,
            string executionId,
            List<OfflineCoordinatorIssue> target)
        {
            foreach (var issue in source ?? Array.Empty<CraftAdvanceIssue>())
            {
                target.Add(new OfflineCoordinatorIssue(
                    issue?.Code,
                    OfflineCoordinatorStage.Craft,
                    executionId,
                    issue?.Message));
            }
        }

        private static bool HasDataIntegrityIssue(IReadOnlyList<CraftAdvanceIssue> issues)
        {
            foreach (var issue in issues ?? Array.Empty<CraftAdvanceIssue>())
                if (string.Equals(issue?.Code, "DataIntegrityFailure", StringComparison.Ordinal)) return true;
            return false;
        }

        private static void SortActivityExecutions(ActivityExecutionSaveData[] executions)
        {
            Array.Sort(executions, (left, right) => StringComparer.Ordinal.Compare(
                left?.executionId ?? string.Empty,
                right?.executionId ?? string.Empty));
        }

        private static void SortCraftExecutions(CraftExecutionSaveData[] executions)
        {
            Array.Sort(executions, (left, right) => StringComparer.Ordinal.Compare(
                left?.executionId ?? string.Empty,
                right?.executionId ?? string.Empty));
        }

        private static void SortCombatAggregates(CombatRuntimeAggregate[] aggregates)
        {
            Array.Sort(aggregates, (left, right) => StringComparer.Ordinal.Compare(
                left?.execution?.executionId ?? string.Empty,
                right?.execution?.executionId ?? string.Empty));
        }

        private OfflineCoordinatorReport PublishDiagnostic(OfflineCoordinatorReport report)
        {
            try
            {
                _diagnosticSink?.Invoke(report);
            }
            catch (Exception)
            {
                // Diagnostics cannot alter the transaction result.
            }
            return report;
        }

        private static OfflineCoordinatorReport NoOp(
            TimeAdvancePlan plan,
            OfflineCoordinatorCode code)
        {
            var context = new PassContext(plan)
            {
                Success = true,
                Code = code
            };
            return context.ToReport();
        }

        private static OfflineCoordinatorReport FailureWithoutPlan(
            OfflineCoordinatorCode code,
            OfflineCoordinatorStage stage,
            string executionId,
            string issueCode,
            string message)
        {
            var issues = new[] { new OfflineCoordinatorIssue(issueCode, stage, executionId, message) };
            return new OfflineCoordinatorReport(
                false,
                code,
                false,
                false,
                0L,
                0L,
                0L,
                null,
                null,
                null,
                null,
                null,
                Array.Empty<string>(),
                0,
                0,
                0,
                0,
                stage,
                executionId,
                issues);
        }

        private static string FirstIssueExecutionId(List<OfflineCoordinatorIssue> issues) =>
            issues.Count == 0 ? string.Empty : issues[0].ExecutionId;

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OfflineCoordinator));
        }

        private sealed class MutableSummary
        {
            public int Attempted;
            public int Succeeded;
            public int Completed;
            public int Partial;
            public int NoOp;

            public OfflineProcessorSummary ToImmutable() =>
                new OfflineProcessorSummary(Attempted, Succeeded, Completed, Partial, NoOp);
        }

        private sealed class PassContext
        {
            private readonly List<string> _processedExecutionIds = new List<string>();
            private readonly HashSet<string> _processedExecutionIdSet = new HashSet<string>(StringComparer.Ordinal);
            private HeroFatigueRecoveryResult[] _recoveries = Array.Empty<HeroFatigueRecoveryResult>();
            private int _restoredFatigue;

            public PassContext(TimeAdvancePlan timePlan)
            {
                TimePlan = timePlan;
            }

            public TimeAdvancePlan TimePlan { get; }
            public bool Success;
            public OfflineCoordinatorCode Code;
            public bool StateCommitted;
            public bool Saved;
            public OfflineCoordinatorStage FailedStage;
            public string FailedExecutionId = string.Empty;
            public int EligibleHeroCount;
            public readonly MutableSummary Work = new MutableSummary();
            public readonly MutableSummary Danger = new MutableSummary();
            public readonly MutableSummary Construction = new MutableSummary();
            public readonly MutableSummary Craft = new MutableSummary();
            public readonly List<OfflineCoordinatorIssue> Issues = new List<OfflineCoordinatorIssue>();
            public readonly List<DeferredEvent> DeferredEvents = new List<DeferredEvent>();
            public int AttemptedEventCount;
            public int PublishedEventCount;
            public int FailedEventCount;

            public void AddProcessedExecution(string executionId)
            {
                if (!string.IsNullOrWhiteSpace(executionId) && _processedExecutionIdSet.Add(executionId))
                    _processedExecutionIds.Add(executionId);
            }

            public void SetFatigue(TimeAdvanceResult result)
            {
                _recoveries = new HeroFatigueRecoveryResult[result.Recoveries.Count];
                for (var index = 0; index < result.Recoveries.Count; index++)
                {
                    _recoveries[index] = result.Recoveries[index];
                    _restoredFatigue += result.Recoveries[index].RestoredFatigue;
                }
            }

            public OfflineCoordinatorReport ToReport()
            {
                return new OfflineCoordinatorReport(
                    Success,
                    Code,
                    StateCommitted,
                    Saved,
                    TimePlan.PreviousUtcSeconds,
                    TimePlan.NowUtcSeconds,
                    TimePlan.DeltaSeconds,
                    Work.ToImmutable(),
                    Danger.ToImmutable(),
                    Construction.ToImmutable(),
                    Craft.ToImmutable(),
                    new OfflineFatigueSummary(EligibleHeroCount, _restoredFatigue, _recoveries),
                    _processedExecutionIds.ToArray(),
                    DeferredEvents.Count,
                    AttemptedEventCount,
                    PublishedEventCount,
                    FailedEventCount,
                    FailedStage,
                    FailedExecutionId,
                    Issues.ToArray());
            }
        }

        private sealed class DeferredEvent
        {
            public DeferredEvent(
                OfflineCoordinatorStage stage,
                string executionId,
                int localOrder,
                Action publish)
            {
                Stage = stage;
                ExecutionId = executionId ?? string.Empty;
                LocalOrder = localOrder;
                Publish = publish ?? throw new ArgumentNullException(nameof(publish));
            }

            public OfflineCoordinatorStage Stage { get; }
            public string ExecutionId { get; }
            public int LocalOrder { get; }
            public Action Publish { get; }

            public static int Compare(DeferredEvent left, DeferredEvent right)
            {
                var stage = left.Stage.CompareTo(right.Stage);
                if (stage != 0)
                    return stage;
                var execution = StringComparer.Ordinal.Compare(left.ExecutionId, right.ExecutionId);
                return execution != 0 ? execution : left.LocalOrder.CompareTo(right.LocalOrder);
            }
        }

        private sealed class ProcessorFailure
        {
            public ProcessorFailure(
                OfflineCoordinatorCode code,
                OfflineCoordinatorStage stage,
                string executionId)
            {
                Code = code;
                Stage = stage;
                ExecutionId = executionId ?? string.Empty;
            }

            public OfflineCoordinatorCode Code { get; }
            public OfflineCoordinatorStage Stage { get; }
            public string ExecutionId { get; }

            public static ProcessorFailure Runtime(OfflineCoordinatorStage stage, string executionId) =>
                new ProcessorFailure(OfflineCoordinatorCode.RuntimeError, stage, executionId);

            public static ProcessorFailure FromActivityResult(
                WorkAdvanceStopReason stopReason,
                OfflineCoordinatorStage stage,
                string executionId)
            {
                var code = stopReason == WorkAdvanceStopReason.ValidationFailed
                    ? OfflineCoordinatorCode.ValidationFailed
                    : OfflineCoordinatorCode.ProcessorFailed;
                return new ProcessorFailure(code, stage, executionId);
            }

            public static ProcessorFailure FromActivityResult(
                ConstructionAdvanceStopReason stopReason,
                OfflineCoordinatorStage stage,
                string executionId)
            {
                var code = stopReason == ConstructionAdvanceStopReason.ValidationFailed
                    ? OfflineCoordinatorCode.ValidationFailed
                    : OfflineCoordinatorCode.ProcessorFailed;
                return new ProcessorFailure(code, stage, executionId);
            }
        }
    }
}
