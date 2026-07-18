using GuildIdle.Activities;
using GuildIdle.Player;
using UnityEditor;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;
using RuntimePlayer = GuildIdle.Player.Player;

namespace GuildIdle.Editor.Activities
{
    public sealed class ActivityRuntimeDebugWindow : EditorWindow
    {
        private ActivityRuntimeService _runtime;
        private PlayerState _boundState;
        private string _activityId = "work_pine_wood";
        private string _heroId = "ren";
        private float _tickSeconds = 1f;
        private int _plannedCycleCount = 1;
        private string _selectedExecutionId;
        private Vector2 _scroll;
        private string _lastMessage;

        [MenuItem("GuildIdle/Activities/Runtime Debug")]
        public static void Open()
        {
            GetWindow<ActivityRuntimeDebugWindow>("Activity Runtime");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Activity Runtime", EditorStyles.boldLabel);
            var canUseRuntime = CanUseRuntime();
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Enter Play Mode to use Activity Runtime debug controls.", MessageType.Info);
            else if (!RuntimeConfigs.IsLoaded)
                EditorGUILayout.HelpBox("Runtime configs are not loaded.", MessageType.Warning);
            else if (!RuntimePlayer.IsLoaded)
                EditorGUILayout.HelpBox("Player state is not loaded.", MessageType.Warning);

            _activityId = EditorGUILayout.TextField("Activity Id", _activityId);
            _heroId = EditorGUILayout.TextField("Hero Id", _heroId);
            _tickSeconds = Mathf.Max(0f, EditorGUILayout.FloatField("Tick Seconds", _tickSeconds));
            _plannedCycleCount = Mathf.Max(1, EditorGUILayout.IntField("Planned Cycles", _plannedCycleCount));
            _selectedExecutionId = EditorGUILayout.TextField("Execution Id", _selectedExecutionId);

            EditorGUI.BeginDisabledGroup(!canUseRuntime);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Start"))
                ShowStart(_runtime.Start(new ActivityStartRequest { activityId = _activityId, heroId = _heroId, plannedCycleCount = _plannedCycleCount }));
            if (GUILayout.Button("Tick"))
                ShowTick(_runtime.Tick(_tickSeconds));
            if (GUILayout.Button("Cancel"))
                ShowCancel(_runtime.Cancel(SelectedExecutionId()));
            if (GUILayout.Button("Snapshot"))
                _lastMessage = "Snapshot refreshed.";
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Unlock Required Buildings"))
                UnlockRequiredBuildings();
            EditorGUI.EndDisabledGroup();

            if (!string.IsNullOrWhiteSpace(_lastMessage))
                EditorGUILayout.HelpBox(_lastMessage, MessageType.Info);

            if (!canUseRuntime)
                return;

            DrawHeroState();
            DrawExecutions();
        }

        private bool CanUseRuntime()
        {
            if (!Application.isPlaying || !RuntimeConfigs.IsLoaded || !RuntimePlayer.IsLoaded)
            {
                ReleaseRuntime();
                return false;
            }

            if (_runtime == null || !ReferenceEquals(_boundState, RuntimePlayer.State))
            {
                ReleaseRuntime();
                _runtime = PlayerRuntimeComposition.CreateRuntimeService();
                _boundState = RuntimePlayer.State;
            }

            return true;
        }

        private void OnDisable()
        {
            ReleaseRuntime();
        }

        private void ReleaseRuntime()
        {
            _runtime?.Dispose();
            _runtime = null;
            _boundState = null;
        }

        private void DrawHeroState()
        {
            var heroState = _runtime.GetHeroActivityState(_heroId);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Hero", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Active Hero Limit", _runtime.GetActiveHeroLimit().ToString());
            EditorGUILayout.LabelField("Hero Id", _heroId ?? string.Empty);
            EditorGUILayout.Toggle("Busy", heroState.isBusy);
            EditorGUILayout.LabelField("Current Execution", heroState.currentActivityExecutionId ?? string.Empty);
        }

        private void DrawExecutions()
        {
            var snapshot = _runtime.GetSnapshot();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Running Executions", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var execution in snapshot.executions)
            {
                if (execution == null)
                    continue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.SelectableLabel(execution.executionId, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Select", GUILayout.Width(64f)))
                    _selectedExecutionId = execution.executionId;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("Activity", execution.activityId);
                EditorGUILayout.LabelField("Hero", execution.heroId);
                EditorGUILayout.LabelField("Status", execution.status.ToString());
                EditorGUILayout.LabelField("Elapsed", $"{execution.elapsedSeconds:0.##} / {execution.durationSeconds:0.##}");
                EditorGUILayout.Slider("Progress", execution.progress, 0f, 1f);
                EditorGUILayout.LabelField("Remaining", execution.remainingSeconds.ToString("0.##"));
                EditorGUILayout.LabelField("Completed Cycles", execution.completedCycles.ToString());
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        private string SelectedExecutionId()
        {
            if (!string.IsNullOrWhiteSpace(_selectedExecutionId))
                return _selectedExecutionId;

            var snapshot = _runtime.GetSnapshot();
            return snapshot.executions.Length > 0 ? snapshot.executions[0].executionId : null;
        }

        private void ShowStart(ActivityStartResult result)
        {
            if (result?.success == true)
                _selectedExecutionId = result.executionId;

            _lastMessage = FormatResult("Start", result?.success == true, result?.issues);
        }

        private void ShowTick(ActivityTickResult result)
        {
            _lastMessage = FormatResult("Tick", result?.success == true, result?.issues);
        }

        private void ShowCancel(ActivityCancelResult result)
        {
            if (result?.success == true)
                _selectedExecutionId = null;

            _lastMessage = FormatResult("Cancel", result?.success == true, result?.issues);
        }

        private void UnlockRequiredBuildings()
        {
            var changed = 0;
            foreach (var requirement in RuntimeConfigs.Activities.GetRequirements(_activityId))
            {
                if (requirement == null || !IsBuildingRequirement(requirement.reqType))
                    continue;

                var requiredLevel = Mathf.Max(1, requirement.value);
                if (!RuntimePlayer.IsBuildingUnlocked(requirement.targetId))
                {
                    if (RuntimePlayer.UnlockBuilding(requirement.targetId))
                        changed++;
                }

                if (RuntimePlayer.GetBuildingLevel(requirement.targetId) < requiredLevel &&
                    RuntimePlayer.SetBuildingLevel(requirement.targetId, requiredLevel))
                {
                    changed++;
                }
            }

            if (changed > 0 && RuntimePlayer.Save())
                _lastMessage = $"Unlocked required buildings for '{_activityId}'.";
            else
                _lastMessage = $"No missing building requirements for '{_activityId}'.";
        }

        private static bool IsBuildingRequirement(string requirementType)
        {
            return string.Equals(requirementType, "BuildingLevel", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(requirementType, "Building", System.StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatResult(string action, bool success, ActivityRequirementIssue[] issues)
        {
            if (issues == null || issues.Length == 0)
                return $"{action}: {(success ? "success" : "failed")}.";

            return $"{action}: {(success ? "success" : "failed")} - {issues[0].issueType}: {issues[0].message}";
        }
    }
}
