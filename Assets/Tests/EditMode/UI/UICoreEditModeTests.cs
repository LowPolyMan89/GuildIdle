using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace GuildIdle.UI.Core.Tests.EditMode
{
    public sealed class EditModeScreen : UIScreen
    {
    }

    public sealed class EditModeWindow : UIWindow
    {
    }

    public sealed class EditModePopupWindow : UIWindow
    {
    }

    public sealed class EditModePanel : UIPanel
    {
    }

    public sealed class EditModeArgs : IUIOpenArgs
    {
    }

    public sealed class EditModeArgsWindow : UIWindow, IUIOpenArgsReceiver<EditModeArgs>
    {
        public void ApplyOpenArgs(EditModeArgs args)
        {
        }
    }

    public sealed class UICoreEditModeTests
    {
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (var index = _created.Count - 1; index >= 0; index--)
                if (_created[index] != null)
                    UnityEngine.Object.DestroyImmediate(_created[index]);

            _created.Clear();
        }

        [Test]
        public void UIRoot_ValidatesLayerReferencesAndOrder()
        {
            var root = CreateRoot(out var catalog, out var layers);

            Assert.DoesNotThrow(root.ValidateOrThrow);
            Assert.That(root.PrefabCatalog, Is.SameAs(catalog));
            Assert.That(root.GetLayer(UILayer.Screen), Is.SameAs(layers[0]));
            Assert.That(root.GetLayer(UILayer.Window), Is.SameAs(layers[1]));
            Assert.That(root.GetLayer(UILayer.Popup), Is.SameAs(layers[2]));
            Assert.That(root.GetLayer(UILayer.Overlay), Is.SameAs(layers[3]));

            layers[3].SetSiblingIndex(0);
            var error = Assert.Throws<InvalidOperationException>(root.ValidateOrThrow);
            Assert.That(error.Message, Does.Contain("sibling index"));
        }

        [TestCase(typeof(EditModeScreen), UILayer.Screen)]
        [TestCase(typeof(EditModeWindow), UILayer.Window)]
        [TestCase(typeof(EditModePopupWindow), UILayer.Popup)]
        public void Catalog_AcceptsSupportedTopLevelRegistration(Type viewType, UILayer layer)
        {
            var prefab = CreateViewObject(viewType);
            var catalog = CreateCatalog(new UIPrefabCatalog.Entry(viewType, prefab, layer));

            Assert.DoesNotThrow(catalog.ValidateOrThrow);
        }

        [Test]
        public void Catalog_RejectsPanelRegistration()
        {
            var prefab = CreateViewObject(typeof(EditModePanel));
            var catalog = CreateCatalog(new UIPrefabCatalog.Entry(typeof(EditModePanel), prefab, UILayer.Window));

            var error = Assert.Throws<InvalidOperationException>(catalog.ValidateOrThrow);
            Assert.That(error.Message, Does.Contain("cannot be registered"));
        }

        [Test]
        public void Catalog_RejectsDuplicateViewType()
        {
            var first = CreateViewObject(typeof(EditModeWindow));
            var second = CreateViewObject(typeof(EditModeWindow));
            var catalog = CreateCatalog(
                new UIPrefabCatalog.Entry(typeof(EditModeWindow), first, UILayer.Window),
                new UIPrefabCatalog.Entry(typeof(EditModeWindow), second, UILayer.Popup));

            var error = Assert.Throws<InvalidOperationException>(catalog.ValidateOrThrow);
            Assert.That(error.Message, Does.Contain("duplicates registered view type"));
        }

        [Test]
        public void Catalog_RejectsNullPrefab()
        {
            var catalog = CreateCatalog(new UIPrefabCatalog.Entry(typeof(EditModeWindow), null, UILayer.Window));

            var error = Assert.Throws<InvalidOperationException>(catalog.ValidateOrThrow);
            Assert.That(error.Message, Does.Contain("null prefab"));
        }

        [Test]
        public void Catalog_RejectsPrefabWithoutUIView()
        {
            var prefab = new GameObject("NotAView");
            _created.Add(prefab);
            var catalog = CreateCatalog(new UIPrefabCatalog.Entry(typeof(EditModeWindow), prefab, UILayer.Window));

            var error = Assert.Throws<InvalidOperationException>(catalog.ValidateOrThrow);
            Assert.That(error.Message, Does.Contain("has no UIView"));
        }

        [Test]
        public void Catalog_RejectsTypePrefabMismatch()
        {
            var prefab = CreateViewObject(typeof(EditModePopupWindow));
            var catalog = CreateCatalog(new UIPrefabCatalog.Entry(typeof(EditModeWindow), prefab, UILayer.Window));

            var error = Assert.Throws<InvalidOperationException>(catalog.ValidateOrThrow);
            Assert.That(error.Message, Does.Contain("but prefab"));
        }

        [Test]
        public void Catalog_RejectsUnresolvableType()
        {
            var prefab = CreateViewObject(typeof(EditModeWindow));
            var entry = new UIPrefabCatalog.Entry(typeof(EditModeWindow), prefab, UILayer.Window);
            SetField(entry.ViewType, "assemblyQualifiedName", "Missing.UIView, Missing.Assembly");
            var catalog = CreateCatalog(entry);

            var error = Assert.Throws<InvalidOperationException>(catalog.ValidateOrThrow);
            Assert.That(error.Message, Does.Contain("cannot be resolved"));
        }

        [TestCase(typeof(EditModeScreen), UILayer.Window)]
        [TestCase(typeof(EditModeWindow), UILayer.Screen)]
        [TestCase(typeof(EditModeWindow), UILayer.Overlay)]
        public void Catalog_RejectsInvalidLayerForViewType(Type viewType, UILayer layer)
        {
            var prefab = CreateViewObject(viewType);
            var catalog = CreateCatalog(new UIPrefabCatalog.Entry(viewType, prefab, layer));

            var error = Assert.Throws<InvalidOperationException>(catalog.ValidateOrThrow);
            Assert.That(error.Message, Does.Contain("must target"));
        }

        [Test]
        public void TypedNavigationOverloads_RequireMatchingArgsReceiver()
        {
            var methods = typeof(UIService).GetMethods(BindingFlags.Instance | BindingFlags.Public);
            AssertTypedOverload(methods, nameof(UIService.ShowScreen), typeof(UIScreen));
            AssertTypedOverload(methods, nameof(UIService.OpenWindow), typeof(UIWindow));
        }

        [Test]
        public void RuntimeAssembly_HasNoGameplayAssemblyDependency()
        {
            var assembly = typeof(UIService).Assembly;
            var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
            var forbiddenPrefixes = new[]
            {
                "GuildIdle.Activities",
                "GuildIdle.Combat",
                "GuildIdle.Player",
                "GuildIdle.Progression",
                "GuildIdle.Settlement"
            };
            var hasGameplayNamespace = assembly.GetTypes()
                .Select(type => type.Namespace)
                .Where(value => value != null)
                .Any(value => forbiddenPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal)));

            Assert.That(assembly.GetName().Name, Is.EqualTo("GuildIdle.UI.Core"));
            Assert.That(references, Does.Not.Contain("Assembly-CSharp"));
            Assert.That(hasGameplayNamespace, Is.False);
        }

        private UIRoot CreateRoot(out UIPrefabCatalog catalog, out RectTransform[] layers)
        {
            var rootObject = new GameObject("UIRoot", typeof(RectTransform));
            rootObject.SetActive(false);
            _created.Add(rootObject);
            var root = rootObject.AddComponent<UIRoot>();
            catalog = CreateCatalog();
            layers = new[]
            {
                CreateLayer(rootObject.transform, "Screens"),
                CreateLayer(rootObject.transform, "Windows"),
                CreateLayer(rootObject.transform, "Popups"),
                CreateLayer(rootObject.transform, "Overlays")
            };
            SetField(root, "screens", layers[0]);
            SetField(root, "windows", layers[1]);
            SetField(root, "popups", layers[2]);
            SetField(root, "overlays", layers[3]);
            SetField(root, "prefabCatalog", catalog);
            return root;
        }

        private UIPrefabCatalog CreateCatalog(params UIPrefabCatalog.Entry[] entries)
        {
            var catalog = ScriptableObject.CreateInstance<UIPrefabCatalog>();
            _created.Add(catalog);
            SetField(catalog, "entries", entries);
            return catalog;
        }

        private GameObject CreateViewObject(Type viewType)
        {
            var value = new GameObject(viewType.Name);
            value.SetActive(false);
            value.AddComponent(viewType);
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

        private static void AssertTypedOverload(IEnumerable<MethodInfo> methods, string methodName, Type baseViewType)
        {
            var method = methods.Single(candidate => candidate.Name == methodName && candidate.GetGenericArguments().Length == 2);
            var genericArguments = method.GetGenericArguments();
            var viewConstraints = genericArguments[0].GetGenericParameterConstraints();
            var argsConstraints = genericArguments[1].GetGenericParameterConstraints();

            Assert.That(viewConstraints, Does.Contain(baseViewType));
            Assert.That(viewConstraints.Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IUIOpenArgsReceiver<>)), Is.True);
            Assert.That(argsConstraints, Does.Contain(typeof(IUIOpenArgs)));
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing field {target.GetType().FullName}.{name}");
            field.SetValue(target, value);
        }
    }
}
