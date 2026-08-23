using System;
using System.Collections.Generic;

public sealed class SettlementHudState
{
    public static readonly SettlementHudState Empty = new SettlementHudState(
        Array.Empty<CurrencyItemState>(),
        StageInfoState.Empty,
        Array.Empty<QuestItemState>(),
        ActiveHeroesPanelState.Empty);

    public SettlementHudState(
        IReadOnlyList<CurrencyItemState> currencies,
        StageInfoState stage,
        IReadOnlyList<QuestItemState> quests,
        ActiveHeroesPanelState activeHeroes)
    {
        Currencies = currencies ?? Array.Empty<CurrencyItemState>();
        Stage = stage ?? StageInfoState.Empty;
        Quests = quests ?? Array.Empty<QuestItemState>();
        ActiveHeroes = activeHeroes ?? ActiveHeroesPanelState.Empty;
    }

    public IReadOnlyList<CurrencyItemState> Currencies { get; }
    public StageInfoState Stage { get; }
    public IReadOnlyList<QuestItemState> Quests { get; }
    public ActiveHeroesPanelState ActiveHeroes { get; }
}

public sealed class CurrencyItemState
{
    public CurrencyItemState(string amount)
    {
        Amount = amount ?? string.Empty;
    }

    public string Amount { get; }
}

public sealed class QuestItemState
{
    public QuestItemState(string name, string description)
    {
        Name = name ?? string.Empty;
        Description = description ?? string.Empty;
    }

    public string Name { get; }
    public string Description { get; }
}

public sealed class StageInfoState
{
    public static readonly StageInfoState Empty = new StageInfoState(string.Empty, string.Empty, 0f);

    public StageInfoState(string name, string progressText, float progress)
    {
        Name = name ?? string.Empty;
        ProgressText = progressText ?? string.Empty;
        Progress = progress;
    }

    public string Name { get; }
    public string ProgressText { get; }
    public float Progress { get; }
}

public sealed class ActiveHeroesPanelState
{
    public static readonly ActiveHeroesPanelState Empty = new ActiveHeroesPanelState(
        0,
        0,
        Array.Empty<ActiveHeroCardState>());

    public ActiveHeroesPanelState(int currentCount, int limit, IReadOnlyList<ActiveHeroCardState> heroes)
    {
        CurrentCount = currentCount;
        Limit = limit;
        Heroes = heroes ?? Array.Empty<ActiveHeroCardState>();
    }

    public int CurrentCount { get; }
    public int Limit { get; }
    public IReadOnlyList<ActiveHeroCardState> Heroes { get; }
}

public sealed class ActiveHeroCardState
{
    public static readonly ActiveHeroCardState Empty = new ActiveHeroCardState(
        string.Empty,
        string.Empty,
        0f,
        string.Empty,
        string.Empty);

    public ActiveHeroCardState(string heroName, string workName, float progress, string cycle, string remainingTime)
    {
        HeroName = heroName ?? string.Empty;
        WorkName = workName ?? string.Empty;
        Progress = progress;
        Cycle = cycle ?? string.Empty;
        RemainingTime = remainingTime ?? string.Empty;
    }

    public string HeroName { get; }
    public string WorkName { get; }
    public float Progress { get; }
    public string Cycle { get; }
    public string RemainingTime { get; }
}
