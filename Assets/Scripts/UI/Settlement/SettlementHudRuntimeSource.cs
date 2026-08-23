using System;
using System.Collections.Generic;
using GuildIdle.Activities;
using GuildIdle.Player;
using GuildIdle.Progression;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;
using RuntimePlayer = GuildIdle.Player.Player;

public interface ISettlementHudRuntimeSource
{
    bool IsReady { get; }
    long MainCurrencyAmount { get; }
    QuestRuntimeSnapshot GetQuestSnapshot();
    StageProgressionSnapshot GetStageSnapshot();
    ActiveHeroesRuntimeSnapshot GetActiveHeroesSnapshot();
    string Localise(string id);
}

public sealed class RuntimeSettlementHudSource : ISettlementHudRuntimeSource
{
    private const string MainCurrencyId = "gold_id";

    public bool IsReady => RuntimeConfigs.IsLoaded && RuntimePlayer.IsLoaded && RuntimePlayer.State != null;
    public long MainCurrencyAmount => RuntimePlayer.State?.GetCurrency(MainCurrencyId) ?? 0L;
    public QuestRuntimeSnapshot GetQuestSnapshot() => RuntimePlayer.Progression?.GetQuestSnapshot();
    public StageProgressionSnapshot GetStageSnapshot() => RuntimePlayer.Progression?.GetStageSnapshot();

    public ActiveHeroesRuntimeSnapshot GetActiveHeroesSnapshot()
    {
        var playerState = RuntimePlayer.State;
        if (playerState == null)
            return ActiveHeroesRuntimeSnapshot.Empty;

        var currentCount = playerState.GetActiveHeroCount();
        var limit = ActiveHeroLimitResolver.GetCurrentLimit(new PlayerStateActivityAdapter(playerState));
        if (currentCount == 0)
        {
            return new ActiveHeroesRuntimeSnapshot(
                currentCount,
                limit,
                Array.Empty<ActiveHeroRuntimeSnapshot>());
        }

        var activityExecutions =
            OnlineActivityRuntime.GetPresentationSnapshot()?.executions ?? Array.Empty<ActivityExecutionSnapshot>();
        var craftExecutions = OnlineActivityRuntime.GetPresentationCraftSnapshots();
        var combatExecutions = OnlineActivityRuntime.GetCombatSnapshots();
        var heroes = new List<ActiveHeroRuntimeSnapshot>(currentCount);

        foreach (var hero in RuntimeConfigs.Heroes.Heroes)
        {
            if (hero == null || !playerState.IsHeroBusy(hero.heroId))
                continue;

            var executionId = playerState.GetHeroCurrentActivityExecutionId(hero.heroId);
            heroes.Add(BuildHeroSnapshot(
                hero.nameId,
                hero.heroId,
                executionId,
                activityExecutions,
                craftExecutions,
                combatExecutions));
        }

        return new ActiveHeroesRuntimeSnapshot(currentCount, limit, heroes.ToArray());
    }

    public string Localise(string id) => RuntimeConfigs.Localisation.Get(id);

    private static ActiveHeroRuntimeSnapshot BuildHeroSnapshot(
        string heroNameId,
        string heroId,
        string executionId,
        ActivityExecutionSnapshot[] activities,
        OnlineCraftSnapshot[] crafts,
        OnlineCombatSnapshot[] combats)
    {
        foreach (var activity in activities)
        {
            if (activity == null || !string.Equals(activity.executionId, executionId, StringComparison.Ordinal))
                continue;

            RuntimeConfigs.Activities.TryGet(activity.activityId, out var config);
            return new ActiveHeroRuntimeSnapshot(
                heroNameId,
                heroId,
                config?.nameId,
                activity.activityId,
                activity.progress,
                activity.completedCycles,
                activity.plannedCycles,
                activity.remainingSeconds);
        }

        foreach (var craft in crafts)
        {
            if (craft == null || !string.Equals(craft.executionId, executionId, StringComparison.Ordinal))
                continue;

            RuntimeConfigs.Items.TryGet(craft.outputItemId, out var outputItem);
            var progress = craft.durationSeconds > 0
                ? Mathf.Clamp01(craft.progressSeconds / craft.durationSeconds)
                : 0f;
            return new ActiveHeroRuntimeSnapshot(
                heroNameId,
                heroId,
                outputItem?.NameId,
                craft.craftId,
                progress,
                0,
                0,
                Mathf.Max(0f, craft.durationSeconds - craft.progressSeconds));
        }

        foreach (var combat in combats)
        {
            if (combat == null || !string.Equals(combat.executionId, executionId, StringComparison.Ordinal))
                continue;

            RuntimeConfigs.Activities.TryGet(combat.activityId, out var config);
            return new ActiveHeroRuntimeSnapshot(
                heroNameId,
                heroId,
                config?.nameId,
                combat.activityId,
                0f,
                combat.enemyIndex,
                combat.enemyCount,
                -1f);
        }

        return new ActiveHeroRuntimeSnapshot(
            heroNameId,
            heroId,
            string.Empty,
            executionId,
            0f,
            0,
            0,
            -1f);
    }
}

public sealed class ActiveHeroesRuntimeSnapshot
{
    public static readonly ActiveHeroesRuntimeSnapshot Empty = new ActiveHeroesRuntimeSnapshot(
        0,
        0,
        Array.Empty<ActiveHeroRuntimeSnapshot>());

    public ActiveHeroesRuntimeSnapshot(int currentCount, int limit, IReadOnlyList<ActiveHeroRuntimeSnapshot> heroes)
    {
        CurrentCount = currentCount;
        Limit = limit;
        Heroes = heroes ?? Array.Empty<ActiveHeroRuntimeSnapshot>();
    }

    public int CurrentCount { get; }
    public int Limit { get; }
    public IReadOnlyList<ActiveHeroRuntimeSnapshot> Heroes { get; }
}

public sealed class ActiveHeroRuntimeSnapshot
{
    public ActiveHeroRuntimeSnapshot(
        string heroNameId,
        string heroFallbackName,
        string workNameId,
        string workFallbackName,
        float progress,
        int currentCycle,
        int totalCycles,
        float remainingSeconds)
    {
        HeroNameId = heroNameId ?? string.Empty;
        HeroFallbackName = heroFallbackName ?? string.Empty;
        WorkNameId = workNameId ?? string.Empty;
        WorkFallbackName = workFallbackName ?? string.Empty;
        Progress = progress;
        CurrentCycle = currentCycle;
        TotalCycles = totalCycles;
        RemainingSeconds = remainingSeconds;
    }

    public string HeroNameId { get; }
    public string HeroFallbackName { get; }
    public string WorkNameId { get; }
    public string WorkFallbackName { get; }
    public float Progress { get; }
    public int CurrentCycle { get; }
    public int TotalCycles { get; }
    public float RemainingSeconds { get; }
}
