using GuildIdle.Localisation;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace GuildIdle.Editor.Localisation
{
    public static class LocalisationTestPrefabCreator
    {
        private const string PrefabFolder = "Assets/Prefabs/Tests";
        private const string PrefabPath = PrefabFolder + "/LocalisationTextTest.prefab";

        [MenuItem("GuildIdle/Localisation/Create Test Prefab")]
        private static void CreateTestPrefab()
        {
            EnsurePrefabFolder();

            var gameObject = new GameObject("LocalisationTextTest", typeof(RectTransform));
            try
            {
                var rectTransform = gameObject.GetComponent<RectTransform>();
                rectTransform.sizeDelta = new Vector2(420f, 80f);

                var text = gameObject.AddComponent<TextMeshProUGUI>();
                text.text = "hero.aska.name";
                text.fontSize = 28f;
                text.alignment = TextAlignmentOptions.Center;

                var localisationText = gameObject.AddComponent<LocalisationText>();
                localisationText.Text = text;
                localisationText.Id = "hero.aska.name";

                PrefabUtility.SaveAsPrefabAsset(gameObject, PrefabPath);
                AssetDatabase.Refresh();
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                Debug.Log($"[Localisation] Test prefab created at {PrefabPath}.");
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        private static void EnsurePrefabFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");

            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder("Assets/Prefabs", "Tests");
        }
    }
}
