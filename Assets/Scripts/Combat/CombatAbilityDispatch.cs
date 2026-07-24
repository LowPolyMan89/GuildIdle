using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using GuildIdle.Configs;

namespace GuildIdle.Combat
{
    public static class CombatAbilityTriggers
    {
        public const string OnBattleStart = "OnBattleStart";
        public const string OnAttackHit = "OnAttackHit";
    }

    public enum CombatEffectKind
    {
        ApplyStatus = 0,
        ModifyStat = 1,
        Damage = 2,
        Heal = 3
    }

    public sealed class CombatEffectDescriptor
    {
        public CombatEffectDescriptor(
            CombatEffectKind kind,
            string statusId = null,
            string statId = null,
            double value = 0d,
            double durationSeconds = 0d,
            string damageType = null,
            string operation = null)
        {
            Kind = kind;
            StatusId = statusId;
            StatId = statId;
            Value = value;
            DurationSeconds = durationSeconds;
            DamageType = damageType;
            Operation = operation;
        }

        public CombatEffectKind Kind { get; }
        public string StatusId { get; }
        public string StatId { get; }
        public double Value { get; }
        public double DurationSeconds { get; }
        public string DamageType { get; }
        public string Operation { get; }
    }

    public sealed class CombatAbilityDescriptor
    {
        public CombatAbilityDescriptor(
            string abilityId,
            string trigger,
            double chancePercent,
            string target,
            double cooldownSeconds,
            CombatEffectDescriptor effect)
        {
            AbilityId = abilityId;
            Trigger = trigger;
            ChancePercent = chancePercent;
            Target = target;
            CooldownSeconds = cooldownSeconds;
            Effect = effect;
        }

        public string AbilityId { get; }
        public string Trigger { get; }
        public double ChancePercent { get; }
        public string Target { get; }
        public double CooldownSeconds { get; }
        public CombatEffectDescriptor Effect { get; }
    }

    public sealed class CombatEffectRequest : CombatEvent
    {
        public CombatEffectRequest(
            string eventKey,
            string triggerEventKey,
            double timestampSeconds,
            long sequence,
            CombatActorSide sourceOwnerSide,
            string sourceOwnerCombatantId,
            string sourceAbilityId,
            string trigger,
            string targetCombatantId,
            CombatEffectDescriptor effect)
            : base(
                eventKey,
                timestampSeconds,
                sequence,
                sourceOwnerSide,
                sourceOwnerCombatantId,
                targetCombatantId)
        {
            TriggerEventKey = triggerEventKey;
            SourceOwnerSide = sourceOwnerSide;
            SourceOwnerCombatantId = sourceOwnerCombatantId;
            SourceAbilityId = sourceAbilityId;
            Trigger = trigger;
            Effect = effect;
        }

        public string TriggerEventKey { get; }
        public CombatActorSide SourceOwnerSide { get; }
        public string SourceOwnerCombatantId { get; }
        public string SourceAbilityId { get; }
        public string Trigger { get; }
        public CombatEffectDescriptor Effect { get; }
    }

    public interface ICombatAbilityDescriptorProvider
    {
        bool TryGetAbilities(
            CombatActorSide ownerSide,
            string ownerDefinitionId,
            out CombatAbilityDescriptor[] abilities,
            out string error);
    }

    public sealed class EmptyCombatAbilityDescriptorProvider : ICombatAbilityDescriptorProvider
    {
        public static readonly EmptyCombatAbilityDescriptorProvider Instance =
            new EmptyCombatAbilityDescriptorProvider();

        private EmptyCombatAbilityDescriptorProvider()
        {
        }

        public bool TryGetAbilities(
            CombatActorSide ownerSide,
            string ownerDefinitionId,
            out CombatAbilityDescriptor[] abilities,
            out string error)
        {
            abilities = Array.Empty<CombatAbilityDescriptor>();
            error = null;
            return true;
        }
    }

    public sealed class CombatAbilityRegistry
    {
        private static readonly Regex ApplyStatusPattern = new Regex(
            @"^\s*ApplyStatus\s*:\s*(?<status>[A-Za-z0-9_.-]+)\s*$",
            RegexOptions.CultureInvariant);

        private static readonly Regex ModifyStatPattern = new Regex(
            @"^\s*ModifyStat\s*:\s*(?<stat>[A-Za-z0-9_.-]+)\s+(?<value>[+-]?(?:\d+(?:\.\d+)?|\.\d+))\s*,\s*duration\s+(?<duration>\d+(?:\.\d+)?)\s*sec\s*$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private readonly HashSet<string> _triggers = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Func<CombatActorSide, CombatActorSide>> _targets =
            new Dictionary<string, Func<CombatActorSide, CombatActorSide>>(StringComparer.Ordinal);
        private readonly Dictionary<string, TryParseEffect> _effectParsers =
            new Dictionary<string, TryParseEffect>(StringComparer.Ordinal);

        private delegate bool TryParseEffect(
            string value,
            out CombatEffectDescriptor effect,
            out string error);

        public CombatAbilityRegistry()
        {
            _triggers.Add(CombatAbilityTriggers.OnBattleStart);
            _triggers.Add(CombatAbilityTriggers.OnAttackHit);
            _targets.Add("self", side => side);
            _targets.Add("enemy", Opposite);
            _effectParsers.Add("ApplyStatus", TryParseApplyStatus);
            _effectParsers.Add("ModifyStat", TryParseModifyStat);
        }

        public bool IsSupportedTrigger(string trigger)
        {
            return !string.IsNullOrWhiteSpace(trigger) && _triggers.Contains(trigger);
        }

        public bool TryResolveTarget(
            string target,
            CombatActorSide ownerSide,
            out CombatActorSide targetSide)
        {
            targetSide = CombatActorSide.System;
            if (ownerSide == CombatActorSide.System ||
                string.IsNullOrWhiteSpace(target) ||
                !_targets.TryGetValue(target, out var resolver))
            {
                return false;
            }

            targetSide = resolver(ownerSide);
            return targetSide != CombatActorSide.System;
        }

        public bool TryCreateDescriptor(
            EnemyAbilityConfigDto source,
            out CombatAbilityDescriptor descriptor,
            out string error)
        {
            descriptor = null;
            error = null;
            if (source == null ||
                string.IsNullOrWhiteSpace(source.abilityId) ||
                !IsSupportedTrigger(source.trigger) ||
                !ValidPercent(source.chancePercent) ||
                source.cooldownSec < 0 ||
                !TryResolveTarget(source.target, CombatActorSide.Enemy, out _) ||
                !TryParseEffectDescriptor(source.effects, out var effect, out error))
            {
                error ??= $"Ability '{source?.abilityId ?? "<null>"}' has an invalid descriptor.";
                return false;
            }

            descriptor = new CombatAbilityDescriptor(
                source.abilityId,
                source.trigger,
                source.chancePercent,
                source.target,
                source.cooldownSec,
                effect);
            return true;
        }

        public bool TryParseEffectDescriptor(
            string value,
            out CombatEffectDescriptor effect,
            out string error)
        {
            effect = null;
            error = null;
            var separator = value?.IndexOf(':') ?? -1;
            if (separator <= 0)
            {
                error = "Combat ability effect must use a registered effect type.";
                return false;
            }

            var effectType = value.Substring(0, separator).Trim();
            if (!_effectParsers.TryGetValue(effectType, out var parser))
            {
                error = $"Combat ability effect type '{effectType}' is not registered.";
                return false;
            }

            return parser(value, out effect, out error);
        }

        private static bool TryParseApplyStatus(
            string value,
            out CombatEffectDescriptor effect,
            out string error)
        {
            effect = null;
            error = null;
            var match = ApplyStatusPattern.Match(value ?? string.Empty);
            if (!match.Success)
            {
                error = "ApplyStatus effect must contain exactly one status id.";
                return false;
            }

            effect = new CombatEffectDescriptor(
                CombatEffectKind.ApplyStatus,
                statusId: match.Groups["status"].Value);
            return true;
        }

        private static bool TryParseModifyStat(
            string value,
            out CombatEffectDescriptor effect,
            out string error)
        {
            effect = null;
            error = null;
            var match = ModifyStatPattern.Match(value ?? string.Empty);
            if (!match.Success ||
                !double.TryParse(
                    match.Groups["value"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var modifierValue) ||
                !double.TryParse(
                    match.Groups["duration"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var durationSeconds) ||
                InvalidNumber(modifierValue) ||
                InvalidNumber(durationSeconds) ||
                durationSeconds <= 0d)
            {
                error = "ModifyStat effect must contain a stat, finite value and positive duration in seconds.";
                return false;
            }

            effect = new CombatEffectDescriptor(
                CombatEffectKind.ModifyStat,
                statId: match.Groups["stat"].Value,
                value: modifierValue,
                durationSeconds: durationSeconds,
                operation: CombatModifierOperations.Add);
            return true;
        }

        private static CombatActorSide Opposite(CombatActorSide side)
        {
            return side == CombatActorSide.Hero
                ? CombatActorSide.Enemy
                : side == CombatActorSide.Enemy
                    ? CombatActorSide.Hero
                    : CombatActorSide.System;
        }

        private static bool ValidPercent(double value)
        {
            return !InvalidNumber(value) && value >= 0d && value <= 100d;
        }

        private static bool InvalidNumber(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value);
        }
    }

    public sealed class ConfigCombatAbilityDescriptorProvider : ICombatAbilityDescriptorProvider
    {
        private readonly EnemiesConfigRepository _configs;
        private readonly CombatAbilityRegistry _registry;

        public ConfigCombatAbilityDescriptorProvider(
            EnemiesConfigRepository configs,
            CombatAbilityRegistry registry = null)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _registry = registry ?? new CombatAbilityRegistry();
        }

        public bool TryGetAbilities(
            CombatActorSide ownerSide,
            string ownerDefinitionId,
            out CombatAbilityDescriptor[] abilities,
            out string error)
        {
            abilities = Array.Empty<CombatAbilityDescriptor>();
            error = null;
            if (ownerSide == CombatActorSide.Hero)
                return true;
            if (ownerSide != CombatActorSide.Enemy || string.IsNullOrWhiteSpace(ownerDefinitionId))
            {
                error = "Config combat abilities require a hero or enemy owner.";
                return false;
            }

            if (!_configs.TryGet(ownerDefinitionId, out var owner))
            {
                error = $"Enemy ability owner '{ownerDefinitionId}' was not found.";
                return false;
            }

            var abilityIds = owner.combatAbilityIds ?? Array.Empty<string>();
            if (abilityIds.Length > CombatRuntimeSaveDataUtility.PersistentCollectionLimit)
            {
                error = "Enemy ability list exceeds the bounded combat state limit.";
                return false;
            }

            var result = new CombatAbilityDescriptor[abilityIds.Length];
            var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < abilityIds.Length; index++)
            {
                var abilityId = abilityIds[index];
                if (string.IsNullOrWhiteSpace(abilityId) ||
                    !uniqueIds.Add(abilityId) ||
                    !_configs.TryGetAbility(abilityId, out var source))
                {
                    error = $"Enemy ability '{abilityId ?? "<null>"}' is missing or duplicated.";
                    return false;
                }

                if (!_registry.TryCreateDescriptor(source, out result[index], out error))
                    return false;
                if (result[index].Effect.Kind == CombatEffectKind.ApplyStatus &&
                    !_configs.TryGetCombatStatus(result[index].Effect.StatusId, out _))
                {
                    error = $"Enemy ability '{abilityId}' references missing combat status '{result[index].Effect.StatusId}'.";
                    return false;
                }
            }

            abilities = result;
            return true;
        }
    }

    internal sealed class CombatAbilityDispatcher
    {
        private const int ChanceRollResolution = 10000;
        private const double ChanceRollsPerPercent = ChanceRollResolution / 100d;

        private readonly ICombatAbilityDescriptorProvider _provider;
        private readonly CombatAbilityRegistry _registry;

        public CombatAbilityDispatcher(
            ICombatAbilityDescriptorProvider provider,
            CombatAbilityRegistry registry)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public bool TryDispatch(
            CombatSessionSaveData session,
            CombatActorSide ownerSide,
            string trigger,
            string triggerEventKey,
            double timestampSeconds,
            ICombatRng rng,
            List<CombatEvent> events,
            out bool stateChanged,
            out CombatAdvanceError error)
        {
            stateChanged = false;
            error = null;
            var owner = GetCombatant(session, ownerSide);
            if (session == null ||
                session.scheduler == null ||
                owner == null ||
                rng == null ||
                events == null ||
                string.IsNullOrWhiteSpace(triggerEventKey) ||
                InvalidTime(timestampSeconds))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.AbilityDispatchFailed,
                    "Combat ability dispatch requires a live owner, scheduler, RNG and trigger event.");
                return false;
            }

            if (owner.currentHp <= 0)
                return true;

            if (!_registry.IsSupportedTrigger(trigger))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.UnsupportedAbilityTrigger,
                    $"Combat ability trigger '{trigger ?? "<null>"}' is not registered.");
                return false;
            }

            CombatAbilityDescriptor[] abilities;
            try
            {
                if (!_provider.TryGetAbilities(
                        ownerSide,
                        owner.definitionId,
                        out abilities,
                        out var providerError) ||
                    abilities == null)
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.AbilityDescriptorNotFound,
                        providerError ?? $"Abilities for '{owner.definitionId}' were not found.");
                    return false;
                }
            }
            catch (Exception exception)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidAbilityDescriptor,
                    $"Combat ability provider failed: {exception.Message}");
                return false;
            }

            if (abilities.Length > CombatRuntimeSaveDataUtility.PersistentCollectionLimit)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidAbilityDescriptor,
                    "Combat ability descriptor list exceeds the bounded state limit.");
                return false;
            }

            var ordered = (CombatAbilityDescriptor[])abilities.Clone();
            Array.Sort(
                ordered,
                (left, right) => string.Compare(
                    left?.AbilityId,
                    right?.AbilityId,
                    StringComparison.Ordinal));
            var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ability in ordered)
            {
                if (!ValidateDescriptor(ability, ownerSide, uniqueIds, out error))
                    return false;
                if (!string.Equals(ability.Trigger, trigger, StringComparison.Ordinal))
                    continue;

                var operationKey =
                    $"{triggerEventKey}:ability:{owner.combatantId}:{ability.AbilityId}";
                var cooldown = FindCooldown(owner, ability.AbilityId);
                if (string.Equals(cooldown?.lastTriggerEventKey, operationKey, StringComparison.Ordinal))
                    continue;

                if (cooldown == null)
                {
                    if (!TryAddCooldown(owner, ability.AbilityId, out cooldown))
                    {
                        error = new CombatAdvanceError(
                            CombatAdvanceErrorCode.AbilityDispatchFailed,
                            "Combat ability cooldown state reached its bounded retention limit.");
                        return false;
                    }
                }

                cooldown.lastTriggerEventKey = operationKey;
                stateChanged = true;
                if (timestampSeconds < cooldown.nextReadyAtSeconds)
                    continue;

                var chanceRoll = RollInclusive(rng, 1, ChanceRollResolution);
                cooldown.lastChanceRoll = chanceRoll;
                cooldown.lastChanceResolved = true;
                var successfulRolls = (int)Math.Round(
                    ability.ChancePercent * ChanceRollsPerPercent,
                    MidpointRounding.AwayFromZero);
                if (chanceRoll > successfulRolls)
                    continue;

                if (!_registry.TryResolveTarget(ability.Target, ownerSide, out var targetSide))
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.UnsupportedAbilityTarget,
                        $"Combat ability target '{ability.Target}' is not registered.");
                    return false;
                }

                var target = GetCombatant(session, targetSide);
                if (target == null)
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.AbilityDispatchFailed,
                        $"Combat ability target '{ability.Target}' is not present.");
                    return false;
                }

                if (!TryReserveSequence(session.scheduler, out var sequence))
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.AbilityDispatchFailed,
                        "Combat scheduler sequence is exhausted.");
                    return false;
                }

                cooldown.nextReadyAtSeconds = timestampSeconds + ability.CooldownSeconds;
                events.Add(new CombatEffectRequest(
                    operationKey,
                    triggerEventKey,
                    timestampSeconds,
                    sequence,
                    ownerSide,
                    owner.combatantId,
                    ability.AbilityId,
                    trigger,
                    target.combatantId,
                    ability.Effect));
            }

            return true;
        }

        private bool ValidateDescriptor(
            CombatAbilityDescriptor ability,
            CombatActorSide ownerSide,
            HashSet<string> uniqueIds,
            out CombatAdvanceError error)
        {
            error = null;
            if (ability == null ||
                string.IsNullOrWhiteSpace(ability.AbilityId) ||
                !uniqueIds.Add(ability.AbilityId) ||
                !_registry.IsSupportedTrigger(ability.Trigger) ||
                !ValidPercent(ability.ChancePercent) ||
                InvalidTime(ability.CooldownSeconds) ||
                !ValidateEffect(ability.Effect))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidAbilityDescriptor,
                    $"Combat ability '{ability?.AbilityId ?? "<null>"}' is invalid or duplicated.");
                return false;
            }

            if (!_registry.TryResolveTarget(ability.Target, ownerSide, out _))
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.UnsupportedAbilityTarget,
                    $"Combat ability target '{ability.Target ?? "<null>"}' is not registered.");
                return false;
            }

            return true;
        }

        private static bool ValidateEffect(CombatEffectDescriptor effect)
        {
            if (effect == null)
                return false;
            if (effect.Kind == CombatEffectKind.ApplyStatus)
                return !string.IsNullOrWhiteSpace(effect.StatusId);
            return effect.Kind == CombatEffectKind.ModifyStat &&
                   !string.IsNullOrWhiteSpace(effect.StatId) &&
                   !InvalidNumber(effect.Value) &&
                   !InvalidTime(effect.DurationSeconds) &&
                   effect.DurationSeconds > 0d;
        }

        private static CombatantStateSaveData GetCombatant(
            CombatSessionSaveData session,
            CombatActorSide side)
        {
            if (session == null)
                return null;
            return side == CombatActorSide.Hero
                ? session.hero
                : side == CombatActorSide.Enemy
                    ? session.currentEnemy
                    : null;
        }

        private static CombatAbilityCooldownSaveData FindCooldown(
            CombatantStateSaveData owner,
            string abilityId)
        {
            foreach (var value in owner.abilityCooldowns ?? Array.Empty<CombatAbilityCooldownSaveData>())
                if (value != null && string.Equals(value.abilityId, abilityId, StringComparison.Ordinal))
                    return value;
            return null;
        }

        private static bool TryAddCooldown(
            CombatantStateSaveData owner,
            string abilityId,
            out CombatAbilityCooldownSaveData cooldown)
        {
            var values = owner.abilityCooldowns ?? Array.Empty<CombatAbilityCooldownSaveData>();
            cooldown = null;
            if (values.Length >= CombatRuntimeSaveDataUtility.PersistentCollectionLimit)
                return false;

            cooldown = new CombatAbilityCooldownSaveData { abilityId = abilityId };
            var expanded = new CombatAbilityCooldownSaveData[values.Length + 1];
            Array.Copy(values, expanded, values.Length);
            expanded[expanded.Length - 1] = cooldown;
            Array.Sort(
                expanded,
                (left, right) => string.Compare(
                    left?.abilityId,
                    right?.abilityId,
                    StringComparison.Ordinal));
            owner.abilityCooldowns = expanded;
            return true;
        }

        private static bool TryReserveSequence(
            CombatSchedulerStateSaveData scheduler,
            out long sequence)
        {
            sequence = scheduler.nextSequence;
            if (sequence == long.MaxValue)
                return false;
            scheduler.nextSequence++;
            return true;
        }

        private static int RollInclusive(ICombatRng rng, int minimum, int maximum)
        {
            var range = (ulong)((long)maximum - minimum + 1L);
            var threshold = unchecked(0UL - range) % range;
            ulong value;
            do
            {
                value = rng.NextUInt64();
            } while (value < threshold);

            return (int)(minimum + (long)(value % range));
        }

        private static bool ValidPercent(double value)
        {
            return !InvalidNumber(value) && value >= 0d && value <= 100d;
        }

        private static bool InvalidTime(double value)
        {
            return InvalidNumber(value) || value < 0d;
        }

        private static bool InvalidNumber(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value);
        }
    }
}
