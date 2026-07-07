using System;

namespace GuildIdle.Activities
{
    public interface IActivityRandom
    {
        int RangeInclusive(int min, int max);
        float Percent();
    }

    public sealed class SystemActivityRandom : IActivityRandom
    {
        private readonly Random _random;

        public SystemActivityRandom()
            : this(Environment.TickCount)
        {
        }

        public SystemActivityRandom(int seed)
        {
            _random = new Random(seed);
        }

        public int RangeInclusive(int min, int max)
        {
            if (max <= min)
                return min;

            return _random.Next(min, max + 1);
        }

        public float Percent()
        {
            return (float)(_random.NextDouble() * 100.0);
        }
    }
}
