using GuildIdle.Combat;

namespace GuildIdle.Configs
{
    public sealed class ConfigDatabase
    {
        public ItemsConfigRepository Items { get; }
        public CombatConsumableDescriptorRepository CombatConsumables { get; }
        public HeroesConfigRepository Heroes { get; }
        public ActivitiesConfigRepository Activities { get; }
        public BuildingsConfigRepository Buildings { get; }
        public CraftsConfigRepository Crafts { get; }
        public QuestConfigRepository Quests { get; }
        public EnemiesConfigRepository Enemies { get; }
        public FormulasConfigRepository Formulas { get; }
        public LootConfigRepository Loot { get; }
        public MapConfigRepository Map { get; }
        public StorageConfigRepository Storage { get; }
        public LocalisationConfigRepository Localisation { get; }

        public ConfigDatabase(
            ItemsRuntimeConfigDto items,
            HeroesRuntimeConfigDto heroes,
            ActivitiesRuntimeConfigDto activities,
            BuildingsRuntimeConfigDto buildings,
            QuestRuntimeConfigDto quests,
            EnemiesRuntimeConfigDto enemies,
            FormulaRuntimeConfigDto formulas,
            LootRuntimeConfigDto loot,
            MapRuntimeConfigDto map,
            StorageRuntimeConfigDto storage,
            LocalisationRuntimeConfigDto localisation)
        {
            Items = new ItemsConfigRepository(items);
            Storage = new StorageConfigRepository(storage);
            CombatConsumables = new CombatConsumableDescriptorRepository(Items, Storage);
            Heroes = new HeroesConfigRepository(heroes);
            Activities = new ActivitiesConfigRepository(activities);
            Buildings = new BuildingsConfigRepository(buildings);
            Crafts = new CraftsConfigRepository(Items, Buildings);
            Quests = new QuestConfigRepository(quests);
            Enemies = new EnemiesConfigRepository(enemies);
            Formulas = new FormulasConfigRepository(formulas);
            Loot = new LootConfigRepository(loot);
            Map = new MapConfigRepository(map);
            Localisation = new LocalisationConfigRepository(localisation);
        }
    }
}
