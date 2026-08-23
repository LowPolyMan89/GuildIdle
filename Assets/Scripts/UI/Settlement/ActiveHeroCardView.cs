using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ActiveHeroCardView : MonoBehaviour
{
    [SerializeField] private TMP_Text _heroNameText;
    [SerializeField] private Image _heroIconImage;

    [SerializeField] private CardWorkView _cardWorkView;

    public void Render(ActiveHeroCardState state)
    {
        state ??= ActiveHeroCardState.Empty;

        if (_heroNameText != null)
            _heroNameText.text = state.HeroName;

        _cardWorkView?.Render(state);
    }

    [System.Serializable]
    public class CardWorkView
    {
        [SerializeField] private TMP_Text _workNameText;
        [SerializeField] private Slider _workProgressSlider;
        [SerializeField] private TMP_Text _workCycleText;
        [SerializeField] private TMP_Text _workTimeText;
        [SerializeField] private Image _workImageIcon;

        public void Render(ActiveHeroCardState state)
        {
            if (_workNameText != null)
                _workNameText.text = state.WorkName;
            if (_workProgressSlider != null)
                _workProgressSlider.normalizedValue = Mathf.Clamp01(state.Progress);
            if (_workCycleText != null)
                _workCycleText.text = state.Cycle;
            if (_workTimeText != null)
                _workTimeText.text = state.RemainingTime;
        }
    }
}
