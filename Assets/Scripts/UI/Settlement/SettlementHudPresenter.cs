using System;
using System.Collections.Generic;
using System.Globalization;
using GuildIdle.Progression;
using UnityEngine;

public sealed class SettlementHudPresenter
{
    private readonly SettlementHudView _view;
    private readonly ISettlementHudRuntimeSource _runtimeSource;
    private SettlementHudState _lastState;

    public SettlementHudPresenter(SettlementHudView view, ISettlementHudRuntimeSource runtimeSource)
    {
        _view = view != null ? view : throw new ArgumentNullException(nameof(view));
        _runtimeSource = runtimeSource ?? throw new ArgumentNullException(nameof(runtimeSource));
    }

    public void Refresh(bool forceRender = false)
    {
        var state = _runtimeSource.IsReady ? BuildState() : SettlementHudState.Empty;
        if (!forceRender && ContentEquals(_lastState, state))
            return;

        _lastState = state;
        _view.Render(state);
    }

    private SettlementHudState BuildState()
    {
        var currencies = new[]
        {
            new CurrencyItemState(_runtimeSource.MainCurrencyAmount.ToString("N0", CultureInfo.CurrentCulture))
        };

        var questSnapshot = _runtimeSource.GetQuestSnapshot();
        var activeInstances = questSnapshot?.ActiveInstances;
        var quests = new List<QuestItemState>(activeInstances?.Count ?? 0);
        foreach (var quest in activeInstances ?? Array.Empty<QuestInstanceSnapshot>())
        {
            if (quest == null)
                continue;

            quests.Add(new QuestItemState(
                _runtimeSource.Localise(quest.NameId),
                _runtimeSource.Localise(quest.ShortDescriptionId),
                BuildQuestSteps(quest),
                quest.IconId));
        }

        return new SettlementHudState(currencies, BuildStageInfoState(), quests.ToArray(), BuildActiveHeroesState());
    }

    private StageInfoState BuildStageInfoState()
    {
        var snapshot = _runtimeSource.GetStageSnapshot();
        if (snapshot == null)
            return StageInfoState.Empty;

        var progressPercent = Mathf.Clamp(snapshot.RequiredProgressPercent, 0, 100);
        return new StageInfoState(
            _runtimeSource.Localise(snapshot.NameId),
            $"{progressPercent}%",
            progressPercent / 100f);
    }

    private ActiveHeroesPanelState BuildActiveHeroesState()
    {
        var snapshot = _runtimeSource.GetActiveHeroesSnapshot() ?? ActiveHeroesRuntimeSnapshot.Empty;
        var heroes = new List<ActiveHeroCardState>(snapshot.Heroes.Count);
        foreach (var hero in snapshot.Heroes)
        {
            if (hero == null)
                continue;

            heroes.Add(new ActiveHeroCardState(
                LocaliseOrFallback(hero.HeroNameId, hero.HeroFallbackName),
                LocaliseOrFallback(hero.WorkNameId, hero.WorkFallbackName),
                hero.Progress,
                FormatCycle(hero.CurrentCycle, hero.TotalCycles),
                FormatRemainingTime(hero.RemainingSeconds)));
        }

        return new ActiveHeroesPanelState(snapshot.CurrentCount, snapshot.Limit, heroes.ToArray());
    }

    private string LocaliseOrFallback(string id, string fallback)
    {
        var value = _runtimeSource.Localise(id);
        return string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value;
    }

    private static string FormatCycle(int currentCycle, int totalCycles)
    {
        return totalCycles > 0
            ? $"{Mathf.Clamp(currentCycle, 0, totalCycles)}/{totalCycles}"
            : string.Empty;
    }

    private static string FormatRemainingTime(float remainingSeconds)
    {
        if (remainingSeconds < 0f || float.IsNaN(remainingSeconds) || float.IsInfinity(remainingSeconds))
            return string.Empty;

        var totalSeconds = (long)Math.Ceiling(remainingSeconds);
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return hours > 0
            ? $"{hours:00}:{minutes:00}:{seconds:00}"
            : $"{minutes:00}:{seconds:00}";
    }

    private IReadOnlyList<QuestStepItemState> BuildQuestSteps(QuestInstanceSnapshot quest)
    {
        var result = new List<QuestStepItemState>();
        foreach (var step in quest.Steps ?? Array.Empty<QuestStepSnapshot>())
        {
            if (step == null)
                continue;

            result.Add(new QuestStepItemState(
                $"{_runtimeSource.Localise(step.DescriptionId)} {step.CurrentValue}/{step.TargetValue}",
                step.Completed));
        }

        return result;
    }

    private static bool ContentEquals(SettlementHudState left, SettlementHudState right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null ||
            left.Currencies.Count != right.Currencies.Count ||
            left.Quests.Count != right.Quests.Count ||
            !StageInfoEqual(left.Stage, right.Stage) ||
            !ActiveHeroesEqual(left.ActiveHeroes, right.ActiveHeroes))
            return false;

        for (var index = 0; index < left.Currencies.Count; index++)
        {
            if (!string.Equals(left.Currencies[index].Amount, right.Currencies[index].Amount, StringComparison.Ordinal))
                return false;
        }

        for (var index = 0; index < left.Quests.Count; index++)
        {
            var leftQuest = left.Quests[index];
            var rightQuest = right.Quests[index];
            if (!string.Equals(leftQuest.Name, rightQuest.Name, StringComparison.Ordinal) ||
                !string.Equals(leftQuest.ShortDescription, rightQuest.ShortDescription, StringComparison.Ordinal) ||
                !string.Equals(leftQuest.IconId, rightQuest.IconId, StringComparison.Ordinal) ||
                !QuestStepsEqual(leftQuest.Steps, rightQuest.Steps))
            {
                return false;
            }
        }

        return true;
    }

    private static bool QuestStepsEqual(
        IReadOnlyList<QuestStepItemState> left,
        IReadOnlyList<QuestStepItemState> right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null || left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index].Text, right[index].Text, StringComparison.Ordinal) ||
                left[index].Completed != right[index].Completed)
            {
                return false;
            }
        }

        return true;
    }

    private static bool StageInfoEqual(StageInfoState left, StageInfoState right)
    {
        if (ReferenceEquals(left, right))
            return true;

        return left != null && right != null &&
               string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
               string.Equals(left.ProgressText, right.ProgressText, StringComparison.Ordinal) &&
               left.Progress.Equals(right.Progress);
    }

    private static bool ActiveHeroesEqual(ActiveHeroesPanelState left, ActiveHeroesPanelState right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null ||
            left.CurrentCount != right.CurrentCount ||
            left.Limit != right.Limit ||
            left.Heroes.Count != right.Heroes.Count)
            return false;

        for (var index = 0; index < left.Heroes.Count; index++)
        {
            var leftHero = left.Heroes[index];
            var rightHero = right.Heroes[index];
            if (!string.Equals(leftHero.HeroName, rightHero.HeroName, StringComparison.Ordinal) ||
                !string.Equals(leftHero.WorkName, rightHero.WorkName, StringComparison.Ordinal) ||
                !leftHero.Progress.Equals(rightHero.Progress) ||
                !string.Equals(leftHero.Cycle, rightHero.Cycle, StringComparison.Ordinal) ||
                !string.Equals(leftHero.RemainingTime, rightHero.RemainingTime, StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
