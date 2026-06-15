using System;
using System.Collections.Generic;

namespace GuildIdle
{
    public sealed class Stat
    {
        private readonly List<StatModifier> _modifiers = new List<StatModifier>();
        private readonly List<TimedStatEffect> _effects = new List<TimedStatEffect>();
        private float _currentValue;
        private float _baseMaxValue;

        public Stat(string id, float currentValue, float maxValue, float regeneration = 0f)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Stat id cannot be empty.", nameof(id));

            if (maxValue < 0f)
                throw new ArgumentOutOfRangeException(nameof(maxValue), maxValue, "Max value cannot be negative.");

            Id = id;
            Regeneration = regeneration;
            _baseMaxValue = maxValue;
            _currentValue = Clamp(currentValue, 0f, MaxValue);
        }

        public string Id { get; }
        public float Regeneration { get; set; }

        public float CurrentValue
        {
            get => _currentValue;
            set => _currentValue = Clamp(value, 0f, MaxValue);
        }

        public float BaseMaxValue
        {
            get => _baseMaxValue;
            set
            {
                if (value < 0f)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Base max value cannot be negative.");

                _baseMaxValue = value;
                ClampCurrentValue();
            }
        }

        public float MaxValue => CalculateMaxValue();
        public IReadOnlyList<StatModifier> Modifiers => _modifiers;
        public IReadOnlyList<TimedStatEffect> Effects => _effects;

        public void AddModifier(StatModifier modifier)
        {
            if (modifier == null)
                throw new ArgumentNullException(nameof(modifier));

            RemoveModifier(modifier.Id);
            _modifiers.Add(modifier);
            ClampCurrentValue();
        }

        public bool RemoveModifier(string id)
        {
            var index = _modifiers.FindIndex(modifier => modifier.Id == id);
            if (index < 0)
                return false;

            _modifiers.RemoveAt(index);
            ClampCurrentValue();
            return true;
        }

        public void AddEffect(TimedStatEffect effect)
        {
            if (effect == null)
                throw new ArgumentNullException(nameof(effect));

            RemoveEffect(effect.Id);
            _effects.Add(effect);
        }

        public bool RemoveEffect(string id)
        {
            var index = _effects.FindIndex(effect => effect.Id == id);
            if (index < 0)
                return false;

            _effects.RemoveAt(index);
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime), deltaTime, "Delta time cannot be negative.");

            CurrentValue += Regeneration * deltaTime;

            for (var i = _effects.Count - 1; i >= 0; i--)
            {
                var effect = _effects[i];
                var appliedTime = effect.Advance(deltaTime);

                if (appliedTime > 0f)
                    ApplyEffect(effect, appliedTime);

                if (effect.IsFinished)
                    _effects.RemoveAt(i);
            }

            ClampCurrentValue();
        }

        public void Update(float deltaTime)
        {
            Tick(deltaTime);
        }

        private void ApplyEffect(TimedStatEffect effect, float appliedTime)
        {
            var delta = CalculateEffectDelta(effect, appliedTime);

            if (effect.Target == StatTarget.Current)
            {
                CurrentValue += delta;
                return;
            }

            BaseMaxValue = Math.Max(0f, BaseMaxValue + delta);
        }

        private float CalculateEffectDelta(TimedStatEffect effect, float appliedTime)
        {
            if (effect.Type == StatModifierType.Flat)
                return effect.ValuePerSecond * appliedTime;

            return MaxValue * (effect.ValuePerSecond / 100f) * appliedTime;
        }

        private float CalculateMaxValue()
        {
            var flatBonus = 0f;
            var percentBonus = 0f;

            for (var i = 0; i < _modifiers.Count; i++)
            {
                var modifier = _modifiers[i];
                if (modifier.Target != StatTarget.Max)
                    continue;

                if (modifier.Type == StatModifierType.Flat)
                    flatBonus += modifier.Value;
                else
                    percentBonus += modifier.Value;
            }

            var maxValue = (_baseMaxValue + flatBonus) * (1f + percentBonus / 100f);
            return Math.Max(0f, maxValue);
        }

        private void ClampCurrentValue()
        {
            _currentValue = Clamp(_currentValue, 0f, MaxValue);
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
