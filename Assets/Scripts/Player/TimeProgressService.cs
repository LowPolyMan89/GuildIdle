using System;
using System.Collections.Generic;

namespace GuildIdle.Player
{
    public interface ITimeProvider
    {
        long GetUtcNowUnixSeconds();
    }

    public sealed class SystemUtcTimeProvider : ITimeProvider
    {
        public static readonly SystemUtcTimeProvider Instance = new SystemUtcTimeProvider();

        private SystemUtcTimeProvider()
        {
        }

        public long GetUtcNowUnixSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }

    public enum TimeAdvanceResultCode
    {
        Applied,
        NoElapsedTime,
        ClockRollback,
        BaselineInitialized,
        SaveFailed,
        StalePlan
    }

    public sealed class TimeAdvancePlan
    {
        internal TimeAdvancePlan(
            TimeAdvanceResultCode code,
            long previousUtcSeconds,
            long nowUtcSeconds,
            long deltaSeconds)
        {
            Code = code;
            PreviousUtcSeconds = previousUtcSeconds;
            NowUtcSeconds = nowUtcSeconds;
            DeltaSeconds = deltaSeconds;
        }

        public TimeAdvanceResultCode Code { get; }
        public long PreviousUtcSeconds { get; }
        public long NowUtcSeconds { get; }
        public long DeltaSeconds { get; }
    }

    public sealed class HeroEligibilitySnapshot
    {
        private readonly HashSet<string> _eligibleHeroIds;
        private readonly string[] _orderedHeroIds;

        internal HeroEligibilitySnapshot(IEnumerable<string> eligibleHeroIds)
        {
            _eligibleHeroIds = new HashSet<string>(eligibleHeroIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            _orderedHeroIds = new string[_eligibleHeroIds.Count];
            _eligibleHeroIds.CopyTo(_orderedHeroIds);
            Array.Sort(_orderedHeroIds, StringComparer.Ordinal);
        }

        public IReadOnlyList<string> EligibleHeroIds => Array.AsReadOnly(_orderedHeroIds);

        internal bool Contains(string heroId)
        {
            return _eligibleHeroIds.Contains(heroId);
        }
    }

    public sealed class HeroFatigueRecoveryResult
    {
        internal HeroFatigueRecoveryResult(string heroId, int restoredFatigue)
        {
            HeroId = heroId;
            RestoredFatigue = restoredFatigue;
        }

        public string HeroId { get; }
        public int RestoredFatigue { get; }
    }

    public sealed class TimeAdvanceResult
    {
        private readonly HeroFatigueRecoveryResult[] _recoveries;

        internal TimeAdvanceResult(
            bool success,
            TimeAdvanceResultCode code,
            long processedDeltaSeconds,
            HeroFatigueRecoveryResult[] recoveries)
        {
            Success = success;
            Code = code;
            ProcessedDeltaSeconds = processedDeltaSeconds;
            _recoveries = recoveries ?? Array.Empty<HeroFatigueRecoveryResult>();
        }

        public bool Success { get; }
        public TimeAdvanceResultCode Code { get; }
        public long ProcessedDeltaSeconds { get; }
        public IReadOnlyList<HeroFatigueRecoveryResult> Recoveries => Array.AsReadOnly(_recoveries);
    }

    public sealed class TimeProgressService
    {
        public const int FatigueRecoveryIntervalSeconds = 60;

        private readonly PlayerState _state;
        private readonly ITimeProvider _timeProvider;

        internal TimeProgressService(PlayerState state, ITimeProvider timeProvider)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        public TimeAdvancePlan PrepareAdvance()
        {
            return PrepareAdvance(_timeProvider.GetUtcNowUnixSeconds());
        }

        public TimeAdvancePlan PrepareAdvance(long nowUtcSeconds)
        {
            var now = NormalizeUnixSeconds(nowUtcSeconds);
            if (!_state.IsTimeBaselineInitialized)
            {
                return new TimeAdvancePlan(
                    TimeAdvanceResultCode.BaselineInitialized,
                    0L,
                    now,
                    0L);
            }

            var previous = _state.LastProcessedUtcSeconds;
            if (now == previous)
            {
                return new TimeAdvancePlan(
                    TimeAdvanceResultCode.NoElapsedTime,
                    previous,
                    now,
                    0L);
            }

            if (now < previous)
            {
                return new TimeAdvancePlan(
                    TimeAdvanceResultCode.ClockRollback,
                    previous,
                    now,
                    0L);
            }

            return new TimeAdvancePlan(
                TimeAdvanceResultCode.Applied,
                previous,
                now,
                now - previous);
        }

        public HeroEligibilitySnapshot CaptureEligibilitySnapshot()
        {
            var heroIds = _state.GetOrderedHeroIds();
            var eligible = new List<string>(heroIds.Length);
            foreach (var heroId in heroIds)
            {
                if (!_state.IsHeroBusy(heroId))
                    eligible.Add(heroId);
            }

            return new HeroEligibilitySnapshot(eligible);
        }

        public TimeAdvanceResult Apply(TimeAdvancePlan plan, HeroEligibilitySnapshot eligibility)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (eligibility == null)
                throw new ArgumentNullException(nameof(eligibility));

            switch (plan.Code)
            {
                case TimeAdvanceResultCode.BaselineInitialized:
                    if (_state.IsTimeBaselineInitialized)
                        return StalePlan();
                    _state.InitializeTimeBaseline(plan.NowUtcSeconds);
                    return Success(TimeAdvanceResultCode.BaselineInitialized, 0L, Array.Empty<HeroFatigueRecoveryResult>());

                case TimeAdvanceResultCode.NoElapsedTime:
                    return IsCurrentPlan(plan)
                        ? Success(TimeAdvanceResultCode.NoElapsedTime, 0L, Array.Empty<HeroFatigueRecoveryResult>())
                        : StalePlan();

                case TimeAdvanceResultCode.ClockRollback:
                    return IsCurrentPlan(plan)
                        ? Success(TimeAdvanceResultCode.ClockRollback, 0L, Array.Empty<HeroFatigueRecoveryResult>())
                        : StalePlan();

                case TimeAdvanceResultCode.Applied:
                    if (!IsCurrentPlan(plan) || plan.DeltaSeconds <= 0L || plan.NowUtcSeconds <= plan.PreviousUtcSeconds)
                        return StalePlan();
                    return ApplyRecovery(plan, eligibility);

                default:
                    return StalePlan();
            }
        }

        public TimeAdvanceResult AdvanceAndSave()
        {
            var plan = PrepareAdvance();
            var eligibility = CaptureEligibilitySnapshot();
            var before = _state.ToSaveData();
            var applied = Apply(plan, eligibility);
            if (!applied.Success || !MutatesState(applied.Code))
                return applied;

            if (_state.Save())
                return applied;

            _state.RestoreTransactional(before);
            return new TimeAdvanceResult(
                false,
                TimeAdvanceResultCode.SaveFailed,
                0L,
                Array.Empty<HeroFatigueRecoveryResult>());
        }

        internal void EnsureInitializedBaseline()
        {
            if (_state.IsTimeBaselineInitialized)
                return;

            _state.InitializeTimeBaseline(NormalizeUnixSeconds(_timeProvider.GetUtcNowUnixSeconds()));
            _state.MarkNormalized();
        }

        private TimeAdvanceResult ApplyRecovery(TimeAdvancePlan plan, HeroEligibilitySnapshot eligibility)
        {
            var heroIds = _state.GetOrderedHeroIds();
            var recoveries = new HeroFatigueRecoveryResult[heroIds.Length];
            for (var index = 0; index < heroIds.Length; index++)
            {
                var heroId = heroIds[index];
                var restored = 0;
                if (eligibility.Contains(heroId))
                    restored = RecoverHero(heroId, plan.DeltaSeconds);
                recoveries[index] = new HeroFatigueRecoveryResult(heroId, restored);
            }

            _state.SetLastProcessedUtcSeconds(plan.NowUtcSeconds);
            return Success(TimeAdvanceResultCode.Applied, plan.DeltaSeconds, recoveries);
        }

        private int RecoverHero(string heroId, long deltaSeconds)
        {
            var fatigue = _state.GetHeroFatigue(heroId);
            var maxFatigue = _state.GetHeroMaxFatigue(heroId);
            if (fatigue >= maxFatigue)
            {
                _state.SetHeroFatigueRemainderSeconds(heroId, 0);
                return 0;
            }

            var savedRemainder = _state.GetHeroFatigueRemainderSeconds(heroId);
            var deltaPoints = deltaSeconds / FatigueRecoveryIntervalSeconds;
            var combinedRemainder = savedRemainder + (int)(deltaSeconds % FatigueRecoveryIntervalSeconds);
            var restoredPoints = deltaPoints + combinedRemainder / FatigueRecoveryIntervalSeconds;
            var nextRemainder = combinedRemainder % FatigueRecoveryIntervalSeconds;
            var missingFatigue = maxFatigue - fatigue;
            if (restoredPoints >= missingFatigue)
            {
                _state.RestoreHeroFatigue(heroId, missingFatigue);
                _state.SetHeroFatigueRemainderSeconds(heroId, 0);
                return missingFatigue;
            }

            var restored = (int)restoredPoints;
            if (restored > 0)
                _state.RestoreHeroFatigue(heroId, restored);
            _state.SetHeroFatigueRemainderSeconds(heroId, nextRemainder);
            return restored;
        }

        private bool IsCurrentPlan(TimeAdvancePlan plan)
        {
            return _state.IsTimeBaselineInitialized &&
                   _state.LastProcessedUtcSeconds == plan.PreviousUtcSeconds;
        }

        private static bool MutatesState(TimeAdvanceResultCode code)
        {
            return code == TimeAdvanceResultCode.Applied ||
                   code == TimeAdvanceResultCode.BaselineInitialized;
        }

        private static long NormalizeUnixSeconds(long value)
        {
            return Math.Max(0L, value);
        }

        private static TimeAdvanceResult Success(
            TimeAdvanceResultCode code,
            long processedDeltaSeconds,
            HeroFatigueRecoveryResult[] recoveries)
        {
            return new TimeAdvanceResult(true, code, processedDeltaSeconds, recoveries);
        }

        private static TimeAdvanceResult StalePlan()
        {
            return new TimeAdvanceResult(
                false,
                TimeAdvanceResultCode.StalePlan,
                0L,
                Array.Empty<HeroFatigueRecoveryResult>());
        }
    }
}
