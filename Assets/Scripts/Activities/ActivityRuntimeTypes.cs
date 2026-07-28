using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Player;

namespace GuildIdle.Activities
{
    public sealed class ActivityRuntimeInfo
    {
        public string activityId;
        public string activityType;
        public int durationSeconds;
        public bool isRepeatable;
        public bool requiresHero = true;
        public ActivityConfigDto activity;
    }

    public sealed class ActivityStartRequest
    {
        public string activityId;
        public string runtimeKind;
        public string heroId;
        public int? plannedCycleCount;
    }

    public sealed class ActivityPlannedRewardRange
    {
        public string rewardType;
        public string targetId;
        public long minAmount;
        public long maxAmount;
    }

    public sealed class WorkRuntimeDescriptor
    {
        public string activityId;
        public string heroId;
        public int minCycleCount;
        public int maxCycleCount;
        public int cycleSeconds;
        public int fatiguePerCycle;
        public int plannedCycleCount;
        public int plannedFatigue;
        public long plannedDurationSeconds;
        public ActivityPlannedRewardRange[] expectedRewards = Array.Empty<ActivityPlannedRewardRange>();
    }

    public sealed class WorkDescriptorResult
    {
        public bool success;
        public WorkRuntimeDescriptor descriptor;
        public ActivityRequirementIssue[] issues = Array.Empty<ActivityRequirementIssue>();
    }

    public enum WorkAdvanceStopReason
    {
        None = 0,
        IntervalExhausted = 1,
        PlanCompleted = 2,
        InsufficientFatigue = 3,
        DangerBoundaryReached = 4,
        ProcessingLimitReached = 5,
        InvalidRequest = 6,
        ExecutionNotFound = 7,
        ExecutionNotRunning = 8,
        NotWorkExecution = 9,
        ValidationFailed = 10,
        RuntimeError = 11
    }

    public sealed class WorkAdvanceRequest
    {
        public WorkAdvanceRequest(string executionId, long availableSeconds)
            : this(executionId, availableSeconds, ActivityRuntimeService.DefaultWorkAdvanceOperationLimit)
        {
        }

        public WorkAdvanceRequest(string executionId, long availableSeconds, int operationLimit)
        {
            ExecutionId = executionId;
            AvailableSeconds = availableSeconds;
            OperationLimit = operationLimit;
        }

        public string ExecutionId { get; }
        public long AvailableSeconds { get; }
        public int OperationLimit { get; }
    }

    public sealed class WorkAdvanceResult
    {
        internal WorkAdvanceResult(
            bool success,
            WorkAdvanceStopReason stopReason,
            string executionId,
            int processedCycles,
            long consumedSeconds,
            long remainingSeconds,
            ActivityRuntimeStatus executionStatus,
            bool hasPartialCycle,
            bool planCompleted,
            IReadOnlyList<ActivityRequirementIssue> issues,
            IReadOnlyList<PendingResultDeferredResolvedEvent> deferredResolvedEvents)
        {
            Success = success;
            StopReason = stopReason;
            ExecutionId = executionId;
            ProcessedCycles = processedCycles;
            ConsumedSeconds = consumedSeconds;
            RemainingSeconds = remainingSeconds;
            ExecutionStatus = executionStatus;
            HasPartialCycle = hasPartialCycle;
            PlanCompleted = planCompleted;
            Issues = issues ?? Array.AsReadOnly(Array.Empty<ActivityRequirementIssue>());
            DeferredResolvedEvents = deferredResolvedEvents ??
                                     Array.AsReadOnly(Array.Empty<PendingResultDeferredResolvedEvent>());
        }

        public bool Success { get; }
        public string Code => StopReason.ToString();
        public WorkAdvanceStopReason StopReason { get; }
        public string ExecutionId { get; }
        public int ProcessedCycles { get; }
        public long ConsumedSeconds { get; }
        public long RemainingSeconds { get; }
        public ActivityRuntimeStatus ExecutionStatus { get; }
        public bool HasPartialCycle { get; }
        public bool PlanCompleted { get; }
        public bool FatigueStopped => StopReason == WorkAdvanceStopReason.InsufficientFatigue;
        public bool DangerBoundaryReached => StopReason == WorkAdvanceStopReason.DangerBoundaryReached;
        public bool ProcessingLimitReached => StopReason == WorkAdvanceStopReason.ProcessingLimitReached;
        public IReadOnlyList<ActivityRequirementIssue> Issues { get; }
        public IReadOnlyList<PendingResultDeferredResolvedEvent> DeferredResolvedEvents { get; }
    }

    public sealed class ActivityRuntimeEvent
    {
        public string eventType;
        public string targetId;
        public int value;
        public bool progressionAlreadyProcessed;
    }

    public static class ActivityRuntimeEventType
    {
        public const string ActivityCompleted = "ActivityCompleted";
        public const string BuildingLevelChanged = "BuildingLevelChanged";
    }

    public sealed class LinkedCombatGatewayResult
    {
        public bool success;
        public bool replayed;
        public string code;
        public string message;
        public string completedActivityId;
        public ActivityRuntimeEvent[] events = Array.Empty<ActivityRuntimeEvent>();
        public LinkedCombatStartRequestSaveData request;
        public ActivityRuntimeSnapshot snapshot;
    }

    public interface ILinkedCombatStartGateway
    {
        LinkedCombatStartRequestSaveData[] GetPendingLinkedCombatStarts();
        LinkedCombatGatewayResult BindLinkedCombatExecution(string requestId, string combatExecutionId);
        LinkedCombatGatewayResult ResolveLinkedCombatExecution(string requestId, string combatExecutionId);
    }

    public interface ILinkedCombatRuntimeCoordinator : IDisposable
    {
        void Reconcile();
    }

    public sealed class FormulaEvaluationContext
    {
        private readonly Dictionary<string, float> _stats = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public float skillLevel;
        public bool hasContextBase;
        public float contextBase;

        public void SetStat(string statId, float value)
        {
            if (!string.IsNullOrWhiteSpace(statId))
                _stats[statId] = value;
        }

        public bool TryGetStat(string statId, out float value)
        {
            value = 0f;
            return !string.IsNullOrWhiteSpace(statId) && _stats.TryGetValue(statId, out value);
        }
    }

    public sealed class FormulaEvaluationResult
    {
        public bool success;
        public string code;
        public string message;
        public float value;
    }

    public sealed class FormulaRuntime
    {
        private delegate FormulaEvaluationResult Evaluator(FormulaConfigDto formula, FormulaEvaluationContext context);

        private readonly Dictionary<string, Evaluator> _evaluators = new Dictionary<string, Evaluator>(StringComparer.OrdinalIgnoreCase)
        {
            ["linear_stats_with_skill_level"] = EvaluateLinearStatsWithSkillLevel,
            ["context_base_minus_stats_and_skill_level"] = EvaluateContextBaseMinusStatsAndSkillLevel
        };

        public FormulaEvaluationResult Evaluate(FormulaConfigDto formula, FormulaEvaluationContext context)
        {
            if (formula == null)
                return Failure("FormulaMissing", "Formula descriptor is required.");
            if (string.IsNullOrWhiteSpace(formula.formulaId))
                return Failure("FormulaIdRequired", "Formula descriptor requires formulaId.");
            if (!formula.enabled)
                return Failure("FormulaDisabled", $"Formula '{formula.formulaId}' is disabled.");
            if (context == null)
                return Failure("FormulaContextMissing", "Formula context is required.");
            if (string.IsNullOrWhiteSpace(formula.formulaType) || !_evaluators.TryGetValue(formula.formulaType, out var evaluator))
                return Failure("FormulaTypeUnsupported", $"Formula type '{formula.formulaType}' is unsupported.");
            if (!IsSupportedRounding(formula.rounding))
                return Failure("FormulaRoundingUnsupported", $"Formula '{formula.formulaId}' has unsupported rounding '{formula.rounding}'.");

            var result = evaluator(formula, context);
            if (!result.success)
                return result;

            var value = Math.Max(formula.minValue, result.value);
            if (formula.maxValue > 0f)
                value = Math.Min(formula.maxValue, value);
            if (formula.capValue > 0f)
                value = Math.Min(formula.capValue, value);
            result.value = ApplyRounding(value, formula.rounding);
            return result;
        }

        private static FormulaEvaluationResult EvaluateLinearStatsWithSkillLevel(FormulaConfigDto formula, FormulaEvaluationContext context)
        {
            if (!TryRequiredStats(formula, context, out var primary, out var secondary, out var failure))
                return failure;
            return Success(formula.baseValue + primary * formula.primaryStatMultiplier + secondary * formula.secondaryStatMultiplier + context.skillLevel * formula.levelMultiplier);
        }

        private static FormulaEvaluationResult EvaluateContextBaseMinusStatsAndSkillLevel(FormulaConfigDto formula, FormulaEvaluationContext context)
        {
            if (!context.hasContextBase)
                return Failure("ContextBaseRequired", $"Formula '{formula.formulaId}' requires context_base.");
            if (!TryRequiredStats(formula, context, out var primary, out var secondary, out var failure))
                return failure;
            return Success(context.contextBase - primary * formula.primaryStatMultiplier - secondary * formula.secondaryStatMultiplier - context.skillLevel * formula.levelMultiplier);
        }

        private static bool TryRequiredStats(FormulaConfigDto formula, FormulaEvaluationContext context, out float primary, out float secondary, out FormulaEvaluationResult failure)
        {
            primary = 0f;
            secondary = 0f;
            failure = null;
            if (string.IsNullOrWhiteSpace(formula.primaryStat) || !context.TryGetStat(formula.primaryStat, out primary))
            {
                failure = Failure("PrimaryStatRequired", $"Formula '{formula.formulaId}' requires primary stat '{formula.primaryStat}'.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(formula.secondaryStat) || !context.TryGetStat(formula.secondaryStat, out secondary))
            {
                failure = Failure("SecondaryStatRequired", $"Formula '{formula.formulaId}' requires secondary stat '{formula.secondaryStat}'.");
                return false;
            }
            return true;
        }

        private static float ApplyRounding(float value, string rounding)
        {
            if (string.Equals(rounding, "floor", StringComparison.OrdinalIgnoreCase))
                return (float)Math.Floor(value);
            if (string.Equals(rounding, "ceil", StringComparison.OrdinalIgnoreCase) || string.Equals(rounding, "ceiling", StringComparison.OrdinalIgnoreCase))
                return (float)Math.Ceiling(value);
            if (string.Equals(rounding, "round_2", StringComparison.OrdinalIgnoreCase))
                return (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
            return (float)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private static bool IsSupportedRounding(string rounding)
        {
            return string.Equals(rounding, "floor", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(rounding, "ceil", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(rounding, "ceiling", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(rounding, "round", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(rounding, "round_2", StringComparison.OrdinalIgnoreCase);
        }

        private static FormulaEvaluationResult Success(float value) => new FormulaEvaluationResult { success = true, code = "Applied", value = value };
        private static FormulaEvaluationResult Failure(string code, string message) => new FormulaEvaluationResult { success = false, code = code, message = message };
    }

    public sealed class ActivityExecutionSnapshot
    {
        public string executionId;
        public string activityId;
        public string runtimeKind;
        public string heroId;
        public ActivityRuntimeStatus status;
        public float elapsedSeconds;
        public float durationSeconds;
        public float progress;
        public float remainingSeconds;
        public int completedCycles;
        public int plannedCycles;
        public bool currentCycleFatiguePaid;
        public string cyclePhase;
        public string completionPhase;
        public string endReason;
        public float accumulatedBuildPoints;
        public LinkedCombatStartRequestSaveData linkedCombat;
        public string pendingResultId;
        public long startedAtUnixSeconds;
    }

    public sealed class HeroActivityState
    {
        public string heroId;
        public bool isBusy;
        public string currentActivityExecutionId;
    }

    public sealed class ActivityRuntimeSnapshot
    {
        public ActivityExecutionSnapshot[] executions = Array.Empty<ActivityExecutionSnapshot>();
    }

    public sealed class ActivityRuntimeProgressionResult
    {
        public bool success;
        public string code;
        public string message;
    }

    public interface IActivityRuntimeProgressionProcessor
    {
        ActivityRuntimeProgressionResult ProcessBuildingLevelChanged(string buildingId, int level);
        ActivityRuntimeProgressionResult ProcessActivityCompleted(string activityId);
        ActivityRuntimeProgressionResult ProcessActivityFailed(string activityId);
    }

    public sealed class ActivityStartResult
    {
        public bool success;
        public string executionId;
        public ActivityExecutionContext context;
        public ActivityCheckResult startCheck;
        public ActivityCostResult costCheck;
        public ActivityCostResult appliedCost;
        public ActivityRequirementIssue[] issues = Array.Empty<ActivityRequirementIssue>();
        public ActivityRuntimeSnapshot snapshot;
    }

    public sealed class ActivityTickResult
    {
        public bool success;
        public float deltaTime;
        public int processedExecutions;
        public int processedCycles;
        public bool saved;
        public bool cycleLimitReached;
        public ActivityRequirementIssue[] issues = Array.Empty<ActivityRequirementIssue>();
        public ActivityRewardResult[] rewardResults = Array.Empty<ActivityRewardResult>();
        public ActivityRuntimeEvent[] events = Array.Empty<ActivityRuntimeEvent>();
        public ActivityRuntimeSnapshot snapshot;
    }

    public sealed class ActivityCompleteResult
    {
        public bool success;
        public string executionId;
        public bool saved;
        public ActivityRequirementIssue[] issues = Array.Empty<ActivityRequirementIssue>();
        public ActivityRewardResult[] rewardResults = Array.Empty<ActivityRewardResult>();
        public ActivityRuntimeEvent[] events = Array.Empty<ActivityRuntimeEvent>();
        public ActivityRuntimeSnapshot snapshot;
    }

    public sealed class ActivityCancelResult
    {
        public bool success;
        public string executionId;
        public bool saved;
        public ActivityRequirementIssue[] issues = Array.Empty<ActivityRequirementIssue>();
        public ActivityRuntimeEvent[] events = Array.Empty<ActivityRuntimeEvent>();
        public ActivityRuntimeSnapshot snapshot;
    }

}
