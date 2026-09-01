using System;
using UnityEngine;

namespace GuildIdle.UI.Core
{
    [DisallowMultipleComponent]
    public sealed class UIRoot : MonoBehaviour
    {
        [SerializeField] private RectTransform hud;
        [SerializeField] private RectTransform screens;
        [SerializeField] private RectTransform windows;
        [SerializeField] private RectTransform popups;
        [SerializeField] private RectTransform overlays;
        [SerializeField] private UIPrefabCatalog prefabCatalog;

        public UIService Service { get; private set; }
        public RectTransform Hud => hud;
        public RectTransform Screens => screens;
        public RectTransform Windows => windows;
        public RectTransform Popups => popups;
        public RectTransform Overlays => overlays;
        public UIPrefabCatalog PrefabCatalog => prefabCatalog;

        public Transform GetLayer(UILayer layer)
        {
            return layer switch
            {
                UILayer.Screen => screens,
                UILayer.Window => windows,
                UILayer.Popup => popups,
                UILayer.Overlay => overlays,
                _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown UI layer.")
            };
        }

        public void ValidateOrThrow()
        {
            if (hud == null || screens == null || windows == null || popups == null || overlays == null)
                throw new InvalidOperationException($"UIRoot '{name}' must reference all five layer containers.");

            if (prefabCatalog == null)
                throw new InvalidOperationException($"UIRoot '{name}' must reference a UIPrefabCatalog asset.");

            if (hud == screens || hud == windows || hud == popups || hud == overlays ||
                screens == windows || screens == popups || screens == overlays ||
                windows == popups || windows == overlays || popups == overlays)
            {
                throw new InvalidOperationException($"UIRoot '{name}' layer containers must be distinct.");
            }

            var layers = new[] { screens, hud, windows, popups, overlays };
            for (var index = 0; index < layers.Length; index++)
            {
                var layer = layers[index];
                if (layer.parent != transform)
                    throw new InvalidOperationException($"UIRoot layer '{layer.name}' must be a direct child of '{name}'.");

                if (layer.GetSiblingIndex() != index)
                {
                    throw new InvalidOperationException(
                        $"UIRoot layer '{layer.name}' must have sibling index {index}, but has {layer.GetSiblingIndex()}.");
                }
            }

            prefabCatalog.ValidateOrThrow();
        }

        private void Awake()
        {
            ValidateOrThrow();
            Service = new UIService(screens, windows, popups, overlays, prefabCatalog);
        }

        private void OnDestroy()
        {
            Service?.Dispose();
            Service = null;
        }
    }
}
