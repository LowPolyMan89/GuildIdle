using System;

namespace GuildIdle
{
    public sealed class TimedStatEffect
    {
        public TimedStatEffect(
            string id,
            StatTarget target,
            StatModifierType type,
            float valuePerSecond,
            float duration)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Effect id cannot be empty.", nameof(id));

            if (duration < 0f)
                throw new ArgumentOutOfRangeException(nameof(duration), duration, "Effect duration cannot be negative.");

            Id = id;
            Target = target;
            Type = type;
            ValuePerSecond = valuePerSecond;
            Duration = duration;
        }

        public string Id { get; }
        public StatTarget Target { get; }
        public StatModifierType Type { get; }
        public float ValuePerSecond { get; }
        public float Duration { get; }
        public float ElapsedTime { get; private set; }
        public float RemainingTime => Clamp(Duration - ElapsedTime, 0f, Duration);
        public bool IsFinished => ElapsedTime >= Duration;

        internal float Advance(float deltaTime)
        {
            if (deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime), deltaTime, "Delta time cannot be negative.");

            if (IsFinished || deltaTime <= 0f)
                return 0f;

            var appliedTime = Math.Min(deltaTime, RemainingTime);
            ElapsedTime += appliedTime;
            return appliedTime;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }
    }
}
