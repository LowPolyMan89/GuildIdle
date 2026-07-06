using System;
using System.IO;
using GuildIdle.Configs;
using GuildIdle.Localisation;
using TMPro;
using UnityEditor;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Editor.Localisation
{
    [CustomEditor(typeof(LocalisationText))]
    [CanEditMultipleObjects]
    public sealed class LocalisationTextEditor : UnityEditor.Editor
    {
        private const string RuntimeConfigPath = "Assets/StreamingAssets/Configs/localisation_configs.runtime.json";

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            if (GUILayout.Button("Check"))
                CheckSelected();
        }

        private void CheckSelected()
        {
            foreach (var targetObject in targets)
            {
                if (targetObject is LocalisationText localisationText)
                    Check(localisationText);
            }
        }

        private static void Check(LocalisationText localisationText)
        {
            var text = localisationText.Text != null
                ? localisationText.Text
                : localisationText.GetComponent<TMP_Text>();

            Undo.RecordObject(localisationText, "Check Localisation Text");
            if (text != null)
            {
                Undo.RecordObject(text, "Check Localisation Text");
                localisationText.Text = text;
            }

            var id = localisationText.Id;
            if (string.IsNullOrWhiteSpace(id) && text != null)
            {
                localisationText.Id = text.text;
                id = localisationText.Id;
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                EditorUtility.SetDirty(localisationText);
                return;
            }

            var checkedText = ResolveText(id);
            localisationText.ApplyCheckedText(checkedText);

            EditorUtility.SetDirty(localisationText);
            if (text != null)
                EditorUtility.SetDirty(text);
        }

        private static string ResolveText(string id)
        {
            if (Application.isPlaying &&
                RuntimeConfigs.IsLoaded &&
                RuntimeConfigs.Localisation.TryGet(id, out var runtimeText))
            {
                return runtimeText;
            }

            if (TryGetEditorText(id, RuntimeConfigs.Localisation.Lang, out var editorText))
                return editorText;

            return id;
        }

        private static bool TryGetEditorText(string id, string lang, out string text)
        {
            text = null;
            if (string.IsNullOrWhiteSpace(id))
                return false;

            var fullPath = Path.GetFullPath(RuntimeConfigPath);
            if (!File.Exists(fullPath))
                return false;

            LocalisationRuntimeConfigDto dto;
            try
            {
                dto = JsonUtility.FromJson<LocalisationRuntimeConfigDto>(File.ReadAllText(fullPath));
            }
            catch (Exception)
            {
                return false;
            }

            foreach (var entry in dto?.localisations ?? Array.Empty<LocalisationEntryDto>())
            {
                if (!string.Equals(entry.id, id, StringComparison.Ordinal))
                    continue;

                text = SelectText(entry, LocalisationService.NormalizeLang(lang));
                return !string.IsNullOrEmpty(text);
            }

            return false;
        }

        private static string SelectText(LocalisationEntryDto entry, string lang)
        {
            switch (lang)
            {
                case LocalisationService.EnglishLang:
                    return entry.en;
                case LocalisationService.TurkishLang:
                    return entry.tr;
                default:
                    return entry.ru;
            }
        }
    }
}
