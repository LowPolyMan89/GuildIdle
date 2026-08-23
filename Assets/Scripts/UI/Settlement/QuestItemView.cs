using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class QuestItemView : MonoBehaviour
{
    private static readonly Color32 CompletedStepColor = new Color32(46, 160, 67, 255);

    [SerializeField] private TMP_Text _questName;
    [SerializeField] private RectTransform _questStepContainer;
    [SerializeField] private TMP_Text _questStepTextTemplate;

    private readonly List<TMP_Text> _stepTexts = new List<TMP_Text>();
    private Color _defaultStepColor;
    private bool _hasDefaultStepColor;

    public void Render(QuestItemState state)
    {
        if (_questName != null)
            _questName.text = state?.Name ?? string.Empty;

        var steps = state?.Steps ?? Array.Empty<QuestStepItemState>();
        var showShortDescription = steps.Count == 0 && !string.IsNullOrWhiteSpace(state?.ShortDescription);
        var itemCount = showShortDescription ? 1 : steps.Count;
        if (!EnsureStepTextCount(itemCount))
            return;

        for (var index = 0; index < itemCount; index++)
        {
            var text = _stepTexts[index];
            if (showShortDescription)
            {
                text.text = state.ShortDescription;
                text.color = _defaultStepColor;
                continue;
            }

            text.text = steps[index].Text;
            text.color = steps[index].Completed ? CompletedStepColor : _defaultStepColor;
        }
    }

    private bool EnsureStepTextCount(int count)
    {
        _stepTexts.RemoveAll(text => text == null);
        if (_questStepContainer == null || _questStepTextTemplate == null)
        {
            if (count > 0)
                Debug.LogError($"[QuestItemView] Step container or template is not assigned on '{name}'.", this);
            return count == 0;
        }

        if (!_hasDefaultStepColor)
        {
            _defaultStepColor = _questStepTextTemplate.color;
            _hasDefaultStepColor = true;
        }

        while (_stepTexts.Count < count)
        {
            var text = Instantiate(_questStepTextTemplate, _questStepContainer, false);
            text.gameObject.SetActive(true);
            _stepTexts.Add(text);
        }

        while (_stepTexts.Count > count)
        {
            var lastIndex = _stepTexts.Count - 1;
            var text = _stepTexts[lastIndex];
            _stepTexts.RemoveAt(lastIndex);
            if (text == null)
                continue;

            text.gameObject.SetActive(false);
            Destroy(text.gameObject);
        }

        return true;
    }
}
