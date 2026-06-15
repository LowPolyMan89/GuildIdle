using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildIdle
{
    [Serializable]
    public sealed class StatConfig
    {
        public string Id;
        public string Category;
        public string LocalisationNameId;
        public string LocalisationDescriptionId;
        public string IconId;
    }

    public static class ConfigProvider
    {
        private const string StatsResourcePath = "Stats";

        private static readonly Dictionary<string, StatConfig> _stats = new Dictionary<string, StatConfig>();

        public static IReadOnlyDictionary<string, StatConfig> Stats => _stats;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RuntimeInitialize()
        {
            LoadAll();
        }

        public static void Reload()
        {
            LoadAll();
        }

        public static bool TryGetStat(string id, out StatConfig config)
        {
            EnsureLoaded();
            return _stats.TryGetValue(id, out config);
        }

        public static StatConfig GetStat(string id)
        {
            if (TryGetStat(id, out var config))
                return config;

            throw new KeyNotFoundException($"Stat config with id '{id}' was not found.");
        }

        private static void LoadAll()
        {
            _stats.Clear();

            LoadConfigs(
                StatsResourcePath,
                _stats,
                config => config.Id,
                IsStatConfigAsset,
                "stat");
        }

        private static void EnsureLoaded()
        {
            if (_stats.Count == 0)
                LoadAll();
        }

        private static void LoadConfigs<TConfig>(
            string resourcePath,
            Dictionary<string, TConfig> target,
            Func<TConfig, string> getId,
            Func<TConfig, TextAsset, bool> isExpectedConfig,
            string configType)
        {
            var assets = Resources.LoadAll<TextAsset>(resourcePath);

            foreach (var asset in assets)
            {
                TConfig config;
                try
                {
                    config = JsonUtility.FromJson<TConfig>(asset.text);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Failed to parse {configType} config '{asset.name}': {exception.Message}");
                    continue;
                }

                if (config == null || !isExpectedConfig(config, asset))
                    continue;

                var id = getId(config);
                if (string.IsNullOrWhiteSpace(id))
                {
                    Debug.LogError($"{configType} config '{asset.name}' has empty Id.");
                    continue;
                }

                if (target.ContainsKey(id))
                {
                    Debug.LogError($"Duplicate {configType} config id '{id}' in '{asset.name}'.");
                    continue;
                }

                target.Add(id, config);
            }
        }

        private static bool IsStatConfigAsset(StatConfig config, TextAsset asset)
        {
            if (string.IsNullOrWhiteSpace(config.Id))
                return false;

            if (string.IsNullOrWhiteSpace(config.Category))
            {
                Debug.LogError($"Stat config '{asset.name}' has empty Category.");
                return false;
            }

            return true;
        }
    }
}
