using System;
using System.Collections.Generic;
using System.Reflection;
using GuildIdle.Activities;
using GuildIdle.Combat;
using GuildIdle.Configs;
using GuildIdle.Core;
using NUnit.Framework;

public sealed class CombatWindowPresentationTests
{
    [SetUp]
    public void SetUp()
    {
        Configs.SetDatabaseForTests(CreateDatabase(new LocalisationRuntimeConfigDto
        {
            localisations = new[]
            {
                Entry("ui.combat.status.running", "АВТОМАТИЧЕСКИЙ БОЙ\nБой идёт — окно можно безопасно закрыть"),
                Entry("ui.combat.outcome.victory", "ПОБЕДА"),
                Entry("ui.combat.outcome.defeat", "ПОРАЖЕНИЕ"),
                Entry("ui.combat.outcome.retreat", "ОТСТУПЛЕНИЕ"),
                Entry("ui.combat.status.resolved", "{0}\nРезультат получен · Герой свободен"),
                Entry("ui.combat.status.result_pending",
                    "{0}\nРезультат готов · Добыча {1} · Боевой опыт +{2}")
            }
        }));
        Configs.Localisation.SetLang("ru");
    }

    [TearDown]
    public void TearDown()
    {
        Configs.SetDatabaseForTests(CreateDatabase(null));
    }

    [Test]
    public void RunningCombat_ShowsSafeCloseHintAndNoResultAction()
    {
        var state = CreateState(string.Empty, canRetreat: true, canOpenLoot: false);

        Assert.That(state.StatusMark, Is.EqualTo("⚔"));
        Assert.That(state.StatusText, Does.Contain("Бой идёт"));
        Assert.That(state.CanOpenLoot, Is.False);
    }

    [Test]
    public void RetreatResult_ShowsPreservedRewards()
    {
        var state = CreateState("Retreat", canRetreat: false, canOpenLoot: true);

        Assert.That(state.StatusMark, Is.EqualTo("⚑"));
        Assert.That(state.StatusText, Does.Contain("ОТСТУПЛЕНИЕ"));
        Assert.That(state.StatusText, Does.Contain("Добыча 3"));
        Assert.That(state.StatusText, Does.Contain("Боевой опыт +40"));
        Assert.That(state.CanOpenLoot, Is.True);
    }

    [TestCase("Combat", true)]
    [TestCase("combat", true)]
    [TestCase("Peaceful", false)]
    public void CombatActions_FilterSkillsByType(string type, bool expected)
    {
        var method = typeof(CombatWindowController).GetMethod(
            "IsCombatSkill",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);

        var skill = new GuildIdle.Configs.HeroUniqueSkillConfigDto { type = type };

        Assert.That((bool)method.Invoke(null, new object[] { skill }), Is.EqualTo(expected));
    }

    [Test]
    public void ConsumablePicker_UsesSeparateListWithDescriptions()
    {
        var option = new CombatConsumableOptionState(
            "stack-1", "Roasted Rabbit", "Restores health.", null, 3, true);
        var state = new HeroSelectionPopupState(
            "Consumables", string.Empty, null, Array.Empty<HeroSelectionOptionState>(),
            new[] { option }, string.Empty);

        Assert.That(state.Heroes, Is.Empty);
        Assert.That(state.Consumables, Has.Count.EqualTo(1));
        Assert.That(state.Consumables[0].Description, Is.EqualTo("Restores health."));
        Assert.That(state.Consumables[0].Quantity, Is.EqualTo(3));
    }

    [Test]
    public void ResolvedResult_ExplainsThatHeroIsFree()
    {
        var state = CreateState("Victory", canRetreat: false, canOpenLoot: false);

        Assert.That(state.StatusMark, Is.EqualTo("✓"));
        Assert.That(state.StatusText, Does.Contain("Результат получен"));
        Assert.That(state.StatusText, Does.Contain("Герой свободен"));
    }

    [Test]
    public void LinkedDangerCombat_UsesExactChildExecutionForWorkCard()
    {
        var activity = new ActivityExecutionSnapshot
        {
            activityId = "hunt_rabbits",
            linkedCombat = new LinkedCombatStartRequestSaveData
            {
                combatExecutionId = "linked-combat"
            }
        };
        var unrelated = new OnlineCombatSnapshot
        {
            executionId = "other-combat",
            activityId = "hunt_rabbits",
            status = CombatExecutionStatus.ResultPending
        };
        var linked = new OnlineCombatSnapshot
        {
            executionId = "linked-combat",
            activityId = "hunt_rabbits",
            status = CombatExecutionStatus.Running
        };

        var result = FindCombatExecution(new[] { unrelated, linked }, activity, ObjectActivityKind.Work,
            "hunt_rabbits");

        Assert.That(result, Is.SameAs(linked));
    }

    [Test]
    public void WorkWithoutLinkedDanger_DoesNotAdoptCombatWithSameActivityId()
    {
        var combat = new OnlineCombatSnapshot
        {
            executionId = "direct-combat",
            activityId = "hunt_rabbits",
            status = CombatExecutionStatus.Running
        };

        var result = FindCombatExecution(new[] { combat }, new ActivityExecutionSnapshot(),
            ObjectActivityKind.Work, "hunt_rabbits");

        Assert.That(result, Is.Null);
    }

    private static CombatWindowState CreateState(string outcome, bool canRetreat, bool canOpenLoot)
    {
        return new CombatWindowState(
            1,
            4,
            CombatActorState.Empty,
            CombatActorState.Empty,
            Array.Empty<CombatActionState>(),
            3,
            40,
            outcome,
            canRetreat,
            canOpenLoot);
    }

    private static OnlineCombatSnapshot FindCombatExecution(
        IEnumerable<OnlineCombatSnapshot> combats,
        ActivityExecutionSnapshot activity,
        ObjectActivityKind kind,
        string activityId)
    {
        var method = typeof(RuntimeObjectActivitiesSource).GetMethod(
            "FindCombatExecution",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        return (OnlineCombatSnapshot)method.Invoke(null, new object[] { combats, activity, kind, activityId });
    }

    private static ConfigDatabase CreateDatabase(LocalisationRuntimeConfigDto localisation) =>
        new ConfigDatabase(null, null, null, null, null, null, null, null, null, null, localisation);

    private static LocalisationEntryDto Entry(string id, string text) => new LocalisationEntryDto
    {
        id = id,
        ru = text,
        en = text,
        tr = text
    };
}
