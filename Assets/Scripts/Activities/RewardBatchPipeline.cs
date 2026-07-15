using System;
using System.Collections.Generic;
using GuildIdle.Core;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Activities
{
    internal sealed class RewardDefinition
    {
        public string sourceId;
        public string rewardType;
        public string targetId;
        public int min;
        public int max;
        public float chance = 100f;
        public string grantMoment;
    }

    internal sealed class PreparedRewardBatch
    {
        public bool success;
        public RewardMutation[] mutations = Array.Empty<RewardMutation>();
        public ActivityAppliedReward[] rewards = Array.Empty<ActivityAppliedReward>();
        public ActivityRequirementIssue[] issues = Array.Empty<ActivityRequirementIssue>();
        internal int[] mutationIndexes = Array.Empty<int>();
        internal LootDropResult[] mutationDrops = Array.Empty<LootDropResult>();

        public void ApplyResults(RewardMutationResult[] results)
        {
            results ??= Array.Empty<RewardMutationResult>();
            for (var i = 0; i < mutationDrops.Length && i < results.Length; i++)
            {
                if (mutationDrops[i] != null)
                    mutationDrops[i].granted = results[i].Applied;
            }
            for (var i = 0; i < rewards.Length; i++)
            {
                var mutationIndex = i < mutationIndexes.Length ? mutationIndexes[i] : -1;
                if (mutationIndex >= 0 && mutationIndex < results.Length)
                    rewards[i].applied = results[mutationIndex].Applied;
                else if (!rewards[i].isResultOnly)
                    rewards[i].applied = rewards[i].lootRoll?.success == true;
            }
        }
    }

    internal static class RewardBatchPipeline
    {
        public static PreparedRewardBatch Prepare(
            IEnumerable<RewardDefinition> definitions,
            string grantMoment,
            string heroId,
            IActivityRandom random,
            bool rollLoot)
        {
            random ??= ActivityResolverUtilities.DefaultRandom();
            var issues = new List<ActivityRequirementIssue>();
            var mutations = new List<RewardMutation>();
            var rewards = new List<ActivityAppliedReward>();
            var indexes = new List<int>();
            var drops = new List<LootDropResult>();

            foreach (var definition in definitions ?? Array.Empty<RewardDefinition>())
            {
                if (definition == null || !ActivityResolverUtilities.MomentMatches(definition.grantMoment, grantMoment))
                    continue;
                if (!ActivityResolverUtilities.ChancePassed(definition.chance, random))
                    continue;

                PrepareOne(definition, heroId, random, rollLoot, issues, mutations, rewards, indexes, drops);
            }

            var success = true;
            foreach (var issue in issues)
            {
                if (issue.isError)
                {
                    success = false;
                    break;
                }
            }

            return new PreparedRewardBatch
            {
                success = success,
                mutations = mutations.ToArray(),
                rewards = rewards.ToArray(),
                issues = issues.ToArray(),
                mutationIndexes = indexes.ToArray(),
                mutationDrops = drops.ToArray()
            };
        }

        private static void PrepareOne(
            RewardDefinition definition,
            string heroId,
            IActivityRandom random,
            bool rollLoot,
            List<ActivityRequirementIssue> issues,
            List<RewardMutation> mutations,
            List<ActivityAppliedReward> rewards,
            List<int> indexes,
            List<LootDropResult> drops)
        {
            var type = definition.rewardType ?? string.Empty;
            var targetId = definition.targetId;
            var amount = ActivityResolverUtilities.PositiveAmount(definition.min, definition.max, random);
            if (!ActivityTypeParser.TryParseRewardType(type, out var parsedType))
            {
                AddIssue(issues, definition, $"Unsupported reward type '{type}'.");
                return;
            }

            if (parsedType == RewardTypeEnum.SkillExp)
            {
                if (!ActivityResolverUtilities.IsKnownSkill(targetId))
                {
                    AddIssue(issues, definition, $"Unknown skill id '{targetId}'.");
                    return;
                }
                if (string.IsNullOrWhiteSpace(heroId))
                {
                    AddIssue(issues, definition, "SkillExp reward needs an executor hero context.");
                    return;
                }

                AddMutation(
                    mutations,
                    rewards,
                    indexes,
                    new RewardMutation(RewardMutationKind.HeroSkillExp, targetId, amount, heroId),
                    new ActivityAppliedReward { rewardType = type, targetId = targetId, ownerType = ActivityResolverUtilities.OwnerHero, ownerId = heroId, amount = amount, message = "Prepared hero skill exp reward." },
                    drops);
                return;
            }

            if (parsedType == RewardTypeEnum.LootTable)
            {
                if (!rollLoot)
                {
                    rewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, ownerType = ActivityResolverUtilities.OwnerProfile, isResultOnly = true, message = "LootTable is not rolled during preview." });
                    indexes.Add(-1);
                    return;
                }

                var roll = LootResolver.RollLootTable(targetId, random);
                rewards.Add(new ActivityAppliedReward { rewardType = type, targetId = targetId, ownerType = ActivityResolverUtilities.OwnerProfile, lootRoll = roll, message = roll.success ? "Prepared loot table." : "Failed to roll loot table." });
                indexes.Add(-1);
                foreach (var issue in roll.issues)
                    AddIssue(issues, definition, issue);
                foreach (var drop in roll.drops)
                {
                    var kind = drop.isCurrency ? RewardMutationKind.Currency : RewardMutationKind.Item;
                    AddMutation(
                        mutations,
                        rewards,
                        indexes,
                        new RewardMutation(kind, drop.targetId, drop.amount),
                        new ActivityAppliedReward { rewardType = drop.dropType, targetId = drop.targetId, ownerType = ActivityResolverUtilities.OwnerProfile, amount = drop.amount, isCurrency = drop.isCurrency, message = "Prepared loot drop." },
                        drops,
                        drop);
                }
                return;
            }

            if (parsedType == RewardTypeEnum.Gold || parsedType == RewardTypeEnum.Currency)
            {
                var currencyId = parsedType == RewardTypeEnum.Gold ? ActivityResolverUtilities.GoldCurrencyId : targetId;
                if (!RuntimeConfigs.Items.TryGetCurrency(currencyId, out _))
                {
                    AddIssue(issues, definition, $"Unknown currency id '{currencyId}'.");
                    return;
                }
                AddMutation(
                    mutations,
                    rewards,
                    indexes,
                    new RewardMutation(RewardMutationKind.Currency, currencyId, amount),
                    new ActivityAppliedReward { rewardType = parsedType == RewardTypeEnum.Gold ? RewardType.Gold : RewardType.Currency, targetId = currencyId, ownerType = ActivityResolverUtilities.OwnerProfile, amount = amount, isCurrency = true, message = "Prepared currency reward." },
                    drops);
                return;
            }

            if (parsedType == RewardTypeEnum.Hero)
            {
                if (!RuntimeConfigs.Heroes.TryGet(targetId, out _))
                {
                    AddIssue(issues, definition, $"Unknown hero id '{targetId}'.");
                    return;
                }
                AddMutation(mutations, rewards, indexes, new RewardMutation(RewardMutationKind.Hero, targetId, 1), new ActivityAppliedReward { rewardType = type, targetId = targetId, ownerType = ActivityResolverUtilities.OwnerProfile, amount = 1, message = "Prepared hero reward." }, drops);
                return;
            }

            if (parsedType == RewardTypeEnum.UnlockBuilding)
            {
                if (!RuntimeConfigs.Buildings.TryGet(targetId, out _))
                {
                    AddIssue(issues, definition, $"Unknown building id '{targetId}'.");
                    return;
                }
                AddMutation(mutations, rewards, indexes, new RewardMutation(RewardMutationKind.UnlockBuilding, targetId, 1), new ActivityAppliedReward { rewardType = type, targetId = targetId, ownerType = ActivityResolverUtilities.OwnerProfile, amount = 1, message = "Prepared building unlock." }, drops);
                return;
            }

            if (parsedType == RewardTypeEnum.UnlockLocation)
            {
                if (!RuntimeConfigs.Map.TryGetLocation(targetId, out _))
                {
                    AddIssue(issues, definition, $"Unknown location id '{targetId}'.");
                    return;
                }
                AddMutation(mutations, rewards, indexes, new RewardMutation(RewardMutationKind.UnlockLocation, targetId, 1), new ActivityAppliedReward { rewardType = type, targetId = targetId, ownerType = ActivityResolverUtilities.OwnerProfile, amount = 1, message = "Prepared location unlock." }, drops);
                return;
            }

            if (IsItemReward(parsedType))
            {
                if (!TryValidateItem(parsedType, targetId))
                {
                    AddIssue(issues, definition, $"Unknown {type} reward target id '{targetId}'.");
                    return;
                }
                AddMutation(mutations, rewards, indexes, new RewardMutation(RewardMutationKind.Item, targetId, amount), new ActivityAppliedReward { rewardType = type, targetId = targetId, ownerType = ActivityResolverUtilities.OwnerProfile, amount = amount, message = "Prepared item reward." }, drops);
                return;
            }

            if (parsedType == RewardTypeEnum.HeroExp || parsedType == RewardTypeEnum.Reputation)
            {
                AddIssue(issues, definition, $"Reward type '{type}' is recognized but not implemented in PlayerState yet.", true);
                return;
            }

            AddIssue(issues, definition, $"Unsupported reward type '{type}'.");
        }

        private static void AddMutation(
            List<RewardMutation> mutations,
            List<ActivityAppliedReward> rewards,
            List<int> indexes,
            RewardMutation mutation,
            ActivityAppliedReward reward,
            List<LootDropResult> drops,
            LootDropResult drop = null)
        {
            indexes.Add(mutations.Count);
            mutations.Add(mutation);
            drops.Add(drop);
            rewards.Add(reward);
        }

        private static bool IsItemReward(RewardTypeEnum type) =>
            type == RewardTypeEnum.Resource || type == RewardTypeEnum.Equipment ||
            type == RewardTypeEnum.Consumable || type == RewardTypeEnum.Recipe ||
            type == RewardTypeEnum.Item;

        private static bool TryValidateItem(RewardTypeEnum type, string targetId)
        {
            switch (type)
            {
                case RewardTypeEnum.Resource:
                    return RuntimeConfigs.Items.TryGetResource(targetId, out _);
                case RewardTypeEnum.Equipment:
                    return RuntimeConfigs.Items.TryGetEquipmentWeapon(targetId, out _) || RuntimeConfigs.Items.TryGetEquipmentArmor(targetId, out _);
                case RewardTypeEnum.Consumable:
                    return RuntimeConfigs.Items.TryGetConsumable(targetId, out _);
                case RewardTypeEnum.Recipe:
                    return RuntimeConfigs.Items.TryGetRecipe(targetId, out _);
                case RewardTypeEnum.Item:
                    return RuntimeConfigs.Items.TryGet(targetId, out _);
                default:
                    return false;
            }
        }

        private static void AddIssue(List<ActivityRequirementIssue> issues, RewardDefinition definition, string message, bool notImplemented = false)
        {
            ActivityResolverUtilities.AddIssue(issues, definition.sourceId, definition.rewardType, definition.targetId, 0, 0, true, notImplemented, message);
        }
    }
}
