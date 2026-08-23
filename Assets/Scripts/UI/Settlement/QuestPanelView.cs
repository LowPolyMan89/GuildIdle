using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class QuestPanelView : MonoBehaviour
{
    [SerializeField] private QuestItemView _questItemViewPrefab;
    [SerializeField] private List<QuestItemView> _activeQuests = new List<QuestItemView>();
    [SerializeField] private StageInfoElement _stageInfoElement;

    public void Render(StageInfoState stage, IReadOnlyList<QuestItemState> quests)
    {
        _stageInfoElement?.Render(stage ?? StageInfoState.Empty);

        quests ??= System.Array.Empty<QuestItemState>();
        if (!EnsureItemCount(quests.Count))
            return;

        for (var index = 0; index < quests.Count; index++)
            _activeQuests[index].Render(quests[index]);
    }

    private bool EnsureItemCount(int count)
    {
        _activeQuests.RemoveAll(view => view == null);

        if (_questItemViewPrefab == null)
        {
            if (count > 0)
                Debug.LogError($"[QuestPanelView] Quest item prefab is not assigned on '{name}'.", this);
            return count == 0;
        }

        while (_activeQuests.Count < count)
            _activeQuests.Add(Instantiate(_questItemViewPrefab, transform, false));

        while (_activeQuests.Count > count)
        {
            var lastIndex = _activeQuests.Count - 1;
            var view = _activeQuests[lastIndex];
            _activeQuests.RemoveAt(lastIndex);
            if (view != null)
                Destroy(view.gameObject);
        }

        return true;
    }
    
    [System.Serializable]
    public class StageInfoElement
    {
        public TMP_Text _stageName;
        public TMP_Text _stagePersent;
        public Slider _stageProgress;

        public void Render(StageInfoState state)
        {
            if (_stageName != null)
                _stageName.text = state.Name;
            if (_stagePersent != null)
                _stagePersent.text = state.ProgressText;
            if (_stageProgress != null)
                _stageProgress.normalizedValue = Mathf.Clamp01(state.Progress);
        }
    }
}
