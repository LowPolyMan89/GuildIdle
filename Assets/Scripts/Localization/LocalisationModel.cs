using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildIdle
{
    [Serializable]
    public sealed class LocalisationConfig
    {
        public LocalisationText[] Texts;
    }

    [Serializable]
    public sealed class LocalisationText
    {
        public string Id;
        public LocalisationValue[] Lang;
    }

    [Serializable]
    public sealed class LocalisationValue
    {
        public string Value;
    }

    public static class LocalisationModel
    {
        private const string DefaultLanguage = "ru";
        private const string PlayerPrefsKey = "localisation_language";
        private const string ResourcePath = "Localisation";

        private static readonly string[] _languages = { "ru", "en", "tr" };
        private static readonly HashSet<string> _warnings = new HashSet<string>();
        private static Dictionary<string, LocalisationValue[]> _texts;
        private static string _currentLanguage;

        static LocalisationModel()
        {
            _currentLanguage = PlayerPrefs.GetString(PlayerPrefsKey, DefaultLanguage);

            if (!IsSupportedLanguage(_currentLanguage))
                _currentLanguage = DefaultLanguage;
        }

        public static event Action<string> LanguageChanged;

        public static string[] Languages => (string[])_languages.Clone();

        public static string CurrentLanguage
        {
            get => _currentLanguage;
            set => SetLanguage(value);
        }

        public static void Reload()
        {
            _texts = null;
            _warnings.Clear();
            EnsureLoaded();
        }

        public static void SetLanguage(string languageCode)
        {
            if (!IsSupportedLanguage(languageCode))
            {
                WarnOnce($"unsupported_language_{languageCode}", $"Unsupported language '{languageCode}'.");
                return;
            }

            if (_currentLanguage == languageCode)
                return;

            _currentLanguage = languageCode;
            PlayerPrefs.SetString(PlayerPrefsKey, _currentLanguage);
            PlayerPrefs.Save();
            LanguageChanged?.Invoke(_currentLanguage);
        }

        public static string GetText(string id)
        {
            if (TryGetText(id, out var value))
                return value;

            return id;
        }

        public static bool TryGetText(string id, out string value)
        {
            value = id;

            if (string.IsNullOrWhiteSpace(id))
            {
                WarnOnce("empty_key", "Localisation key cannot be empty.");
                return false;
            }

            EnsureLoaded();

            if (_texts == null || !_texts.TryGetValue(id, out var langValues))
            {
                WarnOnce($"missing_key_{id}", $"Localisation key '{id}' was not found.");
                return false;
            }

            var languageIndex = GetCurrentLanguageIndex();
            if (langValues == null || languageIndex >= langValues.Length || langValues[languageIndex] == null || langValues[languageIndex].Value == null)
            {
                WarnOnce($"missing_value_{id}_{_currentLanguage}", $"Localisation key '{id}' has no '{_currentLanguage}' value.");
                return false;
            }

            value = langValues[languageIndex].Value;
            return true;
        }

        private static void EnsureLoaded()
        {
            if (_texts != null)
                return;

            _texts = new Dictionary<string, LocalisationValue[]>();

            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                WarnOnce("missing_asset", $"Localisation resource '{ResourcePath}' was not found.");
                return;
            }

            var config = JsonUtility.FromJson<LocalisationConfig>(asset.text);
            if (config?.Texts == null)
            {
                WarnOnce("invalid_asset", "Localisation resource has no Texts array.");
                return;
            }

            foreach (var text in config.Texts)
            {
                if (text == null || string.IsNullOrWhiteSpace(text.Id))
                    continue;

                if (_texts.ContainsKey(text.Id))
                {
                    WarnOnce($"runtime_duplicate_{text.Id}", $"Duplicate localisation key '{text.Id}' in runtime config.");
                    continue;
                }

                _texts.Add(text.Id, text.Lang);
            }
        }

        private static int GetCurrentLanguageIndex()
        {
            var index = Array.IndexOf(_languages, _currentLanguage);
            return index >= 0 ? index : 0;
        }

        private static bool IsSupportedLanguage(string languageCode)
        {
            return Array.IndexOf(_languages, languageCode) >= 0;
        }

        private static void WarnOnce(string id, string message)
        {
            if (_warnings.Add(id))
                Debug.LogWarning(message);
        }
    }
}
