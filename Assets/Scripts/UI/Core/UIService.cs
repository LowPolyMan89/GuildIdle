using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GuildIdle.UI.Core
{
    public sealed class UIService : IDisposable
    {
        private readonly Dictionary<UILayer, Transform> _layers;
        private readonly Dictionary<Type, UIWindow> _windows = new Dictionary<Type, UIWindow>();
        private readonly UIPrefabCatalog _catalog;
        private UIScreen _currentScreen;
        private Type _currentScreenType;
        private bool _disposed;

        internal UIService(
            Transform screens,
            Transform windows,
            Transform popups,
            Transform overlays,
            UIPrefabCatalog catalog)
        {
            _layers = new Dictionary<UILayer, Transform>
            {
                [UILayer.Screen] = screens != null ? screens : throw new ArgumentNullException(nameof(screens)),
                [UILayer.Window] = windows != null ? windows : throw new ArgumentNullException(nameof(windows)),
                [UILayer.Popup] = popups != null ? popups : throw new ArgumentNullException(nameof(popups)),
                [UILayer.Overlay] = overlays != null ? overlays : throw new ArgumentNullException(nameof(overlays))
            };
            _catalog = catalog != null ? catalog : throw new ArgumentNullException(nameof(catalog));
            _catalog.ValidateOrThrow();
        }

        public TScreen ShowScreen<TScreen>()
            where TScreen : UIScreen
        {
            ThrowIfDisposed();
            var requestedType = typeof(TScreen);
            if (_currentScreenType == requestedType && _currentScreen != null)
            {
                var existing = (TScreen)_currentScreen;
                if (!existing.IsShown)
                {
                    try
                    {
                        BindAndShow(existing, null);
                    }
                    catch
                    {
                        _currentScreen = null;
                        _currentScreenType = null;
                        DestroyView(existing);
                        throw;
                    }
                }

                return existing;
            }

            CloseCurrentScreen();
            var created = CreateView<TScreen>();
            try
            {
                BindAndShow(created, null);
                _currentScreen = created;
                _currentScreenType = requestedType;
                return created;
            }
            catch
            {
                DestroyView(created);
                throw;
            }
        }

        public TScreen ShowScreen<TScreen, TArgs>(TArgs args)
            where TScreen : UIScreen, IUIOpenArgsReceiver<TArgs>
            where TArgs : IUIOpenArgs
        {
            ThrowIfDisposed();
            var requestedType = typeof(TScreen);
            if (_currentScreenType == requestedType && _currentScreen != null)
            {
                var existing = (TScreen)_currentScreen;
                try
                {
                    BindAndShow(existing, () => existing.ApplyOpenArgs(args));
                    return existing;
                }
                catch
                {
                    _currentScreen = null;
                    _currentScreenType = null;
                    DestroyView(existing);
                    throw;
                }
            }

            CloseCurrentScreen();
            var created = CreateView<TScreen>();
            try
            {
                BindAndShow(created, () => created.ApplyOpenArgs(args));
                _currentScreen = created;
                _currentScreenType = requestedType;
                return created;
            }
            catch
            {
                DestroyView(created);
                throw;
            }
        }

        public TWindow OpenWindow<TWindow>()
            where TWindow : UIWindow
        {
            ThrowIfDisposed();
            var requestedType = typeof(TWindow);
            if (_windows.TryGetValue(requestedType, out var existingWindow) && existingWindow != null)
            {
                var existing = (TWindow)existingWindow;
                if (!existing.IsShown)
                {
                    try
                    {
                        BindAndShow(existing, null);
                    }
                    catch
                    {
                        _windows.Remove(requestedType);
                        DestroyView(existing);
                        throw;
                    }
                }

                existing.transform.SetAsLastSibling();
                return existing;
            }

            _windows.Remove(requestedType);
            var created = CreateView<TWindow>();
            try
            {
                BindAndShow(created, null);
                _windows.Add(requestedType, created);
                return created;
            }
            catch
            {
                DestroyView(created);
                throw;
            }
        }

        public TWindow OpenWindow<TWindow, TArgs>(TArgs args)
            where TWindow : UIWindow, IUIOpenArgsReceiver<TArgs>
            where TArgs : IUIOpenArgs
        {
            ThrowIfDisposed();
            var requestedType = typeof(TWindow);
            if (_windows.TryGetValue(requestedType, out var existingWindow) && existingWindow != null)
            {
                var existing = (TWindow)existingWindow;
                try
                {
                    BindAndShow(existing, () => existing.ApplyOpenArgs(args));
                    existing.transform.SetAsLastSibling();
                    return existing;
                }
                catch
                {
                    _windows.Remove(requestedType);
                    DestroyView(existing);
                    throw;
                }
            }

            _windows.Remove(requestedType);
            var created = CreateView<TWindow>();
            try
            {
                BindAndShow(created, () => created.ApplyOpenArgs(args));
                _windows.Add(requestedType, created);
                return created;
            }
            catch
            {
                DestroyView(created);
                throw;
            }
        }

        public bool CloseWindow<TWindow>()
            where TWindow : UIWindow
        {
            ThrowIfDisposed();
            var type = typeof(TWindow);
            if (!_windows.TryGetValue(type, out var window))
                return false;

            _windows.Remove(type);
            DestroyView(window);
            return true;
        }

        public bool IsWindowOpen<TWindow>()
            where TWindow : UIWindow
        {
            ThrowIfDisposed();
            return _windows.TryGetValue(typeof(TWindow), out var window) && window != null && window.IsShown;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Exception firstError = null;
            try
            {
                CloseCurrentScreen();
            }
            catch (Exception error)
            {
                firstError = error;
            }

            var openWindows = new List<UIWindow>(_windows.Values);
            _windows.Clear();
            foreach (var window in openWindows)
            {
                if (window != null)
                {
                    try
                    {
                        DestroyView(window);
                    }
                    catch (Exception error)
                    {
                        firstError ??= error;
                    }
                }
            }

            if (firstError != null)
                throw new InvalidOperationException("One or more UI views failed during UIService disposal.", firstError);
        }

        private TView CreateView<TView>()
            where TView : UIView
        {
            var registration = _catalog.ResolveOrThrow(typeof(TView));
            var parent = _layers[registration.TargetLayer];
            var instance = Object.Instantiate(registration.Prefab, parent, false);
            if (instance.GetType() != typeof(TView))
            {
                DestroyView(instance);
                throw new InvalidOperationException(
                    $"UI catalog resolved '{registration.ViewType.FullName}', but instantiated '{instance.GetType().FullName}'.");
            }

            instance.gameObject.SetActive(false);
            instance.transform.SetAsLastSibling();
            return (TView)instance;
        }

        private static void BindAndShow(UIView view, Action applyArguments)
        {
            view.BindForNavigation(applyArguments);
            view.Show();
        }

        private void CloseCurrentScreen()
        {
            var screen = _currentScreen;
            _currentScreen = null;
            _currentScreenType = null;
            if (screen != null)
                DestroyView(screen);
        }

        private static void DestroyView(UIView view)
        {
            if (view == null)
                return;

            try
            {
                view.Hide();
            }
            finally
            {
                if (Application.isPlaying)
                    Object.Destroy(view.gameObject);
                else
                    Object.DestroyImmediate(view.gameObject);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UIService));
        }
    }
}
