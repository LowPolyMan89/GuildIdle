using TMPro;
using UnityEngine;

namespace GuildIdle
{
    public class LocalisedText : MonoBehaviour
    {
        public TMP_Text TMPText;
        public string Key;

        private void Reset()
        {
            TMPText = GetComponent<TMP_Text>();
        }

        private void Awake()
        {
            EnsureTextReference();
        }

        private void OnEnable()
        {
            LocalisationModel.LanguageChanged += OnLanguageChanged;
            ApplyText();
        }

        private void OnDisable()
        {
            LocalisationModel.LanguageChanged -= OnLanguageChanged;
        }

        public void ApplyText()
        {
            EnsureTextReference();

            if (TMPText == null)
            {
                Debug.LogWarning($"LocalisedText on '{name}' has no TMP_Text reference.", this);
                return;
            }

            TMPText.text = LocalisationModel.GetText(Key);
        }

        private void OnLanguageChanged(string language)
        {
            ApplyText();
        }

        private void EnsureTextReference()
        {
            if (TMPText == null)
                TMPText = GetComponent<TMP_Text>();
        }
    }
}
