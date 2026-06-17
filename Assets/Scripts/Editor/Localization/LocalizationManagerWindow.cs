using System;
using System.Collections.Generic;
using GuildIdle;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuildIdle.Editor
{
    public sealed class LocalizationManagerWindow : EditorWindow
    {
        private readonly List<Button> _tableButtons = new List<Button>();
        private readonly List<Button> _textButtons = new List<Button>();

        private List<LocalisationTableRecord> _tables = new List<LocalisationTableRecord>();
        private LocalisationTableRecord _selectedTable;
        private LocalisationTextRecord _selectedText;
        private string _search = string.Empty;
        private bool _dirty;

        private VisualElement _tableList;
        private VisualElement _textList;
        private VisualElement _inspectorPanel;
        private VisualElement _validationPanel;
        private Label _titleLabel;
        private Label _pathLabel;
        private TextField _searchField;
        private Button _saveButton;
        private Button _autoTranslateButton;
        private Button _duplicateButton;
        private Button _deleteButton;

        [MenuItem("Tools/Configs/Localization Manager")]
        public static void Open()
        {
            OpenWindow();
        }

        public static void OpenForKey(string key)
        {
            var window = OpenWindow();
            window.FocusKey(key);
        }

        public static void OpenForCreatedKey(string tableId, string key)
        {
            var window = OpenWindow();
            window.FocusKey(key, tableId);
        }

        private static LocalizationManagerWindow OpenWindow()
        {
            var window = GetWindow<LocalizationManagerWindow>();
            window.titleContent = new GUIContent("Localization Manager");
            window.minSize = new Vector2(960f, 560f);
            window.Show();
            return window;
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Row;
            rootVisualElement.style.paddingLeft = 6f;
            rootVisualElement.style.paddingRight = 6f;
            rootVisualElement.style.paddingTop = 6f;
            rootVisualElement.style.paddingBottom = 6f;

            BuildTableColumn();
            BuildTextColumn();
            BuildInspectorColumn();

            ReloadAll(false);
        }

        private void BuildTableColumn()
        {
            var column = CreateColumn(190f);
            rootVisualElement.Add(column);

            column.Add(CreateHeader("Tables"));

            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            toolbar.Add(new Button(() => ReloadAll(true)) { text = "Reload" });
            toolbar.Add(new Button(ValidateLocalisation) { text = "Validate" });
            toolbar.Add(new Button(CreateTable) { text = "Create" });
            column.Add(toolbar);

            _tableList = new ScrollView();
            _tableList.style.flexGrow = 1f;
            column.Add(_tableList);
        }

        private void BuildTextColumn()
        {
            var column = CreateColumn(260f);
            rootVisualElement.Add(column);

            column.Add(CreateHeader("Keys"));

            _searchField = new TextField();
            _searchField.tooltip = "Filter by Id or ru/en/tr text";
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _search = evt.newValue ?? string.Empty;
                RefreshTextList();
            });
            column.Add(_searchField);

            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            toolbar.Add(new Button(CreateText) { text = "Create" });
            _duplicateButton = new Button(DuplicateText) { text = "Duplicate" };
            _deleteButton = new Button(DeleteText) { text = "Delete" };
            toolbar.Add(_duplicateButton);
            toolbar.Add(_deleteButton);
            column.Add(toolbar);

            _textList = new ScrollView();
            _textList.style.flexGrow = 1f;
            column.Add(_textList);
        }

        private void BuildInspectorColumn()
        {
            var column = new VisualElement();
            column.style.flexGrow = 1f;
            column.style.marginLeft = 6f;
            rootVisualElement.Add(column);

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _titleLabel = CreateHeader("Inspector");
            _titleLabel.style.flexGrow = 1f;
            _autoTranslateButton = new Button(AutoTranslateSelected) { text = "Auto Translate" };
            _saveButton = new Button(SaveSelected) { text = "Save" };
            header.Add(_titleLabel);
            header.Add(_autoTranslateButton);
            header.Add(_saveButton);
            column.Add(header);

            _pathLabel = new Label();
            _pathLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            _pathLabel.style.marginBottom = 4f;
            column.Add(_pathLabel);

            var split = new TwoPaneSplitView(0, 420, TwoPaneSplitViewOrientation.Vertical);
            split.style.flexGrow = 1f;
            column.Add(split);

            _inspectorPanel = new ScrollView();
            _validationPanel = new ScrollView();
            split.Add(_inspectorPanel);
            split.Add(_validationPanel);
        }

        private void ReloadAll(bool confirmDirty)
        {
            if (confirmDirty && !ConfirmDiscardChanges())
                return;

            var selectedTableId = _selectedTable != null ? _selectedTable.Id : null;
            var selectedTextId = _selectedText != null ? _selectedText.Id : null;

            _tables = LocalisationEditorAssetIo.LoadTables();
            _selectedTable = FindTable(selectedTableId) ?? (_tables.Count > 0 ? _tables[0] : null);
            _selectedText = FindText(_selectedTable, selectedTextId);
            _dirty = false;

            RefreshTableList();
            RefreshTextList();
            RefreshInspector();
            RefreshValidationPanel(null);
        }

        private void FocusKey(string key, string preferredTableId = null)
        {
            _tables = LocalisationEditorAssetIo.LoadTables();
            _search = key ?? string.Empty;
            if (_searchField != null)
                _searchField.SetValueWithoutNotify(_search);

            _selectedTable = null;
            _selectedText = null;

            if (!string.IsNullOrWhiteSpace(key) &&
                LocalisationEditorAssetIo.TryFindText(_tables, key, out var table, out var record))
            {
                _selectedTable = table;
                _selectedText = record;
            }
            else if (!string.IsNullOrWhiteSpace(preferredTableId))
            {
                _selectedTable = FindTable(preferredTableId);
            }

            _selectedTable = _selectedTable ?? (_tables.Count > 0 ? _tables[0] : null);
            _dirty = false;
            RefreshTableList();
            RefreshTextList();
            RefreshInspector();
            RefreshValidationPanel(_selectedText != null || string.IsNullOrWhiteSpace(key)
                ? null
                : CreateSingleErrorReport($"Localisation key '{key}' was not found."));
        }

        private void SelectTable(LocalisationTableRecord table)
        {
            if (!ConfirmDiscardChanges())
                return;

            _selectedTable = table;
            _selectedText = null;
            _dirty = false;
            RefreshTableList();
            RefreshTextList();
            RefreshInspector();
            RefreshValidationPanel(null);
        }

        private void SelectText(LocalisationTextRecord text)
        {
            if (!ConfirmDiscardChanges())
                return;

            _selectedText = text;
            _dirty = false;
            RefreshTextList();
            RefreshInspector();
            RefreshValidationPanel(null);
        }

        private void RefreshTableList()
        {
            _tableList.Clear();
            _tableButtons.Clear();

            foreach (var table in _tables)
            {
                var button = new Button(() => SelectTable(table))
                {
                    text = $"{table.Id} ({table.Count})"
                };
                button.style.unityTextAlign = TextAnchor.MiddleLeft;

                if (table == _selectedTable)
                    button.style.backgroundColor = new Color(0.22f, 0.36f, 0.58f, 1f);

                _tableButtons.Add(button);
                _tableList.Add(button);
            }
        }

        private void RefreshTextList()
        {
            _textList.Clear();
            _textButtons.Clear();

            _duplicateButton.SetEnabled(_selectedText != null);
            _deleteButton.SetEnabled(_selectedText != null);

            if (_selectedTable == null)
                return;

            var filter = _search.Trim();
            foreach (var record in LocalisationEditorAssetIo.GetTextRecords(_selectedTable))
            {
                if (!MatchesSearch(record, filter))
                    continue;

                var button = new Button(() => SelectText(record))
                {
                    text = CreateTextListLabel(record)
                };
                button.style.unityTextAlign = TextAnchor.MiddleLeft;

                if (_selectedText != null && record.Text == _selectedText.Text)
                    button.style.backgroundColor = new Color(0.22f, 0.36f, 0.58f, 1f);

                _textButtons.Add(button);
                _textList.Add(button);
            }
        }

        private void RefreshInspector()
        {
            _inspectorPanel.Clear();

            if (_selectedTable == null)
            {
                _titleLabel.text = "Inspector";
                _pathLabel.text = "No localisation table found.";
                SetSelectedButtonsEnabled(false);
                return;
            }

            if (_selectedText == null)
            {
                _titleLabel.text = _selectedTable.Id;
                _pathLabel.text = _selectedTable.Path;
                SetSelectedButtonsEnabled(false);
                _inspectorPanel.Add(new Label("No key selected."));
                return;
            }

            _titleLabel.text = $"{_selectedTable.Id}: {_selectedText.Id}";
            _pathLabel.text = _selectedTable.Path;
            SetSelectedButtonsEnabled(true);

            BuildTextFields();
            RefreshAutoTranslateButton();
        }

        private void BuildTextFields()
        {
            var idField = new TextField("Id") { value = _selectedText.Text.Id ?? string.Empty };
            idField.RegisterValueChangedCallback(evt =>
            {
                _selectedText.Text.Id = evt.newValue;
                MarkDirty();
                RefreshValidationPanel(LocalisationEditorAssetIo.ValidateEntry(_tables, _selectedText.Text));
                RefreshTextList();
            });
            _inspectorPanel.Add(idField);

            var languages = LocalisationModel.Languages;
            for (var i = 0; i < languages.Length; i++)
            {
                var index = i;
                var field = new TextField(languages[index])
                {
                    multiline = true,
                    value = LocalisationEditorAssetIo.GetValue(_selectedText.Text, index)
                };
                field.style.minHeight = 72f;
                field.RegisterValueChangedCallback(evt =>
                {
                    LocalisationEditorAssetIo.SetValue(_selectedText.Text, index, evt.newValue);
                    MarkDirty();
                    RefreshAutoTranslateButton();
                    RefreshTextList();
                });
                _inspectorPanel.Add(field);
            }
        }

        private void RefreshValidationPanel(ConfigValidationReport report)
        {
            _validationPanel.Clear();
            _validationPanel.Add(CreateHeader("Validation"));

            if (report == null)
            {
                _validationPanel.Add(new Label("Run Validate to see localisation issues."));
                return;
            }

            if (report.IsValid && report.Warnings.Count == 0)
            {
                _validationPanel.Add(new Label("No validation issues."));
                return;
            }

            foreach (var warning in report.Warnings)
            {
                var label = new Label("Warning: " + warning);
                label.style.color = new Color(0.95f, 0.72f, 0.25f);
                _validationPanel.Add(label);
            }

            foreach (var error in report.Errors)
            {
                var label = new Label("Error: " + error);
                label.style.color = new Color(1f, 0.38f, 0.32f);
                _validationPanel.Add(label);
            }
        }

        private void CreateText()
        {
            if (_selectedTable == null || !ConfirmDiscardChanges())
                return;

            var id = LocalisationEditorAssetIo.CreateUniqueId(_tables);
            var entry = LocalisationEditorAssetIo.CreateDefaultEntry(id);
            LocalisationEditorAssetIo.AddEntry(_selectedTable, entry);
            _selectedText = FindText(_selectedTable, id);
            SaveCurrentTableAndReload(id);
        }

        private void CreateTable()
        {
            if (!ConfirmDiscardChanges())
                return;

            var id = LocalisationEditorAssetIo.CreateUniqueTableId(_tables);
            var table = LocalisationEditorAssetIo.CreateDefaultTable(id);
            var report = LocalisationEditorAssetIo.ValidateTables(AppendTable(_tables, table));
            RefreshValidationPanel(report);

            if (!report.IsValid)
            {
                EditorUtility.DisplayDialog("Create table failed", "Generated localisation table id is invalid.", "OK");
                return;
            }

            try
            {
                LocalisationEditorAssetIo.SaveTable(table);
                _dirty = false;
                _tables = LocalisationEditorAssetIo.LoadTables();
                _selectedTable = FindTable(id);
                _selectedText = null;
                RefreshTableList();
                RefreshTextList();
                RefreshInspector();
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Create table failed", exception.Message, "OK");
            }
        }

        private void DuplicateText()
        {
            if (_selectedTable == null || _selectedText == null || !ConfirmDiscardChanges())
                return;

            var id = LocalisationEditorAssetIo.CreateUniqueId(_tables);
            var copy = LocalisationEditorAssetIo.DuplicateEntry(_selectedText.Text, id);
            LocalisationEditorAssetIo.AddEntry(_selectedTable, copy);
            _selectedText = FindText(_selectedTable, id);
            SaveCurrentTableAndReload(id);
        }

        private void DeleteText()
        {
            if (_selectedTable == null || _selectedText == null)
                return;

            if (!EditorUtility.DisplayDialog(
                    "Delete localisation key",
                    $"Delete '{_selectedText.Id}'?\n\n{_selectedTable.Path}",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            LocalisationEditorAssetIo.RemoveEntry(_selectedTable, _selectedText.Text);
            _selectedText = null;
            SaveCurrentTableAndReload(null);
        }

        private void SaveSelected()
        {
            if (_selectedTable == null || _selectedText == null)
                return;

            var report = LocalisationEditorAssetIo.ValidateEntry(_tables, _selectedText.Text);
            RefreshValidationPanel(report);

            if (!report.IsValid)
            {
                EditorUtility.DisplayDialog("Save failed", "Fix localisation validation errors before saving.", "OK");
                return;
            }

            SaveCurrentTableAndReload(_selectedText.Id);
        }

        private void AutoTranslateSelected()
        {
            if (_selectedText == null)
                return;

            var ru = LocalisationEditorAssetIo.GetValue(_selectedText.Text, 0);
            if (string.IsNullOrWhiteSpace(ru))
            {
                RefreshValidationPanel(CreateSingleErrorReport("Russian source text is empty."));
                EditorUtility.DisplayDialog("Auto Translate failed", "Russian source text is empty.", "OK");
                return;
            }

            var en = LocalisationEditorAssetIo.GetValue(_selectedText.Text, 1);
            var tr = LocalisationEditorAssetIo.GetValue(_selectedText.Text, 2);
            if ((!string.IsNullOrWhiteSpace(en) || !string.IsNullOrWhiteSpace(tr)) &&
                !EditorUtility.DisplayDialog(
                    "Overwrite translations?",
                    "Auto Translate will replace existing en/tr values for this key.",
                    "Overwrite",
                    "Cancel"))
            {
                return;
            }

            if (!LocalisationAutoTranslator.TryTranslate(ru, "en", out var translatedEn, out var enError))
            {
                RefreshValidationPanel(CreateSingleErrorReport(enError));
                return;
            }

            if (!LocalisationAutoTranslator.TryTranslate(ru, "tr", out var translatedTr, out var trError))
            {
                RefreshValidationPanel(CreateSingleErrorReport(trError));
                return;
            }

            LocalisationEditorAssetIo.SetValue(_selectedText.Text, 1, translatedEn);
            LocalisationEditorAssetIo.SetValue(_selectedText.Text, 2, translatedTr);
            MarkDirty();
            RefreshInspector();
            RefreshTextList();
            RefreshValidationPanel(null);
        }

        private void ValidateLocalisation()
        {
            var report = LocalisationEditorAssetIo.ValidateTables(_tables);
            RefreshValidationPanel(report);
        }

        private void SaveCurrentTableAndReload(string selectedTextId)
        {
            try
            {
                var report = LocalisationEditorAssetIo.ValidateTables(_tables);
                RefreshValidationPanel(report);

                if (!report.IsValid)
                {
                    EditorUtility.DisplayDialog("Save failed", "Fix localisation validation errors before saving.", "OK");
                    RefreshTableList();
                    RefreshTextList();
                    RefreshInspector();
                    return;
                }

                var tableId = _selectedTable.Id;
                LocalisationEditorAssetIo.SaveTable(_selectedTable);
                _dirty = false;
                _tables = LocalisationEditorAssetIo.LoadTables();
                _selectedTable = FindTable(tableId);
                _selectedText = FindText(_selectedTable, selectedTextId);
                RefreshTableList();
                RefreshTextList();
                RefreshInspector();
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Save failed", exception.Message, "OK");
            }
        }

        private void MarkDirty()
        {
            _dirty = true;
            if (_selectedText != null)
                _titleLabel.text = $"{_selectedTable.Id}: {_selectedText.Id} *";
        }

        private bool ConfirmDiscardChanges()
        {
            if (!_dirty)
                return true;

            return EditorUtility.DisplayDialog(
                "Discard unsaved changes?",
                "The selected localisation key has unsaved changes.",
                "Discard",
                "Cancel");
        }

        private void SetSelectedButtonsEnabled(bool enabled)
        {
            _saveButton.SetEnabled(enabled);
            _autoTranslateButton.SetEnabled(enabled);
            _duplicateButton.SetEnabled(enabled);
            _deleteButton.SetEnabled(enabled);
        }

        private void RefreshAutoTranslateButton()
        {
            _autoTranslateButton.SetEnabled(_selectedText != null && !string.IsNullOrWhiteSpace(LocalisationEditorAssetIo.GetValue(_selectedText.Text, 0)));
        }

        private LocalisationTableRecord FindTable(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            foreach (var table in _tables)
            {
                if (table.Id == id)
                    return table;
            }

            return null;
        }

        private static LocalisationTextRecord FindText(LocalisationTableRecord table, string id)
        {
            if (table == null || string.IsNullOrWhiteSpace(id))
                return null;

            foreach (var record in LocalisationEditorAssetIo.GetTextRecords(table))
            {
                if (record.Id == id)
                    return record;
            }

            return null;
        }

        private static ConfigValidationReport CreateSingleErrorReport(string message)
        {
            var report = new ConfigValidationReport();
            report.AddError(message);
            return report;
        }

        private static List<LocalisationTableRecord> AppendTable(List<LocalisationTableRecord> tables, LocalisationTableRecord table)
        {
            var next = new List<LocalisationTableRecord>(tables ?? new List<LocalisationTableRecord>());
            next.Add(table);
            return next;
        }

        private static bool MatchesSearch(LocalisationTextRecord record, string filter)
        {
            if (record?.Text == null)
                return false;

            if (string.IsNullOrWhiteSpace(filter))
                return true;

            if (Contains(record.Text.Id, filter))
                return true;

            for (var i = 0; i < LocalisationModel.Languages.Length; i++)
            {
                if (Contains(LocalisationEditorAssetIo.GetValue(record.Text, i), filter))
                    return true;
            }

            return false;
        }

        private static string CreateTextListLabel(LocalisationTextRecord record)
        {
            var ru = LocalisationEditorAssetIo.GetValue(record.Text, 0);
            return string.IsNullOrWhiteSpace(ru) ? record.Id : $"{record.Id}  -  {ru}";
        }

        private static bool Contains(string value, string filter)
        {
            return value != null && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static VisualElement CreateColumn(float width)
        {
            var column = new VisualElement();
            column.style.width = width;
            column.style.marginRight = 6f;
            return column;
        }

        private static Label CreateHeader(string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 14f;
            label.style.marginBottom = 4f;
            return label;
        }
    }
}
