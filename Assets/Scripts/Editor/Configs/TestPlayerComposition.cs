using GuildIdle.Configs;
using GuildIdle.Player;

namespace GuildIdle.Editor.Configs
{
    public static class TestPlayerComposition
    {
        public static readonly PlayerBootstrapDefinition BootstrapDefinition = new PlayerBootstrapDefinition(
            "stage_arrival",
            new[] { "ren" },
            new[] { new StarterEquipmentDefinition("ren", "item_wooden_club", "weapon") });

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
                database.Items,
                database.Heroes,
                database.Activities,
                database.Buildings,
                database.Storage);
            return new PlayerStateFactory(bootstrapConfigs, heroStats, BootstrapDefinition);
        }
    }
}
