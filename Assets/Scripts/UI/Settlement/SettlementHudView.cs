using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GuildIdle.Activities;
using GuildIdle.Player;
using GuildIdle.Progression;
using UnityEngine;
using RuntimeConfigs = GuildIdle.Configs.Configs;
using RuntimePlayer = GuildIdle.Player.Player;

public static class SettlementHudUiEvents
{
    public static event Action RefreshRequested;

    public static void RequestRefresh()
    {
        RefreshRequested?.Invoke();
    }
}

public class SettlementHudView : MonoBehaviour
{
    private const float RefreshIntervalSeconds = 0.5f;

    [SerializeField] private CurrencyPanelView _currencyPanelView;
    [SerializeField] private QuestPanelView _questPanelView;
    [SerializeField] private ActiveHeroesPanelView _activeHeroesPanelView;
    [SerializeField] private BottomNavigationPanelView _bottomNavigationPanelView;

    private SettlementHudPresenter _presenter;
    private float _nextRefreshTime;

    private void Awake()
    {
        _presenter = new SettlementHudPresenter(this);
    }

    private void OnEnable()
    {
        SettlementHudUiEvents.RefreshRequested += HandleRefreshRequested;
        _nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
        RefreshNow();
    }

    private void OnDisable()
    {
        SettlementHudUiEvents.RefreshRequested -= HandleRefreshRequested;
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextRefreshTime)
            return;

        _nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
        RefreshNow();
    }

    public void RefreshNow()
    {
        _presenter ??= new SettlementHudPresenter(this);
        _presenter.Refresh(true);
    }

    private void HandleRefreshRequested()
    {
        RefreshNow();
    }

    public void Render(SettlementHudState state)
    {
        state ??= SettlementHudState.Empty;
        _currencyPanelView?.Render(state.Currencies);
        _questPanelView?.Render(state.Stage, state.Quests);
        _activeHeroesPanelView?.Render(state.ActiveHeroes);
    }
}

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

public interface ISettlementHudRuntimeSource
{
    bool IsReady { get; }
    long MainCurrencyAmount { get; }
    QuestRuntimeSnapshot GetQuestSnapshot();
    StageProgressionSnapshot GetStageSnapshot();
    ActiveHeroesRuntimeSnapshot GetActiveHeroesSnapshot();
    string Localise(string id);
}

public sealed class SettlementHudPresenter
{
    private readonly SettlementHudView _view;
    private readonly ISettlementHudRuntimeSource _runtimeSource;
    private SettlementHudState _lastState;

    public SettlementHudPresenter(SettlementHudView view)
        : this(view, new RuntimeSettlementHudSource())
    {
    }

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
                BuildQuestDescription(quest)));
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

    private string BuildQuestDescription(QuestInstanceSnapshot quest)
    {
        var result = new StringBuilder();
        var description = _runtimeSource.Localise(quest.DescriptionId);
        if (!string.IsNullOrWhiteSpace(description))
            result.Append(description);

        foreach (var step in quest.Steps ?? Array.Empty<QuestStepSnapshot>())
        {
            if (step == null)
                continue;

            if (result.Length > 0)
                result.AppendLine();

            result.Append(step.Completed ? "✓ " : "○ ");
            result.Append(_runtimeSource.Localise(step.DescriptionId));
            result.Append(' ');
            result.Append(step.CurrentValue);
            result.Append('/');
            result.Append(step.TargetValue);
        }

        return result.ToString();
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
                !string.Equals(leftQuest.Description, rightQuest.Description, StringComparison.Ordinal))
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

    private sealed class RuntimeSettlementHudSource : ISettlementHudRuntimeSource
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

            var activityExecutions = OnlineActivityRuntime.GetPresentationSnapshot()?.executions ?? Array.Empty<ActivityExecutionSnapshot>();
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
}
