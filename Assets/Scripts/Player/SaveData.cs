using System;
using GuildIdle.Core;

namespace GuildIdle.Player
{
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentSaveVersion = 7;

        public int saveVersion = CurrentSaveVersion;
        public string currentStageId;
        public CurrencySaveEntry[] currencies = Array.Empty<CurrencySaveEntry>();
        public long storageRevision;
        public ItemStackSaveData[] itemStacks = Array.Empty<ItemStackSaveData>();
        public ItemInstanceSaveData[] itemInstances = Array.Empty<ItemInstanceSaveData>();
        public EquipmentSlotSaveData[] equipmentSlots = Array.Empty<EquipmentSlotSaveData>();
        public string[] unlockedHeroes = Array.Empty<string>();
        public string[] acquiredHeroes = Array.Empty<string>();
        public HeroSaveData[] heroes = Array.Empty<HeroSaveData>();
        public QuestInstanceSaveData[] questInstances = Array.Empty<QuestInstanceSaveData>();
        public string[] unlockedBuildings = Array.Empty<string>();
        public BuildingLevelSaveEntry[] buildingLevels = Array.Empty<BuildingLevelSaveEntry>();
        public string[] unlockedLocations = Array.Empty<string>();
        public string[] completedActivities = Array.Empty<string>();
        public string[] availableActivities = Array.Empty<string>();
        public ActivityRuntimeSaveData activityRuntime = new ActivityRuntimeSaveData();
        public PendingResultSaveData[] pendingResults = Array.Empty<PendingResultSaveData>();
        public PendingResultSourceReferenceSaveData[] resultSources = Array.Empty<PendingResultSourceReferenceSaveData>();
        public OperationReceiptSaveData[] operationReceipts = Array.Empty<OperationReceiptSaveData>();
    }

    [Serializable]
    public sealed class CurrencySaveEntry
    {
        public string currencyId;
        public long amount;
    }

    [Serializable]
    public sealed class ItemStackSaveData
    {
        public string stackId;
        public string itemId;
        public int quantity;
        public string stateId;
        public string ownerType;
        public string ownerId;
        public string contextType;
        public string contextId;
    }

    [Serializable]
    public sealed class ItemInstanceSaveData
    {
        public string instanceId;
        public string itemId;
        public int quality;
        public string stateId;
        public string ownerType;
        public string ownerId;
        public string contextType;
        public string contextId;
    }

    [Serializable]
    public sealed class EquipmentSlotSaveData
    {
        public string heroId;
        public string equipmentSlot;
        public string itemInstanceId;
    }

    [Serializable]
    public sealed class HeroSaveData
    {
        public string heroId;
        public int level;
        public long exp;
        public int fatigue;
        public int maxFatigue;
        public string currentActivityExecutionId;
        public HeroSkillSaveData[] skills = Array.Empty<HeroSkillSaveData>();
        public HeroEffectCounterSaveData[] effectCounters = Array.Empty<HeroEffectCounterSaveData>();
    }

    [Serializable]
    public sealed class HeroSkillSaveData
    {
        public string skillId;
        public int level;
        public long exp;
    }

    [Serializable]
    public sealed class HeroEffectCounterSaveData
    {
        public string effectId;
        public long value;
    }

    [Serializable]
    public static class QuestInstanceStatus
    {
        public const string Active = "Active";
        public const string RewardPending = "RewardPending";
        public const string Completed = "Completed";
        public const string Expired = "Expired";

        public static bool IsValid(string value) =>
            string.Equals(value, Active, StringComparison.Ordinal) ||
            string.Equals(value, RewardPending, StringComparison.Ordinal) ||
            string.Equals(value, Completed, StringComparison.Ordinal) ||
            string.Equals(value, Expired, StringComparison.Ordinal);
    }

    [Serializable]
    public sealed class QuestInstanceSaveData
    {
        public string instanceId;
        public string questId;
        public string cycleId;
        public string status = QuestInstanceStatus.Active;
        public bool rewardsGranted;
        public string pendingResultId;
        public QuestStepSaveData[] steps = Array.Empty<QuestStepSaveData>();
    }

    [Serializable]
    public sealed class QuestStepSaveData
    {
        public string stepId;
        public int currentValue;
        public bool completed;
    }

    [Serializable]
    public sealed class BuildingLevelSaveEntry
    {
        public string buildingId;
        public int level;
    }

    public static class PendingResultState
    {
        public const string ResultPending = "ResultPending";
    }

    public static class PendingResultSourceType
    {
        public const string Activity = "Activity";
        public const string Combat = "Combat";
        public const string Craft = "Craft";
        public const string Quest = "Quest";
    }

    [Serializable]
    public sealed class PendingResultSaveData
    {
        public string resultId;
        public string sourceType;
        public string sourceId;
        public string sourceExecutionId;
        public string ownerHeroId;
        public string state = PendingResultState.ResultPending;
        public long revision;
        public PendingResultEntrySaveData[] entries = Array.Empty<PendingResultEntrySaveData>();
    }

    [Serializable]
    public sealed class PendingResultEntrySaveData
    {
        public string entryId;
        public int sortOrder;
        public string rewardType;
        public string targetId;
        public long quantity;
        public string origin;
        public int quality;
    }

    public static class PendingResultSourceState
    {
        public const string Pending = "Pending";
        public const string Resolved = "Resolved";
    }

    [Serializable]
    public sealed class PendingResultSourceReferenceSaveData
    {
        public string sourceType;
        public string sourceId;
        public string sourceExecutionId;
        public string resultId;
        public string state = PendingResultSourceState.Pending;
    }

    [Serializable]
    public sealed class OperationReceiptSaveData
    {
        public string aggregateId;
        public string operationId;
        public string fingerprint;
        public bool success;
        public string code;
        public long storageRevision;
        public long resultRevision;
        public string stackId;
        public string instanceId;
        public int quantity;
        public bool resolved;
    }
}
