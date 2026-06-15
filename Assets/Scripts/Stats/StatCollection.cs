using System;
using System.Collections.Generic;

namespace GuildIdle
{
    public sealed class StatCollection
    {
        private readonly Dictionary<string, Stat> _stats = new Dictionary<string, Stat>();

        public int Count => _stats.Count;
        public IEnumerable<Stat> Values => _stats.Values;

        public Stat Add(string id, float currentValue, float maxValue, float regeneration = 0f)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Stat id cannot be empty.", nameof(id));

            if (_stats.ContainsKey(id))
                throw new InvalidOperationException($"Stat with id '{id}' already exists.");

            var stat = new Stat(id, currentValue, maxValue, regeneration);
            _stats.Add(id, stat);
            return stat;
        }

        public bool Remove(string id)
        {
            return _stats.Remove(id);
        }

        public bool TryGet(string id, out Stat stat)
        {
            return _stats.TryGetValue(id, out stat);
        }

        public Stat Get(string id)
        {
            if (!_stats.TryGetValue(id, out var stat))
                throw new KeyNotFoundException($"Stat with id '{id}' was not found.");

            return stat;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime), deltaTime, "Delta time cannot be negative.");

            foreach (var stat in _stats.Values)
                stat.Tick(deltaTime);
        }

        public void Update(float deltaTime)
        {
            Tick(deltaTime);
        }
    }
}
