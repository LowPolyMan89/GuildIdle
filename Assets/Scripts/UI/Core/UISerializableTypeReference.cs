using System;
using UnityEngine;

namespace GuildIdle.UI.Core
{
    [Serializable]
    public sealed class UISerializableTypeReference
    {
        [SerializeField] private string assemblyQualifiedName;

        public UISerializableTypeReference()
        {
        }

        public UISerializableTypeReference(Type type)
        {
            Set(type);
        }

        public string AssemblyQualifiedName => assemblyQualifiedName;

        public Type Resolve()
        {
            return string.IsNullOrWhiteSpace(assemblyQualifiedName)
                ? null
                : Type.GetType(assemblyQualifiedName, false);
        }

        public void Set(Type type)
        {
            assemblyQualifiedName = type?.AssemblyQualifiedName;
        }
    }
}
