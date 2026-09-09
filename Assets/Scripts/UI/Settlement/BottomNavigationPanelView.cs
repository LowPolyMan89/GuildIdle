using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GuildIdle.Localisation;
using RuntimeConfigs = GuildIdle.Configs.Configs;

public class BottomNavigationPanelView : MonoBehaviour
{
    [SerializeField] private Button _storageButton;

    public event Action InventoryRequested;

    private void OnEnable()
    {
        RuntimeConfigs.OnLoaded += RefreshLabel;
        LocalisationService.LanguageChanged += HandleLanguageChanged;
        RefreshLabel();
        if (_storageButton != null)
            _storageButton.onClick.AddListener(HandleStorageClicked);
    }

    private void OnDisable()
    {
        RuntimeConfigs.OnLoaded -= RefreshLabel;
        LocalisationService.LanguageChanged -= HandleLanguageChanged;
        if (_storageButton != null)
            _storageButton.onClick.RemoveListener(HandleStorageClicked);
    }

    private void HandleStorageClicked()
    {
        InventoryRequested?.Invoke();
    }

    private void HandleLanguageChanged(string language) => RefreshLabel();

    private void RefreshLabel()
    {
        var label = _storageButton != null ? _storageButton.GetComponentInChildren<TMP_Text>(true) : null;
        if (label != null) label.text = StorageUiText.Get("button", "Склад");
    }
}
