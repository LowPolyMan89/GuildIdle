using GuildIdle;
using GuildIdle.Editor;
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class GuildIdleSystemsTests
{
    [Test]
    public void Stat_ClampsCurrentValueToRange()
    {
        var stat = new Stat("health", 150f, 100f);

        Assert.AreEqual(100f, stat.CurrentValue);

        stat.CurrentValue = -10f;
        Assert.AreEqual(0f, stat.CurrentValue);

        stat.CurrentValue = 120f;
        Assert.AreEqual(100f, stat.CurrentValue);
    }

    [Test]
    public void Stat_ModifiersChangeMaxValue()
    {
        var stat = new Stat("strength", 10f, 100f);

        stat.AddModifier(new StatModifier("flat_bonus", StatTarget.Max, StatModifierType.Flat, 25f));
        stat.AddModifier(new StatModifier("percent_bonus", StatTarget.Max, StatModifierType.Percent, 20f));

        Assert.AreEqual(150f, stat.MaxValue);
    }

    [Test]
    public void Stat_TimedEffectAppliesAndExpires()
    {
        var stat = new Stat("fatigue", 0f, 100f);
        stat.AddEffect(new TimedStatEffect("work", StatTarget.Current, StatModifierType.Flat, 10f, 2f));

        stat.Tick(0.5f);
        Assert.AreEqual(5f, stat.CurrentValue);
        Assert.AreEqual(1, stat.Effects.Count);

        stat.Tick(2f);
        Assert.AreEqual(20f, stat.CurrentValue);
        Assert.AreEqual(0, stat.Effects.Count);
    }

    [Test]
    public void ConfigDatabase_LoadsGuildIdleStatConfigs()
    {
        ConfigDatabase.Reload();

        Assert.AreEqual(18, ConfigDatabase.Stats.Count);
        AssertLoadedStat("strength", "attribute");
        AssertLoadedStat("mining", "skill");
        AssertLoadedStat("damage", "combat");
        AssertLoadedStat("fatigue", "state");
    }

    [Test]
    public void ConfigDatabase_LoadsMvpConfigs()
    {
        ConfigDatabase.Reload();

        Assert.AreEqual(6, ConfigDatabase.Resources.Count);
        Assert.AreEqual(1, ConfigDatabase.Heroes.Count);
        Assert.AreEqual(1, ConfigDatabase.HeroGrowth.Count);
        Assert.AreEqual(6, ConfigDatabase.Skills.Count);
        Assert.AreEqual(1, ConfigDatabase.SkillLevels.Count);
        Assert.AreEqual(1, ConfigDatabase.Tasks.Count);

        Assert.AreEqual("Wood", ConfigDatabase.GetResource("wood").DisplayName);
        Assert.AreEqual("wood_name", ConfigDatabase.GetResource("wood").LocalisationNameId);
        Assert.AreEqual("wood_description", ConfigDatabase.GetResource("wood").LocalisationDescriptionId);
        Assert.AreEqual("Leo", ConfigDatabase.GetHero("hero_leo").DisplayName);
        Assert.AreEqual("Woodcutting", ConfigDatabase.GetSkill("woodcutting").DisplayName);
        Assert.AreEqual("Forest Edge", ConfigDatabase.GetTask("task_wood_gathering_01").DisplayName);
    }

    [Test]
    public void ConfigDatabase_TaskUsesJsonValues()
    {
        ConfigDatabase.Reload();

        var task = ConfigDatabase.GetTask("task_wood_gathering_01");

        Assert.AreEqual(30f, task.CycleDurationSeconds);
        Assert.AreEqual(1f, task.FatiguePerCycle);
        Assert.AreEqual(2, task.HeroExpPerCycle);
        Assert.AreEqual(4, task.SkillExpPerCycle);
        Assert.AreEqual("woodcutting", task.TargetSkillId);
        Assert.AreEqual(1, task.Rewards.Length);
        Assert.AreEqual("resource", task.Rewards[0].Type);
        Assert.AreEqual("wood", task.Rewards[0].Id);
        Assert.AreEqual(8, task.Rewards[0].Amount);
    }

    [Test]
    public void ConfigDatabase_ValidatePassesForSampleData()
    {
        ConfigDatabase.Reload();

        var report = ConfigDatabase.Validate();

        Assert.IsTrue(report.IsValid, string.Join("\n", report.Errors));
        Assert.AreEqual(0, report.Errors.Count);
    }

    [Test]
    public void ConfigDatabase_MissingIdsUseTryGetAndThrowingGetters()
    {
        ConfigDatabase.Reload();

        Assert.IsFalse(ConfigDatabase.TryGetResource("missing_resource", out _));
        Assert.IsFalse(ConfigDatabase.HasTask("missing_task"));
        Assert.Throws<KeyNotFoundException>(() => ConfigDatabase.GetHero("missing_hero"));
    }

    [Test]
    public void ConfigEditorRegistry_ContainsAllConfigTypes()
    {
        var expectedTypes = new HashSet<Type>
        {
            typeof(StatConfig),
            typeof(ResourceConfig),
            typeof(HeroConfig),
            typeof(HeroGrowthConfig),
            typeof(SkillConfig),
            typeof(SkillLevelConfig),
            typeof(TaskConfig),
            typeof(ItemConfig),
            typeof(CraftRecipeConfig),
            typeof(EnemyConfig),
            typeof(CombatLocationConfig),
            typeof(BuildingConfig),
            typeof(QuestConfig)
        };

        foreach (var descriptor in ConfigEditorRegistry.Descriptors)
        {
            Assert.IsTrue(expectedTypes.Remove(descriptor.ConfigType), descriptor.ConfigType.Name);
            Assert.IsNotNull(descriptor.IdField);
            Assert.IsFalse(string.IsNullOrWhiteSpace(descriptor.FolderPath));
            Assert.IsFalse(string.IsNullOrWhiteSpace(descriptor.ResourcePath));
        }

        Assert.AreEqual(0, expectedTypes.Count, "Some config types are missing from the editor registry.");
    }

    [Test]
    public void ConfigEditorRegistry_DefaultFactoriesCreateIds()
    {
        foreach (var descriptor in ConfigEditorRegistry.Descriptors)
        {
            var config = descriptor.CreateDefault($"test_{descriptor.IdPrefix}");
            Assert.IsNotNull(config);
            Assert.AreEqual($"test_{descriptor.IdPrefix}", descriptor.GetId(config));
        }
    }

    [Test]
    public void ConfigEditorAssetIo_SaveLoadAndRenameRoundTrip()
    {
        var descriptor = ConfigEditorRegistry.GetByType(typeof(ResourceConfig));
        var id = "test_resource_roundtrip";
        var renamedId = "test_resource_roundtrip_renamed";
        var path = $"{descriptor.FolderPath}/{id}.json";
        var renamedPath = $"{descriptor.FolderPath}/{renamedId}.json";

        try
        {
            DeleteAssetIfExists(path);
            DeleteAssetIfExists(renamedPath);

            var resource = (ResourceConfig)descriptor.CreateDefault(id);
            resource.DisplayName = "Roundtrip Resource";

            var savedPath = ConfigEditorAssetIo.SaveConfig(descriptor, resource);
            Assert.AreEqual(path, savedPath);
            Assert.IsTrue(File.Exists(path));

            var loaded = (ResourceConfig)ConfigEditorAssetIo.LoadConfig(descriptor, savedPath);
            Assert.AreEqual(id, loaded.Id);
            Assert.AreEqual("Roundtrip Resource", loaded.DisplayName);

            loaded.Id = renamedId;
            var nextPath = ConfigEditorAssetIo.SaveConfig(descriptor, loaded, savedPath);
            Assert.AreEqual(renamedPath, nextPath);
            Assert.IsFalse(File.Exists(path));
            Assert.IsTrue(File.Exists(renamedPath));
        }
        finally
        {
            DeleteAssetIfExists(path);
            DeleteAssetIfExists(renamedPath);
            ConfigDatabase.Reload();
        }
    }

    [Test]
    public void ConfigManagerValidationReport_CanBeDisplayed()
    {
        ConfigDatabase.Reload();

        var report = ConfigDatabase.Validate();

        Assert.IsNotNull(report.Errors);
        Assert.IsNotNull(report.Warnings);
    }

    [Test]
    public void LocalisationEditorAssetIo_LoadsSourceTablesOnly()
    {
        var tables = LocalisationEditorAssetIo.LoadTables();

        Assert.GreaterOrEqual(tables.Count, 2);
        foreach (var table in tables)
        {
            Assert.IsFalse(table.Path.Contains("/Resources/"), table.Path);
            Assert.AreNotEqual(LocalisationBuilder.OutputPath, table.Path);
        }
    }

    [Test]
    public void LocalisationEditorAssetIo_DefaultEntryHasValidKey()
    {
        var entry = LocalisationEditorAssetIo.CreateDefaultEntry("new_text_1");

        Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Id));
        Assert.IsTrue(LocalisationEditorAssetIo.IsValidKeyFormat(entry.Id));
        Assert.AreEqual(LocalisationModel.Languages.Length, entry.Lang.Length);
    }

    [Test]
    public void LocalisationEditorAssetIo_DefaultTableHasValidIdAndPath()
    {
        var table = LocalisationEditorAssetIo.CreateDefaultTable("new_table_1");

        Assert.IsFalse(string.IsNullOrWhiteSpace(table.Id));
        Assert.IsTrue(LocalisationEditorAssetIo.IsValidTableIdFormat(table.Id));
        Assert.AreEqual("Assets/Configs/Localization/new_table_1.json", table.Path);
        Assert.IsNotNull(table.Config.Texts);
        Assert.AreEqual(0, table.Config.Texts.Length);
    }

    [Test]
    public void LocalisationEditorAssetIo_ValidatorCatchesInvalidEntries()
    {
        var table = new LocalisationTableRecord
        {
            Id = "test",
            Path = "Assets/Configs/Localization/test.json",
            Config = new LocalisationConfig
            {
                Texts = new[]
                {
                    LocalisationEditorAssetIo.CreateDefaultEntry(string.Empty),
                    LocalisationEditorAssetIo.CreateDefaultEntry("Bad-Key"),
                    LocalisationEditorAssetIo.CreateDefaultEntry("duplicate_key"),
                    LocalisationEditorAssetIo.CreateDefaultEntry("duplicate_key"),
                    new LocalisationText
                    {
                        Id = "wrong_lang_count",
                        Lang = new[] { new LocalisationValue { Value = "ru" } }
                    }
                }
            }
        };

        var duplicateTable = LocalisationEditorAssetIo.CreateDefaultTable("test");
        duplicateTable.Path = "Assets/Configs/Localization/test_duplicate.json";

        var report = LocalisationEditorAssetIo.ValidateTables(new[] { table, duplicateTable });

        Assert.IsFalse(report.IsValid);
        Assert.GreaterOrEqual(report.Errors.Count, 5);
    }

    [Test]
    public void LocalisationEditorAssetIo_SaveLoadRenameAndBuildRoundTrip()
    {
        var path = "Assets/Configs/Localization/test_editor_localisation.json";
        var id = "test_localisation_roundtrip";
        var renamedId = "test_localisation_roundtrip_renamed";

        try
        {
            DeleteAssetIfExists(path);

            var table = new LocalisationTableRecord
            {
                Id = "test_editor_localisation",
                Path = path,
                Config = new LocalisationConfig { Texts = Array.Empty<LocalisationText>() }
            };

            var entry = LocalisationEditorAssetIo.CreateDefaultEntry(id);
            LocalisationEditorAssetIo.SetValue(entry, 0, "Тестовая строка");
            LocalisationEditorAssetIo.SetValue(entry, 1, "Test string");
            LocalisationEditorAssetIo.SetValue(entry, 2, "Test metni");
            LocalisationEditorAssetIo.AddEntry(table, entry);

            Assert.IsTrue(LocalisationEditorAssetIo.ValidateTables(new[] { table }).IsValid);
            LocalisationEditorAssetIo.SaveTable(table);

            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(LocalisationEditorAssetIo.TryReadConfig(path, out var loadedConfig, out var readError), readError);
            Assert.AreEqual(id, loadedConfig.Texts[0].Id);
            Assert.AreEqual("Тестовая строка", LocalisationEditorAssetIo.GetValue(loadedConfig.Texts[0], 0));

            loadedConfig.Texts[0].Id = renamedId;
            table.Config = loadedConfig;
            Assert.IsTrue(LocalisationEditorAssetIo.ValidateTables(new[] { table }).IsValid);
            LocalisationEditorAssetIo.SaveTable(table);

            Assert.IsTrue(LocalisationEditorAssetIo.TryReadConfig(path, out loadedConfig, out readError), readError);
            Assert.AreEqual(renamedId, loadedConfig.Texts[0].Id);
            Assert.IsTrue(File.ReadAllText(LocalisationBuilder.OutputPath).Contains(renamedId));
        }
        finally
        {
            DeleteAssetIfExists(path);
            LocalisationBuilder.BuildLocalisation();
            LocalisationModel.Reload();
        }
    }

    [Test]
    public void LocalisationModel_ReturnsTextsForSupportedLanguages()
    {
        var previousLanguage = LocalisationModel.CurrentLanguage;

        try
        {
            LocalisationModel.Reload();

            LocalisationModel.SetLanguage("ru");
            Assert.AreEqual("Сила", LocalisationModel.GetText("strength_name"));
            Assert.AreEqual("Дерево", LocalisationModel.GetText("wood_name"));
            Assert.AreEqual("Базовый строительный и ремесленный материал из лесозаготовки.", LocalisationModel.GetText("wood_description"));

            LocalisationModel.SetLanguage("en");
            Assert.AreEqual("Strength", LocalisationModel.GetText("strength_name"));
            Assert.AreEqual("Wood", LocalisationModel.GetText("wood_name"));
            Assert.AreEqual("A basic building and crafting material gathered through woodcutting.", LocalisationModel.GetText("wood_description"));

            LocalisationModel.SetLanguage("tr");
            Assert.AreEqual("Güç", LocalisationModel.GetText("strength_name"));
            Assert.AreEqual("Odun", LocalisationModel.GetText("wood_name"));
            Assert.AreEqual("Odunculukla toplanan temel inşaat ve zanaat malzemesi.", LocalisationModel.GetText("wood_description"));
        }
        finally
        {
            LocalisationModel.SetLanguage(previousLanguage);
        }
    }

    [Test]
    public void LocalisationModel_ReturnsKeyForMissingText()
    {
        LocalisationModel.Reload();

        LogAssert.Expect(LogType.Warning, "Localisation key 'missing_key_for_test' was not found.");

        Assert.AreEqual("missing_key_for_test", LocalisationModel.GetText("missing_key_for_test"));
    }

    private static void AssertLoadedStat(string id, string category)
    {
        Assert.IsTrue(ConfigDatabase.TryGetStat(id, out var config), $"Stat '{id}' should be loaded.");
        Assert.AreEqual(category, config.Category);
        Assert.AreEqual($"{id}_name", config.LocalisationNameId);
        Assert.AreEqual($"{id}_description", config.LocalisationDescriptionId);
        Assert.AreEqual($"Icons/Stats/{id}_icon", config.IconId);
    }

    private static void DeleteAssetIfExists(string path)
    {
        if (File.Exists(path))
            AssetDatabase.DeleteAsset(path);
    }
}
