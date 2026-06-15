using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GuildIdle;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GuildIdle.Editor
{
    public sealed class ConfigManagerWindow : EditorWindow
    {
        private readonly List<Button> _typeButtons = new List<Button>();
        private readonly List<Button> _recordButtons = new List<Button>();

        private ConfigTypeDescriptor _selectedDescriptor;
        private ConfigAssetRecord _selectedRecord;
        private List<ConfigAssetRecord> _records = new List<ConfigAssetRecord>();
        private string _search = string.Empty;
        private bool _dirty;

        private VisualElement _typeList;
        private VisualElement _recordList;
        private VisualElement _inspectorPanel;
        private VisualElement _validationPanel;
        private Label _titleLabel;
        private Label _pathLabel;
        private TextField _searchField;
        private Button _saveButton;
        private Button _duplicateButton;
        private Button _deleteButton;

        [MenuItem("Tools/Configs/Config Manager")]
        public static void Open()
        {
            var window = GetWindow<ConfigManagerWindow>();
            window.titleContent = new GUIContent("Config Manager");
            window.minSize = new Vector2(960f, 560f);
            window.Show();
        }

        public void CreateGUI()
        {
            ConfigDatabase.Reload();
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Row;
            rootVisualElement.style.paddingLeft = 6f;
            rootVisualElement.style.paddingRight = 6f;
            rootVisualElement.style.paddingTop = 6f;
            rootVisualElement.style.paddingBottom = 6f;

            BuildTypeColumn();
            BuildRecordColumn();
            BuildInspectorColumn();

            SelectDescriptor(_selectedDescriptor ?? ConfigEditorRegistry.Descriptors[0]);
        }

        private void BuildTypeColumn()
        {
            var column = CreateColumn(190f);
            rootVisualElement.Add(column);

            column.Add(CreateHeader("Types"));

            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var reloadButton = new Button(ReloadAll) { text = "Reload" };
            var validateButton = new Button(ValidateConfigs) { text = "Validate" };
            toolbar.Add(reloadButton);
            toolbar.Add(validateButton);
            column.Add(toolbar);

            _typeList = new ScrollView();
            _typeList.style.flexGrow = 1f;
            column.Add(_typeList);
        }

        private void BuildRecordColumn()
        {
            var column = CreateColumn(260f);
            rootVisualElement.Add(column);

            column.Add(CreateHeader("Configs"));

            _searchField = new TextField();
            _searchField.tooltip = "Filter by Id or DisplayName";
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _search = evt.newValue ?? string.Empty;
                RefreshRecordList();
            });
            column.Add(_searchField);

            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            toolbar.Add(new Button(CreateConfig) { text = "Create" });
            _duplicateButton = new Button(DuplicateConfig) { text = "Duplicate" };
            _deleteButton = new Button(DeleteConfig) { text = "Delete" };
            toolbar.Add(_duplicateButton);
            toolbar.Add(_deleteButton);
            column.Add(toolbar);

            _recordList = new ScrollView();
            _recordList.style.flexGrow = 1f;
            column.Add(_recordList);
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
            _saveButton = new Button(SaveSelected) { text = "Save" };
            header.Add(_titleLabel);
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

        private void SelectDescriptor(ConfigTypeDescriptor descriptor)
        {
            if (descriptor == null)
                return;

            if (!ConfirmDiscardChanges())
                return;

            _selectedDescriptor = descriptor;
            _selectedRecord = null;
            _records = ConfigEditorAssetIo.LoadRecords(_selectedDescriptor);
            _dirty = false;
            RefreshTypeList();
            RefreshRecordList();
            RefreshInspector();
            RefreshValidationPanel(null);
        }

        private void SelectRecord(ConfigAssetRecord record)
        {
            if (!ConfirmDiscardChanges())
                return;

            _selectedRecord = record;
            _dirty = false;
            RefreshRecordList();
            RefreshInspector();
        }

        private void RefreshTypeList()
        {
            _typeList.Clear();
            _typeButtons.Clear();

            foreach (var descriptor in ConfigEditorRegistry.Descriptors)
            {
                var count = ConfigEditorAssetIo.LoadRecords(descriptor).Count;
                var button = new Button(() => SelectDescriptor(descriptor))
                {
                    text = $"{descriptor.DisplayName} ({count})"
                };

                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                if (descriptor == _selectedDescriptor)
                    button.AddToClassList("selected");

                _typeButtons.Add(button);
                _typeList.Add(button);
            }
        }

        private void RefreshRecordList()
        {
            _recordList.Clear();
            _recordButtons.Clear();

            _duplicateButton.SetEnabled(_selectedRecord != null);
            _deleteButton.SetEnabled(_selectedRecord != null);

            var filter = _search.Trim();
            foreach (var record in _records)
            {
                if (!MatchesSearch(record, filter))
                    continue;

                var name = string.IsNullOrWhiteSpace(record.DisplayName) ? record.Id : $"{record.Id}  -  {record.DisplayName}";
                var button = new Button(() => SelectRecord(record)) { text = name };
                button.style.unityTextAlign = TextAnchor.MiddleLeft;

                if (record == _selectedRecord)
                    button.style.backgroundColor = new Color(0.22f, 0.36f, 0.58f, 1f);

                _recordButtons.Add(button);
                _recordList.Add(button);
            }
        }

        private void RefreshInspector()
        {
            _inspectorPanel.Clear();

            if (_selectedRecord == null)
            {
                _titleLabel.text = _selectedDescriptor != null ? _selectedDescriptor.DisplayName : "Inspector";
                _pathLabel.text = "No config selected.";
                _saveButton.SetEnabled(false);
                _duplicateButton.SetEnabled(false);
                _deleteButton.SetEnabled(false);
                return;
            }

            _titleLabel.text = $"{_selectedDescriptor.DisplayName}: {_selectedRecord.Id}";
            _pathLabel.text = _selectedRecord.Path;
            _saveButton.SetEnabled(true);
            _duplicateButton.SetEnabled(true);
            _deleteButton.SetEnabled(true);

            BuildObjectFields(_inspectorPanel, _selectedRecord.Config, 0);
        }

        private void RefreshValidationPanel(ConfigValidationReport report)
        {
            _validationPanel.Clear();
            _validationPanel.Add(CreateHeader("Validation"));

            if (report == null)
            {
                _validationPanel.Add(new Label("Run Validate to see config issues."));
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

        private void BuildObjectFields(VisualElement parent, object target, int depth)
        {
            if (target == null)
            {
                parent.Add(new Label("null"));
                return;
            }

            var fields = target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public);
            foreach (var field in fields)
                BuildField(parent, target, field, depth);
        }

        private void BuildField(VisualElement parent, object target, FieldInfo field, int depth)
        {
            var fieldType = field.FieldType;
            var value = field.GetValue(target);

            if (fieldType == typeof(string))
            {
                var container = new VisualElement();
                var control = new TextField(field.Name) { value = value as string ?? string.Empty };
                control.RegisterValueChangedCallback(evt =>
                {
                    field.SetValue(target, evt.newValue);
                    MarkDirty();
                    RefreshReferenceStatus(container, field.Name, evt.newValue);
                });
                container.Add(control);
                AddIndented(parent, container, depth);
                AddReferenceStatus(container, field.Name, control.value);
                return;
            }

            if (fieldType == typeof(int))
            {
                var control = new IntegerField(field.Name) { value = value != null ? (int)value : 0 };
                control.RegisterValueChangedCallback(evt =>
                {
                    field.SetValue(target, evt.newValue);
                    MarkDirty();
                });
                AddIndented(parent, control, depth);
                return;
            }

            if (fieldType == typeof(float))
            {
                var control = new FloatField(field.Name) { value = value != null ? (float)value : 0f };
                control.RegisterValueChangedCallback(evt =>
                {
                    field.SetValue(target, evt.newValue);
                    MarkDirty();
                });
                AddIndented(parent, control, depth);
                return;
            }

            if (fieldType == typeof(bool))
            {
                var control = new Toggle(field.Name) { value = value != null && (bool)value };
                control.RegisterValueChangedCallback(evt =>
                {
                    field.SetValue(target, evt.newValue);
                    MarkDirty();
                });
                AddIndented(parent, control, depth);
                return;
            }

            if (fieldType.IsArray)
            {
                BuildArrayField(parent, target, field, depth);
                return;
            }

            if (fieldType.IsClass && fieldType.GetConstructor(Type.EmptyTypes) != null)
            {
                var foldout = new Foldout { text = field.Name, value = depth < 1 };
                AddIndented(parent, foldout, depth);

                if (value == null)
                {
                    var createButton = new Button(() =>
                    {
                        var instance = Activator.CreateInstance(fieldType);
                        field.SetValue(target, instance);
                        MarkDirty();
                        RefreshInspector();
                    })
                    {
                        text = "Create"
                    };
                    foldout.Add(createButton);
                }
                else
                {
                    BuildObjectFields(foldout, value, depth + 1);
                }

                return;
            }

            var unsupported = new Label($"{field.Name}: unsupported {fieldType.Name}");
            AddIndented(parent, unsupported, depth);
        }

        private void BuildArrayField(VisualElement parent, object target, FieldInfo field, int depth)
        {
            var elementType = field.FieldType.GetElementType();
            var array = field.GetValue(target) as Array ?? Array.CreateInstance(elementType, 0);

            var foldout = new Foldout { text = $"{field.Name} [{array.Length}]", value = depth < 1 };
            AddIndented(parent, foldout, depth);

            for (var i = 0; i < array.Length; i++)
            {
                var index = i;
                var element = array.GetValue(index);
                var row = new Foldout { text = $"Element {index}", value = false };
                row.style.marginLeft = 10f;
                foldout.Add(row);

                var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                row.Add(toolbar);

                toolbar.Add(new Button(() => MoveArrayElement(target, field, index, -1)) { text = "Up" });
                toolbar.Add(new Button(() => MoveArrayElement(target, field, index, 1)) { text = "Down" });
                toolbar.Add(new Button(() => RemoveArrayElement(target, field, index)) { text = "Delete" });

                BuildArrayElement(row, target, field, elementType, element, index, depth + 1);
            }

            foldout.Add(new Button(() => AddArrayElement(target, field, elementType)) { text = $"Add {elementType.Name}" });
        }

        private void BuildArrayElement(
            VisualElement parent,
            object arrayOwner,
            FieldInfo arrayField,
            Type elementType,
            object element,
            int index,
            int depth)
        {
            if (elementType == typeof(string))
            {
                var control = new TextField("Value") { value = element as string ?? string.Empty };
                control.RegisterValueChangedCallback(evt =>
                {
                    SetArrayElement(arrayOwner, arrayField, index, evt.newValue);
                    MarkDirty();
                });
                AddIndented(parent, control, depth);
                return;
            }

            if (elementType == typeof(int))
            {
                var control = new IntegerField("Value") { value = element != null ? (int)element : 0 };
                control.RegisterValueChangedCallback(evt =>
                {
                    SetArrayElement(arrayOwner, arrayField, index, evt.newValue);
                    MarkDirty();
                });
                AddIndented(parent, control, depth);
                return;
            }

            if (elementType == typeof(float))
            {
                var control = new FloatField("Value") { value = element != null ? (float)element : 0f };
                control.RegisterValueChangedCallback(evt =>
                {
                    SetArrayElement(arrayOwner, arrayField, index, evt.newValue);
                    MarkDirty();
                });
                AddIndented(parent, control, depth);
                return;
            }

            if (elementType == typeof(bool))
            {
                var control = new Toggle("Value") { value = element != null && (bool)element };
                control.RegisterValueChangedCallback(evt =>
                {
                    SetArrayElement(arrayOwner, arrayField, index, evt.newValue);
                    MarkDirty();
                });
                AddIndented(parent, control, depth);
                return;
            }

            if (element == null)
            {
                element = Activator.CreateInstance(elementType);
                SetArrayElement(arrayOwner, arrayField, index, element);
            }

            BuildObjectFields(parent, element, depth);
        }

        private void AddArrayElement(object target, FieldInfo field, Type elementType)
        {
            var array = field.GetValue(target) as Array ?? Array.CreateInstance(elementType, 0);
            var next = Array.CreateInstance(elementType, array.Length + 1);
            Array.Copy(array, next, array.Length);
            next.SetValue(CreateDefaultValue(elementType), array.Length);
            field.SetValue(target, next);
            MarkDirty();
            RefreshInspector();
        }

        private void RemoveArrayElement(object target, FieldInfo field, int index)
        {
            var array = field.GetValue(target) as Array;
            if (array == null || index < 0 || index >= array.Length)
                return;

            var elementType = field.FieldType.GetElementType();
            var next = Array.CreateInstance(elementType, array.Length - 1);
            var write = 0;
            for (var read = 0; read < array.Length; read++)
            {
                if (read == index)
                    continue;

                next.SetValue(array.GetValue(read), write);
                write++;
            }

            field.SetValue(target, next);
            MarkDirty();
            RefreshInspector();
        }

        private void MoveArrayElement(object target, FieldInfo field, int index, int direction)
        {
            var array = field.GetValue(target) as Array;
            if (array == null)
                return;

            var nextIndex = index + direction;
            if (nextIndex < 0 || nextIndex >= array.Length)
                return;

            var current = array.GetValue(index);
            var other = array.GetValue(nextIndex);
            array.SetValue(other, index);
            array.SetValue(current, nextIndex);
            field.SetValue(target, array);
            MarkDirty();
            RefreshInspector();
        }

        private void SetArrayElement(object target, FieldInfo field, int index, object value)
        {
            var array = field.GetValue(target) as Array;
            if (array == null || index < 0 || index >= array.Length)
                return;

            array.SetValue(value, index);
            field.SetValue(target, array);
        }

        private static object CreateDefaultValue(Type type)
        {
            if (type == typeof(string))
                return string.Empty;

            if (type == typeof(int))
                return 0;

            if (type == typeof(float))
                return 0f;

            if (type == typeof(bool))
                return false;

            return Activator.CreateInstance(type);
        }

        private void CreateConfig()
        {
            if (_selectedDescriptor == null || !ConfirmDiscardChanges())
                return;

            var id = ConfigEditorAssetIo.CreateUniqueId(_selectedDescriptor);
            var config = _selectedDescriptor.CreateDefault(id);
            var path = ConfigEditorAssetIo.SaveConfig(_selectedDescriptor, config);
            ReloadSelectedDescriptor(path);
        }

        private void DuplicateConfig()
        {
            if (_selectedRecord == null || !ConfirmDiscardChanges())
                return;

            var json = JsonUtility.ToJson(_selectedRecord.Config, false);
            var copy = JsonUtility.FromJson(json, _selectedDescriptor.ConfigType);
            var id = ConfigEditorAssetIo.CreateUniqueId(_selectedDescriptor);
            _selectedDescriptor.SetId(copy, id);
            var path = ConfigEditorAssetIo.SaveConfig(_selectedDescriptor, copy);
            ReloadSelectedDescriptor(path);
        }

        private void DeleteConfig()
        {
            if (_selectedRecord == null)
                return;

            if (!EditorUtility.DisplayDialog(
                    "Delete config",
                    $"Delete '{_selectedRecord.Id}'?\n\n{_selectedRecord.Path}",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            ConfigEditorAssetIo.DeleteConfig(_selectedRecord.Path);
            _selectedRecord = null;
            _dirty = false;
            ReloadSelectedDescriptor(null);
        }

        private void SaveSelected()
        {
            if (_selectedRecord == null)
                return;

            try
            {
                var path = ConfigEditorAssetIo.SaveConfig(_selectedDescriptor, _selectedRecord.Config, _selectedRecord.Path);
                _selectedRecord.Path = path;
                _dirty = false;
                ReloadSelectedDescriptor(path);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Save failed", exception.Message, "OK");
            }
        }

        private void ReloadAll()
        {
            if (!ConfirmDiscardChanges())
                return;

            ConfigDatabase.Reload();
            SelectDescriptor(_selectedDescriptor ?? ConfigEditorRegistry.Descriptors[0]);
        }

        private void ValidateConfigs()
        {
            ConfigDatabase.Reload();
            var report = ConfigDatabase.Validate();
            RefreshValidationPanel(report);
        }

        private void ReloadSelectedDescriptor(string selectPath)
        {
            ConfigDatabase.Reload();
            _records = ConfigEditorAssetIo.LoadRecords(_selectedDescriptor);
            _selectedRecord = null;

            if (!string.IsNullOrWhiteSpace(selectPath))
            {
                foreach (var record in _records)
                {
                    if (record.Path == selectPath)
                    {
                        _selectedRecord = record;
                        break;
                    }
                }
            }

            RefreshTypeList();
            RefreshRecordList();
            RefreshInspector();
        }

        private void MarkDirty()
        {
            _dirty = true;
            if (_selectedRecord != null)
                _titleLabel.text = $"{_selectedDescriptor.DisplayName}: {_selectedRecord.Id} *";
        }

        private bool ConfirmDiscardChanges()
        {
            if (!_dirty)
                return true;

            return EditorUtility.DisplayDialog(
                "Discard unsaved changes?",
                "The selected config has unsaved changes.",
                "Discard",
                "Cancel");
        }

        private void AddReferenceStatus(VisualElement parent, string fieldName, string value)
        {
            var status = CreateReferenceStatusLabel(fieldName, value);
            if (status != null)
                parent.Add(status);
        }

        private void RefreshReferenceStatus(VisualElement parent, string fieldName, string value)
        {
            if (parent == null)
                return;

            var oldStatus = parent.Q<Label>("reference-status");
            if (oldStatus != null)
                parent.Remove(oldStatus);

            AddReferenceStatus(parent, fieldName, value);
        }

        private Label CreateReferenceStatusLabel(string fieldName, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || fieldName == "Id")
                return null;

            string targetType = null;
            bool exists = true;

            if (fieldName == "GrowthTableId")
            {
                targetType = "HeroGrowth";
                exists = ConfigDatabase.HasHeroGrowth(value);
            }
            else if (fieldName == "SkillId" || fieldName == "TargetSkillId" || fieldName == "RequiredSkillId")
            {
                targetType = "Skill";
                exists = ConfigDatabase.HasSkill(value);
            }
            else if (fieldName == "ItemId" || fieldName == "ResultItemId")
            {
                targetType = "Item";
                exists = ConfigDatabase.HasItem(value);
            }
            else if (fieldName == "BossEnemyId")
            {
                targetType = "Enemy";
                exists = ConfigDatabase.HasEnemy(value);
            }
            else if (fieldName == "StatId")
            {
                targetType = "Stat";
                exists = ConfigDatabase.HasStat(value);
            }
            else if (fieldName == "LocalisationNameId" || fieldName == "LocalisationDescriptionId")
            {
                targetType = "Localization";
                exists = LocalisationModel.TryGetText(value, out _);
            }

            if (targetType == null)
                return null;

            var label = new Label(exists ? $"{targetType}: OK" : $"{targetType}: missing '{value}'")
            {
                name = "reference-status"
            };
            label.style.marginLeft = 150f;
            label.style.fontSize = 10f;
            label.style.color = exists ? new Color(0.42f, 0.8f, 0.44f) : new Color(1f, 0.38f, 0.32f);
            return label;
        }

        private static bool MatchesSearch(ConfigAssetRecord record, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            return Contains(record.Id, filter) || Contains(record.DisplayName, filter);
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

        private static void AddIndented(VisualElement parent, VisualElement element, int depth)
        {
            element.style.marginLeft = depth * 12f;
            element.style.marginBottom = 2f;
            parent.Add(element);
        }
    }
}
