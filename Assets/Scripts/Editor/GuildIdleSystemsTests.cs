using GuildIdle;
using System.Collections.Generic;
using NUnit.Framework;
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
    public void LocalisationModel_ReturnsTextsForSupportedLanguages()
    {
        var previousLanguage = LocalisationModel.CurrentLanguage;

        try
        {
            LocalisationModel.Reload();

            LocalisationModel.SetLanguage("ru");
            Assert.AreEqual("Сила", LocalisationModel.GetText("strength_name"));

            LocalisationModel.SetLanguage("en");
            Assert.AreEqual("Strength", LocalisationModel.GetText("strength_name"));

            LocalisationModel.SetLanguage("tr");
            Assert.AreEqual("Güç", LocalisationModel.GetText("strength_name"));
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
}
