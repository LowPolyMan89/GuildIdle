using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Activities
{
    public static class ActivityCostResolver
    {
        public static ActivityCostResult CanPayCost(string activityId)
        {
            return CanPayCost(activityId, ActivityResolverUtilities.DefaultState());
        }

        public static ActivityCostResult CanPayCost(string activityId, IActivityPlayerState state)
        {
            return BuildCostResult(activityId, state, apply: false);
        }

        public static ActivityCostResult ApplyCost(string activityId)
        {
            return ApplyCost(activityId, ActivityResolverUtilities.DefaultState());
        }

        public static ActivityCostResult ApplyCost(string activityId, IActivityPlayerState state)
        {
            var check = BuildCostResult(activityId, state, apply: false);
            if (!check.success)
                return check;

            return BuildCostResult(activityId, state, apply: true);
        }

        private static ActivityCostResult BuildCostResult(string activityId, IActivityPlayerState state, bool apply)
        {
            var issues = new List<ActivityRequirementIssue>();
            var costs = new List<ActivityAppliedCost>();
            var itemCosts = new Dictionary<string, int>(StringComparer.Ordinal);
            var currencyCosts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (!ActivityResolverUtilities.TryGetActivity(activityId, issues, out var activity))
                return Finish(activityId, issues, costs);

            if (activity.fatigueCost > 0)
            {
                costs.Add(new ActivityAppliedCost
                {
                    costType = "Fatigue",
                    targetId = "fatigue",
                    amount = activity.fatigueCost,
                    applied = false,
                    message = "Fatigue is result-only until PlayerState stores fatigue."
                });
            }

            foreach (var requirement in RuntimeConfigs.Activities.GetRequirements(activityId))
            {
                if (requirement == null || !requirement.consume)
                    continue;

                AddConsumableRequirementCost(requirement, issues, itemCosts, currencyCosts);
            }

            AddAggregatedItemCosts(activityId, itemCosts, state, apply, issues, costs);
            AddAggregatedCurrencyCosts(activityId, currencyCosts, state, apply, issues, costs);

            return Finish(activityId, issues, costs);
        }

        private static void AddConsumableRequirementCost(
            ActivityRequirementConfigDto requirement,
            List<ActivityRequirementIssue> issues,
            Dictionary<string, int> itemCosts,
            Dictionary<string, int> currencyCosts)
        {
            var type = requirement.reqType ?? string.Empty;
            var targetId = requirement.targetId;
            var amount = ActivityResolverUtilities.RequirementAmount(requirement.value);

            if (ActivityResolverUtilities.IsAnyItemType(type))
            {
                if (!RuntimeConfigs.Items.TryGet(targetId, out _))
                {
                    AddUnknownCostIssue(requirement, issues);
                    return;
                }

                AddAggregatedCost(itemCosts, targetId, amount);
                return;
            }

            if (string.Equals(type, "Currency", StringComparison.OrdinalIgnoreCase))
            {
                if (!RuntimeConfigs.Items.TryGetCurrency(targetId, out _))
                {
                    AddUnknownCostIssue(requirement, issues);
                    return;
                }

                AddAggregatedCost(currencyCosts, targetId, amount);
                return;
            }

            ActivityResolverUtilities.AddIssue(issues, requirement.activityId, type, targetId, amount, 0, false, true, $"Consumable requirement '{type}' is not implemented as a cost.");
        }

        private static void AddUnknownCostIssue(ActivityRequirementConfigDto requirement, List<ActivityRequirementIssue> issues)
        {
            ActivityResolverUtilities.AddIssue(issues, requirement.activityId, requirement.reqType, requirement.targetId, requirement.value, 0, true, false, $"Unknown consumable cost target id '{requirement.targetId}'.");
        }

        private static void AddAggregatedCost(Dictionary<string, int> costs, string targetId, int amount)
        {
            costs.TryGetValue(targetId, out var current);
            costs[targetId] = current + amount;
        }

        private static void AddAggregatedItemCosts(
            string activityId,
            Dictionary<string, int> itemCosts,
            IActivityPlayerState state,
            bool apply,
            List<ActivityRequirementIssue> issues,
            List<ActivityAppliedCost> costs)
        {
            foreach (var itemCost in itemCosts)
            {
                var current = state.GetItem(itemCost.Key);
                if (current < itemCost.Value)
                {
                    ActivityResolverUtilities.AddIssue(issues, activityId, "Item", itemCost.Key, itemCost.Value, current, false, false, $"Cannot pay Item '{itemCost.Key}': {current}/{itemCost.Value}.");
                    continue;
                }

                var applied = apply && state.SpendItem(itemCost.Key, itemCost.Value);
                costs.Add(new ActivityAppliedCost { costType = "Item", targetId = itemCost.Key, amount = itemCost.Value, applied = applied, message = apply ? "Spent item cost." : "Can spend item cost." });
            }
        }

        private static void AddAggregatedCurrencyCosts(
            string activityId,
            Dictionary<string, int> currencyCosts,
            IActivityPlayerState state,
            bool apply,
            List<ActivityRequirementIssue> issues,
            List<ActivityAppliedCost> costs)
        {
            foreach (var currencyCost in currencyCosts)
            {
                var current = state.GetCurrency(currencyCost.Key);
                if (current < currencyCost.Value)
                {
                    ActivityResolverUtilities.AddIssue(issues, activityId, "Currency", currencyCost.Key, currencyCost.Value, current, false, false, $"Cannot pay Currency '{currencyCost.Key}': {current}/{currencyCost.Value}.");
                    continue;
                }

                var applied = apply && state.SpendCurrency(currencyCost.Key, currencyCost.Value);
                costs.Add(new ActivityAppliedCost { costType = "Currency", targetId = currencyCost.Key, amount = currencyCost.Value, applied = applied, message = apply ? "Spent currency cost." : "Can spend currency cost." });
            }
        }

        private static ActivityCostResult Finish(string activityId, List<ActivityRequirementIssue> issues, List<ActivityAppliedCost> costs)
        {
            var success = issues.Count == 0;

            return new ActivityCostResult
            {
                activityId = activityId,
                success = success,
                issues = issues.ToArray(),
                costs = costs.ToArray()
            };
        }
    }
}
