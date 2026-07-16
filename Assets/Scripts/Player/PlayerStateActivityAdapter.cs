using System;
using GuildIdle.Activities;
using GuildIdle.Core;

namespace GuildIdle.Player
{
    public sealed class PlayerStateActivityAdapter : IActivityPlayerState
    {
        private readonly PlayerState _state;

        public PlayerStateActivityAdapter(PlayerState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public IPendingResultService PendingResults => _state.PendingResults;
        public IStorageService Storage => _state.Storage;
        public SaveData CaptureCheckpoint() => _state.ToSaveData();
        public void RestoreCheckpoint(SaveData checkpoint) => _state.RestoreTransactional(checkpoint);
        public bool HasHero(string heroId) => _state.HasHero(heroId);
        public bool AddHero(string heroId) => _state.AddHero(heroId);
        public bool HasHeroState(string heroId) => _state.HasHeroState(heroId);
        public int GetHeroFatigue(string heroId) => _state.GetHeroFatigue(heroId);
        public bool SpendHeroFatigue(string heroId, int amount) => _state.SpendHeroFatigue(heroId, amount);
        public int GetHeroLevel(string heroId) => _state.GetHeroState(heroId)?.level ?? 0;
        public int GetHeroStat(string heroId, string statId) => _state.CalculateHeroStat(heroId, statId);
        public int GetHeroSkillLevel(string heroId, string skillId) => _state.GetHeroSkillLevel(heroId, skillId);
        public bool AddHeroSkillExp(string heroId, string skillId, int amount) => _state.AddHeroSkillExp(heroId, skillId, amount);
        public long GetHeroEffectCounter(string heroId, string effectId) => _state.GetHeroEffectCounter(heroId, effectId);
        public bool SetHeroEffectCounter(string heroId, string effectId, long value) => _state.SetHeroEffectCounter(heroId, effectId, value);
        public bool IsHeroBusy(string heroId) => _state.IsHeroBusy(heroId);
        public string GetHeroCurrentActivityExecutionId(string heroId) => _state.GetHeroCurrentActivityExecutionId(heroId);
        public int GetItem(string itemId) => _state.GetItem(itemId);
        public int GetAvailableForActionCount(string itemId, StorageActionContext actionContext) => _state.GetAvailableForActionCount(itemId, actionContext);
        public bool HasItem(string itemId, int amount) => _state.HasItem(itemId, amount);
        public bool AddItem(string itemId, int amount) => _state.AddItem(itemId, amount);
        public bool SpendItem(string itemId, int amount) => _state.SpendItem(itemId, amount);
        public bool SpendItem(string itemId, int amount, StorageActionContext actionContext) =>
            _state.Storage.Consume($"activity-cost:{actionContext?.ContextId}:{itemId}:{Guid.NewGuid():N}", _state.Storage.GetSnapshot().Revision, itemId, amount, actionContext).Success;
        public long GetCurrency(string currencyId) => _state.GetCurrency(currencyId);
        public bool AddCurrency(string currencyId, long amount) => _state.AddCurrency(currencyId, amount);
        public bool SpendCurrency(string currencyId, long amount) => _state.SpendCurrency(currencyId, amount);
        public bool IsBuildingUnlocked(string buildingId) => _state.IsBuildingUnlocked(buildingId);
        public int GetBuildingLevel(string buildingId) => _state.GetBuildingLevel(buildingId);
        public bool SetBuildingLevel(string buildingId, int level) => _state.SetBuildingLevel(buildingId, level);
        public bool UnlockBuilding(string buildingId) => _state.UnlockBuilding(buildingId);
        public bool IsLocationUnlocked(string locationId) => _state.IsLocationUnlocked(locationId);
        public bool UnlockLocation(string locationId) => _state.UnlockLocation(locationId);
        public bool IsActivityCompleted(string activityId) => _state.IsActivityCompleted(activityId);
        public bool CompleteActivity(string activityId) => _state.CompleteActivity(activityId);
        public bool TryApplyRewardBatch(RewardMutation[] mutations, out RewardMutationResult[] results, out string error) =>
            _state.TryApplyRewardBatch(mutations, out results, out error);
    }
}
