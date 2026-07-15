using System;
using GuildIdle.Configs;

namespace GuildIdle.Player
{
    public interface IPlayerBootstrapConfigProvider
    {
        BuildingConfigDto[] Buildings { get; }
        bool TryGetActivity(string activityId, out ActivityConfigDto activity);
        ActivityRewardConfigDto[] GetRewards(string activityId);
    }

    public sealed class RepositoryHeroStatsConfigAdapter : IHeroStatsConfigProvider
    {
        private readonly HeroesConfigRepository _heroes;
        private readonly FormulasConfigRepository _formulas;
        private readonly ActivitiesConfigRepository _activities;

        public RepositoryHeroStatsConfigAdapter(
            HeroesConfigRepository heroes,
            FormulasConfigRepository formulas,
            ActivitiesConfigRepository activities)
        {
            _heroes = heroes ?? throw new ArgumentNullException(nameof(heroes));
            _formulas = formulas ?? throw new ArgumentNullException(nameof(formulas));
            _activities = activities ?? throw new ArgumentNullException(nameof(activities));
        }

        public HeroGrowthConfigDto[] HeroGrowth => _heroes.HeroGrowth;
        public SkillProgressionConfigDto[] SkillProgression => _activities.SkillsProgression;
        public bool TryGetHero(string heroId, out HeroConfigDto hero) => _heroes.TryGet(heroId, out hero);
        public bool TryGetFormula(string formulaId, out FormulaConfigDto formula) =>
            _formulas.TryGetFormula(formulaId, out formula);
    }

    public sealed class RepositoryPlayerBootstrapConfigAdapter : IPlayerBootstrapConfigProvider
    {
        private readonly ActivitiesConfigRepository _activities;
        private readonly BuildingsConfigRepository _buildings;

        public RepositoryPlayerBootstrapConfigAdapter(
            ActivitiesConfigRepository activities,
            BuildingsConfigRepository buildings)
        {
            _activities = activities ?? throw new ArgumentNullException(nameof(activities));
            _buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
        }

        public BuildingConfigDto[] Buildings => _buildings.Buildings;
        public bool TryGetActivity(string activityId, out ActivityConfigDto activity) =>
            _activities.TryGet(activityId, out activity);
        public ActivityRewardConfigDto[] GetRewards(string activityId) => _activities.GetRewards(activityId);
    }
}
