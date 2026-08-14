using System;
using UnityEngine;

namespace GuildIdle.Settlement
{
    public sealed class BuildingView : MonoBehaviour
    {
        [Serializable]
        public sealed class LevelVisual
        {
            [SerializeField] private int level;
            [SerializeField] private GameObject visual;

            public int Level => level;
            public GameObject Visual => visual;
        }

        [SerializeField] private string buildingId;
        [SerializeField] private LevelVisual[] levelVisuals = Array.Empty<LevelVisual>();

        public string BuildingId => buildingId;
        public int? CurrentLevel { get; private set; }

        public bool Initialize(int level)
        {
            GameObject selected = null;
            var matches = 0;

            foreach (var entry in levelVisuals ?? Array.Empty<LevelVisual>())
            {
                if (entry?.Visual != null)
                    entry.Visual.SetActive(false);

                if (entry != null && entry.Level == level && entry.Visual != null)
                {
                    selected = entry.Visual;
                    matches++;
                }
            }

            CurrentLevel = null;
            if (matches != 1)
            {
                Debug.LogError(
                    matches == 0
                        ? $"[BuildingView] Building '{buildingId}' has no visual for level {level}."
                        : $"[BuildingView] Building '{buildingId}' has {matches} visuals for level {level}; exactly one is required.",
                    this);
                return false;
            }

            selected.SetActive(true);
            CurrentLevel = level;
            return true;
        }

        public void ClearVisuals()
        {
            foreach (var entry in levelVisuals ?? Array.Empty<LevelVisual>())
                if (entry?.Visual != null)
                    entry.Visual.SetActive(false);

            CurrentLevel = null;
        }

        public void SetStageActive(bool active)
        {
            gameObject.SetActive(active);
            if (!active)
                CurrentLevel = null;
        }
    }
}
