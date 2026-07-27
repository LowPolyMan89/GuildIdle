using System;
using System.Collections.Generic;
using System.Linq;
using GuildIdle.Combat;
using GuildIdle.Core;
using GuildIdle.Editor.Configs;
using GuildIdle.Player;
using NUnit.Framework;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Editor.Player
{
    public sealed class CombatOutcomeServiceTests
    {
        private PlayerStateFactory _factory;
        private MemorySaveStorage _storage;
        private PlayerState _state;

        [SetUp]
        public void SetUp()
        {
            var database = new TestConfigDatabaseBuilder()
                .WithFullPlayerStateTestData()
                .Build();
            RuntimeConfigs.SetDatabaseForTests(database);
            _factory = TestPlayerComposition.CreatePlayerStateFactory(database);
            _storage = new MemorySaveStorage();
            _state = SaveService.Load(_factory, _storage);
        }

        [Test]
        public void VictoryFormsOneCombatResultAndClaimCompletesDirectCombat()
        {
            var aggregate = Aggregate(
                CombatTerminalCandidateKinds.Victory,
                victory: true);
            aggregate.session.loot = new[]
            {
                Reward("loot-gold", "Currency", "gold_id", 7,
                    PendingResultOrigin.CombatLoot)
            };
            aggregate.session.accumulatedEnemyExp = 12;
            aggregate.session.completionRewards = new[]
            {
                Reward("completion-exp", "SkillExp", "skill_combat", 40,
                    PendingResultOrigin.ActivityReward)
            };
            aggregate.session.loadoutKind = CombatLoadoutKind.Consumable;
            aggregate.session.broughtConsumable =
                new CombatConsumableStateSaveData
                {
                    sourceStackId = "consumed-source-stack",
                    itemId = "consumable_hunting_potion",
                    initialQuantity = 3,
                    remainingQuantity = 2,
                    nextCheckAtSeconds = 1d
                };
            Assert.That(_state.AddCombatAggregate(aggregate), Is.True);
            var goldBefore = _state.GetCurrency("gold_id");

            var formed = new CombatOutcomeService(_state)
                .FinalizeTerminal("combat-a");

            Assert.That(formed.Success, Is.True, formed.Message);
            Assert.That(formed.Code, Is.EqualTo("ResultPending"));
            Assert.That(_state.PendingResults.GetAll(), Has.Length.EqualTo(1));
            var pending = _state.PendingResults.GetAll()[0];
            Assert.That(pending.sourceType,
                Is.EqualTo(PendingResultSourceType.Combat));
            Assert.That(pending.sourceExecutionId, Is.EqualTo("combat-a"));
            Assert.That(
                pending.entries.Select(value => value.origin),
                Is.EquivalentTo(new[]
                {
                    PendingResultOrigin.CombatLoot,
                    PendingResultOrigin.EnemyCombatExp,
                    PendingResultOrigin.ActivityReward,
                    PendingResultOrigin.BroughtConsumable
                }));
            Assert.That(_state.GetCurrency("gold_id"), Is.EqualTo(goldBefore));
            Assert.That(_state.GetCombatAggregate("combat-a").execution.status,
                Is.EqualTo(CombatExecutionStatus.ResultPending));
            Assert.That(_state.GetHeroCurrentActivityExecutionId("ren"),
                Is.EqualTo("combat-a"));
            Assert.That(_state.IsActivityCompleted(
                "combat_clear_hall_forest"), Is.False);

            var claimed = _state.PendingResults.ClaimAll(
                "claim-combat-a",
                pending.resultId,
                pending.revision,
                _state.Storage.GetSnapshot().Revision);

            Assert.That(claimed.Success, Is.True, claimed.Message);
            Assert.That(claimed.Resolved, Is.True);
            Assert.That(_state.GetCurrency("gold_id"), Is.EqualTo(goldBefore + 7));
            Assert.That(_state.GetItem("consumable_hunting_potion"),
                Is.EqualTo(2));
            Assert.That(_state.GetCombatAggregate("combat-a").execution.status,
                Is.EqualTo(CombatExecutionStatus.Completed));
            Assert.That(_state.IsActivityCompleted(
                "combat_clear_hall_forest"), Is.True);
            Assert.That(_state.IsHeroBusy("ren"), Is.False);

            var replay = new CombatOutcomeService(_state)
                .FinalizeTerminal("combat-a");
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(_state.PendingResults.GetAll(), Is.Empty);
        }

        [Test]
        public void RetreatIsIdempotentAndDoesNotIncludeCompletionRewards()
        {
            var aggregate = RunningAggregate();
            aggregate.session.loot = new[]
            {
                Reward("loot-gold", "Currency", "gold_id", 5,
                    PendingResultOrigin.CombatLoot)
            };
            aggregate.session.completionRewards = new[]
            {
                Reward("completion-exp", "SkillExp", "skill_combat", 40,
                    PendingResultOrigin.ActivityReward)
            };
            Assert.That(_state.AddCombatAggregate(aggregate), Is.True);
            var service = new CombatOutcomeService(_state);

            var first = service.RequestRetreat(new CombatRetreatCommand
            {
                OperationId = "retreat-operation",
                ExecutionId = "combat-a"
            });
            var replay = service.RequestRetreat(new CombatRetreatCommand
            {
                OperationId = "retreat-operation",
                ExecutionId = "combat-a"
            });
            var conflict = service.RequestRetreat(new CombatRetreatCommand
            {
                OperationId = "retreat-operation",
                ExecutionId = "combat-b"
            });

            Assert.That(first.Success, Is.True, first.Message);
            Assert.That(replay.Success, Is.True);
            Assert.That(replay.Replayed, Is.True);
            Assert.That(conflict.Success, Is.False);
            Assert.That(conflict.Code, Is.EqualTo("OperationConflict"));
            Assert.That(_state.PendingResults.GetAll(), Has.Length.EqualTo(1));
            Assert.That(
                _state.PendingResults.GetAll()[0].entries
                    .Select(value => value.origin),
                Does.Not.Contain(PendingResultOrigin.ActivityReward));
            Assert.That(_state.GetCombatAggregate("combat-a").execution.outcome,
                Is.EqualTo(CombatTerminalCandidateKinds.Retreat));
        }

        [Test]
        public void DefeatAggregatesEligibleLootAndPersistsProtectedBreakdown()
        {
            var aggregate = Aggregate(
                CombatTerminalCandidateKinds.Defeat,
                victory: false);
            aggregate.session.loot = new[]
            {
                Reward("gold-a", "Currency", "gold_id", 3,
                    PendingResultOrigin.CombatLoot),
                Reward("gold-b", "Currency", "gold_id", 5,
                    PendingResultOrigin.CombatLoot),
                Reward("linked", "Resource", "resource_pine_wood", 4,
                    PendingResultOrigin.ActivityLootInCombat)
            };
            aggregate.session.accumulatedEnemyExp = 9;
            aggregate.session.loadoutKind = CombatLoadoutKind.Consumable;
            aggregate.session.broughtConsumable =
                new CombatConsumableStateSaveData
                {
                    sourceStackId = "spent-source",
                    itemId = "consumable_hunting_potion",
                    initialQuantity = 4,
                    remainingQuantity = 3,
                    nextCheckAtSeconds = 1d
                };
            Assert.That(_state.AddCombatAggregate(aggregate), Is.True);

            var result = new CombatOutcomeService(_state)
                .FinalizeTerminal("combat-a");

            Assert.That(result.Success, Is.True, result.Message);
            var saved = _state.GetCombatAggregate("combat-a").session;
            Assert.That(saved.defeatLoss.lossPercent,
                Is.InRange(25, 50));
            Assert.That(saved.defeatLoss.entries, Has.Length.EqualTo(2));
            var goldLoss = saved.defeatLoss.entries.Single(value =>
                value.targetId == "gold_id");
            Assert.That(goldLoss.quantityBefore, Is.EqualTo(8));
            Assert.That(goldLoss.quantityLost,
                Is.EqualTo(8 * saved.defeatLoss.lossPercent / 100));
            Assert.That(goldLoss.quantityKept,
                Is.EqualTo(8 - goldLoss.quantityLost));
            Assert.That(saved.defeatLoss.entries.Any(value =>
                value.origin == PendingResultOrigin.BroughtConsumable), Is.False);
            var pending = _state.PendingResults.GetAll()[0];
            Assert.That(pending.entries.Single(value =>
                    value.origin == PendingResultOrigin.BroughtConsumable)
                .quantity, Is.EqualTo(3));
            Assert.That(pending.entries.Single(value =>
                    value.origin == PendingResultOrigin.EnemyCombatExp)
                .quantity, Is.EqualTo(9));

            var restored = SaveService.Load(_factory, _storage);
            var restoredLoss =
                restored.GetCombatAggregate("combat-a").session.defeatLoss;
            Assert.That(restored.PendingResults.GetAll(),
                Has.Length.EqualTo(1));
            Assert.That(
                restored.GetCombatAggregate("combat-a").execution.status,
                Is.EqualTo(CombatExecutionStatus.ResultPending));
            Assert.That(restoredLoss.lossPercent,
                Is.EqualTo(saved.defeatLoss.lossPercent));
            Assert.That(restoredLoss.entries.Select(value =>
                    (value.targetId, value.quantityBefore,
                        value.quantityLost, value.quantityKept)),
                Is.EqualTo(saved.defeatLoss.entries.Select(value =>
                    (value.targetId, value.quantityBefore,
                        value.quantityLost, value.quantityKept))));
        }

        [Test]
        public void EmptyOutcomeResolvesImmediatelyWithoutPersistentResult()
        {
            var aggregate = Aggregate(
                CombatTerminalCandidateKinds.Retreat,
                victory: false);
            Assert.That(_state.AddCombatAggregate(aggregate), Is.True);

            var result = new CombatOutcomeService(_state)
                .FinalizeTerminal("combat-a");

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.ResolvedImmediately, Is.True);
            Assert.That(_state.PendingResults.GetAll(), Is.Empty);
            Assert.That(_state.GetCombatAggregate("combat-a").execution.status,
                Is.EqualTo(CombatExecutionStatus.Completed));
            Assert.That(_state.IsHeroBusy("ren"), Is.False);
            Assert.That(_state.IsActivityCompleted(
                "combat_clear_hall_forest"), Is.False);
        }

        [Test]
        public void MissingCompletionSnapshotReturnsTypedCorruptedState()
        {
            var aggregate = Aggregate(
                CombatTerminalCandidateKinds.Victory,
                victory: true);
            aggregate.session.completionRewardsSnapshotCreated = false;
            Assert.That(_state.AddCombatAggregate(aggregate), Is.True);

            var result = new CombatOutcomeService(_state)
                .FinalizeTerminal("combat-a");

            Assert.That(result.Success, Is.False);
            Assert.That(result.Code, Is.EqualTo("CorruptedState"));
            Assert.That(_state.PendingResults.GetAll(), Is.Empty);
            Assert.That(_state.GetCombatAggregate("combat-a").execution.status,
                Is.EqualTo(CombatExecutionStatus.Running));
        }

        [Test]
        public void FailedPendingFormationRollsBackTerminalCandidateAndOutcome()
        {
            Assert.That(_state.AddCombatAggregate(RunningAggregate()), Is.True);
            var detached = _state.GetCombatAggregate("combat-a");
            detached.session.queuePosition = detached.session.enemyQueue.Length;
            detached.session.currentEnemy = null;
            detached.session.simulationStopped = true;
            detached.session.combatTimeSeconds = 1d;
            detached.session.scheduler.scheduledEvents =
                Array.Empty<CombatScheduledEventSaveData>();
            detached.session.terminalCandidate =
                new CombatTerminalCandidateSaveData
                {
                    candidateId = "session-a:victory",
                    kind = CombatTerminalCandidateKinds.Victory,
                    eventKey = "terminal:victory",
                    createdAtSeconds = 1d
                };
            detached.session.loot = new[]
            {
                Reward("invalid", "Resource", "missing-resource", 1,
                    PendingResultOrigin.CombatLoot)
            };
            var beforeRng =
                _state.GetCombatAggregate("combat-a").session.rng.state;

            var committed = new CombatOutcomeService(_state)
                .TryCommit(detached, out var error);

            Assert.That(committed, Is.False);
            Assert.That(error.Code,
                Is.EqualTo(CombatAdvanceErrorCode.OutcomeCommitFailed));
            var restored = _state.GetCombatAggregate("combat-a");
            Assert.That(restored.execution.outcomeFinalized, Is.False);
            Assert.That(restored.session.terminalCandidate, Is.Null);
            Assert.That(restored.session.simulationStopped, Is.False);
            Assert.That(restored.session.rng.state, Is.EqualTo(beforeRng));
            Assert.That(_state.PendingResults.GetAll(), Is.Empty);
        }

        private static CombatRuntimeAggregate RunningAggregate()
        {
            var aggregate = BaseAggregate();
            aggregate.session.currentEnemy =
                new CombatantStateSaveData
                {
                    combatantId = "enemy-0",
                    definitionId = "enemy_lean_wolf",
                    currentHp = 10,
                    maxHp = 10
                };
            aggregate.session.enemyQueue = new[]
            {
                new CombatEnemyQueueEntrySaveData
                {
                    combatantId = "enemy-0",
                    enemyId = "enemy_lean_wolf",
                    level = 1,
                    queueIndex = 0
                }
            };
            aggregate.session.queuePosition = 0;
            return aggregate;
        }

        private static CombatRuntimeAggregate Aggregate(
            string outcome,
            bool victory)
        {
            var aggregate = BaseAggregate();
            aggregate.session.simulationStopped = true;
            aggregate.session.terminalCandidate =
                new CombatTerminalCandidateSaveData
                {
                    candidateId = $"session-a:{outcome.ToLowerInvariant()}",
                    kind = outcome,
                    eventKey = $"terminal:{outcome}",
                    createdAtSeconds = 1d
                };
            aggregate.session.combatTimeSeconds = 1d;
            if (victory)
            {
                aggregate.session.enemyQueue =
                    Array.Empty<CombatEnemyQueueEntrySaveData>();
                aggregate.session.queuePosition = 0;
                aggregate.session.currentEnemy = null;
            }
            else
            {
                aggregate.session.enemyQueue = new[]
                {
                    new CombatEnemyQueueEntrySaveData
                    {
                        combatantId = "enemy-0",
                        enemyId = "enemy_lean_wolf",
                        level = 1,
                        queueIndex = 0
                    }
                };
                aggregate.session.queuePosition = 0;
                aggregate.session.currentEnemy =
                    new CombatantStateSaveData
                    {
                        combatantId = "enemy-0",
                        definitionId = "enemy_lean_wolf",
                        currentHp = 10,
                        maxHp = 10
                    };
                if (outcome == CombatTerminalCandidateKinds.Defeat)
                    aggregate.session.hero.currentHp = 0;
            }
            return aggregate;
        }

        private static CombatRuntimeAggregate BaseAggregate() =>
            new CombatRuntimeAggregate
            {
                execution = new CombatExecutionSaveData
                {
                    executionId = "combat-a",
                    sessionId = "session-a",
                    sourceActivityId = "combat_clear_hall_forest",
                    sourceExecutionId = "combat-a",
                    occupationOwnerId = "combat-a",
                    heroId = "ren",
                    status = CombatExecutionStatus.Running
                },
                session = new CombatSessionSaveData
                {
                    sessionId = "session-a",
                    executionId = "combat-a",
                    enemyGroupId = "enemy_group_underwood_wolves",
                    combatMode = CombatEnemyQueueBuilder.Queue1V1Mode,
                    hero = new CombatantStateSaveData
                    {
                        combatantId = "hero-ren",
                        definitionId = "ren",
                        currentHp = 100,
                        maxHp = 100
                    },
                    scheduler = new CombatSchedulerStateSaveData(),
                    rng = CombatRngStateFactory.CreateSplitMix64(1234UL),
                    enemyExpTargetId = "skill_combat",
                    completionRewardsSnapshotCreated = true,
                    loadoutKind = CombatLoadoutKind.Empty
                }
            };

        private static CombatRewardEntrySaveData Reward(
            string id,
            string rewardType,
            string targetId,
            long quantity,
            string origin) =>
            new CombatRewardEntrySaveData
            {
                entryId = id,
                rewardType = rewardType,
                targetId = targetId,
                quantity = quantity,
                origin = origin
            };

        private sealed class MemorySaveStorage : ISaveStorage
        {
            private readonly Dictionary<string, string> _values =
                new Dictionary<string, string>(StringComparer.Ordinal);

            public bool HasKey(string key) => _values.ContainsKey(key);
            public string GetString(string key, string defaultValue) =>
                _values.TryGetValue(key, out var value)
                    ? value
                    : defaultValue;
            public void SetString(string key, string value) =>
                _values[key] = value;
            public void DeleteKey(string key) => _values.Remove(key);
            public void Save() { }
        }
    }
}
