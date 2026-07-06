using GuildIdle.Player;
using UnityEditor;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;
using RuntimePlayer = GuildIdle.Player.Player;

namespace GuildIdle.Editor.Player
{
    public sealed class PlayerProfileEditor : EditorWindow
    {
        private Vector2 _scroll;
        private string _json;
        private string _status;

        [MenuItem("GuildIdle/Player/Profile Editor")]
        public static void Open()
        {
            var window = GetWindow<PlayerProfileEditor>();
            window.titleContent = new GUIContent("Player Profile");
            window.minSize = new Vector2(520f, 360f);
            window.RefreshFromPlayerPrefs();
            window.Show();
        }

        private void OnEnable()
        {
            RefreshFromPlayerPrefs();
        }

        private void OnGUI()
        {
            if (Application.isPlaying && !PlayerPrefs.HasKey(SaveService.SaveKey) && string.IsNullOrEmpty(_json))
                TryAutoRefreshRuntimeSnapshot();

            DrawToolbar();
            EditorGUILayout.Space(6f);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("PlayerPrefs key", EditorStyles.boldLabel, GUILayout.Width(110f));
                EditorGUILayout.SelectableLabel(SaveService.SaveKey, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(_status, MessageType.Info);
            EditorGUILayout.Space(4f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _json = EditorGUILayout.TextArea(_json ?? string.Empty, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh PlayerPrefs", EditorStyles.toolbarButton))
                    RefreshFromPlayerPrefs();

                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    if (GUILayout.Button("Runtime Snapshot", EditorStyles.toolbarButton))
                        RefreshFromRuntimeSnapshot();

                    if (GUILayout.Button("Save Runtime", EditorStyles.toolbarButton))
                        SaveRuntimeState();

                    if (GUILayout.Button("Create Default Save", EditorStyles.toolbarButton))
                        CreateDefaultSave();
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Write JSON", EditorStyles.toolbarButton))
                    WriteJsonToPlayerPrefs();

                if (GUILayout.Button("Delete Save", EditorStyles.toolbarButton))
                    DeleteSave();
            }
        }

        private void RefreshFromPlayerPrefs()
        {
            if (!PlayerPrefs.HasKey(SaveService.SaveKey))
            {
                _json = string.Empty;
                _status = Application.isPlaying
                    ? "No saved profile in PlayerPrefs. Use Runtime Snapshot or Create Default Save."
                    : "No saved profile in PlayerPrefs. Enter Play Mode to create a default profile.";
                return;
            }

            _json = PrettyJson(PlayerPrefs.GetString(SaveService.SaveKey, string.Empty));
            _status = "Loaded profile JSON from PlayerPrefs.";
        }

        private void RefreshFromRuntimeSnapshot()
        {
            if (!RuntimeConfigs.IsLoaded)
            {
                _status = "Runtime configs are not loaded yet.";
                return;
            }

            if (!RuntimePlayer.IsLoaded && !RuntimePlayer.Load())
            {
                _status = "Runtime player state is not loaded. Enter Play Mode and wait for Configs.";
                return;
            }

            _json = JsonUtility.ToJson(RuntimePlayer.Snapshot(), true);
            _status = "Loaded current runtime Player snapshot.";
        }

        private void SaveRuntimeState()
        {
            if (!Application.isPlaying)
            {
                _status = "Save Runtime is available only in Play Mode.";
                return;
            }

            if (!RuntimePlayer.Save())
            {
                _status = "Runtime save failed. Check Console for details.";
                return;
            }

            RefreshFromPlayerPrefs();
            _status = "Runtime PlayerState saved to PlayerPrefs.";
        }

        private void CreateDefaultSave()
        {
            if (!Application.isPlaying)
            {
                _status = "Create Default Save is available only in Play Mode.";
                return;
            }

            if (!RuntimeConfigs.IsLoaded)
            {
                _status = "Runtime configs are not loaded yet.";
                return;
            }

            if (!RuntimePlayer.ResetSave())
            {
                _status = "Default save creation failed. Check Console for details.";
                return;
            }

            RefreshFromPlayerPrefs();
            _status = "Default Player profile created and saved to PlayerPrefs.";
        }

        private void WriteJsonToPlayerPrefs()
        {
            if (string.IsNullOrWhiteSpace(_json))
            {
                _status = "JSON is empty; nothing was written.";
                return;
            }

            if (!TryParseSaveData(_json, out _))
            {
                _status = "JSON is not valid SaveData; nothing was written.";
                return;
            }

            PlayerPrefs.SetString(SaveService.SaveKey, CompactJson(_json));
            PlayerPrefs.Save();
            RefreshFromPlayerPrefs();
            _status = "JSON written to PlayerPrefs.";
        }

        private void DeleteSave()
        {
            if (!PlayerPrefs.HasKey(SaveService.SaveKey))
            {
                _status = "No saved profile to delete.";
                return;
            }

            if (!EditorUtility.DisplayDialog("Delete Player Profile", "Delete the saved PlayerPrefs profile?", "Delete", "Cancel"))
                return;

            PlayerPrefs.DeleteKey(SaveService.SaveKey);
            PlayerPrefs.Save();
            RefreshFromPlayerPrefs();
            _status = "Saved profile deleted from PlayerPrefs.";
        }

        private static string PrettyJson(string json)
        {
            return TryParseSaveData(json, out var saveData)
                ? JsonUtility.ToJson(saveData, true)
                : json;
        }

        private static string CompactJson(string json)
        {
            return TryParseSaveData(json, out var saveData)
                ? JsonUtility.ToJson(saveData)
                : json;
        }

        private static bool TryParseSaveData(string json, out SaveData saveData)
        {
            saveData = null;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                saveData = JsonUtility.FromJson<SaveData>(json);
                return saveData != null;
            }
            catch
            {
                return false;
            }
        }

        private void TryAutoRefreshRuntimeSnapshot()
        {
            if (!RuntimeConfigs.IsLoaded)
                return;

            if (!RuntimePlayer.IsLoaded && !RuntimePlayer.Load())
                return;

            RefreshFromPlayerPrefs();
        }
    }
}
