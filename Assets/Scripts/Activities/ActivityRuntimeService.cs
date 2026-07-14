using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Core;
using UnityEngine;
using CoreActivityRuntimeStatus = GuildIdle.Core.ActivityRuntimeStatus;

namespace GuildIdle.Activities
{
    public sealed class ActivityRuntimeService
    {
        public const int MaxCyclesPerTick = 100;

        private readonly IActivityRuntimeStore _store;
        private readonly IActivityPlayerState _activityState;

        public ActivityRuntimeService(IActivityRuntimeStore store, IActivityPlayerState activityState)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _activityState = activityState ?? throw new ArgumentNullException(nameof(activityState));
        }

        public ActivityStartResult Start(string activityId, string heroId)
        {
            return StartInternal(activityId, heroId);
        }

        public ActivityTickResult Tick(float deltaTime)
        {
            var issues = new List<ActivityRequirementIssue>();
            var rewards = new List<ActivityRewardResult>();
            var result = new ActivityTickResult { deltaTime = deltaTime };
            var changed = false;

            if (deltaTime < 0f)
            {
                AddIssue(issues, string.Empty, "TickDeltaTime", string.Empty, 0, 0, true, false, "ActivityRuntimeService.Tick requires non-negative deltaTime.");
                return FinishTick(result, issues, rewards, changed);
            }

            var executions = GetExecutions();
            foreach (var execution in executions)
            {
                if (execution == null || execution.status != CoreActivityRuntimeStatus.Running)
                    continue;

                result.processedExecutions++;
                if (!TryGetRuntimeInfo(execution.activityId, issues, out var info))
                    continue;

                execution.elapsedSeconds += deltaTime;
                changed = true;

                if (execution.elapsedSeconds < info.durationSeconds)
                {
                    changed |= UpdateExecution(execution);
                    continue;
                }

                if (info.isRepeatable)
                    ProcessRepeatableTick(execution, info, issues, rewards, result, ref changed);
                else
                    ProcessOneShotCompletion(execution, info, issues, rewards, ref changed);
            }

            return FinishTick(result, issues, rewards, changed);
        }

        public ActivityCompleteResult Complete(string executionId)
        {
            var issues = new List<ActivityRequirementIssue>();
            var rewards = new List<ActivityRewardResult>();
            var result = new ActivityCompleteResult { executionId = executionId };
            var changed = false;

            var execution = GetExecution(executionId);
            if (execution == null)
            {
                AddIssue(issues, string.Empty, "ActivityExecution", executionId, 1, 0, false, false, $"Activity execution '{executionId}' is not running.");
                return FinishComplete(result, issues, rewards, changed);
            }

            if (!TryGetRuntimeInfo(execution.activityId, issues, out var info))
                return FinishComplete(result, issues, rewards, changed);

            execution.elapsedSeconds = info.durationSeconds;
            changed = true;

            if (info.isRepeatable)
                ProcessRepeatableTick(execution, info, issues, rewards, new ActivityTickResult(), ref changed);
            else
                ProcessOneShotCompletion(execution, info, issues, rewards, ref changed);

            return FinishComplete(result, issues, rewards, changed);
        }

        public ActivityCompleteResult CompleteCurrent()
        {
            var executions = GetExecutions();
            if (executions.Length == 0)
                return Complete(null);

            return Complete(executions[0].executionId);
        }

        public ActivityCancelResult Cancel(string executionId)
        {
            var issues = new List<ActivityRequirementIssue>();
            var result = new ActivityCancelResult { executionId = executionId };
            var changed = false;

            var execution = GetExecution(executionId);
            if (execution == null)
            {
                AddIssue(issues, string.Empty, "ActivityExecution", executionId, 1, 0, false, false, $"Activity execution '{executionId}' is not running.");
                return FinishCancel(result, issues, changed);
            }

            changed = RemoveExecution(execution.executionId);
            return FinishCancel(result, issues, changed);
        }

        public ActivityRuntimeSnapshot GetSnapshot()
        {
            var executions = GetExecutions();
            var snapshots = new ActivityExecutionSnapshot[executions.Length];
            for (var i = 0; i < executions.Length; i++)
                snapshots[i] = ToSnapshot(executions[i]);

            return new ActivityRuntimeSnapshot { executions = snapshots };
        }

        public HeroActivityState GetHeroActivityState(string heroId)
        {
            return new HeroActivityState
            {
                heroId = heroId,
                isBusy = _activityState.IsHeroBusy(heroId),
                currentActivityExecutionId = _activityState.GetHeroCurrentActivityExecutionId(heroId)
            };
        }

        public int GetActiveHeroLimit()
        {
            return ActiveHeroLimitResolver.GetCurrentLimit(_activityState);
        }

        public static bool TryGetRuntimeInfo(string activityId, out ActivityRuntimeInfo info)
        {
            return TryGetRuntimeInfo(activityId, new List<ActivityRequirementIssue>(), out info);
        }

        private ActivityStartResult StartInternal(string activityId, string heroId)
        {
            var issues = new List<ActivityRequirementIssue>();
            var executionId = NewExecutionId();
            var startedAt = UnixNow();
            var context = new ActivityExecutionContext
            {
                activityId = activityId,
                heroId = heroId,
                executionId = executionId,
                startedAtUnixSeconds = startedAt
            };

            var result = new ActivityStartResult
            {
                executionId = executionId,
                context = context
            };

            TryGetRuntimeInfo(activityId, issues, out var info);

            if (string.IsNullOrWhiteSpace(heroId))
                AddIssue(issues, activityId, "HeroExecutor", string.Empty, 1, 0, true, false, "Activity start requires heroId.");

            result.startCheck = ActivityResolver.CanStart(context, _activityState);
            issues.AddRange(result.startCheck.issues);

            if (info != null && !info.isRepeatable && _activityState.IsActivityCompleted(activityId))
                AddIssue(issues, activityId, "ActivityCompleted", activityId, 1, 1, false, false, $"Activity '{activityId}' is non-repeatable and already completed.");

            if (!HasBlockingIssues(issues))
                result.costCheck = ActivityResolver.CanPayCost(context, _activityState);
            if (result.costCheck != null)
                issues.AddRange(result.costCheck.issues);

            if (!HasBlockingIssues(issues))
                CanStoreExecution(context, issues);

            if (!HasBlockingIssues(issues))
                ValidateActiveHeroLimit(context, issues);

            if (HasBlockingIssues(issues))
                return FinishStart(result, issues, false);

            result.appliedCost = ActivityResolver.ApplyCost(context, _activityState);
            issues.AddRange(result.appliedCost.issues);
            if (!result.appliedCost.success || HasBlockingIssues(issues))
                return FinishStart(result, issues, false);

            var execution = new ActivityExecutionSaveData
            {
                executionId = executionId,
                activityId = activityId,
                heroId = heroId,
                status = CoreActivityRuntimeStatus.Running,
                elapsedSeconds = 0f,
                completedCycles = 0,
                startedAtUnixSeconds = startedAt
            };

            if (!AddExecution(execution))
            {
                AddIssue(issues, activityId, "ActivityExecution", executionId, 1, 0, true, false, $"Failed to store activity execution '{executionId}'.");
                return FinishStart(result, issues, false);
            }

            Save();
            return FinishStart(result, issues, true);
        }

        private void ProcessRepeatableTick(
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
                var preview = PreviewRewards(execution, "OnCycle");
                if (!preview.success)
                {
                    issues.AddRange(preview.issues);
                    break;
                }

                var reward = ApplyRewards(execution, "OnCycle", markCompletion: false);
                rewards.Add(reward);
                if (!reward.success)
                {
                    issues.AddRange(reward.issues);
                    break;
                }

                execution.completedCycles++;
                execution.elapsedSeconds -= info.durationSeconds;
                cycles++;
                tickResult.processedCycles++;
                changed = true;
            }

            if (execution.elapsedSeconds >= info.durationSeconds && cycles >= MaxCyclesPerTick)
            {
                tickResult.cycleLimitReached = true;
                AddIssue(issues, execution.activityId, "TickCycleLimitReached", execution.executionId, MaxCyclesPerTick, cycles, false, false, $"Processed maximum {MaxCyclesPerTick} cycles for execution '{execution.executionId}'.");
            }

            changed |= UpdateExecution(execution);
        }

        private void ProcessOneShotCompletion(
            ActivityExecutionSaveData execution,
            ActivityRuntimeInfo info,
            List<ActivityRequirementIssue> issues,
            List<ActivityRewardResult> rewards,
            ref bool changed)
        {
            var wasCompleted = _activityState.IsActivityCompleted(execution.activityId);
            var completePreview = PreviewRewards(execution, "OnComplete");
            if (!completePreview.success)
            {
                issues.AddRange(completePreview.issues);
                changed |= UpdateExecution(execution);
                return;
            }

            if (!wasCompleted)
            {
                var firstCompletePreview = PreviewRewards(execution, "OnFirstComplete");
                if (!firstCompletePreview.success)
                {
                    issues.AddRange(firstCompletePreview.issues);
                    changed |= UpdateExecution(execution);
                    return;
                }
            }

            var complete = ApplyRewards(execution, "OnComplete", markCompletion: false);
            rewards.Add(complete);
            if (!complete.success)
            {
                issues.AddRange(complete.issues);
                changed |= UpdateExecution(execution);
                return;
            }

            if (!wasCompleted)
            {
                var firstComplete = ApplyRewards(execution, "OnFirstComplete", markCompletion: false);
                rewards.Add(firstComplete);
                if (!firstComplete.success)
                {
                    issues.AddRange(firstComplete.issues);
                    changed |= UpdateExecution(execution);
                    return;
                }

                if (!_activityState.CompleteActivity(execution.activityId))
                {
                    AddIssue(issues, execution.activityId, "ActivityCompleted", execution.activityId, 1, 0, true, false, $"Failed to mark activity '{execution.activityId}' completed.");
                    changed |= UpdateExecution(execution);
                    return;
                }
            }

            changed |= RemoveExecution(execution.executionId);
        }

        private ActivityRewardResult ApplyRewards(ActivityExecutionSaveData execution, string grantMoment, bool markCompletion)
        {
            return ActivityResolver.ApplyRewards(ToContext(execution), grantMoment, _activityState, ActivityResolverUtilities.DefaultRandom(), markCompletion);
        }

        private ActivityRewardResult PreviewRewards(ActivityExecutionSaveData execution, string grantMoment)
        {
            return ActivityRewardResolver.PreviewRewards(ToContext(execution), grantMoment, _activityState);
        }

        private ActivityExecutionContext ToContext(ActivityExecutionSaveData execution)
        {
            return new ActivityExecutionContext
            {
                activityId = execution.activityId,
                heroId = execution.heroId,
                executionId = execution.executionId,
                startedAtUnixSeconds = execution.startedAtUnixSeconds
            };
        }

        private ActivityExecutionSnapshot ToSnapshot(ActivityExecutionSaveData execution)
        {
            var duration = TryGetRuntimeInfo(execution.activityId, out var info) ? info.durationSeconds : 0;
            var progress = duration > 0 ? Mathf.Clamp01(execution.elapsedSeconds / duration) : 0f;
            return new ActivityExecutionSnapshot
            {
                executionId = execution.executionId,
                activityId = execution.activityId,
                heroId = execution.heroId,
                status = execution.status,
                elapsedSeconds = execution.elapsedSeconds,
                durationSeconds = duration,
                progress = progress,
                remainingSeconds = Math.Max(0f, duration - execution.elapsedSeconds),
                completedCycles = execution.completedCycles,
                startedAtUnixSeconds = execution.startedAtUnixSeconds
            };
        }

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

            info = new ActivityRuntimeInfo
            {
                activityId = activity.id,
                activityType = activity.type,
                durationSeconds = duration,
                isRepeatable = activity.isRepeatable,
                requiresHero = true,
                activity = activity
            };
            return true;
        }

        private void CanStoreExecution(ActivityExecutionContext context, List<ActivityRequirementIssue> issues)
        {
            if (GetExecution(context.executionId) != null)
                AddIssue(issues, context.activityId, "ActivityExecution", context.executionId, 1, 1, true, false, $"Activity execution '{context.executionId}' already exists.");

            var currentExecutionId = _activityState.GetHeroCurrentActivityExecutionId(context.heroId);
            if (!string.IsNullOrWhiteSpace(currentExecutionId) &&
                !string.Equals(currentExecutionId, context.executionId, StringComparison.Ordinal))
            {
                AddIssue(issues, context.activityId, "HeroBusy", context.heroId, 1, 1, false, false, $"Hero '{context.heroId}' is busy with execution '{currentExecutionId}'.");
            }
        }

        private void ValidateActiveHeroLimit(ActivityExecutionContext context, List<ActivityRequirementIssue> issues)
        {
            var currentLimit = ActiveHeroLimitResolver.GetCurrentLimit(_activityState);
            var activeHeroCount = CountActiveHeroes();
            if (activeHeroCount < currentLimit)
                return;

            AddIssue(
                issues,
                context.activityId,
                "ActiveHeroLimitReached",
                context.heroId,
                currentLimit,
                activeHeroCount,
                false,
                false,
                $"Active hero limit reached: {activeHeroCount}/{currentLimit} heroes are already running activities.");
        }

        private int CountActiveHeroes()
        {
            var heroIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var execution in GetExecutions())
            {
                if (execution == null ||
                    execution.status != CoreActivityRuntimeStatus.Running ||
                    string.IsNullOrWhiteSpace(execution.heroId))
                {
                    continue;
                }

                heroIds.Add(execution.heroId);
            }

            return heroIds.Count;
        }

        private ActivityExecutionSaveData[] GetExecutions()
        {
            return _store.GetActivityExecutions();
        }

        private ActivityExecutionSaveData GetExecution(string executionId)
        {
            return _store.GetActivityExecution(executionId);
        }

        private bool AddExecution(ActivityExecutionSaveData execution)
        {
            return _store.AddActivityExecution(execution);
        }

        private bool UpdateExecution(ActivityExecutionSaveData execution)
        {
            return _store.UpdateActivityExecution(execution);
        }

        private bool RemoveExecution(string executionId)
        {
            return _store.RemoveActivityExecution(executionId);
        }

        private bool Save()
        {
            return _store.Save();
        }

        private ActivityStartResult FinishStart(ActivityStartResult result, List<ActivityRequirementIssue> issues, bool success)
        {
            result.success = success && !HasBlockingIssues(issues);
            result.issues = issues.ToArray();
            result.snapshot = GetSnapshot();
            return result;
        }

        private ActivityTickResult FinishTick(ActivityTickResult result, List<ActivityRequirementIssue> issues, List<ActivityRewardResult> rewards, bool changed)
        {
            result.success = !HasBlockingIssues(issues);
            result.issues = issues.ToArray();
            result.rewardResults = rewards.ToArray();
            result.saved = changed && Save();
            result.snapshot = GetSnapshot();
            return result;
        }

        private ActivityCompleteResult FinishComplete(ActivityCompleteResult result, List<ActivityRequirementIssue> issues, List<ActivityRewardResult> rewards, bool changed)
        {
            result.success = !HasBlockingIssues(issues);
            result.issues = issues.ToArray();
            result.rewardResults = rewards.ToArray();
            result.saved = changed && Save();
            result.snapshot = GetSnapshot();
            return result;
        }

        private ActivityCancelResult FinishCancel(ActivityCancelResult result, List<ActivityRequirementIssue> issues, bool changed)
        {
            result.success = changed && !HasBlockingIssues(issues);
            result.issues = issues.ToArray();
            result.saved = changed && Save();
            result.snapshot = GetSnapshot();
            return result;
        }

        private static bool HasBlockingIssues(List<ActivityRequirementIssue> issues)
        {
            foreach (var issue in issues)
            {
                if (string.Equals(issue.issueType, "TickCycleLimitReached", StringComparison.Ordinal))
                    continue;

                return true;
            }

            return false;
        }

        private static void AddIssue(
            List<ActivityRequirementIssue> issues,
            string activityId,
            string issueType,
            string targetId,
            int requiredAmount,
            long currentAmount,
            bool isError,
            bool isNotImplemented,
            string message)
        {
            ActivityResolverUtilities.AddIssue(issues, activityId, issueType, targetId, requiredAmount, currentAmount, isError, isNotImplemented, message);
        }

        private static string NewExecutionId()
        {
            return $"activity_{Guid.NewGuid():N}";
        }

        private static long UnixNow()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
