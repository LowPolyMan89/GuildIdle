using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildIdle.UI.Core
{
    public abstract class UIView : MonoBehaviour
    {
        private readonly List<Action> _cleanup = new List<Action>();
        private bool _destroyed;

        public bool IsBound { get; private set; }
        public bool IsShown { get; private set; }

        internal void ShowForLifecycle()
        {
            if (_destroyed)
                throw new InvalidOperationException($"Cannot show destroyed UI view '{GetType().FullName}'.");

            if (IsShown)
                return;

            if (!IsBound)
                BindForLifecycle();

            gameObject.SetActive(true);
            IsShown = true;

            try
            {
                OnShow();
            }
            catch
            {
                IsShown = false;
                gameObject.SetActive(false);
                UnbindInternal();
                throw;
            }
        }

        internal void HideForLifecycle()
        {
            if (_destroyed)
                return;

            try
            {
                if (IsShown)
                    OnHide();
            }
            finally
            {
                IsShown = false;
                if (gameObject.activeSelf)
                    gameObject.SetActive(false);

                UnbindInternal();
            }
        }

        protected void RegisterCleanup(Action cleanup)
        {
            if (cleanup == null)
                throw new ArgumentNullException(nameof(cleanup));

            if (!IsBound)
            {
                throw new InvalidOperationException(
                    $"UI view '{GetType().FullName}' can register cleanup only during an active bind scope.");
            }

            _cleanup.Add(cleanup);
        }

        protected virtual void OnBind()
        {
        }

        protected virtual void OnShow()
        {
        }

        protected virtual void OnHide()
        {
        }

        protected virtual void OnUnbind()
        {
        }

        internal void BindForLifecycle(Action applyArguments = null)
        {
            if (_destroyed)
                throw new InvalidOperationException($"Cannot bind destroyed UI view '{GetType().FullName}'.");

            if (IsShown || IsBound)
                HideForLifecycle();

            applyArguments?.Invoke();
            IsBound = true;

            try
            {
                OnBind();
            }
            catch
            {
                UnbindInternal();
                throw;
            }
        }

        private void UnbindInternal()
        {
            if (!IsBound && _cleanup.Count == 0)
                return;

            try
            {
                if (IsBound)
                    OnUnbind();
            }
            finally
            {
                IsBound = false;
                ReleaseCleanup();
            }
        }

        private void ReleaseCleanup()
        {
            Exception firstError = null;
            for (var index = _cleanup.Count - 1; index >= 0; index--)
            {
                try
                {
                    _cleanup[index]?.Invoke();
                }
                catch (Exception error)
                {
                    firstError ??= error;
                }
            }

            _cleanup.Clear();
            if (firstError != null)
                throw new InvalidOperationException($"UI view '{GetType().FullName}' cleanup failed.", firstError);
        }

        private void OnDestroy()
        {
            if (_destroyed)
                return;

            try
            {
                if (IsShown)
                    OnHide();
            }
            finally
            {
                IsShown = false;
                try
                {
                    UnbindInternal();
                }
                finally
                {
                    _destroyed = true;
                }
            }
        }
    }
}
