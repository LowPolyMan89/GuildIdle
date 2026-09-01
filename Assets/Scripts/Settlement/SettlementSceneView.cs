using System;
using System.Collections.Generic;
using GuildIdle.Configs;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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

    public readonly struct BuildingSelectionContext
    {
        public BuildingSelectionContext(string buildingId, int buildingLevel)
        {
            BuildingId = buildingId ?? string.Empty;
            BuildingLevel = buildingLevel;
        }

        public string BuildingId { get; }
        public int BuildingLevel { get; }
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
        private readonly List<RaycastResult> _uiRaycastResults = new List<RaycastResult>();
        private BuildingView[] _allBuildingViews = Array.Empty<BuildingView>();
        private ISettlementSceneRuntimeSource _runtimeSource;
        private ObjectActivitiesController _activitiesController;
        private string _appliedStageId;
        private bool _lookupBuilt;
        private bool _selectionBlocked;

        public string AppliedStageId => _appliedStageId;
        public event Action<BuildingSelectionContext> BuildingSelected;

        private void Awake()
        {
            _runtimeSource ??= new RuntimeSettlementSceneSource();
            BuildBuildingLookup();
            _activitiesController ??= new ObjectActivitiesController(this);
        }

        private void OnEnable()
        {
            RuntimeConfigs.OnLoaded += HandleConfigsLoaded;
            _activitiesController ??= new ObjectActivitiesController(this);
            _activitiesController.Bind();
            RefreshNow();
        }

        private void OnDisable()
        {
            RuntimeConfigs.OnLoaded -= HandleConfigsLoaded;
            _activitiesController?.Dispose();
        }

        private void Update()
        {
            RefreshNow();
            _activitiesController?.Tick();
            HandleSelectionInput();
        }

        public void SetSelectionBlocked(bool blocked)
        {
            _selectionBlocked = blocked;
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

        private void HandleSelectionInput()
        {
            if (_selectionBlocked || sceneCamera == null || !TryGetPointerPress(out var screenPosition))
                return;

            if (IsPointerOverUi(screenPosition))
                return;

            var ray = sceneCamera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out var hit, sceneCamera.farClipPlane, ~0, QueryTriggerInteraction.Collide))
                return;

            var view = hit.collider != null ? hit.collider.GetComponentInParent<BuildingView>() : null;
            if (view == null || !view.CurrentLevel.HasValue || !view.gameObject.activeInHierarchy)
                return;

            if (!_buildingViews.TryGetValue(view.BuildingId, out var registered) ||
                !ReferenceEquals(registered, view) ||
                !_activeBuildingIds.Contains(view.BuildingId))
            {
                return;
            }

            BuildingSelected?.Invoke(new BuildingSelectionContext(view.BuildingId, view.CurrentLevel.Value));
        }

        private bool IsPointerOverUi(Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return false;

            _uiRaycastResults.Clear();
            eventSystem.RaycastAll(
                new PointerEventData(eventSystem) { position = screenPosition },
                _uiRaycastResults);
            return _uiRaycastResults.Count > 0;
        }

        private static bool TryGetPointerPress(out Vector2 screenPosition)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
            {
                screenPosition = touchscreen.primaryTouch.position.ReadValue();
                return true;
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                screenPosition = mouse.position.ReadValue();
                return true;
            }

            screenPosition = default;
            return false;
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
