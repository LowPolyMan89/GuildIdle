using System;
using UnityEngine;
using UnityEngine.UI;

public class BottomNavigationPanelView : MonoBehaviour
{
    [SerializeField] private Button _storageButton;

    public event Action InventoryRequested;

    private void OnEnable()
    {
        if (_storageButton != null)
            _storageButton.onClick.AddListener(HandleStorageClicked);
    }

    private void OnDisable()
    {
        if (_storageButton != null)
            _storageButton.onClick.RemoveListener(HandleStorageClicked);
    }

    private void HandleStorageClicked()
    {
        InventoryRequested?.Invoke();
    }
}
