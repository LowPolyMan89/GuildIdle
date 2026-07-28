using System;

namespace GuildIdle.Activities
{
    public readonly struct ActivityRandomState
    {
        public ActivityRandomState(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }
    }

    public interface IActivityRandom
    {
        int RangeInclusive(int min, int max);
        float Percent();
    }

    public interface ITransactionalActivityRandom : IActivityRandom
    {
        ActivityRandomState CaptureState();
        void RestoreState(ActivityRandomState state);
    }

    public sealed class SystemActivityRandom : ITransactionalActivityRandom
    {
        private const ulong SeedOffset = 0x9E3779B97F4A7C15UL;
        private const ulong Multiplier = 2685821657736338717UL;
        private ulong _state;

        public SystemActivityRandom()
            : this(Environment.TickCount)
        {
        }

        public SystemActivityRandom(int seed)
        {
            _state = unchecked((ulong)(uint)seed) + SeedOffset;
            if (_state == 0UL)
                _state = SeedOffset;
        }

        public int RangeInclusive(int min, int max)
        {
            if (max <= min)
                return min;

            var span = (ulong)((long)max - min + 1L);
            var rejectionThreshold = unchecked(0UL - span) % span;
            ulong value;
            do
            {
                value = NextUInt64();
            } while (value < rejectionThreshold);

            return (int)(min + (long)(value % span));
        }

        public float Percent()
        {
            return (float)((NextUInt64() >> 40) * (100.0 / (1UL << 24)));
        }

        public ActivityRandomState CaptureState() => new ActivityRandomState(_state);

        public void RestoreState(ActivityRandomState state)
        {
            _state = state.Value == 0UL ? SeedOffset : state.Value;
        }

        private ulong NextUInt64()
        {
            var value = _state;
            value ^= value >> 12;
            value ^= value << 25;
            value ^= value >> 27;
            _state = value;
            return value * Multiplier;
        }
    }
}
