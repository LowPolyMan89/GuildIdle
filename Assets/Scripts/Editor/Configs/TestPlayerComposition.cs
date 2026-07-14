using GuildIdle.Configs;
using GuildIdle.Player;

namespace GuildIdle.Editor.Configs
{
    public static class TestPlayerComposition
    {
        public const string StarterActivityId = "starter_hero_available";

        public static HeroStatsService CreateHeroStats(ConfigDatabase database)
        {
            return new HeroStatsService(new RepositoryHeroStatsConfigAdapter(
                database.Heroes,
                database.Formulas,
                database.Activities));
        }

        public static PlayerStateFactory CreatePlayerStateFactory(ConfigDatabase database)
        {
            var heroStats = CreateHeroStats(database);
            var bootstrapConfigs = new RepositoryPlayerBootstrapConfigAdapter(
                database.Activities,
                database.Buildings);
            return new PlayerStateFactory(bootstrapConfigs, heroStats, StarterActivityId);
        }
    }
}
