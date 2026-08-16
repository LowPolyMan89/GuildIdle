using System;
using System.Collections.Generic;
using System.Linq;
using GuildIdle.UI.Core;
using UnityEditor;
using UnityEngine;

namespace GuildIdle.UI.Editor
{
    [CustomPropertyDrawer(typeof(UISerializableTypeReference))]
    public sealed class UISerializableTypeReferenceDrawer : PropertyDrawer
    {
        private static Type[] _viewTypes;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var typeNameProperty = property.FindPropertyRelative("assemblyQualifiedName");
            var types = GetViewTypes();
            var names = new List<string> { "<None>" };
            names.AddRange(types.Select(type => type.FullName));

            var selectedIndex = 0;
            var currentType = string.IsNullOrWhiteSpace(typeNameProperty.stringValue)
                ? null
                : Type.GetType(typeNameProperty.stringValue, false);
            if (currentType != null)
            {
                var index = Array.IndexOf(types, currentType);
                if (index >= 0)
                    selectedIndex = index + 1;
            }

            EditorGUI.BeginProperty(position, label, property);
            var nextIndex = EditorGUI.Popup(position, label.text, selectedIndex, names.ToArray());
            if (nextIndex != selectedIndex)
            {
                typeNameProperty.stringValue = nextIndex == 0
                    ? string.Empty
                    : types[nextIndex - 1].AssemblyQualifiedName;
            }

            EditorGUI.EndProperty();
        }

        private static Type[] GetViewTypes()
        {
            if (_viewTypes != null)
                return _viewTypes;

            _viewTypes = TypeCache.GetTypesDerivedFrom<UIView>()
                .Where(type => !type.IsAbstract && !type.ContainsGenericParameters)
                .Where(type => typeof(UIScreen).IsAssignableFrom(type) || typeof(UIWindow).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();
            return _viewTypes;
        }
    }
}
