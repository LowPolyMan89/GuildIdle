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
        private const int StatusStackLimit = 8;
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
                    stackIds = new[] { "status-1-stack-1", "status-1-stack-2" },
                    expiresAtSeconds = 20d,
                    nextTickAtSeconds = 13d,
                    lastApplyEventKey = "status-apply-event",
                    lastTickEventKey = "status-tick-event"
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
                    expiresAtSeconds = 19d,
                    appliedEventKey = "modifier-apply-event"
                }
            };
            source.session.loot = new[] { Reward("loot-1", 0, "Resource", "resource_pine_wood", 4, "combat_loot") };
            source.session.accumulatedEnemyExp = 23;
            source.session.completionRewards = new[] { Reward("reward-1", 0, "Gold", "gold_id", 7, "activity_loot_in_combat") };
            source.session.broughtConsumable = new CombatConsumableStateSaveData
            {
                sourceStackId = "stack-food",
                itemId = "item_food",
                initialQuantity = 3,
                remainingQuantity = 2,
                nextCheckAtSeconds = 13d,
                nextAllowedUseAtSeconds = 17d,
                lastAppliedEventKey = "consume-1"
            };
            source.session.lastDeathPreventionOperation =
                new CombatDeathPreventionOperationSaveData
                {
                    operationKey = "damage-1:death-prevention:hero:skill:effect",
                    targetCombatantId = "hero:ren",
                    effectId = "effect",
                    chanceRoll = 2500,
                    successful = true
                };
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
            Assert.That(
                aggregate.session.lastDeathPreventionOperation.operationKey,
                Is.EqualTo("damage-1:death-prevention:hero:skill:effect"));
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
        public void LegacyOriginStackIdNormalizesIntoSourceStackId()
        {
            var aggregate = Aggregate(
                "combat-legacy-loadout",
                "session-legacy-loadout",
                "ren",
                "group-legacy-loadout",
                "enemy-legacy-loadout");
            aggregate.session.loadoutKind = CombatLoadoutKind.Consumable;
            aggregate.session.broughtConsumable = new CombatConsumableStateSaveData
            {
                sourceStackId = "legacy-stack",
                itemId = "item-food",
                initialQuantity = 2,
                remainingQuantity = 1
            };
            var save = _factory.CreateDefault().ToSaveData();
            save.combatRuntime = new CombatRuntimeSaveData
            {
                executions = new[] { aggregate.execution },
                sessions = new[] { aggregate.session }
            };
            var legacyJson = JsonUtility.ToJson(save).Replace(
                "\"sourceStackId\":\"legacy-stack\"",
                "\"originStackId\":\"legacy-stack\"");
            var storage = new MemorySaveStorage();
            storage.SetString(SaveService.SaveKey, legacyJson);

            var restored = SaveService.Load(_factory, storage, out var origin);
            var normalizedJson = storage.GetString(SaveService.SaveKey, string.Empty);

            Assert.That(origin, Is.EqualTo(SaveLoadOrigin.ExistingV9));
            Assert.That(
                restored.GetCombatAggregate("combat-legacy-loadout").session.broughtConsumable.sourceStackId,
                Is.EqualTo("legacy-stack"));
            Assert.That(normalizedJson, Does.Contain("\"sourceStackId\":\"legacy-stack\""));
            Assert.That(normalizedJson, Does.Not.Contain("\"originStackId\":\"legacy-stack\""));
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
        public void AddCombatAggregateRejectsDuplicateSessionIdAtomically()
        {
            var state = _factory.CreateDefault();
            Assert.That(state.AddHero("aska"), Is.True);
            var first = Aggregate(
                "combat-first",
                "shared-session",
                "ren",
                "group-first",
                "enemy-first");
            var second = Aggregate(
                "combat-second",
                "shared-session",
                "aska",
                "group-second",
                "enemy-second");
            Assert.That(state.AddCombatAggregate(first), Is.True);
            var before = JsonUtility.ToJson(
                state.GetCombatAggregate("combat-first"));

            bool added;
            using (new SuppressedLogHandler())
                added = state.AddCombatAggregate(second);

            Assert.That(added, Is.False);
            Assert.That(
                JsonUtility.ToJson(state.GetCombatAggregate("combat-first")),
                Is.EqualTo(before));
            Assert.That(state.GetCombatAggregate("combat-second"), Is.Null);
            Assert.That(
                state.GetHeroCurrentActivityExecutionId("aska"),
                Is.Null);
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

        [TestCase("over-limit")]
        [TestCase("duplicate-event-key")]
        [TestCase("duplicate-sequence")]
        [TestCase("negative-sequence")]
        [TestCase("sequence-at-next")]
        [TestCase("duplicate-hero-attack")]
        [TestCase("duplicate-consumable-check")]
        [TestCase("system-actor-attack")]
        [TestCase("wrong-attack-phase")]
        [TestCase("empty-event-key")]
        [TestCase("empty-event-type")]
        [TestCase("negative-timestamp")]
        [TestCase("nan-timestamp")]
        [TestCase("infinite-timestamp")]
        public void InvalidPendingSchedulerIsRejectedAtomically(string scenario)
        {
            var state = _factory.CreateDefault();
            var before = JsonUtility.ToJson(state.ToSaveData());
            var aggregate = Aggregate("combat-invalid", "session-invalid", "ren", "group-invalid", "enemy-invalid");
            ConfigureInvalidScheduler(aggregate.session.scheduler, scenario);

            using (new SuppressedLogHandler())
                Assert.That(state.AddCombatAggregate(aggregate), Is.False);

            Assert.That(state.GetCombatAggregates(), Is.Empty);
            Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(before));
        }

        [TestCase("simulation-running")]
        [TestCase("living-hero")]
        [TestCase("scheduled-event")]
        [TestCase("timestamp-mismatch")]
        [TestCase("unsupported-kind")]
        [TestCase("conflicting-outcome")]
        public void InvalidDefeatTerminalStateIsRejectedAtomically(string scenario)
        {
            var state = _factory.CreateDefault();
            var before = JsonUtility.ToJson(state.ToSaveData());
            var aggregate =
                Aggregate("combat-terminal", "session-terminal", "ren", "group-terminal", "enemy-terminal");
            ConfigureDefeatTerminal(aggregate, 3d);

            switch (scenario)
            {
                case "simulation-running":
                    aggregate.session.simulationStopped = false;
                    break;
                case "living-hero":
                    aggregate.session.hero.currentHp = 1;
                    break;
                case "scheduled-event":
                    aggregate.session.scheduler.nextSequence = 1;
                    aggregate.session.scheduler.scheduledEvents =
                        new[] { FutureEvent(0, "terminal-future") };
                    break;
                case "timestamp-mismatch":
                    aggregate.session.terminalCandidate.createdAtSeconds = 2d;
                    break;
                case "unsupported-kind":
                    aggregate.session.terminalCandidate.kind = "Victory";
                    break;
                case "conflicting-outcome":
                    aggregate.execution.outcome = "Victory";
                    aggregate.execution.outcomeFinalized = true;
                    break;
                default:
                    Assert.Fail($"Unknown terminal scenario '{scenario}'.");
                    break;
            }

            using (new SuppressedLogHandler())
                Assert.That(state.AddCombatAggregate(aggregate), Is.False);

            Assert.That(state.GetCombatAggregates(), Is.Empty);
            Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(before));
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
                kind = CombatTerminalCandidateKinds.Defeat,
                eventKey = "terminal-a",
                createdAtSeconds = 3d
            };
            terminal.session.combatTimeSeconds = 3d;
            terminal.session.hero.currentHp = 0;
            terminal.session.scheduler.scheduledEvents =
                Array.Empty<CombatScheduledEventSaveData>();
            terminal.session.simulationStopped = true;
            Assert.That(state.UpdateCombatAggregate(terminal), Is.True);
            Assert.That(state.GetHeroCurrentActivityExecutionId("ren"), Is.EqualTo("combat-a"));

            var pending = state.GetCombatAggregate("combat-a");
            pending.execution.status = CombatExecutionStatus.ResultPending;
            pending.execution.outcome = "Defeat";
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
            aggregate.session.scheduler.nextSequence = CollectionLimit;
            aggregate.session.scheduler.scheduledEvents = new CombatScheduledEventSaveData[CollectionLimit];
            for (var index = 0; index < CollectionLimit; index++)
                aggregate.session.scheduler.scheduledEvents[index] = FutureEvent(index, $"future-{index:D2}");
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

        private static void ConfigureDefeatTerminal(
            CombatRuntimeAggregate aggregate,
            double createdAtSeconds)
        {
            aggregate.session.combatTimeSeconds = createdAtSeconds;
            aggregate.session.hero.currentHp = 0;
            aggregate.session.scheduler.scheduledEvents =
                Array.Empty<CombatScheduledEventSaveData>();
            aggregate.session.terminalCandidate = new CombatTerminalCandidateSaveData
            {
                candidateId = $"{aggregate.session.sessionId}:defeat",
                kind = CombatTerminalCandidateKinds.Defeat,
                eventKey = $"{aggregate.session.sessionId}:terminal:defeat",
                createdAtSeconds = createdAtSeconds
            };
            aggregate.session.simulationStopped = true;
        }

        private static void ConfigureInvalidScheduler(CombatSchedulerStateSaveData scheduler, string scenario)
        {
            switch (scenario)
            {
                case "over-limit":
                    scheduler.nextSequence = CollectionLimit + 1;
                    scheduler.scheduledEvents = new CombatScheduledEventSaveData[CollectionLimit + 1];
                    for (var index = 0; index < scheduler.scheduledEvents.Length; index++)
                        scheduler.scheduledEvents[index] = FutureEvent(index, $"future-{index}");
                    return;
                case "duplicate-event-key":
                    scheduler.nextSequence = 2;
                    scheduler.scheduledEvents = new[]
                    {
                        FutureEvent(0, "duplicate"),
                        FutureEvent(1, "duplicate")
                    };
                    return;
                case "duplicate-sequence":
                    scheduler.nextSequence = 2;
                    scheduler.scheduledEvents = new[]
                    {
                        FutureEvent(0, "future-a"),
                        FutureEvent(0, "future-b")
                    };
                    return;
                case "negative-sequence":
                    scheduler.nextSequence = 1;
                    scheduler.scheduledEvents = new[] { FutureEvent(-1, "negative-sequence") };
                    return;
                case "sequence-at-next":
                    scheduler.nextSequence = 1;
                    scheduler.scheduledEvents = new[] { FutureEvent(1, "sequence-at-next") };
                    return;
                case "duplicate-hero-attack":
                    scheduler.nextSequence = 2;
                    scheduler.scheduledEvents = new[]
                    {
                        ActorAttack(0, "hero-attack-a", CombatActorSide.Hero),
                        ActorAttack(1, "hero-attack-b", CombatActorSide.Hero)
                    };
                    return;
                case "duplicate-consumable-check":
                    scheduler.nextSequence = 2;
                    scheduler.scheduledEvents = new[]
                    {
                        ConsumableCheck(0, "consumable-check-a"),
                        ConsumableCheck(1, "consumable-check-b")
                    };
                    return;
                case "system-actor-attack":
                    scheduler.nextSequence = 1;
                    scheduler.scheduledEvents = new[]
                    {
                        ActorAttack(0, "system-attack", CombatActorSide.System)
                    };
                    return;
                case "wrong-attack-phase":
                    scheduler.nextSequence = 1;
                    scheduler.scheduledEvents = new[]
                    {
                        ActorAttack(0, "wrong-phase", CombatActorSide.Hero, 99)
                    };
                    return;
                case "empty-event-key":
                    scheduler.nextSequence = 1;
                    scheduler.scheduledEvents = new[] { FutureEvent(0, " ") };
                    return;
                case "empty-event-type":
                    scheduler.nextSequence = 1;
                    scheduler.scheduledEvents = new[] { FutureEvent(0, "empty-type", " ") };
                    return;
                case "negative-timestamp":
                    scheduler.nextSequence = 1;
                    scheduler.scheduledEvents = new[] { FutureEvent(0, "negative-time", timestampSeconds: -1d) };
                    return;
                case "nan-timestamp":
                    scheduler.nextSequence = 1;
                    scheduler.scheduledEvents = new[] { FutureEvent(0, "nan-time", timestampSeconds: double.NaN) };
                    return;
                case "infinite-timestamp":
                    scheduler.nextSequence = 1;
                    scheduler.scheduledEvents = new[] { FutureEvent(0, "infinite-time", timestampSeconds: double.PositiveInfinity) };
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
            }
        }

        private static CombatScheduledEventSaveData FutureEvent(
            long sequence,
            string eventKey,
            string eventType = "future_event",
            double timestampSeconds = 2d)
        {
            return new CombatScheduledEventSaveData
            {
                eventKey = eventKey,
                eventType = eventType,
                timestampSeconds = timestampSeconds,
                phasePriority = 0,
                actorSide = CombatActorSide.System,
                sequence = sequence
            };
        }

        private static CombatScheduledEventSaveData ActorAttack(
            long sequence,
            string eventKey,
            CombatActorSide side,
            int phasePriority = (int)CombatScheduledEventPhase.ActorAttack)
        {
            return new CombatScheduledEventSaveData
            {
                eventKey = eventKey,
                eventType = CombatRuntimeService.ActorAttackEventType,
                timestampSeconds = 2d,
                phasePriority = phasePriority,
                actorSide = side,
                sequence = sequence
            };
        }

        private static CombatScheduledEventSaveData ConsumableCheck(
            long sequence,
            string eventKey)
        {
            return new CombatScheduledEventSaveData
            {
                eventKey = eventKey,
                eventType = CombatRuntimeService.ConsumableCheckEventType,
                timestampSeconds = 2d,
                phasePriority = (int)CombatScheduledEventPhase.ConsumableCheck,
                actorSide = CombatActorSide.System,
                sequence = sequence
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
            {
                var stackIds = new string[StatusStackLimit];
                for (var stackIndex = 0; stackIndex < stackIds.Length; stackIndex++)
                    stackIds[stackIndex] = $"status-stack-{index:D2}-{stackIndex:D2}";
                result[index] = new CombatStatusInstanceSaveData
                {
                    statusInstanceId = $"status-instance-{index:D2}",
                    statusId = $"status-{index:D2}",
                    sourceCombatantId = sourceCombatantId,
                    stackIds = stackIds,
                    expiresAtSeconds = index + 10d,
                    nextTickAtSeconds = index + 1d,
                    lastApplyEventKey = $"status-apply-event-{index:D2}",
                    lastTickEventKey = $"status-tick-event-{index:D2}"
                };
            }
            return result;
        }

        [Test]
        public void NegativeCombatantHpIsRejectedAtomically()
        {
            var state = _factory.CreateDefault();
            Assert.That(
                state.AddCombatAggregate(
                    Aggregate("combat-a", "session-a", "ren", "group-a", "enemy-a")),
                Is.True);
            var before = JsonUtility.ToJson(state.ToSaveData());
            var update = state.GetCombatAggregate("combat-a");
            update.session.hero.currentHp = -1;

            using (new SuppressedLogHandler())
                Assert.That(state.UpdateCombatAggregate(update), Is.False);

            Assert.That(JsonUtility.ToJson(state.ToSaveData()), Is.EqualTo(before));
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
                    expiresAtSeconds = index + 10d,
                    appliedEventKey = $"modifier-apply-event-{index:D2}"
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
