using System;
using GuildIdle.Activities;
using GuildIdle.Core;

namespace GuildIdle.Player
{
    public sealed class PlayerStateActivityAdapter : IActivityPlayerState, IRewardBatchStore
    {
        private readonly PlayerState _state;

        public PlayerStateActivityAdapter(PlayerState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public bool HasHero(string heroId) => _state.HasHero(heroId);
        public bool AddHero(string heroId) => _state.AddHero(heroId);
        public bool HasHeroState(string heroId) => _state.HasHeroState(heroId);
        public int GetHeroFatigue(string heroId) => _state.GetHeroFatigue(heroId);
        public bool SpendHeroFatigue(string heroId, int amount) => _state.SpendHeroFatigue(heroId, amount);
        public int GetHeroSkillLevel(string heroId, string skillId) => _state.GetHeroSkillLevel(heroId, skillId);
        public bool AddHeroSkillExp(string heroId, string skillId, int amount) => _state.AddHeroSkillExp(heroId, skillId, amount);
        public bool IsHeroBusy(string heroId) => _state.IsHeroBusy(heroId);
        public string GetHeroCurrentActivityExecutionId(string heroId) => _state.GetHeroCurrentActivityExecutionId(heroId);
        public int GetItem(string itemId) => _state.GetItem(itemId);
        public bool HasItem(string itemId, int amount) => _state.HasItem(itemId, amount);
        public bool AddItem(string itemId, int amount) => _state.AddItem(itemId, amount);
        public bool SpendItem(string itemId, int amount) => _state.SpendItem(itemId, amount);
        public long GetCurrency(string currencyId) => _state.GetCurrency(currencyId);
        public bool AddCurrency(string currencyId, long amount) => _state.AddCurrency(currencyId, amount);
        public bool SpendCurrency(string currencyId, long amount) => _state.SpendCurrency(currencyId, amount);
        public bool IsBuildingUnlocked(string buildingId) => _state.IsBuildingUnlocked(buildingId);
        public int GetBuildingLevel(string buildingId) => _state.GetBuildingLevel(buildingId);
        public bool UnlockBuilding(string buildingId) => _state.UnlockBuilding(buildingId);
        public bool IsLocationUnlocked(string locationId) => _state.IsLocationUnlocked(locationId);
        public bool UnlockLocation(string locationId) => _state.UnlockLocation(locationId);
        public bool IsActivityCompleted(string activityId) => _state.IsActivityCompleted(activityId);
        public bool CompleteActivity(string activityId) => _state.CompleteActivity(activityId);
        public bool TryApplyRewardBatch(RewardMutation[] mutations, out RewardMutationResult[] results, out string error) =>
            _state.TryApplyRewardBatch(mutations, out results, out error);
    }
}
