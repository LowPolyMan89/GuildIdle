using System;
using System.Collections.Generic;
using System.Globalization;
using GuildIdle.Configs;

namespace GuildIdle.Combat
{
    public enum CombatConsumableUsePlace
    {
        Combat = 0
    }

    public enum CombatConsumableConditionKind
    {
        HpPercent = 0
    }

    public enum CombatConsumableComparisonOperator
    {
        LessOrEqual = 0
    }

    public sealed class CombatConsumableConditionDescriptor
    {
        public CombatConsumableConditionDescriptor(
            CombatConsumableConditionKind kind,
            CombatConsumableComparisonOperator comparisonOperator,
            double value)
        {
            Kind = kind;
            Operator = comparisonOperator;
            Value = value;
        }

        public CombatConsumableConditionKind Kind { get; }
        public CombatConsumableComparisonOperator Operator { get; }
        public double Value { get; }
    }

    public sealed class CombatConsumableDescriptor
    {
        public CombatConsumableDescriptor(
            string itemId,
            CombatConsumableUsePlace usePlace,
            CombatConsumableConditionDescriptor condition,
            CombatEffectDescriptor effect,
            double cooldownSeconds,
            double checkIntervalSeconds,
            int maxStack)
        {
            ItemId = itemId;
            UsePlace = usePlace;
            Condition = condition;
            Effect = effect;
            CooldownSeconds = cooldownSeconds;
            CheckIntervalSeconds = checkIntervalSeconds;
            MaxStack = maxStack;
        }

        public string ItemId { get; }
        public CombatConsumableUsePlace UsePlace { get; }
        public CombatConsumableConditionDescriptor Condition { get; }
        public CombatEffectDescriptor Effect { get; }
        public double CooldownSeconds { get; }
        public double CheckIntervalSeconds { get; }
        public int MaxStack { get; }
    }

    public interface ICombatConsumableDescriptorProvider
    {
        bool TryGet(string itemId, out CombatConsumableDescriptor descriptor);
    }

    public sealed class EmptyCombatConsumableDescriptorProvider :
        ICombatConsumableDescriptorProvider
    {
        public static readonly EmptyCombatConsumableDescriptorProvider Instance =
            new EmptyCombatConsumableDescriptorProvider();

        private EmptyCombatConsumableDescriptorProvider()
        {
        }

        public bool TryGet(string itemId, out CombatConsumableDescriptor descriptor)
        {
            descriptor = null;
            return false;
        }
    }

    public delegate bool TryEvaluateCombatConsumableCondition(
        CombatConsumableConditionDescriptor condition,
        CombatantStateSaveData hero,
        out bool satisfied,
        out string error);

    public sealed class CombatConsumableConditionRegistry
    {
        private readonly Dictionary<string, TryEvaluateCombatConsumableCondition> _handlers =
            new Dictionary<string, TryEvaluateCombatConsumableCondition>(StringComparer.Ordinal);

        public CombatConsumableConditionRegistry()
        {
            Register(
                CombatConsumableConditionKind.HpPercent,
                CombatConsumableComparisonOperator.LessOrEqual,
                TryEvaluateHpPercentLessOrEqual);
        }

        public void Register(
            CombatConsumableConditionKind kind,
            CombatConsumableComparisonOperator comparisonOperator,
            TryEvaluateCombatConsumableCondition handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            _handlers[Key(kind, comparisonOperator)] = handler;
        }

        public bool IsRegistered(
            CombatConsumableConditionKind kind,
            CombatConsumableComparisonOperator comparisonOperator)
        {
            return _handlers.ContainsKey(Key(kind, comparisonOperator));
        }

        public bool TryEvaluate(
            CombatConsumableConditionDescriptor condition,
            CombatantStateSaveData hero,
            out bool satisfied,
            out string error)
        {
            satisfied = false;
            error = null;
            if (condition == null ||
                !_handlers.TryGetValue(Key(condition.Kind, condition.Operator), out var handler))
            {
                error =
                    $"Combat consumable condition '{condition?.Kind.ToString() ?? "<null>"} " +
                    $"{condition?.Operator.ToString() ?? "<null>"}' is not registered.";
                return false;
            }

            try
            {
                return handler(condition, hero, out satisfied, out error);
            }
            catch (Exception exception)
            {
                error = $"Combat consumable condition handler failed: {exception.Message}";
                return false;
            }
        }

        private static bool TryEvaluateHpPercentLessOrEqual(
            CombatConsumableConditionDescriptor condition,
            CombatantStateSaveData hero,
            out bool satisfied,
            out string error)
        {
            satisfied = false;
            error = null;
            if (hero == null ||
                hero.maxHp <= 0 ||
                double.IsNaN(condition.Value) ||
                double.IsInfinity(condition.Value) ||
                condition.Value < 0d ||
                condition.Value > 100d)
            {
                error = "hp_percent condition requires a live hero with max HP and a finite threshold from 0 to 100.";
                return false;
            }

            satisfied = hero.currentHp * 100.0d / hero.maxHp <= condition.Value;
            return true;
        }

        private static string Key(
            CombatConsumableConditionKind kind,
            CombatConsumableComparisonOperator comparisonOperator)
        {
            return $"{(int)kind}:{(int)comparisonOperator}";
        }
    }

    public sealed class CombatConsumableDescriptorRepository : ICombatConsumableDescriptorProvider
    {
        private readonly Dictionary<string, CombatConsumableDescriptor> _descriptors =
            new Dictionary<string, CombatConsumableDescriptor>(StringComparer.Ordinal);

        public CombatConsumableDescriptorRepository(
            ItemsConfigRepository items,
            StorageConfigRepository storage)
        {
            items ??= new ItemsConfigRepository(null);
            storage ??= new StorageConfigRepository(null);

            foreach (var consumable in items.Consumables)
            {
                var matchingRule = FindSingleStorageRule(consumable, storage);
                if (!CombatConsumableConfigParser.TryCreate(
                        consumable,
                        matchingRule.maxStack,
                        out var descriptor,
                        out var error))
                {
                    var itemId = consumable?.id ?? "<null>";
                    throw new InvalidOperationException(
                        $"Combat consumable '{itemId}' is invalid: {error}");
                }

                if (_descriptors.ContainsKey(descriptor.ItemId))
                    throw new InvalidOperationException(
                        $"Duplicate combat consumable descriptor item id '{descriptor.ItemId}'.");

                _descriptors.Add(descriptor.ItemId, descriptor);
            }
        }

        public int Count => _descriptors.Count;

        public bool TryGet(string itemId, out CombatConsumableDescriptor descriptor)
        {
            descriptor = null;
            return !string.IsNullOrWhiteSpace(itemId) &&
                   _descriptors.TryGetValue(itemId, out descriptor);
        }

        private static StorageRuleConfigDto FindSingleStorageRule(
            ConsumableConfigDto consumable,
            StorageConfigRepository storage)
        {
            StorageRuleConfigDto matchingRule = null;
            var count = 0;
            foreach (var rule in storage.StorageRules)
            {
                if (rule == null ||
                    !string.Equals(rule.itemKind, consumable?.kind, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                count++;
                matchingRule = rule;
            }

            var itemId = consumable?.id ?? "<null>";
            if (count != 1)
            {
                throw new InvalidOperationException(
                    $"Combat consumable '{itemId}' requires exactly one StorageRule for item kind " +
                    $"'{consumable?.kind}', but found {count}.");
            }

            if (!string.Equals(matchingRule.mode, "stack", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Combat consumable '{itemId}' requires StorageRule mode 'stack'.");
            }

            if (matchingRule.maxStack <= 0)
            {
                throw new InvalidOperationException(
                    $"Combat consumable '{itemId}' requires StorageRule max_stack greater than 0.");
            }

            return matchingRule;
        }
    }

    public static class CombatConsumableConfigParser
    {
        private const string SupportedKind = "consumable";
        private const string SupportedUsePlace = "combat";
        private const string SupportedConditionName = "hp_percent";
        private const string SupportedConditionOperator = "<=";
        private const string SupportedEffectName = "RestoreHealthFlat";

        private delegate bool ConditionParser(
            CombatConsumableComparisonOperator comparisonOperator,
            string valueText,
            out CombatConsumableConditionDescriptor condition,
            out string error);

        private delegate bool EffectParser(
            string valueText,
            out CombatEffectDescriptor effect,
            out string error);

        private static readonly Dictionary<string, CombatConsumableComparisonOperator>
            ComparisonOperators =
                new Dictionary<string, CombatConsumableComparisonOperator>(StringComparer.Ordinal)
                {
                    [SupportedConditionOperator] =
                        CombatConsumableComparisonOperator.LessOrEqual
                };

        private static readonly Dictionary<string, ConditionParser> ConditionParsers =
            new Dictionary<string, ConditionParser>(StringComparer.Ordinal)
            {
                [SupportedConditionName] = TryParseHpPercentCondition
            };

        private static readonly Dictionary<string, EffectParser> EffectParsers =
            new Dictionary<string, EffectParser>(StringComparer.Ordinal)
            {
                [SupportedEffectName] = TryParseRestoreHealthFlatEffect
            };

        public static bool TryParseSource(
            string kind,
            string usePlace,
            string useCondition,
            string effects,
            string cooldownSeconds,
            string checkIntervalSeconds,
            out CombatConsumableConditionDescriptor condition,
            out CombatEffectDescriptor effect,
            out double parsedCooldownSeconds,
            out double parsedCheckIntervalSeconds,
            out string error)
        {
            condition = null;
            effect = null;
            parsedCooldownSeconds = 0d;
            parsedCheckIntervalSeconds = 0d;

            if (!TryParseSingleEffectField(effects, out var effectToken, out error))
                return false;

            if (!TryParseFiniteDouble(cooldownSeconds, out parsedCooldownSeconds))
            {
                error = "cooldown_seconds must be a finite invariant double.";
                return false;
            }

            if (!TryParseFiniteDouble(checkIntervalSeconds, out parsedCheckIntervalSeconds))
            {
                error = "check_interval_seconds must be a finite invariant double.";
                return false;
            }

            return TryParseValues(
                kind,
                usePlace,
                useCondition,
                new[] { effectToken },
                parsedCooldownSeconds,
                parsedCheckIntervalSeconds,
                out condition,
                out effect,
                out error);
        }

        public static bool TryCreate(
            ConsumableConfigDto config,
            int maxStack,
            out CombatConsumableDescriptor descriptor,
            out string error)
        {
            descriptor = null;
            if (config == null)
            {
                error = "config is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(config.id))
            {
                error = "item id is required.";
                return false;
            }

            if (!TryParseValues(
                    config.kind,
                    config.usePlace,
                    config.useCondition,
                    config.effects,
                    config.cooldownSeconds,
                    config.checkIntervalSeconds,
                    out var condition,
                    out var effect,
                    out error))
            {
                return false;
            }

            if (maxStack <= 0)
            {
                error = "MaxStack must be greater than 0.";
                return false;
            }

            descriptor = new CombatConsumableDescriptor(
                config.id,
                CombatConsumableUsePlace.Combat,
                condition,
                effect,
                config.cooldownSeconds,
                config.checkIntervalSeconds,
                maxStack);
            return true;
        }

        public static bool TryParseFiniteDouble(string raw, out double value)
        {
            var parsed = double.TryParse(
                (raw ?? string.Empty).Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
            if (parsed && !double.IsNaN(value) && !double.IsInfinity(value))
                return true;

            value = 0d;
            return false;
        }

        private static bool TryParseValues(
            string kind,
            string usePlace,
            string useCondition,
            IReadOnlyList<string> effects,
            double cooldownSeconds,
            double checkIntervalSeconds,
            out CombatConsumableConditionDescriptor condition,
            out CombatEffectDescriptor effect,
            out string error)
        {
            condition = null;
            effect = null;

            if (!string.Equals(kind?.Trim(), SupportedKind, StringComparison.OrdinalIgnoreCase))
            {
                error = $"kind must be '{SupportedKind}'.";
                return false;
            }

            if (!string.Equals(usePlace?.Trim(), SupportedUsePlace, StringComparison.OrdinalIgnoreCase))
            {
                error = $"use_place must be '{SupportedUsePlace}'.";
                return false;
            }

            if (!TryParseCondition(useCondition, out condition, out error))
                return false;

            if (effects == null || effects.Count != 1)
            {
                error = "effects must contain exactly one token.";
                return false;
            }

            if (!TryParseEffect(effects[0], out effect, out error))
                return false;

            if (double.IsNaN(cooldownSeconds) ||
                double.IsInfinity(cooldownSeconds) ||
                cooldownSeconds < 0d)
            {
                error = "cooldown_seconds must be finite and greater than or equal to 0.";
                return false;
            }

            if (double.IsNaN(checkIntervalSeconds) ||
                double.IsInfinity(checkIntervalSeconds) ||
                checkIntervalSeconds <= 0d)
            {
                error = "check_interval_seconds must be finite and greater than 0.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryParseSingleEffectField(
            string raw,
            out string effectToken,
            out string error)
        {
            effectToken = null;
            var tokens = (raw ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.None);
            if (tokens.Length != 1 || string.IsNullOrWhiteSpace(tokens[0]))
            {
                error = "effects must contain exactly one non-empty token.";
                return false;
            }

            effectToken = tokens[0].Trim();
            error = null;
            return true;
        }

        private static bool TryParseCondition(
            string raw,
            out CombatConsumableConditionDescriptor condition,
            out string error)
        {
            condition = null;
            var token = (raw ?? string.Empty).Trim();
            var operatorToken = string.Empty;
            var comparisonOperator = default(CombatConsumableComparisonOperator);
            var operatorIndex = -1;
            foreach (var registeredOperator in ComparisonOperators)
            {
                var index = token.IndexOf(registeredOperator.Key, StringComparison.Ordinal);
                if (index < 0)
                    continue;

                if (operatorIndex >= 0 ||
                    token.IndexOf(
                        registeredOperator.Key,
                        index + registeredOperator.Key.Length,
                        StringComparison.Ordinal) >= 0)
                {
                    error = "use_condition must contain exactly one registered operator.";
                    return false;
                }

                operatorToken = registeredOperator.Key;
                comparisonOperator = registeredOperator.Value;
                operatorIndex = index;
            }

            if (operatorIndex < 0)
            {
                error = "Unknown condition operator.";
                return false;
            }

            var name = token.Substring(0, operatorIndex).Trim();
            if (!ConditionParsers.TryGetValue(name, out var parser))
            {
                error = $"Unknown condition type '{name}'.";
                return false;
            }

            var valueText = token.Substring(operatorIndex + operatorToken.Length).Trim();
            return parser(comparisonOperator, valueText, out condition, out error);
        }

        private static bool TryParseHpPercentCondition(
            CombatConsumableComparisonOperator comparisonOperator,
            string valueText,
            out CombatConsumableConditionDescriptor condition,
            out string error)
        {
            condition = null;
            if (!TryParseFiniteDouble(valueText, out var value))
            {
                error = "hp_percent condition value must be a finite invariant double.";
                return false;
            }

            if (value < 0d || value > 100d)
            {
                error = "hp_percent condition value must be between 0 and 100 inclusive.";
                return false;
            }

            condition = new CombatConsumableConditionDescriptor(
                CombatConsumableConditionKind.HpPercent,
                comparisonOperator,
                value);
            error = null;
            return true;
        }

        private static bool TryParseEffect(
            string raw,
            out CombatEffectDescriptor effect,
            out string error)
        {
            effect = null;
            var token = (raw ?? string.Empty).Trim();
            var separatorIndex = token.IndexOf(':');
            if (separatorIndex < 0 || token.IndexOf(':', separatorIndex + 1) >= 0)
            {
                error = "effect must contain exactly one ':' separator.";
                return false;
            }

            var name = token.Substring(0, separatorIndex).Trim();
            if (!EffectParsers.TryGetValue(name, out var parser))
            {
                error = $"Unknown effect type '{name}'.";
                return false;
            }

            var valueText = token.Substring(separatorIndex + 1).Trim();
            return parser(valueText, out effect, out error);
        }

        private static bool TryParseRestoreHealthFlatEffect(
            string valueText,
            out CombatEffectDescriptor effect,
            out string error)
        {
            effect = null;
            if (!TryParseFiniteDouble(valueText, out var value))
            {
                error = "RestoreHealthFlat value must be a finite invariant double.";
                return false;
            }

            if (value <= 0d)
            {
                error = "RestoreHealthFlat value must be greater than 0.";
                return false;
            }

            effect = new CombatEffectDescriptor(CombatEffectKind.Heal, value: value);
            error = null;
            return true;
        }
    }
}
