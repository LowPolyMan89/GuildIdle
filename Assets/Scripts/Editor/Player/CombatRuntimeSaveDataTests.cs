using System;
using System.Collections.Generic;
using System.Text;
using GuildIdle.Combat;
using GuildIdle.Editor.Configs;
using GuildIdle.Player;
using NUnit.Framework;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Editor.Player
{
    public sealed class CombatRuntimeSaveDataTests
    {
        private const int CollectionLimit = 64;
        private PlayerStateFactory _factory;

        [SetUp]
        public void SetUp()
        {
            var database = new TestConfigDatabaseBuilder().WithFullPlayerStateTestData().Build();
            RuntimeConfigs.SetDatabaseForTests(database);
            _factory = TestPlayerComposition.CreatePlayerStateFactory(database);
        }

        [Test]
        public void FullAggregateRoundtripPreservesSimulationPositionAndStableLinks()
        {
            var storage = new MemorySaveStorage();
            var state = _factory.CreateDefault();
            var source = Aggregate("combat-a", "session-a", "ren", "group-wolves", "wolf");
            source.session.combatTimeSeconds = 12.75d;
            source.session.scheduler.nextSequence = 41;
            source.session.scheduler.lastResolvedEventKey = "event-40";
            source.session.scheduler.scheduledEvents = new[]
            {
                new CombatScheduledEventSaveData
                {
                    eventKey = "event-41",
                    eventType = CombatRuntimeService.ActorAttackEventType,
                    timestampSeconds = 13d,
                    phasePriority = (int)CombatScheduledEventPhase.ActorAttack,
                    actorSide = CombatActorSide.Hero,
                    sequence = 40
                }
            };
            source.session.rng.algorithmId = "test-xoshiro";
            source.session.rng.formatVersion = 3;
            source.session.rng.state = "opaque-state-v3:0123456789abcdef";
            source.session.rng.drawCount = 17;
            source.session.hero.abilityCooldowns = new[]
            {
                new CombatAbilityCooldownSaveData
                {
                    abilityId = "hero-ability",
                    nextReadyAtSeconds = 15d,
                    lastTriggerEventKey = "trigger-1",
                    lastChanceRoll = 37,
                    lastChanceResolved = true
                }
            };
            source.session.hero.statuses = new[]
            {
                new CombatStatusInstanceSaveData
                {
                    statusInstanceId = "status-1",
                    statusId = "bleed_weak",
                    sourceCombatantId = "enemy-0",
                    stacks = 2,
                    expiresAtSeconds = 20d,
                    nextTickAtSeconds = 13d,
                    lastEventKey = "status-event"
                }
            };
            source.session.hero.independentModifiers = new[]
            {
                new CombatTemporaryModifierSaveData
                {
                    modifierInstanceId = "modifier-1",
                    sourceId = "ability-independent",
                    statId = "damage",
                    operation = "Add",
                    value = 3.5f,
                    expiresAtSeconds = 19d
                }
            };
            source.session.loot = new[] { Reward("loot-1", 0, "Resource", "resource_pine_wood", 4, "combat_loot") };
            source.session.accumulatedEnemyExp = 23;
            source.session.completionRewards = new[] { Reward("reward-1", 0, "Gold", "gold_id", 7, "activity_loot_in_combat") };
            source.session.broughtConsumable = new CombatConsumableStateSaveData
            {
                originStackId = "stack-food",
                itemId = "item_food",
                initialQuantity = 3,
                remainingQuantity = 2,
                nextCheckAtSeconds = 13d,
                nextAllowedUseAtSeconds = 17d,
                lastAppliedEventKey = "consume-1"
            };
            source.session.terminalCandidate = new CombatTerminalCandidateSaveData
            {
                candidateId = "candidate-1",
                kind = "DefeatCandidate",
                eventKey = "terminal-1",
                createdAtSeconds = 12.75d
            };
            source.session.simulationStopped = true;

            Assert.That(state.AddCombatAggregate(source), Is.True);
            var resultPending = state.GetCombatAggregate("combat-a");
            resultPending.execution.status = CombatExecutionStatus.ResultPending;
            resultPending.execution.outcome = "Defeat";
            resultPending.execution.outcomeFinalized = true;
            resultPending.execution.resultCreated = true;
            resultPending.execution.pendingResultId = "result:Combat:combat-a";
            Assert.That(state.UpdateCombatAggregate(resultPending), Is.True);
            var before = JsonUtility.ToJson(state.ToSaveData());
            Assert.That(SaveService.Save(state, storage), Is.True);

            var restored = SaveService.Load(_factory, storage, out var origin);
            var after = JsonUtility.ToJson(restored.ToSaveData());
            var aggregate = restored.GetCombatAggregate("combat-a");

            Assert.That(origin, Is.EqualTo(SaveLoadOrigin.ExistingV9));
            Assert.That(after, Is.EqualTo(before));
            Assert.That(aggregate.execution.sessionId, Is.EqualTo("session-a"));
            Assert.That(aggregate.execution.pendingResultId, Is.EqualTo("result:Combat:combat-a"));
            Assert.That(aggregate.session.executionId, Is.EqualTo("combat-a"));
            Assert.That(aggregate.session.combatTimeSeconds, Is.EqualTo(12.75d));
            Assert.That(aggregate.session.scheduler.nextSequence, Is.EqualTo(41));
            Assert.That(aggregate.session.scheduler.scheduledEvents[0].eventKey, Is.EqualTo("event-41"));
            Assert.That(aggregate.session.rng.algorithmId, Is.EqualTo("test-xoshiro"));
            Assert.That(aggregate.session.rng.formatVersion, Is.EqualTo(3));
            Assert.That(aggregate.session.rng.state, Is.EqualTo("opaque-state-v3:0123456789abcdef"));
            Assert.That(aggregate.session.rng.drawCount, Is.EqualTo(17));
            Assert.That(aggregate.session.hero.statuses[0].statusInstanceId, Is.EqualTo("status-1"));
            Assert.That(aggregate.session.hero.independentModifiers[0].modifierInstanceId, Is.EqualTo("modifier-1"));
            Assert.That(restored.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo("combat-a"));
            Assert.That(after, Does.Not.Contain("derivedModifiers"));

            Assert.That(SaveService.Save(restored, storage), Is.True);
            var restoredAgain = SaveService.Load(_factory, storage, out var repeatedOrigin);
            var repeated = JsonUtility.ToJson(restoredAgain.ToSaveData());
            Assert.That(repeatedOrigin, Is.EqualTo(SaveLoadOrigin.ExistingV9));
            Assert.That(repeated, Is.EqualTo(after));
            Assert.That(restoredAgain.GetCombatAggregate("combat-a").session.rng.state,
                Is.EqualTo("opaque-state-v3:0123456789abcdef"));
        }

        [Test]
        public void StoreReturnsDeepCopiesAndUpdateRejectsIdentityOrLimitChangesAtomically()
        {
            var state = _factory.CreateDefault();
            Assert.That(state.AddCombatAggregate(Aggregate("combat-a", "session-a", "ren", "group-a", "enemy-a")), Is.True);

            var detached = state.GetCombatAggregate("combat-a");
            detached.session.hero.currentHp = 1;
            detached.session.enemyQueue[0].enemyId = "mutated";
            detached.session.rng.algorithmId = "mutated-rng";
            detached.session.rng.formatVersion = 99;
            detached.session.rng.state = "mutated-state";
            detached.session.rng.drawCount = 99;
            Assert.That(state.GetCombatAggregate("combat-a").session.hero.currentHp, Is.EqualTo(100));
            Assert.That(state.GetCombatAggregate("combat-a").session.enemyQueue[0].enemyId, Is.EqualTo("enemy-a"));
            Assert.That(state.GetCombatAggregate("combat-a").session.rng.algorithmId, Is.EqualTo("test-rng"));
            Assert.That(state.GetCombatAggregate("combat-a").session.rng.formatVersion, Is.EqualTo(1));
            Assert.That(state.GetCombatAggregate("combat-a").session.rng.state, Is.EqualTo("fixture-state"));
            Assert.That(state.GetCombatAggregate("combat-a").session.rng.drawCount, Is.Zero);

            var before = JsonUtility.ToJson(state.ToSaveData());
            var identityMutation = state.GetCombatAggregate("combat-a");
            identityMutation.execution.sourceActivityId = "another-source";
            using (new SuppressedLogHandler())
                Assert.That(state.UpdateCombatAggregate(identityMutation), Is.False);
            Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(before));

            var oversized = state.GetCombatAggregate("combat-a");
            oversized.session.hero.abilityCooldowns = Cooldowns(CollectionLimit + 1);
            using (new SuppressedLogHandler())
                Assert.That(state.UpdateCombatAggregate(oversized), Is.False);
            Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(before));

            using (new SuppressedLogHandler())
                Assert.That(state.AddCombatAggregate(Aggregate("combat-b", "session-b", "ren", "group-b", "enemy-b")), Is.False);
            Assert.That(state.GetCombatAggregates(), Has.Length.EqualTo(1));
        }

        [Test]
        public void InvalidRngDescriptorIsRejectedAtomically()
        {
            var state = _factory.CreateDefault();
            Assert.That(state.AddCombatAggregate(Aggregate("combat-a", "session-a", "ren", "group-a", "enemy-a")), Is.True);
            var before = JsonUtility.ToJson(state.ToSaveData());
            var invalidDescriptors = new[]
            {
                new CombatRngStateSaveData { algorithmId = " ", formatVersion = 1, state = "state", drawCount = 0 },
                new CombatRngStateSaveData { algorithmId = "rng", formatVersion = 0, state = "state", drawCount = 0 },
                new CombatRngStateSaveData { algorithmId = "rng", formatVersion = 1, state = " ", drawCount = 0 },
                new CombatRngStateSaveData { algorithmId = "rng", formatVersion = 1, state = "state", drawCount = -1 }
            };

            foreach (var invalidDescriptor in invalidDescriptors)
            {
                var update = state.GetCombatAggregate("combat-a");
                update.session.rng = invalidDescriptor;
                using (new SuppressedLogHandler())
                    Assert.That(state.UpdateCombatAggregate(update), Is.False);
                Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(before));
            }

            var addState = _factory.CreateDefault();
            var beforeAdd = JsonUtility.ToJson(addState.ToSaveData());
            var invalidAdd = Aggregate("combat-b", "session-b", "ren", "group-b", "enemy-b");
            invalidAdd.session.rng.state = null;
            using (new SuppressedLogHandler())
                Assert.That(addState.AddCombatAggregate(invalidAdd), Is.False);
            Assert.That(JsonUtility.ToJson(addState.ToSaveData()), Is.EqualTo(beforeAdd));
        }

        [Test]
        public void TerminalStateDoesNotReleaseHeroUntilExecutionAndPendingResultAreResolved()
        {
            var state = _factory.CreateDefault();
            Assert.That(state.AddCombatAggregate(Aggregate("combat-a", "session-a", "ren", "group-a", "enemy-a")), Is.True);

            var terminal = state.GetCombatAggregate("combat-a");
            terminal.session.terminalCandidate = new CombatTerminalCandidateSaveData
            {
                candidateId = "candidate-a",
                kind = "VictoryCandidate",
                eventKey = "terminal-a",
                createdAtSeconds = 3d
            };
            terminal.session.simulationStopped = true;
            Assert.That(state.UpdateCombatAggregate(terminal), Is.True);
            Assert.That(state.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo("combat-a"));

            var pending = state.GetCombatAggregate("combat-a");
            pending.execution.status = CombatExecutionStatus.ResultPending;
            pending.execution.outcome = "Victory";
            pending.execution.outcomeFinalized = true;
            pending.execution.resultCreated = true;
            pending.execution.pendingResultId = "result:Combat:combat-a";
            Assert.That(state.UpdateCombatAggregate(pending), Is.True);
            Assert.That(state.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo("combat-a"));

            var completed = state.GetCombatAggregate("combat-a");
            completed.execution.status = CombatExecutionStatus.Completed;
            completed.execution.pendingResultResolved = true;
            completed.execution.completedAtUnixSeconds = 200;
            Assert.That(state.UpdateCombatAggregate(completed), Is.True);
            Assert.That(state.IsHeroBusy("ren"), Is.False);
            Assert.That(state.RemoveCombatAggregate("combat-a"), Is.True);
            Assert.That(state.GetCombatAggregates(), Is.Empty);
        }

        [Test]
        public void LoadReconcilesPairsDeterministicallyWithoutTruncatingDamagedSessions()
        {
            var valid = Aggregate("combat-valid", "session-valid", "ren", "group-valid", "enemy-valid");
            valid.session.hero.abilityCooldowns = null;
            valid.session.hero.statuses = null;
            valid.session.hero.independentModifiers = null;
            var broken = Aggregate("combat-broken", "session-broken", "ren", "group-broken", "enemy-broken");
            broken.session.loot = Rewards(CollectionLimit + 1, "oversized");
            var orphanExecution = Aggregate("combat-orphan", "session-orphan", "ren", "group-orphan", "enemy-orphan").execution;
            var duplicateA = Aggregate("combat-duplicate", "session-duplicate-a", "ren", "group-duplicate", "enemy-a");
            var duplicateB = Aggregate("combat-duplicate", "session-duplicate-b", "ren", "group-duplicate", "enemy-b");
            var save = _factory.CreateDefault().ToSaveData();
            save.combatRuntime = new CombatRuntimeSaveData
            {
                executions = new[] { orphanExecution, duplicateB.execution, broken.execution, valid.execution, duplicateA.execution },
                sessions = new[] { duplicateA.session, broken.session, valid.session, duplicateB.session }
            };

            PlayerState loaded;
            using (new SuppressedLogHandler())
                loaded = _factory.Create(save);

            Assert.That(loaded.GetCombatAggregates(), Has.Length.EqualTo(1));
            Assert.That(loaded.GetCombatAggregate("combat-valid"), Is.Not.Null);
            Assert.That(loaded.GetCombatAggregate("combat-broken"), Is.Null);
            Assert.That(loaded.GetCombatAggregate("combat-orphan"), Is.Null);
            Assert.That(loaded.GetCombatAggregate("combat-duplicate"), Is.Null);
            Assert.That(loaded.GetCombatAggregate("combat-valid").session.hero.abilityCooldowns, Is.Empty);
            Assert.That(loaded.GetCombatAggregate("combat-valid").session.hero.statuses, Is.Empty);
            Assert.That(loaded.GetCombatAggregate("combat-valid").session.hero.independentModifiers, Is.Empty);
            Assert.That(loaded.GetCombatAggregate("combat-valid").execution.executionId, Is.EqualTo("combat-valid"));
            Assert.That(loaded.GetCombatAggregate("combat-valid").session.sessionId, Is.EqualTo("session-valid"));
        }

        [Test]
        public void DifferentEnemyGroupUsesTheSameAggregatePath()
        {
            var first = _factory.CreateDefault();
            var second = _factory.CreateDefault();

            Assert.That(first.AddCombatAggregate(Aggregate("combat-wolves", "session-wolves", "ren", "enemy_group_wolves", "wolf")), Is.True);
            Assert.That(second.AddCombatAggregate(Aggregate("combat-bandits", "session-bandits", "ren", "enemy_group_bandits", "bandit")), Is.True);

            Assert.That(first.GetCombatAggregate("combat-wolves").session.enemyGroupId, Is.EqualTo("enemy_group_wolves"));
            Assert.That(second.GetCombatAggregate("combat-bandits").session.enemyGroupId, Is.EqualTo("enemy_group_bandits"));
            Assert.That(second.GetCombatAggregate("combat-bandits").session.currentEnemy.definitionId, Is.EqualTo("bandit"));
        }

        [Test]
        public void RepresentativeMaxFilledCombatRuntimeKeepsFullSaveBelowTwoHundredKilobytes()
        {
            var state = _factory.CreateDefault();
            var aggregate = Aggregate("combat-max", "session-max", "ren", "group-max", "enemy-0");
            aggregate.session.enemyQueue = Queue(CollectionLimit);
            aggregate.session.currentEnemy = Combatant("enemy-combatant-0", "enemy-0");
            aggregate.session.hero.abilityCooldowns = Cooldowns(CollectionLimit);
            aggregate.session.hero.statuses = Statuses(CollectionLimit, "hero-ren");
            aggregate.session.hero.independentModifiers = Modifiers(CollectionLimit);
            aggregate.session.currentEnemy.abilityCooldowns = Cooldowns(CollectionLimit);
            aggregate.session.currentEnemy.statuses = Statuses(CollectionLimit, "enemy-combatant-0");
            aggregate.session.currentEnemy.independentModifiers = Modifiers(CollectionLimit);
            aggregate.session.loot = Rewards(CollectionLimit, "loot");
            aggregate.session.completionRewards = Rewards(CollectionLimit, "completion");

            Assert.That(state.AddCombatAggregate(aggregate), Is.True);
            var bytes = Encoding.UTF8.GetByteCount(JsonUtility.ToJson(state.ToSaveData()));

            Assert.That(bytes, Is.LessThan(200 * 1024));
        }

        private static CombatRuntimeAggregate Aggregate(string executionId, string sessionId, string heroId, string groupId, string enemyId)
        {
            var enemyCombatantId = $"{sessionId}:enemy:0";
            return new CombatRuntimeAggregate
            {
                execution = new CombatExecutionSaveData
                {
                    executionId = executionId,
                    sessionId = sessionId,
                    sourceActivityId = "combat_activity",
                    sourceExecutionId = executionId,
                    sourceRequestId = $"request:{executionId}",
                    occupationOwnerId = executionId,
                    heroId = heroId,
                    status = CombatExecutionStatus.Running,
                    startedAtUnixSeconds = 100
                },
                session = new CombatSessionSaveData
                {
                    sessionId = sessionId,
                    executionId = executionId,
                    enemyGroupId = groupId,
                    combatMode = "Queue_1v1",
                    enemyQueue = new[]
                    {
                        new CombatEnemyQueueEntrySaveData { combatantId = enemyCombatantId, enemyId = enemyId, level = 1, queueIndex = 0 }
                    },
                    queuePosition = 0,
                    hero = Combatant($"hero-{heroId}", heroId),
                    currentEnemy = Combatant(enemyCombatantId, enemyId),
                    scheduler = new CombatSchedulerStateSaveData(),
                    rng = new CombatRngStateSaveData
                    {
                        algorithmId = "test-rng",
                        formatVersion = 1,
                        state = "fixture-state"
                    }
                }
            };
        }

        private static CombatantStateSaveData Combatant(string combatantId, string definitionId)
        {
            return new CombatantStateSaveData
            {
                combatantId = combatantId,
                definitionId = definitionId,
                currentHp = 100,
                maxHp = 100,
                nextAttackAtSeconds = 1d
            };
        }

        private static CombatEnemyQueueEntrySaveData[] Queue(int count)
        {
            var result = new CombatEnemyQueueEntrySaveData[count];
            for (var index = 0; index < count; index++)
                result[index] = new CombatEnemyQueueEntrySaveData
                {
                    combatantId = $"enemy-combatant-{index}",
                    enemyId = $"enemy-{index}",
                    level = index + 1,
                    queueIndex = index
                };
            return result;
        }

        private static CombatAbilityCooldownSaveData[] Cooldowns(int count)
        {
            var result = new CombatAbilityCooldownSaveData[count];
            for (var index = 0; index < count; index++)
                result[index] = new CombatAbilityCooldownSaveData
                {
                    abilityId = $"ability-{index:D2}",
                    nextReadyAtSeconds = index + 1d,
                    lastTriggerEventKey = $"ability-event-{index:D2}",
                    lastChanceRoll = index,
                    lastChanceResolved = true
                };
            return result;
        }

        private static CombatStatusInstanceSaveData[] Statuses(int count, string sourceCombatantId)
        {
            var result = new CombatStatusInstanceSaveData[count];
            for (var index = 0; index < count; index++)
                result[index] = new CombatStatusInstanceSaveData
                {
                    statusInstanceId = $"status-instance-{index:D2}",
                    statusId = $"status-{index:D2}",
                    sourceCombatantId = sourceCombatantId,
                    stacks = index + 1,
                    expiresAtSeconds = index + 10d,
                    nextTickAtSeconds = index + 1d,
                    lastEventKey = $"status-event-{index:D2}"
                };
            return result;
        }

        private static CombatTemporaryModifierSaveData[] Modifiers(int count)
        {
            var result = new CombatTemporaryModifierSaveData[count];
            for (var index = 0; index < count; index++)
                result[index] = new CombatTemporaryModifierSaveData
                {
                    modifierInstanceId = $"modifier-{index:D2}",
                    sourceId = $"source-{index:D2}",
                    statId = $"stat-{index:D2}",
                    operation = "Add",
                    value = index + 0.5f,
                    expiresAtSeconds = index + 10d
                };
            return result;
        }

        private static CombatRewardEntrySaveData[] Rewards(int count, string prefix)
        {
            var result = new CombatRewardEntrySaveData[count];
            for (var index = 0; index < count; index++)
                result[index] = Reward($"{prefix}-{index:D2}", index, "Resource", $"resource-{index:D2}", index + 1, "combat_loot");
            return result;
        }

        private static CombatRewardEntrySaveData Reward(string entryId, int sortOrder, string rewardType, string targetId, long quantity, string origin)
        {
            return new CombatRewardEntrySaveData
            {
                entryId = entryId,
                sortOrder = sortOrder,
                rewardType = rewardType,
                targetId = targetId,
                quantity = quantity,
                origin = origin
            };
        }

        private sealed class MemorySaveStorage : ISaveStorage
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
            public bool HasKey(string key) => _values.ContainsKey(key);
            public string GetString(string key, string defaultValue) => _values.TryGetValue(key, out var value) ? value : defaultValue;
            public void SetString(string key, string value) => _values[key] = value;
            public void DeleteKey(string key) => _values.Remove(key);
            public void Save() { }
        }

        private sealed class SuppressedLogHandler : ILogHandler, IDisposable
        {
            private readonly ILogHandler _previous = Debug.unityLogger.logHandler;
            public SuppressedLogHandler() => Debug.unityLogger.logHandler = this;
            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args) { }
            public void LogException(Exception exception, UnityEngine.Object context) { }
            public void Dispose() => Debug.unityLogger.logHandler = _previous;
        }
    }
}
