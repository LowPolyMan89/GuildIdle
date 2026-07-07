using GuildIdle.Player;

namespace GuildIdle.Activities
{
    public interface IActivityPlayerState
    {
        bool HasHero(string heroId);
        bool AddHero(string heroId);
        string GetHeroInSlot(int slotIndex);
        int GetHeroSlotIndex(string heroId);
        bool HasHeroState(string heroId);
        int GetHeroFatigue(string heroId);
        bool SpendHeroFatigue(string heroId, int amount);
        int GetHeroSkillLevel(string heroId, string skillId);
        bool AddHeroSkillExp(string heroId, string skillId, int amount);
        bool IsHeroBusy(string heroId);
        string GetHeroCurrentActivityExecutionId(string heroId);
        int GetItem(string itemId);
        bool HasItem(string itemId, int amount);
        bool AddItem(string itemId, int amount);
        bool SpendItem(string itemId, int amount);
        long GetCurrency(string currencyId);
        bool AddCurrency(string currencyId, long amount);
        bool SpendCurrency(string currencyId, long amount);
        bool IsBuildingUnlocked(string buildingId);
        int GetBuildingLevel(string buildingId);
        bool UnlockBuilding(string buildingId);
        bool IsLocationUnlocked(string locationId);
        bool UnlockLocation(string locationId);
        bool IsActivityCompleted(string activityId);
        bool CompleteActivity(string activityId);
    }

    public sealed class PlayerStateActivityAdapter : IActivityPlayerState
    {
        private readonly PlayerState _state;

        public PlayerStateActivityAdapter(PlayerState state)
        {
            _state = state;
        }

        public bool HasHero(string heroId) => _state != null && _state.HasHero(heroId);
        public bool AddHero(string heroId) => _state != null && _state.AddHero(heroId);
        public string GetHeroInSlot(int slotIndex) => _state != null ? _state.GetHeroInSlot(slotIndex) : null;
        public int GetHeroSlotIndex(string heroId) => _state != null ? _state.GetHeroSlotIndex(heroId) : -1;
        public bool HasHeroState(string heroId) => _state != null && _state.HasHeroState(heroId);
        public int GetHeroFatigue(string heroId) => _state != null ? _state.GetHeroFatigue(heroId) : 0;
        public bool SpendHeroFatigue(string heroId, int amount) => _state != null && _state.SpendHeroFatigue(heroId, amount);
        public int GetHeroSkillLevel(string heroId, string skillId) => _state != null ? _state.GetHeroSkillLevel(heroId, skillId) : 0;
        public bool AddHeroSkillExp(string heroId, string skillId, int amount) => _state != null && _state.AddHeroSkillExp(heroId, skillId, amount);
        public bool IsHeroBusy(string heroId) => _state != null && _state.IsHeroBusy(heroId);
        public string GetHeroCurrentActivityExecutionId(string heroId) => _state != null ? _state.GetHeroCurrentActivityExecutionId(heroId) : null;
        public int GetItem(string itemId) => _state != null ? _state.GetItem(itemId) : 0;
        public bool HasItem(string itemId, int amount) => _state != null && _state.HasItem(itemId, amount);
        public bool AddItem(string itemId, int amount) => _state != null && _state.AddItem(itemId, amount);
        public bool SpendItem(string itemId, int amount) => _state != null && _state.SpendItem(itemId, amount);
        public long GetCurrency(string currencyId) => _state != null ? _state.GetCurrency(currencyId) : 0L;
        public bool AddCurrency(string currencyId, long amount) => _state != null && _state.AddCurrency(currencyId, amount);
        public bool SpendCurrency(string currencyId, long amount) => _state != null && _state.SpendCurrency(currencyId, amount);
        public bool IsBuildingUnlocked(string buildingId) => _state != null && _state.IsBuildingUnlocked(buildingId);
        public int GetBuildingLevel(string buildingId) => _state != null ? _state.GetBuildingLevel(buildingId) : 0;
        public bool UnlockBuilding(string buildingId) => _state != null && _state.UnlockBuilding(buildingId);
        public bool IsLocationUnlocked(string locationId) => _state != null && _state.IsLocationUnlocked(locationId);
        public bool UnlockLocation(string locationId) => _state != null && _state.UnlockLocation(locationId);
        public bool IsActivityCompleted(string activityId) => _state != null && _state.IsActivityCompleted(activityId);
        public bool CompleteActivity(string activityId) => _state != null && _state.CompleteActivity(activityId);
    }

    public sealed class PlayerActivityAdapter : IActivityPlayerState
    {
        public bool HasHero(string heroId) => global::GuildIdle.Player.Player.HasHero(heroId);
        public bool AddHero(string heroId) => global::GuildIdle.Player.Player.AddHero(heroId);
        public string GetHeroInSlot(int slotIndex) => global::GuildIdle.Player.Player.GetHeroInSlot(slotIndex);
        public int GetHeroSlotIndex(string heroId) => global::GuildIdle.Player.Player.GetHeroSlotIndex(heroId);
        public bool HasHeroState(string heroId) => global::GuildIdle.Player.Player.HasHeroState(heroId);
        public int GetHeroFatigue(string heroId) => global::GuildIdle.Player.Player.GetHeroFatigue(heroId);
        public bool SpendHeroFatigue(string heroId, int amount) => global::GuildIdle.Player.Player.SpendHeroFatigue(heroId, amount);
        public int GetHeroSkillLevel(string heroId, string skillId) => global::GuildIdle.Player.Player.GetHeroSkillLevel(heroId, skillId);
        public bool AddHeroSkillExp(string heroId, string skillId, int amount) => global::GuildIdle.Player.Player.AddHeroSkillExp(heroId, skillId, amount);
        public bool IsHeroBusy(string heroId) => global::GuildIdle.Player.Player.IsHeroBusy(heroId);
        public string GetHeroCurrentActivityExecutionId(string heroId) => global::GuildIdle.Player.Player.GetHeroCurrentActivityExecutionId(heroId);
        public int GetItem(string itemId) => global::GuildIdle.Player.Player.GetItem(itemId);
        public bool HasItem(string itemId, int amount) => global::GuildIdle.Player.Player.HasItem(itemId, amount);
        public bool AddItem(string itemId, int amount) => global::GuildIdle.Player.Player.AddItem(itemId, amount);
        public bool SpendItem(string itemId, int amount) => global::GuildIdle.Player.Player.SpendItem(itemId, amount);
        public long GetCurrency(string currencyId) => global::GuildIdle.Player.Player.GetCurrency(currencyId);
        public bool AddCurrency(string currencyId, long amount) => global::GuildIdle.Player.Player.AddCurrency(currencyId, amount);
        public bool SpendCurrency(string currencyId, long amount) => global::GuildIdle.Player.Player.SpendCurrency(currencyId, amount);
        public bool IsBuildingUnlocked(string buildingId) => global::GuildIdle.Player.Player.IsBuildingUnlocked(buildingId);
        public int GetBuildingLevel(string buildingId) => global::GuildIdle.Player.Player.GetBuildingLevel(buildingId);
        public bool UnlockBuilding(string buildingId) => global::GuildIdle.Player.Player.UnlockBuilding(buildingId);
        public bool IsLocationUnlocked(string locationId) => global::GuildIdle.Player.Player.IsLocationUnlocked(locationId);
        public bool UnlockLocation(string locationId) => global::GuildIdle.Player.Player.UnlockLocation(locationId);
        public bool IsActivityCompleted(string activityId) => global::GuildIdle.Player.Player.IsActivityCompleted(activityId);
        public bool CompleteActivity(string activityId) => global::GuildIdle.Player.Player.CompleteActivity(activityId);
    }
}
