using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Editor.Configs;
using GuildIdle.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Editor.Player
{
    public sealed class TimeProgressServiceTests
    {
        private ConfigDatabase _database;
        private FakeTimeProvider _clock;
        private PlayerStateFactory _factory;

        [SetUp]
        public void SetUp()
        {
            _database = new TestConfigDatabaseBuilder()
                .WithFullPlayerStateTestData()
                .Build();
            RuntimeConfigs.SetDatabaseForTests(_database);
            _clock = new FakeTimeProvider(1_000L);
            _factory = CreateFactory(_clock);
        }

        [Test]
        public void NewGameAndResetInitializeBaselineWithoutRecovery()
        {
            var storage = new TestSaveStorage();

            var state = SaveService.Load(_factory, storage);

            AssertTimeProgress(state, 1_000L, 0);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(state.GetHeroMaxFatigue("ren")));

            _clock.UtcNowSeconds = 2_000L;
            var reset = SaveService.ResetSave(_factory, storage);

            AssertTimeProgress(reset, 2_000L, 0);
            Assert.That(reset.GetHeroFatigue("ren"), Is.EqualTo(reset.GetHeroMaxFatigue("ren")));
        }

        [Test]
        public void MissingBaselineIsInitializedWithoutChargingUnknownInterval()
        {
            var original = _factory.CreateDefault();
            Assert.That(original.SpendHeroFatigue("ren", 10), Is.True);
            var saveData = original.ToSaveData();
            saveData.timeProgress = new TimeProgressSaveData
            {
                baselineInitialized = false,
                lastProcessedUtcSeconds = 777L,
                fatigueRemainders = new[]
                {
                    new HeroFatigueRemainderSaveData { heroId = "ren", fatigueRemainderSeconds = 59 }
                }
            };
            _clock.UtcNowSeconds = 5_000L;

            var normalized = _factory.Create(saveData);

            Assert.That(normalized.GetHeroFatigue("ren"), Is.EqualTo(original.GetHeroFatigue("ren")));
            AssertTimeProgress(normalized, 5_000L, 0);
        }

        [Test]
        public void SameTimestampAndClockRollbackAreIdempotentNoOps()
        {
            var storage = new TestSaveStorage();
            var state = SaveService.Load(_factory, storage);
            Assert.That(state.SpendHeroFatigue("ren", 2), Is.True);
            var saveCalls = storage.SaveCalls;

            var same = state.TimeProgress.AdvanceAndSave();
            _clock.UtcNowSeconds = 999L;
            var rollback = state.TimeProgress.AdvanceAndSave();

            Assert.That(same.Success, Is.True);
            Assert.That(same.Code, Is.EqualTo(TimeAdvanceResultCode.NoElapsedTime));
            Assert.That(rollback.Success, Is.True);
            Assert.That(rollback.Code, Is.EqualTo(TimeAdvanceResultCode.ClockRollback));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(state.GetHeroMaxFatigue("ren") - 2));
            AssertTimeProgress(state, 1_000L, 0);
            Assert.That(storage.SaveCalls, Is.EqualTo(saveCalls));
        }

        [Test]
        public void FiftyNinePlusOneSecondUsesPerHeroRemainder()
        {
            var state = CreateBoundState(out _);
            Assert.That(state.SpendHeroFatigue("ren", 2), Is.True);

            _clock.UtcNowSeconds = 1_059L;
            var first = state.TimeProgress.AdvanceAndSave();
            _clock.UtcNowSeconds = 1_060L;
            var second = state.TimeProgress.AdvanceAndSave();

            Assert.That(first.Code, Is.EqualTo(TimeAdvanceResultCode.Applied));
            Assert.That(first.ProcessedDeltaSeconds, Is.EqualTo(59L));
            Assert.That(second.Code, Is.EqualTo(TimeAdvanceResultCode.Applied));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(state.GetHeroMaxFatigue("ren") - 1));
            AssertTimeProgress(state, 1_060L, 0);
        }

        [Test]
        public void MultipleHeroesKeepIndependentRemaindersAndNewHeroStartsAtZero()
        {
            var state = CreateBoundState(out _);
            Assert.That(state.SpendHeroFatigue("ren", 3), Is.True);
            Assert.That(state.AddHero("aska"), Is.True);
            Assert.That(state.SpendHeroFatigue("aska", 3), Is.True);
            AssertRemainder(state, "aska", 0);

            _clock.UtcNowSeconds = 1_030L;
            state.TimeProgress.AdvanceAndSave();
            AssertRemainder(state, "ren", 30);
            AssertRemainder(state, "aska", 30);
            Assert.That(state.SetHeroBusy("aska", "busy-aska"), Is.True);

            _clock.UtcNowSeconds = 1_060L;
            state.TimeProgress.AdvanceAndSave();

            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(state.GetHeroMaxFatigue("ren") - 2));
            Assert.That(state.GetHeroFatigue("aska"), Is.EqualTo(state.GetHeroMaxFatigue("aska") - 3));
            AssertRemainder(state, "ren", 0);
            AssertRemainder(state, "aska", 30);

            Assert.That(state.ClearHeroBusy("aska", "busy-aska"), Is.True);
            _clock.UtcNowSeconds = 1_090L;
            state.TimeProgress.AdvanceAndSave();

            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(state.GetHeroMaxFatigue("ren") - 2));
            Assert.That(state.GetHeroFatigue("aska"), Is.EqualTo(state.GetHeroMaxFatigue("aska") - 2));
            AssertRemainder(state, "ren", 30);
            AssertRemainder(state, "aska", 0);
            Assert.That(state.ToSaveData().timeProgress.fatigueRemainders[0].heroId, Is.EqualTo("aska"));
            Assert.That(state.ToSaveData().timeProgress.fatigueRemainders[1].heroId, Is.EqualTo("ren"));
        }

        [TestCase(ActivityRuntimeStatus.Running)]
        [TestCase(ActivityRuntimeStatus.ResultPending)]
        public void RunningAndResultPendingBlockRecoveryThroughCanonicalBusyContract(ActivityRuntimeStatus status)
        {
            var state = CreateBoundState(out _);
            Assert.That(state.SpendHeroFatigue("ren", 1), Is.True);
            Assert.That(state.AddActivityExecution(new ActivityExecutionSaveData
            {
                executionId = "busy-execution",
                activityId = "combat_first_map_node",
                heroId = "ren",
                status = ActivityRuntimeStatus.Running
            }), Is.True);
            if (status == ActivityRuntimeStatus.ResultPending)
            {
                var execution = state.GetActivityExecution("busy-execution");
                execution.status = ActivityRuntimeStatus.ResultPending;
                execution.pendingResultId = "result:Activity:busy-execution";
                Assert.That(state.UpdateActivityExecution(execution), Is.True);
            }

            _clock.UtcNowSeconds = 1_060L;
            state.TimeProgress.AdvanceAndSave();

            Assert.That(state.IsHeroBusy("ren"), Is.True);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(state.GetHeroMaxFatigue("ren") - 1));
            AssertRemainder(state, "ren", 0);
        }

        [Test]
        public void PausedExecutionWithoutAssignedHeroAllowsRecovery()
        {
            var state = CreateBoundState(out _);
            Assert.That(state.SpendHeroFatigue("ren", 1), Is.True);
            Assert.That(state.AddActivityExecution(new ActivityExecutionSaveData
            {
                executionId = "paused-construction",
                activityId = "combat_first_map_node",
                heroId = "ren",
                status = ActivityRuntimeStatus.Running
            }), Is.True);
            var execution = state.GetActivityExecution("paused-construction");
            execution.status = ActivityRuntimeStatus.Paused;
            execution.heroId = null;
            Assert.That(state.UpdateActivityExecution(execution), Is.True);

            _clock.UtcNowSeconds = 1_060L;
            state.TimeProgress.AdvanceAndSave();

            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(state.GetHeroMaxFatigue("ren")));
            AssertRemainder(state, "ren", 0);
        }

        [Test]
        public void ReachingMaxFatigueDiscardsSurplusTime()
        {
            var state = CreateBoundState(out _);
            Assert.That(state.SpendHeroFatigue("ren", 1), Is.True);
            _clock.UtcNowSeconds = 1_119L;

            state.TimeProgress.AdvanceAndSave();

            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(state.GetHeroMaxFatigue("ren")));
            AssertRemainder(state, "ren", 0);
            Assert.That(state.SpendHeroFatigue("ren", 1), Is.True);

            _clock.UtcNowSeconds = 1_120L;
            state.TimeProgress.AdvanceAndSave();

            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(state.GetHeroMaxFatigue("ren") - 1));
            AssertRemainder(state, "ren", 1);
        }

        [Test]
        public void LargeIntervalMatchesSequentialIntervals()
        {
            var firstClock = new FakeTimeProvider(1_000L);
            var secondClock = new FakeTimeProvider(1_000L);
            var first = CreateFactory(firstClock).CreateDefault();
            var second = CreateFactory(secondClock).CreateDefault();
            Assert.That(first.SpendHeroFatigue("ren", 100), Is.True);
            Assert.That(second.SpendHeroFatigue("ren", 100), Is.True);

            var firstPlan = first.TimeProgress.PrepareAdvance(4_601L);
            first.TimeProgress.Apply(firstPlan, first.TimeProgress.CaptureEligibilitySnapshot());
            for (var now = 1_060L; now <= 4_600L; now += 60L)
            {
                var plan = second.TimeProgress.PrepareAdvance(now);
                second.TimeProgress.Apply(plan, second.TimeProgress.CaptureEligibilitySnapshot());
            }
            var lastPlan = second.TimeProgress.PrepareAdvance(4_601L);
            second.TimeProgress.Apply(lastPlan, second.TimeProgress.CaptureEligibilitySnapshot());

            Assert.That(first.GetHeroFatigue("ren"), Is.EqualTo(second.GetHeroFatigue("ren")));
            Assert.That(first.ToSaveData().timeProgress.lastProcessedUtcSeconds,
                Is.EqualTo(second.ToSaveData().timeProgress.lastProcessedUtcSeconds));
            AssertRemainder(first, "ren", GetRemainder(second, "ren"));
        }

        [Test]
        public void LongMaxTimestampIsProcessedWithoutOverflow()
        {
            var state = _factory.CreateDefault();
            Assert.That(state.SpendHeroFatigue("ren", state.GetHeroMaxFatigue("ren")), Is.True);
            var saveData = state.ToSaveData();
            saveData.timeProgress.lastProcessedUtcSeconds = 0L;
            state = _factory.Create(saveData);

            var plan = state.TimeProgress.PrepareAdvance(long.MaxValue);
            var result = state.TimeProgress.Apply(plan, state.TimeProgress.CaptureEligibilitySnapshot());

            Assert.That(result.Success, Is.True);
            Assert.That(result.ProcessedDeltaSeconds, Is.EqualTo(long.MaxValue));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(state.GetHeroMaxFatigue("ren")));
            AssertTimeProgress(state, long.MaxValue, 0);
        }

        [Test]
        public void ExplicitEligibilitySnapshotIsNotRecalculatedDuringApply()
        {
            var state = CreateBoundState(out _);
            Assert.That(state.SpendHeroFatigue("ren", 1), Is.True);
            var plan = state.TimeProgress.PrepareAdvance(1_060L);
            var eligibleWhileFree = state.TimeProgress.CaptureEligibilitySnapshot();
            Assert.That(state.SetHeroBusy("ren", "became-busy"), Is.True);

            var result = state.TimeProgress.Apply(plan, eligibleWhileFree);

            Assert.That(result.Success, Is.True);
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(state.GetHeroMaxFatigue("ren")));
        }

        [Test]
        public void SaveLoadRoundtripAndRepeatedTimestampDoNotDuplicateRecovery()
        {
            var state = CreateBoundState(out var storage);
            Assert.That(state.SpendHeroFatigue("ren", 2), Is.True);
            _clock.UtcNowSeconds = 1_059L;
            state.TimeProgress.AdvanceAndSave();

            var restored = SaveService.Load(_factory, storage, out var origin);

            Assert.That(origin, Is.EqualTo(SaveLoadOrigin.ExistingV10));
            AssertTimeProgress(restored, 1_059L, 59);
            _clock.UtcNowSeconds = 1_060L;
            var applied = restored.TimeProgress.AdvanceAndSave();
            var reloaded = SaveService.Load(_factory, storage);
            var repeated = reloaded.TimeProgress.AdvanceAndSave();

            Assert.That(applied.Code, Is.EqualTo(TimeAdvanceResultCode.Applied));
            Assert.That(repeated.Code, Is.EqualTo(TimeAdvanceResultCode.NoElapsedTime));
            Assert.That(reloaded.GetHeroFatigue("ren"), Is.EqualTo(reloaded.GetHeroMaxFatigue("ren") - 1));
            AssertTimeProgress(reloaded, 1_060L, 0);
        }

        [Test]
        public void SaveFailureRollsBackFatigueRemainderAndTimestamp()
        {
            var state = CreateBoundState(out var storage);
            Assert.That(state.SpendHeroFatigue("ren", 2), Is.True);
            var before = state.ToSaveData();
            storage.ThrowOnSave = true;
            _clock.UtcNowSeconds = 1_061L;
            LogAssert.Expect(LogType.Error, "[SaveService] Failed to save player state. Expected save failure.");

            var result = state.TimeProgress.AdvanceAndSave();

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo(TimeAdvanceResultCode.SaveFailed));
            Assert.That(state.GetHeroFatigue("ren"), Is.EqualTo(before.heroes[0].fatigue));
            Assert.That(state.ToSaveData().timeProgress.lastProcessedUtcSeconds,
                Is.EqualTo(before.timeProgress.lastProcessedUtcSeconds));
            AssertRemainder(state, "ren", before.timeProgress.fatigueRemainders[0].fatigueRemainderSeconds);
        }

        [Test]
        public void SuccessfulConvenienceAdvanceCommitsExactlyOnce()
        {
            var state = CreateBoundState(out var storage);
            Assert.That(state.SpendHeroFatigue("ren", 1), Is.True);
            var before = storage.SaveCalls;
            _clock.UtcNowSeconds = 1_060L;

            var result = state.TimeProgress.AdvanceAndSave();

            Assert.That(result.Success, Is.True);
            Assert.That(storage.SaveCalls, Is.EqualTo(before + 1));
        }

        private PlayerStateFactory CreateFactory(ITimeProvider timeProvider)
        {
            return TestPlayerComposition.CreatePlayerStateFactory(_database, timeProvider: timeProvider);
        }

        private PlayerState CreateBoundState(out TestSaveStorage storage)
        {
            storage = new TestSaveStorage();
            return SaveService.Load(_factory, storage);
        }

        private static void AssertTimeProgress(PlayerState state, long expectedTimestamp, int expectedRenRemainder)
        {
            var timeProgress = state.ToSaveData().timeProgress;
            Assert.That(timeProgress.baselineInitialized, Is.True);
            Assert.That(timeProgress.lastProcessedUtcSeconds, Is.EqualTo(expectedTimestamp));
            AssertRemainder(state, "ren", expectedRenRemainder);
        }

        private static void AssertRemainder(PlayerState state, string heroId, int expected)
        {
            Assert.That(GetRemainder(state, heroId), Is.EqualTo(expected));
        }

        private static int GetRemainder(PlayerState state, string heroId)
        {
            foreach (var entry in state.ToSaveData().timeProgress.fatigueRemainders)
            {
                if (string.Equals(entry.heroId, heroId, StringComparison.Ordinal))
                    return entry.fatigueRemainderSeconds;
            }

            Assert.Fail($"Missing fatigue remainder for hero '{heroId}'.");
            return -1;
        }

        private sealed class FakeTimeProvider : ITimeProvider
        {
            public FakeTimeProvider(long utcNowSeconds)
            {
                UtcNowSeconds = utcNowSeconds;
            }

            public long UtcNowSeconds { get; set; }

            public long GetUtcNowUnixSeconds()
            {
                return UtcNowSeconds;
            }
        }

        private sealed class TestSaveStorage : ISaveStorage
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

            public int SaveCalls { get; private set; }
            public bool ThrowOnSave { get; set; }

            public bool HasKey(string key)
            {
                return _values.ContainsKey(key);
            }

            public string GetString(string key, string defaultValue)
            {
                return _values.TryGetValue(key, out var value) ? value : defaultValue;
            }

            public void SetString(string key, string value)
            {
                _values[key] = value;
            }

            public void DeleteKey(string key)
            {
                _values.Remove(key);
            }

            public void Save()
            {
                SaveCalls++;
                if (ThrowOnSave)
                    throw new InvalidOperationException("Expected save failure.");
            }
        }
    }
}
