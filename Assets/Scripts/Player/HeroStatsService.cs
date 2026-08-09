using System;
using System.Collections.Generic;
using GuildIdle.Configs;

namespace GuildIdle.Player
{
    public interface IHeroStatsConfigProvider
    {
        HeroGrowthConfigDto[] HeroGrowth { get; }
        SkillProgressionConfigDto[] SkillProgression { get; }
        bool TryGetHero(string heroId, out HeroConfigDto hero);
        bool TryGetFormula(string formulaId, out FormulaConfigDto formula);
    }

    public sealed class HeroStatsService
    {
        public const string MaxFatigueFormulaId = "hero_max_fatigue";
        public const int DefaultMaxFatigue = 100;

        private static readonly string[] PrimaryStatIdValues =
        {
            "Strength",
            "Agility",
            "Intelligence",
            "Luck",
            "Endurance"
        };

        private static readonly IReadOnlyList<string> ReadOnlyPrimaryStatIds =
            Array.AsReadOnly(PrimaryStatIdValues);

        public static IReadOnlyList<string> PrimaryStatIds => ReadOnlyPrimaryStatIds;

        private readonly IHeroStatsConfigProvider _configs;

        public HeroStatsService(IHeroStatsConfigProvider configs)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public int ResolveSkillLevel(long exp)
        {
            var level = 1;
            foreach (var row in _configs.SkillProgression)
            {
                if (row != null && exp >= row.totalExpRequired)
                    level = Math.Max(level, row.level);
            }

            return level;
        }

        public int CalculateHeroStat(string heroId, string statId, int level)
        {
            if (!_configs.TryGetHero(heroId, out var hero) || hero == null)
                return 0;

            var value = GetBaseStat(hero.baseStats, statId);
            foreach (var growth in _configs.HeroGrowth)
            {
                if (growth == null ||
                    growth.level > level ||
                    !string.Equals(growth.heroId, heroId, StringComparison.Ordinal))
                {
                    continue;
                }

                value += GetGrowthStat(growth, statId);
            }

            return value;
        }

        public int CalculateMaxFatigue(string heroId, int level)
        {
            if (!_configs.TryGetFormula(MaxFatigueFormulaId, out var formula) ||
                formula == null ||
                !formula.enabled)
            {
                return DefaultMaxFatigue;
            }

            var resolvedLevel = Math.Max(1, level);
            var primaryStat = CalculateHeroStat(heroId, formula.primaryStat, resolvedLevel);
            var value = formula.baseValue +
                primaryStat * formula.primaryStatMultiplier +
                resolvedLevel * formula.levelMultiplier;
            return Math.Max(1, RoundFormulaValue(value, formula.rounding));
        }

        private static int GetBaseStat(HeroBaseStatsDto stats, string statId)
        {
            if (stats == null)
                return 0;

            if (string.Equals(statId, "Strength", StringComparison.OrdinalIgnoreCase))
                return stats.strength;
            if (string.Equals(statId, "Agility", StringComparison.OrdinalIgnoreCase))
                return stats.agility;
            if (string.Equals(statId, "Intelligence", StringComparison.OrdinalIgnoreCase))
                return stats.intelligence;
            if (string.Equals(statId, "Luck", StringComparison.OrdinalIgnoreCase))
                return stats.luck;
            if (string.Equals(statId, "Endurance", StringComparison.OrdinalIgnoreCase))
                return stats.endurance;

            return 0;
        }

        private static int GetGrowthStat(HeroGrowthConfigDto growth, string statId)
        {
            if (string.Equals(statId, "Strength", StringComparison.OrdinalIgnoreCase))
                return growth.addStrength;
            if (string.Equals(statId, "Agility", StringComparison.OrdinalIgnoreCase))
                return growth.addAgility;
            if (string.Equals(statId, "Intelligence", StringComparison.OrdinalIgnoreCase))
                return growth.addIntelligence;
            if (string.Equals(statId, "Luck", StringComparison.OrdinalIgnoreCase))
                return growth.addLuck;
            if (string.Equals(statId, "Endurance", StringComparison.OrdinalIgnoreCase))
                return growth.addEndurance;

            return 0;
        }

        private static int RoundFormulaValue(float value, string rounding)
        {
            if (string.Equals(rounding, "Floor", StringComparison.OrdinalIgnoreCase))
                return (int)Math.Floor(value);

            if (string.Equals(rounding, "Ceil", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rounding, "Ceiling", StringComparison.OrdinalIgnoreCase))
            {
                return (int)Math.Ceiling(value);
            }

            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }
    }
}
