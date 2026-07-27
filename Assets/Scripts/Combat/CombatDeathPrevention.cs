using System;
using System.Collections.Generic;
using GuildIdle.Configs;

namespace GuildIdle.Combat
{
    public static class CombatDeathPreventionTriggers
    {
        public const string OnHealthBelowZero = "OnHealthBelowZero";
    }

    public static class CombatDeathPreventionConditions
    {
        public const string HealthBelowZero = "hp<0";
    }

    public static class CombatDeathPreventionEffects
    {
        public const string PreventDeath = "PreventDeath";
    }

    public static class CombatDeathPreventionTargets
    {
        public const string Self = "self";
    }

    public static class CombatTerminalCandidateKinds
    {
        public const string Victory = "Victory";
        public const string Retreat = "Retreat";
        public const string Defeat = "Defeat";
    }

    public sealed class CombatDeathPreventionDescriptor
    {
        public CombatDeathPreventionDescriptor(
            string skillId,
            string effectId,
            string trigger,
            string condition,
            double chancePercent,
            string effect,
            string target,
            int value)
        {
            SkillId = skillId;
            EffectId = effectId;
            Trigger = trigger;
            Condition = condition;
            ChancePercent = chancePercent;
            Effect = effect;
            Target = target;
            Value = value;
        }

        public string SkillId { get; }
        public string EffectId { get; }
        public string Trigger { get; }
        public string Condition { get; }
        public double ChancePercent { get; }
        public string Effect { get; }
        public string Target { get; }
        public int Value { get; }
    }

    public interface ICombatDeathPreventionDescriptorProvider
    {
        bool TryGetDescriptors(
            CombatActorSide ownerSide,
            string ownerDefinitionId,
            out CombatDeathPreventionDescriptor[] descriptors,
            out string error);
    }

    public sealed class EmptyCombatDeathPreventionDescriptorProvider :
        ICombatDeathPreventionDescriptorProvider
    {
        public static readonly EmptyCombatDeathPreventionDescriptorProvider Instance =
            new EmptyCombatDeathPreventionDescriptorProvider();

        private EmptyCombatDeathPreventionDescriptorProvider()
        {
        }

        public bool TryGetDescriptors(
            CombatActorSide ownerSide,
            string ownerDefinitionId,
            out CombatDeathPreventionDescriptor[] descriptors,
            out string error)
        {
            descriptors = Array.Empty<CombatDeathPreventionDescriptor>();
            error = null;
            return true;
        }
    }

    public sealed class CombatDeathPreventionRegistry
    {
        public bool TryCreateDescriptor(
            HeroSkillEffectConfigDto source,
            out CombatDeathPreventionDescriptor descriptor,
            out string error)
        {
            descriptor = null;
            error = null;
            if (source == null ||
                string.IsNullOrWhiteSpace(source.skillId) ||
                string.IsNullOrWhiteSpace(source.effectId) ||
                !string.Equals(
                    source.trigger,
                    CombatDeathPreventionTriggers.OnHealthBelowZero,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    source.condition,
                    CombatDeathPreventionConditions.HealthBelowZero,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    source.effect,
                    CombatDeathPreventionEffects.PreventDeath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    source.target,
                    CombatDeathPreventionTargets.Self,
                    StringComparison.Ordinal) ||
                float.IsNaN(source.chancePercent) ||
                float.IsInfinity(source.chancePercent) ||
                source.chancePercent < 0f ||
                source.chancePercent > 100f ||
                float.IsNaN(source.value) ||
                float.IsInfinity(source.value) ||
                source.value != 1f)
            {
                error =
                    $"Death-prevention effect '{source?.effectId ?? "<null>"}' has an unsupported descriptor.";
                return false;
            }

            descriptor = new CombatDeathPreventionDescriptor(
                source.skillId,
                source.effectId,
                source.trigger,
                source.condition,
                source.chancePercent,
                source.effect,
                source.target,
                1);
            return true;
        }

        public bool Validate(
            CombatDeathPreventionDescriptor descriptor,
            out string error)
        {
            error = null;
            if (descriptor == null ||
                string.IsNullOrWhiteSpace(descriptor.SkillId) ||
                string.IsNullOrWhiteSpace(descriptor.EffectId) ||
                !string.Equals(
                    descriptor.Trigger,
                    CombatDeathPreventionTriggers.OnHealthBelowZero,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    descriptor.Condition,
                    CombatDeathPreventionConditions.HealthBelowZero,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    descriptor.Effect,
                    CombatDeathPreventionEffects.PreventDeath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    descriptor.Target,
                    CombatDeathPreventionTargets.Self,
                    StringComparison.Ordinal) ||
                double.IsNaN(descriptor.ChancePercent) ||
                double.IsInfinity(descriptor.ChancePercent) ||
                descriptor.ChancePercent < 0d ||
                descriptor.ChancePercent > 100d ||
                descriptor.Value != 1)
            {
                error =
                    $"Death-prevention effect '{descriptor?.EffectId ?? "<null>"}' is invalid.";
                return false;
            }

            return true;
        }
    }

    public sealed class ConfigCombatDeathPreventionDescriptorProvider :
        ICombatDeathPreventionDescriptorProvider
    {
        private readonly HeroesConfigRepository _configs;
        private readonly CombatDeathPreventionRegistry _registry;

        public ConfigCombatDeathPreventionDescriptorProvider(
            HeroesConfigRepository configs,
            CombatDeathPreventionRegistry registry = null)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _registry = registry ?? new CombatDeathPreventionRegistry();
        }

        public bool TryGetDescriptors(
            CombatActorSide ownerSide,
            string ownerDefinitionId,
            out CombatDeathPreventionDescriptor[] descriptors,
            out string error)
        {
            descriptors = Array.Empty<CombatDeathPreventionDescriptor>();
            error = null;
            if (ownerSide == CombatActorSide.Enemy)
                return true;
            if (ownerSide != CombatActorSide.Hero ||
                string.IsNullOrWhiteSpace(ownerDefinitionId) ||
                !_configs.TryGet(ownerDefinitionId, out var hero))
            {
                error =
                    $"Hero death-prevention owner '{ownerDefinitionId ?? "<null>"}' was not found.";
                return false;
            }

            var configuredSkillIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var skillId in hero.uniqueSkillIds ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(skillId) ||
                    !configuredSkillIds.Add(skillId))
                {
                    error =
                        $"Hero '{ownerDefinitionId}' has a missing or duplicated unique skill reference.";
                    return false;
                }
            }

            var enabledSkillIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var skill in _configs.HeroUniqueSkills ??
                                  Array.Empty<HeroUniqueSkillConfigDto>())
            {
                if (skill == null ||
                    !configuredSkillIds.Contains(skill.skillId))
                {
                    continue;
                }

                if (!string.Equals(skill.heroId, ownerDefinitionId, StringComparison.Ordinal))
                {
                    error =
                        $"Hero unique skill '{skill.skillId}' belongs to hero '{skill.heroId ?? "<null>"}' instead of '{ownerDefinitionId}'.";
                    return false;
                }

                if (!skill.enabled)
                    continue;
                if (!enabledSkillIds.Add(skill.skillId))
                {
                    error =
                        $"Hero '{ownerDefinitionId}' has a missing or duplicated enabled unique skill.";
                    return false;
                }
            }

            foreach (var skillId in configuredSkillIds)
            {
                if (!enabledSkillIds.Contains(skillId))
                {
                    error =
                        $"Hero '{ownerDefinitionId}' unique skill '{skillId}' is missing from its enabled unique skills.";
                    return false;
                }
            }

            var result = new List<CombatDeathPreventionDescriptor>();
            var effectIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in _configs.GetEffectsByTrigger(
                         CombatDeathPreventionTriggers.OnHealthBelowZero))
            {
                if (source == null || !enabledSkillIds.Contains(source.skillId))
                    continue;
                if (!effectIds.Add(source.effectId))
                {
                    error =
                        $"Hero '{ownerDefinitionId}' has duplicated death-prevention effect '{source.effectId}'.";
                    return false;
                }

                if (!_registry.TryCreateDescriptor(source, out var descriptor, out error))
                    return false;
                result.Add(descriptor);
            }

            result.Sort(CompareDescriptors);
            if (result.Count > CombatRuntimeSaveDataUtility.PersistentCollectionLimit)
            {
                error = "Death-prevention descriptor list exceeds the bounded combat state limit.";
                return false;
            }

            descriptors = result.ToArray();
            return true;
        }

        private static int CompareDescriptors(
            CombatDeathPreventionDescriptor left,
            CombatDeathPreventionDescriptor right)
        {
            var result = string.Compare(left?.SkillId, right?.SkillId, StringComparison.Ordinal);
            return result != 0
                ? result
                : string.Compare(left?.EffectId, right?.EffectId, StringComparison.Ordinal);
        }
    }

    public sealed class CombatDeathPreventionResultEvent : CombatEvent
    {
        public CombatDeathPreventionResultEvent(
            string eventKey,
            double timestampSeconds,
            long sequence,
            CombatActorSide targetSide,
            string targetCombatantId,
            string skillId,
            string effectId,
            int chanceRoll,
            int chanceBasisPoints,
            bool successful,
            int finalHp)
            : base(
                eventKey,
                timestampSeconds,
                sequence,
                targetSide,
                targetCombatantId,
                targetCombatantId)
        {
            SkillId = skillId;
            EffectId = effectId;
            ChanceRoll = chanceRoll;
            ChanceBasisPoints = chanceBasisPoints;
            Successful = successful;
            FinalHp = finalHp;
        }

        public string SkillId { get; }
        public string EffectId { get; }
        public int ChanceRoll { get; }
        public int ChanceBasisPoints { get; }
        public bool Successful { get; }
        public int FinalHp { get; }
    }

    public sealed class CombatDeathPreventedEvent : CombatEvent
    {
        public CombatDeathPreventedEvent(
            string eventKey,
            double timestampSeconds,
            long sequence,
            CombatActorSide targetSide,
            string targetCombatantId,
            string effectId,
            int finalHp)
            : base(
                eventKey,
                timestampSeconds,
                sequence,
                targetSide,
                targetCombatantId,
                targetCombatantId)
        {
            EffectId = effectId;
            FinalHp = finalHp;
        }

        public string EffectId { get; }
        public int FinalHp { get; }
    }

    public sealed class CombatTerminalCandidateCreatedEvent : CombatEvent
    {
        public CombatTerminalCandidateCreatedEvent(
            string eventKey,
            double timestampSeconds,
            long sequence,
            string targetCombatantId,
            string sourceEffectId,
            string candidateId,
            string kind,
            int finalHp)
            : base(
                eventKey,
                timestampSeconds,
                sequence,
                CombatActorSide.Hero,
                targetCombatantId,
                targetCombatantId)
        {
            SourceEffectId = sourceEffectId;
            CandidateId = candidateId;
            Kind = kind;
            FinalHp = finalHp;
        }

        public string SourceEffectId { get; }
        public string CandidateId { get; }
        public string Kind { get; }
        public int FinalHp { get; }
    }

    public sealed class CombatHpMutation
    {
        private readonly Action<int> _setEventFinalHp;

        public CombatHpMutation(
            string sourceEventKey,
            string sourceEffectId,
            double timestampSeconds,
            long sequence,
            CombatActorSide sourceSide,
            string sourceCombatantId,
            CombatActorSide targetSide,
            string targetCombatantId,
            int hpBefore,
            long rawHpAfter,
            int finalHpAfter,
            Action<int> setEventFinalHp)
        {
            SourceEventKey = sourceEventKey;
            SourceEffectId = sourceEffectId;
            TimestampSeconds = timestampSeconds;
            Sequence = sequence;
            SourceSide = sourceSide;
            SourceCombatantId = sourceCombatantId;
            TargetSide = targetSide;
            TargetCombatantId = targetCombatantId;
            HpBefore = hpBefore;
            RawHpAfter = rawHpAfter;
            FinalHpAfter = finalHpAfter;
            _setEventFinalHp = setEventFinalHp;
        }

        public string SourceEventKey { get; }
        public string SourceEffectId { get; }
        public double TimestampSeconds { get; }
        public long Sequence { get; }
        public CombatActorSide SourceSide { get; }
        public string SourceCombatantId { get; }
        public CombatActorSide TargetSide { get; }
        public string TargetCombatantId { get; }
        public int HpBefore { get; }
        public long RawHpAfter { get; }
        public int FinalHpAfter { get; private set; }

        public void SetFinalHp(int value)
        {
            FinalHpAfter = value;
            _setEventFinalHp?.Invoke(value);
        }
    }

    internal sealed class CombatDeathPreventionResolver
    {
        private const int ChanceRollResolution = 10000;
        private const double ChanceRollsPerPercent = ChanceRollResolution / 100d;

        private readonly ICombatDeathPreventionDescriptorProvider _provider;
        private readonly CombatDeathPreventionRegistry _registry;

        public CombatDeathPreventionResolver(
            ICombatDeathPreventionDescriptorProvider provider,
            CombatDeathPreventionRegistry registry)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public bool TryResolve(
            CombatSessionSaveData session,
            CombatantStateSaveData target,
            CombatHpMutation mutation,
            ICombatRng rng,
            List<CombatEvent> events,
            out bool prevented,
            out CombatAdvanceError error)
        {
            prevented = false;
            error = null;
            if (session?.scheduler == null ||
                target == null ||
                mutation == null ||
                rng == null ||
                events == null ||
                mutation.RawHpAfter >= 0L)
            {
                return true;
            }

            CombatDeathPreventionDescriptor[] descriptors;
            try
            {
                if (!_provider.TryGetDescriptors(
                        mutation.TargetSide,
                        target.definitionId,
                        out descriptors,
                        out var providerError) ||
                    descriptors == null)
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.DeathPreventionDescriptorNotFound,
                        providerError ??
                        $"Death-prevention descriptors for '{target.definitionId}' were not found.");
                    return false;
                }
            }
            catch (Exception exception)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidDeathPreventionDescriptor,
                    $"Death-prevention provider failed: {exception.Message}");
                return false;
            }

            if (descriptors.Length > CombatRuntimeSaveDataUtility.PersistentCollectionLimit)
            {
                error = new CombatAdvanceError(
                    CombatAdvanceErrorCode.InvalidDeathPreventionDescriptor,
                    "Death-prevention descriptor list exceeds the bounded combat state limit.");
                return false;
            }

            var ordered = (CombatDeathPreventionDescriptor[])descriptors.Clone();
            Array.Sort(ordered, CompareDescriptors);
            var effectIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var descriptor in ordered)
            {
                if (!_registry.Validate(descriptor, out var descriptorError) ||
                    !effectIds.Add(descriptor?.EffectId))
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.InvalidDeathPreventionDescriptor,
                        descriptorError ??
                        $"Death-prevention effect '{descriptor?.EffectId ?? "<null>"}' is duplicated.");
                    return false;
                }

                var operationKey =
                    $"{mutation.SourceEventKey}:death-prevention:{target.combatantId}:{descriptor.SkillId}:{descriptor.EffectId}";
                var saved = session.lastDeathPreventionOperation;
                if (string.Equals(saved?.operationKey, operationKey, StringComparison.Ordinal))
                {
                    prevented = saved.successful;
                    if (prevented)
                    {
                        target.currentHp = descriptor.Value;
                        mutation.SetFinalHp(descriptor.Value);
                    }
                    return true;
                }

                var chanceRoll = RollInclusive(rng, 1, ChanceRollResolution);
                var chanceBasisPoints = (int)Math.Round(
                    descriptor.ChancePercent * ChanceRollsPerPercent,
                    MidpointRounding.AwayFromZero);
                var successful = chanceRoll <= chanceBasisPoints;
                session.lastDeathPreventionOperation =
                    new CombatDeathPreventionOperationSaveData
                    {
                        operationKey = operationKey,
                        targetCombatantId = target.combatantId,
                        effectId = descriptor.EffectId,
                        chanceRoll = chanceRoll,
                        successful = successful
                    };

                if (!TryReserveSequence(session.scheduler, out var resultSequence))
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.DeathPreventionFailed,
                        "Combat scheduler sequence is exhausted during death prevention.");
                    return false;
                }

                events.Add(new CombatDeathPreventionResultEvent(
                    operationKey,
                    mutation.TimestampSeconds,
                    resultSequence,
                    mutation.TargetSide,
                    target.combatantId,
                    descriptor.SkillId,
                    descriptor.EffectId,
                    chanceRoll,
                    chanceBasisPoints,
                    successful,
                    successful ? descriptor.Value : 0));
                if (!successful)
                    continue;

                target.currentHp = descriptor.Value;
                mutation.SetFinalHp(descriptor.Value);
                prevented = true;
                if (!TryReserveSequence(session.scheduler, out var successSequence))
                {
                    error = new CombatAdvanceError(
                        CombatAdvanceErrorCode.DeathPreventionFailed,
                        "Combat scheduler sequence is exhausted during death prevention.");
                    return false;
                }

                events.Add(new CombatDeathPreventedEvent(
                    $"{operationKey}:successful",
                    mutation.TimestampSeconds,
                    successSequence,
                    mutation.TargetSide,
                    target.combatantId,
                    descriptor.EffectId,
                    descriptor.Value));
                return true;
            }

            return true;
        }

        private static int CompareDescriptors(
            CombatDeathPreventionDescriptor left,
            CombatDeathPreventionDescriptor right)
        {
            var result = string.Compare(left?.SkillId, right?.SkillId, StringComparison.Ordinal);
            return result != 0
                ? result
                : string.Compare(left?.EffectId, right?.EffectId, StringComparison.Ordinal);
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
    }
}
