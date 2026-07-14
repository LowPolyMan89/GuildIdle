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

        // TODO: Вынести CalculateHeroMaxFatigue из PlayerState в этот сервис
        // с сохранением точной логики (GetHeroStat, GetBaseStat, GetGrowthStat,
        // RoundFormulaValue, проверка formula.enabled).
    }
}