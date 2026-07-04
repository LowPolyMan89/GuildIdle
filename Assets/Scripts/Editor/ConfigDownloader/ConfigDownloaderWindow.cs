using UnityEditor;
using UnityEngine;

namespace GuildIdle.Editor.ConfigDownloader
{
    public sealed class ConfigDownloaderWindow : EditorWindow
    {
        private ConfigSourceSettingsCollection _settings;
        private Vector2 _scroll;
        private bool _dirty;

        [MenuItem("Tools/Configs/Config Pipeline")]
        public static void Open()
        {
            var window = GetWindow<ConfigDownloaderWindow>();
            window.titleContent = new GUIContent("Config Pipeline");
            window.minSize = new Vector2(1040f, 620f);
            window.Show();
        }

        [MenuItem("Tools/Configs/Config Downloader")]
        public static void OpenLegacyMenu()
        {
            Open();
        }

        private void OnEnable()
        {
            LoadSettings();
        }

        private void OnGUI()
        {
            if (_settings == null)
                LoadSettings();

            DrawToolbar();

            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox(
                "Editor-only config pipeline. Downloads use Google Sheets; parse and validate use local raw JSON only. Runtime/WebGL builds load generated JSON from Assets.",
                MessageType.Info);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_settings?.sources != null)
            {
                foreach (var source in _settings.sources)
                    DrawSource(source);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Reload Settings", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                LoadSettings();

            EditorGUI.BeginDisabledGroup(!_dirty);
            if (GUILayout.Button("Save Settings", EditorStyles.toolbarButton, GUILayout.Width(105f)))
                SaveSettings();
            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Download Enabled", EditorStyles.toolbarButton, GUILayout.Width(135f)))
            {
                GoogleSheetConfigDownloader.DownloadEnabled(_settings);
                _dirty = false;
                Repaint();
            }

            if (GUILayout.Button("Parse Enabled", EditorStyles.toolbarButton, GUILayout.Width(110f)))
            {
                ConfigPipelineOperations.ParseEnabled(_settings);
                _dirty = false;
                Repaint();
            }

            if (GUILayout.Button("Validate Enabled", EditorStyles.toolbarButton, GUILayout.Width(120f)))
            {
                ConfigPipelineOperations.ValidateEnabled(_settings);
                _dirty = false;
                Repaint();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSource(ConfigSourceSettings source)
        {
            if (source == null)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            source.enabled = DrawChangedToggle("Enabled", source.enabled, GUILayout.Width(80f));
            EditorGUILayout.LabelField(source.display_name, EditorStyles.boldLabel, GUILayout.MinWidth(240f));
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Download", GUILayout.Width(90f)))
            {
                GoogleSheetConfigDownloader.Download(source);
                ConfigSourceSettingsStore.Save(_settings);
                _dirty = false;
                Repaint();
            }

            if (GUILayout.Button("Parse", GUILayout.Width(80f)))
            {
                ConfigPipelineOperations.Parse(source);
                ConfigSourceSettingsStore.Save(_settings);
                _dirty = false;
                Repaint();
            }

            if (GUILayout.Button("Validate", GUILayout.Width(85f)))
            {
                ConfigPipelineOperations.Validate(source);
                ConfigSourceSettingsStore.Save(_settings);
                _dirty = false;
                Repaint();
            }

            EditorGUILayout.EndHorizontal();

            DrawStatusRow("download", source.last_download_status, source.last_download_time);
            DrawStatusRow("parse", source.last_parse_status, source.last_parse_time);
            DrawStatusRow("validation", source.last_validation_status, source.last_validation_time);

            source.config_id = DrawChangedTextField("config_id", source.config_id);
            source.display_name = DrawChangedTextField("display_name", source.display_name);
            source.sheet_url = DrawChangedTextField("sheet_url", source.sheet_url);
            source.source_type = DrawChangedTextField("source_type", source.source_type);
            source.output_json_path = DrawChangedTextField("raw output_json_path", source.output_json_path);
            source.runtime_json_path = DrawChangedTextField("runtime_json_path", source.runtime_json_path);

            if (!ConfigPipelineOperations.HasParser(source))
                EditorGUILayout.HelpBox("Parse/Validate are not implemented for this source yet.", MessageType.None);

            if (!string.IsNullOrWhiteSpace(source.error_message))
            {
                var previousColor = GUI.color;
                GUI.color = new Color(1f, 0.55f, 0.45f);
                EditorGUILayout.TextArea(source.error_message, GUILayout.MinHeight(34f));
                GUI.color = previousColor;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawStatusRow(string label, string status, string time)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(90f));
            DrawStatus(status, GUILayout.Width(130f));
            EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(time) ? "-" : time);
            EditorGUILayout.EndHorizontal();
        }

        private string DrawChangedTextField(string label, string value)
        {
            EditorGUI.BeginChangeCheck();
            var nextValue = EditorGUILayout.TextField(label, value ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
                _dirty = true;

            return nextValue;
        }

        private bool DrawChangedToggle(string label, bool value, params GUILayoutOption[] options)
        {
            EditorGUI.BeginChangeCheck();
            var nextValue = EditorGUILayout.ToggleLeft(label, value, options);
            if (EditorGUI.EndChangeCheck())
                _dirty = true;

            return nextValue;
        }

        private static void DrawStatus(string status, params GUILayoutOption[] options)
        {
            var previousColor = GUI.color;
            GUI.color = GetStatusColor(status);
            GUILayout.Label(string.IsNullOrWhiteSpace(status) ? ConfigPipelineStatus.NotRun : status, options);
            GUI.color = previousColor;
        }

        private static Color GetStatusColor(string status)
        {
            if (status == ConfigDownloadStatus.Success || status == ConfigPipelineStatus.Success)
                return new Color(0.45f, 0.9f, 0.45f);

            if (status == ConfigDownloadStatus.NotDownloaded ||
                status == ConfigPipelineStatus.NotRun ||
                string.IsNullOrWhiteSpace(status))
            {
                return Color.white;
            }

            if (status == ConfigPipelineStatus.Unsupported)
                return new Color(0.75f, 0.75f, 0.75f);

            return new Color(1f, 0.55f, 0.45f);
        }

        private void LoadSettings()
        {
            _settings = ConfigSourceSettingsStore.LoadOrCreate();
            _dirty = false;
        }

        private void SaveSettings()
        {
            ConfigSourceSettingsStore.Save(_settings);
            _dirty = false;
        }
    }
}
