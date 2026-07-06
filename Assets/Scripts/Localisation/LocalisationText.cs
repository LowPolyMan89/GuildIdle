using TMPro;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Localisation
{
    [DisallowMultipleComponent]
    public sealed class LocalisationText : MonoBehaviour
    {
        [SerializeField] private TMP_Text TMP;
        [SerializeField] private string id;

        private bool _warnedMissingTmp;

        public TMP_Text Text
        {
            get => TMP;
            set => TMP = value;
        }

        public string Id
        {
            get => id;
            set
            {
                id = value;
                Refresh();
            }
        }

        public void Refresh()
        {
            if (!isActiveAndEnabled || !RuntimeConfigs.IsLoaded)
                return;

            if (!TryEnsureText())
                return;

            CaptureIdIfEmpty();
            if (string.IsNullOrWhiteSpace(id))
                return;

            TMP.text = RuntimeConfigs.Localisation.Get(id);
        }

        public void Check()
        {
            if (!TryEnsureText())
                return;

            CaptureIdIfEmpty();
            if (string.IsNullOrWhiteSpace(id))
                return;

            if (Application.isPlaying &&
                RuntimeConfigs.IsLoaded &&
                RuntimeConfigs.Localisation.TryGet(id, out var text))
            {
                TMP.text = text;
                return;
            }

            TMP.text = id;
        }

        public void ApplyCheckedText(string text)
        {
            if (!TryEnsureText())
                return;

            CaptureIdIfEmpty();
            TMP.text = string.IsNullOrEmpty(text) ? id : text;
        }

        private void OnEnable()
        {
            TryEnsureText();
            CaptureIdIfEmpty();

            RuntimeConfigs.OnLoaded += Refresh;
            LocalisationService.LanguageChanged += HandleLanguageChanged;

            if (RuntimeConfigs.IsLoaded)
                Refresh();
            else
                RuntimeConfigs.WaitUntilLoaded(Refresh);
        }

        private void OnDisable()
        {
            RuntimeConfigs.OnLoaded -= Refresh;
            LocalisationService.LanguageChanged -= HandleLanguageChanged;
        }

        private void Reset()
        {
            TryEnsureText();
            CaptureIdIfEmpty();
        }

        private void OnValidate()
        {
            TryEnsureText();
            CaptureIdIfEmpty();
        }

        private void HandleLanguageChanged(string lang)
        {
            Refresh();
        }

        private bool TryEnsureText()
        {
            if (TMP == null)
                TMP = GetComponent<TMP_Text>();

            if (TMP != null)
                return true;

            if (!_warnedMissingTmp)
            {
                Debug.LogWarning($"[LocalisationText] Missing TMP_Text on '{name}'.", this);
                _warnedMissingTmp = true;
            }

            return false;
        }

        private void CaptureIdIfEmpty()
        {
            if (TMP == null || !string.IsNullOrWhiteSpace(id))
                return;

            id = TMP.text;
        }
    }
}
