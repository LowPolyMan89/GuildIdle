using System;
using System.Collections.Generic;
using GuildIdle.Configs;

namespace GuildIdle.Combat
{
    public interface ICombatEnemyQueueProvider
    {
        bool TryBuildQueue(
            string sessionId,
            string enemyGroupId,
            ICombatRng rng,
            out CombatEnemyQueueEntrySaveData[] queue,
            out string error);

        bool TryCreateEnemyState(
            CombatEnemyQueueEntrySaveData queueEntry,
            out CombatantStateSaveData enemy,
            out string error);
    }

    public sealed class CombatEnemyQueueBuilder
    {
        public const string Queue1V1Mode = "Queue_1v1";

        private readonly ICombatEnemyQueueProvider _provider;
        private readonly ICombatRngFactory _rngFactory;

        public CombatEnemyQueueBuilder(
            ICombatEnemyQueueProvider provider,
            ICombatRngFactory rngFactory = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _rngFactory = rngFactory ?? new CombatRngFactory();
        }

        public bool TryBuild(
            CombatSessionSaveData source,
            out CombatSessionSaveData session,
            out CombatAdvanceError error)
        {
            session = null;
            error = null;
            if (source == null ||
                string.IsNullOrWhiteSpace(source.sessionId) ||
                string.IsNullOrWhiteSpace(source.enemyGroupId))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidEnemyQueue,
                    "Combat session id and enemy group id are required to build an enemy queue.");
                return false;
            }

            if (!string.Equals(source.combatMode, Queue1V1Mode, StringComparison.Ordinal))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.UnsupportedCombatMode,
                    $"Combat mode '{source.combatMode ?? "<null>"}' is not supported.");
                return false;
            }

            if (!TryRestoreRng(source.rng, out var rng, out error))
                return false;

            try
            {
                if (!_provider.TryBuildQueue(
                        source.sessionId,
                        source.enemyGroupId,
                        rng,
                        out var queue,
                        out var providerError) ||
                    queue == null ||
                    queue.Length == 0)
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.EnemyQueueNotFound,
                        providerError ?? $"Enemy group '{source.enemyGroupId}' did not produce an enemy queue.");
                    return false;
                }

                if (queue.Length > CombatRuntimeSaveDataUtility.PersistentCollectionLimit)
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.InvalidEnemyQueue,
                        "Combat enemy queue exceeds its bounded retention limit.");
                    return false;
                }

                if (!_provider.TryCreateEnemyState(queue[0], out var enemy, out providerError) ||
                    enemy == null)
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.EnemyStateCreationFailed,
                        providerError ?? "The first enemy state could not be created.");
                    return false;
                }

                session = CombatRuntimeSaveDataUtility.CloneSession(source);
                session.enemyQueue = queue;
                session.queuePosition = 0;
                session.currentEnemy = enemy;
                session.rng = rng.CaptureState();
                session.simulationStopped = false;
                return true;
            }
            catch (Exception exception)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.ProcessingFailed,
                    $"Combat enemy queue build failed: {exception.Message}");
                return false;
            }
        }

        private bool TryRestoreRng(
            CombatRngStateSaveData state,
            out ICombatRng rng,
            out CombatAdvanceError error)
        {
            rng = null;
            error = null;
            try
            {
                if (_rngFactory.TryRestore(state, out rng, out error) && rng != null)
                    return true;
            }
            catch (Exception exception)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidRngState,
                    $"Combat RNG restore failed: {exception.Message}");
                return false;
            }

            error ??= new CombatAdvanceError(
                CombatAdvanceErrorCode.InvalidRngState,
                "Combat RNG factory rejected the saved state.");
            return false;
        }
    }

    public sealed class ConfigCombatEnemyQueueProvider : ICombatEnemyQueueProvider
    {
        private readonly EnemiesConfigRepository _configs;

        public ConfigCombatEnemyQueueProvider(EnemiesConfigRepository configs)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public bool TryBuildQueue(
            string sessionId,
            string enemyGroupId,
            ICombatRng rng,
            out CombatEnemyQueueEntrySaveData[] queue,
            out string error)
        {
            queue = Array.Empty<CombatEnemyQueueEntrySaveData>();
            error = null;
            if (string.IsNullOrWhiteSpace(sessionId) ||
                string.IsNullOrWhiteSpace(enemyGroupId) ||
                rng == null)
            {
                error = "Session id, enemy group id and RNG are required.";
                return false;
            }

            if (!_configs.TryGetGroup(enemyGroupId, out var group) || group.Length == 0)
            {
                error = $"Enemy group '{enemyGroupId}' was not found.";
                return false;
            }

            var result = new List<CombatEnemyQueueEntrySaveData>();
            for (var first = 0; first < group.Length;)
            {
                var last = first + 1;
                while (last < group.Length && group[last].sortOrder == group[first].sortOrder)
                    last++;

                if (!TrySelectEntry(group, first, last, rng, out var selected, out error) ||
                    !TryParseAndValidate(selected, out var enemyId, out var level, out error))
                {
                    return false;
                }

                var count = selected.minCount;
                if (selected.maxCount > selected.minCount)
                    count = RollInclusive(rng, selected.minCount, selected.maxCount);
                if (count > CombatRuntimeSaveDataUtility.PersistentCollectionLimit - result.Count)
                {
                    error = "Enemy group expands beyond the bounded combat queue limit.";
                    return false;
                }

                for (var index = 0; index < count; index++)
                {
                    var queueIndex = result.Count;
                    result.Add(new CombatEnemyQueueEntrySaveData
                    {
                        combatantId = $"{sessionId}:enemy:{queueIndex}",
                        enemyId = enemyId,
                        level = level,
                        queueIndex = queueIndex
                    });
                }

                first = last;
            }

            if (result.Count == 0)
            {
                error = $"Enemy group '{enemyGroupId}' produced an empty queue.";
                return false;
            }

            queue = result.ToArray();
            return true;
        }

        public bool TryCreateEnemyState(
            CombatEnemyQueueEntrySaveData queueEntry,
            out CombatantStateSaveData enemy,
            out string error)
        {
            enemy = null;
            error = null;
            if (queueEntry == null ||
                string.IsNullOrWhiteSpace(queueEntry.combatantId) ||
                string.IsNullOrWhiteSpace(queueEntry.enemyId) ||
                queueEntry.level <= 0)
            {
                error = "Enemy queue entry is invalid.";
                return false;
            }

            if (!_configs.TryGet(queueEntry.enemyId, out var enemyConfig))
            {
                error = $"Enemy '{queueEntry.enemyId}' was not found.";
                return false;
            }

            if (!TryGetLevel(queueEntry.level, out var levelConfig))
            {
                error = $"Enemy level '{queueEntry.level}' was not found.";
                return false;
            }

            var scaledHp = Math.Ceiling(enemyConfig.hp * (double)levelConfig.hpMultiplier);
            if (scaledHp <= 0d || scaledHp > int.MaxValue)
            {
                error = $"Enemy '{queueEntry.enemyId}' level {queueEntry.level} has invalid scaled HP.";
                return false;
            }

            var maxHp = (int)scaledHp;
            enemy = new CombatantStateSaveData
            {
                combatantId = queueEntry.combatantId,
                definitionId = queueEntry.enemyId,
                currentHp = maxHp,
                maxHp = maxHp
            };
            return true;
        }

        private bool TryParseAndValidate(
            EnemyGroupConfigDto entry,
            out string enemyId,
            out int level,
            out string error)
        {
            enemyId = null;
            level = 0;
            error = null;
            if (entry == null ||
                entry.minCount <= 0 ||
                entry.maxCount < entry.minCount ||
                entry.maxCount > CombatRuntimeSaveDataUtility.PersistentCollectionLimit)
            {
                error = "Enemy group entry has an invalid count range.";
                return false;
            }

            var separator = entry.enemyRef?.LastIndexOf(':') ?? -1;
            if (separator <= 0 ||
                separator == entry.enemyRef.Length - 1 ||
                !int.TryParse(entry.enemyRef.Substring(separator + 1), out level) ||
                level <= 0)
            {
                error = $"Enemy reference '{entry.enemyRef ?? "<null>"}' must use enemy_id:level format.";
                return false;
            }

            enemyId = entry.enemyRef.Substring(0, separator);
            if (!_configs.TryGet(enemyId, out _) || !TryGetLevel(level, out _))
            {
                error = $"Enemy reference '{entry.enemyRef}' does not resolve to an enemy and level.";
                return false;
            }

            return true;
        }

        private static bool TrySelectEntry(
            EnemyGroupConfigDto[] group,
            int first,
            int last,
            ICombatRng rng,
            out EnemyGroupConfigDto selected,
            out string error)
        {
            selected = null;
            error = null;
            if (last - first == 1)
            {
                selected = group[first];
                return true;
            }

            long totalWeight = 0;
            for (var index = first; index < last; index++)
            {
                if (group[index] == null || group[index].weight < 0)
                {
                    error = "Enemy group alternative has an invalid weight.";
                    return false;
                }

                totalWeight += group[index].weight;
                if (totalWeight > int.MaxValue)
                {
                    error = "Enemy group alternative weights exceed the supported range.";
                    return false;
                }
            }

            if (totalWeight <= 0)
            {
                error = "Enemy group alternatives require a positive total weight.";
                return false;
            }

            var roll = RollInclusive(rng, 1, (int)totalWeight);
            long cumulative = 0;
            for (var index = first; index < last; index++)
            {
                cumulative += group[index].weight;
                if (roll <= cumulative)
                {
                    selected = group[index];
                    return true;
                }
            }

            error = "Enemy group alternative selection failed.";
            return false;
        }

        private bool TryGetLevel(int level, out EnemyLevelConfigDto result)
        {
            foreach (var entry in _configs.EnemyLevels)
            {
                if (entry != null && entry.level == level)
                {
                    result = entry;
                    return true;
                }
            }

            result = null;
            return false;
        }

        private static int RollInclusive(ICombatRng rng, int minimum, int maximum)
        {
            var range = (ulong)((long)maximum - minimum + 1L);
            var threshold = unchecked(0UL - range) % range;
            ulong value;
            do
            {
                value = rng.NextUInt64();
            } while (value < threshold);

            return (int)(minimum + (long)(value % range));
        }
    }
}
