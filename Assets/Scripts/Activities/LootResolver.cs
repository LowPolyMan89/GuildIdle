using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Core;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Activities
{
    public static class LootResolver
    {
        public static LootRollResult RollLootTable(string lootTableId)
        {
            return RollLootTable(lootTableId, ActivityResolverUtilities.DefaultRandom());
        }

        public static LootRollResult RollLootTable(string lootTableId, IActivityRandom random)
        {
            random ??= ActivityResolverUtilities.DefaultRandom();
            var issues = new List<string>();
            var drops = new List<LootDropResult>();

            if (!RuntimeConfigs.IsLoaded)
                return FailedTable(lootTableId, "Runtime configs are not loaded.");

            if (!RuntimeConfigs.Loot.TryGet(lootTableId, out var table))
                return FailedTable(lootTableId, $"Unknown loot table id '{lootTableId}'.");

            if (!table.enabled)
                return new LootRollResult { lootTableId = lootTableId, success = true, drops = Array.Empty<LootDropResult>(), issues = Array.Empty<string>() };

            var groups = RuntimeConfigs.Loot.GetGroups(lootTableId);
            if (groups.Length == 0)
            {
                RollEntries(lootTableId, string.Empty, table.rollMode, table.rollCountMin, table.rollCountMax, RuntimeConfigs.Loot.GetEntries(lootTableId), random, drops, issues);
            }
            else
            {
                foreach (var group in groups)
                {
                    if (group == null || !ActivityResolverUtilities.ChancePassed(group.chance, random))
                        continue;

                    var entries = EntriesForGroup(lootTableId, group.rollGroup);
                    RollEntries(lootTableId, group.rollGroup, group.rollMode, group.rollCountMin, group.rollCountMax, entries, random, drops, issues);
                }
            }

            return new LootRollResult
            {
                lootTableId = lootTableId,
                success = issues.Count == 0,
                drops = drops.ToArray(),
                issues = issues.ToArray()
            };
        }

        public static LootRollResult RollEnemyLoot(string lootGroupId)
        {
            return RollEnemyLoot(lootGroupId, ActivityResolverUtilities.DefaultRandom());
        }

        public static LootRollResult RollEnemyLoot(string lootGroupId, IActivityRandom random)
        {
            random ??= ActivityResolverUtilities.DefaultRandom();
            var issues = new List<string>();
            var drops = new List<LootDropResult>();

            if (!RuntimeConfigs.IsLoaded)
                return FailedEnemyLoot(lootGroupId, "Runtime configs are not loaded.");

            var rows = RuntimeConfigs.Enemies.GetEnemyLoot(lootGroupId);
            if (rows.Length == 0)
                return FailedEnemyLoot(lootGroupId, $"Unknown enemy loot group id '{lootGroupId}'.");

            foreach (var row in rows)
            {
                if (row == null || !ActivityResolverUtilities.ChancePassed(row.chancePercent, random))
                    continue;

                var min = Math.Max(0, row.minCount);
                var max = Math.Max(min, row.maxCount);
                var amount = random.RangeInclusive(min, max);
                if (amount <= 0)
                    continue;

                AddDrop("EnemyLoot", row.lootId, amount, row.lootId, drops, issues);
            }

            return new LootRollResult
            {
                lootGroupId = lootGroupId,
                success = issues.Count == 0,
                drops = drops.ToArray(),
                issues = issues.ToArray()
            };
        }

        internal static ActivityAppliedReward ApplyDrop(LootDropResult drop, IActivityPlayerState state)
        {
            if (drop == null)
                return new ActivityAppliedReward { rewardType = "Loot", applied = false, message = "Empty loot drop." };

            if (drop.isCurrency)
            {
                var applied = state.AddCurrency(drop.targetId, drop.amount);
                drop.granted = applied;
                return new ActivityAppliedReward
                {
                    rewardType = drop.dropType,
                    targetId = drop.targetId,
                    ownerType = ActivityResolverUtilities.OwnerProfile,
                    amount = drop.amount,
                    applied = applied,
                    isCurrency = true,
                    message = applied ? "Applied currency loot." : "Failed to apply currency loot."
                };
            }

            var itemApplied = state.AddItem(drop.targetId, drop.amount);
            drop.granted = itemApplied;
            return new ActivityAppliedReward
            {
                rewardType = drop.dropType,
                targetId = drop.targetId,
                ownerType = ActivityResolverUtilities.OwnerProfile,
                amount = drop.amount,
                applied = itemApplied,
                isCurrency = false,
                message = itemApplied ? "Applied item loot." : "Failed to apply item loot."
            };
        }

        private static LootTableEntryConfigDto[] EntriesForGroup(string lootTableId, string rollGroup)
        {
            var result = new List<LootTableEntryConfigDto>();
            foreach (var entry in RuntimeConfigs.Loot.GetEntries(lootTableId))
            {
                if (entry == null)
                    continue;

                if (string.Equals(entry.requiredRollGroup, rollGroup, StringComparison.Ordinal))
                    result.Add(entry);
            }

            return result.ToArray();
        }

        private static void RollEntries(
            string lootTableId,
            string rollGroup,
            string rollMode,
            int rollCountMin,
            int rollCountMax,
            LootTableEntryConfigDto[] entries,
            IActivityRandom random,
            List<LootDropResult> drops,
            List<string> issues)
        {
            if (LootRollMode.Matches(rollMode, LootRollMode.GuaranteedAll))
            {
                foreach (var entry in entries)
                {
                    if (entry != null && ActivityResolverUtilities.ChancePassed(entry.chance, random))
                        AddDrop(entry.dropType, entry.targetId, ActivityResolverUtilities.PositiveAmount(entry.min, entry.max, random), entry.entryId, drops, issues);
                }

                return;
            }

            if (!LootRollMode.Matches(rollMode, LootRollMode.WeightedOne) &&
                !LootRollMode.Matches(rollMode, LootRollMode.WeightedMany))
            {
                issues.Add($"Unsupported roll mode '{rollMode}' in loot table '{lootTableId}' group '{rollGroup}'.");
                return;
            }

            var rollCount = ActivityResolverUtilities.PositiveAmount(rollCountMin, rollCountMax, random);
            for (var i = 0; i < rollCount; i++)
            {
                var selected = SelectWeighted(entries, random);
                if (selected == null)
                    continue;

                AddDrop(selected.dropType, selected.targetId, ActivityResolverUtilities.PositiveAmount(selected.min, selected.max, random), selected.entryId, drops, issues);

                if (LootRollMode.Matches(rollMode, LootRollMode.WeightedOne))
                    break;
            }
        }

        private static LootTableEntryConfigDto SelectWeighted(LootTableEntryConfigDto[] entries, IActivityRandom random)
        {
            var candidates = new List<LootTableEntryConfigDto>();
            var totalWeight = 0;
            foreach (var entry in entries)
            {
                if (entry == null || entry.weight <= 0 || !ActivityResolverUtilities.ChancePassed(entry.chance, random))
                    continue;

                candidates.Add(entry);
                totalWeight += entry.weight;
            }

            if (candidates.Count == 0 || totalWeight <= 0)
                return null;

            var roll = random.RangeInclusive(1, totalWeight);
            var current = 0;
            foreach (var entry in candidates)
            {
                current += entry.weight;
                if (roll <= current)
                    return entry;
            }

            return candidates[candidates.Count - 1];
        }

        private static void AddDrop(string dropType, string targetId, int amount, string sourceId, List<LootDropResult> drops, List<string> issues)
        {
            var resolvedType = dropType ?? string.Empty;
            var resolvedTarget = targetId;
            var isCurrency = false;

            if (DropType.Matches(resolvedType, DropType.Gold) ||
                string.Equals(targetId, ActivityResolverUtilities.GoldCurrencyId, StringComparison.Ordinal))
            {
                resolvedType = DropType.Gold;
                resolvedTarget = ActivityResolverUtilities.GoldCurrencyId;
                isCurrency = true;
            }
            else if (DropType.Matches(resolvedType, DropType.Currency))
            {
                isCurrency = true;
            }
            else if (RuntimeConfigs.Items.TryGetCurrency(targetId, out _))
            {
                resolvedType = DropType.Currency;
                isCurrency = true;
            }

            if (isCurrency)
            {
                if (!RuntimeConfigs.Items.TryGetCurrency(resolvedTarget, out _))
                {
                    issues.Add($"Unknown currency loot target '{resolvedTarget}' from '{sourceId}'.");
                    Debug.LogError($"[LootResolver] Unknown currency loot target '{resolvedTarget}' from '{sourceId}'.");
                    return;
                }
            }
            else if (!RuntimeConfigs.Items.TryGet(resolvedTarget, out _))
            {
                issues.Add($"Unknown item loot target '{resolvedTarget}' from '{sourceId}'.");
                Debug.LogError($"[LootResolver] Unknown item loot target '{resolvedTarget}' from '{sourceId}'.");
                return;
            }

            drops.Add(new LootDropResult
            {
                dropType = resolvedType,
                targetId = resolvedTarget,
                amount = amount,
                isCurrency = isCurrency,
                granted = false,
                message = "Rolled loot drop."
            });
        }

        private static LootRollResult FailedTable(string lootTableId, string issue)
        {
            Debug.LogError($"[LootResolver] {issue}");
            return new LootRollResult { lootTableId = lootTableId, success = false, issues = new[] { issue } };
        }

        private static LootRollResult FailedEnemyLoot(string lootGroupId, string issue)
        {
            Debug.LogError($"[LootResolver] {issue}");
            return new LootRollResult { lootGroupId = lootGroupId, success = false, issues = new[] { issue } };
        }
    }
}
