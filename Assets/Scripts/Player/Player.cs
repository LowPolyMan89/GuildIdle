using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Player
{
    public static class Player
    {
        private static PlayerState _state;

        public static bool IsLoaded => _state != null && RuntimeConfigs.IsLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            RuntimeConfigs.OnLoaded -= LoadAfterConfigs;
            RuntimeConfigs.OnLoaded += LoadAfterConfigs;
            RuntimeConfigs.OnLoadFailed -= HandleConfigLoadFailed;
            RuntimeConfigs.OnLoadFailed += HandleConfigLoadFailed;

            if (RuntimeConfigs.IsLoaded)
                Load();
            else if (!RuntimeConfigs.HasErrors)
                RuntimeConfigs.WaitUntilLoaded(LoadAfterConfigs);
        }

        public static bool Load()
        {
            if (!RuntimeConfigs.IsLoaded)
            {
                Debug.LogError("[Player] Cannot load player state before runtime configs are loaded.");
                return false;
            }

            _state = SaveService.Load();
            return true;
        }

        public static bool Save()
        {
            return EnsureLoaded("save player state") && SaveService.Save(_state);
        }

        public static bool ResetSave()
        {
            if (!RuntimeConfigs.IsLoaded)
            {
                Debug.LogError("[Player] Cannot reset player state before runtime configs are loaded.");
                return false;
            }

            _state = SaveService.ResetSave();
            return true;
        }

        public static SaveData Snapshot()
        {
            return EnsureLoaded("snapshot player state") ? _state.ToSaveData() : new SaveData();
        }

        public static bool HasHero(string heroId)
        {
            return EnsureLoaded("check hero") && _state.HasHero(heroId);
        }

        public static bool AddHero(string heroId)
        {
            return EnsureLoaded("add hero") && _state.AddHero(heroId);
        }

        public static string GetHeroInSlot(int slotIndex)
        {
            return EnsureLoaded("get hero slot") ? _state.GetHeroInSlot(slotIndex) : null;
        }

        public static int GetHeroSlotIndex(string heroId)
        {
            return EnsureLoaded("get hero slot index") ? _state.GetHeroSlotIndex(heroId) : -1;
        }

        public static bool SetHeroSlot(int slotIndex, string heroId)
        {
            return EnsureLoaded("set hero slot") && _state.SetHeroSlot(slotIndex, heroId);
        }

        public static bool HasHeroState(string heroId)
        {
            return EnsureLoaded("check hero state") && _state.HasHeroState(heroId);
        }

        public static HeroSaveData GetHeroState(string heroId)
        {
            return EnsureLoaded("get hero state") ? _state.GetHeroState(heroId) : null;
        }

        public static int GetHeroFatigue(string heroId)
        {
            return EnsureLoaded("get hero fatigue") ? _state.GetHeroFatigue(heroId) : 0;
        }

        public static int GetHeroMaxFatigue(string heroId)
        {
            return EnsureLoaded("get hero max fatigue") ? _state.GetHeroMaxFatigue(heroId) : 0;
        }

        public static bool SpendHeroFatigue(string heroId, int amount)
        {
            return EnsureLoaded("spend hero fatigue") && _state.SpendHeroFatigue(heroId, amount);
        }

        public static bool RestoreHeroFatigue(string heroId, int amount)
        {
            return EnsureLoaded("restore hero fatigue") && _state.RestoreHeroFatigue(heroId, amount);
        }

        public static int GetHeroSkillLevel(string heroId, string skillId)
        {
            return EnsureLoaded("get hero skill level") ? _state.GetHeroSkillLevel(heroId, skillId) : 0;
        }

        public static long GetHeroSkillExp(string heroId, string skillId)
        {
            return EnsureLoaded("get hero skill exp") ? _state.GetHeroSkillExp(heroId, skillId) : 0L;
        }

        public static bool AddHeroSkillExp(string heroId, string skillId, int amount)
        {
            return EnsureLoaded("add hero skill exp") && _state.AddHeroSkillExp(heroId, skillId, amount);
        }

        public static bool IsHeroBusy(string heroId)
        {
            return EnsureLoaded("check hero busy") && _state.IsHeroBusy(heroId);
        }

        public static string GetHeroCurrentActivityExecutionId(string heroId)
        {
            return EnsureLoaded("get hero current activity execution") ? _state.GetHeroCurrentActivityExecutionId(heroId) : null;
        }

        public static bool SetHeroBusy(string heroId, string executionId)
        {
            return EnsureLoaded("set hero busy") && _state.SetHeroBusy(heroId, executionId);
        }

        public static bool ClearHeroBusy(string heroId, string executionId)
        {
            return EnsureLoaded("clear hero busy") && _state.ClearHeroBusy(heroId, executionId);
        }

        public static bool HasItem(string itemId, int amount)
        {
            return EnsureLoaded("check item") && _state.HasItem(itemId, amount);
        }

        public static int GetItem(string itemId)
        {
            return EnsureLoaded("get item") ? _state.GetItem(itemId) : 0;
        }

        public static bool AddItem(string itemId, int amount)
        {
            return EnsureLoaded("add item") && _state.AddItem(itemId, amount);
        }

        public static bool SpendItem(string itemId, int amount)
        {
            return EnsureLoaded("spend item") && _state.SpendItem(itemId, amount);
        }

        public static long GetCurrency(string currencyId)
        {
            return EnsureLoaded("get currency") ? _state.GetCurrency(currencyId) : 0L;
        }

        public static bool AddCurrency(string currencyId, long amount)
        {
            return EnsureLoaded("add currency") && _state.AddCurrency(currencyId, amount);
        }

        public static bool SpendCurrency(string currencyId, long amount)
        {
            return EnsureLoaded("spend currency") && _state.SpendCurrency(currencyId, amount);
        }

        public static bool IsBuildingUnlocked(string buildingId)
        {
            return EnsureLoaded("check building") && _state.IsBuildingUnlocked(buildingId);
        }

        public static bool CanClickBuilding(string buildingId)
        {
            return EnsureLoaded("check building clickability") && _state.CanClickBuilding(buildingId);
        }

        public static bool UnlockBuilding(string buildingId)
        {
            return EnsureLoaded("unlock building") && _state.UnlockBuilding(buildingId);
        }

        public static int GetBuildingLevel(string buildingId)
        {
            return EnsureLoaded("get building level") ? _state.GetBuildingLevel(buildingId) : 0;
        }

        public static bool SetBuildingLevel(string buildingId, int level)
        {
            return EnsureLoaded("set building level") && _state.SetBuildingLevel(buildingId, level);
        }

        public static bool IsLocationUnlocked(string locationId)
        {
            return EnsureLoaded("check location") && _state.IsLocationUnlocked(locationId);
        }

        public static bool UnlockLocation(string locationId)
        {
            return EnsureLoaded("unlock location") && _state.UnlockLocation(locationId);
        }

        public static bool IsActivityCompleted(string activityId)
        {
            return EnsureLoaded("check activity completion") && _state.IsActivityCompleted(activityId);
        }

        public static bool CompleteActivity(string activityId)
        {
            return EnsureLoaded("complete activity") && _state.CompleteActivity(activityId);
        }

        public static bool IsActivityAvailable(string activityId)
        {
            return EnsureLoaded("check activity availability") && _state.IsActivityAvailable(activityId);
        }

        public static bool SetActivityAvailable(string activityId, bool available)
        {
            return EnsureLoaded("set activity availability") && _state.SetActivityAvailable(activityId, available);
        }

        public static ActivityExecutionSaveData[] GetActivityExecutions()
        {
            return EnsureLoaded("get activity executions") ? _state.GetActivityExecutions() : System.Array.Empty<ActivityExecutionSaveData>();
        }

        public static ActivityExecutionSaveData GetActivityExecution(string executionId)
        {
            return EnsureLoaded("get activity execution") ? _state.GetActivityExecution(executionId) : null;
        }

        public static bool AddActivityExecution(ActivityExecutionSaveData execution)
        {
            return EnsureLoaded("add activity execution") && _state.AddActivityExecution(execution);
        }

        public static bool UpdateActivityExecution(ActivityExecutionSaveData execution)
        {
            return EnsureLoaded("update activity execution") && _state.UpdateActivityExecution(execution);
        }

        public static bool RemoveActivityExecution(string executionId)
        {
            return EnsureLoaded("remove activity execution") && _state.RemoveActivityExecution(executionId);
        }

        private static void LoadAfterConfigs()
        {
            Load();
        }

        private static void HandleConfigLoadFailed(string error)
        {
            _state = null;
            Debug.LogError($"[Player] Runtime configs failed to load; player state was not initialized. {error}");
        }

        private static bool EnsureLoaded(string action)
        {
            if (_state != null)
                return true;

            if (RuntimeConfigs.IsLoaded)
                return Load();

            if (!RuntimeConfigs.HasErrors)
                RuntimeConfigs.WaitUntilLoaded(LoadAfterConfigs);

            var reason = RuntimeConfigs.HasErrors ? $"config load failed: {RuntimeConfigs.LastError}" : "runtime configs are not loaded";
            Debug.LogError($"[Player] Cannot {action}: {reason}.");
            return false;
        }
    }
}
