using System;
using GuildIdle.Configs;
using UnityEngine;

namespace GuildIdle.Localisation
{
    public sealed class LocalisationService
    {
        public const string DefaultLang = "ru";
        public const string EnglishLang = "en";
        public const string TurkishLang = "tr";
        public const string PlayerPrefsLangKey = "GuildIdle.Localisation.Lang";

        private static string _lang;

        private LocalisationConfigRepository _repository;

        public static event Action<string> LanguageChanged;

        public event Action<string> OnLanguageChanged
        {
            add => LanguageChanged += value;
            remove => LanguageChanged -= value;
        }

        public string Lang => GetCurrentLang();

        public void SetLang(string lang)
        {
            SetGlobalLang(lang);
        }

        public string Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return string.Empty;

            if (_repository == null)
            {
                Debug.LogWarning($"[Localisation] Runtime localisation config is not loaded yet. Returning id '{id}'.");
                return id;
            }

            if (!_repository.TryGet(id, out var entry))
            {
                Debug.LogWarning($"[Localisation] Missing localisation id '{id}'.");
                return id;
            }

            var text = GetText(entry, GetCurrentLang());
            if (!string.IsNullOrEmpty(text))
                return text;

            Debug.LogWarning($"[Localisation] Missing '{GetCurrentLang()}' translation for id '{id}'.");
            return id;
        }

        public bool TryGet(string id, out string text)
        {
            text = null;
            if (string.IsNullOrWhiteSpace(id) || _repository == null || !_repository.TryGet(id, out var entry))
                return false;

            text = GetText(entry, GetCurrentLang());
            return !string.IsNullOrEmpty(text);
        }

        public static string SetGlobalLang(string lang)
        {
            var normalized = NormalizeLang(lang);
            _lang = normalized;
            PlayerPrefs.SetString(PlayerPrefsLangKey, normalized);
            PlayerPrefs.Save();
            LanguageChanged?.Invoke(normalized);
            return normalized;
        }

        public static string NormalizeLang(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang))
                return DefaultLang;

            switch (lang.Trim().ToLowerInvariant())
            {
                case DefaultLang:
                    return DefaultLang;
                case EnglishLang:
                    return EnglishLang;
                case TurkishLang:
                    return TurkishLang;
                default:
                    Debug.LogWarning($"[Localisation] Unsupported language '{lang}'. Falling back to '{DefaultLang}'.");
                    return DefaultLang;
            }
        }

        internal void SetRepository(LocalisationConfigRepository repository)
        {
            _repository = repository;
        }

        private static string GetCurrentLang()
        {
            if (!string.IsNullOrEmpty(_lang))
                return _lang;

            _lang = NormalizeLang(PlayerPrefs.GetString(PlayerPrefsLangKey, DefaultLang));
            return _lang;
        }

        private static string GetText(LocalisationEntryDto entry, string lang)
        {
            switch (lang)
            {
                case EnglishLang:
                    return entry.en;
                case TurkishLang:
                    return entry.tr;
                default:
                    return entry.ru;
            }
        }
    }
}
