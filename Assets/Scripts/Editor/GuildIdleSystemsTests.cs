using GuildIdle;
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
    public void ConfigProvider_LoadsGuildIdleStatConfigs()
    {
        ConfigProvider.Reload();

        Assert.AreEqual(18, ConfigProvider.Stats.Count);
        AssertLoadedStat("strength", "attribute");
        AssertLoadedStat("mining", "skill");
        AssertLoadedStat("damage", "combat");
        AssertLoadedStat("fatigue", "state");
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
        Assert.IsTrue(ConfigProvider.TryGetStat(id, out var config), $"Stat '{id}' should be loaded.");
        Assert.AreEqual(category, config.Category);
        Assert.AreEqual($"{id}_name", config.LocalisationNameId);
        Assert.AreEqual($"{id}_description", config.LocalisationDescriptionId);
        Assert.AreEqual($"Icons/Stats/{id}_icon", config.IconId);
    }
}
