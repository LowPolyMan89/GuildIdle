using GuildIdle.Core;

namespace GuildIdle.Activities
{
    public interface IActivityPlayerState : IRewardBatchStore
    {
        bool HasHero(string heroId);
        bool AddHero(string heroId);
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

}
