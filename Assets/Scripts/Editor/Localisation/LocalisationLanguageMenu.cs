using GuildIdle.Localisation;
using UnityEditor;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Editor.Localisation
{
    public static class LocalisationLanguageMenu
    {
        private const string Root = "GuildIdle/Localisation/Language/";
        private const string RuPath = Root + "Ru";
        private const string EnPath = Root + "En";
        private const string TrPath = Root + "Tr";

        [MenuItem(RuPath)]
        private static void SetRu()
        {
            SetLang(LocalisationService.DefaultLang);
        }

        [MenuItem(EnPath)]
        private static void SetEn()
        {
            SetLang(LocalisationService.EnglishLang);
        }

        [MenuItem(TrPath)]
        private static void SetTr()
        {
            SetLang(LocalisationService.TurkishLang);
        }

        [MenuItem(RuPath, true)]
        private static bool ValidateRu()
        {
            return Validate(LocalisationService.DefaultLang);
        }

        [MenuItem(EnPath, true)]
        private static bool ValidateEn()
        {
            return Validate(LocalisationService.EnglishLang);
        }

        [MenuItem(TrPath, true)]
        private static bool ValidateTr()
        {
            return Validate(LocalisationService.TurkishLang);
        }

        private static void SetLang(string lang)
        {
            RuntimeConfigs.Localisation.SetLang(lang);
            var normalized = RuntimeConfigs.Localisation.Lang;
            Debug.Log($"[Localisation] Current language set to '{normalized}'.");
        }

        private static bool Validate(string lang)
        {
            Menu.SetChecked(RuPath, RuntimeConfigs.Localisation.Lang == LocalisationService.DefaultLang);
            Menu.SetChecked(EnPath, RuntimeConfigs.Localisation.Lang == LocalisationService.EnglishLang);
            Menu.SetChecked(TrPath, RuntimeConfigs.Localisation.Lang == LocalisationService.TurkishLang);
            return true;
        }
    }
}
