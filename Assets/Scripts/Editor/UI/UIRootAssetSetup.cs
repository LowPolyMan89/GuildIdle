using System;
using System.Linq;
using GuildIdle.UI.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GuildIdle.UI.Editor
{
    public static class UIRootAssetSetup
    {
        public const string CatalogPath = "Assets/Configs/UI/UIPrefabCatalog.asset";
        public const string PrefabPath = "Assets/Prefabs/UI/UIRoot.prefab";
        public const string ScenePath = "Assets/Scenes/Init.unity";

        [MenuItem("GuildIdle/UI/Setup UI Foundation")]
        public static void Setup()
        {
            EnsureFolder("Assets/Configs", "UI");
            EnsureFolder("Assets/Prefabs", "UI");

            var catalog = GetOrCreateCatalog();
            var prefab = CreateOrUpdatePrefab(catalog);
            AddPrefabToInitScene(prefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[UIRootAssetSetup] UI foundation assets are ready.");
        }

        private static UIPrefabCatalog GetOrCreateCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<UIPrefabCatalog>(CatalogPath);
            if (catalog != null)
            {
                catalog.ValidateOrThrow();
                return catalog;
            }

            catalog = ScriptableObject.CreateInstance<UIPrefabCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            return catalog;
        }

        private static GameObject CreateOrUpdatePrefab(UIPrefabCatalog catalog)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var root = existing != null
                ? PrefabUtility.LoadPrefabContents(PrefabPath)
                : new GameObject("UIRoot", typeof(RectTransform));

            try
            {
                root.name = "UIRoot";
                var rootTransform = root.GetComponent<RectTransform>();
                if (rootTransform == null)
                    throw new InvalidOperationException("UIRoot prefab root must use RectTransform.");

                rootTransform.localPosition = Vector3.zero;
                rootTransform.localRotation = Quaternion.identity;
                rootTransform.localScale = Vector3.one;
                var canvas = GetOrAddComponent<Canvas>(root);
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                GetOrAddComponent<GraphicRaycaster>(root);
                GetOrAddComponent<EventSystem>(root);
                GetOrAddComponent<InputSystemUIInputModule>(root);
                var uiRoot = GetOrAddComponent<UIRoot>(root);

                var screens = EnsureLayer(root.transform, "Screens", 0);
                var hud = EnsureLayer(root.transform, "HUD", 1);
                var windows = EnsureLayer(root.transform, "Windows", 2);
                var popups = EnsureLayer(root.transform, "Popups", 3);
                var overlays = EnsureLayer(root.transform, "Overlays", 4);

                var serializedRoot = new SerializedObject(uiRoot);
                serializedRoot.FindProperty("hud").objectReferenceValue = hud;
                serializedRoot.FindProperty("screens").objectReferenceValue = screens;
                serializedRoot.FindProperty("windows").objectReferenceValue = windows;
                serializedRoot.FindProperty("popups").objectReferenceValue = popups;
                serializedRoot.FindProperty("overlays").objectReferenceValue = overlays;
                serializedRoot.FindProperty("prefabCatalog").objectReferenceValue = catalog;
                serializedRoot.ApplyModifiedPropertiesWithoutUndo();

                var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (saved == null)
                    throw new InvalidOperationException($"Failed to save UIRoot prefab at '{PrefabPath}'.");

                return saved;
            }
            finally
            {
                if (existing != null)
                    PrefabUtility.UnloadPrefabContents(root);
                else
                    Object.DestroyImmediate(root);
            }
        }

        private static void AddPrefabToInitScene(GameObject prefab)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roots = Object.FindObjectsByType<UIRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(root => root.gameObject.scene == scene)
                .ToArray();

            if (roots.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Scene '{ScenePath}' contains {roots.Length} UIRoot instances. Resolve the conflict manually.");
            }

            if (roots.Length == 1)
            {
                var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(roots[0].gameObject);
                if (!string.Equals(path, PrefabPath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Scene '{ScenePath}' contains a UIRoot that is not an instance of '{PrefabPath}'.");
                }
            }
            else
            {
                var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (instance == null)
                    throw new InvalidOperationException($"Failed to instantiate '{PrefabPath}' in '{ScenePath}'.");

                instance.name = "UIRoot";
            }

            var finalRoots = Object.FindObjectsByType<UIRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Count(root => root.gameObject.scene == scene);
            if (finalRoots != 1)
                throw new InvalidOperationException($"Scene '{ScenePath}' must contain exactly one UIRoot, found {finalRoots}.");

            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Failed to save scene '{ScenePath}'.");
        }

        private static RectTransform EnsureLayer(Transform root, string layerName, int siblingIndex)
        {
            var matches = root.Cast<Transform>().Where(child => child.name == layerName).ToArray();
            if (matches.Length > 1)
                throw new InvalidOperationException($"UIRoot prefab has multiple direct children named '{layerName}'.");

            RectTransform layer;
            if (matches.Length == 0)
            {
                var layerObject = new GameObject(layerName, typeof(RectTransform));
                layer = layerObject.GetComponent<RectTransform>();
                layer.SetParent(root, false);
            }
            else
            {
                layer = matches[0] as RectTransform;
                if (layer == null)
                    throw new InvalidOperationException($"UIRoot child '{layerName}' must use RectTransform.");
            }

            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.offsetMin = Vector2.zero;
            layer.offsetMax = Vector2.zero;
            layer.localScale = Vector3.one;
            layer.SetSiblingIndex(siblingIndex);
            return layer;
        }

        private static T GetOrAddComponent<T>(GameObject target)
            where T : Component
        {
            return target.TryGetComponent<T>(out var component) ? component : target.AddComponent<T>();
        }

        private static void EnsureFolder(string parent, string child)
        {
            var path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }
    }
}
