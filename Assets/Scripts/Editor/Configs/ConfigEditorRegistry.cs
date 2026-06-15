using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using GuildIdle;
using UnityEditor;
using UnityEngine;

namespace GuildIdle.Editor
{
    public sealed class ConfigTypeDescriptor
    {
        private readonly Func<string, object> _createDefault;

        public ConfigTypeDescriptor(
            string displayName,
            Type configType,
            string folderPath,
            string resourcePath,
            string idPrefix,
            Func<string, object> createDefault)
        {
            DisplayName = displayName;
            ConfigType = configType;
            FolderPath = folderPath;
            ResourcePath = resourcePath;
            IdPrefix = idPrefix;
            _createDefault = createDefault;
            IdField = configType.GetField("Id", BindingFlags.Instance | BindingFlags.Public);
        }

        public string DisplayName { get; }
        public Type ConfigType { get; }
        public string FolderPath { get; }
        public string ResourcePath { get; }
        public string IdPrefix { get; }
        public FieldInfo IdField { get; }

        public object CreateDefault(string id)
        {
            return _createDefault(id);
        }

        public string GetId(object config)
        {
            return IdField != null ? IdField.GetValue(config) as string : null;
        }

        public void SetId(object config, string id)
        {
            IdField?.SetValue(config, id);
        }
    }

    public sealed class ConfigAssetRecord
    {
        public ConfigAssetRecord(ConfigTypeDescriptor descriptor, string path, object config)
        {
            Descriptor = descriptor;
            Path = path;
            Config = config;
        }

        public ConfigTypeDescriptor Descriptor { get; }
        public string Path { get; set; }
        public object Config { get; set; }
        public string Id => Descriptor.GetId(Config);
        public string DisplayName => ConfigEditorAssetIo.GetDisplayName(Config);
    }

    public static class ConfigEditorRegistry
    {
        private static readonly ConfigTypeDescriptor[] _descriptors =
        {
            new ConfigTypeDescriptor("Stats", typeof(StatConfig), "Assets/Configs/Stats/Resources/Stats", ConfigDatabase.StatsResourcePath, "stat", CreateStat),
            new ConfigTypeDescriptor("Resources", typeof(ResourceConfig), "Assets/Configs/Resources/Resources/GameResources", ConfigDatabase.ResourcesResourcePath, "resource", CreateResource),
            new ConfigTypeDescriptor("Heroes", typeof(HeroConfig), "Assets/Configs/Heroes/Resources/Heroes", ConfigDatabase.HeroesResourcePath, "hero", CreateHero),
            new ConfigTypeDescriptor("Hero Growth", typeof(HeroGrowthConfig), "Assets/Configs/HeroGrowth/Resources/HeroGrowth", ConfigDatabase.HeroGrowthResourcePath, "growth", CreateHeroGrowth),
            new ConfigTypeDescriptor("Skills", typeof(SkillConfig), "Assets/Configs/Skills/Resources/Skills", ConfigDatabase.SkillsResourcePath, "skill", CreateSkill),
            new ConfigTypeDescriptor("Skill Levels", typeof(SkillLevelConfig), "Assets/Configs/SkillLevels/Resources/SkillLevels", ConfigDatabase.SkillLevelsResourcePath, "skill_levels", CreateSkillLevels),
            new ConfigTypeDescriptor("Tasks", typeof(TaskConfig), "Assets/Configs/Tasks/Resources/Tasks", ConfigDatabase.TasksResourcePath, "task", CreateTask),
            new ConfigTypeDescriptor("Items", typeof(ItemConfig), "Assets/Configs/Items/Resources/Items", ConfigDatabase.ItemsResourcePath, "item", CreateItem),
            new ConfigTypeDescriptor("Craft Recipes", typeof(CraftRecipeConfig), "Assets/Configs/CraftRecipes/Resources/CraftRecipes", ConfigDatabase.CraftRecipesResourcePath, "recipe", CreateCraftRecipe),
            new ConfigTypeDescriptor("Enemies", typeof(EnemyConfig), "Assets/Configs/Enemies/Resources/Enemies", ConfigDatabase.EnemiesResourcePath, "enemy", CreateEnemy),
            new ConfigTypeDescriptor("Combat Locations", typeof(CombatLocationConfig), "Assets/Configs/CombatLocations/Resources/CombatLocations", ConfigDatabase.CombatLocationsResourcePath, "location", CreateCombatLocation),
            new ConfigTypeDescriptor("Buildings", typeof(BuildingConfig), "Assets/Configs/Buildings/Resources/Buildings", ConfigDatabase.BuildingsResourcePath, "building", CreateBuilding),
            new ConfigTypeDescriptor("Quests", typeof(QuestConfig), "Assets/Configs/Quests/Resources/Quests", ConfigDatabase.QuestsResourcePath, "quest", CreateQuest)
        };

        public static IReadOnlyList<ConfigTypeDescriptor> Descriptors => _descriptors;

        public static ConfigTypeDescriptor GetByType(Type type)
        {
            for (var i = 0; i < _descriptors.Length; i++)
            {
                if (_descriptors[i].ConfigType == type)
                    return _descriptors[i];
            }

            return null;
        }

        private static StatConfig CreateStat(string id)
        {
            return new StatConfig
            {
                Id = id,
                Category = "attribute",
                LocalisationNameId = $"{id}_name",
                LocalisationDescriptionId = $"{id}_description",
                IconId = $"Icons/Stats/{id}_icon"
            };
        }

        private static ResourceConfig CreateResource(string id)
        {
            return new ResourceConfig
            {
                Id = id,
                DisplayName = id,
                LocalisationNameId = $"{id}_name",
                LocalisationDescriptionId = $"{id}_description",
                Icon = $"Icons/Resources/{id}_icon",
                MaxAmount = 1,
                IsPremium = false
            };
        }

        private static HeroConfig CreateHero(string id)
        {
            return new HeroConfig
            {
                Id = id,
                DisplayName = id,
                ClassName = "Adventurer",
                Portrait = $"Portraits/Heroes/{id}",
                Description = string.Empty,
                GrowthTableId = "growth_default_hero",
                StartLevel = 1,
                StartSkills = Array.Empty<SkillStartConfig>(),
                PassiveAbilityId = string.Empty,
                AutoAbilityId = string.Empty,
                DefaultEquipment = Array.Empty<EquipmentEntryConfig>()
            };
        }

        private static HeroGrowthConfig CreateHeroGrowth(string id)
        {
            return new HeroGrowthConfig
            {
                Id = id,
                Levels = new[]
                {
                    new HeroGrowthLevelConfig
                    {
                        Level = 1,
                        ExperienceToNextLevel = 1,
                        Strength = 1,
                        Agility = 1,
                        Intelligence = 1,
                        Endurance = 1,
                        Luck = 1
                    }
                }
            };
        }

        private static SkillConfig CreateSkill(string id)
        {
            return new SkillConfig
            {
                Id = id,
                DisplayName = id,
                Description = string.Empty,
                Icon = $"Icons/Stats/{id}_icon",
                MaxLevel = 1
            };
        }

        private static SkillLevelConfig CreateSkillLevels(string id)
        {
            return new SkillLevelConfig
            {
                Id = id,
                SkillId = "woodcutting",
                Levels = new[]
                {
                    new SkillLevelRowConfig
                    {
                        Level = 1,
                        ExperienceToNextLevel = 1,
                        BonusValue = 0f
                    }
                }
            };
        }

        private static TaskConfig CreateTask(string id)
        {
            return new TaskConfig
            {
                Id = id,
                DisplayName = id,
                Description = string.Empty,
                Icon = $"Icons/Tasks/{id}_icon",
                TaskType = "gathering",
                RequiredHeroTags = Array.Empty<string>(),
                CycleDurationSeconds = 1f,
                FatiguePerCycle = 0f,
                HeroExpPerCycle = 0,
                SkillExpPerCycle = 0,
                TargetSkillId = "woodcutting",
                Rewards = Array.Empty<RewardConfig>(),
                RequiredResources = Array.Empty<CostConfig>(),
                RequiredItems = Array.Empty<CostConfig>(),
                UnlockRequirements = Array.Empty<RequirementConfig>()
            };
        }

        private static ItemConfig CreateItem(string id)
        {
            return new ItemConfig
            {
                Id = id,
                DisplayName = id,
                Description = string.Empty,
                Icon = $"Icons/Items/{id}_icon",
                ItemType = "material",
                Rarity = "common",
                SlotType = string.Empty,
                Stackable = true,
                MaxStack = 1,
                StatsBonus = Array.Empty<StatBonusConfig>(),
                WorkBonus = Array.Empty<StatBonusConfig>(),
                CombatBonus = Array.Empty<StatBonusConfig>(),
                SpecialEffectId = string.Empty
            };
        }

        private static CraftRecipeConfig CreateCraftRecipe(string id)
        {
            return new CraftRecipeConfig
            {
                Id = id,
                DisplayName = id,
                ResultItemId = string.Empty,
                ResultAmount = 1,
                RequiredResources = Array.Empty<CostConfig>(),
                RequiredItems = Array.Empty<CostConfig>(),
                CraftDurationSeconds = 1f,
                RequiredSkillId = string.Empty,
                RequiredSkillLevel = 0,
                HeroExpReward = 0,
                SkillExpReward = 0,
                FatigueCost = 0f
            };
        }

        private static EnemyConfig CreateEnemy(string id)
        {
            return new EnemyConfig
            {
                Id = id,
                DisplayName = id,
                Portrait = $"Portraits/Enemies/{id}",
                EnemyType = "undead",
                MaxHealth = 1f,
                Damage = 0f,
                AttackInterval = 1f,
                Defense = 0f,
                CritChance = 0f,
                Rewards = Array.Empty<RewardConfig>(),
                HeroExpReward = 0,
                CombatSkillExpReward = 0
            };
        }

        private static CombatLocationConfig CreateCombatLocation(string id)
        {
            return new CombatLocationConfig
            {
                Id = id,
                DisplayName = id,
                Background = $"Backgrounds/Combat/{id}",
                DurationSeconds = 1f,
                EntryFatigueCost = 0f,
                FatiguePerEnemyKill = 0f,
                FatigueOnDefeat = 0f,
                EnemyPool = Array.Empty<ConfigReferenceWeight>(),
                BossEnemyId = string.Empty,
                UnlockRequirements = Array.Empty<RequirementConfig>(),
                PossibleRewards = Array.Empty<RewardConfig>()
            };
        }

        private static BuildingConfig CreateBuilding(string id)
        {
            return new BuildingConfig
            {
                Id = id,
                DisplayName = id,
                Description = string.Empty,
                Icon = $"Icons/Buildings/{id}_icon",
                MaxLevel = 1,
                Levels = new[]
                {
                    new BuildingLevelConfig
                    {
                        Level = 1,
                        UpgradeCost = Array.Empty<CostConfig>(),
                        UpgradeDurationSeconds = 0f,
                        Effects = Array.Empty<StatBonusConfig>()
                    }
                }
            };
        }

        private static QuestConfig CreateQuest(string id)
        {
            return new QuestConfig
            {
                Id = id,
                DisplayName = id,
                Description = string.Empty,
                Requirements = Array.Empty<RequirementConfig>(),
                Rewards = Array.Empty<RewardConfig>(),
                HeroExpReward = 0,
                GuildExpReward = 0,
                RefreshWeight = 1
            };
        }
    }

    public static class ConfigEditorAssetIo
    {
        public static List<ConfigAssetRecord> LoadRecords(ConfigTypeDescriptor descriptor)
        {
            EnsureFolder(descriptor);

            var records = new List<ConfigAssetRecord>();
            var files = Directory.GetFiles(descriptor.FolderPath, "*.json", SearchOption.TopDirectoryOnly);

            foreach (var file in files)
            {
                var path = ToUnityPath(file);
                var config = LoadConfig(descriptor, path);
                if (config != null)
                    records.Add(new ConfigAssetRecord(descriptor, path, config));
            }

            records.Sort((left, right) => string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase));
            return records;
        }

        public static object LoadConfig(ConfigTypeDescriptor descriptor, string path)
        {
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                return JsonUtility.FromJson(json, descriptor.ConfigType);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failed to load config '{path}': {exception.Message}");
                return null;
            }
        }

        public static string SaveConfig(ConfigTypeDescriptor descriptor, object config, string previousPath = null)
        {
            EnsureFolder(descriptor);

            var id = descriptor.GetId(config);
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("Config Id cannot be empty.");

            var fileName = MakeSafeFileName(id) + ".json";
            var nextPath = ToUnityPath(Path.Combine(descriptor.FolderPath, fileName));
            var json = JsonUtility.ToJson(config, true);

            if (!string.IsNullOrWhiteSpace(previousPath) && previousPath != nextPath && File.Exists(nextPath))
                throw new IOException($"Config file '{nextPath}' already exists.");

            if (!string.IsNullOrWhiteSpace(previousPath) && previousPath != nextPath && File.Exists(previousPath))
                AssetDatabase.DeleteAsset(previousPath);

            File.WriteAllText(nextPath, json, Encoding.UTF8);
            AssetDatabase.ImportAsset(nextPath);
            AssetDatabase.Refresh();
            ConfigDatabase.Reload();
            return nextPath;
        }

        public static bool DeleteConfig(string path)
        {
            var deleted = AssetDatabase.DeleteAsset(path);
            AssetDatabase.Refresh();
            ConfigDatabase.Reload();
            return deleted;
        }

        public static string CreateUniqueId(ConfigTypeDescriptor descriptor)
        {
            var baseId = $"new_{descriptor.IdPrefix}";
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var records = LoadRecords(descriptor);

            foreach (var record in records)
            {
                if (!string.IsNullOrWhiteSpace(record.Id))
                    existing.Add(record.Id);
            }

            if (!existing.Contains(baseId))
                return baseId;

            for (var index = 1; index < 10000; index++)
            {
                var id = $"{baseId}_{index}";
                if (!existing.Contains(id))
                    return id;
            }

            return $"{baseId}_{DateTime.UtcNow.Ticks}";
        }

        public static string GetDisplayName(object config)
        {
            if (config == null)
                return string.Empty;

            var localisationNameField = config.GetType().GetField("LocalisationNameId", BindingFlags.Instance | BindingFlags.Public);
            var localisationNameId = localisationNameField != null ? localisationNameField.GetValue(config) as string : null;
            if (!string.IsNullOrWhiteSpace(localisationNameId) && LocalisationModel.TryGetText(localisationNameId, out var localisedName))
                return localisedName;

            var field = config.GetType().GetField("DisplayName", BindingFlags.Instance | BindingFlags.Public);
            return field != null ? field.GetValue(config) as string : string.Empty;
        }

        public static void EnsureFolder(ConfigTypeDescriptor descriptor)
        {
            if (!Directory.Exists(descriptor.FolderPath))
            {
                Directory.CreateDirectory(descriptor.FolderPath);
                AssetDatabase.Refresh();
            }
        }

        private static string MakeSafeFileName(string id)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = id.Trim().ToCharArray();

            for (var i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                    chars[i] = '_';
            }

            return new string(chars);
        }

        private static string ToUnityPath(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
