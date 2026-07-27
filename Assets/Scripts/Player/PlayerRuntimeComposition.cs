using System;
using GuildIdle.Activities;
using GuildIdle.Combat;
using GuildIdle.Core;
using GuildIdle.Crafting;
using GuildIdle.Progression;
using RuntimeConfigs = GuildIdle.Configs.Configs;

namespace GuildIdle.Player
{
    public static class PlayerRuntimeComposition
    {
        private static readonly PlayerBootstrapDefinition BootstrapDefinition = new PlayerBootstrapDefinition("stage_arrival");
        private static PlayerStateFactory _playerStateFactory;

        public static event Action<CraftStartedEvent> CraftStarted;
        public static event Action<CraftResultPendingEvent> CraftResultPending;
        public static event Action<CombatStartedEvent> CombatStarted;

        public static ActivityRuntimeService CreateRuntimeService()
        {
            var state = Player.State;
            if (state == null)
                throw new System.InvalidOperationException("Player state is not loaded yet. Call Player.Load() or wait for config load.");

            return new ActivityRuntimeService(
                state,
                new PlayerStateActivityAdapter(state),
                eventSink: HandleActivityRuntimeEvent,
                progressionProcessor: CreateActivityProgressionProcessor(state));
        }

        public static ActivityRuntimeService CreateRuntimeService(PlayerState state)
        {
            return new ActivityRuntimeService(
                state ?? throw new System.ArgumentNullException(nameof(state)),
                new PlayerStateActivityAdapter(state),
                progressionProcessor: CreateActivityProgressionProcessor(state));
        }

        public static IStorageService CreateStorageService()
        {
            var state = Player.State;
            if (state == null)
                throw new InvalidOperationException("Player state is not loaded yet. Call Player.Load() or wait for config load.");
            return state.Storage;
        }

        public static CraftRuntimeService CreateCraftRuntimeService()
        {
            var state = Player.State;
            if (state == null)
                throw new InvalidOperationException("Player state is not loaded yet. Call Player.Load() or wait for config load.");
            return CreateCraftRuntimeService(state);
        }

        public static CraftRuntimeService CreateCraftRuntimeService(PlayerState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            return new CraftRuntimeService(
                RuntimeConfigs.Crafts,
                new PlayerStateCraftAdapter(state),
                HandleCraftStartedEvent,
                HandleCraftResultPendingEvent);
        }

        public static CombatStartService CreateCombatStartService()
        {
            var state = Player.State;
            if (state == null)
                throw new InvalidOperationException(
                    "Player state is not loaded yet. Call Player.Load() or wait for config load.");
            return CreateCombatStartService(state);
        }

        public static CombatStartService CreateCombatStartService(PlayerState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            return new CombatStartService(
                new PlayerStateCombatStartAdapter(
                    state,
                    RuntimeConfigs.Formulas,
                    RuntimeConfigs.Items),
                new ConfigCombatStartActivityDescriptorProvider(RuntimeConfigs.Activities),
                RuntimeConfigs.CombatConsumables,
                new ConfigCombatEnemyQueueProvider(RuntimeConfigs.Enemies),
                eventSink: HandleCombatStartedEvent,
                completionRewards:
                    new ConfigCombatCompletionRewardProvider(
                        RuntimeConfigs.Activities));
        }

        public static CombatOutcomeService CreateCombatOutcomeService()
        {
            var state = Player.State;
            if (state == null)
                throw new InvalidOperationException(
                    "Player state is not loaded yet. Call Player.Load() or wait for config load.");
            return new CombatOutcomeService(state);
        }

        public static CombatOutcomeService CreateCombatOutcomeService(
            PlayerState state)
        {
            return new CombatOutcomeService(
                state ?? throw new ArgumentNullException(nameof(state)));
        }

        public static CombatRuntimeService CreateCombatRuntimeService(
            ICombatDescriptorProvider descriptors)
        {
            var state = Player.State;
            if (state == null)
                throw new InvalidOperationException(
                    "Player state is not loaded yet. Call Player.Load() or wait for config load.");
            return CreateCombatRuntimeService(state, descriptors);
        }

        public static CombatRuntimeService CreateCombatRuntimeService(
            PlayerState state,
            ICombatDescriptorProvider descriptors)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (descriptors == null)
                throw new ArgumentNullException(nameof(descriptors));
            return new CombatRuntimeService(
                state,
                descriptors,
                enemyQueue:
                    new ConfigCombatEnemyQueueProvider(RuntimeConfigs.Enemies),
                abilities:
                    new ConfigCombatAbilityDescriptorProvider(
                        RuntimeConfigs.Enemies),
                statuses:
                    new ConfigCombatStatusDescriptorProvider(
                        RuntimeConfigs.Enemies),
                deathPrevention:
                    new ConfigCombatDeathPreventionDescriptorProvider(
                        RuntimeConfigs.Heroes),
                consumables: RuntimeConfigs.CombatConsumables,
                enemyRewards:
                    new ConfigCombatEnemyRewardProvider(
                        RuntimeConfigs.Enemies,
                        RuntimeConfigs.Items),
                committer: new CombatOutcomeService(state));
        }

        public static IPendingResultService CreatePendingResultService()
        {
            var state = Player.State;
            if (state == null)
                throw new InvalidOperationException("Player state is not loaded yet. Call Player.Load() or wait for config load.");
            return state.PendingResults;
        }

        public static ProgressionRuntimeService CreateProgressionRuntimeService()
        {
            var state = Player.State;
            if (state == null)
                throw new InvalidOperationException("Player state is not loaded yet. Call Player.Load() or wait for config load.");

            return CreateProgressionRuntimeService(state);
        }

        public static ProgressionRuntimeService CreateProgressionRuntimeService(PlayerState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            var configs = new RepositoryProgressionConfigAdapter(RuntimeConfigs.Quests);
            var store = new PlayerStateProgressionAdapter(state);
            return new ProgressionRuntimeService(
                new QuestRuntimeService(configs, store),
                new StageProgressionService(configs, store),
                store,
                new RepositoryNonBuildTransitionAdapter(RuntimeConfigs.Buildings));
        }

        private static IActivityRuntimeProgressionProcessor CreateActivityProgressionProcessor(PlayerState state)
        {
            var configs = new RepositoryProgressionConfigAdapter(RuntimeConfigs.Quests);
            var store = new PlayerStateProgressionAdapter(state);
            var progression = new ProgressionRuntimeService(
                new QuestRuntimeService(configs, store),
                new StageProgressionService(configs, store),
                store,
                new RepositoryNonBuildTransitionAdapter(RuntimeConfigs.Buildings),
                subscribePendingResults: false);
            return new ActivityProgressionProcessor(progression);
        }

        private sealed class ActivityProgressionProcessor : IActivityRuntimeProgressionProcessor
        {
            private readonly ProgressionRuntimeService _progression;
            public ActivityProgressionProcessor(ProgressionRuntimeService progression) => _progression = progression ?? throw new ArgumentNullException(nameof(progression));

            public ActivityRuntimeProgressionResult ProcessBuildingLevelChanged(string buildingId, int level) =>
                ToResult(_progression.ApplyWithinOuterTransaction(new BuildingLevelChanged(buildingId, level)));

            public ActivityRuntimeProgressionResult ProcessActivityCompleted(string activityId) =>
                ToResult(_progression.ApplyActivityCompletedWithinOuterTransaction(activityId));

            private static ActivityRuntimeProgressionResult ToResult(ProgressionRuntimeUpdate update)
            {
                if (update?.Issues != null && update.Issues.Count > 0)
                {
                    var issue = update.Issues[0];
                    return new ActivityRuntimeProgressionResult { success = false, code = issue.Code, message = issue.Message };
                }
                return new ActivityRuntimeProgressionResult { success = true, code = "Applied" };
            }
        }

        internal static PlayerBootstrapService CreateBootstrapService(
            Func<bool> isPlayerLoaded,
            Func<bool> loadPlayer,
            Action<string> handleConfigLoadFailed)
        {
            return new PlayerBootstrapService(
                new RuntimeConfigLifecycleAdapter(),
                isPlayerLoaded,
                loadPlayer,
                handleConfigLoadFailed);
        }

        internal static PlayerState LoadPlayerState(out SaveLoadOrigin origin)
        {
            return SaveService.Load(GetPlayerStateFactory(), null, out origin);
        }

        internal static PlayerState ResetPlayerState()
        {
            return SaveService.ResetSave(GetPlayerStateFactory());
        }

        internal static void InvalidatePlayerStateFactory()
        {
            _playerStateFactory = null;
        }

        private static void HandleActivityRuntimeEvent(ActivityRuntimeEvent runtimeEvent)
        {
            if (runtimeEvent == null || Player.Progression == null)
                return;
            if (runtimeEvent.progressionAlreadyProcessed)
                return;
            if (string.Equals(runtimeEvent.eventType, ActivityRuntimeEventType.BuildingLevelChanged, StringComparison.Ordinal))
                Player.Progression.Handle(new BuildingLevelChanged(runtimeEvent.targetId, runtimeEvent.value));
            else if (string.Equals(runtimeEvent.eventType, ActivityRuntimeEventType.ActivityCompleted, StringComparison.Ordinal))
                Player.Progression.HandleActivityCompleted(runtimeEvent.targetId);
        }

        private static void HandleCraftStartedEvent(CraftStartedEvent craftStartedEvent)
        {
            CraftStarted?.Invoke(craftStartedEvent);
        }

        private static void HandleCraftResultPendingEvent(CraftResultPendingEvent craftResultPendingEvent)
        {
            CraftResultPending?.Invoke(craftResultPendingEvent);
        }

        private static void HandleCombatStartedEvent(CombatStartedEvent combatStartedEvent)
        {
            CombatStarted?.Invoke(combatStartedEvent);
        }

        private static PlayerStateFactory GetPlayerStateFactory()
        {
            if (_playerStateFactory != null)
                return _playerStateFactory;

            var heroStatsConfigs = new RepositoryHeroStatsConfigAdapter(
                RuntimeConfigs.Heroes,
                RuntimeConfigs.Formulas,
                RuntimeConfigs.Activities);
            var bootstrapConfigs = new RepositoryPlayerBootstrapConfigAdapter(
                RuntimeConfigs.Items,
                RuntimeConfigs.Heroes,
                RuntimeConfigs.Activities,
                RuntimeConfigs.Buildings,
                RuntimeConfigs.Quests,
                RuntimeConfigs.Storage);
            var heroStats = new HeroStatsService(heroStatsConfigs);
            _playerStateFactory = new PlayerStateFactory(
                bootstrapConfigs,
                heroStats,
                BootstrapDefinition);
            return _playerStateFactory;
        }

        private sealed class RuntimeConfigLifecycleAdapter : IRuntimeConfigLifecycle
        {
            public bool IsLoaded => RuntimeConfigs.IsLoaded;

            public event Action Loaded
            {
                add => RuntimeConfigs.OnLoaded += value;
                remove => RuntimeConfigs.OnLoaded -= value;
            }

            public event Action<string> LoadFailed
            {
                add => RuntimeConfigs.OnLoadFailed += value;
                remove => RuntimeConfigs.OnLoadFailed -= value;
            }
        }
    }
}
