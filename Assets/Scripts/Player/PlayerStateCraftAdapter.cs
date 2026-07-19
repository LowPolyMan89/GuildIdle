using System;
using GuildIdle.Activities;
using GuildIdle.Crafting;

namespace GuildIdle.Player
{
    public sealed class PlayerStateCraftAdapter : ICraftPlayerState
    {
        private readonly PlayerState _state;
        private readonly PlayerStateActivityAdapter _activityAdapter;
        private readonly StorageService _storage;
        private bool _storageChanged;

        public PlayerStateCraftAdapter(PlayerState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _activityAdapter = new PlayerStateActivityAdapter(state);
            _storage = state.Storage as StorageService ?? throw new InvalidOperationException("PlayerState must use StorageService for craft transactions.");
        }

        public SaveData CaptureCheckpoint()
        {
            _storageChanged = false;
            return _state.ToSaveData();
        }

        public void RestoreCheckpoint(SaveData checkpoint)
        {
            _state.RestoreTransactional(checkpoint);
            _storageChanged = false;
        }
        public bool TryGetOperationReceipt(string aggregateId, string operationId, out OperationReceiptSaveData receipt) =>
            _state.TryGetOperationReceipt(aggregateId, operationId, out receipt);
        public void RecordOperationReceipt(OperationReceiptSaveData receipt) => _state.RecordOperationReceipt(receipt);
        public bool HasHero(string heroId) => _state.HasHero(heroId);
        public bool HasHeroState(string heroId) => _state.HasHeroState(heroId);
        public int GetHeroFatigue(string heroId) => _state.GetHeroFatigue(heroId);
        public bool SpendHeroFatigue(string heroId, int amount) => _state.SpendHeroFatigue(heroId, amount);
        public bool IsHeroBusy(string heroId) => _state.IsHeroBusy(heroId);
        public string GetHeroOccupationOwnerId(string heroId) => _state.GetHeroCurrentActivityExecutionId(heroId);
        public int GetActiveHeroCount() => _state.GetActiveHeroCount();
        public int GetActiveHeroLimit() => ActiveHeroLimitResolver.GetCurrentLimit(_activityAdapter);
        public bool TryOccupyHero(string heroId, string executionId) => _state.SetHeroBusy(heroId, executionId);
        public bool IsBuildingUnlocked(string buildingId) => _state.IsBuildingUnlocked(buildingId);
        public int GetBuildingLevel(string buildingId) => _state.GetBuildingLevel(buildingId);
        public int GetAvailableForCraftCount(string itemId) => _state.Storage.GetAvailableForActionCount(itemId, null);

        public bool TryConsumeCraftCost(string itemId, int quantity, out string error)
        {
            var success = _storage.TryConsumeForCraft(itemId, quantity, out error);
            _storageChanged |= success;
            return success;
        }

        public void PublishCraftStartCommit()
        {
            if (_storageChanged)
                _storage.NotifyExternalMutation();
            _storageChanged = false;
        }

        public CraftExecutionSaveData[] GetCraftExecutions() => _state.GetCraftExecutions();
        public CraftExecutionSaveData GetCraftExecution(string executionId) => _state.GetCraftExecution(executionId);
        public bool AddCraftExecution(CraftExecutionSaveData execution) => _state.AddCraftExecution(execution);
        public bool Save() => _state.Save();
    }
}
