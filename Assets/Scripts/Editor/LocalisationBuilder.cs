using System.Collections.Generic;
using System.IO;
using System.Text;
using GuildIdle;
using UnityEditor;
using UnityEngine;

namespace GuildIdle.Editor
{
    public static class LocalisationBuilder
    {
        public const string SourceDirectory = "Assets/Configs/Localization";
        public const string ResourcesDirectory = SourceDirectory + "/Resources";
        public const string OutputPath = ResourcesDirectory + "/Localisation.json";

        [MenuItem("Tools/Build/BuildLocalisation")]
        public static void BuildLocalisation()
        {
            if (!Directory.Exists(SourceDirectory))
            {
                Debug.LogError($"Localisation source directory '{SourceDirectory}' was not found.");
                return;
            }

            var config = new LocalisationConfig
            {
                Texts = CollectTexts(out var hasErrors).ToArray()
            };

            if (hasErrors)
            {
                Debug.LogError("Localisation build failed. Output file was not changed.");
                return;
            }

            Directory.CreateDirectory(ResourcesDirectory);

            var json = JsonUtility.ToJson(config, true);
            File.WriteAllText(OutputPath, json, Encoding.UTF8);
            AssetDatabase.ImportAsset(OutputPath);
            AssetDatabase.Refresh();

            Debug.Log($"Localisation build completed. {config.Texts.Length} texts written to '{OutputPath}'.");
        }

        private static List<LocalisationText> CollectTexts(out bool hasErrors)
        {
            hasErrors = false;

            var texts = new List<LocalisationText>();
            var ids = new HashSet<string>();
            var files = Directory.GetFiles(SourceDirectory, "*.json", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                var unityPath = ToUnityPath(file);
                if (unityPath == OutputPath || unityPath.StartsWith(ResourcesDirectory + "/"))
                    continue;

                LocalisationConfig fileConfig;
                try
                {
                    fileConfig = JsonUtility.FromJson<LocalisationConfig>(File.ReadAllText(file, Encoding.UTF8));
                }
                catch (System.Exception exception)
                {
                    Debug.LogError($"Localisation file '{unityPath}' could not be parsed: {exception.Message}");
                    hasErrors = true;
                    continue;
                }

                if (fileConfig?.Texts == null)
                {
                    Debug.LogError($"Localisation file '{unityPath}' has no Texts array.");
                    hasErrors = true;
                    continue;
                }

                foreach (var text in fileConfig.Texts)
                    ValidateAndAdd(text, unityPath, ids, texts, ref hasErrors);
            }

            return texts;
        }

        private static void ValidateAndAdd(LocalisationText text, string sourcePath, HashSet<string> ids, List<LocalisationText> texts, ref bool hasErrors)
        {
            if (text == null || string.IsNullOrWhiteSpace(text.Id))
            {
                Debug.LogError($"Localisation file '{sourcePath}' contains text with empty Id.");
                hasErrors = true;
                return;
            }

            if (!ids.Add(text.Id))
            {
                Debug.LogError($"Duplicate localisation Id '{text.Id}' in '{sourcePath}'.");
                hasErrors = true;
                return;
            }

            if (text.Lang == null || text.Lang.Length != LocalisationModel.Languages.Length)
            {
                Debug.LogError($"Localisation Id '{text.Id}' in '{sourcePath}' must contain exactly {LocalisationModel.Languages.Length} Lang values.");
                hasErrors = true;
                return;
            }

            texts.Add(text);
        }

        private static string ToUnityPath(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
