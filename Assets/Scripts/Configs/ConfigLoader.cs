using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace GuildIdle.Configs
{
    internal sealed class ConfigLoader : MonoBehaviour
    {
        private const string ConfigFolder = "Configs";

        private static ConfigLoader _instance;

        private ItemsRuntimeConfigDto _items;
        private HeroesRuntimeConfigDto _heroes;
        private ActivitiesRuntimeConfigDto _activities;
        private BuildingsRuntimeConfigDto _buildings;
        private EnemiesRuntimeConfigDto _enemies;
        private FormulaRuntimeConfigDto _formulas;
        private LootRuntimeConfigDto _loot;
        private MapRuntimeConfigDto _map;
        private StorageRuntimeConfigDto _storage;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            Configs.EnsureLoading();
        }

        public static void StartLoad(bool forceReload)
        {
            if (_instance == null)
            {
                var gameObject = new GameObject("GuildIdle Config Loader");
                DontDestroyOnLoad(gameObject);
                _instance = gameObject.AddComponent<ConfigLoader>();
            }

            if (Configs.IsLoading && !forceReload)
                return;

            Configs.MarkLoading();
            _instance.StopAllCoroutines();
            _instance.StartCoroutine(_instance.LoadAll());
        }

        private IEnumerator LoadAll()
        {
            yield return LoadJson("items_configs.runtime.json", json => _items = Parse<ItemsRuntimeConfigDto>("items_configs.runtime.json", json));
            if (Configs.HasErrors) yield break;

            yield return LoadJson("heroes_configs.runtime.json", json => _heroes = Parse<HeroesRuntimeConfigDto>("heroes_configs.runtime.json", json));
            if (Configs.HasErrors) yield break;

            yield return LoadJson("activity_configs.runtime.json", json => _activities = Parse<ActivitiesRuntimeConfigDto>("activity_configs.runtime.json", json));
            if (Configs.HasErrors) yield break;

            yield return LoadJson("buildings_configs.runtime.json", json => _buildings = Parse<BuildingsRuntimeConfigDto>("buildings_configs.runtime.json", json));
            if (Configs.HasErrors) yield break;

            yield return LoadJson("enemies_configs.runtime.json", json => _enemies = Parse<EnemiesRuntimeConfigDto>("enemies_configs.runtime.json", json));
            if (Configs.HasErrors) yield break;

            yield return LoadJson("formula_configs.runtime.json", json => _formulas = Parse<FormulaRuntimeConfigDto>("formula_configs.runtime.json", json));
            if (Configs.HasErrors) yield break;

            yield return LoadJson("loot_configs.runtime.json", json => _loot = Parse<LootRuntimeConfigDto>("loot_configs.runtime.json", json));
            if (Configs.HasErrors) yield break;

            yield return LoadJson("map_configs.runtime.json", json => _map = Parse<MapRuntimeConfigDto>("map_configs.runtime.json", json));
            if (Configs.HasErrors) yield break;

            yield return LoadJson("storage_configs.runtime.json", json => _storage = Parse<StorageRuntimeConfigDto>("storage_configs.runtime.json", json));
            if (Configs.HasErrors) yield break;

            ConfigDatabase database;
            try
            {
                database = new ConfigDatabase(_items, _heroes, _activities, _buildings, _enemies, _formulas, _loot, _map, _storage);
            }
            catch (Exception exception)
            {
                Configs.Fail($"Failed to build ConfigDatabase: {exception.Message}");
                yield break;
            }

            Configs.Publish(database);
            Debug.Log(
                "[Configs] Loaded runtime configs: " +
                $"items={database.Items.ItemCount}, " +
                $"heroes={database.Heroes.Count}, " +
                $"activities={database.Activities.Count}, " +
                $"buildings={database.Buildings.Count}, " +
                $"enemies={database.Enemies.Count}, " +
                $"formulas={database.Formulas.Count}, " +
                $"loot={database.Loot.Count}, " +
                $"map={database.Map.Count}, " +
                $"storage={database.Storage.Count}.");
        }

        private IEnumerator LoadJson(string fileName, Action<string> onLoaded)
        {
            var uri = BuildStreamingAssetsUri(fileName);
            using (var request = UnityWebRequest.Get(uri))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Configs.Fail($"Failed to load {fileName} from StreamingAssets: {request.error}");
                    yield break;
                }

                var json = request.downloadHandler?.text;
                if (string.IsNullOrWhiteSpace(json))
                {
                    Configs.Fail($"Runtime config file {fileName} is empty or missing.");
                    yield break;
                }

                onLoaded.Invoke(json);
            }
        }

        private static T Parse<T>(string fileName, string json)
            where T : class
        {
            try
            {
                var dto = JsonUtility.FromJson<T>(json);
                if (dto == null)
                    Configs.Fail($"Runtime config file {fileName} could not be parsed.");

                return dto;
            }
            catch (Exception exception)
            {
                Configs.Fail($"Invalid JSON in {fileName}: {exception.Message}");
                return null;
            }
        }

        private static string BuildStreamingAssetsUri(string fileName)
        {
            var root = Application.streamingAssetsPath.TrimEnd('/', '\\');
            var path = $"{root}/{ConfigFolder}/{fileName}";
            if (path.Contains("://"))
                return path;

            if (!Path.IsPathRooted(path))
                return path;

            return new Uri(path).AbsoluteUri;
        }
    }
}
