using System;
using GuildIdle.Configs;

namespace GuildIdle.Player
{
    public sealed class PlayerBootstrapDefinition
    {
        public PlayerBootstrapDefinition(string initialStageId)
        {
            InitialStageId = string.IsNullOrWhiteSpace(initialStageId)
                ? throw new ArgumentException("Initial stage id is required.", nameof(initialStageId))
                : initialStageId;
        }

        public string InitialStageId { get; }
    }

    public interface IPlayerBootstrapConfigProvider
    {
        BuildingConfigDto[] Buildings { get; }
        SkillConfigDto[] Skills { get; }
        QuestConfigDto[] Quests { get; }
        HeroSkillEffectConfigDto[] HeroSkillEffects { get; }
        bool TryGetHero(string heroId, out HeroConfigDto hero);
        bool TryGetItem(string itemId, out IItemConfig item);
        bool TryGetEquipmentSlot(string itemId, out string equipmentSlot);
        bool TryGetSettlementStage(string stageId, out SettlementStageConfigDto stage);
        SettlementStageStarterHeroConfigDto[] GetSettlementStageStarterHeroes(string stageId);
        SettlementStageStarterEquipmentConfigDto[] GetSettlementStageStarterEquipment(string stageId);
        bool TryGetQuest(string questId, out QuestConfigDto quest);
        QuestStartConditionConfigDto[] GetQuestStartConditions(string questId);
        QuestStepConfigDto[] GetQuestSteps(string questId);
        bool IsKnownItemState(string stateId);
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
        private readonly ItemsConfigRepository _items;
        private readonly HeroesConfigRepository _heroes;
        private readonly ActivitiesConfigRepository _activities;
        private readonly BuildingsConfigRepository _buildings;
        private readonly StorageConfigRepository _storage;

        public RepositoryPlayerBootstrapConfigAdapter(
            ItemsConfigRepository items,
            HeroesConfigRepository heroes,
            ActivitiesConfigRepository activities,
            BuildingsConfigRepository buildings,
            StorageConfigRepository storage)
        {
            _items = items ?? throw new ArgumentNullException(nameof(items));
            _heroes = heroes ?? throw new ArgumentNullException(nameof(heroes));
            _activities = activities ?? throw new ArgumentNullException(nameof(activities));
            _buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public BuildingConfigDto[] Buildings => _buildings.Buildings;
        public SkillConfigDto[] Skills => _activities.Skills;
        public QuestConfigDto[] Quests => _activities.Quests;
        public HeroSkillEffectConfigDto[] HeroSkillEffects => _heroes.HeroSkillEffects;
        public bool TryGetHero(string heroId, out HeroConfigDto hero) => _heroes.TryGet(heroId, out hero);
        public bool TryGetItem(string itemId, out IItemConfig item) => _items.TryGet(itemId, out item);
        public bool TryGetSettlementStage(string stageId, out SettlementStageConfigDto stage) =>
            _buildings.TryGetSettlementStage(stageId, out stage);
        public SettlementStageStarterHeroConfigDto[] GetSettlementStageStarterHeroes(string stageId) =>
            _buildings.GetSettlementStageStarterHeroes(stageId);
        public SettlementStageStarterEquipmentConfigDto[] GetSettlementStageStarterEquipment(string stageId) =>
            _buildings.GetSettlementStageStarterEquipment(stageId);
        public bool TryGetQuest(string questId, out QuestConfigDto quest) => _activities.TryGetQuest(questId, out quest);
        public QuestStartConditionConfigDto[] GetQuestStartConditions(string questId) =>
            _activities.GetQuestStartConditions(questId);
        public QuestStepConfigDto[] GetQuestSteps(string questId) => _activities.GetQuestSteps(questId);
        public bool IsKnownItemState(string stateId) => _storage.TryGetItemState(stateId, out _);

        public bool TryGetEquipmentSlot(string itemId, out string equipmentSlot)
        {
            equipmentSlot = null;
            if (_items.TryGetEquipmentWeapon(itemId, out var weapon))
            {
                equipmentSlot = weapon.equipmentSlot;
                return true;
            }

            if (_items.TryGetEquipmentArmor(itemId, out var armor))
            {
                equipmentSlot = armor.equipmentSlot;
                return true;
            }

            return false;
        }
    }
}
