using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using GuildIdle.Core;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Activities
{
    public static class ActivityCostResolver
    {
        public static ActivityCostResult CanPayCost(ActivityExecutionContext context, IActivityPlayerState state)
        {
            return BuildCostResult(context, state, apply: false);
        }

        public static ActivityCostResult ApplyCost(ActivityExecutionContext context, IActivityPlayerState state)
        {
            var check = BuildCostResult(context, state, apply: false);
            if (!check.success)
                return check;

            return BuildCostResult(context, state, apply: true);
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
                    ownerType = ActivityResolverUtilities.OwnerHero,
                    amount = activity.fatigueCost,
                    applied = false,
                    message = "Fatigue requires ActivityExecutionContext to resolve the executor hero."
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

        private static ActivityCostResult MissingContextApplyResult(string activityId)
        {
            var issues = new List<ActivityRequirementIssue>();
            ActivityResolverUtilities.AddIssue(issues, activityId, "ActivityExecutionContext", string.Empty, 1, 0, true, false, "ApplyCost requires ActivityExecutionContext.");
            return Finish(activityId, issues, new List<ActivityAppliedCost>());
        }

        private static ActivityCostResult BuildCostResult(ActivityExecutionContext context, IActivityPlayerState state, bool apply)
        {
            var issues = new List<ActivityRequirementIssue>();
            var costs = new List<ActivityAppliedCost>();
            var itemCosts = new Dictionary<string, int>(StringComparer.Ordinal);
            var currencyCosts = new Dictionary<string, int>(StringComparer.Ordinal);
            var activityId = context?.activityId;
            if (!ActivityResolverUtilities.TryGetActivity(activityId, issues, out var activity))
                return Finish(activityId, issues, costs);

            if (!ActivityResolverUtilities.ValidateExecutionContext(context, state, issues))
                return Finish(activityId, issues, costs);

            if (activity.fatigueCost > 0)
                AddFatigueCost(context, state, activity.fatigueCost, apply, issues, costs);

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

        private static void AddFatigueCost(
            ActivityExecutionContext context,
            IActivityPlayerState state,
            int amount,
            bool apply,
            List<ActivityRequirementIssue> issues,
            List<ActivityAppliedCost> costs)
        {
            var current = state.GetHeroFatigue(context.heroId);
            if (current < amount)
            {
                ActivityResolverUtilities.AddIssue(issues, context.activityId, "Fatigue", context.heroId, amount, current, false, false, $"Cannot pay Fatigue for hero '{context.heroId}': {current}/{amount}.");
                return;
            }

            var applied = apply && state.SpendHeroFatigue(context.heroId, amount);
            costs.Add(new ActivityAppliedCost
            {
                costType = "Fatigue",
                targetId = "fatigue",
                ownerType = ActivityResolverUtilities.OwnerHero,
                ownerId = context.heroId,
                amount = amount,
                applied = applied,
                message = apply ? "Spent hero fatigue cost." : "Can spend hero fatigue cost."
            });
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

            if (!RequirementType.TryParse(type, out var parsedType))
            {
                ActivityResolverUtilities.AddIssue(issues, requirement.activityId, type, targetId, amount, 0, true, false, $"Unsupported consumable requirement type '{type}'.");
                return;
            }

            switch (parsedType)
            {
                case RequirementTypeEnum.Resource:
                    if (!RuntimeConfigs.Items.TryGetResource(targetId, out _))
                    {
                        AddUnknownCostIssue(requirement, issues);
                        return;
                    }

                    AddAggregatedCost(itemCosts, targetId, amount);
                    return;

                case RequirementTypeEnum.Item:
                case RequirementTypeEnum.ItemCount:
                    if (!RuntimeConfigs.Items.TryGet(targetId, out _))
                    {
                        AddUnknownCostIssue(requirement, issues);
                        return;
                    }

                    AddAggregatedCost(itemCosts, targetId, amount);
                    return;

                case RequirementTypeEnum.Currency:
                    if (!RuntimeConfigs.Items.TryGetCurrency(targetId, out _))
                    {
                        AddUnknownCostIssue(requirement, issues);
                        return;
                    }

                    AddAggregatedCost(currencyCosts, targetId, amount);
                    return;

                default:
                    ActivityResolverUtilities.AddIssue(issues, requirement.activityId, type, targetId, amount, 0, false, true, $"Consumable requirement '{type}' is not implemented as a cost.");
                    return;
            }
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
                costs.Add(new ActivityAppliedCost { costType = "Item", targetId = itemCost.Key, ownerType = ActivityResolverUtilities.OwnerProfile, amount = itemCost.Value, applied = applied, message = apply ? "Spent item cost." : "Can spend item cost." });
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
                costs.Add(new ActivityAppliedCost { costType = "Currency", targetId = currencyCost.Key, ownerType = ActivityResolverUtilities.OwnerProfile, amount = currencyCost.Value, applied = applied, message = apply ? "Spent currency cost." : "Can spend currency cost." });
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
