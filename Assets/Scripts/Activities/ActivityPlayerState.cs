using GuildIdle.Core;

namespace GuildIdle.Activities
{
    public interface IActivityPlayerState : IRewardBatchStore
    {
        GuildIdle.Player.IPendingResultService PendingResults { get; }
        GuildIdle.Player.IStorageService Storage { get; }
        GuildIdle.Player.SaveData CaptureCheckpoint();
        void RestoreCheckpoint(GuildIdle.Player.SaveData checkpoint);
        bool HasHero(string heroId);
        bool AddHero(string heroId);
        bool HasHeroState(string heroId);
        int GetHeroFatigue(string heroId);
        bool SpendHeroFatigue(string heroId, int amount);
        int GetHeroLevel(string heroId);
        int GetHeroStat(string heroId, string statId);
        int GetHeroSkillLevel(string heroId, string skillId);
        bool AddHeroSkillExp(string heroId, string skillId, int amount);
        long GetHeroEffectCounter(string heroId, string effectId);
        bool SetHeroEffectCounter(string heroId, string effectId, long value);
        bool IsHeroBusy(string heroId);
        string GetHeroCurrentActivityExecutionId(string heroId);
        int GetItem(string itemId);
        int GetAvailableForActionCount(string itemId, GuildIdle.Player.StorageActionContext actionContext);
        bool HasItem(string itemId, int amount);
        bool AddItem(string itemId, int amount);
        bool SpendItem(string itemId, int amount);
        bool SpendItem(string itemId, int amount, GuildIdle.Player.StorageActionContext actionContext);
        long GetCurrency(string currencyId);
        bool AddCurrency(string currencyId, long amount);
        bool SpendCurrency(string currencyId, long amount);
        bool IsBuildingUnlocked(string buildingId);
        int GetBuildingLevel(string buildingId);
        bool SetBuildingLevel(string buildingId, int level);
        bool UnlockBuilding(string buildingId);
        bool IsLocationUnlocked(string locationId);
        bool UnlockLocation(string locationId);
        bool IsActivityCompleted(string activityId);
        bool CompleteActivity(string activityId);
    }

}
