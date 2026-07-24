using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GuildIdle.Combat;
using GuildIdle.Configs;
using NUnit.Framework;
using UnityEngine;

namespace GuildIdle.EditorTests.Player
{
    public sealed class CombatRuntimeServiceTests
    {
        [Test]
        public void ComparatorUsesTimestampPhaseSideAndSequence()
        {
            var values = new[]
            {
                Scheduled(2d, 0, CombatActorSide.Hero, 0),
                Scheduled(1d, 1, CombatActorSide.Hero, 1),
                Scheduled(1d, 0, CombatActorSide.Enemy, 2),
                Scheduled(1d, 0, CombatActorSide.Hero, 4),
                Scheduled(1d, 0, CombatActorSide.Hero, 3)
            };

            Array.Sort(values, CombatScheduledEventComparer.Instance);

            Assert.That(values.Select(value => value.sequence), Is.EqualTo(new long[] { 3, 4, 2, 1, 0 }));
        }

        [Test]
        public void HeroUsesDerivedIntervalAndEnemyRateIsConvertedToInterval()
        {
            var store = new MemoryStore(Aggregate(100));
            var descriptors = new DescriptorProvider(
                Descriptor(CombatActorSide.Hero, CombatAttackCadence.HeroInterval(2d)),
                Descriptor(CombatActorSide.Enemy, CombatAttackCadence.EnemyRate(2d)));
            var service = new CombatRuntimeService(store, descriptors, new CombatRngFactory());

            var beforeCooldown = service.AdvanceTo("execution", 0.49d);
            var enemyAttack = service.AdvanceTo("execution", 0.5d);
            var beforeHeroCooldown = service.AdvanceTo("execution", 1.99d);
            var heroAttack = service.AdvanceTo("execution", 2d);

            Assert.That(beforeCooldown.Events, Is.Empty);
            Assert.That(enemyAttack.Events.OfType<CombatAttackEvent>().Single().ActorSide, Is.EqualTo(CombatActorSide.Enemy));
            Assert.That(beforeHeroCooldown.Events.OfType<CombatAttackEvent>().Select(value => value.ActorSide),
                Is.EqualTo(new[] { CombatActorSide.Enemy, CombatActorSide.Enemy }));
            Assert.That(heroAttack.Events.OfType<CombatAttackEvent>().Select(value => value.ActorSide),
                Is.EqualTo(new[] { CombatActorSide.Hero, CombatActorSide.Enemy }));
        }

        [TestCase(0d)]
        [TestCase(-1d)]
        public void NonPositiveEnemyRateReturnsTypedErrorWithoutMutation(double attacksPerSecond)
        {
            var source = Aggregate(100);
            var store = new MemoryStore(source);
            var descriptors = new DescriptorProvider(
                Descriptor(CombatActorSide.Hero, CombatAttackCadence.HeroInterval(1d)),
                Descriptor(CombatActorSide.Enemy, CombatAttackCadence.EnemyRate(attacksPerSecond)));
            var service = new CombatRuntimeService(store, descriptors, new ScriptedRngFactory());
            var before = store.Json;

            var result = service.AdvanceTo("execution", 1d);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(CombatAdvanceErrorCode.InvalidAttackCadence));
            Assert.That(store.Json, Is.EqualTo(before));
            Assert.That(store.UpdateCount, Is.Zero);
        }

        [Test]
        public void DodgeIsFirstAndDoesNotConsumeDamageOrCritRolls()
        {
            var source = Aggregate(100);
            source.session.rng = ScriptedRngFactory.State(0, 7, 9);
            var store = new MemoryStore(source);
            var descriptors = new DescriptorProvider(
                Descriptor(CombatActorSide.Hero, CombatAttackCadence.HeroInterval(1d)),
                Descriptor(
                    CombatActorSide.Enemy,
                    CombatAttackCadence.EnemyRate(0.1d),
                    dodgeChancePercent: 100d));
            var service = new CombatRuntimeService(store, descriptors, new ScriptedRngFactory());

            var result = service.AdvanceTo("execution", 1d);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Events.OfType<CombatDodgeEvent>().Single().Damage, Is.Zero);
            Assert.That(result.Events.OfType<CombatDamageEvent>(), Is.Empty);
            Assert.That(store.Value.session.currentEnemy.currentHp, Is.EqualTo(100));
            Assert.That(store.Value.session.rng.drawCount, Is.EqualTo(1));
            Assert.That(store.Value.session.rng.state, Is.EqualTo("7,9"));
        }

        [Test]
        public void HitRollOrderIsDodgeThenInclusiveDamageThenCrit()
        {
            var source = Aggregate(100);
            source.session.rng = ScriptedRngFactory.State(0, 1, ulong.MaxValue);
            var store = new MemoryStore(source);
            var descriptors = new DescriptorProvider(
                Descriptor(
                    CombatActorSide.Hero,
                    CombatAttackCadence.HeroInterval(1d),
                    damageMin: 5,
                    damageMax: 6),
                Descriptor(CombatActorSide.Enemy, CombatAttackCadence.EnemyRate(0.1d)));
            var service = new CombatRuntimeService(store, descriptors, new ScriptedRngFactory());

            var result = service.AdvanceTo("execution", 1d);
            var damage = result.Events.OfType<CombatDamageEvent>().Single();

            Assert.That(damage.DodgeRollPercent, Is.EqualTo(0d));
            Assert.That(damage.BaseDamage, Is.EqualTo(6));
            Assert.That(damage.CritRollPercent, Is.GreaterThan(99d));
            Assert.That(store.Value.session.rng.drawCount, Is.EqualTo(3));
        }

        [Test]
        public void BothInclusiveDamageBoundariesAreReachable()
        {
            var source = Aggregate(100);
            source.session.rng = ScriptedRngFactory.State(
                0, 0, ulong.MaxValue,
                0, 1, ulong.MaxValue);
            var store = new MemoryStore(source);
            var descriptors = new DescriptorProvider(
                Descriptor(
                    CombatActorSide.Hero,
                    CombatAttackCadence.HeroInterval(1d),
                    damageMin: 5,
                    damageMax: 6),
                Descriptor(CombatActorSide.Enemy, CombatAttackCadence.EnemyRate(0.1d)));
            var service = new CombatRuntimeService(store, descriptors, new ScriptedRngFactory());

            var result = service.AdvanceTo("execution", 2d);

            Assert.That(
                result.Events.OfType<CombatDamageEvent>().Select(value => value.BaseDamage),
                Is.EqualTo(new[] { 5, 6 }));
        }

        [Test]
        public void CritThenResistanceThenCeilUsesCanonicalOrder()
        {
            var source = Aggregate(100);
            source.session.rng = ScriptedRngFactory.State(0, 0, 0);
            var store = new MemoryStore(source);
            var descriptors = new DescriptorProvider(
                Descriptor(
                    CombatActorSide.Hero,
                    CombatAttackCadence.HeroInterval(1d),
                    damageMin: 5,
                    damageMax: 5,
                    critChancePercent: 100d,
                    critDamageMultiplier: 1.5d),
                Descriptor(
                    CombatActorSide.Enemy,
                    CombatAttackCadence.EnemyRate(0.1d),
                    physicalResistancePercent: 50d));
            var service = new CombatRuntimeService(store, descriptors, new ScriptedRngFactory());

            var result = service.AdvanceTo("execution", 1d);
            var damage = result.Events.OfType<CombatDamageEvent>().Single();

            Assert.That(damage.Critical, Is.True);
            Assert.That(damage.ResistancePercent, Is.EqualTo(50d));
            Assert.That(damage.Damage, Is.EqualTo(4));
        }

        [Test]
        public void SuccessfulHitDealsAtLeastOneAfterResistance()
        {
            var source = Aggregate(100);
            source.session.rng = ScriptedRngFactory.State(0, 0, ulong.MaxValue);
            var store = new MemoryStore(source);
            var descriptors = new DescriptorProvider(
                Descriptor(CombatActorSide.Hero, CombatAttackCadence.HeroInterval(1d), damageMin: 0, damageMax: 0),
                Descriptor(
                    CombatActorSide.Enemy,
                    CombatAttackCadence.EnemyRate(0.1d),
                    physicalResistancePercent: 100d));
            var service = new CombatRuntimeService(store, descriptors, new ScriptedRngFactory());

            var result = service.AdvanceTo("execution", 1d);

            Assert.That(result.Events.OfType<CombatDamageEvent>().Single().Damage, Is.EqualTo(1));
            Assert.That(store.Value.session.currentEnemy.currentHp, Is.EqualTo(99));
        }

        [Test]
        public void HeroWinsTimestampTieAndKilledEnemyDoesNotAttack()
        {
            var source = Aggregate(10);
            source.session.rng = ScriptedRngFactory.State(0, 0, ulong.MaxValue);
            var store = new MemoryStore(source);
            var descriptors = new DescriptorProvider(
                Descriptor(CombatActorSide.Hero, CombatAttackCadence.HeroInterval(1d), damageMin: 10, damageMax: 10),
                Descriptor(CombatActorSide.Enemy, CombatAttackCadence.EnemyRate(1d), damageMin: 100, damageMax: 100));
            var service = new CombatRuntimeService(store, descriptors, new ScriptedRngFactory());

            var result = service.AdvanceTo("execution", 1d);

            Assert.That(result.Events.OfType<CombatAttackEvent>().Select(value => value.ActorSide),
                Is.EqualTo(new[] { CombatActorSide.Hero }));
            Assert.That(store.Value.session.currentEnemy.currentHp, Is.Zero);
            Assert.That(store.Value.session.hero.currentHp, Is.EqualTo(100));
            Assert.That(store.Value.session.rng.drawCount, Is.EqualTo(3));
        }

        [Test]
        public void LargeDeltaProcessesEveryDueAttackInStableOrder()
        {
            var source = Aggregate(1000);
            source.session.hero.maxHp = source.session.hero.currentHp = 1000;
            source.session.rng = ScriptedRngFactory.State(Enumerable.Repeat(ulong.MaxValue, 30).ToArray());
            var store = new MemoryStore(source);
            var descriptors = new DescriptorProvider(
                Descriptor(CombatActorSide.Hero, CombatAttackCadence.HeroInterval(1d), damageMin: 1, damageMax: 1),
                Descriptor(CombatActorSide.Enemy, CombatAttackCadence.EnemyRate(0.5d), damageMin: 1, damageMax: 1));
            var service = new CombatRuntimeService(store, descriptors, new ScriptedRngFactory());

            var result = service.AdvanceTo("execution", 5d);
            var attacks = result.Events.OfType<CombatAttackEvent>().ToArray();

            Assert.That(attacks.Select(value => value.TimestampSeconds),
                Is.EqualTo(new[] { 1d, 2d, 2d, 3d, 4d, 4d, 5d }));
            Assert.That(attacks.Select(value => value.ActorSide),
                Is.EqualTo(new[]
                {
                    CombatActorSide.Hero,
                    CombatActorSide.Hero,
                    CombatActorSide.Enemy,
                    CombatActorSide.Hero,
                    CombatActorSide.Hero,
                    CombatActorSide.Enemy,
                    CombatActorSide.Hero
                }));
        }

        [Test]
        public void RepeatingAdvanceToSameTimeDoesNotReplayOrConsumeRng()
        {
            var store = new MemoryStore(Aggregate(100));
            var service = new CombatRuntimeService(store, DefaultDescriptors(), new CombatRngFactory());

            var first = service.AdvanceTo("execution", 1d);
            var drawCount = store.Value.session.rng.drawCount;
            var updateCount = store.UpdateCount;
            var second = service.AdvanceTo("execution", 1d);

            Assert.That(first.Events, Is.Not.Empty);
            Assert.That(second.Success, Is.True);
            Assert.That(second.Events, Is.Empty);
            Assert.That(store.Value.session.rng.drawCount, Is.EqualTo(drawCount));
            Assert.That(store.UpdateCount, Is.EqualTo(updateCount));
        }

        [Test]
        public void SameTimeNoOpWithUnsupportedRngReturnsTypedErrorWithoutUpdate()
        {
            var source = InitializedSameTimeAggregate();
            source.session.rng.algorithmId = "unsupported";

            AssertSameTimeRngError(source, CombatAdvanceErrorCode.UnsupportedRngDescriptor);
        }

        [Test]
        public void SameTimeNoOpWithMalformedRngReturnsTypedErrorWithoutUpdate()
        {
            var source = InitializedSameTimeAggregate();
            source.session.rng.state = "not-hex";

            AssertSameTimeRngError(source, CombatAdvanceErrorCode.InvalidRngState);
        }

        [Test]
        public void RestoredResolvedEventKeyIsNotExecutedAgain()
        {
            var source = Aggregate(100);
            var eventKey = "session:attack:hero:0";
            source.session.scheduler.nextSequence = 1;
            source.session.scheduler.lastResolvedEventKey = eventKey;
            source.session.scheduler.scheduledEvents = new[]
            {
                new CombatScheduledEventSaveData
                {
                    eventKey = eventKey,
                    eventType = CombatRuntimeService.ActorAttackEventType,
                    timestampSeconds = 1d,
                    phasePriority = (int)CombatScheduledEventPhase.ActorAttack,
                    actorSide = CombatActorSide.Hero,
                    sequence = 0
                }
            };
            source.session.currentEnemy.nextAttackAtSeconds = 10d;
            var store = new MemoryStore(source);
            var service = new CombatRuntimeService(store, DefaultDescriptors(), new CombatRngFactory());

            var result = service.AdvanceTo("execution", 1d);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Events, Is.Empty);
            Assert.That(store.Value.session.rng.drawCount, Is.Zero);
        }

        [Test]
        public void SaveLoadContinuesSameRngAndSchedulerPosition()
        {
            var initial = Aggregate(1000);
            initial.session.hero.maxHp = initial.session.hero.currentHp = 1000;
            var splitStore = new MemoryStore(initial);
            var splitService = new CombatRuntimeService(splitStore, DefaultDescriptors(), new CombatRngFactory());
            Assert.That(splitService.AdvanceTo("execution", 2d).Success, Is.True);

            var reloadedStore = new MemoryStore(splitStore.Value);
            var reloadedService = new CombatRuntimeService(reloadedStore, DefaultDescriptors(), new CombatRngFactory());
            Assert.That(reloadedService.AdvanceTo("execution", 4d).Success, Is.True);

            var continuousStore = new MemoryStore(initial);
            var continuousService = new CombatRuntimeService(continuousStore, DefaultDescriptors(), new CombatRngFactory());
            Assert.That(continuousService.AdvanceTo("execution", 4d).Success, Is.True);

            Assert.That(
                JsonUtility.ToJson(reloadedStore.Value.session),
                Is.EqualTo(JsonUtility.ToJson(continuousStore.Value.session)));
        }

        [Test]
        public void CombatClearHallForestQueueBuildsFourWolvesThenLeaderFromConfigOrder()
        {
            var configs = EnemyConfigs(
                new[]
                {
                    Enemy("enemy_lean_wolf", 35),
                    Enemy("enemy_wolf_leader", 80)
                },
                new[]
                {
                    Group("enemy_group_underwood_wolves", "enemy_wolf_leader:1", 20, 1, 1),
                    Group("enemy_group_underwood_wolves", "enemy_lean_wolf:1", 10, 4, 4)
                });
            var builder = new CombatEnemyQueueBuilder(
                new ConfigCombatEnemyQueueProvider(configs),
                new ScriptedRngFactory());
            var source = new CombatSessionSaveData
            {
                sessionId = "forest-session",
                executionId = "forest-execution",
                enemyGroupId = "enemy_group_underwood_wolves",
                combatMode = CombatEnemyQueueBuilder.Queue1V1Mode,
                hero = new CombatantStateSaveData
                {
                    combatantId = "hero-combatant",
                    definitionId = "hero",
                    currentHp = 100,
                    maxHp = 100
                },
                rng = ScriptedRngFactory.State()
            };

            var success = builder.TryBuild(source, out var session, out var error);

            Assert.That(success, Is.True, error?.Message);
            Assert.That(
                session.enemyQueue.Select(value => value.enemyId),
                Is.EqualTo(new[]
                {
                    "enemy_lean_wolf",
                    "enemy_lean_wolf",
                    "enemy_lean_wolf",
                    "enemy_lean_wolf",
                    "enemy_wolf_leader"
                }));
            Assert.That(session.enemyQueue.Select(value => value.queueIndex), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            Assert.That(session.currentEnemy.definitionId, Is.EqualTo("enemy_lean_wolf"));
            Assert.That(session.currentEnemy.currentHp, Is.EqualTo(35));
            Assert.That(session.rng.drawCount, Is.Zero);
        }

        [Test]
        public void AdditionalGroupUsesWeightedAlternativeAndCountWithoutIdSpecificBranches()
        {
            var configs = EnemyConfigs(
                new[]
                {
                    Enemy("enemy-a", 10),
                    Enemy("enemy-b", 20),
                    Enemy("enemy-c", 30)
                },
                new[]
                {
                    Group("additional-group", "enemy-a:1", 10, 1, 3, 1),
                    Group("additional-group", "enemy-b:1", 10, 1, 3, 1),
                    Group("additional-group", "enemy-c:1", 20, 1, 1)
                });
            var builder = new CombatEnemyQueueBuilder(
                new ConfigCombatEnemyQueueProvider(configs),
                new ScriptedRngFactory());
            var source = new CombatSessionSaveData
            {
                sessionId = "additional-session",
                enemyGroupId = "additional-group",
                combatMode = CombatEnemyQueueBuilder.Queue1V1Mode,
                rng = ScriptedRngFactory.State(1, 3)
            };

            var success = builder.TryBuild(source, out var session, out var error);

            Assert.That(success, Is.True, error?.Message);
            Assert.That(
                session.enemyQueue.Select(value => value.enemyId),
                Is.EqualTo(new[] { "enemy-b", "enemy-c" }));
            Assert.That(session.rng.drawCount, Is.EqualTo(2));
        }

        [Test]
        public void EnemyDeathStartsNextAtFullCooldownWithoutResettingHeroState()
        {
            var source = QueueAggregate("enemy-a", 1, "enemy-b");
            source.session.hero.nextAttackAtSeconds = 0d;
            source.session.hero.abilityCooldowns = new[]
            {
                new CombatAbilityCooldownSaveData { abilityId = "ability", nextReadyAtSeconds = 9d }
            };
            source.session.hero.statuses = new[]
            {
                new CombatStatusInstanceSaveData
                {
                    statusInstanceId = "status-instance",
                    statusId = "status",
                    sourceCombatantId = "hero-combatant",
                    stacks = 2,
                    expiresAtSeconds = 8d,
                    nextTickAtSeconds = 3d
                }
            };
            source.session.hero.independentModifiers = new[]
            {
                new CombatTemporaryModifierSaveData
                {
                    modifierInstanceId = "modifier",
                    sourceId = "source",
                    statId = "damage",
                    operation = "add",
                    value = 2f,
                    expiresAtSeconds = 7d
                }
            };
            source.session.rng = ScriptedRngFactory.State(0, 0, ulong.MaxValue);
            var store = new MemoryStore(source);
            var enemyQueue = new ConfigCombatEnemyQueueProvider(
                EnemyConfigs(
                    new[] { Enemy("enemy-a", 1), Enemy("enemy-b", 20) },
                    Array.Empty<EnemyGroupConfigDto>()));
            var descriptors = new DescriptorProvider(
                Descriptor(CombatActorSide.Hero, CombatAttackCadence.HeroInterval(1d), damageMin: 1, damageMax: 1),
                Descriptor(CombatActorSide.Enemy, CombatAttackCadence.EnemyRate(0.25d), damageMin: 1, damageMax: 1));
            var service = new CombatRuntimeService(store, descriptors, new ScriptedRngFactory(), enemyQueue);

            var result = service.AdvanceTo("execution", 1d);
            var session = store.Value.session;

            Assert.That(result.Success, Is.True, result.Error?.Message);
            Assert.That(session.queuePosition, Is.EqualTo(1));
            Assert.That(session.currentEnemy.definitionId, Is.EqualTo("enemy-b"));
            Assert.That(session.currentEnemy.currentHp, Is.EqualTo(20));
            Assert.That(session.currentEnemy.nextAttackAtSeconds, Is.EqualTo(5d));
            Assert.That(session.hero.currentHp, Is.EqualTo(100));
            Assert.That(session.hero.nextAttackAtSeconds, Is.EqualTo(2d));
            Assert.That(session.hero.abilityCooldowns.Single().nextReadyAtSeconds, Is.EqualTo(9d));
            Assert.That(session.hero.statuses.Single().stacks, Is.EqualTo(2));
            Assert.That(session.hero.independentModifiers.Single().value, Is.EqualTo(2f));
            Assert.That(session.combatTimeSeconds, Is.EqualTo(1d));
            Assert.That(session.rng.drawCount, Is.EqualTo(3));
        }

        [Test]
        public void MismatchedCurrentEnemyAndQueuePositionReturnsInvalidEnemyQueueWithoutUpdate()
        {
            var source = QueueAggregate("enemy-a", 1, "enemy-b");
            source.session.currentEnemy.definitionId = "enemy-b";
            source.session.rng = ScriptedRngFactory.State(0, 0, ulong.MaxValue);
            var store = new MemoryStore(source);
            var before = store.Json;
            var descriptors = new DescriptorProvider(
                Descriptor(CombatActorSide.Hero, CombatAttackCadence.HeroInterval(1d), damageMin: 1, damageMax: 1),
                Descriptor(CombatActorSide.Enemy, CombatAttackCadence.EnemyRate(1d), damageMin: 1, damageMax: 1));
            var service = new CombatRuntimeService(store, descriptors, new ScriptedRngFactory());

            var result = service.AdvanceTo("execution", 1d);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(CombatAdvanceErrorCode.InvalidEnemyQueue));
            Assert.That(result.Events, Is.Empty);
            Assert.That(store.UpdateCount, Is.Zero);
            Assert.That(store.Json, Is.EqualTo(before));
        }

        [Test]
        public void MismatchedNextQueueIndexReturnsInvalidEnemyQueueWithoutUpdate()
        {
            var source = QueueAggregate("enemy-a", 1, "enemy-b");
            source.session.enemyQueue[1].queueIndex = 7;
            source.session.rng = ScriptedRngFactory.State(0, 0, ulong.MaxValue);
            var store = new MemoryStore(source);
            var before = store.Json;
            var descriptors = new DescriptorProvider(
                Descriptor(CombatActorSide.Hero, CombatAttackCadence.HeroInterval(1d), damageMin: 1, damageMax: 1),
                Descriptor(CombatActorSide.Enemy, CombatAttackCadence.EnemyRate(1d), damageMin: 1, damageMax: 1));
            var service = new CombatRuntimeService(store, descriptors, new ScriptedRngFactory());

            var result = service.AdvanceTo("execution", 1d);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(CombatAdvanceErrorCode.InvalidEnemyQueue));
            Assert.That(result.Events, Is.Empty);
            Assert.That(store.UpdateCount, Is.Zero);
            Assert.That(store.Json, Is.EqualTo(before));
        }

        [Test]
        public void LargeAdvanceContinuesCombatClockAndRngAcrossQueueUntilCompletion()
        {
            var source = QueueAggregate("enemy-a", 1, "enemy-b");
            source.session.rng = ScriptedRngFactory.State(
                0, 0, ulong.MaxValue,
                0, 0, ulong.MaxValue);
            var store = new MemoryStore(source);
            var enemyQueue = new ConfigCombatEnemyQueueProvider(
                EnemyConfigs(
                    new[] { Enemy("enemy-a", 1), Enemy("enemy-b", 1) },
                    Array.Empty<EnemyGroupConfigDto>()));
            var descriptors = new DescriptorProvider(
                Descriptor(CombatActorSide.Hero, CombatAttackCadence.HeroInterval(1d), damageMin: 1, damageMax: 1),
                Descriptor(CombatActorSide.Enemy, CombatAttackCadence.EnemyRate(0.1d), damageMin: 1, damageMax: 1));
            var service = new CombatRuntimeService(store, descriptors, new ScriptedRngFactory(), enemyQueue);

            var result = service.AdvanceTo("execution", 10d);

            Assert.That(result.Success, Is.True, result.Error?.Message);
            Assert.That(result.Events.OfType<CombatAttackEvent>().Select(value => value.TimestampSeconds),
                Is.EqualTo(new[] { 1d, 2d }));
            Assert.That(result.CombatTimeSeconds, Is.EqualTo(2d));
            Assert.That(store.Value.session.combatTimeSeconds, Is.EqualTo(2d));
            Assert.That(store.Value.session.rng.drawCount, Is.EqualTo(6));
            Assert.That(store.Value.session.queuePosition, Is.EqualTo(2));
            Assert.That(store.Value.session.currentEnemy, Is.Null);
            Assert.That(store.Value.session.simulationStopped, Is.True);
        }

        [Test]
        public void SaveLoadBetweenEnemiesContinuesTheSameQueue()
        {
            var initial = QueueAggregate("enemy-a", 1, "enemy-b");
            initial.session.rng = ScriptedRngFactory.State(
                0, 0, ulong.MaxValue,
                0, 0, ulong.MaxValue);
            var enemyQueue = new ConfigCombatEnemyQueueProvider(
                EnemyConfigs(
                    new[] { Enemy("enemy-a", 1), Enemy("enemy-b", 1) },
                    Array.Empty<EnemyGroupConfigDto>()));
            var descriptors = new DescriptorProvider(
                Descriptor(CombatActorSide.Hero, CombatAttackCadence.HeroInterval(1d), damageMin: 1, damageMax: 1),
                Descriptor(CombatActorSide.Enemy, CombatAttackCadence.EnemyRate(0.1d), damageMin: 1, damageMax: 1));

            var splitStore = new MemoryStore(initial);
            var splitService = new CombatRuntimeService(
                splitStore,
                descriptors,
                new ScriptedRngFactory(),
                enemyQueue);
            Assert.That(splitService.AdvanceTo("execution", 1d).Success, Is.True);
            Assert.That(splitStore.Value.session.queuePosition, Is.EqualTo(1));

            var reloadedStore = new MemoryStore(splitStore.Value);
            var reloadedService = new CombatRuntimeService(
                reloadedStore,
                descriptors,
                new ScriptedRngFactory(),
                enemyQueue);
            Assert.That(reloadedService.AdvanceTo("execution", 2d).Success, Is.True);

            var continuousStore = new MemoryStore(initial);
            var continuousService = new CombatRuntimeService(
                continuousStore,
                descriptors,
                new ScriptedRngFactory(),
                enemyQueue);
            Assert.That(continuousService.AdvanceTo("execution", 2d).Success, Is.True);

            Assert.That(
                JsonUtility.ToJson(reloadedStore.Value.session),
                Is.EqualTo(JsonUtility.ToJson(continuousStore.Value.session)));
        }

        [TestCase("unknown", CombatRngStateFactory.SplitMix64FormatVersion)]
        [TestCase(CombatRngStateFactory.SplitMix64AlgorithmId, 99)]
        public void UnsupportedRngDescriptorReturnsTypedErrorWithoutUpdate(string algorithmId, int formatVersion)
        {
            var source = Aggregate(100);
            source.session.rng.algorithmId = algorithmId;
            source.session.rng.formatVersion = formatVersion;
            var store = new MemoryStore(source);
            var before = store.Json;
            var service = new CombatRuntimeService(store, DefaultDescriptors(), new CombatRngFactory());

            var result = service.AdvanceTo("execution", 1d);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(CombatAdvanceErrorCode.UnsupportedRngDescriptor));
            Assert.That(result.Events, Is.Empty);
            Assert.That(store.UpdateCount, Is.Zero);
            Assert.That(store.Json, Is.EqualTo(before));
        }

        [TestCase("not-hex", 0L, CombatAdvanceErrorCode.InvalidRngState)]
        [TestCase("", 0L, CombatAdvanceErrorCode.InvalidRngState)]
        [TestCase("0000000000000000", -1L, CombatAdvanceErrorCode.InvalidRngState)]
        [TestCase("0000000000000000", long.MaxValue, CombatAdvanceErrorCode.ProcessingFailed)]
        public void InvalidOrExhaustedRngStateReturnsTypedErrorWithoutMutation(
            string rngState,
            long drawCount,
            CombatAdvanceErrorCode expectedError)
        {
            var source = Aggregate(100);
            source.session.rng.state = rngState;
            source.session.rng.drawCount = drawCount;
            var store = new MemoryStore(source);
            var before = store.Json;
            var service = new CombatRuntimeService(store, DefaultDescriptors(), new CombatRngFactory());

            var result = service.AdvanceTo("execution", 1d);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(expectedError));
            Assert.That(result.Events, Is.Empty);
            Assert.That(store.UpdateCount, Is.Zero);
            Assert.That(store.Json, Is.EqualTo(before));
        }

        [Test]
        public void DescriptorFailureReturnsTypedErrorWithoutUpdate()
        {
            var store = new MemoryStore(Aggregate(100));
            var before = store.Json;
            var service = new CombatRuntimeService(
                store,
                new DescriptorProvider(null, null),
                new CombatRngFactory());

            var result = service.AdvanceTo("execution", 1d);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(CombatAdvanceErrorCode.DescriptorNotFound));
            Assert.That(result.Events, Is.Empty);
            Assert.That(store.UpdateCount, Is.Zero);
            Assert.That(store.Json, Is.EqualTo(before));
        }

        [Test]
        public void FailedAtomicUpdatePublishesNoEventsAndLeavesStateUntouched()
        {
            var store = new MemoryStore(Aggregate(100)) { RejectUpdates = true };
            var before = store.Json;
            var service = new CombatRuntimeService(store, DefaultDescriptors(), new CombatRngFactory());

            var result = service.AdvanceTo("execution", 1d);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(CombatAdvanceErrorCode.StoreUpdateFailed));
            Assert.That(result.Events, Is.Empty);
            Assert.That(store.UpdateCount, Is.EqualTo(1));
            Assert.That(store.Json, Is.EqualTo(before));
        }

        [Test]
        public void MidAdvanceRngFailureRollsBackDetachedSimulationWithoutPublishingEvents()
        {
            var source = Aggregate(100);
            var store = new MemoryStore(source);
            var before = store.Json;
            var beforeSession = JsonUtility.FromJson<CombatSessionSaveData>(
                JsonUtility.ToJson(store.Value.session));
            var rngFactory = new ThrowOnDrawRngFactory(4);
            var service = new CombatRuntimeService(store, DefaultDescriptors(), rngFactory);

            var result = service.AdvanceTo("execution", 1d);
            var persisted = store.Value.session;

            Assert.That(rngFactory.SuccessfulDraws, Is.EqualTo(3));
            Assert.That(rngFactory.ThrowObserved, Is.True);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(CombatAdvanceErrorCode.ProcessingFailed));
            Assert.That(result.Events, Is.Empty);
            Assert.That(store.UpdateCount, Is.Zero);
            Assert.That(store.Json, Is.EqualTo(before));
            Assert.That(persisted.hero.currentHp, Is.EqualTo(beforeSession.hero.currentHp));
            Assert.That(persisted.currentEnemy.currentHp, Is.EqualTo(beforeSession.currentEnemy.currentHp));
            Assert.That(persisted.combatTimeSeconds, Is.EqualTo(beforeSession.combatTimeSeconds));
            Assert.That(persisted.hero.nextAttackAtSeconds, Is.EqualTo(beforeSession.hero.nextAttackAtSeconds));
            Assert.That(persisted.currentEnemy.nextAttackAtSeconds, Is.EqualTo(beforeSession.currentEnemy.nextAttackAtSeconds));
            Assert.That(persisted.scheduler.scheduledEvents, Is.Empty);
            Assert.That(persisted.scheduler.nextSequence, Is.EqualTo(beforeSession.scheduler.nextSequence));
            Assert.That(persisted.scheduler.lastResolvedEventKey, Is.EqualTo(beforeSession.scheduler.lastResolvedEventKey));
            Assert.That(persisted.rng.state, Is.EqualTo(beforeSession.rng.state));
            Assert.That(persisted.rng.drawCount, Is.EqualTo(beforeSession.rng.drawCount));
        }

        [Test]
        public void WolfLeaderFixturesCreateTypedRequestsWithoutApplyingStatusLifecycle()
        {
            var source = Aggregate(100);
            source.session.currentEnemy.definitionId = "enemy_wolf_leader";
            source.session.rng = ScriptedRngFactory.State(0, 0, 0, 0, 0);
            var store = new MemoryStore(source);
            var configs = new EnemiesConfigRepository(new EnemiesRuntimeConfigDto
            {
                enemies = new[]
                {
                    new EnemyConfigDto
                    {
                        enemyId = "enemy_wolf_leader",
                        combatAbilityIds = new[]
                        {
                            "enemy_ability_bleeding_claws",
                            "enemy_ability_intimidating_howl"
                        }
                    }
                },
                enemyAbilities = new[]
                {
                    new EnemyAbilityConfigDto
                    {
                        abilityId = "enemy_ability_bleeding_claws",
                        trigger = CombatAbilityTriggers.OnAttackHit,
                        chancePercent = 100f,
                        effects = "ApplyStatus: bleed_weak",
                        target = "enemy",
                        cooldownSec = 10
                    },
                    new EnemyAbilityConfigDto
                    {
                        abilityId = "enemy_ability_intimidating_howl",
                        trigger = CombatAbilityTriggers.OnBattleStart,
                        chancePercent = 100f,
                        effects = "ModifyStat: hero_damage_percent -3, duration 5 sec",
                        target = "enemy",
                        cooldownSec = 0
                    }
                },
                combatStatuses = new[]
                {
                    new CombatStatusConfigDto { statusId = "bleed_weak" }
                }
            });
            var descriptors = new DescriptorProvider(
                Descriptor(CombatActorSide.Hero, CombatAttackCadence.HeroInterval(10d)),
                Descriptor(CombatActorSide.Enemy, CombatAttackCadence.EnemyRate(1d)));
            var service = new CombatRuntimeService(
                store,
                descriptors,
                new ScriptedRngFactory(),
                null,
                new ConfigCombatAbilityDescriptorProvider(configs));

            var battleStart = service.AdvanceTo("execution", 0d);
            var attackHit = service.AdvanceTo("execution", 1d);

            var howl = battleStart.Events.OfType<CombatEffectRequest>().Single();
            Assert.That(howl.SourceAbilityId, Is.EqualTo("enemy_ability_intimidating_howl"));
            Assert.That(howl.Trigger, Is.EqualTo(CombatAbilityTriggers.OnBattleStart));
            Assert.That(howl.TargetCombatantId, Is.EqualTo("hero-combatant"));
            Assert.That(howl.Effect.Kind, Is.EqualTo(CombatEffectKind.ModifyStat));
            Assert.That(howl.Effect.StatId, Is.EqualTo("hero_damage_percent"));
            Assert.That(howl.Effect.Value, Is.EqualTo(-3d));
            Assert.That(howl.Effect.DurationSeconds, Is.EqualTo(5d));

            var claws = attackHit.Events.OfType<CombatEffectRequest>().Single();
            Assert.That(claws.SourceAbilityId, Is.EqualTo("enemy_ability_bleeding_claws"));
            Assert.That(claws.Trigger, Is.EqualTo(CombatAbilityTriggers.OnAttackHit));
            Assert.That(claws.TargetCombatantId, Is.EqualTo("hero-combatant"));
            Assert.That(claws.Effect.Kind, Is.EqualTo(CombatEffectKind.ApplyStatus));
            Assert.That(claws.Effect.StatusId, Is.EqualTo("bleed_weak"));
            Assert.That(store.Value.session.hero.statuses, Is.Empty);
            Assert.That(store.Value.session.hero.independentModifiers, Is.Empty);
        }

        [Test]
        public void AttackHitAbilityDoesNotDispatchOnDodge()
        {
            var source = Aggregate(100);
            source.session.rng = ScriptedRngFactory.State(0);
            var store = new MemoryStore(source);
            var abilities = new AbilityDescriptorProvider()
                .Add(
                    CombatActorSide.Enemy,
                    "enemy",
                    Ability(
                        "ability-on-hit",
                        CombatAbilityTriggers.OnAttackHit,
                        "enemy",
                        CombatEffectKind.ApplyStatus));
            var descriptors = new DescriptorProvider(
                Descriptor(
                    CombatActorSide.Hero,
                    CombatAttackCadence.HeroInterval(10d),
                    dodgeChancePercent: 100d),
                Descriptor(CombatActorSide.Enemy, CombatAttackCadence.EnemyRate(1d)));
            var service = new CombatRuntimeService(
                store,
                descriptors,
                new ScriptedRngFactory(),
                null,
                abilities);

            var result = service.AdvanceTo("execution", 1d);

            Assert.That(result.Events.OfType<CombatDodgeEvent>().Count(), Is.EqualTo(1));
            Assert.That(result.Events.OfType<CombatEffectRequest>(), Is.Empty);
            Assert.That(store.Value.session.currentEnemy.abilityCooldowns, Is.Empty);
        }

        [Test]
        public void EnemyBattleStartDispatchesWhenThatQueueCombatantBecomesActive()
        {
            var source = QueueAggregate("enemy-a", 1, "enemy-b");
            source.session.rng = ScriptedRngFactory.State(0, 0, 0, 0);
            var store = new MemoryStore(source);
            var queueConfigs = EnemyConfigs(
                new[] { Enemy("enemy-a", 1), Enemy("enemy-b", 10) },
                Array.Empty<EnemyGroupConfigDto>());
            var abilities = new AbilityDescriptorProvider()
                .Add(
                    CombatActorSide.Enemy,
                    "enemy-b",
                    Ability(
                        "ability-entry",
                        CombatAbilityTriggers.OnBattleStart,
                        "enemy",
                        CombatEffectKind.ApplyStatus));
            var service = new CombatRuntimeService(
                store,
                new DescriptorProvider(
                    Descriptor(CombatActorSide.Hero, CombatAttackCadence.HeroInterval(1d), damageMin: 1, damageMax: 1),
                    Descriptor(CombatActorSide.Enemy, CombatAttackCadence.EnemyRate(0.1d))),
                new ScriptedRngFactory(),
                new ConfigCombatEnemyQueueProvider(queueConfigs),
                abilities);

            var result = service.AdvanceTo("execution", 1d);
            var request = result.Events.OfType<CombatEffectRequest>().Single();

            Assert.That(request.SourceOwnerCombatantId, Is.EqualTo("enemy-combatant-1"));
            Assert.That(request.TimestampSeconds, Is.EqualTo(1d));
            Assert.That(request.TargetCombatantId, Is.EqualTo("hero-combatant"));
            Assert.That(store.Value.session.currentEnemy.definitionId, Is.EqualTo("enemy-b"));
            Assert.That(store.Value.session.currentEnemy.statuses, Is.Empty);
        }

        [Test]
        public void EnemyTargetUsesOpponentOfAbilityOwnerForEitherSideAndOrderingIsStable()
        {
            var source = Aggregate(100);
            source.session.rng = ScriptedRngFactory.State(0, 0, 0);
            var store = new MemoryStore(source);
            var abilities = new AbilityDescriptorProvider()
                .Add(
                    CombatActorSide.Hero,
                    "hero",
                    Ability("ability-z", CombatAbilityTriggers.OnBattleStart, "enemy", CombatEffectKind.ApplyStatus),
                    Ability("ability-a", CombatAbilityTriggers.OnBattleStart, "enemy", CombatEffectKind.ApplyStatus))
                .Add(
                    CombatActorSide.Enemy,
                    "enemy",
                    Ability("ability-m", CombatAbilityTriggers.OnBattleStart, "enemy", CombatEffectKind.ModifyStat));
            var service = new CombatRuntimeService(
                store,
                DefaultDescriptors(),
                new ScriptedRngFactory(),
                null,
                abilities);

            var result = service.AdvanceTo("execution", 0d);
            var requests = result.Events.OfType<CombatEffectRequest>().ToArray();

            Assert.That(
                requests.Select(value => value.SourceAbilityId),
                Is.EqualTo(new[] { "ability-a", "ability-z", "ability-m" }));
            Assert.That(requests.Select(value => value.Sequence), Is.EqualTo(new long[] { 0, 1, 2 }));
            Assert.That(
                requests.Select(value => value.TargetCombatantId),
                Is.EqualTo(new[] { "enemy-combatant", "enemy-combatant", "hero-combatant" }));
            Assert.That(requests.Select(value => value.EventKey).Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void ChanceAndCooldownStateSurviveReloadWithoutRepeatingDispatch()
        {
            var source = Aggregate(100);
            source.session.rng = ScriptedRngFactory.State(99, 0, 0, 0, 0);
            var abilities = new AbilityDescriptorProvider()
                .Add(
                    CombatActorSide.Enemy,
                    "enemy",
                    Ability(
                        "ability-chance",
                        CombatAbilityTriggers.OnBattleStart,
                        "enemy",
                        CombatEffectKind.ApplyStatus,
                        chancePercent: 1d),
                    Ability(
                        "ability-cooldown",
                        CombatAbilityTriggers.OnAttackHit,
                        "enemy",
                        CombatEffectKind.ApplyStatus,
                        cooldownSeconds: 10d));
            var descriptors = new DescriptorProvider(
                Descriptor(CombatActorSide.Hero, CombatAttackCadence.HeroInterval(10d)),
                Descriptor(CombatActorSide.Enemy, CombatAttackCadence.EnemyRate(1d)));
            var splitStore = new MemoryStore(source);
            var splitService = new CombatRuntimeService(
                splitStore,
                descriptors,
                new ScriptedRngFactory(),
                null,
                abilities);

            var first = splitService.AdvanceTo("execution", 1d);
            var drawCount = splitStore.Value.session.rng.drawCount;
            var chance = splitStore.Value.session.currentEnemy.abilityCooldowns
                .Single(value => value.abilityId == "ability-chance");
            var cooldown = splitStore.Value.session.currentEnemy.abilityCooldowns
                .Single(value => value.abilityId == "ability-cooldown");
            Assert.That(first.Events.OfType<CombatEffectRequest>().Select(value => value.SourceAbilityId),
                Is.EqualTo(new[] { "ability-cooldown" }));
            Assert.That(chance.lastChanceResolved, Is.True);
            Assert.That(chance.lastChanceRoll, Is.EqualTo(100));
            Assert.That(cooldown.nextReadyAtSeconds, Is.EqualTo(11d));

            var reloadedStore = new MemoryStore(splitStore.Value);
            var reloadedService = new CombatRuntimeService(
                reloadedStore,
                descriptors,
                new ScriptedRngFactory(),
                null,
                abilities);
            var replay = reloadedService.AdvanceTo("execution", 1d);
            var duringCooldown = reloadedService.AdvanceTo("execution", 2d);

            Assert.That(replay.Events, Is.Empty);
            Assert.That(replay.Success, Is.True);
            Assert.That(duringCooldown.Events.OfType<CombatEffectRequest>(), Is.Empty);
            Assert.That(reloadedStore.Value.session.rng.drawCount, Is.EqualTo(drawCount + 3));
            Assert.That(
                reloadedStore.Value.session.currentEnemy.abilityCooldowns
                    .Single(value => value.abilityId == "ability-cooldown")
                    .lastChanceRoll,
                Is.EqualTo(cooldown.lastChanceRoll));
        }

        [Test]
        public void AdditionalAbilityFixtureDispatchesWithoutProductionIdBranch()
        {
            var source = Aggregate(100);
            source.session.rng = ScriptedRngFactory.State(0);
            var store = new MemoryStore(source);
            var abilities = new AbilityDescriptorProvider()
                .Add(
                    CombatActorSide.Hero,
                    "hero",
                    Ability(
                        "fixture_unrelated_ability",
                        CombatAbilityTriggers.OnBattleStart,
                        "self",
                        CombatEffectKind.ModifyStat));
            var service = new CombatRuntimeService(
                store,
                DefaultDescriptors(),
                new ScriptedRngFactory(),
                null,
                abilities);

            var first = service.AdvanceTo("execution", 0d);
            var second = service.AdvanceTo("execution", 0d);
            var request = first.Events.OfType<CombatEffectRequest>().Single();

            Assert.That(request.SourceAbilityId, Is.EqualTo("fixture_unrelated_ability"));
            Assert.That(request.TargetCombatantId, Is.EqualTo("hero-combatant"));
            Assert.That(request.EventKey, Does.Contain("fixture_unrelated_ability"));
            Assert.That(second.Events, Is.Empty);
        }

        private static DescriptorProvider DefaultDescriptors()
        {
            return new DescriptorProvider(
                Descriptor(CombatActorSide.Hero, CombatAttackCadence.HeroInterval(1d)),
                Descriptor(CombatActorSide.Enemy, CombatAttackCadence.EnemyRate(1d)));
        }

        private static void AssertSameTimeRngError(
            CombatRuntimeAggregate source,
            CombatAdvanceErrorCode expectedError)
        {
            var store = new MemoryStore(source);
            var before = store.Json;
            var service = new CombatRuntimeService(store, DefaultDescriptors(), new CombatRngFactory());

            var result = service.AdvanceTo("execution", source.session.combatTimeSeconds);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(expectedError));
            Assert.That(result.Events, Is.Empty);
            Assert.That(store.UpdateCount, Is.Zero);
            Assert.That(store.Json, Is.EqualTo(before));
        }

        private static CombatActorDescriptor Descriptor(
            CombatActorSide side,
            CombatAttackCadence cadence,
            int damageMin = 2,
            int damageMax = 2,
            double critChancePercent = 0d,
            double critDamageMultiplier = 2d,
            double dodgeChancePercent = 0d,
            double physicalResistancePercent = 0d,
            double magicResistancePercent = 0d)
        {
            return new CombatActorDescriptor(
                side,
                cadence,
                damageMin,
                damageMax,
                "physical",
                critChancePercent,
                critDamageMultiplier,
                dodgeChancePercent,
                physicalResistancePercent,
                magicResistancePercent);
        }

        private static CombatAbilityDescriptor Ability(
            string abilityId,
            string trigger,
            string target,
            CombatEffectKind effectKind,
            double chancePercent = 100d,
            double cooldownSeconds = 0d)
        {
            var effect = effectKind == CombatEffectKind.ApplyStatus
                ? new CombatEffectDescriptor(effectKind, statusId: "status-fixture")
                : new CombatEffectDescriptor(
                    effectKind,
                    statId: "damage_percent",
                    value: -1d,
                    durationSeconds: 1d);
            return new CombatAbilityDescriptor(
                abilityId,
                trigger,
                chancePercent,
                target,
                cooldownSeconds,
                effect);
        }

        private static CombatRuntimeAggregate Aggregate(int enemyHp)
        {
            return new CombatRuntimeAggregate
            {
                execution = new CombatExecutionSaveData
                {
                    executionId = "execution",
                    sessionId = "session",
                    heroId = "hero"
                },
                session = new CombatSessionSaveData
                {
                    sessionId = "session",
                    executionId = "execution",
                    combatTimeSeconds = 0d,
                    hero = new CombatantStateSaveData
                    {
                        combatantId = "hero-combatant",
                        definitionId = "hero",
                        currentHp = 100,
                        maxHp = 100
                    },
                    currentEnemy = new CombatantStateSaveData
                    {
                        combatantId = "enemy-combatant",
                        definitionId = "enemy",
                        currentHp = enemyHp,
                        maxHp = enemyHp
                    },
                    scheduler = new CombatSchedulerStateSaveData(),
                    rng = CombatRngStateFactory.CreateSplitMix64(12345UL)
                }
            };
        }

        private static CombatRuntimeAggregate InitializedSameTimeAggregate()
        {
            var source = Aggregate(100);
            source.session.combatTimeSeconds = 1d;
            source.session.hero.nextAttackAtSeconds = 2d;
            source.session.currentEnemy.nextAttackAtSeconds = 2d;
            source.session.scheduler.nextSequence = 2;
            source.session.scheduler.scheduledEvents = new[]
            {
                Scheduled(2d, (int)CombatScheduledEventPhase.ActorAttack, CombatActorSide.Hero, 0),
                Scheduled(2d, (int)CombatScheduledEventPhase.ActorAttack, CombatActorSide.Enemy, 1)
            };
            return source;
        }

        private static CombatRuntimeAggregate QueueAggregate(
            string firstEnemyId,
            int firstEnemyHp,
            string secondEnemyId)
        {
            var source = Aggregate(firstEnemyHp);
            source.session.enemyGroupId = "group";
            source.session.combatMode = CombatEnemyQueueBuilder.Queue1V1Mode;
            source.session.enemyQueue = new[]
            {
                new CombatEnemyQueueEntrySaveData
                {
                    combatantId = "enemy-combatant",
                    enemyId = firstEnemyId,
                    level = 1,
                    queueIndex = 0
                },
                new CombatEnemyQueueEntrySaveData
                {
                    combatantId = "enemy-combatant-1",
                    enemyId = secondEnemyId,
                    level = 1,
                    queueIndex = 1
                }
            };
            source.session.queuePosition = 0;
            source.session.currentEnemy.definitionId = firstEnemyId;
            return source;
        }

        private static EnemiesConfigRepository EnemyConfigs(
            EnemyConfigDto[] enemies,
            EnemyGroupConfigDto[] groups)
        {
            return new EnemiesConfigRepository(new EnemiesRuntimeConfigDto
            {
                enemies = enemies,
                enemyLevels = new[]
                {
                    new EnemyLevelConfigDto { level = 1, hpMultiplier = 1f }
                },
                enemyGroups = groups
            });
        }

        private static EnemyConfigDto Enemy(string enemyId, int hp)
        {
            return new EnemyConfigDto { enemyId = enemyId, hp = hp };
        }

        private static EnemyGroupConfigDto Group(
            string groupId,
            string enemyRef,
            int sortOrder,
            int minCount,
            int maxCount,
            int weight = 100)
        {
            return new EnemyGroupConfigDto
            {
                enemyGroupId = groupId,
                enemyRef = enemyRef,
                sortOrder = sortOrder,
                minCount = minCount,
                maxCount = maxCount,
                weight = weight
            };
        }

        private static CombatScheduledEventSaveData Scheduled(
            double timestamp,
            int phase,
            CombatActorSide side,
            long sequence)
        {
            return new CombatScheduledEventSaveData
            {
                eventKey = $"event-{sequence}",
                eventType = CombatRuntimeService.ActorAttackEventType,
                timestampSeconds = timestamp,
                phasePriority = phase,
                actorSide = side,
                sequence = sequence
            };
        }

        private sealed class DescriptorProvider : ICombatDescriptorProvider
        {
            private readonly CombatActorDescriptor _hero;
            private readonly CombatActorDescriptor _enemy;

            public DescriptorProvider(CombatActorDescriptor hero, CombatActorDescriptor enemy)
            {
                _hero = hero;
                _enemy = enemy;
            }

            public bool TryGetDescriptor(
                CombatActorSide side,
                string definitionId,
                out CombatActorDescriptor descriptor,
                out string error)
            {
                descriptor = side == CombatActorSide.Hero ? _hero : _enemy;
                error = null;
                return descriptor != null;
            }
        }

        private sealed class AbilityDescriptorProvider : ICombatAbilityDescriptorProvider
        {
            private readonly Dictionary<string, CombatAbilityDescriptor[]> _abilities =
                new Dictionary<string, CombatAbilityDescriptor[]>(StringComparer.Ordinal);

            public AbilityDescriptorProvider Add(
                CombatActorSide side,
                string definitionId,
                params CombatAbilityDescriptor[] abilities)
            {
                _abilities[Key(side, definitionId)] =
                    abilities ?? Array.Empty<CombatAbilityDescriptor>();
                return this;
            }

            public bool TryGetAbilities(
                CombatActorSide ownerSide,
                string ownerDefinitionId,
                out CombatAbilityDescriptor[] abilities,
                out string error)
            {
                if (!_abilities.TryGetValue(Key(ownerSide, ownerDefinitionId), out abilities))
                    abilities = Array.Empty<CombatAbilityDescriptor>();
                error = null;
                return true;
            }

            private static string Key(CombatActorSide side, string definitionId)
            {
                return $"{side}:{definitionId}";
            }
        }

        private sealed class MemoryStore : ICombatRuntimeStore
        {
            public MemoryStore(CombatRuntimeAggregate value)
            {
                Value = Clone(value);
            }

            public CombatRuntimeAggregate Value { get; private set; }
            public bool RejectUpdates { get; set; }
            public int UpdateCount { get; private set; }
            public string Json => JsonUtility.ToJson(Value);

            public CombatRuntimeAggregate[] GetCombatAggregates()
            {
                return new[] { Clone(Value) };
            }

            public CombatRuntimeAggregate GetCombatAggregate(string executionId)
            {
                return string.Equals(Value?.execution?.executionId, executionId, StringComparison.Ordinal)
                    ? Clone(Value)
                    : null;
            }

            public bool AddCombatAggregate(CombatRuntimeAggregate aggregate)
            {
                return false;
            }

            public bool UpdateCombatAggregate(CombatRuntimeAggregate aggregate)
            {
                UpdateCount++;
                if (RejectUpdates)
                    return false;
                Value = Clone(aggregate);
                return true;
            }

            public bool RemoveCombatAggregate(string executionId)
            {
                return false;
            }

            private static CombatRuntimeAggregate Clone(CombatRuntimeAggregate source)
            {
                if (source == null)
                    return null;

                var clone = JsonUtility.FromJson<CombatRuntimeAggregate>(JsonUtility.ToJson(source));
                if (source.session?.currentEnemy == null && clone?.session != null)
                    clone.session.currentEnemy = null;
                return clone;
            }
        }

        private sealed class ScriptedRngFactory : ICombatRngFactory
        {
            private const string AlgorithmId = "scripted";

            public static CombatRngStateSaveData State(params ulong[] values)
            {
                return new CombatRngStateSaveData
                {
                    algorithmId = AlgorithmId,
                    formatVersion = 1,
                    state = string.Join(",", values.Select(value => value.ToString(CultureInfo.InvariantCulture))),
                    drawCount = 0
                };
            }

            public bool TryRestore(
                CombatRngStateSaveData state,
                out ICombatRng rng,
                out CombatAdvanceError error)
            {
                rng = null;
                error = null;
                if (state == null || !string.Equals(state.algorithmId, AlgorithmId, StringComparison.Ordinal))
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.UnsupportedRngDescriptor,
                        "Expected scripted RNG.");
                    return false;
                }

                var values = new Queue<ulong>();
                if (!string.IsNullOrEmpty(state.state))
                {
                    foreach (var value in state.state.Split(','))
                        values.Enqueue(ulong.Parse(value, CultureInfo.InvariantCulture));
                }

                rng = new ScriptedRng(values, state.drawCount);
                return true;
            }

            private sealed class ScriptedRng : ICombatRng
            {
                private readonly Queue<ulong> _values;
                private long _drawCount;

                public ScriptedRng(Queue<ulong> values, long drawCount)
                {
                    _values = values;
                    _drawCount = drawCount;
                }

                public ulong NextUInt64()
                {
                    _drawCount++;
                    return _values.Count == 0 ? ulong.MaxValue : _values.Dequeue();
                }

                public CombatRngStateSaveData CaptureState()
                {
                    return new CombatRngStateSaveData
                    {
                        algorithmId = AlgorithmId,
                        formatVersion = 1,
                        state = string.Join(",", _values.Select(value => value.ToString(CultureInfo.InvariantCulture))),
                        drawCount = _drawCount
                    };
                }
            }
        }

        private sealed class ThrowOnDrawRngFactory : ICombatRngFactory
        {
            private readonly int _throwOnDraw;

            public ThrowOnDrawRngFactory(int throwOnDraw)
            {
                _throwOnDraw = throwOnDraw;
            }

            public int SuccessfulDraws { get; private set; }
            public bool ThrowObserved { get; private set; }

            public bool TryRestore(
                CombatRngStateSaveData state,
                out ICombatRng rng,
                out CombatAdvanceError error)
            {
                rng = new ThrowOnDrawRng(this, state, _throwOnDraw);
                error = null;
                return true;
            }

            private sealed class ThrowOnDrawRng : ICombatRng
            {
                private readonly ThrowOnDrawRngFactory _owner;
                private readonly int _throwOnDraw;
                private readonly CombatRngStateSaveData _initialState;
                private int _drawAttempts;

                public ThrowOnDrawRng(
                    ThrowOnDrawRngFactory owner,
                    CombatRngStateSaveData initialState,
                    int throwOnDraw)
                {
                    _owner = owner;
                    _throwOnDraw = throwOnDraw;
                    _initialState = initialState;
                }

                public ulong NextUInt64()
                {
                    _drawAttempts++;
                    if (_drawAttempts == _throwOnDraw)
                    {
                        _owner.ThrowObserved = true;
                        throw new InvalidOperationException("Simulated mid-advance RNG failure.");
                    }

                    _owner.SuccessfulDraws++;
                    return ulong.MaxValue;
                }

                public CombatRngStateSaveData CaptureState()
                {
                    return new CombatRngStateSaveData
                    {
                        algorithmId = _initialState.algorithmId,
                        formatVersion = _initialState.formatVersion,
                        state = _initialState.state,
                        drawCount = _initialState.drawCount + _owner.SuccessfulDraws
                    };
                }
            }
        }
    }
}
