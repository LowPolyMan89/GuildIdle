namespace GuildIdle.Configs
{
    public sealed class ConfigDatabase
    {
        public ItemsConfigRepository Items { get; }
        public HeroesConfigRepository Heroes { get; }
        public ActivitiesConfigRepository Activities { get; }
        public BuildingsConfigRepository Buildings { get; }
        public EnemiesConfigRepository Enemies { get; }
        public FormulasConfigRepository Formulas { get; }
        public LootConfigRepository Loot { get; }
        public MapConfigRepository Map { get; }
        public StorageConfigRepository Storage { get; }

        public ConfigDatabase(
            ItemsRuntimeConfigDto items,
            HeroesRuntimeConfigDto heroes,
            ActivitiesRuntimeConfigDto activities,
            BuildingsRuntimeConfigDto buildings,
            EnemiesRuntimeConfigDto enemies,
            FormulaRuntimeConfigDto formulas,
            LootRuntimeConfigDto loot,
            MapRuntimeConfigDto map,
            StorageRuntimeConfigDto storage)
        {
            Items = new ItemsConfigRepository(items);
            Heroes = new HeroesConfigRepository(heroes);
            Activities = new ActivitiesConfigRepository(activities);
            Buildings = new BuildingsConfigRepository(buildings);
            Enemies = new EnemiesConfigRepository(enemies);
            Formulas = new FormulasConfigRepository(formulas);
            Loot = new LootConfigRepository(loot);
            Map = new MapConfigRepository(map);
            Storage = new StorageConfigRepository(storage);
        }
    }
}
