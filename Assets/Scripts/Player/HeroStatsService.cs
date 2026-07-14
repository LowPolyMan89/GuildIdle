using System;
using GuildIdle.Configs;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Player
{
    public static class HeroStatsService
    {
        public static int ResolveSkillLevel(long exp)
        {
            var level = 1;
            foreach (var row in RuntimeConfigs.Activities.SkillsProgression)
            {
                if (row != null && exp >= row.totalExpRequired)
                    level = Math.Max(level, row.level);
            }

            return level;
        }

        public static int CalculateMaxFatigue(HeroConfigDto hero)
        {
            if (hero?.baseStats == null)
                return 0;

            foreach (var formula in RuntimeConfigs.Formulas.HeroDerivedStats)
            {
                if (formula == null || !string.Equals(formula.derivedStatId, "max_fatigue", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var baseValue = formula.baseValue;
                var statValue = GetBaseStatValue(hero.baseStats, formula.primaryStat);
                var levelValue = 1 * formula.levelMultiplier;
                var raw = baseValue + (statValue * formula.primaryStatMultiplier) + levelValue;

                return formula.rounding switch
                {
                    "Round" => (int)System.Math.Round(raw),
                    "Floor" => (int)System.Math.Floor(raw),
                    "Ceil" => (int)System.Math.Ceiling(raw),
                    _ => (int)raw
                };
            }

            return 0;
        }

        private static int GetBaseStatValue(HeroBaseStatsDto stats, string statName)
        {
            if (string.IsNullOrWhiteSpace(statName))
                return 0;

            return statName.ToUpperInvariant() switch
            {
                "STRENGTH" => stats.strength,
                "AGILITY" => stats.agility,
                "INTELLIGENCE" => stats.intelligence,
                "LUCK" => stats.luck,
                "ENDURANCE" => stats.endurance,
                _ => 0
            };
        }
    }
}