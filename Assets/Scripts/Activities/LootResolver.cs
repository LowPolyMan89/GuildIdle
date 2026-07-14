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

                AddEnemyDrop(row.lootId, amount, drops, issues);
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
            if (!ActivityTypeParser.TryParseLootRollMode(rollMode, out var parsedRollMode))
            {
                issues.Add($"Unsupported roll mode '{rollMode}' in loot table '{lootTableId}' group '{rollGroup}'.");
                return;
            }

            if (parsedRollMode == LootRollModeEnum.GuaranteedAll)
            {
                foreach (var entry in entries)
                {
                    if (entry != null && ActivityResolverUtilities.ChancePassed(entry.chance, random))
                        AddDrop(entry.dropType, entry.targetId, ActivityResolverUtilities.PositiveAmount(entry.min, entry.max, random), entry.entryId, drops, issues);
                }

                return;
            }

            var rollCount = ActivityResolverUtilities.PositiveAmount(rollCountMin, rollCountMax, random);
            for (var i = 0; i < rollCount; i++)
            {
                var selected = SelectWeighted(entries, random);
                if (selected == null)
                    continue;

                AddDrop(selected.dropType, selected.targetId, ActivityResolverUtilities.PositiveAmount(selected.min, selected.max, random), selected.entryId, drops, issues);

                if (parsedRollMode == LootRollModeEnum.WeightedOne)
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
            if (!DropType.TryParse(dropType, out var parsedType))
            {
                AddDropIssue(issues, $"Unsupported drop type '{dropType}' from '{sourceId}'.");
                return;
            }

            switch (parsedType)
            {
                case DropTypeEnum.Gold:
                    if (!string.Equals(targetId, ActivityResolverUtilities.GoldCurrencyId, StringComparison.OrdinalIgnoreCase) ||
                        !RuntimeConfigs.Items.TryGetCurrency(ActivityResolverUtilities.GoldCurrencyId, out _))
                    {
                        AddDropIssue(issues, $"Unknown Gold loot target '{targetId}' from '{sourceId}'. Expected '{ActivityResolverUtilities.GoldCurrencyId}'.");
                        return;
                    }

                    AddResolvedDrop(DropType.Gold, ActivityResolverUtilities.GoldCurrencyId, amount, true, drops);
                    return;

                case DropTypeEnum.Resource:
                    if (!RuntimeConfigs.Items.TryGetResource(targetId, out _))
                    {
                        AddDropIssue(issues, $"Unknown Resource loot target '{targetId}' from '{sourceId}'.");
                        return;
                    }

                    AddResolvedDrop(DropType.Resource, targetId, amount, false, drops);
                    return;

                case DropTypeEnum.Item:
                    if (!RuntimeConfigs.Items.TryGet(targetId, out _))
                    {
                        AddDropIssue(issues, $"Unknown Item loot target '{targetId}' from '{sourceId}'.");
                        return;
                    }

                    AddResolvedDrop(DropType.Item, targetId, amount, false, drops);
                    return;

                default:
                    AddDropIssue(issues, $"Unsupported drop type '{dropType}' from '{sourceId}'.");
                    return;
            }
        }

        private static void AddEnemyDrop(string targetId, int amount, List<LootDropResult> drops, List<string> issues)
        {
            if (RuntimeConfigs.Items.TryGetCurrency(targetId, out _))
            {
                var type = string.Equals(targetId, ActivityResolverUtilities.GoldCurrencyId, StringComparison.OrdinalIgnoreCase)
                    ? DropType.Gold
                    : "Currency";
                AddResolvedDrop(type, targetId, amount, true, drops);
                return;
            }

            if (!RuntimeConfigs.Items.TryGet(targetId, out _))
            {
                AddDropIssue(issues, $"Unknown enemy loot target '{targetId}'.");
                return;
            }

            AddResolvedDrop(DropType.Item, targetId, amount, false, drops);
        }

        private static void AddResolvedDrop(string dropType, string targetId, int amount, bool isCurrency, List<LootDropResult> drops)
        {
            drops.Add(new LootDropResult
            {
                dropType = dropType,
                targetId = targetId,
                amount = amount,
                isCurrency = isCurrency,
                granted = false,
                message = "Rolled loot drop."
            });
        }

        private static void AddDropIssue(List<string> issues, string issue)
        {
            issues.Add(issue);
            Debug.LogError($"[LootResolver] {issue}");
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
