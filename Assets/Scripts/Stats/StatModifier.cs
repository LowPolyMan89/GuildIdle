using System;

namespace GuildIdle
{
    public sealed class StatModifier
    {
        public StatModifier(string id, StatTarget target, StatModifierType type, float value)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Modifier id cannot be empty.", nameof(id));

            Id = id;
            Target = target;
            Type = type;
            Value = value;
        }

        public string Id { get; }
        public StatTarget Target { get; }
        public StatModifierType Type { get; }
        public float Value { get; }
    }
}
