using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildIdle.UI.Core
{
    [CreateAssetMenu(fileName = "UIPrefabCatalog", menuName = "GuildIdle/UI/Prefab Catalog")]
    public sealed class UIPrefabCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private UISerializableTypeReference viewType = new UISerializableTypeReference();
            [SerializeField] private GameObject prefab;
            [SerializeField] private UILayer targetLayer;

            public Entry()
            {
            }

            public Entry(Type type, GameObject prefabValue, UILayer layer)
            {
                viewType = new UISerializableTypeReference(type);
                prefab = prefabValue;
                targetLayer = layer;
            }

            public UISerializableTypeReference ViewType => viewType;
            public GameObject Prefab => prefab;
            public UILayer TargetLayer => targetLayer;
        }

        internal readonly struct ResolvedEntry
        {
            public ResolvedEntry(Type viewType, UIView prefab, UILayer targetLayer)
            {
                ViewType = viewType;
                Prefab = prefab;
                TargetLayer = targetLayer;
            }

            public Type ViewType { get; }
            public UIView Prefab { get; }
            public UILayer TargetLayer { get; }
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public IReadOnlyList<Entry> Entries => entries ?? Array.Empty<Entry>();

        public void ValidateOrThrow()
        {
            var errors = CollectValidationErrors();
            if (errors.Count == 0)
                return;

            throw new InvalidOperationException(
                $"UI prefab catalog '{name}' is invalid:{Environment.NewLine}- " +
                string.Join($"{Environment.NewLine}- ", errors));
        }

        internal ResolvedEntry ResolveOrThrow(Type requestedType)
        {
            if (requestedType == null)
                throw new ArgumentNullException(nameof(requestedType));

            ValidateOrThrow();
            foreach (var entry in entries ?? Array.Empty<Entry>())
            {
                var registeredType = entry.ViewType.Resolve();
                if (registeredType == requestedType)
                {
                    return new ResolvedEntry(
                        registeredType,
                        entry.Prefab.GetComponent<UIView>(),
                        entry.TargetLayer);
                }
            }

            throw new InvalidOperationException(
                $"UI view type '{requestedType.FullName}' is not registered in catalog '{name}'.");
        }

        private List<string> CollectValidationErrors()
        {
            var errors = new List<string>();
            var registeredTypes = new HashSet<Type>();
            var source = entries ?? Array.Empty<Entry>();

            for (var index = 0; index < source.Length; index++)
            {
                var entry = source[index];
                var prefix = $"Entry {index}";
                if (entry == null)
                {
                    errors.Add($"{prefix} is null.");
                    continue;
                }

                var typeName = entry.ViewType?.AssemblyQualifiedName;
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    errors.Add($"{prefix} has no registered view type.");
                    continue;
                }

                var viewType = entry.ViewType.Resolve();
                if (viewType == null)
                {
                    errors.Add($"{prefix} view type '{typeName}' cannot be resolved.");
                    continue;
                }

                if (viewType.IsAbstract || viewType.ContainsGenericParameters)
                    errors.Add($"{prefix} view type '{viewType.FullName}' must be concrete and non-generic.");

                var isScreen = typeof(UIScreen).IsAssignableFrom(viewType);
                var isWindow = typeof(UIWindow).IsAssignableFrom(viewType);
                if (!isScreen && !isWindow)
                {
                    errors.Add(
                        $"{prefix} view type '{viewType.FullName}' is not a top-level UIScreen or UIWindow. " +
                        "UIPanel and reusable UIView components cannot be registered in the global catalog.");
                }

                if (!registeredTypes.Add(viewType))
                    errors.Add($"{prefix} duplicates registered view type '{viewType.FullName}'.");

                if (entry.Prefab == null)
                {
                    errors.Add($"{prefix} for '{viewType.FullName}' has a null prefab.");
                    continue;
                }

                var prefabViews = entry.Prefab.GetComponents<UIView>();
                if (prefabViews.Length == 0)
                {
                    errors.Add($"{prefix} prefab '{entry.Prefab.name}' has no UIView component.");
                }
                else if (prefabViews.Length != 1)
                {
                    errors.Add($"{prefix} prefab '{entry.Prefab.name}' must contain exactly one UIView component on its root.");
                }
                else if (prefabViews[0].GetType() != viewType)
                {
                    errors.Add(
                        $"{prefix} registers '{viewType.FullName}', but prefab '{entry.Prefab.name}' contains " +
                        $"'{prefabViews[0].GetType().FullName}'.");
                }

                if (isScreen && entry.TargetLayer != UILayer.Screen)
                {
                    errors.Add(
                        $"{prefix} UIScreen '{viewType.FullName}' must target UILayer.Screen, not '{entry.TargetLayer}'.");
                }

                if (isWindow && entry.TargetLayer != UILayer.Window && entry.TargetLayer != UILayer.Popup)
                {
                    errors.Add(
                        $"{prefix} UIWindow '{viewType.FullName}' must target UILayer.Window or UILayer.Popup, " +
                        $"not '{entry.TargetLayer}'.");
                }
            }

            return errors;
        }

        private void OnValidate()
        {
            var errors = CollectValidationErrors();
            foreach (var error in errors)
                Debug.LogError($"[UIPrefabCatalog] {error}", this);
        }
    }
}
