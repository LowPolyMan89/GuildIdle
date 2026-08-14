using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;
using RuntimePlayer = GuildIdle.Player.Player;

namespace GuildIdle.Settlement
{
    public interface ISettlementSceneRuntimeSource
    {
        bool IsReady { get; }
        string CurrentStageId { get; }
        SettlementStageBuildingConfigDto[] GetStageBuildings(string stageId);
        bool TryGetBuildingLevel(string buildingId, out int level);
    }

    public sealed class SettlementSceneView : MonoBehaviour
    {
        [SerializeField] private Transform buildingRoot;
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private SettlementStageViewCatalog stageViewCatalog;

        private readonly Dictionary<string, BuildingView> _buildingViews = new Dictionary<string, BuildingView>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _appliedLevels = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _activeBuildingIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _reportedDiagnostics = new HashSet<string>(StringComparer.Ordinal);
        private BuildingView[] _allBuildingViews = Array.Empty<BuildingView>();
        private ISettlementSceneRuntimeSource _runtimeSource;
        private string _appliedStageId;
        private bool _lookupBuilt;

        public string AppliedStageId => _appliedStageId;

        private void Awake()
        {
            _runtimeSource ??= new RuntimeSettlementSceneSource();
            BuildBuildingLookup();
        }

        private void OnEnable()
        {
            RuntimeConfigs.OnLoaded += HandleConfigsLoaded;
            RefreshNow();
        }

        private void OnDisable()
        {
            RuntimeConfigs.OnLoaded -= HandleConfigsLoaded;
        }

        private void Update()
        {
            RefreshNow();
        }

        public void SetRuntimeSource(ISettlementSceneRuntimeSource runtimeSource)
        {
            _runtimeSource = runtimeSource ?? throw new ArgumentNullException(nameof(runtimeSource));
            _appliedStageId = null;
            _appliedLevels.Clear();
            RefreshNow();
        }

        public void RefreshNow()
        {
            if (!_lookupBuilt)
                BuildBuildingLookup();

            if (_runtimeSource == null || !_runtimeSource.IsReady)
                return;

            var stageId = _runtimeSource.CurrentStageId;
            if (string.IsNullOrWhiteSpace(stageId))
                return;

            if (!string.Equals(_appliedStageId, stageId, StringComparison.Ordinal))
                ApplyStage(stageId);
            else
                RefreshBuildingLevels(stageId);
        }

        private void BuildBuildingLookup()
        {
            _buildingViews.Clear();
            _allBuildingViews = (buildingRoot != null ? buildingRoot : transform)
                .GetComponentsInChildren<BuildingView>(true);

            foreach (var view in _allBuildingViews)
            {
                if (view == null)
                    continue;

                var buildingId = view.BuildingId;
                if (string.IsNullOrWhiteSpace(buildingId))
                {
                    Debug.LogError($"[SettlementSceneView] BuildingView on '{view.name}' has empty building_id.", view);
                    view.SetStageActive(false);
                    continue;
                }

                if (_buildingViews.ContainsKey(buildingId))
                {
                    Debug.LogError($"[SettlementSceneView] Duplicate BuildingView building_id '{buildingId}'.", view);
                    view.SetStageActive(false);
                    continue;
                }

                _buildingViews.Add(buildingId, view);
            }

            _lookupBuilt = true;
        }

        private void ApplyStage(string stageId)
        {
            _activeBuildingIds.Clear();
            _appliedLevels.Clear();

            foreach (var membership in _runtimeSource.GetStageBuildings(stageId) ?? Array.Empty<SettlementStageBuildingConfigDto>())
            {
                if (membership != null && membership.enabled && !string.IsNullOrWhiteSpace(membership.buildingId))
                    _activeBuildingIds.Add(membership.buildingId);
            }

            foreach (var view in _allBuildingViews)
            {
                if (view == null || string.IsNullOrWhiteSpace(view.BuildingId))
                    continue;

                var isRegistered = _buildingViews.TryGetValue(view.BuildingId, out var registered) && ReferenceEquals(registered, view);
                view.SetStageActive(isRegistered && _activeBuildingIds.Contains(view.BuildingId));
            }

            foreach (var buildingId in _activeBuildingIds)
            {
                if (!_buildingViews.ContainsKey(buildingId))
                    ReportOnce($"view:{stageId}:{buildingId}", $"[SettlementSceneView] Active building '{buildingId}' for stage '{stageId}' has no BuildingView in scene.");
            }

            ApplyCameraPreset(stageId);
            _appliedStageId = stageId;
            RefreshBuildingLevels(stageId);
        }

        private void RefreshBuildingLevels(string stageId)
        {
            foreach (var buildingId in _activeBuildingIds)
            {
                if (!_buildingViews.TryGetValue(buildingId, out var view))
                    continue;

                var diagnosticKey = $"state:{stageId}:{buildingId}";
                if (!_runtimeSource.TryGetBuildingLevel(buildingId, out var level))
                {
                    view.ClearVisuals();
                    _appliedLevels.Remove(buildingId);
                    ReportOnce(diagnosticKey, $"[SettlementSceneView] Active building '{buildingId}' for stage '{stageId}' has no PlayerState building state.");
                    continue;
                }

                _reportedDiagnostics.Remove(diagnosticKey);
                if (_appliedLevels.TryGetValue(buildingId, out var appliedLevel) && appliedLevel == level)
                    continue;

                if (view.Initialize(level))
                    _appliedLevels[buildingId] = level;
                else
                    _appliedLevels.Remove(buildingId);
            }
        }

        private void ApplyCameraPreset(string stageId)
        {
            if (sceneCamera == null || stageViewCatalog == null || !stageViewCatalog.TryGetCameraPreset(stageId, out var preset))
            {
                ReportOnce($"camera:{stageId}", $"[SettlementSceneView] Stage '{stageId}' has no camera preset.");
                return;
            }

            _reportedDiagnostics.Remove($"camera:{stageId}");
            preset.Apply(sceneCamera);
        }

        private void HandleConfigsLoaded()
        {
            _appliedStageId = null;
            RefreshNow();
        }

        private void ReportOnce(string key, string message)
        {
            if (_reportedDiagnostics.Add(key))
                Debug.LogError(message, this);
        }

        private sealed class RuntimeSettlementSceneSource : ISettlementSceneRuntimeSource
        {
            public bool IsReady => RuntimeConfigs.IsLoaded && RuntimePlayer.IsLoaded && RuntimePlayer.State != null;
            public string CurrentStageId => RuntimePlayer.State?.CurrentStageId;

            public SettlementStageBuildingConfigDto[] GetStageBuildings(string stageId)
            {
                return RuntimeConfigs.Buildings.GetSettlementStageBuildings(stageId);
            }

            public bool TryGetBuildingLevel(string buildingId, out int level)
            {
                level = 0;
                return RuntimePlayer.State != null && RuntimePlayer.State.TryGetBuildingLevelState(buildingId, out level);
            }
        }
    }
}
