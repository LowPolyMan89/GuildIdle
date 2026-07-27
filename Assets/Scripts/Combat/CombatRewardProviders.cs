using System;
using System.Collections.Generic;
using GuildIdle.Activities;
using GuildIdle.Configs;
using GuildIdle.Core;
using GuildIdle.Player;

namespace GuildIdle.Combat
{
    public sealed class CombatEnemyRewardDescriptor
    {
        public CombatEnemyRewardDescriptor(
            long enemyExp,
            CombatRewardEntrySaveData[] loot)
        {
            EnemyExp = enemyExp;
            Loot = loot ?? Array.Empty<CombatRewardEntrySaveData>();
        }

        public long EnemyExp { get; }
        public CombatRewardEntrySaveData[] Loot { get; }
    }

    public interface ICombatEnemyRewardProvider
    {
        bool TryResolve(
            string enemyId,
            ICombatRng rng,
            out CombatEnemyRewardDescriptor reward,
            out string error);
    }

    public interface ICombatCompletionRewardProvider
    {
        bool TryCreateSnapshot(
            string activityId,
            bool activityAlreadyCompleted,
            ICombatRng rng,
            out CombatRewardEntrySaveData[] rewards,
            out string error);
    }

    public sealed class ConfigCombatEnemyRewardProvider :
        ICombatEnemyRewardProvider
    {
        private readonly EnemiesConfigRepository _enemies;
        private readonly ItemsConfigRepository _items;

        public ConfigCombatEnemyRewardProvider(
            EnemiesConfigRepository enemies,
            ItemsConfigRepository items)
        {
            _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            _items = items ?? throw new ArgumentNullException(nameof(items));
        }

        public bool TryResolve(
            string enemyId,
            ICombatRng rng,
            out CombatEnemyRewardDescriptor reward,
            out string error)
        {
            reward = null;
            error = null;
            if (rng == null || !_enemies.TryGet(enemyId, out var enemy) ||
                enemy == null || enemy.combatExp < 0)
            {
                error = $"Enemy reward descriptor '{enemyId ?? "<null>"}' is invalid.";
                return false;
            }

            var entries = new List<CombatRewardEntrySaveData>();
            var rows = string.IsNullOrWhiteSpace(enemy.lootGroupId)
                ? Array.Empty<EnemyLootConfigDto>()
                : _enemies.GetEnemyLoot(enemy.lootGroupId);
            if (!string.IsNullOrWhiteSpace(enemy.lootGroupId) &&
                rows.Length == 0)
            {
                error = $"Enemy loot group '{enemy.lootGroupId}' was not found.";
                return false;
            }
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                if (row == null ||
                    !string.Equals(row.enemyId, enemyId, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(row.lootId) ||
                    row.minCount < 0 || row.maxCount < row.minCount ||
                    row.chancePercent < 0f || row.chancePercent > 100f ||
                    row.qualityMin < 0 || row.qualityMax < row.qualityMin)
                {
                    error = $"Enemy loot descriptor '{enemy.lootGroupId}' is invalid.";
                    return false;
                }

                if (!CombatRngRolls.ChancePassed(rng, row.chancePercent))
                    continue;
                var quantity = CombatRngRolls.Inclusive(
                    rng,
                    row.minCount,
                    row.maxCount);
                if (quantity <= 0)
                    continue;
                var quality = CombatRngRolls.Inclusive(
                    rng,
                    row.qualityMin,
                    row.qualityMax);
                if (!TryResolveRewardType(row.lootId, out var rewardType))
                {
                    error = $"Enemy loot target '{row.lootId}' is not a configured item or currency.";
                    return false;
                }

                entries.Add(new CombatRewardEntrySaveData
                {
                    sortOrder = index,
                    rewardType = rewardType,
                    targetId = row.lootId,
                    quantity = quantity,
                    origin = PendingResultOrigin.CombatLoot,
                    quality = quality
                });
            }

            reward = new CombatEnemyRewardDescriptor(
                enemy.combatExp,
                entries.ToArray());
            return true;
        }

        private bool TryResolveRewardType(string targetId, out string rewardType)
        {
            rewardType = null;
            if (_items.TryGetCurrency(targetId, out _))
            {
                rewardType = RewardType.Currency;
                return true;
            }

            if (!_items.TryGet(targetId, out var item) || item == null)
                return false;
            if (string.Equals(item.Kind, "resource", StringComparison.OrdinalIgnoreCase))
                rewardType = RewardType.Resource;
            else if (string.Equals(item.Kind, "equipment", StringComparison.OrdinalIgnoreCase))
                rewardType = RewardType.Equipment;
            else if (string.Equals(item.Kind, "consumable", StringComparison.OrdinalIgnoreCase))
                rewardType = RewardType.Consumable;
            else if (string.Equals(item.Kind, "recipe", StringComparison.OrdinalIgnoreCase))
                rewardType = RewardType.Recipe;
            else
                rewardType = RewardType.Item;
            return true;
        }
    }

    public sealed class ConfigCombatCompletionRewardProvider :
        ICombatCompletionRewardProvider
    {
        private readonly ActivitiesConfigRepository _activities;

        public ConfigCombatCompletionRewardProvider(
            ActivitiesConfigRepository activities)
        {
            _activities =
                activities ?? throw new ArgumentNullException(nameof(activities));
        }

        public bool TryCreateSnapshot(
            string activityId,
            bool activityAlreadyCompleted,
            ICombatRng rng,
            out CombatRewardEntrySaveData[] rewards,
            out string error)
        {
            rewards = Array.Empty<CombatRewardEntrySaveData>();
            error = null;
            if (string.IsNullOrWhiteSpace(activityId) || rng == null)
            {
                error = "Combat completion reward snapshot requires activity and RNG.";
                return false;
            }

            var entries = new List<CombatRewardEntrySaveData>();
            var definitions = _activities.GetRewards(activityId);
            for (var index = 0; index < definitions.Length; index++)
            {
                var definition = definitions[index];
                if (definition == null ||
                    (!ActivityResolverUtilities.MomentMatches(
                         definition.grantMoment,
                         GrantMoment.OnComplete) &&
                     !ActivityResolverUtilities.MomentMatches(
                         definition.grantMoment,
                         GrantMoment.OnFirstComplete)))
                    continue;
                if (activityAlreadyCompleted &&
                    ActivityResolverUtilities.MomentMatches(
                        definition.grantMoment,
                        GrantMoment.OnFirstComplete))
                    continue;
                if (string.IsNullOrWhiteSpace(definition.rewardType) ||
                    string.IsNullOrWhiteSpace(definition.targetId) ||
                    definition.min < 0 || definition.max < definition.min ||
                    definition.chance < 0f || definition.chance > 100f)
                {
                    error = $"Activity reward descriptor '{activityId}' is invalid.";
                    return false;
                }
                if (!CombatRngRolls.ChancePassed(rng, definition.chance))
                    continue;
                var quantity = CombatRngRolls.Inclusive(
                    rng,
                    definition.min,
                    definition.max);
                if (quantity <= 0)
                    continue;
                entries.Add(new CombatRewardEntrySaveData
                {
                    entryId = $"completion:{activityId}:{index}",
                    sortOrder = index,
                    rewardType = definition.rewardType,
                    targetId = definition.targetId,
                    quantity = quantity,
                    origin = PendingResultOrigin.ActivityReward
                });
            }

            rewards = entries.ToArray();
            return true;
        }
    }

    internal static class CombatRngRolls
    {
        private const double PercentScale =
            100d / 9007199254740992d;

        public static bool ChancePassed(ICombatRng rng, double chancePercent)
        {
            if (chancePercent <= 0d)
                return false;
            if (chancePercent >= 100d)
                return true;
            var roll = (rng.NextUInt64() >> 11) * PercentScale;
            return roll < chancePercent;
        }

        public static int Inclusive(ICombatRng rng, int minimum, int maximum)
        {
            if (maximum <= minimum)
                return minimum;
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
