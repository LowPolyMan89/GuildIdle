using System;
using GuildIdle.Localisation;
using UnityEngine;

namespace GuildIdle.Configs
{
    public static class Configs
    {
        private static readonly ConfigDatabase EmptyDatabase = new ConfigDatabase(null, null, null, null, null, null, null, null, null, null, null);
        private static readonly LocalisationService LocalisationService = new LocalisationService();

        private static ConfigDatabase _database;
        private static bool _isLoading;

        public static event Action OnLoaded;
        public static event Action<string> OnLoadFailed;

        public static bool IsLoaded => _database != null && !HasErrors;
        public static bool IsLoading => _isLoading;
        public static bool HasErrors { get; private set; }
        public static string LastError { get; private set; }

        public static ItemsConfigRepository Items => GetRepository(database => database.Items, "Items");
        public static HeroesConfigRepository Heroes => GetRepository(database => database.Heroes, "Heroes");
        public static ActivitiesConfigRepository Activities => GetRepository(database => database.Activities, "Activities");
        public static BuildingsConfigRepository Buildings => GetRepository(database => database.Buildings, "Buildings");
        public static QuestConfigRepository Quests => GetRepository(database => database.Quests, "Quests");
        public static EnemiesConfigRepository Enemies => GetRepository(database => database.Enemies, "Enemies");
        public static FormulasConfigRepository Formulas => GetRepository(database => database.Formulas, "Formulas");
        public static LootConfigRepository Loot => GetRepository(database => database.Loot, "Loot");
        public static MapConfigRepository Map => GetRepository(database => database.Map, "Map");
        public static StorageConfigRepository Storage => GetRepository(database => database.Storage, "Storage");
        public static LocalisationService Localisation
        {
            get
            {
                if (Application.isPlaying)
                    EnsureLoading();

                return LocalisationService;
            }
        }

        public static void WaitUntilLoaded(Action onLoaded)
        {
            if (onLoaded == null)
                return;

            if (IsLoaded)
            {
                onLoaded.Invoke();
                return;
            }

            Action wrapper = null;
            wrapper = () =>
            {
                OnLoaded -= wrapper;
                onLoaded.Invoke();
            };

            OnLoaded += wrapper;
            EnsureLoading();
        }

        public static void Reload()
        {
            _database = null;
            LocalisationService.SetRepository(null);
            HasErrors = false;
            LastError = null;
            ConfigLoader.StartLoad(forceReload: true);
        }

        public static void SetDatabaseForTests(ConfigDatabase database)
        {
            _database = database;
            _isLoading = false;
            HasErrors = database == null;
            LastError = database == null ? "Test ConfigDatabase was set to null." : null;
            LocalisationService.SetRepository(database?.Localisation);
        }

        internal static void EnsureLoading()
        {
            if (_database != null || _isLoading)
                return;

            ConfigLoader.StartLoad(forceReload: false);
        }

        internal static void MarkLoading()
        {
            _isLoading = true;
            HasErrors = false;
            LastError = null;
        }

        internal static void Publish(ConfigDatabase database)
        {
            _database = database;
            LocalisationService.SetRepository(database?.Localisation);
            _isLoading = false;
            HasErrors = false;
            LastError = null;
            OnLoaded?.Invoke();
        }

        internal static void Fail(string error)
        {
            _database = null;
            LocalisationService.SetRepository(null);
            _isLoading = false;
            HasErrors = true;
            LastError = string.IsNullOrWhiteSpace(error) ? "Unknown config load error." : error;
            Debug.LogError($"[Configs] {LastError}");
            OnLoadFailed?.Invoke(LastError);
        }

        private static T GetRepository<T>(Func<ConfigDatabase, T> selector, string group)
            where T : class
        {
            if (_database != null)
                return selector(_database);

            EnsureLoading();
            Debug.LogError($"[Configs] Configs.{group} requested before runtime configs finished loading.");
            return selector(EmptyDatabase);
        }
    }
}
