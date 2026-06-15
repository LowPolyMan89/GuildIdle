using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildIdle
{
    [Serializable]
    public sealed class StatConfig
    {
        public string Id;
        public string Category;
        public string LocalisationNameId;
        public string LocalisationDescriptionId;
        public string IconId;
    }

    [Serializable]
    public sealed class ResourceConfig
    {
        public string Id;
        public string DisplayName;
        public string Icon;
        public int MaxAmount;
        public bool IsPremium;
    }

    [Serializable]
    public sealed class HeroConfig
    {
        public string Id;
        public string DisplayName;
        public string ClassName;
        public string Portrait;
        public string Description;
        public string GrowthTableId;
        public int StartLevel = 1;
        public SkillStartConfig[] StartSkills;
        public string PassiveAbilityId;
        public string AutoAbilityId;
        public EquipmentEntryConfig[] DefaultEquipment;
    }

    [Serializable]
    public sealed class SkillStartConfig
    {
        public string SkillId;
        public int Level = 1;
        public int Experience;
    }

    [Serializable]
    public sealed class HeroGrowthConfig
    {
        public string Id;
        public HeroGrowthLevelConfig[] Levels;
    }

    [Serializable]
    public sealed class HeroGrowthLevelConfig
    {
        public int Level;
        public int ExperienceToNextLevel;
        public float Strength;
        public float Agility;
        public float Intelligence;
        public float Endurance;
        public float Luck;
    }

    [Serializable]
    public sealed class SkillConfig
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string Icon;
        public int MaxLevel;
    }

    [Serializable]
    public sealed class SkillLevelConfig
    {
        public string Id;
        public string SkillId;
        public SkillLevelRowConfig[] Levels;
    }

    [Serializable]
    public sealed class SkillLevelRowConfig
    {
        public int Level;
        public int ExperienceToNextLevel;
        public float BonusValue;
    }

    [Serializable]
    public sealed class TaskConfig
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string Icon;
        public string TaskType;
        public string[] RequiredHeroTags;
        public float CycleDurationSeconds;
        public float FatiguePerCycle;
        public int HeroExpPerCycle;
        public int SkillExpPerCycle;
        public string TargetSkillId;
        public RewardConfig[] Rewards;
        public CostConfig[] RequiredResources;
        public CostConfig[] RequiredItems;
        public RequirementConfig[] UnlockRequirements;
    }

    [Serializable]
    public sealed class ItemConfig
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string Icon;
        public string ItemType;
        public string Rarity;
        public string SlotType;
        public bool Stackable;
        public int MaxStack = 1;
        public StatBonusConfig[] StatsBonus;
        public StatBonusConfig[] WorkBonus;
        public StatBonusConfig[] CombatBonus;
        public string SpecialEffectId;
    }

    [Serializable]
    public sealed class CraftRecipeConfig
    {
        public string Id;
        public string DisplayName;
        public string ResultItemId;
        public int ResultAmount = 1;
        public CostConfig[] RequiredResources;
        public CostConfig[] RequiredItems;
        public float CraftDurationSeconds;
        public string RequiredSkillId;
        public int RequiredSkillLevel;
        public int HeroExpReward;
        public int SkillExpReward;
        public float FatigueCost;
    }

    [Serializable]
    public sealed class EnemyConfig
    {
        public string Id;
        public string DisplayName;
        public string Portrait;
        public string EnemyType;
        public float MaxHealth;
        public float Damage;
        public float AttackInterval;
        public float Defense;
        public float CritChance;
        public RewardConfig[] Rewards;
        public int HeroExpReward;
        public int CombatSkillExpReward;
    }

    [Serializable]
    public sealed class CombatLocationConfig
    {
        public string Id;
        public string DisplayName;
        public string Background;
        public float DurationSeconds;
        public float EntryFatigueCost;
        public float FatiguePerEnemyKill;
        public float FatigueOnDefeat;
        public ConfigReferenceWeight[] EnemyPool;
        public string BossEnemyId;
        public RequirementConfig[] UnlockRequirements;
        public RewardConfig[] PossibleRewards;
    }

    [Serializable]
    public sealed class BuildingConfig
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string Icon;
        public int MaxLevel;
        public BuildingLevelConfig[] Levels;
    }

    [Serializable]
    public sealed class QuestConfig
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public RequirementConfig[] Requirements;
        public RewardConfig[] Rewards;
        public int HeroExpReward;
        public int GuildExpReward;
        public int RefreshWeight;
    }

    [Serializable]
    public sealed class RewardConfig
    {
        public string Type;
        public string Id;
        public int Amount;
    }

    [Serializable]
    public sealed class CostConfig
    {
        public string Id;
        public int Amount;
    }

    [Serializable]
    public sealed class RequirementConfig
    {
        public string Type;
        public string Id;
        public int Amount;
        public int Level;
    }

    [Serializable]
    public sealed class StatBonusConfig
    {
        public string StatId;
        public string ModifierType;
        public float Value;
    }

    [Serializable]
    public sealed class EquipmentEntryConfig
    {
        public string SlotType;
        public string ItemId;
    }

    [Serializable]
    public sealed class BuildingLevelConfig
    {
        public int Level;
        public CostConfig[] UpgradeCost;
        public float UpgradeDurationSeconds;
        public StatBonusConfig[] Effects;
    }

    [Serializable]
    public sealed class ConfigReferenceWeight
    {
        public string Id;
        public int Weight = 1;
    }

    public sealed class ConfigValidationReport
    {
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _warnings = new List<string>();

        public IReadOnlyList<string> Errors => _errors;
        public IReadOnlyList<string> Warnings => _warnings;
        public bool IsValid => _errors.Count == 0;

        public void AddError(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                _errors.Add(message);
        }

        public void AddWarning(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                _warnings.Add(message);
        }

        public void AddErrors(IEnumerable<string> messages)
        {
            foreach (var message in messages)
                AddError(message);
        }
    }

    public static class ConfigDatabase
    {
        private const string StatsResourcePath = "Stats";
        private const string ResourcesResourcePath = "GameResources";
        private const string HeroesResourcePath = "Heroes";
        private const string HeroGrowthResourcePath = "HeroGrowth";
        private const string SkillsResourcePath = "Skills";
        private const string SkillLevelsResourcePath = "SkillLevels";
        private const string TasksResourcePath = "Tasks";
        private const string ItemsResourcePath = "Items";
        private const string CraftRecipesResourcePath = "CraftRecipes";
        private const string EnemiesResourcePath = "Enemies";
        private const string CombatLocationsResourcePath = "CombatLocations";
        private const string BuildingsResourcePath = "Buildings";
        private const string QuestsResourcePath = "Quests";

        private static readonly Dictionary<string, StatConfig> _stats = new Dictionary<string, StatConfig>();
        private static readonly Dictionary<string, ResourceConfig> _resources = new Dictionary<string, ResourceConfig>();
        private static readonly Dictionary<string, HeroConfig> _heroes = new Dictionary<string, HeroConfig>();
        private static readonly Dictionary<string, HeroGrowthConfig> _heroGrowth = new Dictionary<string, HeroGrowthConfig>();
        private static readonly Dictionary<string, SkillConfig> _skills = new Dictionary<string, SkillConfig>();
        private static readonly Dictionary<string, SkillLevelConfig> _skillLevels = new Dictionary<string, SkillLevelConfig>();
        private static readonly Dictionary<string, TaskConfig> _tasks = new Dictionary<string, TaskConfig>();
        private static readonly Dictionary<string, ItemConfig> _items = new Dictionary<string, ItemConfig>();
        private static readonly Dictionary<string, CraftRecipeConfig> _craftRecipes = new Dictionary<string, CraftRecipeConfig>();
        private static readonly Dictionary<string, EnemyConfig> _enemies = new Dictionary<string, EnemyConfig>();
        private static readonly Dictionary<string, CombatLocationConfig> _combatLocations = new Dictionary<string, CombatLocationConfig>();
        private static readonly Dictionary<string, BuildingConfig> _buildings = new Dictionary<string, BuildingConfig>();
        private static readonly Dictionary<string, QuestConfig> _quests = new Dictionary<string, QuestConfig>();
        private static readonly List<string> _loadErrors = new List<string>();

        public static IReadOnlyDictionary<string, StatConfig> Stats => _stats;
        public static IReadOnlyDictionary<string, ResourceConfig> Resources => _resources;
        public static IReadOnlyDictionary<string, HeroConfig> Heroes => _heroes;
        public static IReadOnlyDictionary<string, HeroGrowthConfig> HeroGrowth => _heroGrowth;
        public static IReadOnlyDictionary<string, SkillConfig> Skills => _skills;
        public static IReadOnlyDictionary<string, SkillLevelConfig> SkillLevels => _skillLevels;
        public static IReadOnlyDictionary<string, TaskConfig> Tasks => _tasks;
        public static IReadOnlyDictionary<string, ItemConfig> Items => _items;
        public static IReadOnlyDictionary<string, CraftRecipeConfig> CraftRecipes => _craftRecipes;
        public static IReadOnlyDictionary<string, EnemyConfig> Enemies => _enemies;
        public static IReadOnlyDictionary<string, CombatLocationConfig> CombatLocations => _combatLocations;
        public static IReadOnlyDictionary<string, BuildingConfig> Buildings => _buildings;
        public static IReadOnlyDictionary<string, QuestConfig> Quests => _quests;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RuntimeInitialize()
        {
            LoadAll();
        }

        public static void Reload()
        {
            LoadAll();
        }

        public static ConfigValidationReport Validate()
        {
            EnsureLoaded();

            var report = new ConfigValidationReport();
            report.AddErrors(_loadErrors);

            ValidateStats(report);
            ValidateResources(report);
            ValidateHeroes(report);
            ValidateHeroGrowth(report);
            ValidateSkills(report);
            ValidateSkillLevels(report);
            ValidateTasks(report);
            ValidateItems(report);
            ValidateCraftRecipes(report);
            ValidateEnemies(report);
            ValidateCombatLocations(report);
            ValidateBuildings(report);
            ValidateQuests(report);

            return report;
        }

        public static bool TryGetStat(string id, out StatConfig config) => TryGet(_stats, id, out config);
        public static bool TryGetResource(string id, out ResourceConfig config) => TryGet(_resources, id, out config);
        public static bool TryGetHero(string id, out HeroConfig config) => TryGet(_heroes, id, out config);
        public static bool TryGetHeroGrowth(string id, out HeroGrowthConfig config) => TryGet(_heroGrowth, id, out config);
        public static bool TryGetSkill(string id, out SkillConfig config) => TryGet(_skills, id, out config);
        public static bool TryGetSkillLevel(string id, out SkillLevelConfig config) => TryGet(_skillLevels, id, out config);
        public static bool TryGetTask(string id, out TaskConfig config) => TryGet(_tasks, id, out config);
        public static bool TryGetItem(string id, out ItemConfig config) => TryGet(_items, id, out config);
        public static bool TryGetCraftRecipe(string id, out CraftRecipeConfig config) => TryGet(_craftRecipes, id, out config);
        public static bool TryGetEnemy(string id, out EnemyConfig config) => TryGet(_enemies, id, out config);
        public static bool TryGetCombatLocation(string id, out CombatLocationConfig config) => TryGet(_combatLocations, id, out config);
        public static bool TryGetBuilding(string id, out BuildingConfig config) => TryGet(_buildings, id, out config);
        public static bool TryGetQuest(string id, out QuestConfig config) => TryGet(_quests, id, out config);

        public static bool HasStat(string id) => Has(_stats, id);
        public static bool HasResource(string id) => Has(_resources, id);
        public static bool HasHero(string id) => Has(_heroes, id);
        public static bool HasHeroGrowth(string id) => Has(_heroGrowth, id);
        public static bool HasSkill(string id) => Has(_skills, id);
        public static bool HasSkillLevel(string id) => Has(_skillLevels, id);
        public static bool HasTask(string id) => Has(_tasks, id);
        public static bool HasItem(string id) => Has(_items, id);
        public static bool HasCraftRecipe(string id) => Has(_craftRecipes, id);
        public static bool HasEnemy(string id) => Has(_enemies, id);
        public static bool HasCombatLocation(string id) => Has(_combatLocations, id);
        public static bool HasBuilding(string id) => Has(_buildings, id);
        public static bool HasQuest(string id) => Has(_quests, id);

        public static StatConfig GetStat(string id) => Get(_stats, id, "stat");
        public static ResourceConfig GetResource(string id) => Get(_resources, id, "resource");
        public static HeroConfig GetHero(string id) => Get(_heroes, id, "hero");
        public static HeroGrowthConfig GetHeroGrowth(string id) => Get(_heroGrowth, id, "hero growth");
        public static SkillConfig GetSkill(string id) => Get(_skills, id, "skill");
        public static SkillLevelConfig GetSkillLevel(string id) => Get(_skillLevels, id, "skill level");
        public static TaskConfig GetTask(string id) => Get(_tasks, id, "task");
        public static ItemConfig GetItem(string id) => Get(_items, id, "item");
        public static CraftRecipeConfig GetCraftRecipe(string id) => Get(_craftRecipes, id, "craft recipe");
        public static EnemyConfig GetEnemy(string id) => Get(_enemies, id, "enemy");
        public static CombatLocationConfig GetCombatLocation(string id) => Get(_combatLocations, id, "combat location");
        public static BuildingConfig GetBuilding(string id) => Get(_buildings, id, "building");
        public static QuestConfig GetQuest(string id) => Get(_quests, id, "quest");

        private static void LoadAll()
        {
            ClearAll();

            LoadConfigs(StatsResourcePath, _stats, config => config.Id, "stat");
            LoadConfigs(ResourcesResourcePath, _resources, config => config.Id, "resource");
            LoadConfigs(HeroesResourcePath, _heroes, config => config.Id, "hero");
            LoadConfigs(HeroGrowthResourcePath, _heroGrowth, config => config.Id, "hero growth");
            LoadConfigs(SkillsResourcePath, _skills, config => config.Id, "skill");
            LoadConfigs(SkillLevelsResourcePath, _skillLevels, config => config.Id, "skill level");
            LoadConfigs(TasksResourcePath, _tasks, config => config.Id, "task");
            LoadConfigs(ItemsResourcePath, _items, config => config.Id, "item");
            LoadConfigs(CraftRecipesResourcePath, _craftRecipes, config => config.Id, "craft recipe");
            LoadConfigs(EnemiesResourcePath, _enemies, config => config.Id, "enemy");
            LoadConfigs(CombatLocationsResourcePath, _combatLocations, config => config.Id, "combat location");
            LoadConfigs(BuildingsResourcePath, _buildings, config => config.Id, "building");
            LoadConfigs(QuestsResourcePath, _quests, config => config.Id, "quest");
        }

        private static void ClearAll()
        {
            _loadErrors.Clear();
            _stats.Clear();
            _resources.Clear();
            _heroes.Clear();
            _heroGrowth.Clear();
            _skills.Clear();
            _skillLevels.Clear();
            _tasks.Clear();
            _items.Clear();
            _craftRecipes.Clear();
            _enemies.Clear();
            _combatLocations.Clear();
            _buildings.Clear();
            _quests.Clear();
        }

        private static void EnsureLoaded()
        {
            if (_stats.Count == 0 &&
                _resources.Count == 0 &&
                _heroes.Count == 0 &&
                _skills.Count == 0 &&
                _tasks.Count == 0)
            {
                LoadAll();
            }
        }

        private static void LoadConfigs<TConfig>(
            string resourcePath,
            Dictionary<string, TConfig> target,
            Func<TConfig, string> getId,
            string configType)
        {
            var assets = UnityEngine.Resources.LoadAll<TextAsset>(resourcePath);

            foreach (var asset in assets)
            {
                TConfig config;
                try
                {
                    config = JsonUtility.FromJson<TConfig>(asset.text);
                }
                catch (Exception exception)
                {
                    AddLoadError($"Failed to parse {configType} config '{asset.name}': {exception.Message}");
                    continue;
                }

                if (config == null)
                {
                    AddLoadError($"{configType} config '{asset.name}' could not be read.");
                    continue;
                }

                var id = getId(config);
                if (string.IsNullOrWhiteSpace(id))
                {
                    AddLoadError($"{configType} config '{asset.name}' has empty Id.");
                    continue;
                }

                if (target.ContainsKey(id))
                {
                    AddLoadError($"Duplicate {configType} config id '{id}' in '{asset.name}'.");
                    continue;
                }

                target.Add(id, config);
            }
        }

        private static bool TryGet<TConfig>(Dictionary<string, TConfig> source, string id, out TConfig config)
        {
            EnsureLoaded();
            return source.TryGetValue(id, out config);
        }

        private static bool Has<TConfig>(Dictionary<string, TConfig> source, string id)
        {
            EnsureLoaded();
            return source.ContainsKey(id);
        }

        private static TConfig Get<TConfig>(Dictionary<string, TConfig> source, string id, string configType)
        {
            if (TryGet(source, id, out var config))
                return config;

            throw new KeyNotFoundException($"{configType} config with id '{id}' was not found.");
        }

        private static void AddLoadError(string message)
        {
            _loadErrors.Add(message);
            Debug.LogError(message);
        }

        private static void ValidateStats(ConfigValidationReport report)
        {
            foreach (var config in _stats.Values)
            {
                RequireText(report, "stat", config.Id, config.Category, nameof(config.Category));
                RequireText(report, "stat", config.Id, config.LocalisationNameId, nameof(config.LocalisationNameId));
                RequireText(report, "stat", config.Id, config.LocalisationDescriptionId, nameof(config.LocalisationDescriptionId));
            }
        }

        private static void ValidateResources(ConfigValidationReport report)
        {
            foreach (var config in _resources.Values)
            {
                RequireText(report, "resource", config.Id, config.DisplayName, nameof(config.DisplayName));
                RequireNonNegative(report, "resource", config.Id, config.MaxAmount, nameof(config.MaxAmount));
            }
        }

        private static void ValidateHeroes(ConfigValidationReport report)
        {
            foreach (var config in _heroes.Values)
            {
                RequireText(report, "hero", config.Id, config.DisplayName, nameof(config.DisplayName));
                RequireText(report, "hero", config.Id, config.GrowthTableId, nameof(config.GrowthTableId));
                RequirePositive(report, "hero", config.Id, config.StartLevel, nameof(config.StartLevel));
                RequireReference(report, "hero", config.Id, nameof(config.GrowthTableId), config.GrowthTableId, _heroGrowth, "hero growth");

                if (config.StartSkills != null)
                {
                    foreach (var skill in config.StartSkills)
                    {
                        if (skill == null)
                            continue;

                        RequireReference(report, "hero", config.Id, nameof(skill.SkillId), skill.SkillId, _skills, "skill");
                        RequirePositive(report, "hero", config.Id, skill.Level, $"{nameof(config.StartSkills)}.{nameof(skill.Level)}");
                        RequireNonNegative(report, "hero", config.Id, skill.Experience, $"{nameof(config.StartSkills)}.{nameof(skill.Experience)}");
                    }
                }

                ValidateEquipment(report, "hero", config.Id, config.DefaultEquipment);
            }
        }

        private static void ValidateHeroGrowth(ConfigValidationReport report)
        {
            foreach (var config in _heroGrowth.Values)
            {
                if (config.Levels == null || config.Levels.Length == 0)
                {
                    report.AddError($"hero growth config '{config.Id}' has no Levels.");
                    continue;
                }

                ValidateContinuousLevels(report, "hero growth", config.Id, config.Levels, row => row.Level);

                foreach (var row in config.Levels)
                {
                    RequireNonNegative(report, "hero growth", config.Id, row.ExperienceToNextLevel, nameof(row.ExperienceToNextLevel));
                    RequireNonNegative(report, "hero growth", config.Id, row.Strength, nameof(row.Strength));
                    RequireNonNegative(report, "hero growth", config.Id, row.Agility, nameof(row.Agility));
                    RequireNonNegative(report, "hero growth", config.Id, row.Intelligence, nameof(row.Intelligence));
                    RequireNonNegative(report, "hero growth", config.Id, row.Endurance, nameof(row.Endurance));
                    RequireNonNegative(report, "hero growth", config.Id, row.Luck, nameof(row.Luck));
                }
            }
        }

        private static void ValidateSkills(ConfigValidationReport report)
        {
            foreach (var config in _skills.Values)
            {
                RequireText(report, "skill", config.Id, config.DisplayName, nameof(config.DisplayName));
                RequirePositive(report, "skill", config.Id, config.MaxLevel, nameof(config.MaxLevel));
            }
        }

        private static void ValidateSkillLevels(ConfigValidationReport report)
        {
            foreach (var config in _skillLevels.Values)
            {
                RequireReference(report, "skill level", config.Id, nameof(config.SkillId), config.SkillId, _skills, "skill");

                if (config.Levels == null || config.Levels.Length == 0)
                {
                    report.AddError($"skill level config '{config.Id}' has no Levels.");
                    continue;
                }

                ValidateContinuousLevels(report, "skill level", config.Id, config.Levels, row => row.Level);

                foreach (var row in config.Levels)
                {
                    RequireNonNegative(report, "skill level", config.Id, row.ExperienceToNextLevel, nameof(row.ExperienceToNextLevel));
                    RequireNonNegative(report, "skill level", config.Id, row.BonusValue, nameof(row.BonusValue));
                }
            }
        }

        private static void ValidateTasks(ConfigValidationReport report)
        {
            foreach (var config in _tasks.Values)
            {
                RequireText(report, "task", config.Id, config.DisplayName, nameof(config.DisplayName));
                RequireText(report, "task", config.Id, config.TaskType, nameof(config.TaskType));
                RequirePositive(report, "task", config.Id, config.CycleDurationSeconds, nameof(config.CycleDurationSeconds));
                RequireNonNegative(report, "task", config.Id, config.FatiguePerCycle, nameof(config.FatiguePerCycle));
                RequireNonNegative(report, "task", config.Id, config.HeroExpPerCycle, nameof(config.HeroExpPerCycle));
                RequireNonNegative(report, "task", config.Id, config.SkillExpPerCycle, nameof(config.SkillExpPerCycle));
                RequireReference(report, "task", config.Id, nameof(config.TargetSkillId), config.TargetSkillId, _skills, "skill");
                ValidateRewards(report, "task", config.Id, config.Rewards);
                ValidateCosts(report, "task", config.Id, nameof(config.RequiredResources), config.RequiredResources, _resources, "resource");
                ValidateCosts(report, "task", config.Id, nameof(config.RequiredItems), config.RequiredItems, _items, "item");
                ValidateRequirements(report, "task", config.Id, config.UnlockRequirements);
            }
        }

        private static void ValidateItems(ConfigValidationReport report)
        {
            foreach (var config in _items.Values)
            {
                RequireText(report, "item", config.Id, config.DisplayName, nameof(config.DisplayName));
                RequireText(report, "item", config.Id, config.ItemType, nameof(config.ItemType));
                RequirePositive(report, "item", config.Id, config.MaxStack, nameof(config.MaxStack));
                ValidateStatBonuses(report, "item", config.Id, config.StatsBonus);
                ValidateStatBonuses(report, "item", config.Id, config.WorkBonus);
                ValidateStatBonuses(report, "item", config.Id, config.CombatBonus);
            }
        }

        private static void ValidateCraftRecipes(ConfigValidationReport report)
        {
            foreach (var config in _craftRecipes.Values)
            {
                RequireText(report, "craft recipe", config.Id, config.DisplayName, nameof(config.DisplayName));
                RequireReference(report, "craft recipe", config.Id, nameof(config.ResultItemId), config.ResultItemId, _items, "item");
                RequirePositive(report, "craft recipe", config.Id, config.ResultAmount, nameof(config.ResultAmount));
                ValidateCosts(report, "craft recipe", config.Id, nameof(config.RequiredResources), config.RequiredResources, _resources, "resource");
                ValidateCosts(report, "craft recipe", config.Id, nameof(config.RequiredItems), config.RequiredItems, _items, "item");
                RequireNonNegative(report, "craft recipe", config.Id, config.CraftDurationSeconds, nameof(config.CraftDurationSeconds));
                RequireOptionalReference(report, "craft recipe", config.Id, nameof(config.RequiredSkillId), config.RequiredSkillId, _skills, "skill");
                RequireNonNegative(report, "craft recipe", config.Id, config.RequiredSkillLevel, nameof(config.RequiredSkillLevel));
                RequireNonNegative(report, "craft recipe", config.Id, config.HeroExpReward, nameof(config.HeroExpReward));
                RequireNonNegative(report, "craft recipe", config.Id, config.SkillExpReward, nameof(config.SkillExpReward));
                RequireNonNegative(report, "craft recipe", config.Id, config.FatigueCost, nameof(config.FatigueCost));
            }
        }

        private static void ValidateEnemies(ConfigValidationReport report)
        {
            foreach (var config in _enemies.Values)
            {
                RequireText(report, "enemy", config.Id, config.DisplayName, nameof(config.DisplayName));
                RequirePositive(report, "enemy", config.Id, config.MaxHealth, nameof(config.MaxHealth));
                RequireNonNegative(report, "enemy", config.Id, config.Damage, nameof(config.Damage));
                RequirePositive(report, "enemy", config.Id, config.AttackInterval, nameof(config.AttackInterval));
                RequireNonNegative(report, "enemy", config.Id, config.Defense, nameof(config.Defense));
                RequireNonNegative(report, "enemy", config.Id, config.CritChance, nameof(config.CritChance));
                ValidateRewards(report, "enemy", config.Id, config.Rewards);
                RequireNonNegative(report, "enemy", config.Id, config.HeroExpReward, nameof(config.HeroExpReward));
                RequireNonNegative(report, "enemy", config.Id, config.CombatSkillExpReward, nameof(config.CombatSkillExpReward));
            }
        }

        private static void ValidateCombatLocations(ConfigValidationReport report)
        {
            foreach (var config in _combatLocations.Values)
            {
                RequireText(report, "combat location", config.Id, config.DisplayName, nameof(config.DisplayName));
                RequirePositive(report, "combat location", config.Id, config.DurationSeconds, nameof(config.DurationSeconds));
                RequireNonNegative(report, "combat location", config.Id, config.EntryFatigueCost, nameof(config.EntryFatigueCost));
                RequireNonNegative(report, "combat location", config.Id, config.FatiguePerEnemyKill, nameof(config.FatiguePerEnemyKill));
                RequireNonNegative(report, "combat location", config.Id, config.FatigueOnDefeat, nameof(config.FatigueOnDefeat));
                ValidateWeightedReferences(report, "combat location", config.Id, nameof(config.EnemyPool), config.EnemyPool, _enemies, "enemy");
                RequireOptionalReference(report, "combat location", config.Id, nameof(config.BossEnemyId), config.BossEnemyId, _enemies, "enemy");
                ValidateRequirements(report, "combat location", config.Id, config.UnlockRequirements);
                ValidateRewards(report, "combat location", config.Id, config.PossibleRewards);
            }
        }

        private static void ValidateBuildings(ConfigValidationReport report)
        {
            foreach (var config in _buildings.Values)
            {
                RequireText(report, "building", config.Id, config.DisplayName, nameof(config.DisplayName));
                RequirePositive(report, "building", config.Id, config.MaxLevel, nameof(config.MaxLevel));

                if (config.Levels == null || config.Levels.Length == 0)
                {
                    report.AddError($"building config '{config.Id}' has no Levels.");
                    continue;
                }

                ValidateContinuousLevels(report, "building", config.Id, config.Levels, row => row.Level);

                if (config.Levels.Length != config.MaxLevel)
                    report.AddError($"building config '{config.Id}' must contain levels from 1 to MaxLevel ({config.MaxLevel}).");

                foreach (var row in config.Levels)
                {
                    ValidateCosts(report, "building", config.Id, nameof(row.UpgradeCost), row.UpgradeCost, _resources, "resource");
                    RequireNonNegative(report, "building", config.Id, row.UpgradeDurationSeconds, nameof(row.UpgradeDurationSeconds));
                    ValidateStatBonuses(report, "building", config.Id, row.Effects);
                }
            }
        }

        private static void ValidateQuests(ConfigValidationReport report)
        {
            foreach (var config in _quests.Values)
            {
                RequireText(report, "quest", config.Id, config.DisplayName, nameof(config.DisplayName));
                ValidateRequirements(report, "quest", config.Id, config.Requirements);
                ValidateRewards(report, "quest", config.Id, config.Rewards);
                RequireNonNegative(report, "quest", config.Id, config.HeroExpReward, nameof(config.HeroExpReward));
                RequireNonNegative(report, "quest", config.Id, config.GuildExpReward, nameof(config.GuildExpReward));
                RequirePositive(report, "quest", config.Id, config.RefreshWeight, nameof(config.RefreshWeight));
            }
        }

        private static void ValidateRewards(ConfigValidationReport report, string ownerType, string ownerId, RewardConfig[] rewards)
        {
            if (rewards == null)
                return;

            foreach (var reward in rewards)
            {
                if (reward == null)
                    continue;

                RequireText(report, ownerType, ownerId, reward.Type, $"{nameof(RewardConfig)}.{nameof(reward.Type)}");
                RequireText(report, ownerType, ownerId, reward.Id, $"{nameof(RewardConfig)}.{nameof(reward.Id)}");
                RequirePositive(report, ownerType, ownerId, reward.Amount, $"{nameof(RewardConfig)}.{nameof(reward.Amount)}");

                if (reward.Type == "resource")
                    RequireReference(report, ownerType, ownerId, nameof(reward.Id), reward.Id, _resources, "resource");
                else if (reward.Type == "item")
                    RequireReference(report, ownerType, ownerId, nameof(reward.Id), reward.Id, _items, "item");
                else if (!string.IsNullOrWhiteSpace(reward.Type))
                    report.AddError($"{ownerType} config '{ownerId}' has reward with unsupported Type '{reward.Type}'.");
            }
        }

        private static void ValidateCosts<TConfig>(
            ConfigValidationReport report,
            string ownerType,
            string ownerId,
            string fieldName,
            CostConfig[] costs,
            Dictionary<string, TConfig> lookup,
            string targetType)
        {
            if (costs == null)
                return;

            foreach (var cost in costs)
            {
                if (cost == null)
                    continue;

                RequireReference(report, ownerType, ownerId, fieldName, cost.Id, lookup, targetType);
                RequirePositive(report, ownerType, ownerId, cost.Amount, $"{fieldName}.{nameof(cost.Amount)}");
            }
        }

        private static void ValidateRequirements(ConfigValidationReport report, string ownerType, string ownerId, RequirementConfig[] requirements)
        {
            if (requirements == null)
                return;

            foreach (var requirement in requirements)
            {
                if (requirement == null)
                    continue;

                RequireText(report, ownerType, ownerId, requirement.Type, $"{nameof(RequirementConfig)}.{nameof(requirement.Type)}");
                RequireText(report, ownerType, ownerId, requirement.Id, $"{nameof(RequirementConfig)}.{nameof(requirement.Id)}");
                RequireNonNegative(report, ownerType, ownerId, requirement.Amount, $"{nameof(RequirementConfig)}.{nameof(requirement.Amount)}");
                RequireNonNegative(report, ownerType, ownerId, requirement.Level, $"{nameof(RequirementConfig)}.{nameof(requirement.Level)}");

                if (requirement.Type == "resource")
                    RequireReference(report, ownerType, ownerId, nameof(requirement.Id), requirement.Id, _resources, "resource");
                else if (requirement.Type == "item")
                    RequireReference(report, ownerType, ownerId, nameof(requirement.Id), requirement.Id, _items, "item");
                else if (requirement.Type == "skill")
                    RequireReference(report, ownerType, ownerId, nameof(requirement.Id), requirement.Id, _skills, "skill");
                else if (requirement.Type == "enemy")
                    RequireReference(report, ownerType, ownerId, nameof(requirement.Id), requirement.Id, _enemies, "enemy");
                else if (requirement.Type == "combatLocation")
                    RequireReference(report, ownerType, ownerId, nameof(requirement.Id), requirement.Id, _combatLocations, "combat location");
                else if (!string.IsNullOrWhiteSpace(requirement.Type))
                    report.AddError($"{ownerType} config '{ownerId}' has requirement with unsupported Type '{requirement.Type}'.");
            }
        }

        private static void ValidateStatBonuses(ConfigValidationReport report, string ownerType, string ownerId, StatBonusConfig[] bonuses)
        {
            if (bonuses == null)
                return;

            foreach (var bonus in bonuses)
            {
                if (bonus == null)
                    continue;

                RequireReference(report, ownerType, ownerId, nameof(bonus.StatId), bonus.StatId, _stats, "stat");
                RequireText(report, ownerType, ownerId, bonus.ModifierType, $"{nameof(StatBonusConfig)}.{nameof(bonus.ModifierType)}");
            }
        }

        private static void ValidateEquipment(ConfigValidationReport report, string ownerType, string ownerId, EquipmentEntryConfig[] equipment)
        {
            if (equipment == null)
                return;

            foreach (var entry in equipment)
            {
                if (entry == null)
                    continue;

                RequireText(report, ownerType, ownerId, entry.SlotType, $"{nameof(EquipmentEntryConfig)}.{nameof(entry.SlotType)}");
                RequireReference(report, ownerType, ownerId, nameof(entry.ItemId), entry.ItemId, _items, "item");
            }
        }

        private static void ValidateWeightedReferences<TConfig>(
            ConfigValidationReport report,
            string ownerType,
            string ownerId,
            string fieldName,
            ConfigReferenceWeight[] references,
            Dictionary<string, TConfig> lookup,
            string targetType)
        {
            if (references == null)
                return;

            foreach (var reference in references)
            {
                if (reference == null)
                    continue;

                RequireReference(report, ownerType, ownerId, fieldName, reference.Id, lookup, targetType);
                RequirePositive(report, ownerType, ownerId, reference.Weight, $"{fieldName}.{nameof(reference.Weight)}");
            }
        }

        private static void ValidateContinuousLevels<TRow>(
            ConfigValidationReport report,
            string configType,
            string configId,
            TRow[] rows,
            Func<TRow, int> getLevel)
        {
            var seen = new HashSet<int>();
            foreach (var row in rows)
            {
                var level = getLevel(row);
                if (!seen.Add(level))
                    report.AddError($"{configType} config '{configId}' contains duplicate level {level}.");
            }

            for (var level = 1; level <= rows.Length; level++)
            {
                if (!seen.Contains(level))
                    report.AddError($"{configType} config '{configId}' is missing level {level}.");
            }
        }

        private static void RequireText(ConfigValidationReport report, string configType, string configId, string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                report.AddError($"{configType} config '{configId}' has empty {fieldName}.");
        }

        private static void RequireReference<TConfig>(
            ConfigValidationReport report,
            string ownerType,
            string ownerId,
            string fieldName,
            string referenceId,
            Dictionary<string, TConfig> lookup,
            string targetType)
        {
            RequireText(report, ownerType, ownerId, referenceId, fieldName);

            if (!string.IsNullOrWhiteSpace(referenceId) && !lookup.ContainsKey(referenceId))
                report.AddError($"{ownerType} config '{ownerId}' references missing {targetType} id '{referenceId}' in {fieldName}.");
        }

        private static void RequireOptionalReference<TConfig>(
            ConfigValidationReport report,
            string ownerType,
            string ownerId,
            string fieldName,
            string referenceId,
            Dictionary<string, TConfig> lookup,
            string targetType)
        {
            if (!string.IsNullOrWhiteSpace(referenceId) && !lookup.ContainsKey(referenceId))
                report.AddError($"{ownerType} config '{ownerId}' references missing {targetType} id '{referenceId}' in {fieldName}.");
        }

        private static void RequireNonNegative(ConfigValidationReport report, string configType, string configId, float value, string fieldName)
        {
            if (value < 0f)
                report.AddError($"{configType} config '{configId}' has negative {fieldName}.");
        }

        private static void RequireNonNegative(ConfigValidationReport report, string configType, string configId, int value, string fieldName)
        {
            if (value < 0)
                report.AddError($"{configType} config '{configId}' has negative {fieldName}.");
        }

        private static void RequirePositive(ConfigValidationReport report, string configType, string configId, float value, string fieldName)
        {
            if (value <= 0f)
                report.AddError($"{configType} config '{configId}' must have positive {fieldName}.");
        }

        private static void RequirePositive(ConfigValidationReport report, string configType, string configId, int value, string fieldName)
        {
            if (value <= 0)
                report.AddError($"{configType} config '{configId}' must have positive {fieldName}.");
        }
    }

    public static class ConfigProvider
    {
        public static IReadOnlyDictionary<string, StatConfig> Stats => ConfigDatabase.Stats;

        public static void Reload()
        {
            ConfigDatabase.Reload();
        }

        public static bool TryGetStat(string id, out StatConfig config)
        {
            return ConfigDatabase.TryGetStat(id, out config);
        }

        public static StatConfig GetStat(string id)
        {
            return ConfigDatabase.GetStat(id);
        }
    }
}
