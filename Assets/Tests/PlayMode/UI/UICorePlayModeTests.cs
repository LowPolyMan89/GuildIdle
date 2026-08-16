using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GuildIdle.UI.Core.Tests.PlayMode
{
    public sealed class PlayModeArgs : IUIOpenArgs
    {
        public PlayModeArgs(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    public sealed class PlayModeScreen : UIScreen
    {
    }

    public sealed class AlternatePlayModeScreen : UIScreen
    {
    }

    public sealed class PlayModeWindow : UIWindow, IUIOpenArgsReceiver<PlayModeArgs>
    {
        public static int ActiveSubscriptions { get; private set; }
        public int LastArgs { get; private set; }
        public int BindCount { get; private set; }
        public int CleanupCount { get; private set; }

        public static void ResetTracking()
        {
            ActiveSubscriptions = 0;
        }

        public void ApplyOpenArgs(PlayModeArgs args)
        {
            LastArgs = args.Value;
        }

        protected override void OnBind()
        {
            BindCount++;
            ActiveSubscriptions++;
            RegisterCleanup(() =>
            {
                ActiveSubscriptions--;
                CleanupCount++;
            });
        }
    }

    public sealed class PlayModePopupWindow : UIWindow
    {
    }

    public sealed class UICorePlayModeTests
    {
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PlayModeWindow.ResetTracking();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (var index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null)
                    UnityEngine.Object.Destroy(_created[index]);

            _created.Clear();
            yield return null;
            Assert.That(PlayModeWindow.ActiveSubscriptions, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Views_AreCreatedUnderRegisteredLayers()
        {
            var setup = CreateSetup();

            var screen = setup.Service.ShowScreen<PlayModeScreen>();
            var window = setup.Service.OpenWindow<PlayModeWindow>();
            var popup = setup.Service.OpenWindow<PlayModePopupWindow>();

            Assert.That(screen.transform.parent, Is.SameAs(setup.Screens));
            Assert.That(window.transform.parent, Is.SameAs(setup.Windows));
            Assert.That(popup.transform.parent, Is.SameAs(setup.Popups));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RepeatedOpen_ReusesInstanceAndRebindsWithoutDuplicateSubscriptions()
        {
            var setup = CreateSetup();
            var first = setup.Service.OpenWindow<PlayModeWindow, PlayModeArgs>(new PlayModeArgs(1));

            Assert.That(first.LastArgs, Is.EqualTo(1));
            Assert.That(first.BindCount, Is.EqualTo(1));
            Assert.That(PlayModeWindow.ActiveSubscriptions, Is.EqualTo(1));

            var second = setup.Service.OpenWindow<PlayModeWindow, PlayModeArgs>(new PlayModeArgs(2));

            Assert.That(second, Is.SameAs(first));
            Assert.That(second.LastArgs, Is.EqualTo(2));
            Assert.That(second.BindCount, Is.EqualTo(2));
            Assert.That(second.CleanupCount, Is.EqualTo(1));
            Assert.That(PlayModeWindow.ActiveSubscriptions, Is.EqualTo(1));
            Assert.That(setup.Windows.childCount, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator CloseWindow_DestroysInstanceAndNextOpenCreatesNewOne()
        {
            var setup = CreateSetup();
            var first = setup.Service.OpenWindow<PlayModeWindow, PlayModeArgs>(new PlayModeArgs(1));
            var firstId = first.GetInstanceID();

            Assert.That(setup.Service.CloseWindow<PlayModeWindow>(), Is.True);
            Assert.That(setup.Service.IsWindowOpen<PlayModeWindow>(), Is.False);
            Assert.That(PlayModeWindow.ActiveSubscriptions, Is.Zero);
            yield return null;
            Assert.That(first == null, Is.True);

            var second = setup.Service.OpenWindow<PlayModeWindow, PlayModeArgs>(new PlayModeArgs(2));
            Assert.That(second.GetInstanceID(), Is.Not.EqualTo(firstId));
            Assert.That(PlayModeWindow.ActiveSubscriptions, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ScreenReuseAndSwitch_KeepOnlyOneActiveScreen()
        {
            var setup = CreateSetup();
            var first = setup.Service.ShowScreen<PlayModeScreen>();
            var repeated = setup.Service.ShowScreen<PlayModeScreen>();

            Assert.That(repeated, Is.SameAs(first));
            Assert.That(setup.Screens.childCount, Is.EqualTo(1));

            var alternate = setup.Service.ShowScreen<AlternatePlayModeScreen>();
            Assert.That(alternate, Is.Not.SameAs(first));
            yield return null;

            Assert.That(first == null, Is.True);
            Assert.That(setup.Screens.childCount, Is.EqualTo(1));
            Assert.That(alternate.IsShown, Is.True);
        }

        [UnityTest]
        public IEnumerator Dispose_DestroysManagedViewsAndRejectsFurtherNavigation()
        {
            var setup = CreateSetup();
            var screen = setup.Service.ShowScreen<PlayModeScreen>();
            var window = setup.Service.OpenWindow<PlayModeWindow, PlayModeArgs>(new PlayModeArgs(1));
            setup.Service.OpenWindow<PlayModePopupWindow>();

            setup.Service.Dispose();

            Assert.That(PlayModeWindow.ActiveSubscriptions, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => setup.Service.IsWindowOpen<PlayModeWindow>());
            yield return null;

            Assert.That(screen == null, Is.True);
            Assert.That(window == null, Is.True);
            Assert.That(setup.Screens.childCount, Is.Zero);
            Assert.That(setup.Windows.childCount, Is.Zero);
            Assert.That(setup.Popups.childCount, Is.Zero);
        }

        private Setup CreateSetup()
        {
            var screenPrefab = CreateViewPrefab<PlayModeScreen>();
            var alternateScreenPrefab = CreateViewPrefab<AlternatePlayModeScreen>();
            var windowPrefab = CreateViewPrefab<PlayModeWindow>();
            var popupPrefab = CreateViewPrefab<PlayModePopupWindow>();
            var catalog = ScriptableObject.CreateInstance<UIPrefabCatalog>();
            _created.Add(catalog);
            SetField(catalog, "entries", new[]
            {
                new UIPrefabCatalog.Entry(typeof(PlayModeScreen), screenPrefab, UILayer.Screen),
                new UIPrefabCatalog.Entry(typeof(AlternatePlayModeScreen), alternateScreenPrefab, UILayer.Screen),
                new UIPrefabCatalog.Entry(typeof(PlayModeWindow), windowPrefab, UILayer.Window),
                new UIPrefabCatalog.Entry(typeof(PlayModePopupWindow), popupPrefab, UILayer.Popup)
            });

            var rootObject = new GameObject("UIRoot", typeof(RectTransform));
            rootObject.SetActive(false);
            _created.Add(rootObject);
            var root = rootObject.AddComponent<UIRoot>();
            var screens = CreateLayer(rootObject.transform, "Screens");
            var windows = CreateLayer(rootObject.transform, "Windows");
            var popups = CreateLayer(rootObject.transform, "Popups");
            var overlays = CreateLayer(rootObject.transform, "Overlays");
            SetField(root, "screens", screens);
            SetField(root, "windows", windows);
            SetField(root, "popups", popups);
            SetField(root, "overlays", overlays);
            SetField(root, "prefabCatalog", catalog);
            rootObject.SetActive(true);

            return new Setup(root.Service, screens, windows, popups);
        }

        private GameObject CreateViewPrefab<TView>()
            where TView : UIView
        {
            var value = new GameObject(typeof(TView).Name);
            value.SetActive(false);
            value.AddComponent<TView>();
            _created.Add(value);
            return value;
        }

        private static RectTransform CreateLayer(Transform parent, string name)
        {
            var value = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            value.SetParent(parent, false);
            value.anchorMin = Vector2.zero;
            value.anchorMax = Vector2.one;
            value.offsetMin = Vector2.zero;
            value.offsetMax = Vector2.zero;
            return value;
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {target.GetType().FullName}.{name}");
            field.SetValue(target, value);
        }

        private sealed class Setup
        {
            public Setup(UIService service, RectTransform screens, RectTransform windows, RectTransform popups)
            {
                Service = service;
                Screens = screens;
                Windows = windows;
                Popups = popups;
            }

            public UIService Service { get; }
            public RectTransform Screens { get; }
            public RectTransform Windows { get; }
            public RectTransform Popups { get; }
        }
    }
}
