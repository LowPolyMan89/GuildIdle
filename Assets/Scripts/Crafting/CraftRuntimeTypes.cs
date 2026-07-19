using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GuildIdle.Crafting
{
    public static class CraftStartCode
    {
        public const string Available = "Available";
        public const string Applied = "Applied";
        public const string Replayed = "Replayed";
        public const string OperationKeyRequired = "OperationKeyRequired";
        public const string OperationReplayConflict = "OperationReplayConflict";
        public const string UnknownOrDisabledCraft = "UnknownOrDisabledCraft";
        public const string InvalidCraftDescriptor = "InvalidCraftDescriptor";
        public const string CraftUnavailableAtStationLevel = "CraftUnavailableAtStationLevel";
        public const string StationUnavailable = "StationUnavailable";
        public const string AdditionalBuildingUnavailable = "AdditionalBuildingUnavailable";
        public const string HeroNotFound = "HeroNotFound";
        public const string HeroBusy = "HeroBusy";
        public const string ActiveHeroLimitReached = "ActiveHeroLimitReached";
        public const string InsufficientFatigue = "InsufficientFatigue";
        public const string MissingMaterials = "MissingMaterials";
        public const string MissingOrInvalidRecipe = "MissingOrInvalidRecipe";
        public const string ExecutionAlreadyActive = "ExecutionAlreadyActive";
        public const string TransactionFailure = "TransactionFailure";
        public const string SaveFailure = "SaveFailure";
    }

    public static class CraftPaidCostKind
    {
        public const string Material = "Material";
        public const string Recipe = "Recipe";
        public const string MaterialAndRecipe = "MaterialAndRecipe";
    }

    public static class CraftAdvanceCode
    {
        public const string Applied = "Applied";
        public const string Replayed = "Replayed";
        public const string ResultPending = "ResultPending";
        public const string OperationKeyRequired = "OperationKeyRequired";
        public const string OperationSequenceRequired = "OperationSequenceRequired";
        public const string OperationSequenceGap = "OperationSequenceGap";
        public const string OperationReplayConflict = "OperationReplayConflict";
        public const string InvalidDelta = "InvalidDelta";
        public const string ExecutionNotFound = "ExecutionNotFound";
        public const string InvalidExecution = "InvalidExecution";
        public const string DataIntegrityFailure = "DataIntegrityFailure";
        public const string RewardValidationFailure = "RewardValidationFailure";
        public const string PendingResultFailure = "PendingResultFailure";
        public const string TransactionFailure = "TransactionFailure";
        public const string SaveFailure = "SaveFailure";
    }

    public sealed class CraftStartRequest
    {
        public string CraftId { get; set; }
        public string HeroId { get; set; }
        public string StationBuildingId { get; set; }
        public int StationBuildingLevel { get; set; }
        public string OperationKey { get; set; }
    }

    public sealed class CraftCostDescriptor
    {
        public string ItemId { get; }
        public int Quantity { get; }
        public string Kind { get; }

        internal CraftCostDescriptor(string itemId, int quantity, string kind)
        {
            ItemId = itemId ?? string.Empty;
            Quantity = quantity;
            Kind = kind ?? string.Empty;
        }
    }

    public sealed class CraftBuildingRequirementDescriptor
    {
        public string BuildingId { get; }
        public int Level { get; }

        internal CraftBuildingRequirementDescriptor(string buildingId, int level)
        {
            BuildingId = buildingId ?? string.Empty;
            Level = level;
        }
    }

    public sealed class CraftRecipeDescriptor
    {
        public string RequiredItemId { get; }
        public int RequiredCount { get; }
        public bool Consume { get; }

        internal CraftRecipeDescriptor(string requiredItemId, int requiredCount, bool consume)
        {
            RequiredItemId = requiredItemId ?? string.Empty;
            RequiredCount = requiredCount;
            Consume = consume;
        }
    }

    public sealed class CraftStartDescriptor
    {
        public string CraftId { get; }
        public string HeroId { get; }
        public string StationBuildingId { get; }
        public int StationBuildingLevel { get; }
        public int DurationSeconds { get; }
        public string OutputItemId { get; }
        public int OutputCount { get; }
        public string SkillId { get; }
        public int SkillExp { get; }
        public int FatigueCost { get; }
        public IReadOnlyList<CraftBuildingRequirementDescriptor> RequiredBuildings { get; }
        public IReadOnlyList<CraftCostDescriptor> PaidCosts { get; }
        public CraftRecipeDescriptor Recipe { get; }
        public bool CanStart { get; }
        public string BlockCode { get; }
        public string BlockMessage { get; }

        internal CraftStartDescriptor(
            string craftId,
            string heroId,
            string stationBuildingId,
            int stationBuildingLevel,
            int durationSeconds,
            string outputItemId,
            int outputCount,
            string skillId,
            int skillExp,
            int fatigueCost,
            IList<CraftBuildingRequirementDescriptor> requiredBuildings,
            IList<CraftCostDescriptor> paidCosts,
            CraftRecipeDescriptor recipe,
            bool canStart,
            string blockCode,
            string blockMessage)
        {
            CraftId = craftId ?? string.Empty;
            HeroId = heroId ?? string.Empty;
            StationBuildingId = stationBuildingId ?? string.Empty;
            StationBuildingLevel = stationBuildingLevel;
            DurationSeconds = durationSeconds;
            OutputItemId = outputItemId ?? string.Empty;
            OutputCount = outputCount;
            SkillId = skillId ?? string.Empty;
            SkillExp = skillExp;
            FatigueCost = fatigueCost;
            RequiredBuildings = new ReadOnlyCollection<CraftBuildingRequirementDescriptor>(new List<CraftBuildingRequirementDescriptor>(requiredBuildings ?? Array.Empty<CraftBuildingRequirementDescriptor>()));
            PaidCosts = new ReadOnlyCollection<CraftCostDescriptor>(new List<CraftCostDescriptor>(paidCosts ?? Array.Empty<CraftCostDescriptor>()));
            Recipe = recipe ?? new CraftRecipeDescriptor(string.Empty, 0, false);
            CanStart = canStart;
            BlockCode = blockCode ?? (canStart ? CraftStartCode.Available : CraftStartCode.InvalidCraftDescriptor);
            BlockMessage = blockMessage ?? string.Empty;
        }
    }

    public sealed class CraftStartResult
    {
        public bool Success { get; internal set; }
        public bool Replayed { get; internal set; }
        public string Code { get; internal set; }
        public string Message { get; internal set; }
        public string ExecutionId { get; internal set; }
        public CraftStartDescriptor Descriptor { get; internal set; }
        public CraftExecutionSaveData Execution { get; internal set; }
    }

    public sealed class CraftStartedEvent
    {
        public string ExecutionId { get; internal set; }
        public string CraftId { get; internal set; }
        public string HeroId { get; internal set; }
        public string StationBuildingId { get; internal set; }
        public int StationBuildingLevel { get; internal set; }
    }

    public sealed class CraftAdvanceResult
    {
        public bool Success { get; internal set; }
        public bool Replayed { get; internal set; }
        public bool Completed { get; internal set; }
        public string Code { get; internal set; }
        public string Message { get; internal set; }
        public string ExecutionId { get; internal set; }
        public long OperationSequence { get; internal set; }
        public float ProgressSeconds { get; internal set; }
        public string PendingResultId { get; internal set; }
        public CraftExecutionSaveData Execution { get; internal set; }
    }

    public sealed class CraftResultPendingEvent
    {
        public string ExecutionId { get; internal set; }
        public string CraftId { get; internal set; }
        public string HeroId { get; internal set; }
        public string PendingResultId { get; internal set; }
    }
}
