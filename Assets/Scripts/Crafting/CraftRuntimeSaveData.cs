using System;

namespace GuildIdle.Crafting
{
    public enum CraftExecutionStatus
    {
        None = 0,
        Running = 1,
        ResultPending = 2
    }

    [Serializable]
    public sealed class CraftRuntimeSaveData
    {
        public CraftExecutionSaveData[] executions = Array.Empty<CraftExecutionSaveData>();
    }

    [Serializable]
    public sealed class CraftExecutionSaveData
    {
        public string executionId;
        public string craftId;
        public string heroId;
        public string stationBuildingId;
        public int stationBuildingLevel;
        public CraftExecutionStatus status = CraftExecutionStatus.Running;
        public float progressSeconds;
        public int durationSeconds;
        public string outputItemId;
        public int outputCount;
        public string skillId;
        public int skillExp;
        public int fatigueCostPaid;
        public CraftRequiredBuildingSnapshotSaveData[] requiredBuildings = Array.Empty<CraftRequiredBuildingSnapshotSaveData>();
        public CraftPaidCostSaveData[] paidCosts = Array.Empty<CraftPaidCostSaveData>();
        public CraftRecipeAuditSaveData recipe = new CraftRecipeAuditSaveData();
        public bool costsPaid;
        public string startOperationKey;
        public string startFingerprint;
        public string pendingResultId;
        public bool completionRecorded;
        public long lastAdvanceSequence;
        public long completionAdvanceSequence;
        public CraftAdvanceReceiptSaveData[] advanceReceipts = Array.Empty<CraftAdvanceReceiptSaveData>();
        public long startedAtUnixSeconds;
    }

    [Serializable]
    public sealed class CraftAdvanceReceiptSaveData
    {
        public long operationSequence;
        public string operationKey;
        public string fingerprint;
        public double deltaSeconds;
        public float progressSeconds;
        public string code;
        public string pendingResultId;
    }

    [Serializable]
    public sealed class CraftRequiredBuildingSnapshotSaveData
    {
        public string buildingId;
        public int level;
    }

    [Serializable]
    public sealed class CraftPaidCostSaveData
    {
        public string itemId;
        public int quantity;
        public string kind;
    }

    [Serializable]
    public sealed class CraftRecipeAuditSaveData
    {
        public string requiredItemId;
        public int requiredCount;
        public bool consume;
        public int consumedCount;
    }
}
